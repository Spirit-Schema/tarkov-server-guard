// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TarkovServerReporter.Tests
{
    internal static class MemoArchiveBackupTests
    {
        private static int _failures;

        private static int Main(string[] args)
        {
            if (args.Length == 4 && args[0] == "--raid-worker")
                return RunRaidWorker(args[1], args[2], args[3]);
            if (args.Length == 6 && args[0] == "--save-worker")
                return RunSaveWorker(args[1], args[2], args[3], args[4], args[5]);

            string root = Path.Combine(
                Path.GetTempPath(),
                "TarkovServerGuard-MemoArchiveBackupTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                TestFileNameAndDeterministicPrivacyRoundTrip(root);
                TestUnifiedRestoreAndMissingOnly(root);
                TestVersionOneFixtureCompatibility(root);
                TestAtomicWriteAndEmptyProtection(root);
                TestStrictValidation();
                TestStoreCreateOnlyAndBackupFallback(root);
                TestPartialFailureAndRetryState(root);
                TestCrossProcessCreateOnly(root);
                TestIncompleteStoreBackupProtection(root);
                TestMemoSaveDurability(root);
                TestIndependentMemoWriters(root);
                TestCrossProcessMemoWriters(root);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("FAIL: unexpected test exception: " + ex);
            }
            finally
            {
                DeleteRoot(root);
            }

            Console.WriteLine(_failures == 0
                ? "ALL MEMO ARCHIVE BACKUP TESTS PASSED"
                : _failures + " MEMO ARCHIVE BACKUP TEST(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }

        private static void TestFileNameAndDeterministicPrivacyRoundTrip(string root)
        {
            DateTime created = new DateTime(2026, 8, 17, 6, 30, 12, DateTimeKind.Utc);
            Assert(
                MemoArchiveBackupService.CreateDefaultFileName(
                    new DateTime(2026, 8, 17, 15, 30, 12, DateTimeKind.Local))
                    == "TarkovServerGuard-memos-20260817-153012.json",
                "default filename contains local date and time");

            string sharedKey = Key('a');
            var raid = CreateRaid(sharedKey, "raid body", created.AddHours(-2));
            string screenshotPath = Path.Combine(root, "secret-user", "raid-shot.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
            const string screenshotFileBytesMarker = "SYNTHETIC-SCREENSHOT-FILE-BYTES-MARKER";
            File.WriteAllText(screenshotPath, screenshotFileBytesMarker, new UTF8Encoding(false));
            raid.ScreenshotPaths.Add(screenshotPath);
            raid.ScreenshotPaths.Add("Z:\\OtherComputer\\Pictures\\missing-shot.png");
            var secondRaid = CreateRaid(Key('b'), "second", created.AddHours(-1));
            var report = CreateReport(sharedKey, "legacy report", created.AddMinutes(-30));

            MemoArchiveBackupExportResult first = MemoArchiveBackupService.CreateExport(
                new[] { secondRaid, raid }, new[] { report }, created);
            MemoArchiveBackupExportResult second = MemoArchiveBackupService.CreateExport(
                new[] { raid, secondRaid }, new[] { report }, created);
            Assert(first.Success && first.RaidNoteCount == 2
                && first.UserReportMemoCount == 1 && first.TotalCount == 3,
                "unified export includes both memo kinds");
            Assert(first.Utf8Bytes.SequenceEqual(second.Utf8Bytes),
                "export is deterministic regardless of source enumeration order");
            Assert(first.Utf8Bytes.Length > 0
                && !(first.Utf8Bytes.Length >= 3
                    && first.Utf8Bytes[0] == 0xef
                    && first.Utf8Bytes[1] == 0xbb
                    && first.Utf8Bytes[2] == 0xbf)
                && first.Utf8Bytes[first.Utf8Bytes.Length - 1] == (byte)'}',
                "export is UTF-8 without BOM or trailing newline");

            string json = new UTF8Encoding(false, true).GetString(first.Utf8Bytes);
            Assert(json.StartsWith(
                    "{\"Format\":\"TarkovServerGuard.Memos\",\"Version\":1,\"CreatedUtc\":" ,
                    StringComparison.Ordinal)
                && json.IndexOf("\"RaidNotes\"", StringComparison.Ordinal)
                    < json.IndexOf("\"UserReportMemos\"", StringComparison.Ordinal),
                "top-level field order and kind separation are fixed");
            Assert(json.Contains("\"GameType\":\"PvP시즌1\"")
                && json.Contains("\"GameType\":\"Ranked\""),
                "both memo kinds preserve their neutral game-type display value");
            Assert(json.Contains("\"ScreenshotPaths\":[")
                && json.Contains(new JavaScriptSerializer().Serialize(screenshotPath))
                && json.Contains(new JavaScriptSerializer().Serialize(
                    "Z:\\OtherComputer\\Pictures\\missing-shot.png"))
                && json.IndexOf(screenshotFileBytesMarker, StringComparison.Ordinal) < 0
                && json.IndexOf("LogPath", StringComparison.OrdinalIgnoreCase) < 0
                && json.IndexOf("Account", StringComparison.OrdinalIgnoreCase) < 0
                && json.IndexOf("SID", StringComparison.OrdinalIgnoreCase) < 0,
                "screenshot link strings are included in order without reading file bytes");

            MemoArchiveBackupParseResult parsed = MemoArchiveBackupService.Parse(first.Utf8Bytes);
            Assert(parsed.Success && parsed.Items.Count == 3
                && parsed.RaidNoteCount == 2 && parsed.UserReportMemoCount == 1,
                "strict parser reads the unified archive");
            MemoArchiveBackupParsedItem parsedRaid = parsed.Items.First(
                item => item.Kind == MemoArchiveBackupKind.RaidNote && item.Key == sharedKey);
            MemoArchiveBackupParsedItem parsedReport = parsed.Items.First(
                item => item.Kind == MemoArchiveBackupKind.UserReportMemo && item.Key == sharedKey);
            Assert(parsedRaid.RaidNote.ScreenshotPaths.SequenceEqual(new[]
                    {
                        screenshotPath,
                        "Z:\\OtherComputer\\Pictures\\missing-shot.png"
                    }, StringComparer.Ordinal)
                && parsedRaid.RaidNote.GameType == "PvP시즌1"
                && parsedReport.UserReportMemo != null
                && parsedReport.UserReportMemo.GameType == "Ranked",
                "same key remains valid across kinds and screenshot link order is preserved");
            Assert(parsedRaid.PreviewText == "raid body"
                && parsedReport.PreviewText.Length <= 80,
                "bounded preview text is produced without exposing it in errors");

            RaidNoteRecord preciseRaid = CreateRaid(
                Key('0'),
                "sub-millisecond",
                created.AddTicks(-1234));
            MemoArchiveBackupExportResult preciseExport = MemoArchiveBackupService.CreateExport(
                new[] { preciseRaid }, new UserReportMemoRecord[0], created.AddSeconds(1));
            MemoArchiveBackupParseResult preciseParsed = MemoArchiveBackupService.Parse(
                preciseExport.Utf8Bytes);
            Assert(preciseParsed.Success
                && preciseParsed.Items[0].RaidNote.UpdatedUtc.Ticks
                    % TimeSpan.TicksPerMillisecond == 0,
                "timestamps are canonicalized to the stores' lossless millisecond precision");

            MemoArchiveBackupExportResult onlyRaid = MemoArchiveBackupService.CreateExport(
                new[] { raid }, new UserReportMemoRecord[0], created);
            MemoArchiveBackupExportResult onlyReport = MemoArchiveBackupService.CreateExport(
                new RaidNoteRecord[0], new[] { report }, created);
            Assert(onlyRaid.Success && onlyRaid.RaidNoteCount == 1
                && onlyReport.Success && onlyReport.UserReportMemoCount == 1,
                "archives with only one memo kind are supported");
            Assert(!MemoArchiveBackupService.CreateExport(
                    new RaidNoteRecord[0], new UserReportMemoRecord[0], created).Success,
                "empty stores refuse to create a backup");
        }

        private static void TestUnifiedRestoreAndMissingOnly(string root)
        {
            DateTime now = new DateTime(2026, 8, 17, 7, 0, 0, DateTimeKind.Utc);
            string sharedKey = Key('c');
            RaidNoteRecord sourceRaid = CreateRaid(
                sharedKey,
                "source raid",
                now.AddHours(-2));
            sourceRaid.ScreenshotPaths.Add("Z:\\Transferred\\missing-first.png");
            sourceRaid.ScreenshotPaths.Add("Y:\\ArchivedComputer\\Shots\\missing-second.webp");
            sourceRaid.ScreenshotPaths.Add("Z:\\OldTestBuild\\existing-recording.mp4");
            MemoArchiveBackupExportResult export = MemoArchiveBackupService.CreateExport(
                new[] { sourceRaid },
                new[] { CreateReport(sharedKey, "source report", now.AddHours(-1)) },
                now);
            MemoArchiveBackupParseResult parsed = MemoArchiveBackupService.Parse(export.Utf8Bytes);
            var raids = new RaidNoteStore(Path.Combine(root, "roundtrip-raids"));
            var reports = new UserReportMemoStore(Path.Combine(root, "roundtrip-reports"));
            IList<MemoArchiveRestoreItem> preview = MemoArchiveBackupService.CreateRestorePreview(
                parsed, raids, reports);
            Assert(preview.Count == 2
                && preview.All(item => item.Status == MemoArchiveRestoreStatus.New && item.Selected),
                "new items are selected in restore preview");

            MemoArchiveRestoreResult applied = MemoArchiveBackupService.ApplyMissingOnly(
                preview, raids, reports);
            Assert(applied.AddedCount == 2 && applied.SkippedCount == 0
                && applied.FailedCount == 0 && applied.ItemResults.All(item => item.Added),
                "both memo kinds restore and are post-read verified");
            RaidNoteRecord loadedRaid = raids.Load(sharedKey);
            UserReportMemoRecord loadedReport = reports.Load(sharedKey);
            Assert(loadedRaid != null && loadedRaid.NoteText == "source raid"
                && loadedRaid.ScreenshotPaths.SequenceEqual(
                    sourceRaid.ScreenshotPaths,
                    StringComparer.Ordinal)
                && loadedReport != null && loadedReport.MemoText == "source report",
                "round-trip restores screenshot links without requiring files to exist");

            loadedRaid.NoteText = "local raid must survive";
            raids.Save(sharedKey, loadedRaid);
            loadedReport.MemoText = "local report must survive";
            reports.Save(sharedKey, loadedReport);
            string raidPath = Path.Combine(raids.NotesFolder, sharedKey + ".json");
            string reportPath = Path.Combine(root, "roundtrip-reports", sharedKey + ".json");
            byte[] raidBeforeRestore = File.ReadAllBytes(raidPath);
            byte[] reportBeforeRestore = File.ReadAllBytes(reportPath);
            preview = MemoArchiveBackupService.CreateRestorePreview(parsed, raids, reports);
            Assert(preview.All(item => item.Status == MemoArchiveRestoreStatus.Existing
                    && !item.Selected),
                "same kind plus key is classified as existing regardless of content difference");
            applied = MemoArchiveBackupService.ApplyMissingOnly(preview, raids, reports);
            Assert(applied.AddedCount == 0 && applied.SkippedCount == 2
                && applied.FailedCount == 0
                && raids.Load(sharedKey).NoteText == "local raid must survive"
                && reports.Load(sharedKey).MemoText == "local report must survive",
                "existing records are never overwritten");
            Assert(File.ReadAllBytes(raidPath).SequenceEqual(raidBeforeRestore)
                && File.ReadAllBytes(reportPath).SequenceEqual(reportBeforeRestore),
                "skipping existing records preserves their exact primary bytes, including attachment links");
        }

        private static void TestVersionOneFixtureCompatibility(string root)
        {
            // Independently authored v0.8.3-format fixture: unlike a round-trip
            // through the current exporter, this catches simultaneous writer and
            // reader drift that would make old user backups unreadable.
            const string oldArchive = @"{""Format"":""TarkovServerGuard.Memos"",""Version"":1,""CreatedUtc"":""2026-08-20T10:00:00.0000000Z"",""RaidNotes"":[{""Key"":""aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"",""Game"":""EFT"",""GameType"":""PvP시즌1 · PMC · 솔로"",""RaidStartedUtc"":""2026-08-20T09:00:00.0000000Z"",""MapName"":""Customs"",""NoteText"":""0.8.3에서 저장한 메모"",""ScreenshotPaths"":[""Z:\\Screenshots\\missing.png""],""Tags"":[""기존 태그""],""CreatedUtc"":""2026-08-20T09:10:00.0000000Z"",""UpdatedUtc"":""2026-08-20T09:20:00.0000000Z""}],""UserReportMemos"":[{""Key"":""bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"",""ReportCount"":1,""Entries"":[{""Nickname"":""fixture-player"",""Reason"":""기존 신고 사유""}],""MemoText"":""기존 자유 메모"",""Game"":""EFT"",""GameType"":""PvP시즌1 · PMC · 솔로"",""RaidStartedUtc"":""2026-08-20T09:00:00.0000000Z"",""MapName"":""Customs"",""CreatedUtc"":""2026-08-20T09:10:00.0000000Z"",""UpdatedUtc"":""2026-08-20T09:20:00.0000000Z""}]}";
            MemoArchiveBackupParseResult parsed = MemoArchiveBackupService.Parse(new UTF8Encoding(false).GetBytes(oldArchive));
            Assert(parsed.Success && parsed.Items.Count == 2,
                "an independently authored version-one backup remains readable");
            var raids = new RaidNoteStore(Path.Combine(root, "v083-fixture-raids"));
            var reports = new UserReportMemoStore(Path.Combine(root, "v083-fixture-reports"));
            MemoArchiveRestoreResult result = MemoArchiveBackupService.ApplyMissingOnly(
                MemoArchiveBackupService.CreateRestorePreview(parsed, raids, reports), raids, reports);
            Assert(result.AddedCount == 2 && result.FailedCount == 0,
                "a version-one backup restores both memo kinds");
            RaidNoteRecord raid = raids.Load(Key('a'));
            UserReportMemoRecord report = reports.Load(Key('b'));
            Assert(raid != null && raid.NoteText == "0.8.3에서 저장한 메모"
                && raid.GameType == "PvP시즌1 · PMC · 솔로"
                && raid.ScreenshotPaths.SequenceEqual(new[] { "Z:\\Screenshots\\missing.png" })
                && report != null && report.Entries.Count == 1
                && report.Entries[0].Reason == "기존 신고 사유",
                "legacy text, historical labels, missing image links, and structured report fields survive rollback");
        }

        private static void TestAtomicWriteAndEmptyProtection(string root)
        {
            DateTime now = new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc);
            MemoArchiveBackupExportResult export = MemoArchiveBackupService.CreateExport(
                new[] { CreateRaid(Key('d'), "atomic", now.AddHours(-1)) },
                new UserReportMemoRecord[0],
                now);
            string target = Path.Combine(root, "atomic.json");
            File.WriteAllText(target, "old-valid-file", new UTF8Encoding(false));
            MemoArchiveBackupService.WriteAtomic(target, export.Utf8Bytes);
            Assert(MemoArchiveBackupService.ParseFile(target).Success,
                "atomic writer commits a valid non-empty archive");

            byte[] before = File.ReadAllBytes(target);
            AssertThrows(delegate { MemoArchiveBackupService.WriteAtomic(target, Encoding.UTF8.GetBytes("{}")); },
                "invalid or empty content is rejected before overwrite");
            Assert(before.SequenceEqual(File.ReadAllBytes(target)),
                "invalid write leaves an existing backup unchanged");

            using (var lockStream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                AssertThrows(delegate { MemoArchiveBackupService.WriteAtomic(target, export.Utf8Bytes); },
                    "replace failure is surfaced");
                Assert(before.SequenceEqual(File.ReadAllBytes(target)),
                    "replace failure preserves the previous complete backup");
            }
            Assert(Directory.GetFiles(root, "atomic.json.tmp.*").Length == 0,
                "atomic writer cleans uniquely named temporary files");
        }

        private static void TestStrictValidation()
        {
            DateTime now = new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc);
            MemoArchiveBackupExportResult export = MemoArchiveBackupService.CreateExport(
                new[] { CreateRaid(Key('e'), "PRIVATE-CONTENT-MARKER", now.AddHours(-2)) },
                new[] { CreateReport(Key('f'), "REPORT-PRIVATE-MARKER", now.AddHours(-1)) },
                now);
            string json = Encoding.UTF8.GetString(export.Utf8Bytes);

            AssertParseFailure(new byte[] { 0xc3, 0x28 }, "utf8-invalid", "invalid UTF-8");
            AssertParseFailure(new byte[MemoArchiveBackupService.MaximumFileBytes + 1],
                "file-size-limit", "file size limit");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"Version\":1", "\"Version\":\"1\"")),
                "format-unsupported", "wrong primitive type");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"RaidNotes\":[", "\"Unknown\":true,\"RaidNotes\":[")),
                "document-fields-invalid", "unknown top-level field");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"Format\":", "\"F\\u006frmat\":\"duplicate\",\"Format\":")),
                "duplicate-member", "escaped-equivalent duplicate member");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"MapName\":\"Factory\"", "\"MapName\":\"Factory\",\"Unexpected\":0")),
                "fields-invalid", "unknown raid field");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(",\"Tags\":[\"evidence\"]", string.Empty)),
                "fields-invalid", "missing required raid field");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(",\"ScreenshotPaths\":[]", string.Empty)),
                "fields-invalid", "missing required screenshot-path field");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(",\"GameType\":\"PvP시즌1\"", string.Empty)),
                "fields-invalid", "missing required game-type field");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"ScreenshotPaths\":[]", "\"ScreenshotPaths\":42")),
                "screenshot-list-invalid", "wrong screenshot-path list type");
            var serializer = new JavaScriptSerializer();
            int nonPortablePathIndex = 0;
            foreach (string nonPortablePath in new[]
            {
                "relative\\shot.png",
                "C:drive-relative.png",
                "\\root-relative.png",
                "\\\\?\\C:\\device-path.png",
                "\\\\OtherComputer\\Share\\shot.png",
                "C:\\Windows\\System32\\calc.exe",
                "C:\\Shots\\PRIVATE-SCREENSHOT-PATH-MARKER.exe",
                "C:\\Shots\\PRIVATE-SCREENSHOT-PATH-MARKER\u202Egnp.exe.png",
                "C:\\Shots\\bad?.png"
            })
            {
                nonPortablePathIndex++;
                AssertParseFailure(Encoding.UTF8.GetBytes(
                        json.Replace(
                            "\"ScreenshotPaths\":[]",
                            "\"ScreenshotPaths\":["
                                + serializer.Serialize(nonPortablePath) + "]")),
                    "screenshot-path-invalid",
                    "non-portable screenshot path shape "
                        + nonPortablePathIndex.ToString(CultureInfo.InvariantCulture));
            }
            string privateScreenshotPath = "C:\\PRIVATE-SCREENSHOT-PATH-MARKER.png";
            string duplicateScreenshotPaths = serializer.Serialize(privateScreenshotPath)
                + "," + serializer.Serialize(privateScreenshotPath.ToLowerInvariant());
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"ScreenshotPaths\":[]",
                        "\"ScreenshotPaths\":[" + duplicateScreenshotPaths + "]")),
                "screenshot-path-invalid", "case-insensitive duplicate screenshot paths");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"ScreenshotPaths\":[]",
                        "\"ScreenshotPaths\":["
                            + serializer.Serialize("C:\\" + new string('P', 2046)) + "]")),
                "screenshot-path-invalid", "overlong screenshot path");
            string tooManyScreenshotPaths = string.Join(",", Enumerable.Range(0, 101)
                .Select(index => serializer.Serialize(
                    "Z:\\Missing\\shot-" + index.ToString(CultureInfo.InvariantCulture) + ".png")));
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"ScreenshotPaths\":[]",
                        "\"ScreenshotPaths\":[" + tooManyScreenshotPaths + "]")),
                "screenshot-list-invalid", "screenshot path count limit");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"ScreenshotPaths\":[]",
                        "\"ScreenshotPaths\":[\"\\uD800\"]")),
                "json-invalid", "escaped lone surrogate in screenshot path");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("PRIVATE-CONTENT-MARKER", "\\uD800")),
                "json-invalid", "escaped lone surrogate");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(now.AddHours(-2).AddMinutes(-10).ToString("o"),
                        now.AddHours(1).ToString("o"))),
                "timestamp-order-invalid", "created after updated");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("Z\",\"RaidNotes\"", "+00:00\",\"RaidNotes\"")),
                "created-utc-invalid", "non-Z backup timestamp");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"MemoText\":", "\"UnknownReport\":0,\"MemoText\":")),
                "fields-invalid", "unknown user-report field");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"Nickname\":\"SyntheticOne\"",
                        "\"Nickname\":\"SyntheticOne\",\"UnknownEntry\":0")),
                "entry-invalid", "unknown user-report entry field");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"Nickname\":\"SyntheticOne\"",
                        "\"N\\u0069ckname\":\"duplicate\",\"Nickname\":\"SyntheticOne\"")),
                "duplicate-member", "nested escaped-equivalent duplicate member");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"ReportCount\":2", "\"ReportCount\":2.0")),
                "value-invalid", "wrong report-count numeric type");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace("\"GameType\":\"Ranked\"", "\"GameType\":42")),
                "value-invalid", "wrong report game-type type");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"GameType\":\"Ranked\"",
                        "\"GameType\":\"" + new string('T', 65) + "\"")),
                "value-invalid", "overlong report game-type value");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        new JavaScriptSerializer().Serialize("PRIVATE-CONTENT-MARKER"),
                        new JavaScriptSerializer().Serialize(new string('N', 200001)))),
                "value-invalid", "overlong imported raid note text");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"Nickname\":\"SyntheticOne\"",
                        "\"Nickname\":\"" + new string('U', 257) + "\"")),
                "entry-invalid", "overlong imported report nickname");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(
                        "\"Reason\":\"test reason\"",
                        "\"Reason\":\"" + new string('R', 2001) + "\"")),
                "entry-invalid", "overlong imported report reason");

            string olderArchiveTime = now.AddHours(-4).ToString("o");
            AssertParseFailure(Encoding.UTF8.GetBytes(
                    json.Replace(now.ToString("o"), olderArchiveTime)),
                "archive-timestamp-order-invalid", "archive time before item update");

            string legacyJson = json.Replace(
                new JavaScriptSerializer().Serialize("PRIVATE-CONTENT-MARKER"),
                new JavaScriptSerializer().Serialize(RaidNoteStore.LegacyDefaultNoteTemplate));
            MemoArchiveBackupParseResult legacyParsed = MemoArchiveBackupService.Parse(
                Encoding.UTF8.GetBytes(legacyJson));
            Assert(legacyParsed.Success
                && legacyParsed.Items.First(item => item.Kind == MemoArchiveBackupKind.RaidNote)
                    .RaidNote.NoteText == string.Empty,
                "legacy placeholder note is canonicalized before restore verification");

            MemoArchiveBackupExportResult previewExport = MemoArchiveBackupService.CreateExport(
                new[] { CreateRaid(Key('7'), "abc\u202Edef\u200Bghi\r\nj", now.AddMinutes(-1)) },
                new UserReportMemoRecord[0],
                now);
            MemoArchiveBackupParseResult previewParsed = MemoArchiveBackupService.Parse(
                previewExport.Utf8Bytes);
            Assert(previewParsed.Success && previewParsed.Items[0].PreviewText == "abcdefghi j",
                "preview removes bidi and zero-width format controls and folds whitespace");

            string deep = "{\"Format\":\"TarkovServerGuard.Memos\",\"Version\":1,"
                + "\"CreatedUtc\":\"" + now.ToString("o") + "\",\"RaidNotes\":[],"
                + "\"UserReportMemos\":[],\"X\":" + new string('[', 20) + "0"
                + new string(']', 20) + "}";
            AssertParseFailure(Encoding.UTF8.GetBytes(deep), "json-invalid", "nesting depth limit");

            var many = new StringBuilder();
            many.Append("{\"Format\":\"TarkovServerGuard.Memos\",\"Version\":1,")
                .Append("\"CreatedUtc\":\"").Append(now.ToString("o"))
                .Append("\",\"RaidNotes\":[");
            for (int index = 0; index <= MemoArchiveBackupService.MaximumItemCount; index++)
            {
                if (index > 0) many.Append(',');
                many.Append("{}");
            }
            many.Append("],\"UserReportMemos\":[]}");
            AssertParseFailure(Encoding.UTF8.GetBytes(many.ToString()),
                "item-limit", "total item count limit");

            RaidNoteRecord tooLong = CreateRaid(Key('1'), "x", now.AddHours(-1));
            tooLong.NoteText = new string('가', 200001);
            MemoArchiveBackupExportResult tooLongExport = MemoArchiveBackupService.CreateExport(
                new[] { tooLong }, new UserReportMemoRecord[0], now);
            Assert(!tooLongExport.Success && tooLongExport.SafeErrorCode == "raid-note-invalid",
                "export rejects an overlong memo string");
            Assert(tooLongExport.ErrorMessage.IndexOf("가가가", StringComparison.Ordinal) < 0,
                "validation errors never echo memo content");

            string maximumNote = new string('x', 200000);
            var tooManyLargeNotes = new List<RaidNoteRecord>();
            for (int index = 0; index < 85; index++)
            {
                RaidNoteRecord item = CreateRaid(
                    index.ToString("x64", CultureInfo.InvariantCulture),
                    maximumNote,
                    now.AddMinutes(-1));
                tooManyLargeNotes.Add(item);
            }
            MemoArchiveBackupExportResult oversizedExport =
                MemoArchiveBackupService.CreateExport(
                    tooManyLargeNotes,
                    new UserReportMemoRecord[0],
                    now);
            Assert(!oversizedExport.Success
                && oversizedExport.SafeErrorCode == "file-size-limit"
                && oversizedExport.Utf8Bytes == null,
                "export preflights escaped UTF-8 size before building an oversized document");

            RaidNoteRecord duplicateA = CreateRaid(Key('2'), "a", now.AddMinutes(-2));
            RaidNoteRecord duplicateB = CreateRaid(Key('2'), "b", now.AddMinutes(-1));
            MemoArchiveBackupExportResult duplicate = MemoArchiveBackupService.CreateExport(
                new[] { duplicateA, duplicateB }, new UserReportMemoRecord[0], now);
            Assert(!duplicate.Success && duplicate.SafeErrorCode == "duplicate-key",
                "duplicate key in the same kind is rejected");

            RaidNoteRecord longGameType = CreateRaid(Key('c'), "game type", now.AddMinutes(-1));
            longGameType.GameType = new string('T', 65);
            MemoArchiveBackupExportResult longGameTypeResult =
                MemoArchiveBackupService.CreateExport(
                    new[] { longGameType }, new UserReportMemoRecord[0], now);
            Assert(!longGameTypeResult.Success
                && longGameTypeResult.SafeErrorCode == "raid-game-type-invalid",
                "game-type display value is bounded to 64 characters");

            RaidNoteRecord duplicateScreenshotExport = CreateRaid(
                Key('d'),
                "duplicate screenshot export",
                now.AddMinutes(-1));
            duplicateScreenshotExport.ScreenshotPaths.Add(privateScreenshotPath);
            duplicateScreenshotExport.ScreenshotPaths.Add(
                privateScreenshotPath.ToLowerInvariant());
            MemoArchiveBackupExportResult duplicateScreenshotExportResult =
                MemoArchiveBackupService.CreateExport(
                    new[] { duplicateScreenshotExport },
                    new UserReportMemoRecord[0],
                    now);
            Assert(!duplicateScreenshotExportResult.Success
                && duplicateScreenshotExportResult.SafeErrorCode
                    == "raid-screenshot-path-invalid"
                && duplicateScreenshotExportResult.ErrorMessage.IndexOf(
                    "PRIVATE-SCREENSHOT-PATH-MARKER",
                    StringComparison.Ordinal) < 0,
                "export rejects duplicate screenshot paths without echoing their values");

            RaidNoteRecord tooManyScreenshotExport = CreateRaid(
                Key('f'),
                "too many screenshot paths",
                now.AddMinutes(-1));
            tooManyScreenshotExport.ScreenshotPaths = Enumerable.Range(0, 101)
                .Select(index => "Z:\\Missing\\export-" + index + ".png")
                .ToList();
            MemoArchiveBackupExportResult tooManyScreenshotExportResult =
                MemoArchiveBackupService.CreateExport(
                    new[] { tooManyScreenshotExport },
                    new UserReportMemoRecord[0],
                    now);
            Assert(!tooManyScreenshotExportResult.Success
                && tooManyScreenshotExportResult.SafeErrorCode == "raid-screenshot-limit",
                "export rejects screenshot path counts above the strict limit");

            foreach (string nonPortablePath in new[]
            {
                "relative\\export.png",
                "C:drive-relative-export.png",
                "\\root-relative-export.png",
                "\\\\OtherComputer\\Share\\export.png",
                "C:\\Windows\\System32\\calc.exe",
                "C:\\Shots\\PRIVATE-SCREENSHOT-PATH-MARKER.exe",
                "C:\\Shots\\PRIVATE-SCREENSHOT-PATH-MARKER\u202Egnp.exe.png",
                "C:\\Shots\\bad?.png"
            })
            {
                RaidNoteRecord nonPortableExport = CreateRaid(
                    Key('6'),
                    "non-portable screenshot path",
                    now.AddMinutes(-1));
                nonPortableExport.ScreenshotPaths.Add(nonPortablePath);
                MemoArchiveBackupExportResult nonPortableExportResult =
                    MemoArchiveBackupService.CreateExport(
                        new[] { nonPortableExport },
                        new UserReportMemoRecord[0],
                        now);
                Assert(!nonPortableExportResult.Success
                    && nonPortableExportResult.SafeErrorCode
                        == "raid-screenshot-path-invalid"
                    && nonPortableExportResult.ErrorMessage.IndexOf(
                        "PRIVATE-SCREENSHOT-PATH-MARKER",
                        StringComparison.Ordinal) < 0,
                    "export rejects unsafe non-image screenshot link strings without echoing values");
            }

            MemoArchiveBackupExportResult twoUnique = MemoArchiveBackupService.CreateExport(
                new[]
                {
                    CreateRaid(Key('8'), "first", now.AddMinutes(-2)),
                    CreateRaid(Key('9'), "second", now.AddMinutes(-1))
                },
                new UserReportMemoRecord[0],
                now);
            string duplicateKeyJson = Encoding.UTF8.GetString(twoUnique.Utf8Bytes)
                .Replace(Key('9'), Key('8'));
            AssertParseFailure(Encoding.UTF8.GetBytes(duplicateKeyJson),
                "raid-key-duplicate", "duplicate parsed key in the same kind");
        }

        private static void TestStoreCreateOnlyAndBackupFallback(string root)
        {
            DateTime now = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
            string raidFolder = Path.Combine(root, "create-only-raids");
            string reportFolder = Path.Combine(root, "create-only-reports");
            var raidStore = new RaidNoteStore(raidFolder);
            var reportStore = new UserReportMemoStore(reportFolder);
            string key = Key('3');
            RaidNoteRecord firstRaid = CreateRaid(key, "first raid", now.AddHours(-2));
            firstRaid.ScreenshotPaths.Add("Z:\\OtherPC\\missing-shot.png");
            Assert(raidStore.TryRestoreMissing(key, firstRaid),
                "raid store create-only restore saves a missing item");
            Assert(firstRaid.ScreenshotPaths.SequenceEqual(
                    new[] { "Z:\\OtherPC\\missing-shot.png" },
                    StringComparer.Ordinal)
                && raidStore.Load(key).ScreenshotPaths.SequenceEqual(
                    firstRaid.ScreenshotPaths,
                    StringComparer.Ordinal),
                "raid create-only restore preserves missing-file screenshot link strings");
            RaidNoteRecord invalidRestoredPaths = CreateRaid(
                Key('4'),
                "invalid restored screenshot links",
                now.AddHours(-1));
            invalidRestoredPaths.ScreenshotPaths.Add("C:\\Shots\\same.png");
            invalidRestoredPaths.ScreenshotPaths.Add("c:\\shots\\SAME.png");
            AssertThrows(
                delegate
                {
                    raidStore.TryRestoreMissing(
                        invalidRestoredPaths.Key,
                        invalidRestoredPaths);
                },
                "raid store rejects duplicate restored screenshot links");
            Assert(raidStore.Load(invalidRestoredPaths.Key) == null,
                "invalid screenshot links never create a restored record");
            string[] invalidRestoredScreenshotPaths =
            {
                "relative\\shot.png",
                "\\\\OtherPC\\Share\\one.png",
                "C:\\Windows\\System32\\calc.exe",
                "C:\\Shots\\photo\u202Egnp.exe.png",
                "C:\\Shots\\bad?.png"
            };
            for (int index = 0; index < invalidRestoredScreenshotPaths.Length; index++)
            {
                RaidNoteRecord invalidRestoredPath = CreateRaid(
                    Key((char)('5' + index)),
                    "invalid restored screenshot link",
                    now.AddHours(-1));
                invalidRestoredPath.ScreenshotPaths.Add(
                    invalidRestoredScreenshotPaths[index]);
                AssertThrows(
                    delegate
                    {
                        raidStore.TryRestoreMissing(
                            invalidRestoredPath.Key,
                            invalidRestoredPath);
                    },
                    "raid store rejects unsafe restored screenshot link "
                        + index.ToString(CultureInfo.InvariantCulture));
                Assert(raidStore.Load(invalidRestoredPath.Key) == null,
                    "unsafe restored screenshot link never creates record "
                        + index.ToString(CultureInfo.InvariantCulture));
            }
            Assert(RaidNoteStore.IsSafeScreenshotAttachmentPath("C:\\Shots\\one.png")
                && RaidNoteStore.IsSafeScreenshotAttachmentPath(
                    "z:\\Other PC\\레이드 샷.WEBP")
                && RaidNoteStore.IsSafeAttachmentPath(
                    "C:\\Captures\\raid-result.MP4")
                && RaidNoteStore.IsSafeAttachmentPath(
                    "D:\\Captures\\raid-result.mkv")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath(
                    "C:\\Captures\\raid-result.MP4")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath(
                    "\\\\OtherPC\\Share\\one.png")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath("C:one.png")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath("\\one.png")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath("one.png")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath(
                    "C:\\Windows\\System32\\calc.exe")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath(
                    "C:\\Shots\\photo\u202Egnp.exe.png")
                && !RaidNoteStore.IsSafeScreenshotAttachmentPath(
                    "C:\\Shots\\bad?.png"),
                "new screenshot input rejects video while old attachment data permits safe image and video paths");
            RaidNoteRecord conflictingRaid = CreateRaid(key, "must not replace", now.AddHours(-1));
            Assert(!raidStore.TryRestoreMissing(key, conflictingRaid)
                && raidStore.Load(key).NoteText == "first raid",
                "raid create-only restore refuses an existing primary with different content");

            UserReportMemoRecord firstReport = CreateReport(key, "first report", now.AddHours(-2));
            Assert(reportStore.TryRestoreMissing(key, firstReport)
                && !reportStore.TryRestoreMissing(
                    key, CreateReport(key, "must not replace", now.AddHours(-1)))
                && reportStore.Load(key).MemoText == "first report",
                "report store create-only restore never replaces existing content");

            string primary = Path.Combine(raidFolder, key + ".json");
            string backup = primary + ".bak";
            File.Copy(primary, backup, true);
            File.Delete(primary);
            Assert(!raidStore.TryRestoreMissing(
                    key, CreateRaid(key, "must not replace fallback", now.AddMinutes(-1)))
                && raidStore.Load(key).NoteText == "first raid",
                "backup-only existing raid is also treated as existing and preserved");

            TestLegacyStoreGameTypeCompatibility(root, now);

            RaidNoteRecord exactRecord = CreateRaid(
                Key('a'),
                "retry exact",
                now.AddMinutes(-2));
            exactRecord.ScreenshotPaths.Add("Z:\\Retry\\same.png");
            MemoArchiveBackupExportResult exactExport = MemoArchiveBackupService.CreateExport(
                new[] { exactRecord },
                new UserReportMemoRecord[0],
                now);
            MemoArchiveBackupParseResult exactParsed = MemoArchiveBackupService.Parse(
                exactExport.Utf8Bytes);
            var retryStore = new RaidNoteStore(Path.Combine(root, "retry-existing"));
            MemoArchiveBackupParsedItem exactSource = exactParsed.Items[0];
            Assert(retryStore.TryRestoreMissing(exactSource.Key, exactSource.RaidNote),
                "retry fixture first persists the expected record");
            var exactRetry = new MemoArchiveRestoreItem
            {
                Source = exactSource,
                Status = MemoArchiveRestoreStatus.New,
                Selected = true
            };
            MemoArchiveRestoreResult exactResult = MemoArchiveBackupService.ApplyMissingOnly(
                new[] { exactRetry }, retryStore, reportStore);
            Assert(exactResult.SkippedCount == 1 && exactResult.FailedCount == 0
                && exactRetry.Status == MemoArchiveRestoreStatus.Existing
                && !exactRetry.Selected,
                "retry after a prior successful write re-reads equivalent content and completes safely");

            RaidNoteRecord conflictRecord = CreateRaid(
                Key('b'),
                "backup source",
                now.AddMinutes(-2));
            conflictRecord.ScreenshotPaths.Add("Z:\\Backup\\expected.png");
            MemoArchiveBackupExportResult conflictExport = MemoArchiveBackupService.CreateExport(
                new[] { conflictRecord },
                new UserReportMemoRecord[0],
                now);
            MemoArchiveBackupParsedItem conflictSource = MemoArchiveBackupService.Parse(
                conflictExport.Utf8Bytes).Items[0];
            RaidNoteRecord differentScreenshotRecord = CreateRaid(
                conflictSource.Key,
                "backup source",
                now.AddMinutes(-2));
            differentScreenshotRecord.ScreenshotPaths.Add("Z:\\Backup\\different.png");
            Assert(retryStore.TryRestoreMissing(
                    conflictSource.Key,
                    differentScreenshotRecord),
                "conflict fixture persists different screenshot-link content");
            var conflictRetry = new MemoArchiveRestoreItem
            {
                Source = conflictSource,
                Status = MemoArchiveRestoreStatus.New,
                Selected = true
            };
            MemoArchiveRestoreResult conflictResult = MemoArchiveBackupService.ApplyMissingOnly(
                new[] { conflictRetry }, retryStore, reportStore);
            Assert(conflictResult.FailedCount == 1 && conflictResult.SkippedCount == 0
                && conflictRetry.Status == MemoArchiveRestoreStatus.ExistingConflict
                && conflictRetry.Selected
                && retryStore.Load(conflictSource.Key).ScreenshotPaths.SequenceEqual(
                    differentScreenshotRecord.ScreenshotPaths,
                    StringComparer.Ordinal),
                "screenshot-link mismatch is a non-overwriting retryable conflict");
        }

        private static void TestPartialFailureAndRetryState(string root)
        {
            DateTime now = new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc);
            MemoArchiveBackupExportResult export = MemoArchiveBackupService.CreateExport(
                new[] { CreateRaid(Key('4'), "raid fails", now.AddHours(-2)) },
                new[] { CreateReport(Key('5'), "report succeeds", now.AddHours(-1)) },
                now);
            MemoArchiveBackupParseResult parsed = MemoArchiveBackupService.Parse(export.Utf8Bytes);
            string invalidRaidFolder = Path.Combine(root, "folder-is-a-file");
            File.WriteAllText(invalidRaidFolder, "not a directory");
            var raidStore = new RaidNoteStore(invalidRaidFolder);
            var reportStore = new UserReportMemoStore(Path.Combine(root, "partial-reports"));
            IList<MemoArchiveRestoreItem> preview = MemoArchiveBackupService.CreateRestorePreview(
                parsed, raidStore, reportStore);
            MemoArchiveRestoreResult result = MemoArchiveBackupService.ApplyMissingOnly(
                preview, raidStore, reportStore);
            MemoArchiveRestoreItem failedRaid = preview.Single(
                item => item.Source.Kind == MemoArchiveBackupKind.RaidNote);
            Assert(result.AddedCount == 1 && result.FailedCount == 1
                && result.SkippedCount == 0
                && failedRaid.Selected
                && reportStore.Load(Key('5')) != null,
                "failure in one kind does not block the other and failed selection remains retryable");
            Assert(result.ItemResults.Where(item => !item.Added).All(item =>
                    item.ErrorMessage.IndexOf("raid fails", StringComparison.Ordinal) < 0
                    && item.ErrorMessage.IndexOf(root, StringComparison.OrdinalIgnoreCase) < 0),
                "partial failure reports kind and index without content or local path");

            var unavailable = new MemoArchiveRestoreItem
            {
                Source = parsed.Items[0],
                Status = MemoArchiveRestoreStatus.Unavailable,
                Selected = false
            };
            MemoArchiveRestoreResult unavailableResult =
                MemoArchiveBackupService.ApplyMissingOnly(
                    new[] { unavailable }, raidStore, reportStore);
            Assert(unavailableResult.FailedCount == 1
                && unavailableResult.ItemResults[0].SafeErrorCode
                    == "restore-store-unavailable",
                "unavailable preview items remain visible as safe failures in apply results");
        }

        private static void TestLegacyStoreGameTypeCompatibility(string root, DateTime now)
        {
            string raidFolder = Path.Combine(root, "legacy-game-type-raids");
            string reportFolder = Path.Combine(root, "legacy-game-type-reports");
            Directory.CreateDirectory(raidFolder);
            Directory.CreateDirectory(reportFolder);
            string raidKey = Key('d');
            string reportKey = Key('e');
            var serializer = new JavaScriptSerializer();
            var legacyRaid = new Dictionary<string, object>
            {
                { "Key", raidKey },
                { "Game", "EFT" },
                { "RaidStartedUtc", now.AddHours(-2) },
                { "MapName", "Woods" },
                { "NoteText", "legacy raid" },
                { "ScreenshotPaths", new object[0] },
                { "Tags", new object[0] },
                { "CreatedUtc", now.AddHours(-1) },
                { "UpdatedUtc", now }
            };
            var legacyReport = new Dictionary<string, object>
            {
                { "Key", reportKey },
                { "ReportCount", 1 },
                { "Entries", new object[0] },
                { "MemoText", "legacy report" },
                { "Game", "Arena" },
                { "RaidStartedUtc", now.AddHours(-2) },
                { "MapName", "Bay5" },
                { "CreatedUtc", now.AddHours(-1) },
                { "UpdatedUtc", now }
            };
            File.WriteAllText(
                Path.Combine(raidFolder, raidKey + ".json"),
                serializer.Serialize(legacyRaid),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(reportFolder, reportKey + ".json"),
                serializer.Serialize(legacyReport),
                new UTF8Encoding(false));
            Assert(new RaidNoteStore(raidFolder).Load(raidKey).GameType == string.Empty
                && new UserReportMemoStore(reportFolder).Load(reportKey).GameType == string.Empty,
                "legacy store records without GameType load with an empty compatible value");

            var eftSession = new ServerSession
            {
                Game = TarkovGame.Eft,
                ProgressionMode = TarkovProgressionMode.PvpSeason,
                PvpSeasonNumber = 2,
                SessionStarted = now,
                SessionFolderName = "session-game-type-eft"
            };
            var arenaSession = new ServerSession
            {
                Game = TarkovGame.Arena,
                GameMode = "TeamFight",
                SessionStarted = now,
                SessionFolderName = "session-game-type-arena",
                UserReportCount = 1
            };
            var sessionRaidStore = new RaidNoteStore(Path.Combine(root, "session-type-raids"));
            var sessionReportStore = new UserReportMemoStore(
                Path.Combine(root, "session-type-reports"));
            RaidNoteRecord sessionRaid = sessionRaidStore.CreateFor(eftSession);
            UserReportMemoRecord sessionReport = sessionReportStore.CreateFor(arenaSession);
            Assert(sessionRaid.GameType == "PvP/S2"
                && sessionReport.GameType == "TeamFight",
                "CreateFor captures EFT raid type and Arena game mode through GameType");
            sessionRaid.GameType = "tampered";
            sessionReport.GameType = "tampered";
            sessionRaidStore.Save(eftSession, sessionRaid);
            sessionReportStore.Save(arenaSession, sessionReport);
            Assert(sessionRaid.GameType == "PvP/S2"
                && sessionReport.GameType == "TeamFight",
                "Save(session) refreshes GameType from the authoritative session");
        }

        private static void TestMemoSaveDurability(string root)
        {
            string key = Key('7');
            DateTime now = DateTime.UtcNow.AddMinutes(-1);
            string raidFolder = Path.Combine(root, "raid-durability");
            var raids = new RaidNoteStore(raidFolder);
            ExerciseMemoSaveDurability("raid", Path.Combine(raidFolder, key + ".json"),
                text => raids.Save(key, CreateRaid(key, text, now)),
                () => { RaidNoteRecord item = raids.Load(key); return item == null ? null : item.NoteText; });
            string reportFolder = Path.Combine(root, "report-durability");
            var reports = new UserReportMemoStore(reportFolder);
            ExerciseMemoSaveDurability("report", Path.Combine(reportFolder, key + ".json"),
                text => reports.Save(key, CreateReport(key, text, now)),
                () => { UserReportMemoRecord item = reports.Load(key); return item == null ? null : item.MemoText; });
        }

        private static void ExerciseMemoSaveDurability(
            string kind, string primary, Action<string> save, Func<string> load)
        {
            save("first complete note");
            save("second complete note");
            string backup = primary + ".bak";
            byte[] goodBackup = File.ReadAllBytes(backup);
            File.WriteAllText(primary, "{broken-primary", new UTF8Encoding(false));
            Assert(load() == "first complete note", kind + " recovers from its last complete backup");
            save("edited recovered note");
            Assert(load() == "edited recovered note" && File.ReadAllBytes(backup).SequenceEqual(goodBackup),
                kind + " saving a recovered note never replaces the valid backup with corrupt bytes");
            File.WriteAllText(primary, "{broken-again", new UTF8Encoding(false));
            Assert(load() == "first complete note", kind + " retains a usable fallback after recovery and another primary failure");
            File.WriteAllText(backup, "{broken-backup", new UTF8Encoding(false));
            byte[] damagedPrimary = File.ReadAllBytes(primary);
            byte[] damagedBackup = File.ReadAllBytes(backup);
            AssertThrows(() => save("must not replace damaged originals"), kind + " refuses blind overwrite of two unreadable copies");
            Assert(File.ReadAllBytes(primary).SequenceEqual(damagedPrimary)
                && File.ReadAllBytes(backup).SequenceEqual(damagedBackup),
                kind + " rejected save preserves both damaged recovery artifacts byte for byte");

            File.Delete(primary);
            File.Delete(backup);
            save("stable note");
            // A staging file owned by another instance must not be reused or removed.
            string foreignTemporary = primary + ".tmp";
            byte[] marker = Encoding.UTF8.GetBytes("another in-flight save owns this file");
            using (var held = new FileStream(foreignTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                held.Write(marker, 0, marker.Length);
                held.Flush(true);
                save("independent save");
            }
            Assert(load() == "independent save" && File.ReadAllBytes(foreignTemporary).SequenceEqual(marker),
                kind + " save succeeds without touching another writer's staging file");
            byte[] stablePrimary = File.ReadAllBytes(primary);
            byte[] stableBackup = File.ReadAllBytes(backup);
            using (var held = new FileStream(primary, FileMode.Open, FileAccess.Read, FileShare.Read))
                AssertThrows(() => save("cannot commit while target is locked"), kind + " locked destination reports save failure");
            Assert(File.ReadAllBytes(primary).SequenceEqual(stablePrimary)
                && File.ReadAllBytes(backup).SequenceEqual(stableBackup),
                kind + " failed commit preserves the previous primary and recovery copy");
            Assert(Directory.GetFiles(Path.GetDirectoryName(primary), "*.save.*.tmp").Length == 0,
                kind + " successful and failed saves clean only their own temporary files");
        }

        private static void TestIncompleteStoreBackupProtection(string root)
        {
            string raidFolder = Path.Combine(root, "strict-backup-raids");
            string reportFolder = Path.Combine(root, "strict-backup-reports");
            var raids = new RaidNoteStore(raidFolder);
            var reports = new UserReportMemoStore(reportFolder);
            DateTime now = DateTime.UtcNow.AddMinutes(-1);
            string raidKey = Key('8');
            string reportKey = Key('9');
            raids.Save(raidKey, CreateRaid(raidKey, "readable raid", now));
            reports.Save(reportKey, CreateReport(reportKey, "readable report", now));
            string damagedRaid = Path.Combine(raidFolder, Key('a') + ".json");
            File.WriteAllText(damagedRaid, "{damaged-owned-raid", new UTF8Encoding(false));
            byte[] damagedBytes = File.ReadAllBytes(damagedRaid);
            Assert(raids.LoadAll().Count == 1, "normal archive can still show usable notes beside a damaged note");
            MemoArchiveBackupExportResult result = MemoArchiveBackupService.CreateExport(raids, reports, DateTime.UtcNow);
            Assert(!result.Success && result.SafeErrorCode == "source-read-failed",
                "backup refuses to silently omit an unreadable raid note");
            Assert(File.ReadAllBytes(damagedRaid).SequenceEqual(damagedBytes), "failed strict export never repairs or rewrites the source");
            File.Delete(damagedRaid);
            string damagedReport = Path.Combine(reportFolder, Key('b') + ".json");
            File.WriteAllText(damagedReport, "{damaged-owned-report", new UTF8Encoding(false));
            result = MemoArchiveBackupService.CreateExport(raids, reports, DateTime.UtcNow);
            Assert(!result.Success && result.SafeErrorCode == "source-read-failed",
                "backup also refuses to silently omit an unreadable player report");
            var serializer = new JavaScriptSerializer();
            File.WriteAllText(damagedReport + ".bak", serializer.Serialize(CreateReport(Key('b'), "recovered report", now)), new UTF8Encoding(false));
            result = MemoArchiveBackupService.CreateExport(raids, reports, DateTime.UtcNow);
            Assert(result.Success && result.RaidNoteCount == 1 && result.UserReportMemoCount == 2,
                "strict export accepts a valid backup fallback and includes every owned record");
        }

        private static void TestIndependentMemoWriters(string root)
        {
            string key = Key('c');
            DateTime now = DateTime.UtcNow.AddMinutes(-1);
            string raidFolder = Path.Combine(root, "independent-raid-writers");
            var firstRaidStore = new RaidNoteStore(raidFolder);
            var secondRaidStore = new RaidNoteStore(raidFolder);
            ExerciseIndependentWriters("raid", Path.Combine(raidFolder, key + ".json"),
                text => firstRaidStore.Save(key, CreateRaid(key, text, now)),
                text => secondRaidStore.Save(key, CreateRaid(key, text, now)),
                () => firstRaidStore.Load(key).NoteText);
            string reportFolder = Path.Combine(root, "independent-report-writers");
            var firstReportStore = new UserReportMemoStore(reportFolder);
            var secondReportStore = new UserReportMemoStore(reportFolder);
            ExerciseIndependentWriters("report", Path.Combine(reportFolder, key + ".json"),
                text => firstReportStore.Save(key, CreateReport(key, text, now)),
                text => secondReportStore.Save(key, CreateReport(key, text, now)),
                () => firstReportStore.Load(key).MemoText);
        }

        private static void ExerciseIndependentWriters(
            string kind, string primary, Action<string> firstSave, Action<string> secondSave, Func<string> load)
        {
            firstSave("initial");
            for (int round = 0; round < 8; round++)
            {
                string firstText = "first-" + round + ":" + new string('a', 16000);
                string secondText = "second-" + round + ":" + new string('b', 16000);
                using (var gate = new ManualResetEventSlim(false))
                using (var ready = new CountdownEvent(2))
                {
                    Task first = Task.Factory.StartNew(() => { ready.Signal(); gate.Wait(); firstSave(firstText); });
                    Task second = Task.Factory.StartNew(() => { ready.Signal(); gate.Wait(); secondSave(secondText); });
                    bool bothReady = ready.Wait(5000);
                    gate.Set();
                    if (!Task.WaitAll(new[] { first, second }, 10000))
                        throw new TimeoutException("Independent " + kind + " writers did not finish.");
                    Assert(bothReady, kind + " writers are both waiting before the concurrent save starts");
                }
                string actual = load();
                Assert(actual == firstText || actual == secondText,
                    kind + " independent store instances commit one complete record, round " + round);
            }
            Assert(Directory.GetFiles(Path.GetDirectoryName(primary), "*.save.*.tmp").Length == 0,
                kind + " overlapping saves leave no owned staging files behind");
        }

        private static void TestCrossProcessMemoWriters(string root)
        {
            foreach (string kind in new[] { "raid", "report" })
            {
                string folder = Path.Combine(root, "process-save-" + kind);
                string key = Key('d');
                DateTime now = DateTime.UtcNow.AddMinutes(-1);
                if (kind == "raid") new RaidNoteStore(folder).Save(key, CreateRaid(key, "initial", now));
                else new UserReportMemoStore(folder).Save(key, CreateReport(key, "initial", now));
                string eventName = "Local\\TSG-MemoSaveTest-" + Guid.NewGuid().ToString("N");
                using (var gate = new EventWaitHandle(false, EventResetMode.ManualReset, eventName))
                using (Process first = StartSaveWorker(kind, folder, key, "A", eventName))
                using (Process second = StartSaveWorker(kind, folder, key, "B", eventName))
                {
                    try
                    {
                        Task<string> firstReady = first.StandardOutput.ReadLineAsync();
                        Task<string> secondReady = second.StandardOutput.ReadLineAsync();
                        if (!Task.WaitAll(new Task[] { firstReady, secondReady }, 5000))
                            throw new TimeoutException("Save workers did not reach the start barrier.");
                        Assert(firstReady.Result == "READY" && secondReady.Result == "READY",
                            kind + " separate processes are both ready before concurrent writes");
                        Task<string> firstOutput = first.StandardOutput.ReadToEndAsync();
                        Task<string> secondOutput = second.StandardOutput.ReadToEndAsync();
                        gate.Set();
                        if (!first.WaitForExit(15000) || !second.WaitForExit(15000)
                            || !Task.WaitAll(new Task[] { firstOutput, secondOutput }, 5000))
                            throw new TimeoutException("Save worker processes did not exit.");
                        Assert(first.ExitCode == 0 && second.ExitCode == 0,
                            kind + " concurrent process saves complete without replacement/backup collisions: "
                                + firstOutput.Result + secondOutput.Result);
                        string expectedA = "A:15:" + new string('a', 16000);
                        string expectedB = "B:15:" + new string('b', 16000);
                        string actual = kind == "raid"
                            ? new RaidNoteStore(folder).Load(key).NoteText
                            : new UserReportMemoStore(folder).Load(key).MemoText;
                        Assert(actual == expectedA || actual == expectedB,
                            kind + " concurrent processes leave a complete final record");
                        string primary = Path.Combine(folder, key + ".json");
                        File.WriteAllText(primary, "{synthetic-damage", new UTF8Encoding(false));
                        string recovered = kind == "raid"
                            ? new RaidNoteStore(folder).Load(key).NoteText
                            : new UserReportMemoStore(folder).Load(key).MemoText;
                        Assert(recovered != null && recovered.Length > 16000
                            && (recovered.StartsWith("A:", StringComparison.Ordinal)
                                || recovered.StartsWith("B:", StringComparison.Ordinal)),
                            kind + " concurrent process saves also leave a readable full backup");
                        Assert(Directory.GetFiles(folder, "*.save.*.tmp").Length == 0,
                            kind + " process saves clean their staging files");
                    }
                    finally
                    {
                        gate.Set();
                        if (!first.HasExited) { first.Kill(); first.WaitForExit(5000); }
                        if (!second.HasExited) { second.Kill(); second.WaitForExit(5000); }
                    }
                }
            }
        }

        private static Process StartSaveWorker(string kind, string folder, string key, string marker, string eventName)
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "--save-worker " + kind + " " + Quote(folder) + " " + key + " " + marker + " " + Quote(eventName),
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            });
        }

        private static int RunSaveWorker(string kind, string folder, string key, string marker, string eventName)
        {
            try
            {
                using (EventWaitHandle gate = EventWaitHandle.OpenExisting(eventName))
                {
                    Console.WriteLine("READY");
                    Console.Out.Flush();
                    if (!gate.WaitOne(5000)) throw new TimeoutException("Start barrier timed out.");
                    var raids = new RaidNoteStore(folder);
                    var reports = new UserReportMemoStore(folder);
                    for (int round = 0; round < 16; round++)
                    {
                        string body = marker + ":" + round + ":" + new string(marker == "A" ? 'a' : 'b', 16000);
                        if (kind == "raid") raids.Save(key, CreateRaid(key, body, DateTime.UtcNow));
                        else reports.Save(key, CreateReport(key, body, DateTime.UtcNow));
                    }
                }
                Console.WriteLine("SAVED");
                return 0;
            }
            catch (Exception exception)
            {
                Console.WriteLine("FAILED " + exception);
                return 1;
            }
        }

        private static void TestCrossProcessCreateOnly(string root)
        {
            string folder = Path.Combine(root, "cross-process-raids");
            Directory.CreateDirectory(folder);
            string key = Key('6');
            string executable = Process.GetCurrentProcess().MainModule.FileName;
            Process first = StartWorker(executable, folder, key, "worker-A");
            Process second = StartWorker(executable, folder, key, "worker-B");
            string firstOutput = first.StandardOutput.ReadToEnd();
            string secondOutput = second.StandardOutput.ReadToEnd();
            first.WaitForExit(15000);
            second.WaitForExit(15000);
            Assert(first.HasExited && second.HasExited && first.ExitCode == 0 && second.ExitCode == 0,
                "concurrent restore worker processes finish normally");
            int added = (firstOutput.Contains("ADDED") ? 1 : 0)
                + (secondOutput.Contains("ADDED") ? 1 : 0);
            RaidNoteRecord loaded = new RaidNoteStore(folder).Load(key);
            Assert(added == 1 && loaded != null
                && (loaded.NoteText == "worker-A" || loaded.NoteText == "worker-B")
                && Directory.GetFiles(folder, key + ".json").Length == 1,
                "cross-process create-only race has exactly one winner and no overwrite");
        }

        private static Process StartWorker(
            string executable,
            string folder,
            string key,
            string marker)
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--raid-worker " + Quote(folder) + " " + key + " " + marker,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            return Process.Start(start);
        }

        private static int RunRaidWorker(string folder, string key, string marker)
        {
            try
            {
                var store = new RaidNoteStore(folder);
                DateTime now = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
                bool added = store.TryRestoreMissing(key, CreateRaid(key, marker, now));
                Console.WriteLine(added ? "ADDED" : "SKIPPED");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED " + ex.GetType().Name);
                return 1;
            }
        }

        private static RaidNoteRecord CreateRaid(string key, string note, DateTime updated)
        {
            return new RaidNoteRecord
            {
                Key = key,
                Game = "EFT",
                GameType = "PvP시즌1",
                RaidStartedUtc = updated.AddHours(-1),
                MapName = "Factory",
                NoteText = note,
                ScreenshotPaths = new List<string>(),
                Tags = new List<string> { "evidence" },
                CreatedUtc = updated.AddMinutes(-10),
                UpdatedUtc = updated
            };
        }

        private static UserReportMemoRecord CreateReport(
            string key,
            string memo,
            DateTime updated)
        {
            return new UserReportMemoRecord
            {
                Key = key,
                ReportCount = 2,
                Entries = new List<UserReportMemoEntry>
                {
                    new UserReportMemoEntry { Nickname = "SyntheticOne", Reason = "test reason" },
                    new UserReportMemoEntry { Nickname = "SyntheticTwo", Reason = "test reason two" }
                },
                MemoText = memo,
                Game = "Arena",
                GameType = "Ranked",
                RaidStartedUtc = updated.AddHours(-1),
                MapName = "Bay5",
                CreatedUtc = updated.AddMinutes(-10),
                UpdatedUtc = updated
            };
        }

        private static void AssertParseFailure(byte[] bytes, string code, string description)
        {
            MemoArchiveBackupParseResult result = MemoArchiveBackupService.Parse(bytes);
            Assert(!result.Success && result.SafeErrorCode == code,
                description + " is rejected with safe error code " + code);
            Assert((result.ErrorMessage ?? string.Empty).IndexOf(
                    "PRIVATE-CONTENT-MARKER", StringComparison.Ordinal) < 0
                && (result.ErrorMessage ?? string.Empty).IndexOf(
                    "REPORT-PRIVATE-MARKER", StringComparison.Ordinal) < 0
                && (result.ErrorMessage ?? string.Empty).IndexOf(
                    "PRIVATE-SCREENSHOT-PATH-MARKER", StringComparison.Ordinal) < 0,
                description + " error does not echo memo or screenshot-path content");
        }

        private static string Key(char character)
        {
            return new string(character, 64);
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", string.Empty) + "\"";
        }

        private static void AssertThrows(Action action, string description)
        {
            bool threw = false;
            try { action(); }
            catch { threw = true; }
            Assert(threw, description);
        }

        private static void Assert(bool condition, string description)
        {
            if (condition)
            {
                Console.WriteLine("PASS: " + description);
                return;
            }
            _failures++;
            Console.WriteLine("FAIL: " + description);
        }

        private static void DeleteRoot(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                string temp = Path.GetFullPath(Path.GetTempPath())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(full).StartsWith(
                        "TarkovServerGuard-MemoArchiveBackupTests-",
                        StringComparison.Ordinal)
                    && Directory.Exists(full))
                    Directory.Delete(full, true);
            }
            catch
            {
                // Test cleanup must not hide assertion results.
            }
        }
    }
}
