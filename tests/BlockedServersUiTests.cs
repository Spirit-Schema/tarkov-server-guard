// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class BlockedServersUiTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length != 1 || !File.Exists(args[0]))
                    throw new InvalidOperationException("테스트할 TarkovServerGuard.exe 경로가 필요합니다.");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Assembly application = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                AssertSelectionConveniences(application);
                AssertHeaderSorting(application);
                AssertMetricSnapshotAndPresentation(application);
                AssertUsageNotice(application);
                Console.WriteLine("BlockedServersUiTests: PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BlockedServersUiTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void AssertHeaderSorting(Assembly application)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.NonPublic;
            const BindingFlags staticFlags = BindingFlags.Static
                | BindingFlags.NonPublic;
            Type formType = application.GetType("TarkovServerReporter.BlockedServersForm", true);
            Type serverType = application.GetType("TarkovServerReporter.ManagedBlockedServer", true);
            using (var form = (Form)Activator.CreateInstance(formType))
            {
                var grid = (DataGridView)formType.GetField("_grid", instanceFlags).GetValue(form);
                MethodInfo headerClick = formType.GetMethod(
                    "GridColumnHeaderMouseClick",
                    instanceFlags);
                MethodInfo mouseMove = formType.GetMethod("GridCellMouseMove", instanceFlags);
                FieldInfo sortColumn = formType.GetField(
                    "_blockedServerSortColumn",
                    instanceFlags);
                FieldInfo sortOrder = formType.GetField(
                    "_blockedServerSortOrder",
                    instanceFlags);
                FieldInfo arrowColor = formType.GetField("SortArrowOrange", staticFlags);
                Assert(grid != null
                    && headerClick != null
                    && mouseMove != null
                    && sortColumn != null
                    && sortOrder != null
                    && arrowColor != null,
                    "서버차단현황 헤더 정렬 경계를 찾지 못했습니다.");

                foreach (string columnName in new[]
                {
                    "ip", "status", "location", "blockedAt", "note", "kind"
                })
                {
                    DataGridViewColumn column = grid.Columns[columnName];
                    Assert(column.SortMode == DataGridViewColumnSortMode.Programmatic
                        && column.HeaderCell.ToolTipText
                            == "클릭할 때마다 오름차순, 내림차순, 기본 순서로 정렬합니다.",
                        columnName + " 열은 3단계 정렬 동작과 안내를 제공해야 합니다.");
                }
                foreach (string columnName in new[] { "actualRtt", "packetLoss" })
                {
                    DataGridViewColumn column = grid.Columns[columnName];
                    Assert(column.SortMode == DataGridViewColumnSortMode.Programmatic
                        && column.HeaderCell.ToolTipText.Contains("현재 측정값이 아닙니다.")
                        && column.HeaderCell.ToolTipText.Contains("서로 다른 레이드 시점")
                        && column.HeaderCell.ToolTipText.Contains(
                            "클릭할 때마다 오름차순, 내림차순, 기본 순서로 정렬합니다."),
                        columnName + " 측정 열은 정확한 범위 안내와 3단계 정렬을 제공해야 합니다.");
                }
                Assert(grid.Columns["currentPing"].SortMode
                        == DataGridViewColumnSortMode.NotSortable
                    && !grid.Columns["currentPing"].HeaderCell.ToolTipText.Contains(
                        "정렬합니다.")
                    && grid.Columns["selected"].SortMode
                        == DataGridViewColumnSortMode.NotSortable
                    && grid.Columns["remove"].SortMode
                        == DataGridViewColumnSortMode.NotSortable,
                    "측정할 수 없는 현재 핑과 동작 헤더는 정렬 열로 오인되면 안 됩니다.");
                Assert(((Color)arrowColor.GetValue(null)).ToArgb()
                        == Color.FromArgb(232, 157, 54).ToArgb(),
                    "정렬 화살표는 메인 화면과 같은 주황색이어야 합니다.");

                DataGridViewRow first = AddSortableServerRow(
                    grid,
                    serverType,
                    "8.8.8.8",
                    "차단 중",
                    "서울",
                    "2026-08-17 12:00:00",
                    false,
                    "호환 규칙",
                    true);
                DataGridViewRow second = AddSortableServerRow(
                    grid,
                    serverType,
                    "1.1.1.1",
                    "확인 필요",
                    "-",
                    "확인 안 됨",
                    true,
                    "현재 규칙",
                    false);
                DataGridViewRow third = AddSortableServerRow(
                    grid,
                    serverType,
                    "4.4.4.4",
                    "차단 중",
                    "도쿄",
                    "2026-08-16 10:30:00",
                    false,
                    "현재 규칙",
                    false);
                var originalTags = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "8.8.8.8", first.Tag },
                    { "1.1.1.1", second.Tag },
                    { "4.4.4.4", third.Tag }
                };

                InvokeHeaderClick(form, grid, headerClick, "ip");
                Assert(GetIpOrder(grid) == "1.1.1.1|4.4.4.4|8.8.8.8"
                    && (string)sortColumn.GetValue(form) == "ip"
                    && (SortOrder)sortOrder.GetValue(form) == SortOrder.Ascending
                    && grid.Columns["ip"].HeaderCell.SortGlyphDirection
                        == SortOrder.Ascending,
                    "첫 클릭은 서버 IP를 오름차순으로 정렬하고 상태를 표시해야 합니다.");

                InvokeHeaderClick(form, grid, headerClick, "ip");
                Assert(GetIpOrder(grid) == "8.8.8.8|4.4.4.4|1.1.1.1"
                    && (SortOrder)sortOrder.GetValue(form) == SortOrder.Descending
                    && grid.Columns["ip"].HeaderCell.SortGlyphDirection
                        == SortOrder.Descending,
                    "두 번째 클릭은 서버 IP를 내림차순으로 정렬해야 합니다.");

                InvokeHeaderClick(form, grid, headerClick, "ip");
                Assert(GetIpOrder(grid) == "8.8.8.8|1.1.1.1|4.4.4.4"
                    && sortColumn.GetValue(form) == null
                    && (SortOrder)sortOrder.GetValue(form) == SortOrder.None
                    && grid.Columns["ip"].HeaderCell.SortGlyphDirection == SortOrder.None,
                    "세 번째 클릭은 최초 조회 순서를 정확히 복원해야 합니다.");
                AssertRowsRetainActionsAndState(grid, originalTags);

                InvokeHeaderClick(form, grid, headerClick, "blockedAt");
                Assert(GetIpOrder(grid) == "4.4.4.4|8.8.8.8|1.1.1.1",
                    "차단시각 오름차순은 알 수 없는 값을 마지막에 유지해야 합니다.");
                InvokeHeaderClick(form, grid, headerClick, "blockedAt");
                Assert(GetIpOrder(grid) == "8.8.8.8|4.4.4.4|1.1.1.1",
                    "차단시각 내림차순도 알 수 없는 값을 마지막에 유지해야 합니다.");

                InvokeHeaderClick(form, grid, headerClick, "note");
                Assert(GetIpOrder(grid) == "8.8.8.8|4.4.4.4|1.1.1.1",
                    "메모 열은 메모 없음에서 있음 순으로 정렬해야 합니다.");
                AssertRowsRetainActionsAndState(grid, originalTags);

                InvokeHeaderMouseMove(form, grid, mouseMove, "selected");
                Assert(grid.Cursor == Cursors.Default,
                    "서버차단현황 선택 체크박스 헤더에는 손 모양 커서를 표시하지 않아야 합니다.");
                InvokeHeaderMouseMove(form, grid, mouseMove, "kind");
                Assert(grid.Cursor == Cursors.Default,
                    "서버차단현황 정렬 헤더에는 손 모양 커서를 표시하면 안 됩니다.");
                InvokeHeaderMouseMove(form, grid, mouseMove, "remove");
                Assert(grid.Cursor == Cursors.Default,
                    "동작이 없는 개별 해제 헤더는 손 모양 커서를 표시하면 안 됩니다.");
                InvokeCellMouseMove(form, grid, mouseMove, "note", 0);
                Assert(grid.Cursor == Cursors.Hand,
                    "클릭 가능한 메모 셀은 기존 손 모양 커서를 유지해야 합니다.");
                InvokeCellMouseMove(form, grid, mouseMove, "remove", 0);
                Assert(grid.Cursor == Cursors.Hand,
                    "클릭 가능한 개별 해제 셀은 기존 손 모양 커서를 유지해야 합니다.");
            }
        }

        private static void AssertSelectionConveniences(Assembly application)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.NonPublic;
            Type formType = application.GetType("TarkovServerReporter.BlockedServersForm", true);
            Type serverType = application.GetType("TarkovServerReporter.ManagedBlockedServer", true);
            using (var form = (Form)Activator.CreateInstance(formType))
            {
                var grid = (DataGridView)formType.GetField("_grid", instanceFlags).GetValue(form);
                var selectedCountLabel = (Label)formType.GetField(
                    "_selectedCountLabel",
                    instanceFlags).GetValue(form);
                var removeSelectedButton = (Button)formType.GetField(
                    "_removeSelectedButton",
                    instanceFlags).GetValue(form);
                var removeAllButton = (Button)formType.GetField(
                    "_removeAllButton",
                    instanceFlags).GetValue(form);
                MethodInfo headerClick = formType.GetMethod(
                    "GridColumnHeaderMouseClick",
                    instanceFlags);
                MethodInfo keyDown = formType.GetMethod("GridKeyDown", instanceFlags);
                MethodInfo updateButtons = formType.GetMethod("UpdateButtons", instanceFlags);
                MethodInfo selectedAddresses = formType.GetMethod(
                    "GetSelectedAddresses",
                    instanceFlags);
                Assert(grid != null
                    && selectedCountLabel != null
                    && removeSelectedButton != null
                    && removeAllButton != null
                    && headerClick != null
                    && keyDown != null
                    && updateButtons != null
                    && selectedAddresses != null,
                    "서버차단현황 선택 UI 경계를 찾지 못했습니다.");
                form.CreateControl();
                form.PerformLayout();
                if (grid.ClientSize.Width <= 0 || grid.ClientSize.Height <= 0)
                {
                    grid.Dock = DockStyle.None;
                    grid.Bounds = new Rectangle(0, 0, 1000, 360);
                }
                grid.CreateControl();
                grid.PerformLayout();

                DataGridViewColumn selectionColumn = grid.Columns["selected"];
                Assert(grid.AccessibleName == "차단 서버 목록"
                    && selectionColumn.HeaderText == string.Empty
                    && selectionColumn.HeaderCell.ToolTipText
                        == "차단 해제 대상을 전체 선택하거나 전체 해제합니다."
                    && selectionColumn.HeaderCell.AccessibilityObject.Name
                        == "차단 서버 선택 열 헤더",
                    "선택 열과 헤더는 체크박스 용도를 접근성 정보로 제공해야 합니다.");
                Assert(removeSelectedButton.AccessibleName == "선택 해제"
                    && removeSelectedButton.AccessibleDescription
                        == "선택한 서버의 Windows 방화벽 차단만 해제합니다."
                    && removeAllButton.AccessibleName == "전체 해제"
                    && removeAllButton.AccessibleDescription
                        == "목록에 있는 모든 서버의 Windows 방화벽 차단을 해제합니다.",
                    "선택 해제와 전체 해제 버튼의 접근성 이름과 동작 설명이 구분되어야 합니다.");
                AssertSelectionAccessibility(grid, selectedCountLabel, 0, 0);

                AddServerRow(grid, serverType, "8.8.8.8");
                AddServerRow(grid, serverType, "1.1.1.1");
                grid.CurrentCell = grid.Rows[0].Cells["selected"];
                updateButtons.Invoke(form, null);
                AssertSelectionAccessibility(grid, selectedCountLabel, 0, 2);
                string uncheckedHeaderPixels = CaptureSelectionHeaderVisual(grid);

                var mouse = new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0);
                var headerArgs = new DataGridViewCellMouseEventArgs(
                    grid.Columns["selected"].Index,
                    -1,
                    1,
                    1,
                    mouse);
                headerClick.Invoke(form, new object[] { grid, headerArgs });
                Assert(AllRowsHaveSelection(grid, true)
                    && selectedCountLabel.Text == "선택 2개"
                    && removeSelectedButton.Enabled,
                    "선택 헤더 첫 클릭은 모든 행과 선택 수를 즉시 갱신해야 합니다.");
                AssertSelectionAccessibility(grid, selectedCountLabel, 2, 2);
                string checkedHeaderPixels = CaptureSelectionHeaderVisual(grid);
                Assert(checkedHeaderPixels != uncheckedHeaderPixels,
                    "전체 선택 체크박스는 미선택 상태와 다른 체크 표시를 그려야 합니다.");
                string headerSelectAccessibility = CreateSelectionAccessibilitySnapshot(
                    grid,
                    selectedCountLabel);
                Assert(((IList)selectedAddresses.Invoke(form, null)).Count == 2,
                    "전체 선택 뒤 기존 선택 해제 대상 계산이 모든 서버를 반환해야 합니다.");

                headerClick.Invoke(form, new object[] { grid, headerArgs });
                Assert(AllRowsHaveSelection(grid, false)
                    && selectedCountLabel.Text == "선택 0개"
                    && !removeSelectedButton.Enabled,
                    "선택 헤더 두 번째 클릭은 모든 행을 해제해야 합니다.");
                AssertSelectionAccessibility(grid, selectedCountLabel, 0, 2);

                var keyArgs = new KeyEventArgs(Keys.Control | Keys.A);
                keyDown.Invoke(form, new object[] { grid, keyArgs });
                Assert(keyArgs.Handled
                    && keyArgs.SuppressKeyPress
                    && AllRowsHaveSelection(grid, true)
                    && selectedCountLabel.Text == "선택 2개",
                    "Ctrl+A는 기본 그리드 동작 대신 모든 체크박스를 선택해야 합니다.");
                AssertSelectionAccessibility(grid, selectedCountLabel, 2, 2);
                Assert(CreateSelectionAccessibilitySnapshot(grid, selectedCountLabel)
                    == headerSelectAccessibility,
                    "Ctrl+A와 선택 헤더 클릭은 같은 선택 결과와 접근성 상태를 만들어야 합니다.");

                grid.Rows[0].Cells["selected"].Value = false;
                Assert(selectedCountLabel.Text == "선택 1개"
                    && ((IList)selectedAddresses.Invoke(form, null)).Count == 1,
                    "개별 선택 해제 뒤 선택 수와 기존 대상 계산을 유지해야 합니다.");
                AssertSelectionAccessibility(grid, selectedCountLabel, 1, 2);
                string partialHeaderPixels = CaptureSelectionHeaderVisual(grid);
                Assert(partialHeaderPixels != uncheckedHeaderPixels
                    && partialHeaderPixels != checkedHeaderPixels,
                    "부분 선택 체크박스는 미선택·전체 선택과 구분되는 표시를 그려야 합니다.");

                grid.Rows.Clear();
                updateButtons.Invoke(form, null);
                AssertSelectionAccessibility(grid, selectedCountLabel, 0, 0);
                Assert(!removeSelectedButton.Enabled && !removeAllButton.Enabled,
                    "목록 새로고침으로 행이 교체되면 선택 수와 해제 버튼 상태도 초기화되어야 합니다.");
            }
        }

        private static void AssertMetricSnapshotAndPresentation(Assembly application)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type formType = application.GetType("TarkovServerReporter.BlockedServersForm", true);
            Type sessionType = application.GetType("TarkovServerReporter.ServerSession", true);
            Type hostingModeType = application.GetType(
                "TarkovServerReporter.TarkovHostingMode",
                true);
            Type sessionListType = typeof(List<>).MakeGenericType(sessionType);
            var sessions = (IList)Activator.CreateInstance(sessionListType);
            DateTime older = new DateTime(2026, 8, 17, 20, 10, 0);
            DateTime newer = new DateTime(2026, 8, 17, 20, 20, 0);
            DateTime localNewest = new DateTime(2026, 8, 17, 20, 30, 0);
            sessions.Add(CreateSession(
                sessionType, hostingModeType, "203.0.113.10", older, 142.4, null, false));
            sessions.Add(CreateSession(
                sessionType, hostingModeType, "203.0.113.10", newer, null, 0.031, false));
            sessions.Add(CreateSession(
                sessionType, hostingModeType, "203.0.113.10", localNewest, 5, 0, true));
            sessions.Add(CreateSession(
                sessionType, hostingModeType, "198.51.100.20", newer, 81.2, 0, false));
            sessions.Add(CreateSession(
                sessionType, hostingModeType, "192.0.2.30", newer, null, null, false));
            DateTime insufficientAt = new DateTime(2026, 8, 17, 20, 40, 2);
            sessions.Add(CreateSession(
                sessionType,
                hostingModeType,
                "192.0.2.31",
                insufficientAt.AddSeconds(-2),
                null,
                0,
                false,
                true,
                0,
                insufficientAt));

            Type enumerableType = typeof(IEnumerable<>).MakeGenericType(sessionType);
            ConstructorInfo constructor = formType.GetConstructor(new[] { enumerableType });
            Assert(constructor != null,
                "서버차단현황은 읽기 전용 세션 스냅샷을 받는 생성자를 제공해야 합니다.");
            using (var form = (Form)constructor.Invoke(new object[] { sessions }))
            {
                var grid = (DataGridView)formType.GetField("_grid", instanceFlags).GetValue(form);
                var refreshButton = (Button)formType.GetField(
                    "_refreshButton",
                    instanceFlags).GetValue(form);
                var toolTip = (ToolTip)formType.GetField(
                    "_toolTip",
                    instanceFlags).GetValue(form);
                var snapshots = (IDictionary)formType.GetField(
                    "_metricSnapshots",
                    instanceFlags).GetValue(form);
                MethodInfo applyMetrics = formType.GetMethod("ApplyMetricCells", instanceFlags);
                MethodInfo headerClick = formType.GetMethod(
                    "GridColumnHeaderMouseClick",
                    instanceFlags);
                Assert(grid != null && snapshots != null && applyMetrics != null,
                    "차단 서버 로그 지표 표시 경계를 찾지 못했습니다.");

                object firstSnapshot = snapshots["203.0.113.10"];
                Assert(firstSnapshot != null
                    && Math.Abs(Convert.ToDouble(firstSnapshot.GetType()
                        .GetProperty("ActualRttMs").GetValue(firstSnapshot, null)) - 142.4) < 0.001
                    && Math.Abs(Convert.ToDouble(firstSnapshot.GetType()
                        .GetProperty("PacketLoss").GetValue(firstSnapshot, null)) - 0.031) < 0.0001,
                    "RTT와 패킷손실은 각 지표의 가장 최근 유효 원격 레이드 값을 독립적으로 보존해야 합니다.");

                DataGridViewRow first = AddMetricRow(grid, "203.0.113.10");
                DataGridViewRow second = AddMetricRow(grid, "198.51.100.20");
                DataGridViewRow missing = AddMetricRow(grid, "192.0.2.30");
                DataGridViewRow insufficient = AddMetricRow(grid, "192.0.2.31");
                DataGridViewRow noRecentRecord = AddMetricRow(grid, "192.0.2.32");
                applyMetrics.Invoke(form, new object[] { first, "203.0.113.10" });
                applyMetrics.Invoke(form, new object[] { second, "198.51.100.20" });
                applyMetrics.Invoke(form, new object[] { missing, "192.0.2.30" });
                applyMetrics.Invoke(form, new object[] { insufficient, "192.0.2.31" });
                applyMetrics.Invoke(form, new object[] { noRecentRecord, "192.0.2.32" });

                const string currentPingHelp =
                    "차단 규칙이 해당 IP의 모든 아웃바운드 통신(ICMP 포함)을 막으므로 차단 중에는 "
                    + "현재 핑을 측정할 수 없습니다. 해제 후 메인 화면에서 조회해 주세요.";
                Assert(Convert.ToString(first.Cells["currentPing"].Value)
                        == "차단 중 · 측정 불가"
                    && first.Cells["currentPing"].ToolTipText == currentPingHelp,
                    "차단 중 현재 핑을 실제 서버 상태처럼 오인하지 않도록 측정 불가 이유를 제공해야 합니다.");
                Assert(Convert.ToString(first.Cells["actualRtt"].Value) == "142 ms"
                    && Convert.ToString(first.Cells["packetLoss"].Value) == "3.1%"
                    && first.Cells["actualRtt"].ToolTipText.Contains(
                        "2026-08-17 20:10:00 레이드 로그")
                    && first.Cells["packetLoss"].ToolTipText.Contains(
                        "2026-08-17 20:20:00 레이드 로그")
                    && first.Cells["actualRtt"].ToolTipText.EndsWith(
                        "현재 측정값이 아닙니다.", StringComparison.Ordinal),
                    "최근 실게임 지표와 서로 다른 근거 시각을 정확히 표시해야 합니다.");
                Assert(Convert.ToString(missing.Cells["actualRtt"].Value) == "로그없음"
                    && Convert.ToString(missing.Cells["packetLoss"].Value) == "로그없음"
                    && missing.Cells["actualRtt"].ToolTipText
                        == "레이드 진행 중 또는 게임의 버그 · 비정상 종료로 필요한 로그가 기록되지 않았을 수 있습니다.",
                    "실제 로그 지표가 없을 때만 로그없음과 공통 안내를 표시해야 합니다.");
                const string noRecentRecordHelp =
                    "불러온 최근 레이드 최대 100개에서 이 IP의 접속 기록을 찾지 못했습니다. "
                    + "더 오래된 기록은 이 목록에 표시되지 않을 수 있습니다.";
                Assert(Convert.ToString(noRecentRecord.Cells["actualRtt"].Value)
                        == "최근 기록 없음"
                    && Convert.ToString(noRecentRecord.Cells["packetLoss"].Value)
                        == "최근 기록 없음"
                    && noRecentRecord.Cells["actualRtt"].ToolTipText == noRecentRecordHelp
                    && noRecentRecord.Cells["actualRtt"].AccessibilityObject.Description
                        == noRecentRecordHelp,
                    "최근 100개에 IP 기록이 없는 상태를 해당 레이드의 Statistics 누락과 구분해야 합니다.");
                Assert(Convert.ToString(insufficient.Cells["actualRtt"].Value) == "표본부족"
                    && Convert.ToString(insufficient.Cells["packetLoss"].Value) == "0% (참고)"
                    && insufficient.Cells["actualRtt"].ToolTipText.Contains(
                        "2026-08-17 20:40:02 레이드 로그")
                    && insufficient.Cells["actualRtt"].ToolTipText.Contains("수신 표본이 0건")
                    && insufficient.Cells["packetLoss"].ToolTipText.Contains("참고값")
                    && Convert.ToDouble(insufficient.Cells["packetLoss"].Tag) == 0,
                    "차단현황도 수신 0건의 RTT와 참고 손실값을 로그없음으로 잃지 않아야 합니다.");
                Assert(first.Cells["currentPing"].AccessibilityObject.Description
                        == first.Cells["currentPing"].ToolTipText
                    && first.Cells["actualRtt"].AccessibilityObject.Description
                        == first.Cells["actualRtt"].ToolTipText
                    && insufficient.Cells["packetLoss"].AccessibilityObject.Description
                        == insufficient.Cells["packetLoss"].ToolTipText,
                    "현재 핑과 실게임 지표의 전체 설명을 셀 접근성 설명에도 제공해야 합니다.");

                Assert(grid.Columns["currentPing"].HeaderCell.ToolTipText.Contains(
                        "ICMP 포함")
                    && grid.Columns["currentPing"].HeaderCell.AccessibilityObject.Description
                        == grid.Columns["currentPing"].HeaderCell.ToolTipText
                    && grid.Columns["actualRtt"].HeaderCell.AccessibilityObject.Description
                        == grid.Columns["actualRtt"].HeaderCell.ToolTipText
                    && grid.AccessibleDescription.Contains(
                        "차단 중에는 측정할 수 없습니다."),
                    "측정 범위와 제약을 헤더 툴팁과 접근성 설명에서 동일하게 제공해야 합니다.");

                const string refreshHelp =
                    "차단 규칙 목록만 새로고침합니다. 실게임 지표는 이 창을 다시 열 때 최신 로그로 갱신됩니다.";
                Assert(refreshButton.AccessibleName == "차단 규칙 목록 새로고침"
                    && refreshButton.AccessibleDescription == refreshHelp
                    && toolTip.GetToolTip(refreshButton) == refreshHelp,
                    "창 안 새로고침이 방화벽 규칙만 갱신하고 지표 갱신은 재열기 시점이라는 범위를 명확히 알려야 합니다.");

                InvokeHeaderClick(form, grid, headerClick, "actualRtt");
                Assert(GetIpOrder(grid)
                        == "198.51.100.20|203.0.113.10|192.0.2.30|192.0.2.31|192.0.2.32",
                    "실게임 RTT는 문자열이 아닌 수치로 정렬하고 로그 없는 값은 마지막에 유지해야 합니다.");

                form.CreateControl();
                form.PerformLayout();
                grid.CreateControl();
                grid.PerformLayout();
                Assert(form.ClientSize.Width == 1320
                    && !grid.Controls.OfType<HScrollBar>().Any(scroll => scroll.Visible),
                    "기본 창 폭에서는 새 지표 열 때문에 가로 스크롤이 처음부터 나타나면 안 됩니다.");

                Control[] notices = form.Controls.Find("firewallPartyScopeNotice", true);
                const string partyScope =
                    "차단 리스트는 이 PC에만 적용됩니다. 파티 매칭에서는 각 파티원이 같은 서버를 차단해야 "
                    + "모든 파티원의 접속을 막을 수 있습니다.";
                Assert(notices.Length == 1
                    && notices[0].Text == partyScope
                    && notices[0].AccessibleDescription == partyScope,
                    "파티별 로컬 차단 범위를 화면과 접근성 설명에 동일하게 제공해야 합니다.");
            }

            ConstructorInfo completenessConstructor = formType.GetConstructor(
                new[] { enumerableType, typeof(bool) });
            Assert(completenessConstructor != null,
                "최근 로그 완전성 상태를 차단현황에 전달하는 생성자가 필요합니다.");
            using (var incompleteForm = (Form)completenessConstructor.Invoke(
                new object[] { sessions, false }))
            {
                var grid = (DataGridView)formType.GetField("_grid", instanceFlags)
                    .GetValue(incompleteForm);
                MethodInfo applyMetrics = formType.GetMethod("ApplyMetricCells", instanceFlags);
                DataGridViewRow row = AddMetricRow(grid, "203.0.113.10");
                applyMetrics.Invoke(incompleteForm, new object[] { row, "203.0.113.10" });
                Assert(Convert.ToString(row.Cells["actualRtt"].Value) == "확인 불가"
                    && Convert.ToString(row.Cells["packetLoss"].Value) == "확인 불가"
                    && row.Cells["actualRtt"].ToolTipText.Contains("일부 로그를 읽지 못해")
                    && row.Cells["actualRtt"].AccessibilityObject.Description
                        == row.Cells["actualRtt"].ToolTipText,
                    "불완전 스캔은 부분 지표를 최신값이나 로그없음으로 단정하지 않아야 합니다.");
            }
        }

        private static object CreateSession(
            Type sessionType,
            Type hostingModeType,
            string ipAddress,
            DateTime detectedAt,
            double? actualRtt,
            double? packetLoss,
            bool local,
            bool statisticsObserved = false,
            long received = 0,
            DateTime? statisticsAt = null)
        {
            object session = Activator.CreateInstance(sessionType);
            sessionType.GetProperty("IpAddress").SetValue(session, ipAddress, null);
            sessionType.GetProperty("SessionStarted").SetValue(session, detectedAt, null);
            sessionType.GetProperty("LastUpdated").SetValue(session, detectedAt, null);
            sessionType.GetProperty("HostingMode").SetValue(
                session,
                Enum.Parse(hostingModeType, local ? "Local" : "Server"),
                null);
            if (actualRtt.HasValue)
                sessionType.GetProperty("ActualRttMs").SetValue(session, actualRtt.Value, null);
            if (packetLoss.HasValue)
                sessionType.GetProperty("NetworkLoss").SetValue(session, packetLoss.Value, null);
            sessionType.GetProperty("NetworkStatisticsObserved").SetValue(
                session,
                statisticsObserved,
                null);
            sessionType.GetProperty("NetworkReceived").SetValue(session, received, null);
            sessionType.GetProperty("NetworkStatisticsAt").SetValue(session, statisticsAt, null);
            return session;
        }

        private static DataGridViewRow AddMetricRow(DataGridView grid, string ipAddress)
        {
            int index = grid.Rows.Add(
                false,
                ipAddress,
                "차단 중",
                "차단 중 · 측정 불가",
                "로그없음",
                "로그없음",
                "-",
                "확인 안 됨",
                string.Empty,
                "현재 규칙",
                "해제");
            return grid.Rows[index];
        }

        private static void AssertUsageNotice(Assembly application)
        {
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
            Type usageType = application.GetType("TarkovServerReporter.UsageNoticeForm", true);
            string step2 = Convert.ToString(usageType.GetField(
                "Step2Line",
                staticFlags).GetRawConstantValue());
            string step2Explanation = Convert.ToString(usageType.GetField(
                "Step2ExplanationLine",
                staticFlags).GetRawConstantValue());
            string noticeText = Convert.ToString(usageType.GetField(
                "NoticeText",
                staticFlags).GetRawConstantValue());
            const string expected =
                "2. 다음 화면에서 재진입 대신 나가기 확인 선택 (장비는 보존됩니다)";
            const string expectedExplanation =
                "   서버 입장이 완료되지 않은 상태라 일반 레이드 이탈과 다릅니다.";
            using (var form = (Form)Activator.CreateInstance(usageType))
            {
                Assert(step2 == expected
                    && step2Explanation == expectedExplanation
                    && noticeText.Contains(expected)
                    && noticeText.Contains(expectedExplanation)
                    && form.AccessibleDescription == noticeText,
                    "사용방법 2번은 장비 보존과 일반 레이드 이탈의 차이를 화면 읽기 프로그램에도 제공해야 합니다.");
                Control[] visibleParts = GetDescendants(form).ToArray();
                Assert(visibleParts.Any(control => control.Text == "나가기 확인")
                    && visibleParts.Any(control =>
                        control.Text == " 선택 (장비는 보존됩니다)"),
                    "사용방법의 실제 토큰 UI에도 장비 보존 문구가 표시되어야 합니다.");
                Assert(visibleParts.Any(control => control.Text == expectedExplanation),
                    "사용방법 화면에 일반 레이드 이탈과 다른 절차라는 설명이 표시되어야 합니다.");
            }
        }

        private static IEnumerable<Control> GetDescendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control descendant in GetDescendants(child))
                    yield return descendant;
            }
        }

        private static void AssertSelectionAccessibility(
            DataGridView grid,
            Label selectedCountLabel,
            int selectedCount,
            int totalCount)
        {
            string visibleText = string.Format("선택 {0}개", selectedCount);
            string countName = string.Format("선택된 차단 서버 수: {0}개", selectedCount);
            string countDescription = string.Format(
                "현재 차단 서버 {0}개 중 {1}개가 선택되어 있습니다.",
                totalCount,
                selectedCount);
            string nextAction = totalCount > 0 && selectedCount == totalCount
                ? "전체 해제"
                : "전체 선택";
            string headerDescription = string.Format(
                "차단 해제 대상을 고르는 체크박스 열입니다. 헤더를 누르면 {0}합니다. "
                    + "현재 {1}개 중 {2}개가 선택되어 있습니다.",
                nextAction,
                totalCount,
                selectedCount);
            string gridDescription = string.Format(
                "선택 체크박스 열에서 차단 해제할 서버를 고릅니다. "
                    + "현재 차단 서버 {0}개 중 {1}개가 선택되어 있습니다. "
                    + "선택 열 헤더는 전체 선택 또는 전체 해제하고 Ctrl+A는 전체 선택합니다. "
                    + "실게임 RTT와 패킷손실은 서로 다른 최근 레이드의 값일 수 있으며, 현재 핑은 차단 규칙이 "
                    + "ICMP를 포함한 통신을 막아 차단 중에는 측정할 수 없습니다.",
                totalCount,
                selectedCount);
            AccessibleObject header = grid.Columns["selected"].HeaderCell.AccessibilityObject;

            Assert(selectedCountLabel.Text == visibleText
                && selectedCountLabel.AccessibleName == countName
                && selectedCountLabel.AccessibleDescription == countDescription,
                "선택 개수 화면 표시와 접근성 이름·설명이 같은 값으로 갱신되어야 합니다.");
            Assert(grid.AccessibleDescription == gridDescription
                && header.Description == headerDescription
                && header.DefaultAction == nextAction,
                "목록과 선택 헤더의 접근성 상태가 현재 선택 개수와 다음 동작을 설명해야 합니다.");

            AccessibleStates checkedState = header.State
                & (AccessibleStates.Checked | AccessibleStates.Mixed);
            AccessibleStates expectedState = selectedCount == 0
                ? AccessibleStates.None
                : selectedCount == totalCount
                    ? AccessibleStates.Checked
                    : AccessibleStates.Mixed;
            Assert(checkedState == expectedState,
                "선택 헤더의 체크·부분 선택 접근성 상태가 실제 선택 상태와 일치해야 합니다.");
        }

        private static string CreateSelectionAccessibilitySnapshot(
            DataGridView grid,
            Label selectedCountLabel)
        {
            AccessibleObject header = grid.Columns["selected"].HeaderCell.AccessibilityObject;
            AccessibleStates state = header.State
                & (AccessibleStates.Checked | AccessibleStates.Mixed);
            return string.Join(
                "|",
                selectedCountLabel.Text,
                selectedCountLabel.AccessibleName,
                selectedCountLabel.AccessibleDescription,
                grid.AccessibleDescription,
                header.Description,
                header.DefaultAction,
                state.ToString());
        }

        private static void AddServerRow(DataGridView grid, Type serverType, string ipAddress)
        {
            object server = Activator.CreateInstance(serverType);
            serverType.GetProperty("IpAddress").SetValue(server, ipAddress, null);
            int index = grid.Rows.Add(
                false,
                ipAddress,
                "차단 중",
                "차단 중 · 측정 불가",
                "로그없음",
                "로그없음",
                "-",
                "확인 안 됨",
                string.Empty,
                "현재 규칙",
                "해제");
            grid.Rows[index].Tag = server;
        }

        private static DataGridViewRow AddSortableServerRow(
            DataGridView grid,
            Type serverType,
            string ipAddress,
            string status,
            string location,
            string blockedAt,
            bool hasNote,
            string kind,
            bool selected)
        {
            object server = Activator.CreateInstance(serverType);
            serverType.GetProperty("IpAddress").SetValue(server, ipAddress, null);
            int index = grid.Rows.Add(
                selected,
                ipAddress,
                status,
                "차단 중 · 측정 불가",
                "로그없음",
                "로그없음",
                location,
                blockedAt,
                hasNote ? "저장된 차단 메모" : "차단 메모 추가",
                kind,
                "해제");
            DataGridViewRow row = grid.Rows[index];
            row.Tag = server;
            row.Cells["note"].Tag = hasNote;
            return row;
        }

        private static void InvokeHeaderClick(
            Form form,
            DataGridView grid,
            MethodInfo handler,
            string columnName)
        {
            var mouse = new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0);
            var args = new DataGridViewCellMouseEventArgs(
                grid.Columns[columnName].Index,
                -1,
                1,
                1,
                mouse);
            handler.Invoke(form, new object[] { grid, args });
        }

        private static void InvokeHeaderMouseMove(
            Form form,
            DataGridView grid,
            MethodInfo handler,
            string columnName)
        {
            InvokeCellMouseMove(form, grid, handler, columnName, -1);
        }

        private static void InvokeCellMouseMove(
            Form form,
            DataGridView grid,
            MethodInfo handler,
            string columnName,
            int rowIndex)
        {
            var mouse = new MouseEventArgs(MouseButtons.None, 0, 1, 1, 0);
            var args = new DataGridViewCellMouseEventArgs(
                grid.Columns[columnName].Index,
                rowIndex,
                1,
                1,
                mouse);
            handler.Invoke(form, new object[] { grid, args });
        }

        private static string CaptureSelectionHeaderVisual(DataGridView grid)
        {
            int columnIndex = grid.Columns["selected"].Index;
            Rectangle header = grid.GetCellDisplayRectangle(columnIndex, -1, true);
            if (header.Width <= 0 || header.Height <= 0)
            {
                header = new Rectangle(
                    0,
                    0,
                    grid.Columns["selected"].Width,
                    grid.ColumnHeadersHeight);
            }
            Assert(header.Width > 0 && header.Height > 0
                    && grid.ClientSize.Width >= header.Width
                    && grid.ClientSize.Height >= header.Height,
                "선택 헤더 체크박스의 표시 영역을 계산할 수 있어야 합니다.");
            using (var bitmap = new Bitmap(
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

        private static string GetIpOrder(DataGridView grid)
        {
            var values = new List<string>();
            foreach (DataGridViewRow row in grid.Rows)
                values.Add(Convert.ToString(row.Cells["ip"].Value));
            return string.Join("|", values.ToArray());
        }

        private static void AssertRowsRetainActionsAndState(
            DataGridView grid,
            IDictionary<string, object> originalTags)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                string ipAddress = Convert.ToString(row.Cells["ip"].Value);
                Assert(originalTags.ContainsKey(ipAddress)
                    && ReferenceEquals(row.Tag, originalTags[ipAddress])
                    && Convert.ToString(row.Cells["remove"].Value) == "해제",
                    "정렬 뒤에도 서버 행 Tag와 개별 해제 동작이 유지되어야 합니다.");
                bool expectedSelected = ipAddress == "8.8.8.8";
                Assert(Convert.ToBoolean(row.Cells["selected"].Value ?? false)
                        == expectedSelected,
                    "정렬 뒤에도 체크 선택은 같은 서버에 남아 있어야 합니다.");
            }
        }

        private static bool AllRowsHaveSelection(DataGridView grid, bool expected)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (Convert.ToBoolean(row.Cells["selected"].Value ?? false) != expected)
                    return false;
            }
            return true;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
