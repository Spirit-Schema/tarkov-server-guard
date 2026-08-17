// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    public enum MemoArchiveBackupKind
    {
        RaidNote,
        UserReportMemo
    }

    public enum MemoArchiveRestoreStatus
    {
        New,
        Existing,
        ExistingConflict,
        Unavailable
    }

    public sealed class MemoArchiveBackupExportResult
    {
        public bool Success { get; set; }
        public string SafeErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public byte[] Utf8Bytes { get; set; }
        public int RaidNoteCount { get; set; }
        public int UserReportMemoCount { get; set; }

        public int TotalCount
        {
            get { return RaidNoteCount + UserReportMemoCount; }
        }
    }

    public sealed class MemoArchiveBackupParsedItem
    {
        public MemoArchiveBackupKind Kind { get; set; }
        public int SourceIndex { get; set; }
        public string Key { get; set; }
        public RaidNoteRecord RaidNote { get; set; }
        public UserReportMemoRecord UserReportMemo { get; set; }
        public string PreviewText { get; set; }
    }

    public sealed class MemoArchiveBackupParseResult
    {
        public MemoArchiveBackupParseResult()
        {
            Items = new List<MemoArchiveBackupParsedItem>();
        }

        public bool Success { get; set; }
        public string SafeErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime CreatedUtc { get; set; }
        public IList<MemoArchiveBackupParsedItem> Items { get; set; }

        public int RaidNoteCount
        {
            get { return Items.Count(item => item.Kind == MemoArchiveBackupKind.RaidNote); }
        }

        public int UserReportMemoCount
        {
            get { return Items.Count(item => item.Kind == MemoArchiveBackupKind.UserReportMemo); }
        }
    }

    public sealed class MemoArchiveRestoreItem
    {
        public MemoArchiveBackupParsedItem Source { get; set; }
        public MemoArchiveRestoreStatus Status { get; set; }
        public string Detail { get; set; }
        public bool Selected { get; set; }
    }

    public sealed class MemoArchiveRestoreItemResult
    {
        public MemoArchiveRestoreItem Item { get; set; }
        public bool Added { get; set; }
        public bool Skipped { get; set; }
        public string SafeErrorCode { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class MemoArchiveRestoreResult
    {
        public MemoArchiveRestoreResult()
        {
            ItemResults = new List<MemoArchiveRestoreItemResult>();
        }

        public int AddedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }
        public IList<MemoArchiveRestoreItemResult> ItemResults { get; set; }
    }

    /// <summary>
    /// Creates and validates the portable memo archive format. Screenshot link strings are
    /// retained so a restored memo keeps its attachment list, but screenshot file bytes are
    /// never read or embedded. This format is separate from the blocked-server backup format.
    /// </summary>
    public static class MemoArchiveBackupService
    {
        public const string FormatName = "TarkovServerGuard.Memos";
        public const int CurrentVersion = 1;
        public const int MaximumFileBytes = 16 * 1024 * 1024;
        public const int MaximumItemCount = 2048;
        public const int MaximumNestingDepth = 12;
        private const int MaximumGameLength = 32;
        private const int MaximumGameTypeLength = 64;
        private const int MaximumMapLength = 256;
        private const int MaximumRaidNoteLength = 200000;
        private const int MaximumTagCount = 100;
        private const int MaximumTagLength = 64;
        private const int MaximumScreenshotPathCount =
            RaidNoteStore.MaximumScreenshotPathCount;
        private const int MaximumScreenshotPathLength =
            RaidNoteStore.MaximumScreenshotPathLength;
        private const int MaximumPreviewLength = 80;

        private static readonly object RestoreSync = new object();
        private static readonly HashSet<string> TopLevelFields = CreateFieldSet(
            "Format", "Version", "CreatedUtc", "RaidNotes", "UserReportMemos");
        private static readonly HashSet<string> RaidNoteFields = CreateFieldSet(
            "Key", "Game", "GameType", "RaidStartedUtc", "MapName", "NoteText",
            "ScreenshotPaths", "Tags", "CreatedUtc", "UpdatedUtc");
        private static readonly HashSet<string> UserReportFields = CreateFieldSet(
            "Key", "ReportCount", "Entries", "MemoText", "Game", "GameType",
            "RaidStartedUtc", "MapName", "CreatedUtc", "UpdatedUtc");
        private static readonly HashSet<string> UserReportEntryFields = CreateFieldSet(
            "Nickname", "Reason");

        public static string CreateDefaultFileName(DateTime localTimestamp)
        {
            return "TarkovServerGuard-memos-"
                + localTimestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + ".json";
        }

        public static MemoArchiveBackupExportResult CreateExport(
            RaidNoteStore raidStore,
            UserReportMemoStore userReportMemoStore,
            DateTime createdUtc)
        {
            if (raidStore == null) throw new ArgumentNullException("raidStore");
            if (userReportMemoStore == null)
                throw new ArgumentNullException("userReportMemoStore");

            try
            {
                return CreateExport(
                    raidStore.LoadAll(),
                    userReportMemoStore.LoadAll(),
                    createdUtc);
            }
            catch
            {
                return ExportFailure(
                    "source-read-failed",
                    "저장된 메모를 안전하게 읽지 못해 백업을 만들지 않았습니다.");
            }
        }

        public static MemoArchiveBackupExportResult CreateExport(
            IEnumerable<RaidNoteRecord> raidNotes,
            IEnumerable<UserReportMemoRecord> userReportMemos,
            DateTime createdUtc)
        {
            var result = new MemoArchiveBackupExportResult();
            try
            {
                DateTime normalizedCreated = RequireUtc(createdUtc, false, "생성 시각");
                List<RaidNoteRecord> raids = (raidNotes ?? Enumerable.Empty<RaidNoteRecord>())
                    .Select(CloneAndValidateRaidNote)
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ToList();
                List<UserReportMemoRecord> reports =
                    (userReportMemos ?? Enumerable.Empty<UserReportMemoRecord>())
                    .Select(CloneAndValidateUserReportMemo)
                    .OrderBy(item => item.Key, StringComparer.Ordinal)
                    .ToList();

                if (raids.Count + reports.Count == 0)
                    return ExportFailure(
                        "empty-archive",
                        "현재 백업할 메모가 없어 파일을 만들지 않았습니다.");
                if (raids.Count + reports.Count > MaximumItemCount)
                    return ExportFailure(
                        "item-limit",
                        string.Format(
                            CultureInfo.CurrentCulture,
                            "전체 메모 수가 백업 한도({0}개)를 초과했습니다.",
                            MaximumItemCount));
                if (raids.Any(item => item.UpdatedUtc > normalizedCreated)
                    || reports.Any(item => item.UpdatedUtc > normalizedCreated))
                    return ExportFailure(
                        "archive-timestamp-order-invalid",
                        "백업 생성 시각보다 늦은 메모 수정 시각이 있어 파일을 만들지 않았습니다.");
                EnsureUniqueKeys(raids.Select(item => item.Key), "레이드 메모");
                EnsureUniqueKeys(reports.Select(item => item.Key), "유저신고 메모");

                if (EstimateMaximumSerializedBytes(normalizedCreated, raids, reports)
                    > MaximumFileBytes)
                    return ExportFailure(
                        "file-size-limit",
                        "메모 백업 파일 크기 한도를 초과했습니다.");

                string json = SerializeDocument(normalizedCreated, raids, reports);
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(json);
                if (bytes.Length > MaximumFileBytes)
                    return ExportFailure(
                        "file-size-limit",
                        "메모 백업 파일 크기 한도를 초과했습니다.");

                result.Success = true;
                result.Utf8Bytes = bytes;
                result.RaidNoteCount = raids.Count;
                result.UserReportMemoCount = reports.Count;
                return result;
            }
            catch (MemoArchiveValidationException ex)
            {
                return ExportFailure(ex.SafeErrorCode, ex.Message);
            }
            catch
            {
                return ExportFailure(
                    "export-failed",
                    "메모 백업 데이터를 만들지 못했습니다.");
            }
        }

        public static MemoArchiveBackupParseResult ParseFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ParseFailure("path-invalid", "백업 파일 경로가 비어 있습니다.");
            try
            {
                var info = new FileInfo(Path.GetFullPath(path));
                if (!info.Exists || info.Length <= 0)
                    return ParseFailure("file-empty", "비어 있거나 찾을 수 없는 메모 백업 파일입니다.");
                if (info.Length > MaximumFileBytes)
                    return ParseFailure("file-size-limit", "메모 백업 파일 크기 한도를 초과했습니다.");

                byte[] bytes = new byte[(int)info.Length];
                using (var stream = new FileStream(
                    info.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.SequentialScan))
                {
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                            return ParseFailure("file-read-failed", "메모 백업 파일을 끝까지 읽지 못했습니다.");
                        offset += read;
                    }
                    if (stream.ReadByte() >= 0)
                        return ParseFailure("file-size-changed", "읽는 동안 메모 백업 파일이 변경되었습니다.");
                }
                return Parse(bytes);
            }
            catch
            {
                return ParseFailure("file-read-failed", "메모 백업 파일을 안전하게 읽지 못했습니다.");
            }
        }

        public static MemoArchiveBackupParseResult Parse(byte[] utf8Bytes)
        {
            if (utf8Bytes == null || utf8Bytes.Length < 2)
                return ParseFailure("file-empty", "비어 있거나 올바르지 않은 메모 백업 파일입니다.");
            if (utf8Bytes.Length > MaximumFileBytes)
                return ParseFailure("file-size-limit", "메모 백업 파일 크기 한도를 초과했습니다.");

            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(utf8Bytes);
                if (json.Length > 0 && json[0] == '\ufeff') json = json.Substring(1);
            }
            catch (DecoderFallbackException)
            {
                return ParseFailure("utf8-invalid", "메모 백업 파일이 올바른 UTF-8 형식이 아닙니다.");
            }

            var reader = new JsonMemberUniquenessReader(json, MaximumNestingDepth);
            if (!reader.ReadDocument())
            {
                return reader.DuplicateMemberFound
                    ? ParseFailure("duplicate-member", "메모 백업 JSON에 중복된 필드가 포함되어 있습니다.")
                    : ParseFailure("json-invalid", "메모 백업 JSON 형식을 읽을 수 없습니다.");
            }

            IDictionary<string, object> document;
            try
            {
                document = CreateSerializer().DeserializeObject(json) as IDictionary<string, object>;
            }
            catch
            {
                return ParseFailure("json-invalid", "메모 백업 JSON 형식을 읽을 수 없습니다.");
            }
            if (document == null || !HasExactFields(document, TopLevelFields))
                return ParseFailure(
                    "document-fields-invalid",
                    "메모 백업의 필수 필드가 없거나 지원하지 않는 필드가 포함되어 있습니다.");

            object rawFormat;
            object rawVersion;
            object rawCreated;
            object rawRaids;
            object rawReports;
            if (!document.TryGetValue("Format", out rawFormat)
                || !(rawFormat is string)
                || !string.Equals((string)rawFormat, FormatName, StringComparison.Ordinal)
                || !document.TryGetValue("Version", out rawVersion)
                || !(rawVersion is int)
                || (int)rawVersion != CurrentVersion
                || !document.TryGetValue("CreatedUtc", out rawCreated)
                || !document.TryGetValue("RaidNotes", out rawRaids)
                || !document.TryGetValue("UserReportMemos", out rawReports))
            {
                return ParseFailure(
                    "format-unsupported",
                    "지원하지 않는 메모 백업 형식 또는 버전입니다.");
            }

            DateTime createdUtc;
            if (!TryReadUtcString(rawCreated, false, out createdUtc))
                return ParseFailure("created-utc-invalid", "메모 백업 생성 시각이 올바른 UTC 시각이 아닙니다.");

            object[] raidArray = rawRaids as object[];
            object[] reportArray = rawReports as object[];
            if (raidArray == null || reportArray == null)
                return ParseFailure("list-type-invalid", "메모 목록의 자료형이 올바르지 않습니다.");
            if (raidArray.Length + reportArray.Length > MaximumItemCount)
                return ParseFailure(
                    "item-limit",
                    string.Format(
                        CultureInfo.CurrentCulture,
                        "전체 메모 수가 불러오기 한도({0}개)를 초과했습니다.",
                        MaximumItemCount));

            var result = new MemoArchiveBackupParseResult { CreatedUtc = createdUtc };
            var raidKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reportKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < raidArray.Length; index++)
            {
                RaidNoteRecord record;
                string errorCode;
                string error;
                if (!TryParseRaidNote(raidArray[index], index + 1, out record, out errorCode, out error))
                    return ParseFailure(errorCode, error);
                if (!raidKeys.Add(record.Key))
                    return ParseFailure(
                        "raid-key-duplicate",
                        "레이드 메모 " + (index + 1).ToString(CultureInfo.CurrentCulture)
                            + "번의 키가 같은 종류 안에서 중복됩니다.");
                result.Items.Add(new MemoArchiveBackupParsedItem
                {
                    Kind = MemoArchiveBackupKind.RaidNote,
                    SourceIndex = index + 1,
                    Key = record.Key,
                    RaidNote = record,
                    PreviewText = CreatePreview(record.NoteText)
                });
            }
            for (int index = 0; index < reportArray.Length; index++)
            {
                UserReportMemoRecord record;
                string errorCode;
                string error;
                if (!TryParseUserReportMemo(
                        reportArray[index], index + 1, out record, out errorCode, out error))
                    return ParseFailure(errorCode, error);
                if (!reportKeys.Add(record.Key))
                    return ParseFailure(
                        "report-key-duplicate",
                        "유저신고 메모 " + (index + 1).ToString(CultureInfo.CurrentCulture)
                            + "번의 키가 같은 종류 안에서 중복됩니다.");
                result.Items.Add(new MemoArchiveBackupParsedItem
                {
                    Kind = MemoArchiveBackupKind.UserReportMemo,
                    SourceIndex = index + 1,
                    Key = record.Key,
                    UserReportMemo = record,
                    PreviewText = CreatePreview(UserReportMemoStore.BuildDisplayText(record))
                });
            }
            if (result.Items.Any(item => GetUpdatedUtc(item) > result.CreatedUtc))
                return ParseFailure(
                    "archive-timestamp-order-invalid",
                    "백업 생성 시각보다 늦은 메모 수정 시각이 포함되어 있습니다.");
            result.Success = true;
            return result;
        }

        public static IList<MemoArchiveRestoreItem> CreateRestorePreview(
            MemoArchiveBackupParseResult parsed,
            RaidNoteStore raidStore,
            UserReportMemoStore userReportMemoStore)
        {
            var preview = new List<MemoArchiveRestoreItem>();
            if (parsed == null || !parsed.Success || parsed.Items == null) return preview;
            if (raidStore == null) throw new ArgumentNullException("raidStore");
            if (userReportMemoStore == null)
                throw new ArgumentNullException("userReportMemoStore");

            foreach (MemoArchiveBackupParsedItem source in parsed.Items)
            {
                var item = new MemoArchiveRestoreItem { Source = source };
                try
                {
                    bool exists = source.Kind == MemoArchiveBackupKind.RaidNote
                        ? raidStore.Exists(source.Key)
                        : userReportMemoStore.Exists(source.Key);
                    item.Status = exists
                        ? MemoArchiveRestoreStatus.Existing
                        : MemoArchiveRestoreStatus.New;
                    item.Detail = exists
                        ? "같은 종류와 키의 기존 메모가 있어 건너뜁니다."
                        : "없는 메모이므로 새로 추가할 수 있습니다.";
                    item.Selected = !exists;
                }
                catch
                {
                    item.Status = MemoArchiveRestoreStatus.Unavailable;
                    item.Detail = "현재 저장소 상태를 확인하지 못했습니다.";
                    item.Selected = false;
                }
                preview.Add(item);
            }
            return preview;
        }

        public static MemoArchiveRestoreResult ApplyMissingOnly(
            IEnumerable<MemoArchiveRestoreItem> items,
            RaidNoteStore raidStore,
            UserReportMemoStore userReportMemoStore)
        {
            if (raidStore == null) throw new ArgumentNullException("raidStore");
            if (userReportMemoStore == null)
                throw new ArgumentNullException("userReportMemoStore");

            var result = new MemoArchiveRestoreResult();
            lock (RestoreSync)
            {
                foreach (MemoArchiveRestoreItem item in items ?? Enumerable.Empty<MemoArchiveRestoreItem>())
                {
                    if (item == null || item.Source == null) continue;
                    if (item.Status == MemoArchiveRestoreStatus.Existing)
                    {
                        AddSkippedResult(result, item);
                        continue;
                    }
                    if (item.Status == MemoArchiveRestoreStatus.Unavailable)
                    {
                        AddFailedResult(
                            result,
                            item,
                            "restore-store-unavailable",
                            SafeItemLabel(item.Source) + "의 저장소 상태를 확인하지 못했습니다.");
                        continue;
                    }
                    if (item.Status == MemoArchiveRestoreStatus.ExistingConflict)
                    {
                        AddFailedResult(
                            result,
                            item,
                            "restore-existing-conflict",
                            SafeItemLabel(item.Source) + "에 기존 메모 충돌이 있어 덮어쓰지 않았습니다.");
                        continue;
                    }
                    if (item.Status != MemoArchiveRestoreStatus.New || !item.Selected) continue;

                    try
                    {
                        bool added;
                        bool verified;
                        if (item.Source.Kind == MemoArchiveBackupKind.RaidNote)
                        {
                            RaidNoteRecord copy = CloneRaidNote(item.Source.RaidNote);
                            added = raidStore.TryRestoreMissing(item.Source.Key, copy);
                            if (!added)
                            {
                                RaidNoteRecord existing = raidStore.Load(item.Source.Key);
                                HandleExistingAfterRestoreRace(
                                    result,
                                    item,
                                    RaidNotesEquivalentForRestore(item.Source.RaidNote, existing));
                                continue;
                            }
                            RaidNoteRecord loaded = raidStore.Load(item.Source.Key);
                            verified = RaidNotesEquivalentForRestore(item.Source.RaidNote, loaded);
                        }
                        else
                        {
                            UserReportMemoRecord copy = CloneUserReportMemo(item.Source.UserReportMemo);
                            added = userReportMemoStore.TryRestoreMissing(item.Source.Key, copy);
                            if (!added)
                            {
                                UserReportMemoRecord existing = userReportMemoStore.Load(
                                    item.Source.Key);
                                HandleExistingAfterRestoreRace(
                                    result,
                                    item,
                                    UserReportMemosEquivalentForRestore(
                                        item.Source.UserReportMemo, existing));
                                continue;
                            }
                            UserReportMemoRecord loaded = userReportMemoStore.Load(item.Source.Key);
                            verified = UserReportMemosEquivalentForRestore(
                                item.Source.UserReportMemo, loaded);
                        }

                        if (!verified)
                        {
                            AddFailedResult(
                                result,
                                item,
                                "restore-verification-failed",
                                SafeItemLabel(item.Source) + " 저장 후 확인에 실패했습니다.");
                            continue;
                        }
                        result.AddedCount++;
                        item.Selected = false;
                        result.ItemResults.Add(new MemoArchiveRestoreItemResult
                        {
                            Item = item,
                            Added = true
                        });
                    }
                    catch
                    {
                        AddFailedResult(
                            result,
                            item,
                            "restore-save-failed",
                            SafeItemLabel(item.Source) + " 저장에 실패했습니다.");
                    }
                }
            }
            return result;
        }

        public static void WriteAtomic(string path, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("백업 파일 경로가 비어 있습니다.", "path");

            MemoArchiveBackupParseResult parsed = Parse(bytes);
            if (!parsed.Success || parsed.Items.Count == 0)
                throw new InvalidDataException(
                    parsed.Success
                        ? "빈 메모 백업은 기존 파일을 덮어쓸 수 없습니다."
                        : parsed.ErrorMessage);

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("백업 파일 폴더를 찾을 수 없습니다.");
            string temporaryPath = Path.Combine(
                directory,
                Path.GetFileName(fullPath) + ".tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(fullPath))
                    File.Replace(temporaryPath, fullPath, null, true);
                else
                    File.Move(temporaryPath, fullPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                    // A stale uniquely named temporary file is never selected automatically.
                }
            }
        }

        private static void AddSkippedResult(
            MemoArchiveRestoreResult result,
            MemoArchiveRestoreItem item)
        {
            result.SkippedCount++;
            result.ItemResults.Add(new MemoArchiveRestoreItemResult
            {
                Item = item,
                Skipped = true
            });
        }

        private static void HandleExistingAfterRestoreRace(
            MemoArchiveRestoreResult result,
            MemoArchiveRestoreItem item,
            bool sameContent)
        {
            if (sameContent)
            {
                item.Status = MemoArchiveRestoreStatus.Existing;
                item.Selected = false;
                item.Detail = "같은 메모가 먼저 저장되어 안전하게 건너뛰었습니다.";
                AddSkippedResult(result, item);
                return;
            }

            item.Status = MemoArchiveRestoreStatus.ExistingConflict;
            item.Detail = "적용 전에 같은 종류와 키의 다른 메모가 생겨 덮어쓰지 않았습니다.";
            AddFailedResult(
                result,
                item,
                "restore-existing-conflict",
                SafeItemLabel(item.Source) + "에 기존 메모 충돌이 있어 덮어쓰지 않았습니다.");
        }

        private static void AddFailedResult(
            MemoArchiveRestoreResult result,
            MemoArchiveRestoreItem item,
            string errorCode,
            string errorMessage)
        {
            result.FailedCount++;
            result.ItemResults.Add(new MemoArchiveRestoreItemResult
            {
                Item = item,
                SafeErrorCode = errorCode,
                ErrorMessage = errorMessage
            });
        }

        private static bool TryParseRaidNote(
            object raw,
            int sourceIndex,
            out RaidNoteRecord record,
            out string errorCode,
            out string error)
        {
            record = null;
            errorCode = null;
            error = null;
            IDictionary<string, object> item = raw as IDictionary<string, object>;
            string prefix = "레이드 메모 " + sourceIndex.ToString(CultureInfo.CurrentCulture) + "번";
            if (item == null || !HasExactFields(item, RaidNoteFields))
                return ParseItemFailure(
                    prefix, "fields-invalid", "의 필수 필드가 없거나 지원하지 않는 필드가 있습니다.",
                    out errorCode, out error);

            string key;
            string game;
            string gameType;
            string map;
            string note;
            DateTime raidStarted;
            DateTime created;
            DateTime updated;
            object rawScreenshotPaths;
            object rawTags;
            if (!TryReadKey(item, "Key", out key)
                || !TryReadString(item, "Game", MaximumGameLength, out game)
                || !TryReadString(item, "GameType", MaximumGameTypeLength, out gameType)
                || !TryReadUtc(item, "RaidStartedUtc", true, out raidStarted)
                || !TryReadString(item, "MapName", MaximumMapLength, out map)
                || !TryReadString(item, "NoteText", MaximumRaidNoteLength, out note)
                || !item.TryGetValue("ScreenshotPaths", out rawScreenshotPaths)
                || !item.TryGetValue("Tags", out rawTags)
                || !TryReadUtc(item, "CreatedUtc", false, out created)
                || !TryReadUtc(item, "UpdatedUtc", false, out updated))
                return ParseItemFailure(
                    prefix, "value-invalid", "의 값 또는 자료형이 올바르지 않습니다.",
                    out errorCode, out error);

            object[] screenshotPathArray = rawScreenshotPaths as object[];
            if (screenshotPathArray == null
                || screenshotPathArray.Length > MaximumScreenshotPathCount)
                return ParseItemFailure(
                    prefix, "screenshot-list-invalid",
                    "의 스크린샷 연결 목록이 올바르지 않습니다.",
                    out errorCode, out error);
            var screenshotPaths = new List<string>();
            var seenScreenshotPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object rawScreenshotPath in screenshotPathArray)
            {
                string screenshotPath = rawScreenshotPath as string;
                if (string.IsNullOrWhiteSpace(screenshotPath)
                    || screenshotPath.Length > MaximumScreenshotPathLength
                    || !HasValidUnicode(screenshotPath)
                    || !RaidNoteStore.IsSafeScreenshotAttachmentPath(screenshotPath)
                    || !seenScreenshotPaths.Add(screenshotPath))
                    return ParseItemFailure(
                        prefix, "screenshot-path-invalid",
                        "의 스크린샷 연결 경로가 올바르지 않거나 중복됩니다.",
                        out errorCode, out error);
                screenshotPaths.Add(screenshotPath);
            }

            object[] tagArray = rawTags as object[];
            if (tagArray == null || tagArray.Length > MaximumTagCount)
                return ParseItemFailure(
                    prefix, "tag-list-invalid", "의 태그 목록이 올바르지 않습니다.",
                    out errorCode, out error);
            var tags = new List<string>();
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object rawTag in tagArray)
            {
                string tag = rawTag as string;
                if (tag == null || tag.Length == 0 || tag.Length > MaximumTagLength
                    || !HasValidUnicode(tag)
                    || !string.Equals(tag, tag.Trim(), StringComparison.Ordinal)
                    || !seenTags.Add(tag))
                    return ParseItemFailure(
                        prefix, "tag-invalid", "의 태그 값이 올바르지 않거나 중복됩니다.",
                        out errorCode, out error);
                tags.Add(tag);
            }
            record = new RaidNoteRecord
            {
                Key = key,
                Game = game,
                GameType = gameType,
                RaidStartedUtc = raidStarted,
                MapName = map,
                NoteText = RaidNoteStore.NormalizeLegacyNoteText(note),
                Tags = tags,
                ScreenshotPaths = screenshotPaths,
                CreatedUtc = created,
                UpdatedUtc = updated
            };
            if (record.CreatedUtc > record.UpdatedUtc)
                return ParseItemFailure(
                    prefix, "timestamp-order-invalid", "의 생성 시각이 수정 시각보다 늦습니다.",
                    out errorCode, out error);
            return true;
        }

        private static bool TryParseUserReportMemo(
            object raw,
            int sourceIndex,
            out UserReportMemoRecord record,
            out string errorCode,
            out string error)
        {
            record = null;
            errorCode = null;
            error = null;
            IDictionary<string, object> item = raw as IDictionary<string, object>;
            string prefix = "유저신고 메모 " + sourceIndex.ToString(CultureInfo.CurrentCulture) + "번";
            if (item == null || !HasExactFields(item, UserReportFields))
                return ParseItemFailure(
                    prefix, "fields-invalid", "의 필수 필드가 없거나 지원하지 않는 필드가 있습니다.",
                    out errorCode, out error);

            string key;
            string memo;
            string game;
            string gameType;
            string map;
            DateTime raidStarted;
            DateTime created;
            DateTime updated;
            object rawCount;
            object rawEntries;
            if (!TryReadKey(item, "Key", out key)
                || !item.TryGetValue("ReportCount", out rawCount)
                || !(rawCount is int)
                || (int)rawCount < 1
                || (int)rawCount > UserReportMemoStore.MaximumReportCount
                || !item.TryGetValue("Entries", out rawEntries)
                || !TryReadString(item, "MemoText", UserReportMemoStore.MaximumMemoLength, out memo)
                || !TryReadString(item, "Game", MaximumGameLength, out game)
                || !TryReadString(item, "GameType", MaximumGameTypeLength, out gameType)
                || !TryReadUtc(item, "RaidStartedUtc", true, out raidStarted)
                || !TryReadString(item, "MapName", MaximumMapLength, out map)
                || !TryReadUtc(item, "CreatedUtc", false, out created)
                || !TryReadUtc(item, "UpdatedUtc", false, out updated))
                return ParseItemFailure(
                    prefix, "value-invalid", "의 값 또는 자료형이 올바르지 않습니다.",
                    out errorCode, out error);

            object[] entryArray = rawEntries as object[];
            if (entryArray == null || entryArray.Length > UserReportMemoStore.MaximumReportCount)
                return ParseItemFailure(
                    prefix, "entry-list-invalid", "의 신고 항목 목록이 올바르지 않습니다.",
                    out errorCode, out error);
            var entries = new List<UserReportMemoEntry>();
            foreach (object rawEntry in entryArray)
            {
                IDictionary<string, object> entry = rawEntry as IDictionary<string, object>;
                string nickname;
                string reason;
                if (entry == null || !HasExactFields(entry, UserReportEntryFields)
                    || !TryReadString(
                        entry,
                        "Nickname",
                        UserReportMemoStore.MaximumNicknameLength,
                        out nickname)
                    || !TryReadString(
                        entry,
                        "Reason",
                        UserReportMemoStore.MaximumReasonLength,
                        out reason))
                    return ParseItemFailure(
                        prefix, "entry-invalid", "의 신고 항목 값이 올바르지 않습니다.",
                        out errorCode, out error);
                entries.Add(new UserReportMemoEntry { Nickname = nickname, Reason = reason });
            }
            record = new UserReportMemoRecord
            {
                Key = key,
                ReportCount = (int)rawCount,
                Entries = entries,
                MemoText = memo,
                Game = game,
                GameType = gameType,
                RaidStartedUtc = raidStarted,
                MapName = map,
                CreatedUtc = created,
                UpdatedUtc = updated
            };
            if (record.CreatedUtc > record.UpdatedUtc)
                return ParseItemFailure(
                    prefix, "timestamp-order-invalid", "의 생성 시각이 수정 시각보다 늦습니다.",
                    out errorCode, out error);
            return true;
        }

        private static bool ParseItemFailure(
            string prefix,
            string code,
            string suffix,
            out string errorCode,
            out string error)
        {
            errorCode = code;
            error = prefix + suffix;
            return false;
        }

        private static bool TryReadString(
            IDictionary<string, object> source,
            string name,
            int maximumLength,
            out string value)
        {
            value = null;
            object raw;
            if (!source.TryGetValue(name, out raw) || !(raw is string)) return false;
            value = (string)raw;
            return value.Length <= maximumLength && HasValidUnicode(value);
        }

        private static bool TryReadKey(
            IDictionary<string, object> source,
            string name,
            out string value)
        {
            value = null;
            object raw;
            if (!source.TryGetValue(name, out raw) || !(raw is string)) return false;
            string key = (string)raw;
            if (!IsValidKey(key)) return false;
            value = key.ToLowerInvariant();
            return true;
        }

        private static bool TryReadUtc(
            IDictionary<string, object> source,
            string name,
            bool allowDefault,
            out DateTime value)
        {
            value = default(DateTime);
            object raw;
            return source.TryGetValue(name, out raw)
                && TryReadUtcString(raw, allowDefault, out value);
        }

        private static bool TryReadUtcString(object raw, bool allowDefault, out DateTime value)
        {
            value = default(DateTime);
            string text = raw as string;
            DateTime parsed;
            if (text == null || !text.EndsWith("Z", StringComparison.Ordinal)
                || !DateTime.TryParseExact(
                    text,
                    "o",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out parsed)
                || parsed.Kind != DateTimeKind.Utc
                || (!allowDefault && parsed == default(DateTime)))
                return false;
            value = NormalizeStoreTimestampPrecision(parsed);
            return true;
        }

        private static string SerializeDocument(
            DateTime createdUtc,
            IList<RaidNoteRecord> raids,
            IList<UserReportMemoRecord> reports)
        {
            var serializer = CreateSerializer();
            var builder = new StringBuilder();
            builder.Append('{');
            AppendProperty(builder, serializer, "Format", FormatName, true);
            builder.Append(",\"Version\":").Append(CurrentVersion.ToString(CultureInfo.InvariantCulture));
            AppendProperty(builder, serializer, "CreatedUtc", FormatUtc(createdUtc), false);
            builder.Append(",\"RaidNotes\":[");
            for (int index = 0; index < raids.Count; index++)
            {
                if (index > 0) builder.Append(',');
                AppendRaidNote(builder, serializer, raids[index]);
            }
            builder.Append("],\"UserReportMemos\":[");
            for (int index = 0; index < reports.Count; index++)
            {
                if (index > 0) builder.Append(',');
                AppendUserReportMemo(builder, serializer, reports[index]);
            }
            builder.Append("]}");
            return builder.ToString();
        }

        private static long EstimateMaximumSerializedBytes(
            DateTime createdUtc,
            IList<RaidNoteRecord> raids,
            IList<UserReportMemoRecord> reports)
        {
            var serializer = CreateSerializer();
            var utf8 = new UTF8Encoding(false, true);
            long total = 512L + SerializedStringBytes(serializer, utf8, FormatName)
                + SerializedStringBytes(serializer, utf8, FormatUtc(createdUtc));
            foreach (RaidNoteRecord item in raids)
            {
                total += 512L
                    + SerializedStringBytes(serializer, utf8, item.Key)
                    + SerializedStringBytes(serializer, utf8, item.Game)
                    + SerializedStringBytes(serializer, utf8, item.GameType)
                    + SerializedStringBytes(serializer, utf8, item.MapName)
                    + SerializedStringBytes(serializer, utf8, item.NoteText);
                foreach (string screenshotPath in item.ScreenshotPaths)
                    total += 8L + SerializedStringBytes(serializer, utf8, screenshotPath);
                foreach (string tag in item.Tags)
                    total += 8L + SerializedStringBytes(serializer, utf8, tag);
                if (total > MaximumFileBytes) return total;
            }
            foreach (UserReportMemoRecord item in reports)
            {
                total += 640L
                    + SerializedStringBytes(serializer, utf8, item.Key)
                    + SerializedStringBytes(serializer, utf8, item.MemoText)
                    + SerializedStringBytes(serializer, utf8, item.Game)
                    + SerializedStringBytes(serializer, utf8, item.GameType)
                    + SerializedStringBytes(serializer, utf8, item.MapName);
                foreach (UserReportMemoEntry entry in item.Entries)
                {
                    total += 64L
                        + SerializedStringBytes(serializer, utf8, entry.Nickname)
                        + SerializedStringBytes(serializer, utf8, entry.Reason);
                }
                if (total > MaximumFileBytes) return total;
            }
            return total;
        }

        private static int SerializedStringBytes(
            JavaScriptSerializer serializer,
            Encoding utf8,
            string value)
        {
            return utf8.GetByteCount(serializer.Serialize(value ?? string.Empty));
        }

        private static void AppendRaidNote(
            StringBuilder builder,
            JavaScriptSerializer serializer,
            RaidNoteRecord item)
        {
            builder.Append('{');
            AppendProperty(builder, serializer, "Key", item.Key, true);
            AppendProperty(builder, serializer, "Game", item.Game, false);
            AppendProperty(builder, serializer, "GameType", item.GameType, false);
            AppendProperty(builder, serializer, "RaidStartedUtc", FormatUtc(item.RaidStartedUtc), false);
            AppendProperty(builder, serializer, "MapName", item.MapName, false);
            AppendProperty(builder, serializer, "NoteText", item.NoteText, false);
            builder.Append(",\"ScreenshotPaths\":[");
            for (int index = 0; index < item.ScreenshotPaths.Count; index++)
            {
                if (index > 0) builder.Append(',');
                builder.Append(serializer.Serialize(item.ScreenshotPaths[index]));
            }
            builder.Append(']');
            builder.Append(",\"Tags\":[");
            for (int index = 0; index < item.Tags.Count; index++)
            {
                if (index > 0) builder.Append(',');
                builder.Append(serializer.Serialize(item.Tags[index]));
            }
            builder.Append(']');
            AppendProperty(builder, serializer, "CreatedUtc", FormatUtc(item.CreatedUtc), false);
            AppendProperty(builder, serializer, "UpdatedUtc", FormatUtc(item.UpdatedUtc), false);
            builder.Append('}');
        }

        private static void AppendUserReportMemo(
            StringBuilder builder,
            JavaScriptSerializer serializer,
            UserReportMemoRecord item)
        {
            builder.Append('{');
            AppendProperty(builder, serializer, "Key", item.Key, true);
            builder.Append(",\"ReportCount\":")
                .Append(item.ReportCount.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"Entries\":[");
            for (int index = 0; index < item.Entries.Count; index++)
            {
                if (index > 0) builder.Append(',');
                builder.Append('{');
                AppendProperty(builder, serializer, "Nickname", item.Entries[index].Nickname, true);
                AppendProperty(builder, serializer, "Reason", item.Entries[index].Reason, false);
                builder.Append('}');
            }
            builder.Append(']');
            AppendProperty(builder, serializer, "MemoText", item.MemoText, false);
            AppendProperty(builder, serializer, "Game", item.Game, false);
            AppendProperty(builder, serializer, "GameType", item.GameType, false);
            AppendProperty(builder, serializer, "RaidStartedUtc", FormatUtc(item.RaidStartedUtc), false);
            AppendProperty(builder, serializer, "MapName", item.MapName, false);
            AppendProperty(builder, serializer, "CreatedUtc", FormatUtc(item.CreatedUtc), false);
            AppendProperty(builder, serializer, "UpdatedUtc", FormatUtc(item.UpdatedUtc), false);
            builder.Append('}');
        }

        private static void AppendProperty(
            StringBuilder builder,
            JavaScriptSerializer serializer,
            string name,
            string value,
            bool first)
        {
            if (!first) builder.Append(',');
            builder.Append('"').Append(name).Append("\":").Append(serializer.Serialize(value));
        }

        private static RaidNoteRecord CloneAndValidateRaidNote(RaidNoteRecord source)
        {
            if (source == null)
                throw Validation("raid-null", "레이드 메모 항목의 형식이 올바르지 않습니다.");
            RaidNoteRecord clone = CloneRaidNote(source);
            clone.Key = RequireKey(clone.Key, "raid-key-invalid", "레이드 메모 키가 올바르지 않습니다.");
            clone.Game = RequireText(clone.Game, MaximumGameLength, "raid-game-invalid", "레이드 메모 게임 값이 너무 깁니다.");
            clone.GameType = RequireText(
                clone.GameType,
                MaximumGameTypeLength,
                "raid-game-type-invalid",
                "레이드 메모 게임유형 값이 너무 깁니다.");
            clone.MapName = RequireText(clone.MapName, MaximumMapLength, "raid-map-invalid", "레이드 메모 맵 값이 너무 깁니다.");
            clone.NoteText = RequireText(clone.NoteText, MaximumRaidNoteLength, "raid-note-invalid", "레이드 메모 본문이 너무 깁니다.");
            clone.RaidStartedUtc = RequireUtc(clone.RaidStartedUtc, true, "레이드 시작 시각");
            clone.CreatedUtc = RequireUtc(clone.CreatedUtc, false, "레이드 메모 생성 시각");
            clone.UpdatedUtc = RequireUtc(clone.UpdatedUtc, false, "레이드 메모 수정 시각");
            if (clone.Tags.Count > MaximumTagCount)
                throw Validation("raid-tag-limit", "레이드 메모 태그 수가 한도를 초과했습니다.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string tag in clone.Tags)
            {
                if (string.IsNullOrEmpty(tag) || tag.Length > MaximumTagLength
                    || !HasValidUnicode(tag)
                    || !string.Equals(tag, tag.Trim(), StringComparison.Ordinal)
                    || !seen.Add(tag))
                    throw Validation("raid-tag-invalid", "레이드 메모 태그가 올바르지 않거나 중복됩니다.");
            }
            if (clone.ScreenshotPaths.Count > MaximumScreenshotPathCount)
                throw Validation(
                    "raid-screenshot-limit",
                    "레이드 메모 스크린샷 연결 수가 한도를 초과했습니다.");
            var seenScreenshotPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string screenshotPath in clone.ScreenshotPaths)
            {
                if (string.IsNullOrWhiteSpace(screenshotPath)
                    || screenshotPath.Length > MaximumScreenshotPathLength
                    || !HasValidUnicode(screenshotPath)
                    || !RaidNoteStore.IsSafeScreenshotAttachmentPath(screenshotPath)
                    || !seenScreenshotPaths.Add(screenshotPath))
                    throw Validation(
                        "raid-screenshot-path-invalid",
                        "레이드 메모 스크린샷 연결 경로가 올바르지 않거나 중복됩니다.");
            }
            if (clone.CreatedUtc > clone.UpdatedUtc)
                throw Validation(
                    "raid-timestamp-order-invalid",
                    "레이드 메모 생성 시각이 수정 시각보다 늦습니다.");
            return clone;
        }

        private static UserReportMemoRecord CloneAndValidateUserReportMemo(
            UserReportMemoRecord source)
        {
            if (source == null)
                throw Validation("report-null", "유저신고 메모 항목의 형식이 올바르지 않습니다.");
            UserReportMemoRecord clone = CloneUserReportMemo(source);
            clone.Key = RequireKey(clone.Key, "report-key-invalid", "유저신고 메모 키가 올바르지 않습니다.");
            if (clone.ReportCount < 1 || clone.ReportCount > UserReportMemoStore.MaximumReportCount)
                throw Validation("report-count-invalid", "유저신고 메모의 신고 수가 올바르지 않습니다.");
            if (clone.Entries.Count > UserReportMemoStore.MaximumReportCount)
                throw Validation("report-entry-limit", "유저신고 메모의 신고 항목 수가 한도를 초과했습니다.");
            foreach (UserReportMemoEntry entry in clone.Entries)
            {
                entry.Nickname = RequireText(
                    entry.Nickname,
                    UserReportMemoStore.MaximumNicknameLength,
                    "report-nickname-invalid",
                    "유저신고 메모의 닉네임이 너무 깁니다.");
                entry.Reason = RequireText(
                    entry.Reason,
                    UserReportMemoStore.MaximumReasonLength,
                    "report-reason-invalid",
                    "유저신고 메모의 신고 사유가 너무 깁니다.");
            }
            clone.MemoText = RequireText(
                clone.MemoText,
                UserReportMemoStore.MaximumMemoLength,
                "report-memo-invalid",
                "유저신고 메모 본문이 너무 깁니다.");
            clone.Game = RequireText(clone.Game, MaximumGameLength, "report-game-invalid", "유저신고 메모 게임 값이 너무 깁니다.");
            clone.GameType = RequireText(
                clone.GameType,
                MaximumGameTypeLength,
                "report-game-type-invalid",
                "유저신고 메모 게임유형 값이 너무 깁니다.");
            clone.MapName = RequireText(clone.MapName, MaximumMapLength, "report-map-invalid", "유저신고 메모 맵 값이 너무 깁니다.");
            clone.RaidStartedUtc = RequireUtc(clone.RaidStartedUtc, true, "유저신고 메모 레이드 시작 시각");
            clone.CreatedUtc = RequireUtc(clone.CreatedUtc, false, "유저신고 메모 생성 시각");
            clone.UpdatedUtc = RequireUtc(clone.UpdatedUtc, false, "유저신고 메모 수정 시각");
            if (clone.CreatedUtc > clone.UpdatedUtc)
                throw Validation(
                    "report-timestamp-order-invalid",
                    "유저신고 메모 생성 시각이 수정 시각보다 늦습니다.");
            return clone;
        }

        private static RaidNoteRecord CloneRaidNote(RaidNoteRecord source)
        {
            if (source == null) return null;
            return new RaidNoteRecord
            {
                Key = source.Key,
                Game = source.Game ?? string.Empty,
                GameType = source.GameType ?? string.Empty,
                RaidStartedUtc = source.RaidStartedUtc,
                MapName = source.MapName ?? string.Empty,
                NoteText = RaidNoteStore.NormalizeLegacyNoteText(source.NoteText),
                ScreenshotPaths = source.ScreenshotPaths == null
                    ? new List<string>()
                    : new List<string>(source.ScreenshotPaths),
                Tags = source.Tags == null ? new List<string>() : new List<string>(source.Tags),
                CreatedUtc = source.CreatedUtc,
                UpdatedUtc = source.UpdatedUtc
            };
        }

        private static UserReportMemoRecord CloneUserReportMemo(UserReportMemoRecord source)
        {
            if (source == null) return null;
            var entries = new List<UserReportMemoEntry>();
            foreach (UserReportMemoEntry entry in source.Entries ?? new List<UserReportMemoEntry>())
            {
                entries.Add(new UserReportMemoEntry
                {
                    Nickname = entry == null ? string.Empty : (entry.Nickname ?? string.Empty),
                    Reason = entry == null ? string.Empty : (entry.Reason ?? string.Empty)
                });
            }
            return new UserReportMemoRecord
            {
                Key = source.Key,
                ReportCount = source.ReportCount,
                Entries = entries,
                MemoText = source.MemoText ?? string.Empty,
                Game = source.Game ?? string.Empty,
                GameType = source.GameType ?? string.Empty,
                RaidStartedUtc = source.RaidStartedUtc,
                MapName = source.MapName ?? string.Empty,
                CreatedUtc = source.CreatedUtc,
                UpdatedUtc = source.UpdatedUtc
            };
        }

        private static bool RaidNotesEquivalentForRestore(
            RaidNoteRecord expected,
            RaidNoteRecord actual)
        {
            if (expected == null || actual == null) return false;
            return string.Equals(expected.Key, actual.Key, StringComparison.OrdinalIgnoreCase)
                && string.Equals(expected.Game ?? string.Empty, actual.Game ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(expected.GameType ?? string.Empty, actual.GameType ?? string.Empty, StringComparison.Ordinal)
                && expected.RaidStartedUtc == actual.RaidStartedUtc
                && string.Equals(expected.MapName ?? string.Empty, actual.MapName ?? string.Empty, StringComparison.Ordinal)
                && string.Equals(expected.NoteText ?? string.Empty, actual.NoteText ?? string.Empty, StringComparison.Ordinal)
                && expected.CreatedUtc == actual.CreatedUtc
                && expected.UpdatedUtc == actual.UpdatedUtc
                && SequenceEqual(expected.ScreenshotPaths, actual.ScreenshotPaths)
                && SequenceEqual(expected.Tags, actual.Tags);
        }

        private static bool UserReportMemosEquivalentForRestore(
            UserReportMemoRecord expected,
            UserReportMemoRecord actual)
        {
            if (expected == null || actual == null) return false;
            if (!string.Equals(expected.Key, actual.Key, StringComparison.OrdinalIgnoreCase)
                || expected.ReportCount != actual.ReportCount
                || !string.Equals(expected.MemoText ?? string.Empty, actual.MemoText ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(expected.Game ?? string.Empty, actual.Game ?? string.Empty, StringComparison.Ordinal)
                || !string.Equals(expected.GameType ?? string.Empty, actual.GameType ?? string.Empty, StringComparison.Ordinal)
                || expected.RaidStartedUtc != actual.RaidStartedUtc
                || !string.Equals(expected.MapName ?? string.Empty, actual.MapName ?? string.Empty, StringComparison.Ordinal)
                || expected.CreatedUtc != actual.CreatedUtc
                || expected.UpdatedUtc != actual.UpdatedUtc)
                return false;
            IList<UserReportMemoEntry> expectedEntries = expected.Entries ?? new List<UserReportMemoEntry>();
            IList<UserReportMemoEntry> actualEntries = actual.Entries ?? new List<UserReportMemoEntry>();
            if (expectedEntries.Count != actualEntries.Count) return false;
            for (int index = 0; index < expectedEntries.Count; index++)
            {
                UserReportMemoEntry left = expectedEntries[index] ?? new UserReportMemoEntry();
                UserReportMemoEntry right = actualEntries[index] ?? new UserReportMemoEntry();
                if (!string.Equals(left.Nickname ?? string.Empty, right.Nickname ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(left.Reason ?? string.Empty, right.Reason ?? string.Empty, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static bool SequenceEqual(IList<string> left, IList<string> right)
        {
            left = left ?? new List<string>();
            right = right ?? new List<string>();
            return left.Count == right.Count
                && left.SequenceEqual(right, StringComparer.Ordinal);
        }

        private static string CreatePreview(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "(내용 없음)";
            var builder = new StringBuilder();
            bool pendingSpace = false;
            for (int index = 0; index < value.Length && builder.Length < MaximumPreviewLength; index++)
            {
                char character = value[index];
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, index);
                if (category == UnicodeCategory.Format)
                {
                    if (char.IsHighSurrogate(character)
                        && index + 1 < value.Length
                        && char.IsLowSurrogate(value[index + 1])) index++;
                    continue;
                }
                if (char.IsWhiteSpace(character) || char.IsControl(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace && builder.Length < MaximumPreviewLength)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) continue;
                    if (builder.Length + 2 > MaximumPreviewLength) break;
                    builder.Append(character).Append(value[++index]);
                }
                else if (!char.IsLowSurrogate(character))
                {
                    builder.Append(character);
                }
            }
            return builder.Length == 0 ? "(내용 없음)" : builder.ToString();
        }

        private static string SafeItemLabel(MemoArchiveBackupParsedItem item)
        {
            string kind = item.Kind == MemoArchiveBackupKind.RaidNote
                ? "레이드 메모 "
                : "유저신고 메모 ";
            return kind + item.SourceIndex.ToString(CultureInfo.CurrentCulture) + "번";
        }

        private static DateTime GetUpdatedUtc(MemoArchiveBackupParsedItem item)
        {
            return item.Kind == MemoArchiveBackupKind.RaidNote
                ? item.RaidNote.UpdatedUtc
                : item.UserReportMemo.UpdatedUtc;
        }

        private static string RequireText(
            string value,
            int maximumLength,
            string code,
            string message)
        {
            value = value ?? string.Empty;
            if (value.Length > maximumLength || !HasValidUnicode(value))
                throw Validation(code, message);
            return value;
        }

        private static string RequireKey(string value, string code, string message)
        {
            if (!IsValidKey(value)) throw Validation(code, message);
            return value.ToLowerInvariant();
        }

        private static DateTime RequireUtc(DateTime value, bool allowDefault, string label)
        {
            if (!allowDefault && value == default(DateTime))
                throw Validation("utc-invalid", label + "이 올바르지 않습니다.");
            if (value == default(DateTime)) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            DateTime utc;
            if (value.Kind == DateTimeKind.Utc) utc = value;
            else if (value.Kind == DateTimeKind.Local) utc = value.ToUniversalTime();
            else utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return NormalizeStoreTimestampPrecision(utc);
        }

        private static DateTime NormalizeStoreTimestampPrecision(DateTime value)
        {
            long ticks = value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond);
            return new DateTime(ticks, DateTimeKind.Utc);
        }

        private static string FormatUtc(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc
                ? value
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return utc.ToString("o", CultureInfo.InvariantCulture);
        }

        private static void EnsureUniqueKeys(IEnumerable<string> keys, string kind)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (keys.Any(key => !seen.Add(key)))
                throw Validation("duplicate-key", kind + " 키가 같은 종류 안에서 중복됩니다.");
        }

        private static bool IsValidKey(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length == 64
                && value.All(character => (character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'));
        }

        private static bool HasValidUnicode(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        return false;
                    index++;
                }
                else if (char.IsLowSurrogate(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasExactFields(
            IDictionary<string, object> source,
            ISet<string> expected)
        {
            return source.Count == expected.Count && source.Keys.All(expected.Contains);
        }

        private static HashSet<string> CreateFieldSet(params string[] fields)
        {
            return new HashSet<string>(fields, StringComparer.Ordinal);
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = MaximumFileBytes,
                RecursionLimit = MaximumNestingDepth
            };
        }

        private static MemoArchiveBackupExportResult ExportFailure(string code, string message)
        {
            return new MemoArchiveBackupExportResult
            {
                SafeErrorCode = code,
                ErrorMessage = message
            };
        }

        private static MemoArchiveBackupParseResult ParseFailure(string code, string message)
        {
            return new MemoArchiveBackupParseResult
            {
                SafeErrorCode = code,
                ErrorMessage = message
            };
        }

        private static MemoArchiveValidationException Validation(string code, string message)
        {
            return new MemoArchiveValidationException(code, message);
        }

        private sealed class MemoArchiveValidationException : Exception
        {
            internal MemoArchiveValidationException(string safeErrorCode, string message)
                : base(message)
            {
                SafeErrorCode = safeErrorCode;
            }

            internal string SafeErrorCode { get; private set; }
        }

        private sealed class JsonMemberUniquenessReader
        {
            private readonly string _json;
            private readonly int _maximumDepth;
            private int _index;

            internal JsonMemberUniquenessReader(string json, int maximumDepth)
            {
                _json = json ?? string.Empty;
                _maximumDepth = maximumDepth;
            }

            internal bool DuplicateMemberFound { get; private set; }

            internal bool ReadDocument()
            {
                SkipWhitespace();
                if (!ReadValue(0)) return false;
                SkipWhitespace();
                return _index == _json.Length;
            }

            private bool ReadValue(int depth)
            {
                if (depth > _maximumDepth) return false;
                SkipWhitespace();
                if (_index >= _json.Length) return false;
                char token = _json[_index];
                if (token == '{') return ReadObject(depth + 1);
                if (token == '[') return ReadArray(depth + 1);
                if (token == '"')
                {
                    string ignored;
                    return TryReadString(out ignored) && HasValidUnicode(ignored);
                }
                if (token == 't') return TryReadLiteral("true");
                if (token == 'f') return TryReadLiteral("false");
                if (token == 'n') return TryReadLiteral("null");
                return token == '-' || IsDigit(token) ? ReadNumber() : false;
            }

            private bool ReadObject(int depth)
            {
                if (depth > _maximumDepth || !TryReadCharacter('{')) return false;
                SkipWhitespace();
                if (TryReadCharacter('}')) return true;
                var members = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    string member;
                    if (!TryReadString(out member) || !HasValidUnicode(member)) return false;
                    if (!members.Add(member))
                    {
                        DuplicateMemberFound = true;
                        return false;
                    }
                    SkipWhitespace();
                    if (!TryReadCharacter(':') || !ReadValue(depth)) return false;
                    SkipWhitespace();
                    if (TryReadCharacter('}')) return true;
                    if (!TryReadCharacter(',')) return false;
                    SkipWhitespace();
                }
            }

            private bool ReadArray(int depth)
            {
                if (depth > _maximumDepth || !TryReadCharacter('[')) return false;
                SkipWhitespace();
                if (TryReadCharacter(']')) return true;
                while (true)
                {
                    if (!ReadValue(depth)) return false;
                    SkipWhitespace();
                    if (TryReadCharacter(']')) return true;
                    if (!TryReadCharacter(',')) return false;
                    SkipWhitespace();
                }
            }

            private bool TryReadString(out string value)
            {
                value = null;
                if (!TryReadCharacter('"')) return false;
                var builder = new StringBuilder();
                while (_index < _json.Length)
                {
                    char character = _json[_index++];
                    if (character == '"')
                    {
                        value = builder.ToString();
                        return true;
                    }
                    if (character < 0x20) return false;
                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }
                    if (_index >= _json.Length) return false;
                    char escape = _json[_index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            int codePoint = 0;
                            for (int offset = 0; offset < 4; offset++)
                            {
                                if (_index >= _json.Length) return false;
                                int digit = HexValue(_json[_index++]);
                                if (digit < 0) return false;
                                codePoint = (codePoint << 4) | digit;
                            }
                            builder.Append((char)codePoint);
                            break;
                        default:
                            return false;
                    }
                }
                return false;
            }

            private bool ReadNumber()
            {
                if (TryReadCharacter('-') && _index >= _json.Length) return false;
                if (_index >= _json.Length) return false;
                if (_json[_index] == '0') _index++;
                else
                {
                    if (_json[_index] < '1' || _json[_index] > '9') return false;
                    while (_index < _json.Length && IsDigit(_json[_index])) _index++;
                }
                if (_index < _json.Length && _json[_index] == '.')
                {
                    _index++;
                    if (_index >= _json.Length || !IsDigit(_json[_index])) return false;
                    while (_index < _json.Length && IsDigit(_json[_index])) _index++;
                }
                if (_index < _json.Length && (_json[_index] == 'e' || _json[_index] == 'E'))
                {
                    _index++;
                    if (_index < _json.Length && (_json[_index] == '+' || _json[_index] == '-'))
                        _index++;
                    if (_index >= _json.Length || !IsDigit(_json[_index])) return false;
                    while (_index < _json.Length && IsDigit(_json[_index])) _index++;
                }
                return true;
            }

            private bool TryReadLiteral(string literal)
            {
                if (_index + literal.Length > _json.Length) return false;
                for (int offset = 0; offset < literal.Length; offset++)
                {
                    if (_json[_index + offset] != literal[offset]) return false;
                }
                _index += literal.Length;
                return true;
            }

            private bool TryReadCharacter(char expected)
            {
                if (_index >= _json.Length || _json[_index] != expected) return false;
                _index++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_index < _json.Length)
                {
                    char character = _json[_index];
                    if (character != ' ' && character != '\t'
                        && character != '\r' && character != '\n') return;
                    _index++;
                }
            }

            private static bool IsDigit(char character)
            {
                return character >= '0' && character <= '9';
            }

            private static int HexValue(char character)
            {
                if (character >= '0' && character <= '9') return character - '0';
                if (character >= 'a' && character <= 'f') return character - 'a' + 10;
                if (character >= 'A' && character <= 'F') return character - 'A' + 10;
                return -1;
            }
        }
    }
}
