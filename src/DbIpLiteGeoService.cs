// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    public enum DbIpLiteUpdateStatus
    {
        Updated,
        UpToDate,
        NotDue,
        ConsentRequired,
        AlreadyRunning,
        Failed
    }

    public sealed class DbIpLiteUpdateResult
    {
        public DbIpLiteUpdateStatus Status { get; internal set; }
        public string ReleaseMonth { get; internal set; }
        public string ErrorMessage { get; internal set; }
        public bool Success
        {
            get
            {
                return Status == DbIpLiteUpdateStatus.Updated
                    || Status == DbIpLiteUpdateStatus.UpToDate
                    || Status == DbIpLiteUpdateStatus.NotDue;
            }
        }
    }

    /// <summary>
    /// Local-only DB-IP City Lite lookup plus an explicitly authorized monthly updater.
    /// Lookup never makes a network request. Calling the update method with false never downloads.
    /// </summary>
    public sealed class DbIpLiteGeoService : IDisposable
    {
        public const string AttributionText = "IP geolocation by DB-IP.com (CC BY 4.0)";
        public const string AttributionUrl = "https://db-ip.com/";
        public const string LicenseUrl = "https://creativecommons.org/licenses/by/4.0/";
        public const long MaximumCompressedBytes = 256L * 1024 * 1024;
        public const long MaximumDatabaseBytes = 512L * 1024 * 1024;

        private const int StateMaximumBytes = 32 * 1024;
        private static readonly TimeSpan FailedAttemptThrottle = TimeSpan.FromHours(24);
        private static readonly TimeSpan MissingDatabaseFailedAttemptThrottle = TimeSpan.FromHours(1);

        private readonly object _sync = new object();
        private readonly string _storeDirectory;
        private readonly string _databasePath;
        private readonly string _backupPath;
        private readonly string _statePath;
        private readonly string _lockPath;
        private readonly IDbIpLiteDownloadClient _downloadClient;
        private readonly Func<DateTime> _utcNowProvider;
        private readonly SemaphoreSlim _updateGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _disposeCancellation = new CancellationTokenSource();
        private DbIpLiteMmdbReader _reader;
        private string _readerPath;
        private long _readerLength;
        private DateTime _readerWriteUtc;
        private bool _primaryRejected;
        private long _rejectedPrimaryLength;
        private DateTime _rejectedPrimaryWriteUtc;
        private bool _disposed;

        public DbIpLiteGeoService()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard",
                "DbIpLite"), new DbIpLiteHttpDownloadClient())
        {
        }

        internal DbIpLiteGeoService(string storeDirectory, IDbIpLiteDownloadClient downloadClient)
            : this(storeDirectory, downloadClient, delegate { return DateTime.UtcNow; })
        {
        }

        internal DbIpLiteGeoService(
            string storeDirectory,
            IDbIpLiteDownloadClient downloadClient,
            Func<DateTime> utcNowProvider)
        {
            if (string.IsNullOrWhiteSpace(storeDirectory))
                throw new ArgumentException("Store directory is empty.", "storeDirectory");
            if (downloadClient == null) throw new ArgumentNullException("downloadClient");
            if (utcNowProvider == null) throw new ArgumentNullException("utcNowProvider");

            _storeDirectory = Path.GetFullPath(storeDirectory);
            _databasePath = Path.Combine(_storeDirectory, "dbip-city-lite.mmdb");
            _backupPath = _databasePath + ".bak";
            _statePath = Path.Combine(_storeDirectory, "update-state.json");
            _lockPath = Path.Combine(_storeDirectory, "update.lock");
            _downloadClient = downloadClient;
            _utcNowProvider = utcNowProvider;
            TryCleanupOwnedTemporaryFilesAtStartup();
        }

        public string DatabasePath { get { return _databasePath; } }

        public bool HasUsableDatabase
        {
            get
            {
                lock (_sync)
                {
                    ThrowIfDisposed();
                    return TryGetReader() != null;
                }
            }
        }

        public Task<GeoInfo> LookupAsync(string ipAddress)
        {
            return Task.Run(delegate { return Lookup(ipAddress); });
        }

        public GeoInfo Lookup(string ipAddress)
        {
            IPAddress address;
            if (!IPAddress.TryParse(ipAddress, out address) || !IsPublicAddress(address))
                return Failure("공인 서버 IP가 아닙니다.");

            lock (_sync)
            {
                ThrowIfDisposed();
                try
                {
                    DbIpLiteMmdbReader reader = TryGetReader();
                    if (reader == null) return Failure("로컬 지역 DB가 없습니다.");
                    IDictionary<string, object> record = reader.Lookup(address);
                    if (record == null) return Failure("지역 정보가 없습니다.");
                    return ConvertRecord(record);
                }
                catch
                {
                    bool primaryFailed = string.Equals(
                        _readerPath, _databasePath, StringComparison.OrdinalIgnoreCase);
                    if (primaryFailed) MarkPrimaryRejected();
                    ResetReader();
                    if (primaryFailed)
                    {
                        try
                        {
                            DbIpLiteMmdbReader backupReader = TryGetReader();
                            if (backupReader != null)
                            {
                                IDictionary<string, object> backupRecord = backupReader.Lookup(address);
                                if (backupRecord != null) return ConvertRecord(backupRecord);
                            }
                        }
                        catch { ResetReader(); }
                    }
                    return Failure("지역 DB를 읽을 수 없습니다.");
                }
            }
        }

        public async Task<DbIpLiteUpdateResult> UpdateInBackgroundIfDueAsync(
            bool userAcceptedLicenseAndNetworkDownload,
            CancellationToken cancellationToken)
        {
            if (!userAcceptedLicenseAndNetworkDownload)
                return Result(DbIpLiteUpdateStatus.ConsentRequired, null,
                    "DB-IP Lite 라이선스와 약 60~70MB 다운로드에 대한 동의가 필요합니다.");

            if (!_updateGate.Wait(0))
                return Result(DbIpLiteUpdateStatus.AlreadyRunning, null, "이미 갱신 중입니다.");
            CancellationTokenSource linkedCancellation = null;
            try
            {
                lock (_sync) ThrowIfDisposed();
                linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _disposeCancellation.Token);
                cancellationToken = linkedCancellation.Token;
            try { Directory.CreateDirectory(_storeDirectory); }
            catch
            {
                return Result(DbIpLiteUpdateStatus.Failed, null,
                    "지역 DB 저장 폴더를 만들 수 없습니다.");
            }

            FileStream updateLock;
            try
            {
                updateLock = new FileStream(
                    _lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception exception)
            {
                if (!(exception is IOException) && !(exception is UnauthorizedAccessException))
                    return Result(DbIpLiteUpdateStatus.Failed, null, "지역 DB 갱신을 시작할 수 없습니다.");
                return Result(DbIpLiteUpdateStatus.AlreadyRunning, null, "이미 갱신 중입니다.");
            }

            using (updateLock)
            {
                CleanupOwnedTemporaryFiles();
                DateTime nowUtc = GetUtcNow();
                UpdateState state = LoadState();
                UpdateState stateBeforeAttempt = CloneState(state);
                string currentMonth = nowUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                bool hasUsableDatabase;
                bool primaryRepairRequired;
                lock (_sync)
                {
                    hasUsableDatabase = TryGetReader() != null;
                    primaryRepairRequired = hasUsableDatabase
                        && (_primaryRejected || string.Equals(
                            _readerPath, _backupPath, StringComparison.OrdinalIgnoreCase));
                }
                if (state != null
                    && string.Equals(state.ReleaseMonth, currentMonth, StringComparison.Ordinal)
                    && hasUsableDatabase
                    && !primaryRepairRequired)
                    return Result(DbIpLiteUpdateStatus.UpToDate, currentMonth, null);

                TimeSpan failedAttemptThrottle = hasUsableDatabase
                    ? FailedAttemptThrottle : MissingDatabaseFailedAttemptThrottle;
                bool failedRepairAttemptPending = primaryRepairRequired
                    && HasFailedAttemptAfterLastSuccess(state);
                if ((!primaryRepairRequired || failedRepairAttemptPending)
                    && IsWithinFailedAttemptThrottle(nowUtc, state, failedAttemptThrottle))
                    return hasUsableDatabase
                        ? Result(DbIpLiteUpdateStatus.NotDue, state.ReleaseMonth, null)
                        : Result(DbIpLiteUpdateStatus.Failed, state.ReleaseMonth,
                            "최근 지역 DB 갱신이 실패해 1시간 뒤 다시 시도합니다.");

                if (state == null) state = new UpdateState();
                state.LastAttemptUtc = nowUtc;
                SaveState(state);

                try
                {
                    string lastError = null;
                    IList<string> months = hasUsableDatabase
                        ? (IList<string>)new List<string> { currentMonth }
                        : GetCandidateReleaseMonths(nowUtc);
                    for (int index = 0; index < months.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string month = months[index];
                        string compressedTemporaryPath = Path.Combine(
                            _storeDirectory, "download-" + Guid.NewGuid().ToString("N") + ".mmdb.gz");
                        string databaseTemporaryPath = Path.Combine(
                            _storeDirectory, "database-" + Guid.NewGuid().ToString("N") + ".mmdb");
                        try
                        {
                            Uri uri = BuildDownloadUri(month);
                            using (var destination = new FileStream(
                                compressedTemporaryPath, FileMode.CreateNew, FileAccess.Write,
                                FileShare.None, 64 * 1024, FileOptions.SequentialScan))
                            {
                                await _downloadClient.DownloadAsync(
                                    uri, destination, MaximumCompressedBytes, cancellationToken)
                                    .ConfigureAwait(false);
                                destination.Flush(true);
                            }

                            DecompressAndValidateSize(
                                compressedTemporaryPath, databaseTemporaryPath, cancellationToken);
                            string databaseType;
                            DateTime buildUtc;
                            using (var candidate = new DbIpLiteMmdbReader(databaseTemporaryPath))
                            {
                                databaseType = candidate.DatabaseType;
                                buildUtc = candidate.BuildUtc;
                                if (databaseType.IndexOf("City", StringComparison.OrdinalIgnoreCase) < 0)
                                    throw new InvalidDataException("Downloaded MMDB is not a city database.");
                            }

                            string sha256 = ComputeSha256(databaseTemporaryPath);
                            lock (_sync)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                // Lookup may discover lazy record corruption while the download is
                                // running. Decide whether the old primary is backup-worthy and replace
                                // it under the same lock used by MarkPrimaryRejected.
                                CommitDatabase(databaseTemporaryPath);
                                databaseTemporaryPath = null;
                                ResetReader();
                                ClearPrimaryRejection();
                            }
                            state.ReleaseMonth = month;
                            state.LastSuccessUtc = GetUtcNow();
                            state.LastAttemptUtc = state.LastSuccessUtc;
                            state.Sha256 = sha256;
                            state.DatabaseType = databaseType;
                            state.BuildUtc = buildUtc == default(DateTime) ? (DateTime?)null : buildUtc;
                            SaveState(state);
                            return Result(DbIpLiteUpdateStatus.Updated, month, null);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (DbIpLiteDownloadNotFoundException exception)
                        {
                            lastError = SafeError(exception);
                        }
                        catch (Exception exception)
                        {
                            return Result(DbIpLiteUpdateStatus.Failed, month, SafeError(exception));
                        }
                        finally
                        {
                            DeleteTemporaryFile(compressedTemporaryPath);
                            if (databaseTemporaryPath != null) DeleteTemporaryFile(databaseTemporaryPath);
                        }
                    }

                    return Result(DbIpLiteUpdateStatus.Failed, null,
                        string.IsNullOrWhiteSpace(lastError) ? "지역 DB 갱신에 실패했습니다." : lastError);
                }
                catch (OperationCanceledException exception)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        RestoreStateAfterCanceledAttempt(stateBeforeAttempt);
                        throw;
                    }

                    // HttpClient represents its own timeout as TaskCanceledException even when
                    // neither the caller nor app shutdown requested cancellation. That remains a
                    // failed attempt and must retain the normal retry throttle.
                    return Result(DbIpLiteUpdateStatus.Failed, null, SafeError(exception));
                }
            }
            }
            finally
            {
                if (linkedCancellation != null) linkedCancellation.Dispose();
                _updateGate.Release();
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _disposeCancellation.Cancel();
            }

            _updateGate.Wait();
            try
            {
                lock (_sync)
                {
                    ResetReader();
                    IDisposable disposable = _downloadClient as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
            }
            finally
            {
                _updateGate.Release();
                _disposeCancellation.Dispose();
            }
        }

        internal static Uri BuildDownloadUri(string releaseMonth)
        {
            DateTime parsed;
            if (string.IsNullOrWhiteSpace(releaseMonth)
                || !DateTime.TryParseExact(releaseMonth, "yyyy-MM", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
                throw new ArgumentException("Release month must be yyyy-MM.", "releaseMonth");
            return new Uri("https://download.db-ip.com/free/dbip-city-lite-"
                + releaseMonth + ".mmdb.gz", UriKind.Absolute);
        }

        internal static IList<string> GetCandidateReleaseMonths(DateTime utcNow)
        {
            var months = new List<string>(3);
            DateTime month = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            for (int index = 0; index < 3; index++)
                months.Add(month.AddMonths(-index).ToString("yyyy-MM", CultureInfo.InvariantCulture));
            return months;
        }

        private DateTime GetUtcNow()
        {
            DateTime value = _utcNowProvider();
            return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        }

        private static bool IsWithinFailedAttemptThrottle(
            DateTime nowUtc,
            UpdateState state,
            TimeSpan throttle)
        {
            if (state == null || !state.LastAttemptUtc.HasValue) return false;
            TimeSpan elapsed = nowUtc - state.LastAttemptUtc.Value;
            // A future timestamp means the system clock was corrected after the state was saved.
            // Never let that advisory timestamp suppress updates until the future date arrives.
            return elapsed >= TimeSpan.Zero && elapsed < throttle;
        }

        private static bool HasFailedAttemptAfterLastSuccess(UpdateState state)
        {
            if (state == null || !state.LastAttemptUtc.HasValue) return false;
            return !state.LastSuccessUtc.HasValue
                || state.LastAttemptUtc.Value > state.LastSuccessUtc.Value;
        }

        private static UpdateState CloneState(UpdateState state)
        {
            if (state == null) return null;
            return new UpdateState
            {
                ReleaseMonth = state.ReleaseMonth,
                LastAttemptUtc = state.LastAttemptUtc,
                LastSuccessUtc = state.LastSuccessUtc,
                BuildUtc = state.BuildUtc,
                Sha256 = state.Sha256,
                DatabaseType = state.DatabaseType
            };
        }

        private void RestoreStateAfterCanceledAttempt(UpdateState previousState)
        {
            if (previousState != null)
            {
                SaveState(previousState);
                return;
            }

            // The attempt created the advisory state file. Removing it makes the next explicit
            // request immediately eligible instead of treating cancellation as a failed download.
            DeleteTemporaryFile(_statePath);
        }

        internal static bool IsPublicAddress(IPAddress address)
        {
            if (address == null) return false;
            if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                int first = bytes[0];
                int second = bytes[1];
                if (first == 0 || first == 10 || first == 127 || first >= 224) return false;
                if (first == 100 && second >= 64 && second <= 127) return false;
                if (first == 169 && second == 254) return false;
                if (first == 172 && second >= 16 && second <= 31) return false;
                if (first == 192 && second == 168) return false;
                if (first == 192 && second == 0 && bytes[2] == 0) return false;
                if (first == 192 && second == 0 && bytes[2] == 2) return false;
                if (first == 198 && (second == 18 || second == 19)) return false;
                if (first == 198 && second == 51 && bytes[2] == 100) return false;
                if (first == 203 && second == 0 && bytes[2] == 113) return false;
                return true;
            }

            if (bytes.Length != 16 || IPAddress.IPv6None.Equals(address)
                || IPAddress.IPv6Loopback.Equals(address) || address.IsIPv6Multicast
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
                return false;
            if ((bytes[0] & 0xFE) == 0xFC) return false;
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
                return false;
            return true;
        }

        private DbIpLiteMmdbReader TryGetReader()
        {
            string path = null;
            if (File.Exists(_databasePath))
            {
                var primaryInfo = new FileInfo(_databasePath);
                if (_primaryRejected
                    && (primaryInfo.Length != _rejectedPrimaryLength
                        || primaryInfo.LastWriteTimeUtc != _rejectedPrimaryWriteUtc))
                    ClearPrimaryRejection();
                if (!_primaryRejected) path = _databasePath;
            }
            if (path == null && File.Exists(_backupPath)) path = _backupPath;
            if (path == null)
            {
                ResetReader();
                return null;
            }

            var info = new FileInfo(path);
            if (_reader != null && string.Equals(path, _readerPath, StringComparison.OrdinalIgnoreCase)
                && info.Length == _readerLength && info.LastWriteTimeUtc == _readerWriteUtc)
                return _reader;

            ResetReader();
            try
            {
                _reader = new DbIpLiteMmdbReader(path);
                if (_reader.DatabaseType.IndexOf("City", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidDataException("MMDB is not a city database.");
                _readerPath = path;
                _readerLength = info.Length;
                _readerWriteUtc = info.LastWriteTimeUtc;
                return _reader;
            }
            catch
            {
                if (string.Equals(path, _databasePath, StringComparison.OrdinalIgnoreCase))
                    MarkPrimaryRejected();
                ResetReader();
                if (!string.Equals(path, _backupPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(_backupPath))
                {
                    try
                    {
                        info = new FileInfo(_backupPath);
                        _reader = new DbIpLiteMmdbReader(_backupPath);
                        if (_reader.DatabaseType.IndexOf("City", StringComparison.OrdinalIgnoreCase) < 0)
                            throw new InvalidDataException("Backup MMDB is not a city database.");
                        _readerPath = _backupPath;
                        _readerLength = info.Length;
                        _readerWriteUtc = info.LastWriteTimeUtc;
                        return _reader;
                    }
                    catch { ResetReader(); }
                }
                return null;
            }
        }

        private void ResetReader()
        {
            if (_reader != null) _reader.Dispose();
            _reader = null;
            _readerPath = null;
            _readerLength = 0;
            _readerWriteUtc = default(DateTime);
        }

        private void MarkPrimaryRejected()
        {
            try
            {
                var info = new FileInfo(_databasePath);
                _primaryRejected = info.Exists;
                _rejectedPrimaryLength = info.Exists ? info.Length : 0;
                _rejectedPrimaryWriteUtc = info.Exists ? info.LastWriteTimeUtc : default(DateTime);
            }
            catch { _primaryRejected = true; }
        }

        private void ClearPrimaryRejection()
        {
            _primaryRejected = false;
            _rejectedPrimaryLength = 0;
            _rejectedPrimaryWriteUtc = default(DateTime);
        }

        private static GeoInfo ConvertRecord(IDictionary<string, object> record)
        {
            IDictionary<string, object> country = GetMap(record, "country");
            IDictionary<string, object> city = GetMap(record, "city");
            IDictionary<string, object> region = null;
            object subdivisionsValue;
            if (record.TryGetValue("subdivisions", out subdivisionsValue))
            {
                IList<object> subdivisions = subdivisionsValue as IList<object>;
                if (subdivisions != null && subdivisions.Count > 0)
                    region = subdivisions[0] as IDictionary<string, object>;
            }

            string countryCode = GetText(country, "iso_code");
            string countryName = GetLocalizedName(country);
            string regionName = GetLocalizedName(region);
            string cityName = GetLocalizedName(city);
            if (string.IsNullOrWhiteSpace(countryCode) && string.IsNullOrWhiteSpace(countryName)
                && string.IsNullOrWhiteSpace(regionName) && string.IsNullOrWhiteSpace(cityName))
                return Failure("지역 정보가 없습니다.");

            return new GeoInfo
            {
                Success = true,
                CountryCode = countryCode,
                Country = countryName,
                Region = regionName,
                City = cityName
            };
        }

        private static IDictionary<string, object> GetMap(IDictionary<string, object> map, string key)
        {
            if (map == null) return null;
            object value;
            return map.TryGetValue(key, out value) ? value as IDictionary<string, object> : null;
        }

        private static string GetLocalizedName(IDictionary<string, object> map)
        {
            IDictionary<string, object> names = GetMap(map, "names");
            string value = GetText(names, "ko");
            if (string.IsNullOrWhiteSpace(value)) value = GetText(names, "en");
            return value;
        }

        private static string GetText(IDictionary<string, object> map, string key)
        {
            if (map == null) return null;
            object value;
            string text = map.TryGetValue(key, out value) ? value as string : null;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static GeoInfo Failure(string message)
        {
            return new GeoInfo { Success = false, ErrorMessage = message };
        }

        private static DbIpLiteUpdateResult Result(
            DbIpLiteUpdateStatus status, string releaseMonth, string errorMessage)
        {
            return new DbIpLiteUpdateResult
            {
                Status = status,
                ReleaseMonth = releaseMonth,
                ErrorMessage = errorMessage
            };
        }

        private void DecompressAndValidateSize(
            string sourcePath, string destinationPath, CancellationToken cancellationToken)
        {
            long written = 0;
            byte[] buffer = new byte[128 * 1024];
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var gzip = new GZipStream(source, CompressionMode.Decompress, false))
            using (var destination = new FileStream(
                destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.SequentialScan))
            {
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    written = checked(written + read);
                    if (written > MaximumDatabaseBytes)
                        throw new InvalidDataException("Decompressed MMDB exceeds the size limit.");
                    destination.Write(buffer, 0, read);
                }
                destination.Flush(true);
            }
            if (written < 1024)
                throw new InvalidDataException("Downloaded MMDB is unexpectedly small.");
        }

        private void CommitDatabase(string temporaryPath)
        {
            if (File.Exists(_databasePath))
            {
                // ReplaceFile creates the old primary as the backup atomically, without a
                // second full-size temporary copy. Never overwrite a good backup with a
                // primary that only passed file existence checks but is not a usable MMDB.
                if (!_primaryRejected && IsUsableCityDatabase(_databasePath))
                    File.Replace(temporaryPath, _databasePath, _backupPath, true);
                else
                    File.Replace(temporaryPath, _databasePath, null, true);
            }
            else
            {
                File.Move(temporaryPath, _databasePath);
            }
        }

        private static bool IsUsableCityDatabase(string path)
        {
            try
            {
                using (var reader = new DbIpLiteMmdbReader(path))
                    return reader.DatabaseType.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private UpdateState LoadState()
        {
            try
            {
                var info = new FileInfo(_statePath);
                if (!info.Exists || info.Length <= 0 || info.Length > StateMaximumBytes) return null;
                string json;
                using (var reader = new StreamReader(_statePath, Encoding.UTF8, true))
                    json = reader.ReadToEnd();
                var serializer = new JavaScriptSerializer { MaxJsonLength = StateMaximumBytes };
                UpdateStateDocument document = serializer.Deserialize<UpdateStateDocument>(json);
                if (document == null || document.Version != 1) return null;
                return new UpdateState
                {
                    ReleaseMonth = document.ReleaseMonth,
                    LastAttemptUtc = ParseUtc(document.LastAttemptUtc),
                    LastSuccessUtc = ParseUtc(document.LastSuccessUtc),
                    BuildUtc = ParseUtc(document.BuildUtc),
                    Sha256 = document.Sha256,
                    DatabaseType = document.DatabaseType
                };
            }
            catch { return null; }
        }

        private void SaveState(UpdateState state)
        {
            try
            {
                Directory.CreateDirectory(_storeDirectory);
                var document = new UpdateStateDocument
                {
                    Version = 1,
                    ReleaseMonth = state.ReleaseMonth,
                    LastAttemptUtc = FormatUtc(state.LastAttemptUtc),
                    LastSuccessUtc = FormatUtc(state.LastSuccessUtc),
                    BuildUtc = FormatUtc(state.BuildUtc),
                    Sha256 = state.Sha256,
                    DatabaseType = state.DatabaseType
                };
                var serializer = new JavaScriptSerializer { MaxJsonLength = StateMaximumBytes };
                byte[] bytes = new UTF8Encoding(false).GetBytes(serializer.Serialize(document));
                if (bytes.Length > StateMaximumBytes) return;
                string temporaryPath = _statePath + ".tmp." + Guid.NewGuid().ToString("N");
                try
                {
                    using (var stream = new FileStream(
                        temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(_statePath)) File.Replace(temporaryPath, _statePath, null, true);
                    else File.Move(temporaryPath, _statePath);
                }
                finally { DeleteTemporaryFile(temporaryPath); }
            }
            catch
            {
                // State is advisory. A state write failure must not break local lookup or the app.
            }
        }

        private static DateTime? ParseUtc(string value)
        {
            DateTime parsed;
            return DateTime.TryParseExact(value, "o", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)
                ? (DateTime?)parsed.ToUniversalTime() : null;
        }

        private static string FormatUtc(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : null;
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                    builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static string SafeError(Exception exception)
        {
            if (exception is HttpRequestException) return "DB-IP 다운로드에 실패했습니다.";
            if (exception is InvalidDataException) return "다운로드한 지역 DB가 올바르지 않습니다.";
            return "지역 DB 갱신에 실패했습니다.";
        }

        private static void DeleteTemporaryFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private void TryCleanupOwnedTemporaryFilesAtStartup()
        {
            if (!Directory.Exists(_storeDirectory)) return;
            try
            {
                using (var cleanupLock = new FileStream(
                    _lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    CleanupOwnedTemporaryFiles();
            }
            catch
            {
                // Another process may be updating. Never interfere with its temporary files.
            }
        }

        private void CleanupOwnedTemporaryFiles()
        {
            try
            {
                foreach (string path in Directory.GetFiles(_storeDirectory))
                {
                    string name = Path.GetFileName(path);
                    if (IsGuidTemporaryName(name, "download-", ".mmdb.gz")
                        || IsGuidTemporaryName(name, "database-", ".mmdb")
                        || IsGuidTemporaryName(name, "update-state.json.tmp.", string.Empty)
                        || IsGuidTemporaryName(name, "dbip-city-lite.mmdb.bak.tmp.", string.Empty))
                        DeleteTemporaryFile(path);
                }
            }
            catch
            {
                // Cleanup is best effort and must not prevent lookup or startup.
            }
        }

        private static bool IsGuidTemporaryName(string name, string prefix, string suffix)
        {
            if (string.IsNullOrEmpty(name)
                || !name.StartsWith(prefix, StringComparison.Ordinal)
                || !name.EndsWith(suffix, StringComparison.Ordinal))
                return false;
            int length = name.Length - prefix.Length - suffix.Length;
            if (length != 32) return false;
            Guid parsed;
            return Guid.TryParseExact(name.Substring(prefix.Length, length), "N", out parsed);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("DbIpLiteGeoService");
        }

        private sealed class UpdateState
        {
            public string ReleaseMonth { get; set; }
            public DateTime? LastAttemptUtc { get; set; }
            public DateTime? LastSuccessUtc { get; set; }
            public DateTime? BuildUtc { get; set; }
            public string Sha256 { get; set; }
            public string DatabaseType { get; set; }
        }

        private sealed class UpdateStateDocument
        {
            public int Version { get; set; }
            public string ReleaseMonth { get; set; }
            public string LastAttemptUtc { get; set; }
            public string LastSuccessUtc { get; set; }
            public string BuildUtc { get; set; }
            public string Sha256 { get; set; }
            public string DatabaseType { get; set; }
        }
    }

    internal interface IDbIpLiteDownloadClient
    {
        Task DownloadAsync(Uri uri, Stream destination, long maximumBytes, CancellationToken cancellationToken);
    }

    internal sealed class DbIpLiteDownloadNotFoundException : HttpRequestException
    {
        public DbIpLiteDownloadNotFoundException(string message) : base(message) { }
    }

    internal sealed class DbIpLiteHttpDownloadClient : IDbIpLiteDownloadClient, IDisposable
    {
        private readonly HttpClient _client;

        public DbIpLiteHttpDownloadClient()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None };
            _client = new HttpClient(handler, true) { Timeout = TimeSpan.FromMinutes(10) };
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("TarkovServerGuard/0.8.3");
        }

        public async Task DownloadAsync(
            Uri uri, Stream destination, long maximumBytes, CancellationToken cancellationToken)
        {
            if (uri == null || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "download.db-ip.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith("/free/dbip-city-lite-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected DB-IP download address.");
            if (destination == null || !destination.CanWrite) throw new ArgumentException("Destination is not writable.");

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            using (HttpResponseMessage response = await _client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new DbIpLiteDownloadNotFoundException("DB-IP release was not found.");
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new HttpRequestException("DB-IP returned HTTP " + (int)response.StatusCode + ".");
                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && (contentLength.Value <= 0 || contentLength.Value > maximumBytes))
                    throw new InvalidDataException("DB-IP download size is outside the allowed range.");

                long total = 0;
                byte[] buffer = new byte[128 * 1024];
                using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                {
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                        .ConfigureAwait(false)) > 0)
                    {
                        total = checked(total + read);
                        if (total > maximumBytes)
                            throw new InvalidDataException("DB-IP download exceeds the size limit.");
                        await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    }
                }
                if (total <= 0) throw new InvalidDataException("DB-IP download is empty.");
            }
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}
