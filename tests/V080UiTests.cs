// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class V080UiTests
    {
        private struct ButtonInkMetrics
        {
            public Rectangle Bounds;
            public int PixelCount;

            public double CenterX
            {
                get { return (Bounds.Left + Bounds.Right - 1) / 2D; }
            }

            public double CenterY
            {
                get { return (Bounds.Top + Bounds.Bottom - 1) / 2D; }
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length != 2 || !File.Exists(args[0]))
                    throw new InvalidOperationException(
                        "테스트할 TarkovServerGuard.exe 경로와 버전이 필요합니다.");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Assembly application = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                AssertApplicationVersion(application, args[1]);
                AssertDeferredBuildIdentity(application);
                Type mainFormType = application.GetType("TarkovServerReporter.MainForm", true);
                ConstructorInfo constructor = mainFormType.GetConstructor(new[] { typeof(bool) });
                Assert(constructor != null, "MainForm 미리보기 생성자를 찾을 수 없습니다.");
                AssertDeferredUninstallAndPatchNotesRouting(application);
                AssertPatchNotesDarkBorder(application);

                using (var form = (Form)constructor.Invoke(new object[] { true }))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-10000, -10000);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();

                    FieldInfo gridField = mainFormType.GetField(
                        "_historyGrid",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert(gridField != null, "접속 기록 표 필드를 찾을 수 없습니다.");
                    var grid = gridField.GetValue(form) as DataGridView;
                    Assert(grid != null, "접속 기록 표가 생성되지 않았습니다.");

                    AssertTwoLineMeasurementColumn(grid, "actualRtt", "RTT");
                    AssertTwoLineMeasurementColumn(grid, "packetLoss", "패킷손실");
                    AssertPathActionButtonTextAlignment(mainFormType, form);
                    AssertMainHeaderSortToolTips(mainFormType, form, grid);
                    AssertRaidContextPresentation(application, mainFormType, form, grid);
                    AssertIncompleteRefreshClassificationMerge(application, mainFormType);
                    AssertMainActionHeaderSorting(mainFormType, form, grid);

                    using (var bitmap = new Bitmap(
                        Math.Max(1, grid.ClientSize.Width),
                        Math.Max(1, Math.Min(grid.ClientSize.Height, 240))))
                    {
                        grid.DrawToBitmap(
                            bitmap,
                            new Rectangle(Point.Empty, bitmap.Size));
                    }

                    AssertMissingLogTooltips(application, mainFormType);
                    AssertPrivacyNoticeText(form);
                    AssertFirewallPersistenceUi(application, mainFormType, form);
                    AssertInitialFirewallStatePresentation(mainFormType, form);
                    AssertNoPersistentPatchNotesOrUninstallButtons(form);
                }

                Console.WriteLine("V080UiTests: PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("V080UiTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void AssertApplicationVersion(
            Assembly application,
            string expectedVersion)
        {
            Version version = application.GetName().Version;
            string actualVersion = string.Format(
                "{0}.{1}.{2}",
                version.Major,
                version.Minor,
                version.Build);
            Assert(actualVersion == expectedVersion,
                "제품 EXE 버전이 빌드 요청 버전과 일치하지 않습니다.");
        }

        private static void AssertDeferredBuildIdentity(Assembly application)
        {
            Assert(application.GetType("TarkovServerReporter.BuildIdentity", false) == null,
                "보류한 Build ID 런타임 코드가 제품 EXE에 포함되었습니다.");
            Assert(Array.IndexOf(
                application.GetManifestResourceNames(),
                "TarkovServerReporter.BuildIdentity.json") < 0,
                "보류한 Build ID 리소스가 제품 EXE에 포함되었습니다.");
        }

        private static void AssertDeferredUninstallAndPatchNotesRouting(
            Assembly application)
        {
            const BindingFlags staticFlags = BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            Type programType = application.GetType("TarkovServerReporter.Program", true);
            Assert(application.GetType("TarkovServerReporter.UninstallSupport", false) == null,
                "보류한 설치 제거 지원 코드가 제품 EXE에 포함되었습니다.");
            Assert(application.GetType("TarkovServerReporter.UninstallOptionsForm", false) == null,
                "보류한 설치 제거 UI가 제품 EXE에 포함되었습니다.");
            Assert(programType.GetMethod("ShouldRouteUninstallToInspection", staticFlags) == null,
                "보류한 --uninstall 제품 라우팅이 남아 있습니다.");

            MethodInfo previewRoute = programType.GetMethod(
                "ShouldShowPatchNotesPreview",
                staticFlags);
            Assert(previewRoute == null,
                "정식 제품에 패치 내역을 임의로 다시 여는 검수 인자가 남아 있습니다.");
            Type patchNotesType = application.GetType(
                "TarkovServerReporter.PatchNotesForm",
                true);
            Assert(patchNotesType.GetMethod("ShowPreview", staticFlags) == null,
                "제품의 일회성 업데이트 완료창에 임의 미리보기 API가 남아 있습니다.");
        }

        private static void AssertNoPersistentPatchNotesOrUninstallButtons(Form form)
        {
            foreach (Button button in FindControls<Button>(form))
            {
                Assert(!string.Equals(button.Text, "패치내역", StringComparison.Ordinal),
                    "메인 화면에 상시 패치내역 버튼이 남아 있습니다.");
                Assert(!string.Equals(button.Text, "설치제거", StringComparison.Ordinal),
                    "보류한 설치 제거 버튼이 메인 화면에 남아 있습니다.");
            }
        }

        private static void AssertPatchNotesDarkBorder(Assembly application)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            Type entryType = application.GetType(
                "TarkovServerReporter.ReleaseNotesEntry",
                true);
            object entry = Activator.CreateInstance(entryType, true);
            entryType.GetProperty("VersionText", instanceFlags)
                .SetValue(entry, "0.8.1", null);
            entryType.GetProperty("NotesText", instanceFlags)
                .SetValue(entry, "테두리 검증", null);

            Type formType = application.GetType(
                "TarkovServerReporter.PatchNotesForm",
                true);
            using (var form = (Form)Activator.CreateInstance(
                formType,
                instanceFlags,
                null,
                new[] { entry },
                null))
            {
                Panel border = FindControlByName(form, "notesBorderPanel") as Panel;
                RichTextBox notes = null;
                foreach (RichTextBox candidate in FindControls<RichTextBox>(form))
                {
                    notes = candidate;
                    break;
                }
                Assert(border != null && notes != null && notes.Parent == border,
                    "패치내역 텍스트가 전용 다크 테두리 컨테이너 안에 있어야 합니다.");
                Assert(border.BackColor == Color.FromArgb(54, 63, 74)
                    && border.Padding == new Padding(1),
                    "패치내역 테두리는 기존 다크 테마 테두리 색과 1px 두께를 사용해야 합니다.");
                Assert(notes.BorderStyle == BorderStyle.None,
                    "패치내역 텍스트박스의 흰색 네이티브 테두리가 남아 있습니다.");
                Assert(notes.ReadOnly
                    && notes.AccessibleName == "업데이트 변경 사항"
                    && !string.IsNullOrWhiteSpace(notes.AccessibleDescription),
                    "테두리 변경 후에도 읽기 전용·접근성 동작이 유지되어야 합니다.");
            }
        }

        private static void AssertTwoLineMeasurementColumn(
            DataGridView grid,
            string columnName,
            string measurementLabel)
        {
            Assert(grid.Columns.Contains(columnName), "측정값 열이 없습니다: " + columnName);
            DataGridViewColumn column = grid.Columns[columnName];
            string expectedHeader = "실게임\r\n" + measurementLabel;
            Assert(string.Equals(column.HeaderText, expectedHeader, StringComparison.Ordinal),
                columnName + " 헤더는 지정한 두 줄 문구를 그대로 유지해야 합니다.");
            Assert(column.HeaderText.Split(new[] { "\r\n" }, StringSplitOptions.None).Length == 2,
                columnName + " 헤더는 정확히 두 줄이어야 합니다.");
            Assert(column.HeaderCell.Style.Alignment == DataGridViewContentAlignment.MiddleLeft,
                columnName + " 헤더는 좌측 정렬이어야 합니다.");
            Assert(column.HeaderCell.Style.WrapMode == DataGridViewTriState.True,
                columnName + " 헤더는 명시적 줄바꿈을 표시할 수 있어야 합니다.");

            using (var defaultColumn = new DataGridViewTextBoxColumn())
            {
                Assert(column.MinimumWidth == defaultColumn.MinimumWidth,
                    columnName + " 열에 별도의 최소 축소 폭을 강제하면 안 됩니다.");
            }
            column.Width = column.MinimumWidth;
            Assert(column.Width == column.MinimumWidth,
                columnName + " 열은 기본 최소 폭까지 사용자가 줄일 수 있어야 합니다.");
        }

        private static void AssertPathActionButtonTextAlignment(
            Type mainFormType,
            Form mainForm)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            string[] fieldNames =
            {
                "_eftBrowseButton",
                "_arenaBrowseButton",
                "_rediscoverPathButton",
                "_applyPathButton"
            };
            string[] expectedTexts =
            {
                "직접선택",
                "직접선택",
                "자동 찾기",
                "적용"
            };

            for (int index = 0; index < fieldNames.Length; index++)
            {
                FieldInfo field = mainFormType.GetField(fieldNames[index], instanceFlags);
                var button = field == null ? null : field.GetValue(mainForm) as Button;
                Assert(button != null
                        && string.Equals(
                            button.Text,
                            expectedTexts[index],
                            StringComparison.Ordinal),
                    "TSG 경로 작업 버튼을 찾지 못했습니다: " + expectedTexts[index]);
                Assert(button.TextAlign == ContentAlignment.MiddleCenter,
                    expectedTexts[index] + " 경로 버튼 문구는 가로·세로 중앙에 있어야 합니다.");
                Assert(button.UseCompatibleTextRendering,
                    expectedTexts[index]
                    + " 경로 버튼은 글자 행을 보존하며 광학 중앙을 맞추는 렌더러를 사용해야 합니다.");
                Assert(button.Padding.Left == 0
                        && button.Padding.Top == 0
                        && button.Padding.Right == 0
                        && button.Padding.Bottom == 0,
                    expectedTexts[index]
                    + " 경로 버튼에 비대칭 패딩을 적용하면 고 DPI에서 중앙을 지나치면 안 됩니다.");

                ButtonInkMetrics adjusted = MeasureButtonInk(button);
                using (Button intendedReference = CreateButtonRasterReference(
                    button,
                    true))
                using (Button legacyReference = CreateButtonRasterReference(
                    button,
                    false))
                {
                    ButtonInkMetrics intended = MeasureButtonInk(intendedReference);
                    ButtonInkMetrics legacy = MeasureButtonInk(legacyReference);
                    Assert(legacy.CenterY - adjusted.CenterY >= 1D,
                        expectedTexts[index]
                        + " 경로 버튼 글자가 기존 GDI 렌더 기준보다 실제 픽셀상 위로 이동해야 합니다.");
                    Assert(adjusted.PixelCount == intended.PixelCount
                            && adjusted.Bounds == intended.Bounds,
                        expectedTexts[index]
                        + " 경로 버튼은 패딩 없는 기준 렌더의 전체 글자 픽셀을 그대로 표시해야 합니다.");
                    Assert(adjusted.PixelCount >= legacy.PixelCount
                            && adjusted.Bounds.Height >= legacy.Bounds.Height,
                        expectedTexts[index]
                        + " 경로 버튼 렌더러 변경으로 글자 행이나 전경 픽셀이 손실되면 안 됩니다.");
                    double clientCenterX = (button.ClientSize.Width - 1) / 2D;
                    Assert(Math.Abs(adjusted.CenterX - clientCenterX)
                            <= Math.Abs(legacy.CenterX - clientCenterX),
                        expectedTexts[index]
                        + " 경로 버튼의 가로 중앙 정렬이 기존 렌더보다 나빠지면 안 됩니다.");
                }
            }
        }

        private static Button CreateButtonRasterReference(
            Button source,
            bool useCompatibleTextRendering)
        {
            var reference = new Button
            {
                AutoSize = false,
                Size = source.ClientSize,
                Text = source.Text,
                Font = source.Font,
                ForeColor = source.ForeColor,
                BackColor = source.BackColor,
                FlatStyle = source.FlatStyle,
                UseVisualStyleBackColor = source.UseVisualStyleBackColor,
                UseCompatibleTextRendering = useCompatibleTextRendering,
                TextAlign = source.TextAlign,
                Padding = new Padding(0),
                Enabled = source.Enabled,
                TabStop = false
            };
            reference.FlatAppearance.BorderSize = source.FlatAppearance.BorderSize;
            reference.FlatAppearance.BorderColor = source.FlatAppearance.BorderColor;
            reference.FlatAppearance.CheckedBackColor = source.FlatAppearance.CheckedBackColor;
            reference.FlatAppearance.MouseDownBackColor = source.FlatAppearance.MouseDownBackColor;
            reference.FlatAppearance.MouseOverBackColor = source.FlatAppearance.MouseOverBackColor;
            reference.CreateControl();
            return reference;
        }

        private static ButtonInkMetrics MeasureButtonInk(Button button)
        {
            Assert(button != null && button.ClientSize.Width > 4 && button.ClientSize.Height > 4,
                "렌더링할 경로 버튼의 크기가 올바르지 않습니다.");
            using (var bitmap = new Bitmap(button.ClientSize.Width, button.ClientSize.Height))
            {
                button.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Color foreground = button.Enabled ? button.ForeColor : SystemColors.GrayText;
                Color background = button.BackColor;
                int minimumX = bitmap.Width;
                int minimumY = bitmap.Height;
                int maximumX = -1;
                int maximumY = -1;
                int pixelCount = 0;
                for (int y = 2; y < bitmap.Height - 2; y++)
                {
                    for (int x = 2; x < bitmap.Width - 2; x++)
                    {
                        Color pixel = bitmap.GetPixel(x, y);
                        if (ColorDistanceSquared(pixel, foreground)
                            >= ColorDistanceSquared(pixel, background))
                            continue;

                        minimumX = Math.Min(minimumX, x);
                        minimumY = Math.Min(minimumY, y);
                        maximumX = Math.Max(maximumX, x);
                        maximumY = Math.Max(maximumY, y);
                        pixelCount++;
                    }
                }

                Assert(pixelCount > 0,
                    button.Text + " 경로 버튼의 렌더링된 글자 픽셀을 찾지 못했습니다.");
                return new ButtonInkMetrics
                {
                    Bounds = Rectangle.FromLTRB(
                        minimumX,
                        minimumY,
                        maximumX + 1,
                        maximumY + 1),
                    PixelCount = pixelCount
                };
            }
        }

        private static int ColorDistanceSquared(Color left, Color right)
        {
            int red = left.R - right.R;
            int green = left.G - right.G;
            int blue = left.B - right.B;
            return red * red + green * green + blue * blue;
        }

        private static void AssertMainHeaderSortToolTips(
            Type mainFormType,
            Form mainForm,
            DataGridView historyGrid)
        {
            const string sortHelp =
                "클릭할 때마다 오름차순, 내림차순, 기본 순서로 정렬합니다.";
            const string resultHelp =
                "마지막 서버 연결 구간의 결과와 재접속 횟수입니다. 셀에 마우스를 올리면 상태별 의미를 확인할 수 있습니다.";
            string[] sortableColumns =
            {
                "game", "time", "userReport", "note", "mapMode", "ip",
                "location", "ping", "actualRtt", "packetLoss", "result"
            };

            int sortableCount = 0;
            foreach (DataGridViewColumn column in historyGrid.Columns)
            {
                if (column.SortMode == DataGridViewColumnSortMode.Programmatic)
                    sortableCount++;
                else
                    Assert((column.HeaderCell.ToolTipText ?? string.Empty).IndexOf(
                            sortHelp,
                            StringComparison.Ordinal) < 0,
                        column.Name + " 비정렬 헤더가 정렬 가능하다고 안내하면 안 됩니다.");
            }
            Assert(sortableCount == sortableColumns.Length,
                "메인 접속 기록 표의 실제 정렬 헤더 수가 달라졌습니다.");

            foreach (string columnName in sortableColumns)
            {
                Assert(historyGrid.Columns.Contains(columnName),
                    "메인 접속 기록 정렬 헤더가 없습니다: " + columnName);
                DataGridViewColumn column = historyGrid.Columns[columnName];
                string expected = string.Equals(
                        columnName,
                        "result",
                        StringComparison.Ordinal)
                    ? resultHelp + "\r\n" + sortHelp
                    : sortHelp;
                Assert(column.SortMode == DataGridViewColumnSortMode.Programmatic
                        && string.Equals(
                            column.HeaderCell.ToolTipText,
                            expected,
                            StringComparison.Ordinal),
                    columnName + " 정렬 헤더의 3단계 안내가 정확하지 않습니다.");
            }

            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            FieldInfo stickyGridField = mainFormType.GetField(
                "_stickyActionGrid",
                instanceFlags);
            var stickyGrid = stickyGridField == null
                ? null
                : stickyGridField.GetValue(mainForm) as DataGridView;
            Assert(stickyGrid != null && stickyGrid.Columns.Count == 2,
                "메인 고정 차단·해제 헤더의 툴팁 검증 경계를 찾지 못했습니다.");
            foreach (string columnName in new[] { "blockAction", "unblockAction" })
            {
                Assert(stickyGrid.Columns.Contains(columnName),
                    "메인 고정 작업 정렬 헤더가 없습니다: " + columnName);
                DataGridViewColumn column = stickyGrid.Columns[columnName];
                Assert(column.SortMode == DataGridViewColumnSortMode.Programmatic
                        && string.Equals(
                            column.HeaderCell.ToolTipText,
                            sortHelp,
                            StringComparison.Ordinal),
                    columnName + " 고정 작업 헤더의 3단계 안내가 정확하지 않습니다.");
            }
        }

        private static void AssertRaidContextPresentation(
            Assembly application,
            Type mainFormType,
            Form mainForm,
            DataGridView historyGrid)
        {
            Assert(historyGrid.Columns.Contains("mapMode"),
                "맵·게임유형·캐릭터·참가 형태 표시 열이 없습니다.");
            DataGridViewColumn column = historyGrid.Columns["mapMode"];
            Assert(column.HeaderText == "맵 · 게임유형"
                    && column.DefaultCellStyle.WrapMode == DataGridViewTriState.False,
                "레이드 문맥 열은 기존 헤더와 한 줄 표시를 유지해야 합니다.");

            string[] expectedRows =
            {
                "Streets of Tarkov · PvP시즌1 · PMC · 2인 파티",
                "Bay 5 · CheckPoint",
                "Woods · PvE(서버) · 스캐브 · 3인 파티",
                "Factory · PvE(로컬) · PMC · 솔로"
            };
            foreach (string expected in expectedRows)
            {
                DataGridViewCell cell = null;
                foreach (DataGridViewRow row in historyGrid.Rows)
                {
                    DataGridViewCell candidate = row.Cells["mapMode"];
                    if (!string.Equals(
                        Convert.ToString(candidate.Value),
                        expected,
                        StringComparison.Ordinal))
                        continue;
                    cell = candidate;
                    Assert(row.Height == historyGrid.RowTemplate.Height,
                        "레이드 문맥이 길어져도 접속 기록 행 높이가 바뀌면 안 됩니다.");
                    break;
                }
                Assert(cell != null, "레이드 문맥 표시가 정확하지 않습니다: " + expected);
                Assert(cell.ToolTipText == expected,
                    "잘린 레이드 문맥의 툴팁은 전문과 같아야 합니다: " + expected);
                Assert(cell.AccessibilityObject.Description == expected,
                    "보조기술용 레이드 문맥 설명은 전문과 같아야 합니다: " + expected);
            }

            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            FieldInfo mapLabelField = mainFormType.GetField("_mapValueLabel", instanceFlags);
            FieldInfo toolTipField = mainFormType.GetField("_toolTip", instanceFlags);
            var mapLabel = mapLabelField == null ? null : mapLabelField.GetValue(mainForm) as Label;
            var toolTip = toolTipField == null ? null : toolTipField.GetValue(mainForm) as ToolTip;
            string selectedText = "EFT · " + expectedRows[0];
            Assert(mapLabel != null && toolTip != null
                    && mapLabel.Text == selectedText
                    && mapLabel.AccessibleName == "선택한 접속의 게임·맵·유형"
                    && mapLabel.AccessibleDescription == selectedText
                    && toolTip.GetToolTip(mapLabel) == selectedText,
                "선택 서버 상세의 레이드 문맥·툴팁·접근성 설명이 서로 달라졌습니다.");

            Type sessionType = application.GetType("TarkovServerReporter.ServerSession", true);
            object partial = Activator.CreateInstance(sessionType);
            SetProperty(partial, "MapName", "Factory");
            SetEnumProperty(partial, "Game", "Eft");
            SetEnumProperty(partial, "ProgressionMode", "PvpSeason");
            SetProperty(partial, "PvpSeasonNumber", 2);
            SetEnumProperty(partial, "ParticipationType", "Solo");
            MethodInfo formatter = mainFormType.GetMethod(
                "GetMapAndTypeText",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(formatter != null, "레이드 문맥 표시 생성기를 찾지 못했습니다.");
            Assert(Convert.ToString(formatter.Invoke(null, new[] { partial }))
                    == "Factory · PvP시즌2 · 솔로",
                "알 수 없는 캐릭터는 생략하고 확인된 참가 형태만 표시해야 합니다.");
            SetEnumProperty(partial, "ParticipationType", "Unknown");
            SetEnumProperty(partial, "CharacterType", "Pmc");
            Assert(Convert.ToString(formatter.Invoke(null, new[] { partial }))
                    == "Factory · PvP시즌2 · PMC",
                "알 수 없는 참가 형태는 생략하고 확인된 캐릭터만 표시해야 합니다.");
        }

        private static void AssertIncompleteRefreshClassificationMerge(
            Assembly application,
            Type mainFormType)
        {
            Type sessionType = application.GetType("TarkovServerReporter.ServerSession", true);
            MethodInfo merge = mainFormType.GetMethod(
                "MergeRefreshedSessions",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(merge != null, "불완전 새로고침의 기록 병합 경계를 찾지 못했습니다.");

            object refreshed = CreateClassificationSession(
                sessionType, "same-raid", "Unknown", "Unknown", null);
            object existing = CreateClassificationSession(
                sessionType, "same-raid", "Pmc", "Party", 2);
            object retained = InvokeSingleSessionMerge(merge, sessionType, refreshed, existing);
            Assert(GetPropertyText(retained, "CharacterType") == "Pmc"
                    && GetPropertyText(retained, "ParticipationType") == "Party"
                    && Convert.ToInt32(GetPropertyValue(retained, "PartySize")) == 2,
                "불완전 새로고침은 같은 레이드의 기존 분류 근거를 잃으면 안 됩니다.");

            object conflictingRefresh = CreateClassificationSession(
                sessionType, "conflict-raid", "Pmc", "Solo", null);
            object conflictingExisting = CreateClassificationSession(
                sessionType, "conflict-raid", "Scav", "Solo", null);
            object conflict = InvokeSingleSessionMerge(
                merge, sessionType, conflictingRefresh, conflictingExisting);
            Assert(GetPropertyText(conflict, "CharacterType") == "Unknown"
                    && GetPropertyText(conflict, "ParticipationType") == "Solo",
                "서로 충돌한 캐릭터 근거를 불완전 새로고침에서 임의 선택하면 안 됩니다.");
        }

        private static object CreateClassificationSession(
            Type sessionType,
            string key,
            string character,
            string participation,
            int? partySize)
        {
            object session = Activator.CreateInstance(sessionType);
            SetProperty(session, "SessionKey", key);
            SetEnumProperty(session, "Game", "Eft");
            SetEnumProperty(session, "CharacterType", character);
            SetEnumProperty(session, "ParticipationType", participation);
            if (partySize.HasValue) SetProperty(session, "PartySize", partySize.Value);
            return session;
        }

        private static object InvokeSingleSessionMerge(
            MethodInfo merge,
            Type sessionType,
            object refreshed,
            object existing)
        {
            Array refreshedArray = Array.CreateInstance(sessionType, 1);
            refreshedArray.SetValue(refreshed, 0);
            Array existingArray = Array.CreateInstance(sessionType, 1);
            existingArray.SetValue(existing, 0);
            var result = merge.Invoke(null, new object[] { refreshedArray, existingArray })
                as IEnumerable;
            Assert(result != null, "불완전 새로고침 병합 결과를 읽을 수 없습니다.");
            object retained = null;
            int count = 0;
            foreach (object item in result)
            {
                retained = item;
                count++;
            }
            Assert(count == 1 && retained != null,
                "같은 identity의 새·기존 레이드는 한 행으로 병합되어야 합니다.");
            return retained;
        }

        private static void SetEnumProperty(object target, string name, string value)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            Assert(property != null && property.PropertyType.IsEnum,
                "enum 속성을 찾지 못했습니다: " + name);
            property.SetValue(target, Enum.Parse(property.PropertyType, value), null);
        }

        private static void SetProperty(object target, string name, object value)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            Assert(property != null, "속성을 찾지 못했습니다: " + name);
            property.SetValue(target, value, null);
        }

        private static object GetPropertyValue(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            Assert(property != null, "속성을 찾지 못했습니다: " + name);
            return property.GetValue(target, null);
        }

        private static string GetPropertyText(object target, string name)
        {
            return Convert.ToString(GetPropertyValue(target, name));
        }

        private static void AssertMainActionHeaderSorting(
            Type mainFormType,
            Form mainForm,
            DataGridView historyGrid)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            FieldInfo stickyGridField = mainFormType.GetField(
                "_stickyActionGrid",
                instanceFlags);
            FieldInfo sortColumnField = mainFormType.GetField(
                "_historySortColumn",
                instanceFlags);
            FieldInfo sortOrderField = mainFormType.GetField(
                "_historySortOrder",
                instanceFlags);
            FieldInfo selectedSessionField = mainFormType.GetField(
                "_selectedSession",
                instanceFlags);
            MethodInfo raiseHeaderClickMethod = typeof(DataGridView).GetMethod(
                "OnColumnHeaderMouseClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo firewallStatesField = mainFormType.GetField(
                "_firewallStates",
                instanceFlags);
            FieldInfo measuringField = mainFormType.GetField(
                "_isMeasuring",
                instanceFlags);
            MethodInfo refreshActionCellsMethod = mainFormType.GetMethod(
                "RefreshActionCells",
                instanceFlags);
            MethodInfo reapplySortMethod = mainFormType.GetMethod(
                "ReapplyHistorySort",
                instanceFlags);
            var stickyGrid = stickyGridField == null
                ? null
                : stickyGridField.GetValue(mainForm) as DataGridView;
            Assert(stickyGrid != null
                && sortColumnField != null
                && sortOrderField != null
                && selectedSessionField != null
                && raiseHeaderClickMethod != null
                && firewallStatesField != null
                && measuringField != null
                && refreshActionCellsMethod != null
                && reapplySortMethod != null,
                "메인 화면 고정 차단·해제 열의 정렬 경계를 찾지 못했습니다.");
            Assert(stickyGrid.Columns.Contains("blockAction")
                && stickyGrid.Columns.Contains("unblockAction"),
                "고정 작업 영역에 차단·해제 열이 모두 있어야 합니다.");
            Assert(stickyGrid.Columns["blockAction"].HeaderText == "차단"
                && stickyGrid.Columns["unblockAction"].HeaderText == "해제",
                "고정 작업 열의 헤더 문구가 달라졌습니다.");
            Assert(stickyGrid.Columns["blockAction"].SortMode
                    == DataGridViewColumnSortMode.Programmatic
                && stickyGrid.Columns["unblockAction"].SortMode
                    == DataGridViewColumnSortMode.Programmatic,
                "차단·해제 헤더 모두 3단계 사용자 정렬을 지원해야 합니다.");

            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                "blockAction",
                raiseHeaderClickMethod,
                MouseButtons.Right);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                null, SortOrder.None, "차단 헤더 우클릭");

            // Make the selected original first row capable of moving outside the
            // viewport. Calling the handler directly and inspecting only Rows hid the
            // reported regression: selection restoration scrolled the old row back to
            // the top even though the underlying list had sorted correctly.
            int tallRowHeight = Math.Min(
                400,
                Math.Max(80, historyGrid.ClientSize.Height / 2 + 12));
            historyGrid.RowTemplate.Height = tallRowHeight;
            stickyGrid.RowTemplate.Height = tallRowHeight;
            foreach (DataGridViewRow row in historyGrid.Rows) row.Height = tallRowHeight;
            foreach (DataGridViewRow row in stickyGrid.Rows) row.Height = tallRowHeight;

            object selectedSession = FindSessionByKey(historyGrid, "eft-demo-1");
            Assert(selectedSession != null, "선택 보존 검사용 접속 기록을 찾지 못했습니다.");
            selectedSessionField.SetValue(mainForm, selectedSession);
            SelectHistorySession(historyGrid, selectedSession);

            InvokeActionHeaderClick(mainForm, stickyGrid, "blockAction", raiseHeaderClickMethod);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                "blockAction", SortOrder.Ascending, "차단 1차 클릭");
            AssertSessionOrder(historyGrid,
                new[] { "arena-demo-1", "eft-demo-2", "eft-demo-1", "eft-demo-local" },
                "차단 1차 정렬은 지금 차단 가능한 서버를 우선하고 상태 미확인은 마지막이어야 합니다.");
            AssertFirstDisplayedSession(historyGrid, stickyGrid, "arena-demo-1", "차단 1차 정렬");
            AssertActionButtonStates(historyGrid, stickyGrid);

            InvokeActionHeaderClick(mainForm, stickyGrid, "blockAction", raiseHeaderClickMethod);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                "blockAction", SortOrder.Descending, "차단 2차 클릭");
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-1", "eft-demo-2", "arena-demo-1", "eft-demo-local" },
                "차단 2차 정렬은 차단 불필요 서버를 우선하고 동률 순서도 반전해야 합니다.");
            AssertFirstDisplayedSession(historyGrid, stickyGrid, "eft-demo-1", "차단 2차 정렬");
            AssertSelectedSessionPreserved(historyGrid, stickyGrid, "eft-demo-1");
            AssertActionButtonStates(historyGrid, stickyGrid);
            AssertActiveActionHeaderUsesAccentArrow(stickyGrid, "blockAction");

            InvokeActionHeaderClick(mainForm, stickyGrid, "blockAction", raiseHeaderClickMethod);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                null, SortOrder.None, "차단 3차 클릭");
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-1", "arena-demo-1", "eft-demo-2", "eft-demo-local" },
                "차단 3차 클릭은 최신 접속 순서로 복귀해야 합니다.");
            AssertFirstDisplayedSession(historyGrid, stickyGrid, "eft-demo-1", "차단 기본순 복귀");

            InvokeActionHeaderClick(mainForm, stickyGrid, "unblockAction", raiseHeaderClickMethod);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                "unblockAction", SortOrder.Ascending, "해제 1차 클릭");
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-1", "arena-demo-1", "eft-demo-2", "eft-demo-local" },
                "해제 1차 정렬은 지금 해제 가능한 서버를 우선하고 상태 미확인은 마지막이어야 합니다.");
            AssertFirstDisplayedSession(historyGrid, stickyGrid, "eft-demo-1", "해제 1차 정렬");
            AssertActionButtonStates(historyGrid, stickyGrid);

            InvokeActionHeaderClick(mainForm, stickyGrid, "unblockAction", raiseHeaderClickMethod);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                "unblockAction", SortOrder.Descending, "해제 2차 클릭");
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-2", "arena-demo-1", "eft-demo-1", "eft-demo-local" },
                "해제 2차 정렬은 해제 불필요 서버를 우선하고 동률 순서도 반전해야 합니다.");
            AssertFirstDisplayedSession(historyGrid, stickyGrid, "eft-demo-2", "해제 2차 정렬");
            AssertSelectedSessionPreserved(historyGrid, stickyGrid, "eft-demo-1");
            AssertActionButtonStates(historyGrid, stickyGrid);
            AssertActiveActionHeaderUsesAccentArrow(stickyGrid, "unblockAction");

            InvokeActionHeaderClick(mainForm, stickyGrid, "unblockAction", raiseHeaderClickMethod);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                null, SortOrder.None, "해제 3차 클릭");
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-1", "arena-demo-1", "eft-demo-2", "eft-demo-local" },
                "해제 3차 클릭은 최신 접속 순서로 복귀해야 합니다.");
            AssertFirstDisplayedSession(historyGrid, stickyGrid, "eft-demo-1", "해제 기본순 복귀");

            AssertActionSortUsesCurrentRenderedState(
                mainForm,
                historyGrid,
                stickyGrid,
                sortColumnField,
                sortOrderField,
                firewallStatesField,
                measuringField,
                refreshActionCellsMethod,
                reapplySortMethod,
                raiseHeaderClickMethod);
        }

        private static void AssertActionSortUsesCurrentRenderedState(
            Form mainForm,
            DataGridView historyGrid,
            DataGridView stickyGrid,
            FieldInfo sortColumnField,
            FieldInfo sortOrderField,
            FieldInfo firewallStatesField,
            FieldInfo measuringField,
            MethodInfo refreshActionCellsMethod,
            MethodInfo reapplySortMethod,
            MethodInfo raiseHeaderClickMethod)
        {
            var states = firewallStatesField.GetValue(mainForm) as IDictionary;
            Assert(states != null && states.Count >= 3,
                "표시 버튼 상태와 캐시 갱신 시점 회귀 검사용 방화벽 상태가 부족합니다.");
            var savedStates = new ArrayList();
            foreach (DictionaryEntry entry in states) savedStates.Add(entry);

            // Simulate a cache replacement immediately before a click while the old
            // known-state buttons are still painted. Sorting and the following repaint
            // must share the current cache/busy-state calculation, so the final disabled
            // buttons and the all-unknown row order cannot disagree.
            sortColumnField.SetValue(mainForm, "blockAction");
            sortOrderField.SetValue(mainForm, SortOrder.Ascending);
            states.Clear();
            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                "blockAction",
                raiseHeaderClickMethod);
            AssertSortState(sortColumnField, sortOrderField, mainForm,
                "blockAction", SortOrder.Descending, "최종 차단 버튼 상태 정렬");
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-local", "eft-demo-2", "arena-demo-1", "eft-demo-1" },
                "캐시 교체 시점에는 최종 미확인 버튼 상태와 같은 기준으로 행을 정렬해야 합니다.");
            AssertFirstDisplayedSession(
                historyGrid,
                stickyGrid,
                "eft-demo-local",
                "최종 차단 버튼 상태 정렬");
            AssertAllActionButtonsDisabled(historyGrid, stickyGrid,
                "캐시 교체 직후 최종 버튼 상태");

            foreach (DictionaryEntry entry in savedStates)
                states.Add(entry.Key, entry.Value);
            refreshActionCellsMethod.Invoke(mainForm, null);
            sortColumnField.SetValue(mainForm, null);
            sortOrderField.SetValue(mainForm, SortOrder.None);
            reapplySortMethod.Invoke(mainForm, new object[] { false, true });
            mainForm.PerformLayout();
            Application.DoEvents();
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-1", "arena-demo-1", "eft-demo-2", "eft-demo-local" },
                "표시 상태 정렬 회귀 검증 뒤 기본 최신순 복원에 실패했습니다.");

            // A busy transition also disables the final buttons without replacing the
            // cache. Verify that sorting sees the same busy flag before the repaint.
            sortColumnField.SetValue(mainForm, "blockAction");
            sortOrderField.SetValue(mainForm, SortOrder.Ascending);
            measuringField.SetValue(mainForm, true);
            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                "blockAction",
                raiseHeaderClickMethod);
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-local", "eft-demo-2", "arena-demo-1", "eft-demo-1" },
                "조회 진행 상태에서는 최종 비활성 버튼과 같은 미확인 기준으로 정렬해야 합니다.");
            AssertAllActionButtonsDisabled(historyGrid, stickyGrid,
                "조회 진행 중 최종 버튼 상태");
            measuringField.SetValue(mainForm, false);
            refreshActionCellsMethod.Invoke(mainForm, null);
            sortColumnField.SetValue(mainForm, null);
            sortOrderField.SetValue(mainForm, SortOrder.None);
            reapplySortMethod.Invoke(mainForm, new object[] { false, true });

            object commonUnblockedState = null;
            foreach (DictionaryEntry entry in savedStates)
            {
                PropertyInfo blockedProperty = entry.Value.GetType().GetProperty("IsBlocked");
                if (blockedProperty != null
                    && !(bool)blockedProperty.GetValue(entry.Value, null))
                {
                    commonUnblockedState = entry.Value;
                    break;
                }
            }
            Assert(commonUnblockedState != null,
                "동일한 차단 가능 상태 정렬 검사용 방화벽 결과를 찾지 못했습니다.");
            foreach (DictionaryEntry entry in savedStates)
                states[entry.Key] = commonUnblockedState;
            refreshActionCellsMethod.Invoke(mainForm, null);
            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                "blockAction",
                raiseHeaderClickMethod);
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-1", "arena-demo-1", "eft-demo-2", "eft-demo-local" },
                "모든 서버가 차단 가능해도 1차 정렬은 최신 접속순과 상태 미확인 후순위를 유지해야 합니다.");
            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                "blockAction",
                raiseHeaderClickMethod);
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-2", "arena-demo-1", "eft-demo-1", "eft-demo-local" },
                "모든 서버의 작업 상태가 같아도 2차 정렬은 접속시각 동률 순서를 실제로 반전해야 합니다.");
            AssertFirstDisplayedSession(
                historyGrid,
                stickyGrid,
                "eft-demo-2",
                "동일한 차단 가능 상태의 2차 정렬");

            states.Clear();
            refreshActionCellsMethod.Invoke(mainForm, null);
            sortColumnField.SetValue(mainForm, null);
            sortOrderField.SetValue(mainForm, SortOrder.None);
            reapplySortMethod.Invoke(mainForm, new object[] { false, true });
            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                "blockAction",
                raiseHeaderClickMethod);
            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                "blockAction",
                raiseHeaderClickMethod);
            AssertSessionOrder(historyGrid,
                new[] { "eft-demo-local", "eft-demo-2", "arena-demo-1", "eft-demo-1" },
                "모든 상태가 미확인이어도 2차 정렬은 접속시각 순서를 실제로 반전해야 합니다.");
            AssertFirstDisplayedSession(
                historyGrid,
                stickyGrid,
                "eft-demo-local",
                "전체 상태 미확인의 2차 정렬");

            foreach (DictionaryEntry entry in savedStates)
                states.Add(entry.Key, entry.Value);
            refreshActionCellsMethod.Invoke(mainForm, null);
            sortColumnField.SetValue(mainForm, null);
            sortOrderField.SetValue(mainForm, SortOrder.None);
            reapplySortMethod.Invoke(mainForm, new object[] { false, true });
            mainForm.PerformLayout();
            Application.DoEvents();
        }

        private static void AssertAllActionButtonsDisabled(
            DataGridView historyGrid,
            DataGridView stickyGrid,
            string step)
        {
            Assert(historyGrid.Rows.Count == stickyGrid.Rows.Count,
                step + "에서 메인 표와 고정 작업 영역의 행 수가 달라졌습니다.");
            for (int index = 0; index < historyGrid.Rows.Count; index++)
            {
                DataGridViewRow historyRow = historyGrid.Rows[index];
                DataGridViewRow stickyRow = stickyGrid.Rows[index];
                bool historyBlockEnabled = historyRow.Cells["blockAction"].Tag is bool
                    && (bool)historyRow.Cells["blockAction"].Tag;
                bool historyUnblockEnabled = historyRow.Cells["unblockAction"].Tag is bool
                    && (bool)historyRow.Cells["unblockAction"].Tag;
                bool stickyBlockEnabled = stickyRow.Cells["blockAction"].Tag is bool
                    && (bool)stickyRow.Cells["blockAction"].Tag;
                bool stickyUnblockEnabled = stickyRow.Cells["unblockAction"].Tag is bool
                    && (bool)stickyRow.Cells["unblockAction"].Tag;
                Assert(!historyBlockEnabled
                        && !historyUnblockEnabled
                        && !stickyBlockEnabled
                        && !stickyUnblockEnabled,
                    step + "에서 상태 미확인 행의 차단·해제 버튼이 활성화되면 안 됩니다.");
                Assert(GetSessionKey(stickyRow.Tag) == GetSessionKey(historyRow.Tag),
                    step + "에서 고정 작업 행이 메인 표와 다른 기록을 가리킵니다.");
            }
        }

        private static void InvokeActionHeaderClick(
            Form mainForm,
            DataGridView stickyGrid,
            string columnName,
            MethodInfo raiseHeaderClickMethod)
        {
            InvokeActionHeaderClick(
                mainForm,
                stickyGrid,
                columnName,
                raiseHeaderClickMethod,
                MouseButtons.Left);
        }

        private static void InvokeActionHeaderClick(
            Form mainForm,
            DataGridView stickyGrid,
            string columnName,
            MethodInfo raiseHeaderClickMethod,
            MouseButtons button)
        {
            DataGridViewColumn column = stickyGrid.Columns[columnName];
            var mouse = new MouseEventArgs(button, 1, 2, 2, 0);
            var args = new DataGridViewCellMouseEventArgs(
                column.Index,
                -1,
                2,
                2,
                mouse);
            raiseHeaderClickMethod.Invoke(stickyGrid, new object[] { args });
            mainForm.PerformLayout();
            Application.DoEvents();
        }

        private static void AssertSortState(
            FieldInfo columnField,
            FieldInfo orderField,
            Form mainForm,
            string expectedColumn,
            SortOrder expectedOrder,
            string step)
        {
            object actualColumn = columnField.GetValue(mainForm);
            Assert(expectedColumn == null
                    ? actualColumn == null
                    : string.Equals(
                        Convert.ToString(actualColumn),
                        expectedColumn,
                        StringComparison.Ordinal),
                step + " 후 정렬 열 상태가 올바르지 않습니다.");
            Assert((SortOrder)orderField.GetValue(mainForm) == expectedOrder,
                step + " 후 정렬 방향이 올바르지 않습니다.");
        }

        private static void AssertSessionOrder(DataGridView grid, string[] expected, string message)
        {
            Assert(grid.Rows.Count == expected.Length, message + " (행 수 불일치)");
            for (int index = 0; index < expected.Length; index++)
            {
                Assert(GetSessionKey(grid.Rows[index].Tag) == expected[index],
                    message + " (행 " + index + ")");
            }
        }

        private static void AssertFirstDisplayedSession(
            DataGridView historyGrid,
            DataGridView stickyGrid,
            string expectedSessionKey,
            string step)
        {
            int historyIndex = historyGrid.FirstDisplayedScrollingRowIndex;
            int stickyIndex = stickyGrid.FirstDisplayedScrollingRowIndex;
            Assert(historyIndex >= 0
                    && GetSessionKey(historyGrid.Rows[historyIndex].Tag) == expectedSessionKey,
                step + " 후 메인 표의 실제 첫 표시 행이 정렬 결과의 선두여야 합니다.");
            Assert(stickyIndex >= 0
                    && GetSessionKey(stickyGrid.Rows[stickyIndex].Tag) == expectedSessionKey,
                step + " 후 고정 차단·해제 영역도 같은 정렬 선두를 표시해야 합니다.");
        }

        private static object FindSessionByKey(DataGridView grid, string sessionKey)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (GetSessionKey(row.Tag) == sessionKey) return row.Tag;
            }
            return null;
        }

        private static string GetSessionKey(object session)
        {
            if (session == null) return string.Empty;
            PropertyInfo property = session.GetType().GetProperty("SessionKey");
            return property == null ? string.Empty : Convert.ToString(property.GetValue(session, null));
        }

        private static string GetSessionIp(object session)
        {
            if (session == null) return string.Empty;
            PropertyInfo property = session.GetType().GetProperty("IpAddress");
            return property == null ? string.Empty : Convert.ToString(property.GetValue(session, null));
        }

        private static void SelectHistorySession(DataGridView grid, object session)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!ReferenceEquals(row.Tag, session)) continue;
                grid.ClearSelection();
                grid.CurrentCell = row.Cells["game"];
                row.Selected = true;
                return;
            }
        }

        private static void AssertSelectedSessionPreserved(
            DataGridView historyGrid,
            DataGridView stickyGrid,
            string expectedSessionKey)
        {
            Assert(historyGrid.SelectedRows.Count == 1
                && GetSessionKey(historyGrid.SelectedRows[0].Tag) == expectedSessionKey,
                "작업 열 정렬 후에도 메인 목록의 선택 기록을 유지해야 합니다.");
            Assert(stickyGrid.SelectedRows.Count == 1
                && GetSessionKey(stickyGrid.SelectedRows[0].Tag) == expectedSessionKey,
                "작업 열 정렬 후에도 고정 버튼 영역의 선택 기록을 유지해야 합니다.");
        }

        private static void AssertActionButtonStates(
            DataGridView historyGrid,
            DataGridView stickyGrid)
        {
            Assert(historyGrid.Rows.Count == stickyGrid.Rows.Count,
                "정렬 후 메인 목록과 고정 작업 영역의 행 수가 달라졌습니다.");
            for (int index = 0; index < historyGrid.Rows.Count; index++)
            {
                DataGridViewRow historyRow = historyGrid.Rows[index];
                DataGridViewRow stickyRow = stickyGrid.Rows[index];
                string sessionKey = GetSessionKey(historyRow.Tag);
                string ipAddress = GetSessionIp(historyRow.Tag);
                bool blockEnabled = historyRow.Cells["blockAction"].Tag is bool
                    && (bool)historyRow.Cells["blockAction"].Tag;
                bool unblockEnabled = historyRow.Cells["unblockAction"].Tag is bool
                    && (bool)historyRow.Cells["unblockAction"].Tag;
                bool stickyBlockEnabled = stickyRow.Cells["blockAction"].Tag is bool
                    && (bool)stickyRow.Cells["blockAction"].Tag;
                bool stickyUnblockEnabled = stickyRow.Cells["unblockAction"].Tag is bool
                    && (bool)stickyRow.Cells["unblockAction"].Tag;
                Assert(GetSessionKey(stickyRow.Tag) == sessionKey,
                    "정렬 후 고정 작업 행이 메인 목록과 같은 기록을 가리켜야 합니다.");
                Assert(blockEnabled == stickyBlockEnabled
                    && unblockEnabled == stickyUnblockEnabled,
                    "정렬 후 차단·해제 버튼 활성 상태가 고정 작업 영역에 그대로 복사돼야 합니다.");
                if (string.IsNullOrWhiteSpace(ipAddress))
                {
                    Assert(!blockEnabled && !unblockEnabled,
                        "서버 IP가 없는 기록의 차단·해제 버튼은 비활성 상태여야 합니다.");
                }
                else
                {
                    Assert(blockEnabled != unblockEnabled,
                        "방화벽 상태가 확인된 서버는 차단·해제 중 정확히 한 작업만 가능해야 합니다.");
                }
            }
        }

        private static void AssertActiveActionHeaderUsesAccentArrow(
            DataGridView stickyGrid,
            string columnName)
        {
            Rectangle headerBounds = stickyGrid.GetCellDisplayRectangle(
                stickyGrid.Columns[columnName].Index,
                -1,
                true);
            using (var bitmap = new Bitmap(
                Math.Max(1, stickyGrid.ClientSize.Width),
                Math.Max(1, stickyGrid.ClientSize.Height)))
            {
                stickyGrid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Color expectedAccent = Color.FromArgb(232, 157, 54);
                bool found = false;
                int left = Math.Max(0, headerBounds.Left);
                int top = Math.Max(0, headerBounds.Top);
                int right = Math.Min(bitmap.Width, headerBounds.Right);
                int bottom = Math.Min(bitmap.Height, headerBounds.Bottom);
                for (int y = top; y < bottom && !found; y++)
                {
                    for (int x = left; x < right; x++)
                    {
                        if (bitmap.GetPixel(x, y).ToArgb() == expectedAccent.ToArgb())
                        {
                            found = true;
                            break;
                        }
                    }
                }
                Assert(found,
                    "활성 차단·해제 정렬 헤더는 메인 화면과 같은 주황색 화살표를 표시해야 합니다.");
            }
        }

        private static void AssertFirewallPersistenceUi(
            Assembly application,
            Type mainFormType,
            Form mainForm)
        {
            Type noticeType = application.GetType(
                "TarkovServerReporter.FirewallPersistenceNotice",
                true);
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
            string processExitLine = GetStaticString(noticeType, "ProcessExitLine", staticFlags);
            string rulesPersistLine = GetStaticString(noticeType, "RulesPersistLine", staticFlags);
            string fullText = GetStaticString(noticeType, "FullText", staticFlags);

            const string expectedProcessExitLine =
                "창을 닫으면 TSG는 완전히 종료됩니다. 백그라운드에 상주하지 않아 "
                + "TSG 프로세스가 CPU·메모리·네트워크를 추가로 사용하지 않습니다.";
            const string expectedRulesPersistLine =
                "차단 리스트는 Windows 방화벽에 저장되므로 해당 앱 종료·PC 재부팅 후에도 유지되며, "
                + "앱에서 해제할 때까지 계속 적용됩니다.";
            Assert(processExitLine == expectedProcessExitLine,
                "TSG 종료와 추가 리소스 사용에 관한 안내 문구가 확정안과 달라졌습니다.");
            Assert(rulesPersistLine == expectedRulesPersistLine,
                "Windows 방화벽 차단 리스트의 유지 범위 안내가 확정안과 달라졌습니다.");
            Assert(fullText == processExitLine + "\r\n" + rulesPersistLine,
                "서버차단현황의 두 안내 문장이 서로 누락 없이 표시되어야 합니다.");

            Type blockedServersType = application.GetType(
                "TarkovServerReporter.BlockedServersForm",
                true);
            using (var blockedServersForm = (Form)Activator.CreateInstance(blockedServersType))
            {
                blockedServersForm.Size = blockedServersForm.MinimumSize;
                blockedServersForm.CreateControl();
                blockedServersForm.PerformLayout();
                Control notice = FindControlByName(
                    blockedServersForm,
                    "firewallPersistenceNotice");
                Assert(notice is Label, "서버차단현황 상단의 차단 유지 안내를 찾지 못했습니다.");
                Assert(notice.Text == fullText,
                    "서버차단현황 상단 안내가 공통 문구와 달라졌습니다.");
                Assert(notice.Dock == DockStyle.Bottom && notice.Height >= 34,
                    "서버차단현황 상단 안내 두 줄이 잘리지 않도록 공간을 확보해야 합니다.");
                Assert(notice.AccessibleDescription == fullText,
                    "보조기술에도 차단 유지 안내 전문을 제공해야 합니다.");
                Assert(notice.AccessibleName == "앱 종료와 차단 리스트 유지 안내",
                    "보조기술용 안내 이름도 차단 리스트 표현을 사용해야 합니다.");
                string[] noticeLines = fullText.Split(
                    new[] { "\r\n" },
                    StringSplitOptions.None);
                foreach (string line in noticeLines)
                {
                    Size measured = TextRenderer.MeasureText(
                        line,
                        notice.Font,
                        Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    Assert(measured.Width <= notice.ClientSize.Width,
                        "최소 창 너비에서도 차단 유지 안내 한 줄이 추가 줄바꿈되면 안 됩니다.");
                }
                AssertBlockedServerBackupUi(application, blockedServersType, blockedServersForm);
            }

            MethodInfo successMessageMethod = mainFormType.GetMethod(
                "GetFirewallChangeSuccessMessage",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(successMessageMethod != null, "차단 성공 상태 문구 생성기를 찾지 못했습니다.");
            string successMessage = Convert.ToString(successMessageMethod.Invoke(
                null,
                new object[] { "203.0.113.42", true }));
            string expectedSuccessMessage =
                "203.0.113.42 서버 차단을 적용했습니다. 핑은 다시 조회해 주세요.\r\n"
                + rulesPersistLine;
            Assert(successMessage == expectedSuccessMessage,
                "일반 차단 성공 상태의 두 줄 문구가 확정안과 정확히 일치해야 합니다.");

            MethodInfo setStatusMethod = mainFormType.GetMethod(
                "SetStatus",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo statusField = mainFormType.GetField(
                "_statusLabel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo toolTipField = mainFormType.GetField(
                "_toolTip",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(setStatusMethod != null && statusField != null && toolTipField != null,
                "상태줄 전체 문구 표시 경로를 찾지 못했습니다.");
            mainForm.Size = mainForm.MinimumSize;
            mainForm.PerformLayout();
            Application.DoEvents();
            setStatusMethod.Invoke(mainForm, new object[] { successMessage, Color.Red });
            mainForm.PerformLayout();
            Application.DoEvents();
            var statusLabel = statusField.GetValue(mainForm) as Label;
            var toolTip = toolTipField.GetValue(mainForm) as ToolTip;
            Assert(statusLabel != null && toolTip != null,
                "메인 화면 상태 안내와 툴팁을 찾지 못했습니다.");
            Assert(statusLabel.Text == successMessage,
                "화면의 차단 성공 상태가 확정된 두 줄 전문과 정확히 같아야 합니다.");
            Assert(toolTip.GetToolTip(statusLabel) == successMessage,
                "긴 차단 성공 상태가 잘려도 툴팁으로 전문을 확인할 수 있어야 합니다.");
            Assert(statusLabel.AccessibleDescription == successMessage,
                "보조기술에도 차단 성공 상태 전문을 제공해야 합니다.");
            Assert(statusLabel.AccessibleName == "작업 상태 안내",
                "보조기술이 메인 화면 상태 안내의 용도를 식별할 수 있어야 합니다.");
            Assert(statusLabel.Text.Split(
                    new[] { "\r\n" },
                    StringSplitOptions.None).Length == 2,
                "차단 성공 상태는 확정된 두 줄 구조를 유지해야 합니다.");
            Assert(!statusLabel.AutoEllipsis,
                "차단 성공 안내 전문을 말줄임표로 숨기면 안 됩니다.");
            AssertStatusTextFits(statusLabel, successMessage,
                "최소 지원 창 크기에서도 차단 성공 안내 두 줄이 잘리면 안 됩니다.");

            Font originalFont = statusLabel.Font;
            using (var scaledFont = new Font(
                originalFont.FontFamily,
                originalFont.Size * 1.5F,
                originalFont.Style,
                originalFont.Unit))
            {
                statusLabel.Font = scaledFont;
                setStatusMethod.Invoke(mainForm, new object[] { successMessage, Color.Red });
                mainForm.PerformLayout();
                Application.DoEvents();
                AssertStatusTextFits(statusLabel, successMessage,
                    "상태 글꼴이 확대돼도 차단 성공 안내 전문이 잘리면 안 됩니다.");
                statusLabel.Font = originalFont;
            }

            MethodInfo calculateHeightMethod = mainFormType.GetMethod(
                "CalculateStatusAreaHeight",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(calculateHeightMethod != null,
                "DPI별 상태 영역 높이 계산 경계를 찾지 못했습니다.");
            int logicalWidth = Math.Max(
                1,
                statusLabel.ClientSize.Width - statusLabel.Padding.Horizontal);
            AssertCalculatedStatusHeight(
                calculateHeightMethod,
                successMessage,
                originalFont,
                logicalWidth,
                144,
                1.5F);
            AssertCalculatedStatusHeight(
                calculateHeightMethod,
                successMessage,
                originalFont,
                logicalWidth,
                192,
                2F);
        }

        private static void AssertCalculatedStatusHeight(
            MethodInfo calculateHeightMethod,
            string message,
            Font baseFont,
            int logicalWidth,
            int dpi,
            float scale)
        {
            using (var scaledFont = new Font(
                baseFont.FontFamily,
                baseFont.Size * scale,
                baseFont.Style,
                baseFont.Unit))
            {
                int scaledWidth = Math.Max(1, (int)Math.Round(logicalWidth * scale));
                int actualHeight = Convert.ToInt32(calculateHeightMethod.Invoke(
                    null,
                    new object[] { message, scaledFont, scaledWidth, dpi }));
                Size textSize = TextRenderer.MeasureText(
                    message,
                    scaledFont,
                    new Size(scaledWidth, int.MaxValue),
                    TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.WordBreak);
                int scaledMinimum = Math.Max(1, (int)Math.Round(28F * dpi / 96F));
                int scaledPadding = Math.Max(1, (int)Math.Round(6F * dpi / 96F));
                int expectedHeight = Math.Max(scaledMinimum, textSize.Height + scaledPadding);
                Assert(actualHeight == expectedHeight,
                    dpi + " DPI에서도 상태 영역이 측정 글꼴과 두 줄 문구 높이를 정확히 확보해야 합니다.");
            }
        }

        private static void AssertStatusTextFits(
            Label statusLabel,
            string message,
            string errorMessage)
        {
            int width = Math.Max(1, statusLabel.ClientSize.Width - statusLabel.Padding.Horizontal);
            Size required = TextRenderer.MeasureText(
                message,
                statusLabel.Font,
                new Size(width, int.MaxValue),
                TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.WordBreak);
            Assert(statusLabel.ClientSize.Height >= required.Height, errorMessage);
        }

        private static void AssertMissingLogTooltips(
            Assembly application,
            Type mainFormType)
        {
            const string expected =
                "레이드 진행 중 또는 게임의 버그 · 비정상 종료로 필요한 로그가 기록되지 않았을 수 있습니다.";
            const BindingFlags publicStatic = BindingFlags.Static | BindingFlags.Public;
            Type presentationType = application.GetType(
                "TarkovServerReporter.RaidMetricPresentation",
                true);
            string sharedHelp = GetStaticString(
                presentationType,
                "MissingLogHelp",
                publicStatic);
            Assert(sharedHelp == expected,
                "로그 없음 상태의 공통 도움말은 확정 문구와 정확히 일치해야 합니다.");

            Type sessionType = application.GetType(
                "TarkovServerReporter.ServerSession",
                true);
            object session = Activator.CreateInstance(sessionType);
            PropertyInfo attemptsProperty = sessionType.GetProperty("ConnectionAttempts");
            Assert(attemptsProperty != null && attemptsProperty.CanWrite,
                "로그 없음 상태 회귀 검사용 연결 시도 속성을 찾지 못했습니다.");
            attemptsProperty.SetValue(session, 1, null);

            MethodInfo connectionHelpMethod = mainFormType.GetMethod(
                "GetConnectionResultHelp",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo actualRttHelpMethod = presentationType.GetMethod(
                "GetActualRttHelp",
                publicStatic);
            MethodInfo packetLossHelpMethod = presentationType.GetMethod(
                "GetPacketLossHelp",
                publicStatic);
            Assert(connectionHelpMethod != null
                && actualRttHelpMethod != null
                && packetLossHelpMethod != null,
                "로그 없음 툴팁 생성 경로를 모두 찾지 못했습니다.");
            Assert(Convert.ToString(connectionHelpMethod.Invoke(null, new[] { session })) == expected,
                "서버연결 결과의 로그 없음 툴팁이 공통 확정 문구와 달라졌습니다.");
            Assert(Convert.ToString(actualRttHelpMethod.Invoke(null, new[] { session })) == expected,
                "실게임 RTT의 로그 없음 툴팁이 공통 확정 문구와 달라졌습니다.");
            Assert(Convert.ToString(packetLossHelpMethod.Invoke(null, new[] { session })) == expected,
                "패킷손실의 로그 없음 툴팁이 공통 확정 문구와 달라졌습니다.");
        }

        private static void AssertPrivacyNoticeText(Form mainForm)
        {
            const string expected =
                "사용자의 게임 로그·계정정보·SID·로컬경로는 전송하지 않습니다.\r\n"
                + "차단·해제 시에만 Windows 관리자권한을 요청합니다.\r\n"
                + "게임서버 IP 지역은 외부 API 대신 PC의 DB-IP Lite 데이터로 조회합니다.\r\n"
                + "조회 시 새 월간 지역 DB가 있으면 자동으로 업데이트합니다. (약 60~70MB 교체)\r\n"
                + "새 버전 확인을 위해 GitHub Releases에 접속합니다.\r\n"
                + "DB-IP.com . CC BY 4.0";
            Label privacyNotice = null;
            foreach (Label label in FindControls<Label>(mainForm))
            {
                if (label.Text != null
                    && label.Text.StartsWith(
                        "사용자의 게임 로그·계정정보·SID·로컬경로는 전송하지 않습니다.",
                        StringComparison.Ordinal))
                {
                    privacyNotice = label;
                    break;
                }
            }

            Assert(privacyNotice != null,
                "메인 화면의 개인정보·지역 DB 안내를 찾지 못했습니다.");
            Assert(privacyNotice.Text == expected,
                "메인 화면의 지역 DB 용량 안내 문구와 띄어쓰기가 확정안과 달라졌습니다.");
        }

        private static void AssertBlockedServerBackupUi(
            Assembly application,
            Type blockedServersType,
            Form blockedServersForm)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            FieldInfo exportField = blockedServersType.GetField("_exportButton", instanceFlags);
            FieldInfo importField = blockedServersType.GetField("_importButton", instanceFlags);
            var exportButton = exportField == null ? null : exportField.GetValue(blockedServersForm) as Button;
            var importButton = importField == null ? null : importField.GetValue(blockedServersForm) as Button;
            Assert(exportButton != null && exportButton.Text == "내보내기",
                "서버차단현황에서 개인 차단 목록 내보내기를 직접 실행할 수 있어야 합니다.");
            Assert(importButton != null && importButton.Text == "불러오기",
                "서버차단현황에서 개인 차단 목록 불러오기를 직접 실행할 수 있어야 합니다.");

            Type restoreItemType = application.GetType(
                "TarkovServerReporter.BlockedServerRestoreItem",
                true);
            Type previewType = application.GetType(
                "TarkovServerReporter.BlockedServerRestorePreviewForm",
                true);
            Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(restoreItemType);
            object emptyItems = Activator.CreateInstance(listType);
            using (var preview = (Form)Activator.CreateInstance(
                previewType,
                instanceFlags,
                null,
                new[] { emptyItems },
                null))
            {
                FieldInfo gridField = previewType.GetField("_grid", instanceFlags);
                FieldInfo applyField = previewType.GetField("_applyButton", instanceFlags);
                var grid = gridField == null ? null : gridField.GetValue(preview) as DataGridView;
                var applyButton = applyField == null ? null : applyField.GetValue(preview) as Button;
                Assert(grid != null
                    && grid.Columns.Contains("status")
                    && grid.Columns.Contains("ip")
                    && grid.Columns.Contains("metadata")
                    && grid.Columns.Contains("detail"),
                    "복원 전에 새 차단·기존 차단·제외 및 메타데이터 여부를 표로 확인할 수 있어야 합니다.");
                Assert(applyButton != null
                    && applyButton.Text == "선택 항목 복원"
                    && !applyButton.Enabled,
                    "복원 미리보기는 사용자가 적용 항목을 확인한 뒤에만 진행되어야 합니다.");
            }
        }

        private static void AssertInitialFirewallStatePresentation(
            Type mainFormType,
            Form mainForm)
        {
            const BindingFlags instanceFlags = BindingFlags.Instance
                | BindingFlags.NonPublic;
            FieldInfo coordinatorField = mainFormType.GetField(
                "_initialFirewallStateRefresh",
                instanceFlags);
            MethodInfo startMethod = mainFormType.GetMethod(
                "StartInitialFirewallStateRefresh",
                instanceFlags);
            Assert(coordinatorField != null
                && coordinatorField.GetValue(mainForm) != null
                && startMethod != null,
                "로그 로드 직후 수동 조회와 분리된 방화벽 상태 조회 경계가 필요합니다.");

            FieldInfo sessionsField = mainFormType.GetField("_allSessions", instanceFlags);
            FieldInfo pingField = mainFormType.GetField("_pingResults", instanceFlags);
            FieldInfo firewallField = mainFormType.GetField("_firewallStates", instanceFlags);
            var sessions = sessionsField == null
                ? null
                : sessionsField.GetValue(mainForm) as IEnumerable;
            var pings = pingField == null
                ? null
                : pingField.GetValue(mainForm) as IDictionary;
            var states = firewallField == null
                ? null
                : firewallField.GetValue(mainForm) as IDictionary;
            object session = null;
            if (sessions != null)
            {
                foreach (object item in sessions)
                {
                    session = item;
                    break;
                }
            }
            Assert(session != null && pings != null && states != null,
                "차단 중 표시 회귀 검사용 미리보기 데이터를 찾지 못했습니다.");

            string ipAddress = Convert.ToString(session.GetType()
                .GetProperty("IpAddress")
                .GetValue(session, null));
            object ping = pings[ipAddress];
            object firewall = states[ipAddress];
            MethodInfo pingTextMethod = mainFormType.GetMethod(
                "GetPingCellText",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(ping != null && firewall != null && pingTextMethod != null,
                "현재 핑 열의 상태 표시 경로를 찾지 못했습니다.");
            string text = Convert.ToString(pingTextMethod.Invoke(
                null,
                new[] { session, ping, firewall }));
            Assert(text == "차단 중",
                "핑 캐시가 있어도 확인된 방화벽 차단 상태를 현재 핑 열에서 우선 표시해야 합니다.");
        }

        private static string GetStaticString(Type type, string fieldName, BindingFlags flags)
        {
            FieldInfo field = type.GetField(fieldName, flags);
            Assert(field != null && field.FieldType == typeof(string),
                "안내 문구 필드를 찾지 못했습니다: " + fieldName);
            return Convert.ToString(field.GetValue(null));
        }

        private static Control FindControlByName(Control root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.Name, name, StringComparison.Ordinal)) return root;
            foreach (Control child in root.Controls)
            {
                Control found = FindControlByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static IEnumerable<TControl> FindControls<TControl>(Control root)
            where TControl : Control
        {
            if (root == null) yield break;
            foreach (Control child in root.Controls)
            {
                var typed = child as TControl;
                if (typed != null) yield return typed;
                foreach (TControl nested in FindControls<TControl>(child))
                    yield return nested;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
