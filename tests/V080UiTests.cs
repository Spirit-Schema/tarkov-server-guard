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

                    using (var bitmap = new Bitmap(
                        Math.Max(1, grid.ClientSize.Width),
                        Math.Max(1, Math.Min(grid.ClientSize.Height, 240))))
                    {
                        grid.DrawToBitmap(
                            bitmap,
                            new Rectangle(Point.Empty, bitmap.Size));
                    }

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
                .SetValue(entry, "0.8.0", null);
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
            Assert(successMessage.Contains(rulesPersistLine),
                "일반 차단 성공 상태에서 방화벽 차단 리스트 유지 안내가 빠졌습니다.");

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
            setStatusMethod.Invoke(mainForm, new object[] { successMessage, Color.Red });
            var statusLabel = statusField.GetValue(mainForm) as Label;
            var toolTip = toolTipField.GetValue(mainForm) as ToolTip;
            Assert(statusLabel != null && toolTip != null
                && toolTip.GetToolTip(statusLabel) == successMessage,
                "긴 차단 성공 상태가 잘려도 툴팁으로 전문을 확인할 수 있어야 합니다.");
            Assert(statusLabel.AccessibleDescription == successMessage,
                "보조기술에도 차단 성공 상태 전문을 제공해야 합니다.");
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
