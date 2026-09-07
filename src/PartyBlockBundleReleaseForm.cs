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
    internal sealed class PartyBlockBundleReleaseForm : BrandedForm
    {
        private sealed class BundleOption
        {
            internal PartyBlockBundle Bundle { get; set; }
            internal string DisplayText { get; set; }

            public override string ToString()
            {
                return DisplayText;
            }
        }

        private static readonly Color Background = Color.FromArgb(22, 27, 33);
        private static readonly Color Surface = Color.FromArgb(30, 37, 45);
        private static readonly Color SurfaceAlt = Color.FromArgb(37, 45, 55);
        private static readonly Color Border = Color.FromArgb(57, 68, 80);
        private static readonly Color TextPrimary = Color.FromArgb(232, 235, 238);
        private static readonly Color TextMuted = Color.FromArgb(158, 169, 180);
        private static readonly Color Success = Color.FromArgb(68, 184, 121);
        private static readonly Color Danger = Color.FromArgb(224, 91, 91);
        private static readonly Color Warning = Color.FromArgb(231, 184, 73);

        private readonly PartyBlockBundleStore _store;
        private readonly ComboBox _bundleComboBox;
        private readonly Label _bundleDetailLabel;
        private readonly DataGridView _grid;
        private readonly Label _summaryLabel;
        private readonly Button _releaseButton;
        private bool _busy;
        private int _previewGeneration;

        internal PartyBlockBundleReleaseForm(PartyBlockBundleStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            _store = store;
            Text = AppText.Get("파티 묶음 종료");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(860, 570);
            MinimumSize = new Size(700, 470);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowIcon = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(0, 0),
                Text = AppText.Get("파티 차단 묶음 종료"),
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(2, 37),
                Text = AppText.Get("출처 묶음 하나를 골라 안전하게 일괄 해제할 항목을 확인합니다."),
                ForeColor = TextMuted
            });
            root.Controls.Add(header, 0, 0);

            var picker = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0, 2, 0, 8)
            };
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            picker.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            picker.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            picker.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("출처 묶음"),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            _bundleComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 3),
                BackColor = Surface,
                ForeColor = TextPrimary,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = AppText.Get("종료할 파티 출처 묶음")
            };
            picker.Controls.Add(_bundleComboBox, 1, 0);
            _bundleDetailLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = string.Empty,
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = AppText.Get("선택한 파티 묶음 정보")
            };
            picker.Controls.Add(_bundleDetailLabel, 1, 1);
            root.Controls.Add(picker, 0, 1);

            _grid = BuildGrid();
            root.Controls.Add(_grid, 0, 2);

            var resultPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            resultPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            resultPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("종료할 출처 묶음을 선택해 주세요."),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = AppText.Get("파티 묶음 해제 미리보기 요약")
            };
            resultPanel.Controls.Add(_summaryLabel, 0, 0);
            resultPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get(
                    "묶음 생성 전 이미 차단됐거나 다른 활성 묶음이 함께 쓰는 IP는 절대 해제하지 않습니다. "
                    + "해제 대상은 한 번의 관리자 권한 요청으로 처리합니다."),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = AppText.Get("파티 묶음 안전 해제 안내")
            }, 0, 1);
            root.Controls.Add(resultPanel, 0, 3);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = Background
            };
            var cancelButton = CreateButton(AppText.Get("취소"), false, 82);
            cancelButton.DialogResult = DialogResult.Cancel;
            _releaseButton = CreateButton(AppText.Get("묶음 종료"), true, 108);
            _releaseButton.Enabled = false;
            actions.Controls.Add(cancelButton);
            actions.Controls.Add(_releaseButton);
            root.Controls.Add(actions, 0, 4);
            CancelButton = cancelButton;

            Shown += async delegate { await LoadBundlesAsync(); };
            _bundleComboBox.SelectedIndexChanged += async delegate
            {
                await RefreshPreviewAsync();
            };
            _releaseButton.Click += ReleaseClicked;
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!_busy || e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                _summaryLabel.ForeColor = Warning;
                _summaryLabel.Text = AppText.Get("방화벽 상태 확인이 끝난 뒤 창을 닫을 수 있습니다.");
            };
        }

        internal string SelectedBundleId { get; private set; }

        private DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeight = 36,
                RowHeadersVisible = false,
                RowTemplate = { Height = 34 },
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                ReadOnly = true,
                AccessibleName = AppText.Get("파티 묶음 해제 미리보기")
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                SelectionBackColor = SurfaceAlt,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextPrimary,
                SelectionBackColor = Color.FromArgb(48, 59, 70),
                SelectionForeColor = TextPrimary,
                Padding = new Padding(5, 0, 5, 0)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(33, 40, 49);
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ip",
                HeaderText = AppText.Get("서버 IP"),
                Width = 145
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "action",
                HeaderText = AppText.Get("종료 처리"),
                Width = 150
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "detail",
                HeaderText = AppText.Get("보존·해제 사유"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            return grid;
        }

        private async Task LoadBundlesAsync()
        {
            if (_busy) return;
            SetBusy(true, AppText.Get("저장된 파티 출처 묶음을 확인하는 중…"));
            PartyBlockBundleSnapshot snapshot = await Task.Run(() => _store.LoadSnapshot());
            if (IsDisposed) return;
            _bundleComboBox.Items.Clear();
            if (!snapshot.Success)
            {
                SetBusy(false, AppText.Get("파티 묶음 정보가 손상되어 안전한 종료 작업을 시작할 수 없습니다."));
                _summaryLabel.ForeColor = Danger;
                return;
            }
            foreach (PartyBlockBundle bundle in snapshot.Bundles
                .OrderByDescending(item => item.CreatedAtUtc))
            {
                _bundleComboBox.Items.Add(new BundleOption
                {
                    Bundle = bundle,
                    DisplayText = AppText.Format(
                        "{0} · {1}개 · {2}",
                        bundle.SourceName,
                        bundle.Members.Count,
                        bundle.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"))
                });
            }
            SetBusy(false, snapshot.Bundles.Count == 0
                ? AppText.Get("종료할 활성 파티 묶음이 없습니다.")
                : AppText.Format("활성 또는 처리 대기 파티 묶음 {0}개를 확인했습니다.",
                    snapshot.Bundles.Count));
            if (_bundleComboBox.Items.Count > 0) _bundleComboBox.SelectedIndex = 0;
        }

        private async Task RefreshPreviewAsync()
        {
            BundleOption selected = _bundleComboBox.SelectedItem as BundleOption;
            int generation = ++_previewGeneration;
            _grid.Rows.Clear();
            _releaseButton.Enabled = false;
            SelectedBundleId = null;
            if (selected == null) return;

            SetBusy(true, AppText.Get("선택한 묶음의 현재 방화벽 상태를 확인하는 중…"));
            string[] addresses = selected.Bundle.Members
                .Select(item => item.IpAddress)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Dictionary<string, FirewallQueryResult> states = await Task.Run(
                () => FirewallRuleManager.QueryMany(addresses));
            PartyBlockReleasePreview preview = await Task.Run(
                () => _store.CreateReleasePreview(selected.Bundle.BundleId, states));
            if (IsDisposed || generation != _previewGeneration) return;
            if (!preview.Success)
            {
                SetBusy(false, AppText.Get("해제 미리보기를 만들지 못했습니다. 묶음 정보를 다시 확인해 주세요."));
                _summaryLabel.ForeColor = Danger;
                return;
            }

            foreach (PartyBlockReleasePreviewItem item in preview.Items)
            {
                int index = _grid.Rows.Add(
                    item.IpAddress,
                    GetDispositionText(item.Disposition),
                    GetDispositionDetail(item));
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = item;
                row.Cells["action"].Style.ForeColor =
                    item.Disposition == PartyBlockReleaseDisposition.Remove
                        ? Danger
                        : item.Disposition == PartyBlockReleaseDisposition.PendingReconcile
                            ? Warning
                            : Success;
            }

            int removeCount = preview.Items.Count(
                item => item.Disposition == PartyBlockReleaseDisposition.Remove);
            int preservedCount = preview.Items.Count(item =>
                item.Disposition == PartyBlockReleaseDisposition.PreserveBaseline
                || item.Disposition == PartyBlockReleaseDisposition.PreservePermanent
                || item.Disposition == PartyBlockReleaseDisposition.PreserveOverlap);
            int alreadyMissingCount = preview.Items.Count(
                item => item.Disposition == PartyBlockReleaseDisposition.AlreadyMissing);
            int pendingCount = preview.Items.Count(
                item => item.Disposition == PartyBlockReleaseDisposition.PendingReconcile);
            _bundleDetailLabel.Text = AppText.Format(
                "사유: {0} · 상태: {1}",
                GetReasonText(selected.Bundle.ReasonCode),
                GetLifecycleText(selected.Bundle.State));
            SetBusy(false, AppText.Format(
                "해제 {0}개 · 안전하게 유지 {1}개 · 이미 없음 {2}개 · 확인 대기 {3}개",
                removeCount,
                preservedCount,
                alreadyMissingCount,
                pendingCount));
            _summaryLabel.ForeColor = pendingCount == 0 ? TextMuted : Warning;
            SelectedBundleId = selected.Bundle.BundleId;
            _releaseButton.Enabled = preview.Items.Count > 0;
            _releaseButton.BackColor = _releaseButton.Enabled ? Success : SurfaceAlt;
            _releaseButton.ForeColor = _releaseButton.Enabled
                ? Color.FromArgb(18, 36, 27)
                : TextMuted;
        }

        private void ReleaseClicked(object sender, EventArgs e)
        {
            if (_busy || string.IsNullOrWhiteSpace(SelectedBundleId)) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            _bundleComboBox.Enabled = !busy;
            _grid.Enabled = !busy;
            if (!busy) _releaseButton.Enabled = !string.IsNullOrWhiteSpace(SelectedBundleId);
            else _releaseButton.Enabled = false;
            _summaryLabel.Text = status ?? string.Empty;
            _summaryLabel.ForeColor = TextMuted;
            UseWaitCursor = busy;
        }

        private static string GetDispositionText(PartyBlockReleaseDisposition disposition)
        {
            if (disposition == PartyBlockReleaseDisposition.Remove)
                return AppText.Get("차단 해제");
            if (disposition == PartyBlockReleaseDisposition.AlreadyMissing)
                return AppText.Get("이미 해제됨");
            if (disposition == PartyBlockReleaseDisposition.PendingReconcile)
                return AppText.Get("확인 대기");
            return AppText.Get("차단 유지");
        }

        private static string GetDispositionDetail(PartyBlockReleasePreviewItem item)
        {
            if (item.Disposition == PartyBlockReleaseDisposition.Remove)
                return AppText.Get("이 묶음이 마지막 참조이며 생성 전에는 차단되지 않았습니다.");
            if (item.Disposition == PartyBlockReleaseDisposition.PreserveBaseline)
                return AppText.Get("이 묶음 생성 전부터 차단되어 있어 그대로 유지합니다.");
            if (item.Disposition == PartyBlockReleaseDisposition.PreservePermanent)
                return AppText.Get("개인 차단으로 유지하도록 지정되어 그대로 보존합니다.");
            if (item.Disposition == PartyBlockReleaseDisposition.PreserveOverlap)
                return AppText.Format(
                    "다른 활성 파티 묶음 {0}개가 함께 사용하여 유지합니다.",
                    item.OtherActiveBundleCount);
            if (item.Disposition == PartyBlockReleaseDisposition.AlreadyMissing)
                return AppText.Get("현재 앱 관리 차단 규칙이 없어 묶음 정보만 정리합니다.");
            return string.IsNullOrWhiteSpace(item.ErrorMessage)
                ? AppText.Get("현재 상태를 확인하지 못해 안전을 위해 자동 해제하지 않습니다.")
                : AppText.TranslateDiagnostic(item.ErrorMessage,
                    "현재 상태를 확인하지 못해 안전을 위해 자동 해제하지 않습니다.");
        }

        private static string GetReasonText(string reasonCode)
        {
            if (string.Equals(reasonCode, "party-leader", StringComparison.Ordinal))
                return AppText.Get("파티장 공유");
            if (string.Equals(reasonCode, "party-member", StringComparison.Ordinal))
                return AppText.Get("파티원 공유");
            if (string.Equals(reasonCode, "manual-confirmed", StringComparison.Ordinal))
                return AppText.Get("직접 확인");
            return AppText.Get("기타");
        }

        private static string GetLifecycleText(PartyBlockBundleLifecycleState state)
        {
            if (state == PartyBlockBundleLifecycleState.Active)
                return AppText.Get("적용 중");
            if (state == PartyBlockBundleLifecycleState.PendingRelease)
                return AppText.Get("해제 확인 대기");
            if (state == PartyBlockBundleLifecycleState.PendingApply)
                return AppText.Get("적용 확인 대기");
            return AppText.Get("상태 재확인 필요");
        }

        private static Button CreateButton(string text, bool emphasized, int width)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 32),
                Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = emphasized ? Success : SurfaceAlt,
                ForeColor = emphasized ? Color.FromArgb(18, 36, 27) : TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = emphasized
                ? Color.FromArgb(42, 137, 87)
                : Border;
            button.FlatAppearance.MouseOverBackColor = emphasized
                ? Color.FromArgb(82, 201, 139)
                : Color.FromArgb(45, 54, 64);
            return button;
        }
    }
}
