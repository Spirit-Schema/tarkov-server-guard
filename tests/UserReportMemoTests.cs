// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
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
                TestArchiveBatchSelection(testRoot);
                TestArchiveHeaderSorting(testRoot);
                TestRaidContextStorageAndArchiveDisplay(testRoot);
                TestArchiveBackupUi(testRoot);
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
                Assert(grid.ColumnHeadersBorderStyle == DataGridViewHeaderBorderStyle.Single,
                    "메모 보관함 헤더는 서버차단현황과 같은 단일 경계선을 사용해야 합니다.");
                FieldInfo headerBorderField = typeof(RaidNoteArchiveForm).GetField(
                    "HeaderBorder",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert(headerBorderField != null
                    && (System.Drawing.Color)headerBorderField.GetValue(null)
                        == System.Drawing.Color.FromArgb(57, 68, 80),
                    "메모 보관함 헤더 경계색은 서버차단현황 헤더와 같아야 합니다.");
                Assert(grid.GridColor == System.Drawing.Color.FromArgb(54, 63, 74),
                    "헤더 테두리 통일이 메모 보관함 본문 행 경계색까지 바꾸면 안 됩니다.");
                var kinds = grid.Rows.Cast<DataGridViewRow>()
                    .Select(row => Convert.ToString(row.Cells["kind"].Value))
                    .ToList();
                Assert(kinds.Contains("레이드 메모") && kinds.Contains("유저신고 메모"),
                    "메모 보관함은 두 저장소를 함께 표시하고 종류를 구분해야 합니다.");
                Assert(grid.Rows.Count == raidStore.LoadAll().Count + reportStore.LoadAll().Count,
                    "보관함은 최근 로그 수와 무관하게 두 저장소의 전체 레코드를 표시해야 합니다.");
                DataGridViewRow reportRow = grid.Rows.Cast<DataGridViewRow>().First(row =>
                    Convert.ToString(row.Cells["kind"].Value) == "유저신고 메모"
                    && GetDisplayedMapName(row) == "Factory");
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

        private static void TestArchiveBatchSelection(string testRoot)
        {
            string raidFolder = Path.Combine(testRoot, "batch-archive-raids");
            string reportFolder = Path.Combine(testRoot, "batch-archive-reports");
            var raidStore = new RaidNoteStore(raidFolder);
            var reportStore = new UserReportMemoStore(reportFolder);

            ServerSession raidSession = CreateSensitiveSession(1, "batch-raid");
            raidSession.MapName = "BatchRaid";
            RaidNoteRecord raid = raidStore.CreateFor(raidSession);
            raid.NoteText = "batch raid note";
            raidStore.Save(raidSession, raid);

            ServerSession reportSession = CreateSensitiveSession(1, "batch-report");
            reportSession.MapName = "BatchReport";
            UserReportMemoRecord report = reportStore.CreateFor(reportSession);
            report.MemoText = "batch report memo";
            reportStore.Save(reportSession, report);

            ServerSession staleSession = CreateSensitiveSession(1, "stale-report");
            staleSession.MapName = "StaleReport";
            UserReportMemoRecord staleReport = reportStore.CreateFor(staleSession);
            staleReport.MemoText = "stale report memo";
            reportStore.Save(staleSession, staleReport);

            ServerSession collisionSession = CreateSensitiveSession(1, "collision-raid");
            collisionSession.MapName = "CollisionRaid";
            RaidNoteRecord collisionRaid = raidStore.CreateFor(collisionSession);
            collisionRaid.NoteText = "same key, other store";
            raidStore.Save(staleReport.Key, collisionRaid);

            using (var archive = new RaidNoteArchiveForm(raidStore, reportStore))
            {
                ShowOffScreen(archive);
                DataGridView grid = GetPrivateField<DataGridView>(archive, "_grid");
                Button selectedDelete = GetPrivateField<Button>(archive, "_deleteSelectedButton");
                Button currentDelete = GetPrivateField<Button>(archive, "_deleteButton");
                Button refresh = GetPrivateField<Button>(archive, "_refreshButton");
                Assert(grid.Columns[0].Name == "selected"
                    && grid.Columns[0] is DataGridViewCheckBoxColumn,
                    "메모 보관함의 첫 열은 선택 체크박스여야 합니다.");
                Assert(!grid.ReadOnly && !grid.Columns["selected"].ReadOnly,
                    "선택 체크박스는 편집 가능해야 합니다.");
                Assert(grid.Columns.Cast<DataGridViewColumn>()
                    .Where(column => column.Name != "selected")
                    .All(column => column.ReadOnly),
                    "선택 열을 제외한 메모 본문 열은 읽기 전용이어야 합니다.");
                Assert(currentDelete.Text == "삭제" && selectedDelete.Text == "선택 삭제",
                    "현재행 삭제와 선택 삭제는 별도 버튼으로 유지되어야 합니다.");
                int archiveCount = grid.Rows.Count;
                string noneSelectedSummary = "전체 메모 " + archiveCount
                    + "개 중 0개가 선택되었습니다.";
                Assert(grid.AccessibleName == "메모 선택 목록"
                    && grid.AccessibleDescription == "메모 선택 목록입니다. "
                        + noneSelectedSummary
                        + " 선택 열 머리글은 전체 선택 또는 전체 해제이며, Ctrl+A는 전체 선택입니다."
                    && grid.AccessibilityObject.Name == grid.AccessibleName
                    && grid.AccessibilityObject.Description == grid.AccessibleDescription,
                    "메모 목록은 선택 개수와 전체 선택 키를 정확한 접근성 설명으로 제공해야 합니다.");
                AccessibleObject archiveSelectionHeader =
                    grid.Columns["selected"].HeaderCell.AccessibilityObject;
                Assert(grid.Columns["selected"].HeaderText == string.Empty
                    && grid.Columns["selected"].HeaderCell.ToolTipText
                        == "모든 메모를 선택하거나 전체 해제합니다."
                    && archiveSelectionHeader.Name
                        == "메모 전체 선택 또는 전체 해제"
                    && archiveSelectionHeader.Description
                        == "선택 열 머리글입니다. 클릭하면 모든 메모를 선택하거나 선택 해제합니다. "
                            + noneSelectedSummary
                    && archiveSelectionHeader.DefaultAction == "전체 선택"
                    && (archiveSelectionHeader.State
                        & (AccessibleStates.Checked | AccessibleStates.Mixed))
                        == AccessibleStates.None,
                    "문자 대신 표시되는 선택 헤더 체크박스는 용도·다음 동작·미선택 상태를 제공해야 합니다.");
                string uncheckedHeaderPixels = CaptureSelectionHeaderVisual(grid);
                InvokePrivate(archive, "SetAllRowsChecked", true);
                string checkedHeaderPixels = CaptureSelectionHeaderVisual(grid);
                Assert(archiveSelectionHeader.DefaultAction == "전체 해제"
                    && (archiveSelectionHeader.State
                        & (AccessibleStates.Checked | AccessibleStates.Mixed))
                        == AccessibleStates.Checked
                    && checkedHeaderPixels != uncheckedHeaderPixels,
                    "전체 선택 상태는 체크 표시와 접근성 체크 상태를 함께 갱신해야 합니다.");
                grid.Rows[0].Cells["selected"].Value = false;
                string partialHeaderPixels = CaptureSelectionHeaderVisual(grid);
                Assert(archiveSelectionHeader.DefaultAction == "전체 선택"
                    && (archiveSelectionHeader.State
                        & (AccessibleStates.Checked | AccessibleStates.Mixed))
                        == AccessibleStates.Mixed
                    && partialHeaderPixels != uncheckedHeaderPixels
                    && partialHeaderPixels != checkedHeaderPixels,
                    "부분 선택 상태는 가로 표시와 접근성 혼합 상태를 함께 갱신해야 합니다.");
                InvokePrivate(archive, "SetAllRowsChecked", false);
                Assert(grid.Rows[0].Cells["selected"].AccessibilityObject.Name
                        == "메모 선택 체크박스 행 1"
                    && grid.Rows[0].Cells["selected"].AccessibilityObject.Description
                        == "이 메모를 선택 삭제 대상에 포함합니다. 현재 선택되지 않았습니다. "
                            + noneSelectedSummary,
                    "선택 체크박스 셀은 행과 현재 선택 상태를 접근성 정보로 제공해야 합니다.");
                Assert(currentDelete.AccessibleName == "현재 메모 삭제"
                    && currentDelete.AccessibleDescription
                        == "현재 행에 포커스된 메모 1개를 삭제합니다."
                    && selectedDelete.AccessibleName == "선택 삭제"
                    && selectedDelete.AccessibleDescription == "체크박스로 선택한 메모가 없습니다."
                    && currentDelete.AccessibilityObject.Name == currentDelete.AccessibleName
                    && currentDelete.AccessibilityObject.Description
                        == currentDelete.AccessibleDescription
                    && selectedDelete.AccessibilityObject.Name == selectedDelete.AccessibleName
                    && selectedDelete.AccessibilityObject.Description
                        == selectedDelete.AccessibleDescription,
                    "현재행 삭제와 체크박스 선택 삭제의 접근성 이름과 설명을 구분해야 합니다.");
                Assert(!selectedDelete.Enabled, "체크한 메모가 없으면 선택 삭제가 비활성화되어야 합니다.");

                DataGridViewCell firstCheckCell = grid.Rows[0].Cells["selected"];
                grid.CurrentCell = firstCheckCell;
                Assert(grid.BeginEdit(false), "첫 선택 체크박스를 편집 상태로 만들 수 있어야 합니다.");
                IDataGridViewEditingCell firstEditingCell = firstCheckCell as IDataGridViewEditingCell;
                Assert(firstEditingCell != null, "선택 체크박스는 편집 셀 상태를 제공해야 합니다.");
                firstEditingCell.EditingCellFormattedValue = false;
                grid.NotifyCurrentCellDirty(true);
                Assert(grid.IsCurrentCellInEditMode,
                    "회귀 테스트는 편집 중인 현재 체크박스 상태를 재현해야 합니다.");
                int firstCellPaintCount = 0;
                bool firstCellPaintedChecked = false;
                DataGridViewCellPaintingEventHandler firstCellPaintHandler = delegate(
                    object paintSender,
                    DataGridViewCellPaintingEventArgs paintArgs)
                {
                    if (paintArgs.RowIndex != firstCheckCell.RowIndex
                        || paintArgs.ColumnIndex != firstCheckCell.ColumnIndex)
                        return;
                    firstCellPaintCount++;
                    firstCellPaintedChecked = Convert.ToBoolean(firstCheckCell.FormattedValue);
                };
                grid.CellPainting += firstCellPaintHandler;
                var selectAllKeys = new KeyEventArgs(Keys.Control | Keys.A);
                InvokePrivate(archive, "GridKeyDown", grid, selectAllKeys);
                Assert(grid.Rows.Cast<DataGridViewRow>().All(IsChecked),
                    "Ctrl+A는 모든 메모 행을 체크해야 합니다.");
                Assert(selectAllKeys.Handled && selectAllKeys.SuppressKeyPress && selectedDelete.Enabled,
                    "Ctrl+A를 처리한 뒤 선택 삭제가 활성화되어야 합니다.");
                Assert(ReferenceEquals(grid.CurrentCell, firstCheckCell),
                    "Ctrl+A는 현재 셀이나 포커스를 다른 행으로 옮기면 안 됩니다.");
                Assert(!grid.IsCurrentCellInEditMode
                    && Convert.ToBoolean(firstCheckCell.FormattedValue),
                    "Ctrl+A는 첫/current 행 체크박스의 편집 상태를 끝내고 표시값을 즉시 갱신해야 합니다.");
                string allSelectedSummary = "전체 메모 " + archiveCount
                    + "개 중 " + archiveCount + "개가 선택되었습니다.";
                string ctrlAAccessibleDescription = grid.AccessibleDescription;
                string ctrlAHeaderDescription = grid.Columns["selected"]
                    .HeaderCell.AccessibilityObject.Description;
                Assert(ctrlAAccessibleDescription == "메모 선택 목록입니다. "
                        + allSelectedSummary
                        + " 선택 열 머리글은 전체 선택 또는 전체 해제이며, Ctrl+A는 전체 선택입니다."
                    && ctrlAHeaderDescription
                        == "선택 열 머리글입니다. 클릭하면 모든 메모를 선택하거나 선택 해제합니다. "
                            + allSelectedSummary
                    && archiveSelectionHeader.DefaultAction == "전체 해제"
                    && (archiveSelectionHeader.State
                        & (AccessibleStates.Checked | AccessibleStates.Mixed))
                        == AccessibleStates.Checked
                    && firstCheckCell.AccessibilityObject.Description
                        == "이 메모를 선택 삭제 대상에 포함합니다. 현재 선택되었습니다. "
                            + allSelectedSummary
                    && selectedDelete.AccessibleDescription
                        == "체크박스로 선택한 메모 " + archiveCount + "개를 삭제합니다."
                    && grid.AccessibilityObject.Description == ctrlAAccessibleDescription
                    && selectedDelete.AccessibilityObject.Description
                        == selectedDelete.AccessibleDescription,
                    "Ctrl+A 직후 목록·헤더·현재 체크박스·선택 삭제 버튼의 접근성 선택 개수가 같아야 합니다.");
                DrawGridForPaintVerification(grid);
                Assert(firstCellPaintCount > 0 && firstCellPaintedChecked,
                    "Ctrl+A 직후 첫/current 행 체크박스가 체크 상태로 다시 그려져야 합니다.");
                grid.CellPainting -= firstCellPaintHandler;

                InvokePrivate(archive, "SetAllRowsChecked", false);
                grid.CurrentCell = firstCheckCell;
                Assert(grid.BeginEdit(false), "헤더 테스트에서 첫 체크박스를 편집 상태로 만들 수 있어야 합니다.");
                firstEditingCell.EditingCellFormattedValue = false;
                grid.NotifyCurrentCellDirty(true);
                int headerPaintCount = 0;
                bool headerPaintedChecked = false;
                DataGridViewCellPaintingEventHandler headerPaintHandler = delegate(
                    object paintSender,
                    DataGridViewCellPaintingEventArgs paintArgs)
                {
                    if (paintArgs.RowIndex != firstCheckCell.RowIndex
                        || paintArgs.ColumnIndex != firstCheckCell.ColumnIndex)
                        return;
                    headerPaintCount++;
                    headerPaintedChecked = Convert.ToBoolean(firstCheckCell.FormattedValue);
                };
                grid.CellPainting += headerPaintHandler;
                var headerClick = new DataGridViewCellMouseEventArgs(
                    grid.Columns["selected"].Index,
                    -1,
                    2,
                    2,
                    new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0));
                InvokePrivate(archive, "GridColumnHeaderMouseClick", grid, headerClick);
                Assert(grid.Rows.Cast<DataGridViewRow>().All(IsChecked),
                    "선택 헤더를 누르면 모든 메모 행을 체크해야 합니다.");
                Assert(grid.AccessibleDescription == ctrlAAccessibleDescription
                    && grid.Columns["selected"].HeaderCell.AccessibilityObject.Description
                        == ctrlAHeaderDescription
                    && selectedDelete.AccessibleDescription
                        == "체크박스로 선택한 메모 " + archiveCount + "개를 삭제합니다.",
                    "선택 헤더와 Ctrl+A는 같은 선택 결과와 접근성 상태를 만들어야 합니다.");
                Assert(ReferenceEquals(grid.CurrentCell, firstCheckCell)
                    && !grid.IsCurrentCellInEditMode
                    && Convert.ToBoolean(firstCheckCell.FormattedValue),
                    "선택 헤더는 포커스를 옮기지 않고 첫/current 행 체크 표시를 즉시 갱신해야 합니다.");
                DrawGridForPaintVerification(grid);
                Assert(headerPaintCount > 0 && headerPaintedChecked,
                    "선택 헤더 직후 첫/current 행 체크박스가 체크 상태로 다시 그려져야 합니다.");
                grid.CellPainting -= headerPaintHandler;
                InvokePrivate(archive, "GridColumnHeaderMouseClick", grid, headerClick);
                Assert(grid.Rows.Cast<DataGridViewRow>().All(row => !IsChecked(row)),
                    "모두 체크된 상태에서 선택 헤더를 다시 누르면 전체 체크를 해제해야 합니다.");
                Assert(grid.AccessibleDescription == "메모 선택 목록입니다. "
                        + noneSelectedSummary
                        + " 선택 열 머리글은 전체 선택 또는 전체 해제이며, Ctrl+A는 전체 선택입니다."
                    && grid.Columns["selected"].HeaderCell.AccessibilityObject.Description
                        == "선택 열 머리글입니다. 클릭하면 모든 메모를 선택하거나 선택 해제합니다. "
                            + noneSelectedSummary
                    && archiveSelectionHeader.DefaultAction == "전체 선택"
                    && (archiveSelectionHeader.State
                        & (AccessibleStates.Checked | AccessibleStates.Mixed))
                        == AccessibleStates.None
                    && firstCheckCell.AccessibilityObject.Description
                        == "이 메모를 선택 삭제 대상에 포함합니다. 현재 선택되지 않았습니다. "
                            + noneSelectedSummary
                    && selectedDelete.AccessibleDescription == "체크박스로 선택한 메모가 없습니다.",
                    "전체 해제 직후에도 화면 읽기 프로그램에 선택 0개 상태를 일관되게 제공해야 합니다.");
                DataGridViewRow collisionRow = FindRowByMap(grid, "CollisionRaid");
                grid.CurrentCell = collisionRow.Cells["kind"];
                var spaceKey = new KeyEventArgs(Keys.Space);
                InvokePrivate(archive, "GridKeyDown", grid, spaceKey);
                Assert(IsChecked(collisionRow) && spaceKey.Handled && spaceKey.SuppressKeyPress,
                    "Space는 현재행 체크 상태를 토글해야 합니다.");
                refresh.PerformClick();
                Application.DoEvents();
                Assert(IsChecked(FindRowByMap(grid, "CollisionRaid")),
                    "새로고침 뒤에도 동일 identity의 체크 상태를 보존해야 합니다.");

                InvokePrivate(archive, "SetBusy", true, "테스트 중");
                Assert(!grid.Enabled && !refresh.Enabled && !selectedDelete.Enabled,
                    "busy 상태에서는 표·새로고침·선택 삭제를 비활성화해야 합니다.");
                InvokePrivate(archive, "SetBusy", false, null);

                InvokePrivate(archive, "SetAllRowsChecked", false);
                FindRowByMap(grid, "BatchRaid").Cells["selected"].Value = true;
                FindRowByMap(grid, "BatchReport").Cells["selected"].Value = true;
                FindRowByMap(grid, "StaleReport").Cells["selected"].Value = true;
                Application.DoEvents();
                object targets = InvokePrivate(archive, "GetCheckedDeleteTargets");

                reportStore.Delete(staleReport.Key);
                object result = InvokePrivate(archive, "DeleteRevalidatedTargets", targets);
                Assert(GetProperty<int>(result, "RequestedCount") == 3
                    && GetProperty<int>(result, "SucceededCount") == 2
                    && GetProperty<int>(result, "MissingCount") == 1
                    && GetProperty<int>(result, "FailedCount") == 0,
                    "혼합 저장소 선택 삭제는 성공·이미 없음 결과를 항목별로 집계해야 합니다.");
                Assert(!raidStore.Exists(raid.Key) && !reportStore.Exists(report.Key),
                    "현재 존재하는 선택 메모만 각 저장소에서 삭제해야 합니다.");
                Assert(raidStore.Exists(staleReport.Key),
                    "유저신고 메모의 stale 키가 같은 키의 레이드 메모를 삭제하면 안 됩니다.");

                refresh.PerformClick();
                Application.DoEvents();
                Assert(grid.Rows.Cast<DataGridViewRow>().All(row =>
                    GetDisplayedMapName(row) != "BatchRaid"
                    && GetDisplayedMapName(row) != "BatchReport"),
                    "선택 삭제 후 새로고침에서는 삭제된 행이 사라져야 합니다.");

                ServerSession partialRaidSession = CreateSensitiveSession(1, "partial-raid");
                partialRaidSession.MapName = "PartialRaid";
                RaidNoteRecord partialRaid = raidStore.CreateFor(partialRaidSession);
                partialRaid.NoteText = "partial success";
                raidStore.Save(partialRaidSession, partialRaid);
                ServerSession partialReportSession = CreateSensitiveSession(1, "partial-report");
                partialReportSession.MapName = "PartialReport";
                UserReportMemoRecord partialReport = reportStore.CreateFor(partialReportSession);
                partialReport.MemoText = "partial failure";
                reportStore.Save(partialReportSession, partialReport);
                refresh.PerformClick();
                Application.DoEvents();
                InvokePrivate(archive, "SetAllRowsChecked", false);
                FindRowByMap(grid, "PartialRaid").Cells["selected"].Value = true;
                FindRowByMap(grid, "PartialReport").Cells["selected"].Value = true;
                object partialTargets = InvokePrivate(archive, "GetCheckedDeleteTargets");
                string heldReportFolder = reportFolder + "-held";
                Directory.Move(reportFolder, heldReportFolder);
                File.WriteAllText(reportFolder, "This file temporarily blocks the report store.");
                object partialResult;
                try
                {
                    partialResult = InvokePrivate(
                        archive,
                        "DeleteRevalidatedTargets",
                        partialTargets);
                }
                finally
                {
                    File.Delete(reportFolder);
                    Directory.Move(heldReportFolder, reportFolder);
                }
                Assert(GetProperty<int>(partialResult, "SucceededCount") == 1
                    && GetProperty<int>(partialResult, "FailedCount") == 1
                    && GetProperty<int>(partialResult, "MissingCount") == 0,
                    "한 저장소 확인 실패가 다른 저장소의 유효한 선택 삭제를 막으면 안 됩니다.");
                Assert(!raidStore.Exists(partialRaid.Key) && reportStore.Exists(partialReport.Key),
                    "부분 실패 시 성공 항목만 삭제하고 실패 저장소의 메모는 보존해야 합니다.");
                archive.Close();
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
                Assert(FindLabelContaining(
                    form,
                    "일반 레이드 메모와 함께 메모보관함에 저장됩니다.") != null,
                    "유저신고 메모 안내는 일반 레이드 메모와 함께 보관됨을 설명해야 합니다.");
                Assert(FindLabelContaining(form, "별도로 로컬에 저장됩니다.") == null,
                    "유저신고 메모를 별도 보관함에 저장한다고 오해할 수 있는 안내가 남으면 안 됩니다.");
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

        private static object InvokePrivate(object instance, string name, params object[] arguments)
        {
            MethodInfo method = instance.GetType().GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(method != null, "테스트할 메서드를 찾을 수 없습니다: " + name);
            return method.Invoke(instance, arguments);
        }

        private static T GetProperty<T>(object instance, string name)
        {
            Assert(instance != null, "속성을 읽을 테스트 결과가 없습니다: " + name);
            PropertyInfo property = instance.GetType().GetProperty(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert(property != null, "테스트할 속성을 찾을 수 없습니다: " + name);
            return (T)property.GetValue(instance, null);
        }

        private static DataGridViewRow FindRowByMap(DataGridView grid, string mapName)
        {
            DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(candidate =>
                string.Equals(GetDisplayedMapName(candidate), mapName, StringComparison.Ordinal));
            Assert(row != null, "테스트할 메모 행을 찾을 수 없습니다: " + mapName);
            return row;
        }

        private static string GetDisplayedMapName(DataGridViewRow row)
        {
            string value = row == null
                ? string.Empty
                : Convert.ToString(row.Cells["map"].Value) ?? string.Empty;
            int separator = value.IndexOf(" · ", StringComparison.Ordinal);
            return separator < 0 ? value : value.Substring(0, separator);
        }

        private static bool IsChecked(DataGridViewRow row)
        {
            return row != null
                && row.Cells["selected"].Value is bool
                && (bool)row.Cells["selected"].Value;
        }

        private static void TestArchiveHeaderSorting(string testRoot)
        {
            string raidFolder = Path.Combine(testRoot, "sort-archive-raids");
            string reportFolder = Path.Combine(testRoot, "sort-archive-reports");
            var raidStore = new RaidNoteStore(raidFolder);
            var reportStore = new UserReportMemoStore(reportFolder);
            string[] mapNames = { "MemoAlpha", "MemoMike", "MemoZulu" };
            DateTime[] started =
            {
                new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc)
            };
            for (int index = 0; index < mapNames.Length; index++)
            {
                ServerSession session = CreateSensitiveSession(1, "sort-" + index);
                session.MapName = mapNames[index];
                session.SessionStarted = started[index];
                RaidNoteRecord record = raidStore.CreateFor(session);
                record.NoteText = "sort note " + mapNames[index];
                record.Tags.Add("tag-" + index);
                raidStore.Save(session, record);
            }

            using (var archive = new RaidNoteArchiveForm(raidStore, reportStore))
            {
                ShowOffScreen(archive);
                DataGridView grid = GetPrivateField<DataGridView>(archive, "_grid");
                Assert(grid.Columns["selected"].SortMode == DataGridViewColumnSortMode.NotSortable
                    && grid.Columns.Cast<DataGridViewColumn>()
                        .Where(column => column.Name != "selected")
                        .All(column => column.SortMode == DataGridViewColumnSortMode.Programmatic),
                    "메모 보관함은 선택 열을 제외한 모든 데이터 헤더를 정렬 가능하게 제공해야 합니다.");
                Assert(grid.Columns["map"].HeaderText == "맵 · 게임유형"
                    && Convert.ToString(FindRowByMap(grid, "MemoAlpha").Cells["map"].Value)
                        == "MemoAlpha · 미확인",
                    "메모보관함은 맵 열에 저장된 게임유형을 함께 표시해야 합니다.");
                Assert(FindRowByMap(grid, "MemoAlpha").Cells["map"].ToolTipText
                        == "MemoAlpha · 미확인",
                    "메모보관함의 맵·게임유형 툴팁은 저장된 전문과 같아야 합니다.");
                Assert(grid.Columns.Cast<DataGridViewColumn>()
                    .Where(column => column.SortMode == DataGridViewColumnSortMode.Programmatic)
                    .All(column => column.HeaderCell.ToolTipText
                        == "클릭할 때마다 오름차순, 내림차순, 기본 순서로 정렬합니다."),
                    "정렬 가능한 메모 헤더는 3단계 동작을 툴팁으로 알려야 합니다.");

                Assert(archive.GetType().GetMethod(
                        "GridCellMouseMove",
                        BindingFlags.Instance | BindingFlags.NonPublic) == null
                    && grid.Cursor == Cursors.Default,
                    "메모 보관함 헤더는 별도 손 모양 커서 처리를 사용하지 않아야 합니다.");

                string[] originalOrder = GetColumnTexts(grid, "map");
                Assert(originalOrder.SequenceEqual(
                    new[] { "MemoMike", "MemoZulu", "MemoAlpha" },
                    StringComparer.Ordinal),
                    "메모 보관함의 기본 순서는 레이드 시각 최신순이어야 합니다.");
                DataGridViewRow alphaRow = FindRowByMap(grid, "MemoAlpha");
                alphaRow.Cells["selected"].Value = true;
                grid.CurrentCell = alphaRow.Cells["map"];
                Application.DoEvents();

                var mapHeaderClick = new DataGridViewCellMouseEventArgs(
                    grid.Columns["map"].Index,
                    -1,
                    2,
                    2,
                    new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0));
                InvokePrivate(archive, "GridColumnHeaderMouseClick", grid, mapHeaderClick);
                string[] ascendingOrder = GetColumnTexts(grid, "map");
                Assert(ascendingOrder.SequenceEqual(
                    originalOrder.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)),
                    "메모 헤더의 첫 클릭은 오름차순으로 정렬해야 합니다.");
                Assert(IsChecked(FindRowByMap(grid, "MemoAlpha"))
                    && GetDisplayedMapName(grid.CurrentRow) == "MemoAlpha",
                    "메모 정렬은 체크 상태와 현재행을 같은 메모에 유지해야 합니다.");
                Assert(HeaderContainsColor(grid, System.Drawing.Color.FromArgb(232, 157, 54)),
                    "활성 메모 정렬 헤더는 메인 화면과 같은 주황색 화살표를 표시해야 합니다.");
                Assert(grid.Columns.Cast<DataGridViewColumn>()
                    .All(column => column.HeaderCell.SortGlyphDirection == SortOrder.None),
                    "주황색 사용자 지정 화살표와 기본 회색 정렬 화살표를 함께 표시하면 안 됩니다.");

                InvokePrivate(archive, "GridColumnHeaderMouseClick", grid, mapHeaderClick);
                string[] descendingOrder = GetColumnTexts(grid, "map");
                Assert(descendingOrder.SequenceEqual(
                    originalOrder.OrderByDescending(
                        value => value,
                        StringComparer.CurrentCultureIgnoreCase)),
                    "메모 헤더의 두 번째 클릭은 내림차순으로 정렬해야 합니다.");
                Assert(IsChecked(FindRowByMap(grid, "MemoAlpha")),
                    "내림차순 전환 뒤에도 체크 상태를 보존해야 합니다.");

                InvokePrivate(archive, "GridColumnHeaderMouseClick", grid, mapHeaderClick);
                Assert(GetColumnTexts(grid, "map").SequenceEqual(originalOrder),
                    "메모 헤더의 세 번째 클릭은 원래 최신순으로 복귀해야 합니다.");
                Assert(IsChecked(FindRowByMap(grid, "MemoAlpha")),
                    "기본 순서 복귀 뒤에도 체크 상태를 보존해야 합니다.");
            }
        }

        private static void TestRaidContextStorageAndArchiveDisplay(string testRoot)
        {
            string raidFolder = Path.Combine(testRoot, "raid-context-raids");
            string reportFolder = Path.Combine(testRoot, "raid-context-reports");
            var raidStore = new RaidNoteStore(raidFolder);
            var reportStore = new UserReportMemoStore(reportFolder);
            ServerSession session = CreateSensitiveSession(1, "raid-context");
            session.SessionStarted = new DateTime(2026, 8, 17, 12, 30, 0, DateTimeKind.Utc);
            session.MapName = "Factory";
            session.ProgressionMode = TarkovProgressionMode.PvpSeason;
            session.PvpSeasonNumber = 2;
            session.CharacterType = TarkovCharacterType.Scav;
            session.ParticipationType = TarkovParticipationType.Party;
            session.PartySize = 4;
            const string expectedType = "PvP시즌2 · 스캐브 · 4인 파티";
            const string expectedDisplay = "Factory · " + expectedType;

            RaidNoteRecord raid = raidStore.CreateFor(session);
            raid.NoteText = "raid context";
            raidStore.Save(session, raid);
            UserReportMemoRecord report = reportStore.CreateFor(session);
            report.Entries.Add(new UserReportMemoEntry
            {
                Nickname = "SyntheticMember",
                Reason = "synthetic reason"
            });
            reportStore.Save(session, report);
            Assert(raid.GameType == expectedType && report.GameType == expectedType,
                "새 메모는 EFT 게임유형·캐릭터·참가 형태 전문을 저장해야 합니다.");

            raid.GameType = "tampered";
            report.GameType = "tampered";
            raidStore.Save(session, raid);
            reportStore.Save(session, report);
            Assert(raid.GameType == expectedType && report.GameType == expectedType,
                "세션으로 다시 저장하면 메모의 레이드 문맥을 최신 판정으로 갱신해야 합니다.");

            ServerSession legacySession = CreateSensitiveSession(1, "type-only");
            legacySession.SessionStarted = session.SessionStarted.AddMinutes(-1);
            legacySession.MapName = "LegacyMap";
            RaidNoteRecord legacyTypeOnly = raidStore.CreateFor(legacySession);
            legacyTypeOnly.GameType = "PvP";
            legacyTypeOnly.NoteText = "type-only legacy memo";
            raidStore.Save(legacyTypeOnly.Key, legacyTypeOnly);

            using (var archive = new RaidNoteArchiveForm(raidStore, reportStore))
            {
                ShowOffScreen(archive);
                DataGridView grid = GetPrivateField<DataGridView>(archive, "_grid");
                DataGridViewRow[] rows = grid.Rows.Cast<DataGridViewRow>()
                    .Where(row => Convert.ToString(row.Cells["map"].Value) == expectedDisplay)
                    .ToArray();
                Assert(rows.Length == 2
                        && rows.All(row => row.Cells["map"].ToolTipText == expectedDisplay),
                    "일반·유저신고 메모 모두 레이드 문맥 전문과 같은 툴팁을 표시해야 합니다.");
                DataGridViewRow legacyRow = FindRowByMap(grid, "LegacyMap");
                Assert(Convert.ToString(legacyRow.Cells["map"].Value) == "LegacyMap · PvP"
                        && legacyRow.Cells["map"].ToolTipText == "LegacyMap · PvP",
                    "기존 GameType-only 메모는 값을 추측하지 않고 그대로 표시해야 합니다.");
            }

            var arenaSession = new ServerSession
            {
                Game = TarkovGame.Arena,
                SessionKey = "synthetic-arena-context",
                SessionStarted = session.SessionStarted,
                MapName = "Bay 5",
                GameMode = "TeamFight"
            };
            RaidNoteRecord boundedArena = raidStore.CreateFor(arenaSession);
            UserReportMemoRecord boundedArenaReport = reportStore.CreateFor(arenaSession);
            Assert(boundedArena.GameType == "TeamFight"
                    && boundedArenaReport.GameType == "TeamFight",
                "두 메모 종류의 Arena 게임 모드 표시는 변경하지 않아야 합니다.");
            arenaSession.GameMode = new string('M', 100);
            boundedArena = raidStore.CreateFor(arenaSession);
            boundedArenaReport = reportStore.CreateFor(arenaSession);
            Assert(boundedArena.GameType.Length == 64
                    && boundedArena.GameType == new string('M', 64)
                    && boundedArenaReport.GameType == boundedArena.GameType,
                "두 메모 종류의 Arena 게임 모드는 기존 64자 경계를 유지해야 합니다.");
        }

        private static void TestArchiveBackupUi(string testRoot)
        {
            var raidStore = new RaidNoteStore(Path.Combine(testRoot, "backup-ui-raids"));
            var reportStore = new UserReportMemoStore(Path.Combine(testRoot, "backup-ui-reports"));
            using (var archive = new RaidNoteArchiveForm(raidStore, reportStore))
            {
                ShowOffScreen(archive);
                Button exportButton = FindNamedControl<Button>(archive, "MemoBackupExportButton");
                Button importButton = FindNamedControl<Button>(archive, "MemoBackupImportButton");
                Assert(exportButton.Text == "내보내기"
                    && importButton.Text == "불러오기"
                    && exportButton.AccessibleDescription.Contains(
                        RaidNoteArchiveForm.BackupScreenshotNotice)
                    && importButton.AccessibleDescription.Contains(
                        "스크린샷 원본 파일 없이 검증된 로컬 이미지 연결 경로만 복원합니다.")
                    && RaidNoteArchiveForm.BackupScreenshotNotice
                        == "연결된 스크린샷 원본 파일은 백업에 포함되지 않으며, 로컬 이미지 연결 경로만 함께 저장됩니다. "
                            + "경로에는 사용자명 등 개인정보가 포함될 수 있으므로 공유 전에 확인해 주세요. "
                            + "다른 PC에서 같은 경로에 파일이 없으면 스크린샷을 열 수 없습니다.",
                    "메모보관함은 스크린샷 원본 제외, 로컬 이미지 경로 복원과 개인정보 가능성을 정확히 안내해야 합니다.");
                Assert(archive.AutoScaleMode == AutoScaleMode.Dpi,
                    "메모보관함 백업 버튼은 DPI 자동 배율 레이아웃 안에 있어야 합니다.");
                DataGridView archiveGrid = GetPrivateField<DataGridView>(archive, "_grid");
                DataGridViewColumn lastVisibleColumn = archiveGrid.Columns.Cast<DataGridViewColumn>()
                    .Where(column => column.Visible)
                    .OrderBy(column => column.DisplayIndex)
                    .Last();
                Rectangle lastHeaderBounds = archiveGrid.GetCellDisplayRectangle(
                    lastVisibleColumn.Index,
                    -1,
                    false);
                Assert(archive.ClientSize.Width >= 1220
                    && archiveGrid.HorizontalScrollingOffset == 0
                    && lastHeaderBounds.Right <= archiveGrid.ClientRectangle.Right,
                    "메모보관함은 처음 열 때 맵·게임유형 열까지 수평 스크롤 없이 보여야 합니다.");
                archive.Size = archive.MinimumSize;
                archive.PerformLayout();
                Application.DoEvents();
                Rectangle exportBounds = archive.RectangleToClient(
                    exportButton.RectangleToScreen(exportButton.ClientRectangle));
                Rectangle importBounds = archive.RectangleToClient(
                    importButton.RectangleToScreen(importButton.ClientRectangle));
                Assert(archive.ClientRectangle.Contains(exportBounds)
                    && archive.ClientRectangle.Contains(importBounds)
                    && !exportBounds.IntersectsWith(importBounds),
                    "최소 지원 창 크기에서도 메모 백업 버튼이 겹치거나 잘리면 안 됩니다.");
                archive.Close();
            }

            DateTime now = new DateTime(2026, 8, 17, 7, 30, 0, DateTimeKind.Utc);
            MemoArchiveRestoreItem newRaid = CreateMemoRestoreItem(
                MemoArchiveBackupKind.RaidNote,
                new string('a', 64),
                "NewRaid",
                "레이드 복원 미리보기",
                now.AddHours(-3),
                MemoArchiveRestoreStatus.New,
                true);
            MemoArchiveRestoreItem newReport = CreateMemoRestoreItem(
                MemoArchiveBackupKind.UserReportMemo,
                new string('b', 64),
                "NewReport",
                "유저신고 복원 미리보기",
                now.AddHours(-2),
                MemoArchiveRestoreStatus.New,
                true);
            MemoArchiveRestoreItem existingRaid = CreateMemoRestoreItem(
                MemoArchiveBackupKind.RaidNote,
                new string('c', 64),
                "ExistingRaid",
                "기존 메모 미리보기",
                now.AddHours(-1),
                MemoArchiveRestoreStatus.Existing,
                false);
            var items = new[] { newRaid, newReport, existingRaid };
            int applyAttempt = 0;
            int uiThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            int applyWorkerThreadId = 0;
            Func<IEnumerable<MemoArchiveRestoreItem>, MemoArchiveRestoreResult> apply =
                delegate(IEnumerable<MemoArchiveRestoreItem> supplied)
                {
                    applyWorkerThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    applyAttempt++;
                    var result = new MemoArchiveRestoreResult();
                    foreach (MemoArchiveRestoreItem item in supplied)
                    {
                        if (item.Status == MemoArchiveRestoreStatus.Existing)
                        {
                            result.SkippedCount++;
                            result.ItemResults.Add(new MemoArchiveRestoreItemResult
                            {
                                Item = item,
                                Skipped = true
                            });
                        }
                        else if (!item.Selected)
                        {
                            continue;
                        }
                        else if (applyAttempt == 1 && ReferenceEquals(item, newReport))
                        {
                            result.FailedCount++;
                            result.ItemResults.Add(new MemoArchiveRestoreItemResult
                            {
                                Item = item,
                                SafeErrorCode = "synthetic-retryable",
                                ErrorMessage = "유저신고 메모 1번 저장에 실패했습니다."
                            });
                        }
                        else
                        {
                            item.Selected = false;
                            result.AddedCount++;
                            result.ItemResults.Add(new MemoArchiveRestoreItemResult
                            {
                                Item = item,
                                Added = true
                            });
                        }
                    }
                    return result;
                };

            using (var preview = new MemoArchiveRestorePreviewForm(items, apply))
            {
                ShowOffScreen(preview);
                DataGridView grid = FindNamedControl<DataGridView>(
                    preview,
                    "MemoRestorePreviewGrid");
                Button selectAll = FindNamedControl<Button>(preview, "MemoRestoreSelectAllButton");
                Button selectNone = FindNamedControl<Button>(preview, "MemoRestoreSelectNoneButton");
                Button applyButton = FindNamedControl<Button>(preview, "MemoRestoreApplyButton");
                Label restoreResultLabel = GetPrivateField<Label>(preview, "_resultLabel");
                Assert(restoreResultLabel.Text == MemoArchiveRestorePreviewForm.RestorePolicyNotice
                    && restoreResultLabel.AccessibleDescription
                        == MemoArchiveRestorePreviewForm.RestorePolicyNotice,
                    "메모 복원 미리보기는 원본 파일 제외와 검증된 로컬 이미지 경로 복원을 정확히 안내해야 합니다.");
                Assert(FindLabelContaining(preview,
                        "전체 3개 · 레이드 메모 2개 · 유저신고 메모 1개") != null
                    && FindLabelContaining(preview,
                        "새로 추가될 메모 2개 · 기존 항목 건너뜀 1개") != null,
                    "복원 미리보기는 전체·종류·신규·기존 수를 적용 전에 표시해야 합니다.");
                Assert(!preview.ApplyAttempted,
                    "미리보기에서 적용하지 않고 닫는 경우 복원 결과로 오인하면 안 됩니다.");
                InvokePrivate(preview, "SetBusy", true);
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
                typeof(Form).GetMethod(
                    "OnFormClosing",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(preview, new object[] { closing });
                Assert(closing.Cancel,
                    "메모 복원 작업 중에는 X 또는 Alt+F4로 미리보기 창을 닫아 결과를 유실하면 안 됩니다.");
                InvokePrivate(preview, "SetBusy", false);
                Assert(grid.Rows.Count == 3
                    && grid.Rows.Cast<DataGridViewRow>()
                        .Count(row => !row.Cells["selected"].ReadOnly) == 2
                    && grid.Rows.Cast<DataGridViewRow>()
                        .Where(row => !row.Cells["selected"].ReadOnly)
                        .All(IsChecked)
                    && FindRowByMap(grid, "ExistingRaid").Cells["selected"].ReadOnly,
                    "새 메모만 기본 선택하고 기존 메모는 선택할 수 없어야 합니다.");
                Assert(grid.Columns["map"].HeaderText == "맵 · 게임유형"
                    && Convert.ToString(FindRowByMap(grid, "NewRaid").Cells["map"].Value)
                        == "NewRaid · PvP시즌2 · PMC · 2인 파티"
                    && Convert.ToString(FindRowByMap(grid, "NewReport").Cells["map"].Value)
                        == "NewReport · PvP · 스캐브 · 솔로",
                    "복원 미리보기에도 맵과 백업된 게임유형을 함께 표시해야 합니다.");
                Assert(FindRowByMap(grid, "NewRaid").Cells["map"].ToolTipText
                        == "NewRaid · PvP시즌2 · PMC · 2인 파티"
                    && FindRowByMap(grid, "NewReport").Cells["map"].ToolTipText
                        == "NewReport · PvP · 스캐브 · 솔로",
                    "복원 미리보기의 레이드 문맥 툴팁은 표시 전문과 같아야 합니다.");

                selectNone.PerformClick();
                Assert(grid.Rows.Cast<DataGridViewRow>().All(row => !IsChecked(row))
                    && !applyButton.Enabled,
                    "전체 해제는 새 메모 선택만 모두 해제해야 합니다.");
                DataGridViewRow firstSelectable = grid.Rows.Cast<DataGridViewRow>()
                    .First(row => !row.Cells["selected"].ReadOnly);
                firstSelectable.Cells["selected"].Value = true;
                Application.DoEvents();
                Assert((grid.Columns["selected"].HeaderCell.AccessibilityObject.State
                        & AccessibleStates.Mixed) == AccessibleStates.Mixed
                    && grid.Columns["selected"].HeaderCell.AccessibilityObject.DefaultAction
                        == "전체 선택",
                    "일부 메모만 선택되면 복원 선택 헤더가 혼합 상태를 알려야 합니다.");
                var ctrlA = new KeyEventArgs(Keys.Control | Keys.A);
                InvokePrivate(preview, "GridKeyDown", grid, ctrlA);
                Assert(ctrlA.Handled && ctrlA.SuppressKeyPress
                    && grid.Rows.Cast<DataGridViewRow>()
                        .Where(row => !row.Cells["selected"].ReadOnly)
                        .All(IsChecked)
                    && applyButton.Enabled,
                    "Ctrl+A는 기존 항목을 건드리지 않고 복원 가능한 새 메모만 선택해야 합니다.");
                selectNone.PerformClick();
                var headerClick = new DataGridViewCellMouseEventArgs(
                    grid.Columns["selected"].Index,
                    -1,
                    2,
                    2,
                    new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0));
                InvokePrivate(preview, "GridColumnHeaderMouseClick", grid, headerClick);
                Assert(grid.Rows.Cast<DataGridViewRow>()
                    .Where(row => !row.Cells["selected"].ReadOnly)
                    .All(IsChecked),
                    "복원 선택 헤더 체크박스는 새 메모를 전체 선택해야 합니다.");
                Assert(grid.Columns["selected"].HeaderText == string.Empty
                    && grid.Columns["selected"].HeaderCell.AccessibilityObject.Name
                        == "복원 메모 전체 선택 또는 전체 해제",
                    "복원 선택 헤더 체크박스는 문자 헤더 대신 접근성 이름을 제공해야 합니다. "
                        + "Header='" + grid.Columns["selected"].HeaderText
                        + "', Name='" + grid.Columns["selected"].HeaderCell.AccessibilityObject.Name
                        + "', Type='" + grid.Columns["selected"].HeaderCell.GetType().FullName + "'");
                Assert(grid.Columns["selected"].HeaderCell.AccessibilityObject.DefaultAction
                        == "전체 해제"
                    && (grid.Columns["selected"].HeaderCell.AccessibilityObject.State
                        & AccessibleStates.Checked) == AccessibleStates.Checked,
                    "전체 선택된 복원 헤더는 전체 해제 동작과 체크 상태를 제공해야 합니다.");

                Label updatedCount = GetPrivateField<Label>(preview, "_countLabel");
                applyButton.PerformClick();
                WaitForUi(
                    () => applyAttempt >= 1
                        && preview.ApplyCycleCompleted
                        && preview.FailedCount == 1
                        && applyButton.Enabled
                        && updatedCount.Text.IndexOf(
                            "새로 추가될 메모 1개 · 기존 항목 건너뜀 1개",
                            StringComparison.Ordinal) >= 0,
                    "첫 메모 복원 결과를 기다리는 동안 시간이 초과되었습니다.");
                Assert(applyWorkerThreadId != uiThreadId
                    && preview.LastCompletionThreadId == uiThreadId,
                    "메모 복원 저장은 작업 스레드에서 수행하고 결과 UI는 폼 생성 스레드에서 반영해야 합니다.");
                DataGridViewRow addedRow = FindRowByMap(grid, "NewRaid");
                DataGridViewRow failedRow = FindRowByMap(grid, "NewReport");
                Assert(preview.AddedCount == 1
                    && preview.SkippedCount == 1
                    && preview.FailedCount == 1
                    && preview.ResultSummary
                        == "추가 1개 · 기존 항목 건너뜀 1개 · 실패 1개",
                    "부분 복원 결과는 추가·기존 건너뜀·실패 수를 정확히 집계해야 합니다. "
                        + preview.ResultSummary);
                Assert(addedRow.Cells["selected"].ReadOnly
                    && !IsChecked(addedRow),
                    "추가가 끝난 메모 행은 선택 해제하고 다시 적용할 수 없게 해야 합니다.");
                Assert(!failedRow.Cells["selected"].ReadOnly
                    && IsChecked(failedRow)
                    && Convert.ToString(failedRow.Cells["status"].Value) == "실패 · 재시도",
                    "retryable 실패 행은 선택 상태와 재시도 가능 표시를 유지해야 합니다.");
                Assert(updatedCount.Text.IndexOf(
                        "새로 추가될 메모 1개 · 기존 항목 건너뜀 1개",
                        StringComparison.Ordinal) >= 0,
                    "부분 성공 뒤 복원 미리보기의 남은 신규 메모 수를 갱신해야 합니다: "
                        + updatedCount.Text.Replace("\r", "\\r").Replace("\n", "\\n"));

                applyButton.PerformClick();
                WaitForUi(
                    () => applyAttempt >= 2
                        && preview.ApplyCycleCompleted
                        && preview.DialogResult == DialogResult.OK
                        && !preview.Visible,
                    "메모 복원 재시도 결과를 기다리는 동안 시간이 초과되었습니다.");
                Assert(applyAttempt == 2
                    && preview.AddedCount == 2
                    && preview.SkippedCount == 1
                    && preview.FailedCount == 0
                    && preview.HasChanges
                    && preview.ResultSummary
                        == "추가 2개 · 기존 항목 건너뜀 1개 · 실패 0개"
                    && preview.ApplyAttempted,
                    "재시도 성공 뒤에는 누적 추가·기존 건너뜀·현재 실패 수를 정확히 표시해야 합니다.");
            }

            MemoArchiveRestoreItem conflict = CreateMemoRestoreItem(
                MemoArchiveBackupKind.RaidNote,
                new string('d', 64),
                "ConflictRaid",
                "충돌 메모",
                now,
                MemoArchiveRestoreStatus.ExistingConflict,
                false);
            MemoArchiveRestoreItem unavailable = CreateMemoRestoreItem(
                MemoArchiveBackupKind.UserReportMemo,
                new string('e', 64),
                "UnavailableReport",
                "확인 불가 메모",
                now,
                MemoArchiveRestoreStatus.Unavailable,
                false);
            using (var boundaryPreview = new MemoArchiveRestorePreviewForm(
                new[] { conflict, unavailable },
                supplied => { throw new InvalidOperationException("선택 불가 항목은 적용하면 안 됩니다."); }))
            {
                ShowOffScreen(boundaryPreview);
                DataGridView boundaryGrid = FindNamedControl<DataGridView>(
                    boundaryPreview,
                    "MemoRestorePreviewGrid");
                Button boundarySelectAll = FindNamedControl<Button>(
                    boundaryPreview,
                    "MemoRestoreSelectAllButton");
                Button boundaryApply = FindNamedControl<Button>(
                    boundaryPreview,
                    "MemoRestoreApplyButton");
                boundarySelectAll.PerformClick();
                Assert(boundaryGrid.Rows.Cast<DataGridViewRow>().All(row =>
                        row.Cells["selected"].ReadOnly && !IsChecked(row))
                    && !boundaryApply.Enabled
                    && Convert.ToString(
                        FindRowByMap(boundaryGrid, "ConflictRaid").Cells["status"].Value)
                        == "기존 내용 충돌"
                    && Convert.ToString(
                        FindRowByMap(boundaryGrid, "UnavailableReport").Cells["status"].Value)
                        == "확인 불가",
                    "기존 내용 충돌과 저장소 확인 불가 행은 선택·재시도할 수 없어야 합니다.");
                boundaryPreview.Close();
            }

            MemoArchiveRestoreItem exactExistingRace = CreateMemoRestoreItem(
                MemoArchiveBackupKind.RaidNote,
                new string('f', 64),
                "ExactExistingRace",
                "동일 내용 경쟁",
                now,
                MemoArchiveRestoreStatus.New,
                true);
            using (var exactPreview = new MemoArchiveRestorePreviewForm(
                new[] { exactExistingRace },
                supplied =>
                {
                    exactExistingRace.Status = MemoArchiveRestoreStatus.Existing;
                    exactExistingRace.Selected = false;
                    var result = new MemoArchiveRestoreResult { SkippedCount = 1 };
                    result.ItemResults.Add(new MemoArchiveRestoreItemResult
                    {
                        Item = exactExistingRace,
                        Skipped = true
                    });
                    return result;
                }))
            {
                ShowOffScreen(exactPreview);
                FindNamedControl<Button>(exactPreview, "MemoRestoreApplyButton").PerformClick();
                WaitForUi(
                    () => exactPreview.ApplyCycleCompleted && !exactPreview.Visible,
                    "동일 내용 기존 항목 건너뜀 결과를 기다리는 동안 시간이 초과되었습니다.");
                Assert(exactPreview.ApplyAttempted
                    && !exactPreview.HasChanges
                    && exactPreview.SkippedCount == 1,
                    "적용 직전 동일 내용이 생긴 경우 추가 0개인 기존 항목 건너뜀으로 완료해야 합니다.");

                using (var outcomeArchive = new RaidNoteArchiveForm(
                    new RaidNoteStore(Path.Combine(testRoot, "outcome-raids")),
                    new UserReportMemoStore(Path.Combine(testRoot, "outcome-reports"))))
                {
                    ShowOffScreen(outcomeArchive);
                    Assert(!outcomeArchive.Changed,
                        "복원 결과를 반영하기 전 보관함은 변경 상태가 아니어야 합니다.");
                    InvokePrivate(
                        outcomeArchive,
                        "ApplyRestorePreviewOutcome",
                        exactPreview);
                    Assert(outcomeArchive.Changed
                        && GetPrivateField<Label>(outcomeArchive, "_statusLabel").Text
                            == "추가 0개 · 기존 항목 건너뜀 1개 · 실패 0개",
                        "추가 0개인 exact-existing 회복도 보관함 변경으로 알려 메인 메모 셀을 재조회해야 합니다.");
                    outcomeArchive.Close();
                }
            }
        }

        private static MemoArchiveRestoreItem CreateMemoRestoreItem(
            MemoArchiveBackupKind kind,
            string key,
            string mapName,
            string preview,
            DateTime raidStartedUtc,
            MemoArchiveRestoreStatus status,
            bool selected)
        {
            var source = new MemoArchiveBackupParsedItem
            {
                Kind = kind,
                SourceIndex = 1,
                Key = key,
                PreviewText = preview
            };
            if (kind == MemoArchiveBackupKind.RaidNote)
            {
                source.RaidNote = new RaidNoteRecord
                {
                    Key = key,
                    Game = "EFT",
                    GameType = "PvP시즌2 · PMC · 2인 파티",
                    MapName = mapName,
                    RaidStartedUtc = raidStartedUtc,
                    NoteText = preview,
                    CreatedUtc = raidStartedUtc,
                    UpdatedUtc = raidStartedUtc
                };
            }
            else
            {
                source.UserReportMemo = new UserReportMemoRecord
                {
                    Key = key,
                    Game = "EFT",
                    GameType = "PvP · 스캐브 · 솔로",
                    MapName = mapName,
                    RaidStartedUtc = raidStartedUtc,
                    ReportCount = 1,
                    Entries = new List<UserReportMemoEntry>(),
                    MemoText = preview,
                    CreatedUtc = raidStartedUtc,
                    UpdatedUtc = raidStartedUtc
                };
            }
            return new MemoArchiveRestoreItem
            {
                Source = source,
                Status = status,
                Selected = selected,
                Detail = status == MemoArchiveRestoreStatus.New
                    ? "없는 메모이므로 새로 추가할 수 있습니다."
                    : "같은 종류와 키의 기존 메모가 있어 건너뜁니다."
            };
        }

        private static void WaitForUi(Func<bool> condition, string timeoutMessage)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
            Application.DoEvents();
            Assert(condition(), timeoutMessage);
        }

        private static string[] GetColumnTexts(DataGridView grid, string columnName)
        {
            return grid.Rows.Cast<DataGridViewRow>()
                .Select(row => columnName == "map"
                    ? GetDisplayedMapName(row)
                    : Convert.ToString(row.Cells[columnName].Value))
                .ToArray();
        }

        private static string CaptureSelectionHeaderVisual(DataGridView grid)
        {
            int columnIndex = grid.Columns["selected"].Index;
            Rectangle header = grid.GetCellDisplayRectangle(columnIndex, -1, true);
            Assert(header.Width > 0 && header.Height > 0,
                "선택 헤더 체크박스의 표시 영역을 계산할 수 있어야 합니다.");
            using (var bitmap = new System.Drawing.Bitmap(
                Math.Max(1, grid.ClientSize.Width),
                Math.Max(1, grid.ClientSize.Height)))
            {
                grid.DrawToBitmap(bitmap, grid.ClientRectangle);
                int sampleWidth = Math.Min(26, header.Width);
                int sampleHeight = Math.Min(26, header.Height);
                Rectangle sample = new Rectangle(
                    Math.Max(0, header.Left + (header.Width - sampleWidth) / 2),
                    Math.Max(0, header.Top + (header.Height - sampleHeight) / 2),
                    sampleWidth,
                    sampleHeight);
                sample.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                unchecked
                {
                    int hash = 17;
                    for (int y = sample.Top; y < sample.Bottom; y++)
                    {
                        for (int x = sample.Left; x < sample.Right; x++)
                            hash = hash * 31 + bitmap.GetPixel(x, y).ToArgb();
                    }
                    return hash.ToString("X8");
                }
            }
        }

        private static bool HeaderContainsColor(DataGridView grid, System.Drawing.Color color)
        {
            using (var bitmap = new System.Drawing.Bitmap(
                Math.Max(1, grid.ClientSize.Width),
                Math.Max(1, grid.ClientSize.Height)))
            {
                grid.DrawToBitmap(bitmap, grid.ClientRectangle);
                int maximumY = Math.Min(bitmap.Height, grid.ColumnHeadersHeight + 2);
                for (int y = 0; y < maximumY; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()) return true;
                    }
                }
            }
            return false;
        }

        private static void DrawGridForPaintVerification(DataGridView grid)
        {
            using (var bitmap = new System.Drawing.Bitmap(
                Math.Max(1, grid.ClientSize.Width),
                Math.Max(1, grid.ClientSize.Height)))
            {
                grid.DrawToBitmap(bitmap, grid.ClientRectangle);
            }
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
