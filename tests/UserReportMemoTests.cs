// SPDX-License-Identifier: MPL-2.0
// Copyright 2026 Spirit-Schema

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class UserReportMemoTests
    {
        [STAThread]
        private static int Main()
        {
            string testRoot = Path.Combine(
                Path.GetTempPath(),
                "TarkovServerGuardUserReportMemoTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(testRoot);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                TestStableSeparateKeyAndTemplate();
                TestStorageBoundaries(testRoot);
                TestArchiveAndLegacyPlaceholder(testRoot);
                TestStructuredEditorAndEntryLimit(testRoot);
                TestSaveButtonBehavior(testRoot);
                Console.WriteLine("UserReportMemoTests: PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("UserReportMemoTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                TryDeleteTestRoot(testRoot);
            }
        }

        private static void TestStableSeparateKeyAndTemplate()
        {
            ServerSession session = CreateSensitiveSession(3, "stable");
            string key = UserReportMemoStore.CreateStableKey(session);
            string repeated = UserReportMemoStore.CreateStableKey(session);
            string ordinaryRaidNoteKey = RaidNoteStore.CreateStableKey(session);
            Assert(key.Length == 64 && key.All(IsLowerHex), "메모 키는 64자리 소문자 16진수여야 합니다.");
            Assert(string.Equals(key, repeated, StringComparison.Ordinal), "동일 세션의 키는 안정적이어야 합니다.");
            Assert(!string.Equals(key, ordinaryRaidNoteKey, StringComparison.OrdinalIgnoreCase),
                "유저신고 메모 키는 일반 레이드 메모와 별도 도메인이어야 합니다.");

            string template = UserReportMemoUi.BuildDefaultTemplate(3);
            string[] lines = template.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert(lines.Length == 3, "신고 3건의 기본 템플릿은 3줄이어야 합니다.");
            for (int index = 0; index < lines.Length; index++)
            {
                string expectedPrefix = (index + 1) + ". 유저네임:";
                Assert(lines[index].StartsWith(expectedPrefix, StringComparison.Ordinal),
                    "각 템플릿 줄에 순번이 있어야 합니다.");
                Assert(lines[index].Contains("신고사유:"),
                    "닉네임과 신고사유 양식은 같은 줄에 있어야 합니다.");
            }
            Assert(UserReportMemoUi.BuildDefaultTemplate(0).Split(
                new[] { Environment.NewLine }, StringSplitOptions.None).Length == 1,
                "신고 수가 0이어도 템플릿은 최소 1줄이어야 합니다.");
        }

        private static void TestStorageBoundaries(string testRoot)
        {
            string folder = Path.Combine(testRoot, "store");
            var store = new UserReportMemoStore(folder);
            ServerSession session = CreateSensitiveSession(3, "storage");
            string key = UserReportMemoStore.CreateStableKey(session);
            UserReportMemoRecord record = store.CreateFor(session);
            record.Game = "tampered";

            record.MemoText = "first memo";
            record.Entries.Add(new UserReportMemoEntry
            {
                Nickname = "first-user",
                Reason = "first-reason"
            });
            store.Save(session, record);
            DateTime created = record.CreatedUtc;
            record.MemoText = "second memo";
            record.Entries[0].Nickname = "second-user";
            record.Entries[0].Reason = "second-reason";
            store.Save(session, record);
            Assert(record.UpdatedUtc >= created, "수정 시각은 생성 시각보다 빠를 수 없습니다.");

            string target = Path.Combine(folder, key + ".json");
            string backup = target + ".bak";
            Assert(File.Exists(target), "안전한 해시 파일명의 JSON이 생성되어야 합니다.");
            Assert(File.Exists(backup), "두 번째 저장에서 원자 교체용 백업이 생성되어야 합니다.");
            Assert(Directory.EnumerateFiles(folder).All(path =>
                Path.GetFileName(path).StartsWith(key, StringComparison.OrdinalIgnoreCase)),
                "저장 파일명에는 원문 세션 정보가 들어가면 안 됩니다.");

            string json = File.ReadAllText(target, Encoding.UTF8);
            Assert(json.Contains("EFT"), "보관함 표시용 게임 구분이 저장되어야 합니다.");
            Assert(json.Contains("\"Entries\"") && json.Contains("second-user")
                && json.Contains("second-reason"),
                "유저네임과 신고사유는 구조화된 JSON 항목으로 저장되어야 합니다.");
            Assert(!json.Contains(session.SessionKey), "원문 SessionKey가 JSON에 저장되면 안 됩니다.");
            Assert(!json.Contains("SID-sensitive"), "SID 원문이 JSON에 저장되면 안 됩니다.");
            Assert(!json.Contains("PrivatePlayer"), "계정 식별자 원문이 JSON에 저장되면 안 됩니다.");
            Assert(!json.Contains("C:\\Private\\GameLogs"), "로컬 경로 원문이 JSON에 저장되면 안 됩니다.");

            File.WriteAllText(target, "{corrupt", new UTF8Encoding(false));
            IList<UserReportMemoRecord> recoveredAll = store.LoadAll();
            Assert(recoveredAll.Count == 1 && recoveredAll[0].MemoText == "first memo",
                "전체 보관함 열거도 손상된 기본 파일 대신 백업을 사용해야 합니다.");
            UserReportMemoRecord recovered = store.Load(session);
            Assert(recovered != null && recovered.MemoText == "first memo",
                "손상된 기본 파일은 백업에서 복구해 읽어야 합니다.");
            Assert(recovered.Entries.Count == 1
                && recovered.Entries[0].Nickname == "first-user"
                && recovered.Entries[0].Reason == "first-reason",
                "백업 복구에서도 구조화된 신고 항목이 유지되어야 합니다.");

            record = recovered;
            record.MemoText = new string('x', UserReportMemoStore.MaximumMemoLength);
            store.Save(session, record);
            UserReportMemoRecord maximum = store.Load(session);
            Assert(maximum != null && maximum.MemoText.Length == UserReportMemoStore.MaximumMemoLength,
                "허용 최대 길이의 입력은 손실 없이 저장되어야 합니다.");

            record.MemoText = new string('y', UserReportMemoStore.MaximumMemoLength + 1);
            store.Save(session, record);
            UserReportMemoRecord limited = store.Load(session);
            Assert(limited != null && limited.MemoText.Length == UserReportMemoStore.MaximumMemoLength,
                "최대 길이를 넘는 직접 API 입력은 저장 경계에서 제한되어야 합니다.");

            record.Entries[0].Nickname = new string('n', UserReportMemoStore.MaximumNicknameLength + 1);
            record.Entries[0].Reason = new string('r', UserReportMemoStore.MaximumReasonLength + 1);
            store.Save(session, record);
            UserReportMemoRecord limitedEntry = store.Load(session);
            Assert(limitedEntry.Entries[0].Nickname.Length == UserReportMemoStore.MaximumNicknameLength
                && limitedEntry.Entries[0].Reason.Length == UserReportMemoStore.MaximumReasonLength,
                "구조화 입력은 저장 경계에서 각 필드의 허용 길이로 제한되어야 합니다.");

            store.Delete(session);
            Assert(!store.Exists(session), "삭제 후 메모가 존재하면 안 됩니다.");
            Assert(!Directory.EnumerateFiles(folder).Any(), "삭제 시 기본·백업·임시 파일을 모두 제거해야 합니다.");
        }

        private static void TestArchiveAndLegacyPlaceholder(string testRoot)
        {
            string raidFolder = Path.Combine(testRoot, "archive-raids");
            string reportFolder = Path.Combine(testRoot, "archive-reports");
            var raidStore = new RaidNoteStore(raidFolder);
            var reportStore = new UserReportMemoStore(reportFolder);
            ServerSession raidSession = CreateSensitiveSession(2, "archive");
            raidSession.SessionStarted = new DateTime(2026, 8, 14, 10, 20, 30, DateTimeKind.Utc);
            raidSession.MapName = "Factory";

            RaidNoteRecord raid = raidStore.CreateFor(raidSession);
            raid.NoteText = "ordinary archive memo";
            raidStore.Save(raidSession, raid);

            UserReportMemoRecord report = reportStore.CreateFor(raidSession);
            report.MemoText = RaidNoteStore.LegacyDefaultNoteTemplate;
            report.Entries.Add(new UserReportMemoEntry
            {
                Nickname = "archive-user",
                Reason = "archive-reason"
            });
            reportStore.Save(raidSession, report);

            IList<UserReportMemoRecord> allReports = reportStore.LoadAll();
            Assert(allReports.Count == 1, "유저신고 메모는 최근 로그 목록과 무관하게 전체 열거되어야 합니다.");
            Assert(allReports[0].Game == "EFT"
                && allReports[0].MapName == "Factory"
                && allReports[0].RaidStartedUtc == raidSession.SessionStarted,
                "보관함 표시용 안전한 게임·맵·레이드 시각 메타데이터가 유지되어야 합니다.");

            ServerSession legacySession = CreateSensitiveSession(1, "legacy-report");
            UserReportMemoRecord legacyReport = reportStore.CreateFor(legacySession);
            legacyReport.Game = string.Empty;
            legacyReport.MapName = string.Empty;
            legacyReport.RaidStartedUtc = default(DateTime);
            legacyReport.MemoText = "legacy report memo";
            reportStore.Save(legacyReport.Key, legacyReport);
            string legacyPath = Path.Combine(reportFolder, legacyReport.Key + ".json");
            string v066Json = File.ReadAllText(legacyPath, Encoding.UTF8)
                .Replace("\"Entries\":[],", string.Empty);
            Assert(!v066Json.Contains("\"Entries\""),
                "v0.6.6 호환 테스트 JSON에는 구조화 항목이 없어야 합니다.");
            File.WriteAllText(legacyPath, v066Json, new UTF8Encoding(false));
            UserReportMemoRecord loadedLegacy = reportStore.Load(legacyReport.Key);
            Assert(loadedLegacy != null && loadedLegacy.RaidStartedUtc == default(DateTime),
                "메타데이터가 없던 기존 유저신고 메모도 계속 읽혀야 합니다.");
            Assert(loadedLegacy.MemoText == "legacy report memo"
                && loadedLegacy.Entries != null
                && loadedLegacy.Entries.Count == 0,
                "v0.6.6 MemoText는 유실 없이 읽고 빈 구조화 목록으로 마이그레이션해야 합니다.");

            RaidNoteRecord legacyRaid = raidStore.CreateFor(CreateSensitiveSession(1, "legacy-raid"));
            legacyRaid.NoteText = RaidNoteStore.LegacyDefaultNoteTemplate;
            raidStore.Save(legacyRaid.Key, legacyRaid);
            RaidNoteRecord loadedLegacyRaid = raidStore.Load(legacyRaid.Key);
            Assert(loadedLegacyRaid != null && loadedLegacyRaid.NoteText.Length == 0,
                "기존 기본 템플릿 레코드는 빈 메모로 정규화되어야 합니다.");
            string legacyJson = File.ReadAllText(
                Path.Combine(raidFolder, legacyRaid.Key + ".json"), Encoding.UTF8);
            Assert(!legacyJson.Contains("유저닉네임"),
                "회색 placeholder 텍스트는 레이드 메모 JSON에 저장되면 안 됩니다.");

            using (var form = new RaidNoteForm(loadedLegacyRaid, raidStore))
            {
                ShowOffScreen(form);
                TextBox note = GetPrivateField<TextBox>(form, "_noteTextBox");
                Label example = GetPrivateField<Label>(form, "_notePlaceholderLabel");
                Label nickname = GetPrivateField<Label>(form, "_nicknamePlaceholderLabel");
                Assert(note.TextLength == 0, "기본 양식은 실제 텍스트박스 값이 아니어야 합니다.");
                Assert(example.Visible && nickname.Visible,
                    "빈 메모에는 예시와 유저닉네임 회색 placeholder가 함께 보여야 합니다.");
                note.Text = "actual input";
                Application.DoEvents();
                Assert(!example.Visible && !nickname.Visible,
                    "실제 입력을 시작하면 두 placeholder가 사라져야 합니다.");
            }

            using (var archive = new RaidNoteArchiveForm(raidStore, reportStore))
            {
                ShowOffScreen(archive);
                DataGridView grid = GetPrivateField<DataGridView>(archive, "_grid");
                var kinds = grid.Rows.Cast<DataGridViewRow>()
                    .Select(row => Convert.ToString(row.Cells["kind"].Value))
                    .ToList();
                Assert(kinds.Contains("레이드 메모") && kinds.Contains("유저신고 메모"),
                    "메모 보관함은 두 저장소를 함께 표시하고 종류를 구분해야 합니다.");
                Assert(grid.Rows.Count == raidStore.LoadAll().Count + reportStore.LoadAll().Count,
                    "보관함은 최근 로그 수와 무관하게 두 저장소의 전체 레코드를 표시해야 합니다.");
                DataGridViewRow reportRow = grid.Rows.Cast<DataGridViewRow>().First(row =>
                    Convert.ToString(row.Cells["kind"].Value) == "유저신고 메모"
                    && Convert.ToString(row.Cells["map"].Value) == "Factory");
                Assert(Convert.ToString(reportRow.Cells["preview"].Value) != "-",
                    "유저신고 메모는 일반 메모의 구 템플릿과 같은 내용이어도 숨기면 안 됩니다.");
                Assert(Convert.ToString(reportRow.Cells["preview"].Value).Contains("archive-user"),
                    "보관함 미리보기에는 구조화된 유저네임이 표시되어야 합니다.");
                archive.Close();
            }

            reportStore.Delete(report.Key);
            Assert(reportStore.LoadAll().All(item => item.Key != report.Key),
                "보관함에서 사용하는 키 기반 삭제 후 유저신고 메모가 다시 열거되면 안 됩니다.");
        }

        private static void TestSaveButtonBehavior(string testRoot)
        {
            ServerSession successSession = CreateSensitiveSession(2, "ui-success");
            var successStore = new UserReportMemoStore(Path.Combine(testRoot, "ui-success"));
            using (var form = new UserReportMemoForm(successSession, successStore))
            {
                ShowOffScreen(form);
                TextBox nickname = FindNamedControl<TextBox>(form, "ReportNickname1");
                TextBox reason = FindNamedControl<TextBox>(form, "ReportReason1");
                Assert(nickname != null && reason != null,
                    "고정 라벨 옆에 유저네임과 신고사유 입력칸이 있어야 합니다.");
                nickname.Text = "saved-user";
                reason.Text = "saved-reason";
                Button save = FindButton(form, "저장");
                Assert(save != null, "저장 버튼을 찾을 수 없습니다.");
                save.PerformClick();
                Application.DoEvents();
                Assert(!form.Visible, "저장 성공 시 메모 창이 닫혀야 합니다.");
                Assert(form.Changed && successStore.Exists(successSession),
                    "저장 성공 시 변경 상태와 로컬 파일이 남아야 합니다.");
                UserReportMemoRecord saved = successStore.Load(successSession);
                Assert(saved != null && saved.Entries.Count == 2
                    && saved.Entries[0].Nickname == "saved-user"
                    && saved.Entries[0].Reason == "saved-reason",
                    "고정 입력칸의 값이 구조화 항목으로 저장되어야 합니다.");
            }

            string blocker = Path.Combine(testRoot, "ui-failure-blocker");
            File.WriteAllText(blocker, "This file intentionally blocks directory creation.");
            ServerSession failureSession = CreateSensitiveSession(1, "ui-failure");
            var failureStore = new UserReportMemoStore(blocker);
            using (var form = new UserReportMemoForm(failureSession, failureStore))
            {
                ShowOffScreen(form);
                Button save = FindButton(form, "저장");
                Assert(save != null, "저장 버튼을 찾을 수 없습니다.");
                save.PerformClick();
                Application.DoEvents();
                Assert(form.Visible, "저장 실패 시 메모 창이 열려 있어야 합니다.");
                Assert(!form.Changed, "저장 실패를 변경 완료로 표시하면 안 됩니다.");
            }
        }

        private static void TestStructuredEditorAndEntryLimit(string testRoot)
        {
            string folder = Path.Combine(testRoot, "structured-ui");
            var store = new UserReportMemoStore(folder);
            ServerSession legacySession = CreateSensitiveSession(2, "structured-legacy");
            UserReportMemoRecord legacy = store.CreateFor(legacySession);
            legacy.MemoText = "v0.6.6에서 작성한 원문\r\n두 번째 줄";
            store.Save(legacySession, legacy);

            using (var form = new UserReportMemoForm(legacySession, store))
            {
                ShowOffScreen(form);
                Assert(FindLabel(form, "1. 유저네임:") != null
                    && FindLabel(form, "신고사유:") != null,
                    "순번·유저네임·신고사유는 지울 수 없는 고정 라벨이어야 합니다.");
                TextBox legacyText = FindNamedControl<TextBox>(form, "LegacyMemoText");
                Assert(legacyText != null
                    && legacyText.Text == "v0.6.6에서 작성한 원문\r\n두 번째 줄"
                    && legacyText.Visible,
                    "v0.6.6 자유 메모는 별도 기존 메모 영역에 원문 그대로 표시되어야 합니다.");
                form.Close();
            }

            ServerSession excessive = CreateSensitiveSession(
                UserReportMemoStore.MaximumReportCount, "structured-limit");
            using (var form = new UserReportMemoForm(
                excessive,
                new UserReportMemoStore(Path.Combine(testRoot, "structured-limit"))))
            {
                ShowOffScreen(form);
                int nicknameInputs = CountNamedControls(form, "ReportNickname");
                int reasonInputs = CountNamedControls(form, "ReportReason");
                Assert(nicknameInputs == 50 && reasonInputs == 50,
                    "비정상적으로 큰 신고 건수는 입력행 50개로 제한해 UI 폭주를 막아야 합니다.");
                Assert(FindLabelContaining(form, "앞의 50건") != null,
                    "입력행 제한이 적용되면 사용자에게 표시 범위를 알려야 합니다.");
                form.Close();
            }
        }

        private static ServerSession CreateSensitiveSession(int reportCount, string suffix)
        {
            return new ServerSession
            {
                Game = TarkovGame.Eft,
                SessionKey = "C:\\Private\\GameLogs\\SID-sensitive-PrivatePlayer-" + suffix,
                SessionStarted = default(DateTime),
                UserReportCount = reportCount
            };
        }

        private static void ShowOffScreen(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-30000, -30000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
        }

        private static Button FindButton(Control root, string text)
        {
            var queue = new Queue<Control>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                Control current = queue.Dequeue();
                Button button = current as Button;
                if (button != null && string.Equals(button.Text, text, StringComparison.Ordinal))
                    return button;
                foreach (Control child in current.Controls) queue.Enqueue(child);
            }
            return null;
        }

        private static T FindNamedControl<T>(Control root, string name) where T : Control
        {
            var queue = new Queue<Control>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                Control current = queue.Dequeue();
                T match = current as T;
                if (match != null && string.Equals(match.Name, name, StringComparison.Ordinal))
                    return match;
                foreach (Control child in current.Controls) queue.Enqueue(child);
            }
            return null;
        }

        private static Label FindLabel(Control root, string text)
        {
            return FindLabelCore(root, text, false);
        }

        private static Label FindLabelContaining(Control root, string text)
        {
            return FindLabelCore(root, text, true);
        }

        private static Label FindLabelCore(Control root, string text, bool contains)
        {
            var queue = new Queue<Control>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                Control current = queue.Dequeue();
                Label label = current as Label;
                if (label != null
                    && (contains
                        ? label.Text.IndexOf(text, StringComparison.Ordinal) >= 0
                        : string.Equals(label.Text, text, StringComparison.Ordinal)))
                    return label;
                foreach (Control child in current.Controls) queue.Enqueue(child);
            }
            return null;
        }

        private static int CountNamedControls(Control root, string prefix)
        {
            int count = 0;
            var queue = new Queue<Control>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                Control current = queue.Dequeue();
                if (current.Name != null
                    && current.Name.StartsWith(prefix, StringComparison.Ordinal))
                    count++;
                foreach (Control child in current.Controls) queue.Enqueue(child);
            }
            return count;
        }

        private static T GetPrivateField<T>(object instance, string name) where T : class
        {
            FieldInfo field = instance.GetType().GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(field != null, "테스트할 필드를 찾을 수 없습니다: " + name);
            T value = field.GetValue(instance) as T;
            Assert(value != null, "테스트할 필드 값이 없습니다: " + name);
            return value;
        }

        private static bool IsLowerHex(char character)
        {
            return (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f');
        }

        private static void TryDeleteTestRoot(string testRoot)
        {
            try
            {
                string fullRoot = Path.GetFullPath(testRoot);
                string tempRoot = Path.GetFullPath(Path.GetTempPath());
                string name = Path.GetFileName(fullRoot);
                if (fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                    && name.StartsWith("TarkovServerGuardUserReportMemoTests_", StringComparison.Ordinal)
                    && Directory.Exists(fullRoot))
                    Directory.Delete(fullRoot, true);
            }
            catch
            {
                // Test result should not be hidden by best-effort temporary cleanup.
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
