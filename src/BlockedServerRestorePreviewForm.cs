// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal sealed class BlockedServerRestorePreviewForm : BrandedForm
    {
        private static readonly Color Background = Color.FromArgb(22, 27, 33);
        private static readonly Color Surface = Color.FromArgb(30, 37, 45);
        private static readonly Color SurfaceAlt = Color.FromArgb(37, 45, 55);
        private static readonly Color Border = Color.FromArgb(57, 68, 80);
        private static readonly Color TextPrimary = Color.FromArgb(232, 235, 238);
        private static readonly Color TextMuted = Color.FromArgb(158, 169, 180);
        private static readonly Color Success = Color.FromArgb(68, 184, 121);
        private static readonly Color Danger = Color.FromArgb(224, 91, 91);
        private static readonly Color Warning = Color.FromArgb(231, 184, 73);

        private readonly DataGridView _grid;
        private readonly Button _applyButton;
        private readonly IList<BlockedServerRestoreItem> _items;

        internal BlockedServerRestorePreviewForm(IEnumerable<BlockedServerRestoreItem> items)
        {
            _items = (items ?? Enumerable.Empty<BlockedServerRestoreItem>()).ToList();
            SelectedItems = new List<BlockedServerRestoreItem>();

            Text = "차단 목록 복원 미리보기";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(940, 560);
            MinimumSize = new Size(760, 440);
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
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            int newCount = _items.Count(item => item.Status == BlockedServerRestoreStatus.NewBlock);
            int existingCount = _items.Count(item => item.Status == BlockedServerRestoreStatus.AlreadyBlocked);
            int excludedCount = _items.Count(item => item.Status == BlockedServerRestoreStatus.Excluded);
            var header = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(0, 0),
                Text = "차단 목록 복원 미리보기",
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(2, 38),
                Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "새로 차단 {0}개 · 이미 차단됨 {1}개 · 적용 제외 {2}개",
                    newCount,
                    existingCount,
                    excludedCount),
                ForeColor = TextMuted
            });
            root.Controls.Add(header, 0, 0);

            _grid = BuildGrid();
            root.Controls.Add(_grid, 0, 1);
            PopulateRows();

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "선택한 새 IP만 이 PC의 Windows 방화벽에 한 번의 관리자 권한 요청으로 추가합니다.\r\n"
                    + "이미 저장된 지역·메모·차단시각은 덮어쓰지 않고 비어 있는 정보만 복원합니다.",
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AccessibleName = "개인 차단 목록 복원 안내"
            }, 0, 2);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = Background
            };
            var cancelButton = CreateButton("취소", false);
            cancelButton.DialogResult = DialogResult.Cancel;
            _applyButton = CreateButton("선택 항목 복원", true);
            _applyButton.Size = new Size(126, 32);
            _applyButton.Click += ApplyClicked;
            actions.Controls.Add(cancelButton);
            actions.Controls.Add(_applyButton);
            root.Controls.Add(actions, 0, 3);
            CancelButton = cancelButton;

            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += delegate { UpdateApplyButton(); };
            UpdateApplyButton();
        }

        internal IList<BlockedServerRestoreItem> SelectedItems { get; private set; }

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
                ReadOnly = false
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
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "selected",
                HeaderText = "적용",
                Width = 52,
                FlatStyle = FlatStyle.Flat
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "status",
                HeaderText = "구분",
                ReadOnly = true,
                Width = 108
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ip",
                HeaderText = "서버 IP",
                ReadOnly = true,
                Width = 130
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "metadata",
                HeaderText = "백업 정보",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 42F
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "detail",
                HeaderText = "확인 결과",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 58F
            });
            return grid;
        }

        private void PopulateRows()
        {
            foreach (BlockedServerRestoreItem item in _items)
            {
                bool selectable = item.Status == BlockedServerRestoreStatus.NewBlock
                    || (item.Status == BlockedServerRestoreStatus.AlreadyBlocked && item.HasMetadata);
                int index = _grid.Rows.Add(
                    selectable,
                    GetStatusText(item.Status),
                    item.IpAddress,
                    GetMetadataText(item.Entry),
                    item.Detail ?? string.Empty);
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = item;
                row.Cells["selected"].ReadOnly = !selectable;
                row.Cells["status"].Style.ForeColor = item.Status == BlockedServerRestoreStatus.NewBlock
                    ? Success
                    : item.Status == BlockedServerRestoreStatus.AlreadyBlocked ? Warning : Danger;
                if (!selectable) row.DefaultCellStyle.ForeColor = TextMuted;
            }
        }

        private void ApplyClicked(object sender, EventArgs e)
        {
            SelectedItems = _grid.Rows.Cast<DataGridViewRow>()
                .Where(row => Convert.ToBoolean(row.Cells["selected"].Value ?? false))
                .Select(row => row.Tag as BlockedServerRestoreItem)
                .Where(item => item != null && item.Status != BlockedServerRestoreStatus.Excluded)
                .ToList();
            if (SelectedItems.Count == 0) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateApplyButton()
        {
            if (_applyButton == null || _grid == null) return;
            bool hasSelection = _grid.Rows.Cast<DataGridViewRow>()
                .Any(row => !row.Cells["selected"].ReadOnly
                    && Convert.ToBoolean(row.Cells["selected"].Value ?? false));
            _applyButton.Enabled = hasSelection;
            _applyButton.BackColor = hasSelection ? Success : SurfaceAlt;
            _applyButton.ForeColor = hasSelection ? Color.FromArgb(18, 36, 27) : TextMuted;
        }

        private static string GetStatusText(BlockedServerRestoreStatus status)
        {
            if (status == BlockedServerRestoreStatus.NewBlock) return "새로 차단";
            if (status == BlockedServerRestoreStatus.AlreadyBlocked) return "이미 차단됨";
            return "적용 제외";
        }

        private static string GetMetadataText(BlockedServerBackupEntry entry)
        {
            if (entry == null || !entry.HasMetadata) return "없음";
            var fields = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.DataCenter)) fields.Add("데이터센터");
            if (!string.IsNullOrWhiteSpace(entry.Location)) fields.Add("지역");
            if (!string.IsNullOrWhiteSpace(entry.Note)) fields.Add("메모");
            if (entry.BlockedAtUtc.HasValue) fields.Add("차단시각");
            return string.Join(", ", fields);
        }

        private static Button CreateButton(string text, bool emphasized)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(82, 32),
                Margin = new Padding(8, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = emphasized ? Success : SurfaceAlt,
                ForeColor = emphasized ? Color.FromArgb(18, 36, 27) : TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = emphasized ? Color.FromArgb(42, 137, 87) : Border;
            button.FlatAppearance.MouseOverBackColor = emphasized
                ? Color.FromArgb(82, 201, 139)
                : Color.FromArgb(45, 54, 64);
            return button;
        }
    }

    internal sealed class BlockedServerRestoreFailuresForm : BrandedForm
    {
        internal BlockedServerRestoreFailuresForm(
            IEnumerable<KeyValuePair<string, string>> failures)
        {
            IList<KeyValuePair<string, string>> items = (failures
                ?? Enumerable.Empty<KeyValuePair<string, string>>()).ToList();
            Text = "차단 목록 복원 결과";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 440);
            MinimumSize = new Size(620, 360);
            BackColor = Color.FromArgb(22, 27, 33);
            ForeColor = Color.FromArgb(232, 235, 238);
            Font = new Font("Malgun Gothic", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                BackColor = BackColor,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);
            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = string.Format(
                    CultureInfo.CurrentCulture,
                    "최종 앱 관리 차단 규칙을 확인하지 못한 항목이 {0}개 있습니다.",
                    items.Count),
                ForeColor = Color.FromArgb(231, 184, 73),
                Font = new Font("Malgun Gothic", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(30, 37, 45),
                BorderStyle = BorderStyle.FixedSingle,
                GridColor = Color.FromArgb(57, 68, 80),
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 36,
                RowHeadersVisible = false,
                RowTemplate = { Height = 34 },
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                ReadOnly = true
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(37, 45, 55),
                ForeColor = Color.FromArgb(232, 235, 238),
                SelectionBackColor = Color.FromArgb(37, 45, 55),
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(30, 37, 45),
                ForeColor = Color.FromArgb(232, 235, 238),
                SelectionBackColor = Color.FromArgb(48, 59, 70),
                SelectionForeColor = Color.FromArgb(232, 235, 238),
                Padding = new Padding(5, 0, 5, 0)
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ip",
                HeaderText = "서버 IP",
                Width = 140
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "error",
                HeaderText = "실패 사유",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            });
            foreach (KeyValuePair<string, string> item in items)
                grid.Rows.Add(item.Key, item.Value);
            root.Controls.Add(grid, 0, 1);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = BackColor
            };
            var closeButton = new Button
            {
                Text = "닫기",
                Size = new Size(82, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(37, 45, 55),
                ForeColor = Color.FromArgb(232, 235, 238),
                DialogResult = DialogResult.OK
            };
            closeButton.FlatAppearance.BorderColor = Color.FromArgb(57, 68, 80);
            actions.Controls.Add(closeButton);
            root.Controls.Add(actions, 0, 2);
            AcceptButton = closeButton;
            CancelButton = closeButton;
        }
    }
}
