using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    public sealed class UserReportMemoEntry
    {
        public string Nickname { get; set; }
        public string Reason { get; set; }
    }

    public sealed class UserReportMemoRecord
    {
        public string Key { get; set; }
        public int ReportCount { get; set; }
        public List<UserReportMemoEntry> Entries { get; set; }
        // Kept for lossless compatibility with the free-form v0.6.6 memo format.
        public string MemoText { get; set; }
        public string Game { get; set; }
        public DateTime RaidStartedUtc { get; set; }
        public string MapName { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>
    /// Stores the optional user-report memo separately from ordinary raid notes.
    /// Persisted records contain a one-way session digest, the report count,
    /// structured nickname/reason entries, legacy free-form memo text,
    /// archive display metadata (game, raid time, map), and timestamps.
    /// Raw game logs, account identifiers, SIDs, and local paths are never written.
    /// </summary>
    public sealed class UserReportMemoStore
    {
        public const int MaximumMemoLength = 200000;
        public const int MaximumReportCount = 10000;
        public const int MaximumNicknameLength = 256;
        public const int MaximumReasonLength = 2000;
        private const int MaximumJsonLength = 1024 * 1024;

        private readonly object _sync = new object();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly string _memoFolder;

        public UserReportMemoStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard",
                "UserReportMemos"))
        {
        }

        public UserReportMemoStore(string memoFolder)
        {
            if (string.IsNullOrWhiteSpace(memoFolder))
                throw new ArgumentException("유저신고 메모 저장 폴더가 비어 있습니다.", "memoFolder");
            _memoFolder = Path.GetFullPath(memoFolder);
            _serializer.MaxJsonLength = MaximumJsonLength;
            _serializer.RecursionLimit = 10;
        }

        public string MemoFolder
        {
            get { return _memoFolder; }
        }

        public static string CreateStableKey(ServerSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            return CreateDomainKey(RaidNoteStore.CreateStableKey(session));
        }

        public static string CreateStableKey(string opaqueSessionIdentity)
        {
            if (string.IsNullOrWhiteSpace(opaqueSessionIdentity))
                throw new ArgumentException("세션 식별자가 비어 있습니다.", "opaqueSessionIdentity");
            return CreateDomainKey(RaidNoteStore.CreateStableKey(opaqueSessionIdentity));
        }

        public bool Exists(ServerSession session)
        {
            return Exists(CreateStableKey(session));
        }

        public bool Exists(string key)
        {
            ValidateKey(key);
            lock (_sync)
            {
                return File.Exists(GetPath(key)) || File.Exists(GetBackupPath(key));
            }
        }

        public UserReportMemoRecord Load(ServerSession session)
        {
            return Load(CreateStableKey(session));
        }

        public UserReportMemoRecord Load(string key)
        {
            ValidateKey(key);
            lock (_sync)
            {
                string target = GetPath(key);
                UserReportMemoRecord record = TryRead(target, key);
                if (record != null) return record;
                return TryRead(GetBackupPath(key), key);
            }
        }

        public IList<UserReportMemoRecord> LoadAll()
        {
            lock (_sync)
            {
                EnsureFolder();
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string path in Directory.EnumerateFiles(
                        _memoFolder, "*.json*", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(path);
                        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                            name = name.Substring(0, name.Length - 4);
                        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                        string key = name.Substring(0, name.Length - 5);
                        try { ValidateKey(key); keys.Add(key); }
                        catch (ArgumentException) { }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    return new List<UserReportMemoRecord>();
                }

                var records = new List<UserReportMemoRecord>();
                foreach (string key in keys)
                {
                    UserReportMemoRecord record = TryRead(GetPath(key), key)
                        ?? TryRead(GetBackupPath(key), key);
                    if (record != null) records.Add(record);
                }
                return records
                    .OrderByDescending(item => item.RaidStartedUtc)
                    .ThenByDescending(item => item.UpdatedUtc)
                    .ToList();
            }
        }

        public UserReportMemoRecord CreateFor(ServerSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            DateTime now = DateTime.UtcNow;
            return new UserReportMemoRecord
            {
                Key = CreateStableKey(session),
                ReportCount = NormalizeReportCount(session.UserReportCount),
                Entries = new List<UserReportMemoEntry>(),
                MemoText = string.Empty,
                Game = LimitText(session.GameDisplayName, 32),
                RaidStartedUtc = NormalizeDate(session.SessionStarted),
                MapName = LimitText(session.MapName, 256),
                CreatedUtc = now,
                UpdatedUtc = now
            };
        }

        public void Save(ServerSession session, UserReportMemoRecord record)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (record == null) throw new ArgumentNullException("record");
            string key = CreateStableKey(session);
            SaveCore(key, record, NormalizeReportCount(session.UserReportCount), session);
        }

        public void Save(string key, UserReportMemoRecord record)
        {
            if (record == null) throw new ArgumentNullException("record");
            SaveCore(key, record, NormalizeReportCount(record.ReportCount), null);
        }

        public void Delete(ServerSession session)
        {
            Delete(CreateStableKey(session));
        }

        public void Delete(string key)
        {
            ValidateKey(key);
            lock (_sync)
            {
                DeleteForUserRequest(GetPath(key));
                DeleteForUserRequest(GetBackupPath(key));
                DeleteIfPresent(GetTemporaryPath(key));
            }
        }

        public void OpenMemoFolder()
        {
            lock (_sync) EnsureFolder();
            Process.Start("explorer.exe", QuoteArgument(_memoFolder));
        }

        private void SaveCore(
            string key, UserReportMemoRecord record, int reportCount, ServerSession session)
        {
            ValidateKey(key);
            DateTime now = DateTime.UtcNow;
            DateTime created = record.CreatedUtc == default(DateTime)
                ? now
                : NormalizeDate(record.CreatedUtc);
            var normalized = new UserReportMemoRecord
            {
                Key = key.ToLowerInvariant(),
                ReportCount = reportCount,
                Entries = NormalizeEntries(record.Entries),
                MemoText = LimitMemo(record.MemoText),
                Game = LimitText(session == null ? record.Game : session.GameDisplayName, 32),
                RaidStartedUtc = session == null
                    ? NormalizeDate(record.RaidStartedUtc)
                    : NormalizeDate(session.SessionStarted),
                MapName = LimitText(session == null ? record.MapName : session.MapName, 256),
                CreatedUtc = created,
                UpdatedUtc = now
            };
            string json = _serializer.Serialize(normalized);
            if (json.Length <= 0 || json.Length > MaximumJsonLength)
                throw new InvalidOperationException("유저신고 메모 데이터가 허용 크기를 초과했습니다.");

            lock (_sync)
            {
                EnsureFolder();
                string target = GetPath(key);
                string backup = GetBackupPath(key);
                string temporary = GetTemporaryPath(key);
                DeleteIfPresent(temporary);
                WriteDurably(temporary, json);
                try
                {
                    if (File.Exists(target))
                        File.Replace(temporary, target, backup, true);
                    else
                        File.Move(temporary, target);
                }
                finally
                {
                    DeleteIfPresent(temporary);
                }
            }

            record.Key = normalized.Key;
            record.ReportCount = normalized.ReportCount;
            record.Entries = CloneEntries(normalized.Entries);
            record.MemoText = normalized.MemoText;
            record.Game = normalized.Game;
            record.RaidStartedUtc = normalized.RaidStartedUtc;
            record.MapName = normalized.MapName;
            record.CreatedUtc = normalized.CreatedUtc;
            record.UpdatedUtc = normalized.UpdatedUtc;
        }

        private UserReportMemoRecord TryRead(string path, string expectedKey)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumJsonLength * 2L)
                    return null;

                string json;
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    json = reader.ReadToEnd();
                }
                if (json.Length <= 0 || json.Length > MaximumJsonLength) return null;

                UserReportMemoRecord record = _serializer.Deserialize<UserReportMemoRecord>(json);
                if (record == null
                    || !string.Equals(record.Key, expectedKey, StringComparison.OrdinalIgnoreCase)
                    || record.ReportCount < 1
                    || record.ReportCount > MaximumReportCount
                    || !AreValidEntries(record.Entries)
                    || (record.MemoText != null && record.MemoText.Length > MaximumMemoLength)
                    || record.CreatedUtc == default(DateTime)
                    || record.UpdatedUtc == default(DateTime))
                    return null;

                record.Key = expectedKey.ToLowerInvariant();
                record.Entries = NormalizeEntries(record.Entries);
                record.MemoText = record.MemoText ?? string.Empty;
                record.Game = LimitText(record.Game, 32);
                record.RaidStartedUtc = NormalizeDate(record.RaidStartedUtc);
                record.MapName = LimitText(record.MapName, 256);
                record.CreatedUtc = NormalizeDate(record.CreatedUtc);
                record.UpdatedUtc = NormalizeDate(record.UpdatedUtc);
                return record;
            }
            catch
            {
                return null;
            }
        }

        private string GetPath(string key)
        {
            return Path.Combine(_memoFolder, key.ToLowerInvariant() + ".json");
        }

        private string GetBackupPath(string key)
        {
            return GetPath(key) + ".bak";
        }

        private string GetTemporaryPath(string key)
        {
            return GetPath(key) + ".tmp";
        }

        private void EnsureFolder()
        {
            if (!Directory.Exists(_memoFolder)) Directory.CreateDirectory(_memoFolder);
        }

        private static int NormalizeReportCount(int count)
        {
            if (count < 1) return 1;
            return Math.Min(count, MaximumReportCount);
        }

        public static string BuildDisplayText(UserReportMemoRecord record)
        {
            if (record == null) return string.Empty;
            var builder = new StringBuilder();
            IList<UserReportMemoEntry> entries = record.Entries;
            if (entries != null)
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    UserReportMemoEntry entry = entries[index];
                    string nickname = entry == null ? string.Empty : (entry.Nickname ?? string.Empty).Trim();
                    string reason = entry == null ? string.Empty : (entry.Reason ?? string.Empty).Trim();
                    if (nickname.Length == 0 && reason.Length == 0) continue;
                    if (builder.Length > 0) builder.AppendLine();
                    builder.Append(index + 1);
                    builder.Append(". 유저네임: ");
                    builder.Append(nickname.Length == 0 ? "-" : nickname);
                    builder.Append(" · 신고사유: ");
                    builder.Append(reason.Length == 0 ? "-" : reason);
                }
            }

            string legacy = record.MemoText ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(legacy))
            {
                if (builder.Length > 0) builder.AppendLine().AppendLine();
                builder.Append(legacy.Trim());
            }
            return builder.ToString();
        }

        private static List<UserReportMemoEntry> NormalizeEntries(
            IEnumerable<UserReportMemoEntry> entries)
        {
            var normalized = new List<UserReportMemoEntry>();
            if (entries == null) return normalized;
            foreach (UserReportMemoEntry entry in entries.Take(MaximumReportCount))
            {
                normalized.Add(new UserReportMemoEntry
                {
                    Nickname = LimitText(entry == null ? null : entry.Nickname, MaximumNicknameLength),
                    Reason = LimitText(entry == null ? null : entry.Reason, MaximumReasonLength)
                });
            }
            return normalized;
        }

        private static List<UserReportMemoEntry> CloneEntries(
            IEnumerable<UserReportMemoEntry> entries)
        {
            return NormalizeEntries(entries);
        }

        private static bool AreValidEntries(IList<UserReportMemoEntry> entries)
        {
            if (entries == null) return true; // v0.6.6 JSON did not contain this property.
            if (entries.Count > MaximumReportCount) return false;
            foreach (UserReportMemoEntry entry in entries)
            {
                if (entry == null) continue;
                if ((entry.Nickname != null && entry.Nickname.Length > MaximumNicknameLength)
                    || (entry.Reason != null && entry.Reason.Length > MaximumReasonLength))
                    return false;
            }
            return true;
        }

        private static string LimitMemo(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= MaximumMemoLength
                ? value
                : value.Substring(0, MaximumMemoLength);
        }

        private static string LimitText(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        private static DateTime NormalizeDate(DateTime value)
        {
            if (value == default(DateTime)) return default(DateTime);
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        }

        private static string CreateDomainKey(string raidNoteKey)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(
                    Encoding.UTF8.GetBytes("user-report-memo-v1\n" + raidNoteKey));
                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest)
                    builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)
                || key.Length != 64
                || key.Any(character => !((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'))))
                throw new ArgumentException("유효하지 않은 유저신고 메모 키입니다.", "key");
        }

        private static void WriteDurably(string path, string content)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static void DeleteIfPresent(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Cleanup must not hide the original operation result.
            }
        }

        private static void DeleteForUserRequest(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", string.Empty) + "\"";
        }
    }
}
