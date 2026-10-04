// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TarkovServerReporter.Tests
{
    internal static class OperationNoteStoreTests
    {
        private static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "TSG-OperationNoteTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                TestDurableDraft(root);
                TestImmutableSnapshotAndLegacy(root);
                TestInterruptedArchiveExport(root);
                TestRaidBinding(root);
                TestScreenshotEvidence(root);
                Console.WriteLine("OperationNoteStoreTests: PASS (durability, conflict, immutable snapshots, retry, legacy media, raid identity, screenshot evidence)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("OperationNoteStoreTests: FAIL\n" + exception);
                return 1;
            }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        private static void TestDurableDraft(string root)
        {
            var store = CreateStore(root, "draft");
            var draft = store.LoadDraft();
            draft.BodyText = "다음 레이드에도 유지할 초안";
            draft.Checks.Add(new OperationNoteCheck { Text = "정수 필터", Done = true });
            draft.Pins.Add(new OperationPinnedNote { Title = "반복 준비", BodyText = "열쇠와 식수", Checks = new List<OperationNoteCheck> { new OperationNoteCheck { Text = "음식" } } });
            store.SaveDraft(draft);
            var restarted = CreateStore(root, "draft");
            var read = restarted.LoadDraft();
            Assert(read.BodyText == draft.BodyText && read.Checks[0].Done && read.Pins[0].Checks[0].Text == "음식", "draft/checks/pins must survive process restart");
            OperationNoteDraft stale = restarted.LoadDraft();
            draft.BodyText = "second";
            store.SaveDraft(draft);
            Expect<IOException>(delegate { restarted.SaveDraft(stale); }, "stale draft must not overwrite newer content");
            string goodBackup = File.ReadAllText(store.DraftPath + ".bak");
            File.WriteAllText(store.DraftPath, "{corrupt");
            OperationNoteDraft recovered = restarted.LoadDraft();
            Assert(recovered.BodyText == "다음 레이드에도 유지할 초안", "corrupt primary must recover backup");
            recovered.BodyText = "recovered edit";
            restarted.SaveDraft(recovered);
            Assert(File.ReadAllText(store.DraftPath + ".bak") == goodBackup, "repair must not replace good fallback with corrupt primary");
            File.WriteAllText(store.DraftPath, "{bad1");
            File.WriteAllText(store.DraftPath + ".bak", "{bad2");
            Expect<InvalidDataException>(delegate { restarted.LoadDraft(); }, "unreadable draft must not silently become empty");
            Expect<InvalidDataException>(delegate { restarted.SaveDraft(recovered); }, "unreadable stores must not be overwritten");
            Assert(File.ReadAllText(store.DraftPath) == "{bad1", "unreadable original preserved");
        }

        private static void TestImmutableSnapshotAndLegacy(string root)
        {
            var legacy = new RaidNoteStore(Path.Combine(root, "immutable-legacy"));
            var store = new OperationNoteStore(Path.Combine(root, "immutable"), legacy);
            ServerSession raid = CreateRaid("immutable", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
            RaidNoteRecord prior = legacy.CreateFor(raid);
            prior.NoteText = "기존 레이드 메모";
            prior.ScreenshotPaths.Add(@"C:\Old media\legacy.mp4");
            prior.Tags.Add("기존 태그");
            prior.Tags.Add("작전노트 저장본");
            legacy.Save(raid, prior);
            OperationNoteDraft draft = store.LoadDraft();
            draft.BodyText = "당시 내용";
            draft.ObservedRaidKey = RaidNoteStore.CreateStableKey(raid);
            draft.Checks.Add(new OperationNoteCheck { Text = "진입 확인", Done = true });
            store.SaveDraft(draft);
            OperationNoteDraft clickTimeCopy = store.CloneDraft(draft);
            draft.BodyText = "저장을 누른 뒤 이어쓴 내용";
            store.SaveDraft(draft);
            OperationNoteSaveResult first = store.SaveToRaid(clickTimeCopy, raid, new[] { raid }, new string[0]);
            Assert(store.LoadDraft().BodyText == "저장을 누른 뒤 이어쓴 내용", "background snapshot save must not overwrite typing after click");
            Assert(first.Saved && store.IsRaidSaved(raid), "first one-action save has archive receipt");
            string snapshotPath = Path.Combine(store.StorageFolder, "snapshots", first.RaidKey + ".json");
            string originalBytes = File.ReadAllText(snapshotPath);
            draft.BodyText = "다음 레이드 내용";
            draft.Checks[0].Done = false;
            store.SaveDraft(draft);
            raid.IpAddress = "127.0.0.99";
            raid.ServerId = "changed server identity";
            OperationNoteSaveResult again = store.SaveToRaid(draft, raid, new[] { raid }, new string[0]);
            Assert(again.AlreadySaved && File.ReadAllText(snapshotPath) == originalBytes, "continuation and repeated save must not alter historical snapshot");
            RaidNoteRecord archived = legacy.Load(raid);
            Assert(!string.IsNullOrEmpty(archived.OperationSnapshotId), "user-editable generic tag must never act as snapshot provenance");
            Assert(archived.NoteText.Contains("기존 레이드 메모") && archived.NoteText.Contains("[x] 진입 확인")
                && !archived.NoteText.Contains("다음 레이드 내용"), "legacy viewer shows original memo plus immutable rendered checks");
            Assert(archived.ScreenshotPaths.Contains(@"C:\Old media\legacy.mp4") && archived.Tags.Contains("기존 태그"), "legacy attachments including videos and tags are preserved");
            File.Delete(snapshotPath + ".receipt");
            string archivedText = archived.NoteText;
            store.SaveToRaid(draft, raid, new[] { raid }, new string[0]);
            Assert(legacy.Load(raid).NoteText == archivedText, "interrupted receipt retry must not append duplicate text");
            archived = legacy.Load(raid);
            archived.NoteText = "사용자가 기록에서 직접 수정";
            legacy.Save(raid, archived);
            store.SaveToRaid(draft, raid, new[] { raid }, new string[0]);
            Assert(legacy.Load(raid).NoteText == "사용자가 기록에서 직접 수정", "receipt protects later explicit archive edits");
            Assert(store.ReadSnapshot(raid).BodyText == "당시 내용" && store.LoadDraft().BodyText == "다음 레이드 내용", "snapshot and draft are separate values");
        }

        private static void TestInterruptedArchiveExport(string root)
        {
            string legacyFolder = Path.Combine(root, "retry-legacy");
            var legacy = new RaidNoteStore(legacyFolder);
            var store = new OperationNoteStore(Path.Combine(root, "retry"), legacy);
            ServerSession raid = CreateRaid("retry", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
            string key = RaidNoteStore.CreateStableKey(raid);
            Directory.CreateDirectory(legacyFolder);
            string legacyPath = Path.Combine(legacyFolder, key + ".json");
            File.WriteAllText(legacyPath, "{corrupt legacy");
            var draft = store.LoadDraft();
            draft.BodyText = "failed export original";
            draft.ObservedRaidKey = key;
            Expect<InvalidDataException>(delegate { store.SaveToRaid(draft, raid, new[] { raid }, new string[0]); }, "legacy corruption must block destructive append");
            Assert(!store.IsRaidSaved(raid) && store.ReadSnapshot(raid).BodyText == draft.BodyText, "snapshot remains durable and export retry remains available");
            draft.BodyText = "continued while export unavailable";
            store.SaveDraft(draft);
            File.Delete(legacyPath);
            store.SaveToRaid(draft, raid, new[] { raid }, new string[0]);
            Assert(legacy.Load(raid).NoteText.Contains("failed export original") && !legacy.Load(raid).NoteText.Contains("continued while"), "retry exports original snapshot, never current continued text");
        }

        private static void TestRaidBinding(string root)
        {
            var store = CreateStore(root, "binding");
            var raid = CreateRaid("binding", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));
            var draft = store.LoadDraft();
            draft.BodyText = "unbound draft";
            Expect<InvalidOperationException>(delegate { store.SaveToRaid(draft, raid, new[] { raid }, new string[0]); }, "history-only raid must not silently bind current draft");
            draft.ObservedRaidKey = RaidNoteStore.CreateStableKey(raid);
            raid.OperationState = RaidOperationState.InProgress;
            Expect<InvalidOperationException>(delegate { store.SaveToRaid(draft, raid, new[] { raid }, new string[0]); }, "live raid cannot be saved as ended");
            raid.OperationState = RaidOperationState.Completed;
            var next = CreateRaid("next", DateTime.UtcNow.AddMinutes(-30), DateTime.UtcNow.AddMinutes(-2));
            Expect<InvalidOperationException>(delegate { store.SaveToRaid(draft, raid, new[] { raid, next }, new string[0]); }, "newer raid prevents old draft association");
            raid.SessionFolderName = null;
            Expect<InvalidOperationException>(delegate { store.SaveToRaid(draft, raid, new[] { raid }, new string[0]); }, "weak identity cannot automatically bind raid");
        }

        private static void TestScreenshotEvidence(string root)
        {
            string folder = Path.Combine(root, "Escape From Tarkov");
            Directory.CreateDirectory(folder);
            DateTime start = DateTime.UtcNow.AddHours(-3).AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond));
            ServerSession raid = CreateRaid("photos", start, start.AddMinutes(30));
            string matched = WriteCapture(folder, start.AddMinutes(10));
            string boundary = WriteCapture(folder, start);
            string outside = WriteCapture(folder, start.AddMinutes(40));
            string ambiguous = Path.Combine(folder, "Screenshot.png");
            File.WriteAllBytes(ambiguous, new byte[] { 1, 2, 3 });
            File.SetLastWriteTimeUtc(ambiguous, start.AddMinutes(10));
            var linker = new OperationScreenshotLinker();
            IList<string> found = linker.FindForRaid(raid, new[] { raid }, new[] { folder });
            Assert(found.Count == 1 && found[0] == matched, "only timestamp-backed uniquely in-raid screenshot links; boundary/outside/mtime-only excluded");
            var overlap = CreateRaid("overlap", start.AddMinutes(5), start.AddMinutes(15));
            Assert(linker.FindForRaid(raid, new[] { raid, overlap }, new[] { folder }).Count == 0, "overlapping raid intervals must not force image association");
            DateTime captured;
            Assert(OperationScreenshotLinker.TryGetCaptureUtc(matched, out captured) && Math.Abs((captured - start.AddMinutes(10)).TotalSeconds) < 1, "NVIDIA local filename maps to UTC");
            Assert(!OperationScreenshotLinker.TryGetCaptureUtc(ambiguous, out captured), "filesystem timestamps alone are insufficient");
            string hdr = Path.Combine(folder, "Escape From Tarkov Screenshot " + start.AddMinutes(12).ToLocalTime().ToString("yyyy.MM.dd - HH.mm.ss", CultureInfo.InvariantCulture) + ".24.jxr");
            File.WriteAllBytes(hdr, new byte[] { 73, 73, 188, 1 });
            Assert(OperationScreenshotLinker.TryGetCaptureUtc(hdr, out captured)
                && RaidNoteStore.IsSafeScreenshotAttachmentPath(hdr), "NVIDIA HDR JPEG XR capture time and legacy attachment validation are supported");
            File.Delete(hdr);
            string steamFolder = Path.Combine(root, "760", "remote", "3932890", "screenshots");
            Directory.CreateDirectory(steamFolder);
            string steam = Path.Combine(steamFolder, start.AddMinutes(13).ToLocalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_1.jpg");
            using (var bitmap = new System.Drawing.Bitmap(2, 2))
                bitmap.Save(steam, System.Drawing.Imaging.ImageFormat.Jpeg);
            Assert(linker.FindForRaid(raid, new[] { raid }, new[] { steamFolder }).Single() == steam,
                "user-added Steam app screenshot folder supports compact capture filenames without guessing filesystem time");
            Assert(File.Exists(matched) && File.Exists(boundary) && File.Exists(outside) && File.Exists(ambiguous), "auto linking never moves or deletes originals");
            var legacy = new RaidNoteStore(Path.Combine(root, "photo-legacy"));
            var store = new OperationNoteStore(Path.Combine(root, "photo-store"), legacy);
            var draft = store.LoadDraft();
            draft.BodyText = "auto attach";
            draft.ObservedRaidKey = RaidNoteStore.CreateStableKey(raid);
            OperationNoteSaveResult result = store.SaveToRaid(draft, raid, new[] { raid }, new[] { folder, Path.Combine(root, "missing") });
            Assert(result.ScreenshotCount == 1 && legacy.Load(raid).ScreenshotPaths.Contains(matched), "one action saves note and links evidence-backed screenshot for existing viewer");
        }

        private static string WriteCapture(string folder, DateTime capturedUtc)
        {
            string path = Path.Combine(folder, "Escape From Tarkov " + capturedUtc.ToLocalTime().ToString("yyyy.MM.dd - HH.mm.ss", CultureInfo.InvariantCulture) + ".00.png");
            File.WriteAllBytes(path, new byte[] { 137, 80, 78, 71 });
            return path;
        }
        private static OperationNoteStore CreateStore(string root, string name)
        {
            return new OperationNoteStore(Path.Combine(root, name), new RaidNoteStore(Path.Combine(root, name + "-legacy")));
        }
        private static ServerSession CreateRaid(string name, DateTime startedUtc, DateTime endedUtc)
        {
            return new ServerSession { Game = TarkovGame.Eft, SessionFolderName = "log_" + name,
                SessionStarted = startedUtc.AddMinutes(-2), OperationStartedAt = startedUtc,
                OperationEndedAt = endedUtc, OperationState = RaidOperationState.Completed,
                LastUpdated = endedUtc, MapName = "Customs" };
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Expect<T>(Action action, string message) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new InvalidOperationException(message);
        }
    }
}
