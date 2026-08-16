// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal sealed class UninstallFirewallCleanupResult
    {
        internal bool Success { get; set; }
        internal bool Cancelled { get; set; }
        internal int RemovedCount { get; set; }
        internal int FailedCount { get; set; }
        internal string ErrorMessage { get; set; }
    }

    internal interface IUninstallFirewallService
    {
        Task<UninstallFirewallCleanupResult> RemoveAllManagedRulesAsync();
    }

    internal interface IUninstallFirewallRuleGateway
    {
        ManagedBlockedServerQueryResult QueryAllOwnedManagedRules();
        Task<FirewallBatchChangeResult> RemoveManyWithElevationAsync(
            IEnumerable<string> ipAddresses);
    }

    internal sealed class SystemUninstallFirewallRuleGateway
        : IUninstallFirewallRuleGateway
    {
        public ManagedBlockedServerQueryResult QueryAllOwnedManagedRules()
        {
            return FirewallRuleManager.QueryAllOwnedManagedRules();
        }

        public Task<FirewallBatchChangeResult> RemoveManyWithElevationAsync(
            IEnumerable<string> ipAddresses)
        {
            return FirewallRuleManager.RemoveManyWithElevationAsync(ipAddresses);
        }
    }

    internal sealed class SystemUninstallFirewallService : IUninstallFirewallService
    {
        private readonly IUninstallFirewallRuleGateway _gateway;

        internal SystemUninstallFirewallService()
            : this(new SystemUninstallFirewallRuleGateway())
        {
        }

        internal SystemUninstallFirewallService(IUninstallFirewallRuleGateway gateway)
        {
            if (gateway == null) throw new ArgumentNullException("gateway");
            _gateway = gateway;
        }

        public async Task<UninstallFirewallCleanupResult> RemoveAllManagedRulesAsync()
        {
            ManagedBlockedServerQueryResult query = await Task.Run(
                () => _gateway.QueryAllOwnedManagedRules()).ConfigureAwait(false);
            if (query == null || !query.Success)
            {
                return new UninstallFirewallCleanupResult
                {
                    ErrorMessage = query == null || string.IsNullOrWhiteSpace(query.ErrorMessage)
                        ? "앱이 관리하는 방화벽 규칙을 확인하지 못했습니다."
                        : query.ErrorMessage
                };
            }

            string[] addresses = (query.Servers ?? new List<ManagedBlockedServer>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.IpAddress))
                .Select(item => item.IpAddress)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (addresses.Length == 0)
                return new UninstallFirewallCleanupResult { Success = true };

            FirewallBatchChangeResult batch = await _gateway
                .RemoveManyWithElevationAsync(addresses).ConfigureAwait(false);
            if (batch == null)
            {
                return new UninstallFirewallCleanupResult
                {
                    ErrorMessage = "방화벽 규칙 제거 결과를 확인하지 못했습니다."
                };
            }

            IList<FirewallBatchItemResult> batchItems = batch.Items
                ?? new List<FirewallBatchItemResult>();
            int removed = batchItems.Count(item => item != null && item.Success);
            int failed = batchItems.Count(item => item == null || !item.Success);
            if (batch.Cancelled)
            {
                return new UninstallFirewallCleanupResult
                {
                    Cancelled = true,
                    RemovedCount = removed,
                    FailedCount = failed,
                    ErrorMessage = batch.ErrorMessage
                };
            }

            ManagedBlockedServerQueryResult finalQuery = await Task.Run(
                () => _gateway.QueryAllOwnedManagedRules()).ConfigureAwait(false);
            if (finalQuery == null || !finalQuery.Success)
            {
                return new UninstallFirewallCleanupResult
                {
                    RemovedCount = removed,
                    FailedCount = Math.Max(1, failed),
                    ErrorMessage = finalQuery == null
                        || string.IsNullOrWhiteSpace(finalQuery.ErrorMessage)
                        ? "방화벽 규칙 제거 뒤 최종 상태를 확인하지 못했습니다."
                        : finalQuery.ErrorMessage
                };
            }

            int remaining = finalQuery.Servers == null
                ? 0
                : finalQuery.Servers
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.IpAddress))
                    .Select(item => item.IpAddress)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            if (remaining > 0)
            {
                return new UninstallFirewallCleanupResult
                {
                    RemovedCount = removed,
                    FailedCount = Math.Max(failed, remaining),
                    ErrorMessage = "비활성 상태를 포함한 앱 관리 방화벽 규칙 "
                        + remaining + "개가 남아 있어 제거를 완료하지 못했습니다."
                };
            }

            return new UninstallFirewallCleanupResult
            {
                Success = batch.Success && failed == 0,
                RemovedCount = removed,
                FailedCount = failed,
                ErrorMessage = batch.ErrorMessage
            };
        }
    }

    internal sealed class UninstallOptionsForm : BrandedForm
    {
        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color AccentHover = Color.FromArgb(246, 173, 70);
        private static readonly Color Danger = Color.FromArgb(235, 104, 94);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color DarkButtonText = Color.FromArgb(29, 24, 17);

        private readonly Func<bool, UninstallLaunchPlan> _planFactory;
        private readonly IUninstallProcessLauncher _processLauncher;
        private readonly IUninstallFirewallService _firewallService;
        private readonly UninstallLaunchPlan _initialPlan;
        private readonly CheckBox _deleteDataCheckBox;
        private readonly CheckBox _removeFirewallCheckBox;
        private readonly Label _statusLabel;
        private readonly Button _startButton;
        private readonly Button _cancelButton;
        private bool _busy;

        internal bool UninstallStarted { get; private set; }

        internal UninstallOptionsForm(
            Func<bool, UninstallLaunchPlan> planFactory,
            IUninstallProcessLauncher processLauncher,
            IUninstallFirewallService firewallService)
        {
            if (planFactory == null) throw new ArgumentNullException("planFactory");
            _planFactory = planFactory;
            if (processLauncher == null) throw new ArgumentNullException("processLauncher");
            if (firewallService == null) throw new ArgumentNullException("firewallService");
            _processLauncher = processLauncher;
            _firewallService = firewallService;
            _initialPlan = _planFactory(false) ?? new UninstallLaunchPlan
            {
                InstallKind = ApplicationInstallKind.Developer,
                UnavailableReason = "개발·테스트 빌드에서는 실제 설치 제거를 시작하지 않습니다."
            };

            Text = "Tarkov Server Guard 설치 제거";
            AccessibleName = "설치 제거 옵션";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 500);
            MinimumSize = new Size(600, 500);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(24, 18, 24, 18),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "설치 제거 옵션",
                Font = new Font("Malgun Gothic", 16F, FontStyle.Bold),
                ForeColor = Accent,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);

            var explanation = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                BackColor = Surface,
                ForeColor = TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(14, 9, 14, 9),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = _initialPlan.CanStart
                    ? "앱 파일·바로가기·업데이트 구성요소·Windows 설치 등록은 "
                        + "Velopack의 등록된 Update.exe 제거 절차가 정리합니다.\r\n"
                        + "아래 사용자 데이터와 방화벽 규칙은 기본적으로 보존됩니다."
                    : (_initialPlan.UnavailableReason
                        + "\r\n이 화면에서는 실제 제거 없이 선택 항목과 안내 문구를 검수할 수 있습니다."),
                Margin = new Padding(0, 2, 0, 10),
                AccessibleName = "설치 제거 방식 안내"
            };
            root.Controls.Add(explanation, 0, 1);

            _deleteDataCheckBox = CreateOption(
                "로컬 사용자 데이터도 모두 삭제",
                "접속 기록 캐시, 설정, 메모, 차단 메타데이터와 지역 DB 등 앱 소유 데이터가 삭제됩니다.",
                0);
            root.Controls.Add(_deleteDataCheckBox, 0, 2);

            _removeFirewallCheckBox = CreateOption(
                "앱이 관리하는 방화벽 차단 규칙도 모두 제거",
                "Tarkov Server Guard 이름과 구조가 일치하는 소유 규칙만 관리자 권한으로 제거합니다.",
                1);
            root.Controls.Add(_removeFirewallCheckBox, 0, 3);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = _initialPlan.CanStart
                    ? "보존이 기본값입니다. 삭제할 항목만 직접 선택해 주세요."
                    : "검수 모드 · 실제 설치 제거 실행은 비활성화되어 있습니다.",
                ForeColor = _initialPlan.CanStart ? TextMuted : Danger,
                BackColor = Background,
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 12, 0, 0),
                AccessibleName = "설치 제거 상태"
            };
            root.Controls.Add(_statusLabel, 0, 4);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Background,
                Padding = new Padding(0, 10, 0, 0),
                Margin = new Padding(0)
            };
            root.Controls.Add(buttons, 0, 5);

            _cancelButton = CreateButton("닫기", SurfaceAlt, TextPrimary, 104);
            _cancelButton.DialogResult = DialogResult.Cancel;
            _startButton = CreateButton("설치 제거", Accent, DarkButtonText, 120);
            _startButton.Enabled = _initialPlan.CanStart;
            _startButton.FlatAppearance.MouseOverBackColor = AccentHover;
            _startButton.Click += async delegate
            {
                DialogResult confirmation = MessageBox.Show(
                    this,
                    BuildConfirmationText(),
                    "설치 제거 확인",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation == DialogResult.OK)
                    await StartUninstallCoreAsync();
            };
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_startButton);

            CancelButton = _cancelButton;
            Shown += delegate
            {
                if (_startButton.Enabled) _startButton.Focus();
                else _deleteDataCheckBox.Focus();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (_busy && !UninstallStarted) args.Cancel = true;
            };
        }

        internal static bool ShowForCurrentApplication(IWin32Window owner)
        {
            using (var form = new UninstallOptionsForm(
                deleteData => UninstallSupport.CreateCurrentLaunchPlan(deleteData),
                new SystemUninstallProcessLauncher(),
                new SystemUninstallFirewallService()))
            {
                if (owner == null) form.ShowDialog();
                else form.ShowDialog(owner);
                return form.UninstallStarted;
            }
        }

        internal static void ShowInspectionPreview(IWin32Window owner)
        {
            using (var form = new UninstallOptionsForm(
                deleteData => new UninstallLaunchPlan
                {
                    InstallKind = ApplicationInstallKind.Developer,
                    DeleteUserData = deleteData,
                    Arguments = UninstallSupport.UninstallArguments,
                    UnavailableReason =
                        "검수 모드에서는 실행 환경과 관계없이 실제 설치 제거를 시작하지 않습니다."
                },
                new SystemUninstallProcessLauncher(),
                new SystemUninstallFirewallService()))
            {
                if (owner == null) form.ShowDialog();
                else form.ShowDialog(owner);
            }
        }

        internal async Task<bool> StartUninstallCoreAsync()
        {
            if (_busy || !_initialPlan.CanStart) return false;
            SetBusy(true);
            int removedFirewallRuleCount = 0;
            try
            {
                // Validate the exact updater path before making any optional
                // firewall change. The same immutable plan is used afterwards.
                UninstallLaunchPlan plan = _planFactory(_deleteDataCheckBox.Checked);
                if (plan == null || !plan.CanStart)
                {
                    _statusLabel.Text = plan == null || string.IsNullOrWhiteSpace(plan.UnavailableReason)
                        ? "Velopack 설치 제거 경로를 다시 확인하지 못했습니다. 현재 앱은 그대로 유지됩니다."
                        : plan.UnavailableReason;
                    _statusLabel.ForeColor = Danger;
                    return false;
                }

                if (_removeFirewallCheckBox.Checked)
                {
                    _statusLabel.Text = "앱이 관리하는 방화벽 규칙을 확인하고 제거하는 중…";
                    _statusLabel.ForeColor = Accent;
                    UninstallFirewallCleanupResult firewall =
                        await _firewallService.RemoveAllManagedRulesAsync();
                    if (firewall == null || !firewall.Success)
                    {
                        if (firewall != null && firewall.Cancelled)
                            _statusLabel.Text = "관리자 권한 요청이 취소되어 설치 제거를 시작하지 않았습니다. 다시 시도할 수 있습니다.";
                        else if (firewall != null && firewall.FailedCount > 0)
                            _statusLabel.Text = "방화벽 규칙 " + firewall.RemovedCount
                                + "개 제거, " + firewall.FailedCount
                                + "개 실패로 설치 제거를 시작하지 않았습니다. 다시 시도해 주세요.";
                        else
                            _statusLabel.Text = firewall == null || string.IsNullOrWhiteSpace(firewall.ErrorMessage)
                                ? "방화벽 규칙을 안전하게 확인하지 못해 설치 제거를 시작하지 않았습니다."
                                : firewall.ErrorMessage + "\r\n설치 제거를 시작하지 않았습니다.";
                        _statusLabel.ForeColor = Danger;
                        return false;
                    }
                    removedFirewallRuleCount = firewall.RemovedCount;
                }

                _statusLabel.Text = "Windows 설치 제거 절차를 시작하는 중…";
                _statusLabel.ForeColor = Accent;
                string error;
                if (!UninstallSupport.TryStart(plan, _processLauncher, out error))
                {
                    _statusLabel.Text = error + "\r\n"
                        + (removedFirewallRuleCount > 0
                            ? "선택한 앱 관리 방화벽 규칙 " + removedFirewallRuleCount
                                + "개는 이미 제거되었습니다. 앱 파일과 사용자 데이터는 그대로이며 다시 시도할 수 있습니다."
                            : "현재 앱과 선택한 사용자 데이터는 그대로이며 다시 시도할 수 있습니다.");
                    _statusLabel.ForeColor = Danger;
                    return false;
                }

                UninstallStarted = true;
                _statusLabel.Text = "Windows 설치 제거 절차에 안전하게 넘겼습니다. 앱을 종료합니다.";
                _statusLabel.ForeColor = Accent;
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            finally
            {
                if (!UninstallStarted) SetBusy(false);
            }
        }

        internal void SetOptionsForTest(bool deleteData, bool removeFirewall)
        {
            _deleteDataCheckBox.Checked = deleteData;
            _removeFirewallCheckBox.Checked = removeFirewall;
        }

        internal bool IsStartEnabledForTest
        {
            get { return _startButton.Enabled; }
        }

        internal string StatusTextForTest
        {
            get { return _statusLabel.Text; }
        }

        private string BuildConfirmationText()
        {
            return "Tarkov Server Guard 설치 제거를 시작할까요?\r\n\r\n"
                + "로컬 사용자 데이터: " + (_deleteDataCheckBox.Checked ? "모두 삭제" : "보존") + "\r\n"
                + "앱 관리 방화벽 규칙: " + (_removeFirewallCheckBox.Checked ? "모두 제거" : "보존") + "\r\n\r\n"
                + "설치 제거를 시작한 뒤에는 앱 파일과 바로가기가 제거됩니다.";
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _deleteDataCheckBox.Enabled = !busy;
            _removeFirewallCheckBox.Enabled = !busy;
            _startButton.Enabled = !busy && _initialPlan.CanStart;
            _cancelButton.Enabled = !busy;
            ControlBox = !busy;
        }

        private static CheckBox CreateOption(
            string title,
            string description,
            int tabIndex)
        {
            var option = new CheckBox
            {
                Dock = DockStyle.Fill,
                Appearance = Appearance.Button,
                AutoCheck = true,
                CheckAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                Padding = new Padding(14, 8, 14, 8),
                Margin = new Padding(0, 3, 0, 3),
                Text = "□  " + title + "\r\n     " + description,
                TabIndex = tabIndex,
                AccessibleName = title,
                AccessibleDescription = description,
                UseVisualStyleBackColor = false
            };
            EventHandler refreshGlyph = delegate
            {
                option.Text = (option.Checked ? "☑  " : "□  ") + title
                    + "\r\n     " + description;
                option.BackColor = option.Checked ? SurfaceAlt : Surface;
                option.ForeColor = option.Checked ? Accent : TextPrimary;
            };
            option.CheckedChanged += refreshGlyph;
            refreshGlyph(null, EventArgs.Empty);
            return option;
        }

        private static Button CreateButton(string text, Color backColor, Color foreColor, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                Margin = new Padding(6, 0, 0, 0)
            };
            button.FlatAppearance.BorderColor = Border;
            return button;
        }
    }
}
