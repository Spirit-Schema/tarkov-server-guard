// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    /// <summary>
    /// One durable working draft and one create-only snapshot per observed raid.
    /// Snapshots are written before the legacy archive append, whose receipt can be
    /// retried after an interruption without replacing the original snapshot.
    /// </summary>
    public sealed class OperationNoteStore
    {
        public const int MaximumBodyLength = 100000;
        public const int MaximumCheckCount = 300;
        public const int MaximumPinCount = 50;
        private const int MaximumJsonLength = 8 * 1024 * 1024;
        private readonly object _sync = new object();
        private readonly object _snapshotSync = new object();
        private readonly string _folder;
        private readonly RaidNoteStore _legacy;

        public OperationNoteStore()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard", "OperationNotes"), new RaidNoteStore()) { }

        public OperationNoteStore(string folder, RaidNoteStore legacy)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("작전노트 저장 폴더가 비어 있습니다.", "folder");
            if (legacy == null) throw new ArgumentNullException("legacy");
            _folder = Path.GetFullPath(folder);
            _legacy = legacy;
        }

        public string StorageFolder { get { return _folder; } }
        public string DraftPath { get { return Path.Combine(_folder, "current.json"); } }

        public OperationNoteDraft LoadDraft()
        {
            lock (_sync) return LoadDraftCore();
        }

        public OperationNoteDraft CloneDraft(OperationNoteDraft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            return CopyDraft(draft);
        }

        public void SaveDraft(OperationNoteDraft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            lock (_sync)
            using (MemoStoreWriteLease.Acquire(DraftPath))
            {
                OperationNoteDraft current = LoadDraftCore();
                if (current.Revision != draft.Revision)
                    throw new IOException("다른 창에서 초안이 변경되었습니다. 현재 입력은 유지되며 기존 초안을 덮어쓰지 않았습니다.");
                OperationNoteDraft copy = CopyDraft(draft);
                if (copy.Revision == long.MaxValue) throw new InvalidOperationException("초안 변경 번호 한도를 초과했습니다.");
                copy.Revision++;
                copy.UpdatedUtc = DateTime.UtcNow;
                WriteAtomic(DraftPath, Serialize(copy), IsReadableDraft(DraftPath));
                draft.Revision = copy.Revision;
                draft.UpdatedUtc = copy.UpdatedUtc;
            }
        }

        public bool IsRaidSaved(ServerSession session)
        {
            if (session == null) return false;
            string key = RaidNoteStore.CreateStableKey(session);
            string path = SnapshotPath(key);
            // An immutable snapshot awaiting its archive receipt remains retryable.
            return File.Exists(path + ".receipt") && ReadSnapshotCore(key) != null;
        }

        public OperationNoteSnapshot ReadSnapshot(ServerSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            return ReadSnapshotCore(RaidNoteStore.CreateStableKey(session));
        }

        // Caller flushes its current draft, then CloneDraft captures the click-time
        // content. This method never writes the live draft: editing can continue while
        // an asynchronous log refresh and screenshot scan save the frozen copy.
        public OperationNoteSaveResult SaveToRaid(OperationNoteDraft draft, ServerSession endedRaid,
            IEnumerable<ServerSession> knownRaids, IEnumerable<string> screenshotFolders)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            if (endedRaid == null) throw new ArgumentNullException("endedRaid");
            string key = RaidNoteStore.CreateStableKey(endedRaid);
            if (!string.Equals(draft.ObservedRaidKey, key, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("이 초안에서 확인한 레이드가 아니어서 저장하지 않았습니다.");
            DateTime startedUtc, endedUtc;
            if (!TryGetCompletedInterval(endedRaid, out startedUtc, out endedUtc))
                throw new InvalidOperationException("실제 시작·종료 시각이 확인된 레이드에만 저장할 수 있습니다.");
            List<ServerSession> sessions = (knownRaids ?? new[] { endedRaid }).Where(item => item != null).ToList();
            foreach (ServerSession other in sessions)
            {
                if (string.Equals(RaidNoteStore.CreateStableKey(other), key, StringComparison.OrdinalIgnoreCase)) continue;
                if (other.Game == endedRaid.Game && other.OperationStartedAt.HasValue
                    && ToUtc(other.OperationStartedAt.Value) > startedUtc)
                    throw new InvalidOperationException("다음 레이드가 확인되어 이전 레이드에 현재 초안을 연결하지 않았습니다.");
            }
            if (!sessions.Any(item => string.Equals(RaidNoteStore.CreateStableKey(item), key, StringComparison.OrdinalIgnoreCase)))
                sessions.Add(endedRaid);

            lock (_snapshotSync)
            using (MemoStoreWriteLease.Acquire(SnapshotPath(key)))
            {
                OperationNoteSnapshot snapshot = ReadSnapshotCore(key);
                bool existed = snapshot != null;
                if (snapshot == null)
                {
                    OperationNoteDraft copy = CopyDraft(draft);
                    if (string.IsNullOrWhiteSpace(copy.BodyText)
                        && !copy.Checks.Any(check => !string.IsNullOrWhiteSpace(check.Text)))
                        throw new InvalidOperationException("본문이나 체크 항목을 작성한 뒤 저장해 주세요.");
                    var pictures = new OperationScreenshotLinker().FindForRaid(endedRaid, sessions,
                        screenshotFolders ?? OperationScreenshotLinker.DiscoverDefaultFolders());
                    snapshot = new OperationNoteSnapshot
                    {
                        SchemaVersion = 1,
                        RaidKey = key,
                        BodyText = copy.BodyText,
                        Checks = copy.Checks,
                        ScreenshotPaths = pictures.ToList(),
                        SavedUtc = DateTime.UtcNow,
                        RaidStartedUtc = startedUtc,
                        RaidEndedUtc = endedUtc,
                        MapName = endedRaid.MapName ?? string.Empty,
                        DraftRevision = copy.Revision
                    };
                    WriteCreateOnly(SnapshotPath(key), Serialize(snapshot));
                }
                string receiptPath = SnapshotPath(key) + ".receipt";
                if (!File.Exists(receiptPath))
                {
                    _legacy.AppendOperationSnapshot(endedRaid, snapshot.ToNoteText(),
                        snapshot.ScreenshotPaths, snapshot.SavedUtc);
                    WriteCreateOnly(receiptPath, key);
                }
                return new OperationNoteSaveResult
                {
                    Saved = !existed,
                    AlreadySaved = existed,
                    RaidKey = key,
                    ScreenshotCount = snapshot.ScreenshotPaths.Count,
                    Message = existed ? "이 레이드의 저장본을 유지했습니다. 현재 내용은 계속 작성할 수 있습니다."
                        : "레이드에 저장했습니다. 현재 내용을 이어서 작성하세요."
                };
            }
        }

        internal static bool TryGetCompletedInterval(ServerSession session, out DateTime startedUtc, out DateTime endedUtc)
        {
            startedUtc = endedUtc = default(DateTime);
            if (session == null || session.Game != TarkovGame.Eft
                || session.OperationState != RaidOperationState.Completed
                || !session.OperationStartedAt.HasValue || !session.OperationEndedAt.HasValue
                || string.IsNullOrWhiteSpace(session.SessionFolderName)
                || session.SessionStarted == default(DateTime)) return false;
            startedUtc = ToUtc(session.OperationStartedAt.Value);
            endedUtc = ToUtc(session.OperationEndedAt.Value);
            // A huge, future or reversed interval is insufficient evidence for an automatic link.
            return startedUtc != default(DateTime) && endedUtc > startedUtc
                && endedUtc - startedUtc <= TimeSpan.FromHours(3)
                && endedUtc <= DateTime.UtcNow.AddMinutes(1);
        }

        internal static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Unspecified) value = DateTime.SpecifyKind(value, DateTimeKind.Local);
            return value.ToUniversalTime();
        }

        private OperationNoteDraft LoadDraftCore()
        {
            OperationNoteDraft draft = TryReadDraft(DraftPath) ?? TryReadDraft(DraftPath + ".bak");
            if (draft != null) return draft;
            if (File.Exists(DraftPath) || File.Exists(DraftPath + ".bak"))
                throw new InvalidDataException("초안과 복구본을 읽지 못했습니다. 기존 파일을 보존했으며 빈 초안으로 덮어쓰지 않습니다.");
            return new OperationNoteDraft();
        }

        private bool IsReadableDraft(string path) { return TryReadDraft(path) != null; }

        private OperationNoteDraft TryReadDraft(string path)
        {
            try
            {
                string json = ReadBounded(path);
                if (json == null) return null;
                var draft = NewSerializer().Deserialize<OperationNoteDraft>(json);
                if (draft == null || draft.Revision < 1 || draft.UpdatedUtc == default(DateTime)) return null;
                return CopyDraft(draft);
            }
            catch { return null; }
        }

        private OperationNoteSnapshot ReadSnapshotCore(string key)
        {
            string path = SnapshotPath(key);
            string json = ReadBounded(path);
            if (json == null) return null;
            try
            {
                var snapshot = NewSerializer().Deserialize<OperationNoteSnapshot>(json);
                if (snapshot == null || snapshot.SchemaVersion != 1
                    || !string.Equals(snapshot.RaidKey, key, StringComparison.OrdinalIgnoreCase)
                    || snapshot.SavedUtc == default(DateTime) || snapshot.RaidEndedUtc <= snapshot.RaidStartedUtc
                    || snapshot.BodyText == null || snapshot.Checks == null || snapshot.ScreenshotPaths == null)
                    throw new InvalidDataException();
                ValidateContent(snapshot.BodyText, snapshot.Checks);
                return snapshot;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("이 레이드의 기존 작전노트 저장본을 읽지 못해 덮어쓰지 않았습니다.", exception);
            }
        }

        private string SnapshotPath(string key) { return Path.Combine(_folder, "snapshots", key + ".json"); }

        private OperationNoteDraft CopyDraft(OperationNoteDraft draft)
        {
            ValidateContent(draft.BodyText ?? string.Empty, draft.Checks);
            if (draft.Pins == null || draft.Pins.Count > MaximumPinCount)
                throw new InvalidDataException("고정 메모 수가 허용 범위를 벗어났습니다.");
            if (!ValidOpacity(draft.ActiveOpacity) || !ValidOpacity(draft.LockedOpacity))
                throw new InvalidDataException("투명도 값이 올바르지 않습니다.");
            if (!ValidCoordinate(draft.WindowLeft) || !ValidCoordinate(draft.WindowTop))
                throw new InvalidDataException("작전노트 창 위치가 올바르지 않습니다.");
            if (!string.IsNullOrEmpty(draft.ObservedRaidKey)
                && (draft.ObservedRaidKey.Length != 64 || draft.ObservedRaidKey.Any(character => !Uri.IsHexDigit(character))))
                throw new InvalidDataException("확인한 레이드의 식별자가 올바르지 않습니다.");
            var pinIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (OperationPinnedNote pin in draft.Pins)
            {
                if (pin == null || string.IsNullOrWhiteSpace(pin.Id) || pin.Id.Length > 64
                    || !pinIds.Add(pin.Id) || (pin.Title ?? string.Empty).Length > 200)
                    throw new InvalidDataException("고정 메모 정보가 올바르지 않습니다.");
                ValidateContent(pin.BodyText ?? string.Empty, pin.Checks);
            }
            return NewSerializer().Deserialize<OperationNoteDraft>(Serialize(draft));
        }

        private static void ValidateContent(string body, IList<OperationNoteCheck> checks)
        {
            if (body.Length > MaximumBodyLength || checks == null || checks.Count > MaximumCheckCount)
                throw new InvalidDataException("작전노트 내용이 허용 크기를 초과했습니다.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int total = body.Length;
            foreach (OperationNoteCheck check in checks)
            {
                if (check == null || string.IsNullOrWhiteSpace(check.Id) || check.Id.Length > 64
                    || !ids.Add(check.Id) || (check.Text ?? string.Empty).Length > 10000)
                    throw new InvalidDataException("체크 항목 정보가 올바르지 않습니다.");
                total += (check.Text ?? string.Empty).Length + 6;
            }
            if (total > 180000) throw new InvalidDataException("작전노트 전체 내용이 허용 크기를 초과했습니다.");
        }

        private static bool ValidOpacity(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.05 && value <= 1; }
        private static bool ValidCoordinate(double? value) { return !value.HasValue || (!double.IsNaN(value.Value) && !double.IsInfinity(value.Value) && Math.Abs(value.Value) <= 100000); }

        private string Serialize(object value)
        {
            string json = NewSerializer().Serialize(value);
            if (json.Length > MaximumJsonLength) throw new InvalidDataException("작전노트 저장 데이터가 허용 크기를 초과했습니다.");
            return json;
        }

        private static JavaScriptSerializer NewSerializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaximumJsonLength, RecursionLimit = 20 };
        }

        private static string ReadBounded(string path)
        {
            if (!File.Exists(path)) return null;
            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaximumJsonLength * 4L) throw new InvalidDataException("작전노트 저장 파일 크기가 올바르지 않습니다.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        private static void WriteCreateOnly(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { WriteDurably(staging, text); File.Move(staging, path); }
            finally { TryDelete(staging); }
        }

        private static void WriteAtomic(string path, string text, bool primaryReadable)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteDurably(staging, text);
                if (File.Exists(path)) File.Replace(staging, path, primaryReadable ? path + ".bak" : null, true);
                else File.Move(staging, path);
            }
            finally { TryDelete(staging); }
        }

        private static void WriteDurably(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }
}
