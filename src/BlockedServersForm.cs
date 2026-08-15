using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class BlockedServersForm : BrandedForm
    {
        private static readonly Color Background = Color.FromArgb(22, 27, 33);
        private static readonly Color Surface = Color.FromArgb(30, 37, 45);
        private static readonly Color SurfaceAlt = Color.FromArgb(37, 45, 55);
        private static readonly Color Border = Color.FromArgb(57, 68, 80);
        private static readonly Color TextPrimary = Color.FromArgb(232, 235, 238);
        private static readonly Color TextMuted = Color.FromArgb(158, 169, 180);
        private static readonly Color Accent = Color.FromArgb(232, 156, 55);
        private static readonly Color Success = Color.FromArgb(68, 184, 121);
        private static readonly Color Danger = Color.FromArgb(224, 91, 91);

        private readonly DataGridView _grid;
        private readonly Label _summaryLabel;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _removeSelectedButton;
        private readonly Button _removeAllButton;
        private readonly Button _closeButton;
        private bool _busy;

        private sealed class ContainedActionButtonColumn : DataGridViewButtonColumn
        {
            public ContainedActionButtonColumn()
            {
                CellTemplate = new ContainedActionButtonCell();
                FlatStyle = FlatStyle.Flat;
                SortMode = DataGridViewColumnSortMode.NotSortable;
            }
        }

        private sealed class ContainedActionButtonCell : DataGridViewButtonCell
        {
            protected override void Paint(
                Graphics graphics,
                Rectangle clipBounds,
                Rectangle cellBounds,
                int rowIndex,
                DataGridViewElementStates elementState,
                object value,
                object formattedValue,
                string errorText,
                DataGridViewCellStyle cellStyle,
                DataGridViewAdvancedBorderStyle advancedBorderStyle,
                DataGridViewPaintParts paintParts)
            {
                bool selected = (elementState & DataGridViewElementStates.Selected) != 0;
                Color background = selected ? cellStyle.SelectionBackColor : cellStyle.BackColor;
                using (var backgroundBrush = new SolidBrush(background))
                    graphics.FillRectangle(backgroundBrush, cellBounds);
                PaintBorder(graphics, clipBounds, cellBounds, cellStyle, advancedBorderStyle);

                Rectangle buttonBounds = GetButtonBounds(cellBounds, DataGridView);
                Rectangle visibleBounds = Rectangle.Intersect(buttonBounds, clipBounds);
                if (buttonBounds.Width < 18 || buttonBounds.Height < 14 || visibleBounds.IsEmpty) return;

                bool enabled = DataGridView != null && DataGridView.Enabled;
                Color buttonColor = enabled ? Success : SurfaceAlt;
                Color borderColor = enabled ? Color.FromArgb(42, 137, 87) : Border;
                Color textColor = enabled ? Color.FromArgb(18, 36, 27) : TextMuted;
                GraphicsState state = graphics.Save();
                try
                {
                    graphics.SetClip(visibleBounds);
                    using (var buttonBrush = new SolidBrush(buttonColor))
                        graphics.FillRectangle(buttonBrush, buttonBounds);
                    using (var borderPen = new Pen(borderColor))
                        graphics.DrawRectangle(
                            borderPen,
                            buttonBounds.X,
                            buttonBounds.Y,
                            buttonBounds.Width - 1,
                            buttonBounds.Height - 1);

                    string text = Convert.ToString(formattedValue);
                    TextRenderer.DrawText(
                        graphics,
                        string.IsNullOrWhiteSpace(text) ? "해제" : text,
                        cellStyle.Font,
                        Rectangle.Inflate(buttonBounds, -4, -2),
                        textColor,
                        TextFormatFlags.HorizontalCenter
                            | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.SingleLine
                            | TextFormatFlags.EndEllipsis
                            | TextFormatFlags.NoPadding);
                }
                finally
                {
                    graphics.Restore(state);
                }
            }

            protected override Rectangle GetContentBounds(
                Graphics graphics,
                DataGridViewCellStyle cellStyle,
                int rowIndex)
            {
                return GetButtonBounds(
                    new Rectangle(Point.Empty, Size),
                    DataGridView);
            }

            private static Rectangle GetButtonBounds(Rectangle cellBounds, DataGridView grid)
            {
                float scale = grid == null ? 1F : Math.Max(1F, grid.DeviceDpi / 96F);
                int horizontalInset = Math.Max(5, (int)Math.Round(7F * scale));
                int verticalInset = Math.Max(3, (int)Math.Round(5F * scale));
                return Rectangle.Inflate(cellBounds, -horizontalInset, -verticalInset);
            }
        }

        public BlockedServersForm()
        {
            Text = "서버차단현황";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1040, 510);
            MinimumSize = new Size(820, 410);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(0, 0),
                Text = "서버차단현황",
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            _summaryLabel = new Label
            {
                AutoSize = true,
                Location = new Point(2, 36),
                Text = "앱이 관리하는 차단 규칙을 확인하는 중…",
                ForeColor = TextMuted
            };
            header.Controls.Add(_summaryLabel);
            root.Controls.Add(header, 0, 0);

            _grid = BuildGrid();
            root.Controls.Add(_grid, 0, 1);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                AutoEllipsis = true
            };
            root.Controls.Add(_statusLabel, 0, 2);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0),
                BackColor = Background
            };
            _closeButton = CreateButton("닫기", false);
            _removeAllButton = CreateButton("전체 해제", true);
            _removeSelectedButton = CreateButton("선택 해제", false);
            _refreshButton = CreateButton("새로고침", false);
            actions.Controls.Add(_closeButton);
            actions.Controls.Add(_removeAllButton);
            actions.Controls.Add(_removeSelectedButton);
            actions.Controls.Add(_refreshButton);
            root.Controls.Add(actions, 0, 3);

            _closeButton.Click += delegate { Close(); };
            _refreshButton.Click += async delegate { await RefreshRulesAsync(); };
            _removeSelectedButton.Click += async delegate { await RemoveSelectedAsync(); };
            _removeAllButton.Click += async delegate { await RemoveAllAsync(); };
            _grid.CellContentClick += GridCellContentClick;
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += delegate { UpdateButtons(); };
            Shown += async delegate { await RefreshRulesAsync(); };
        }

        public bool FirewallStateChanged { get; private set; }

        public event EventHandler FirewallRulesChanged;

        public Task RefreshAsync()
        {
            return RefreshRulesAsync();
        }

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
                RowTemplate = { Height = 36 },
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
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextPrimary,
                SelectionBackColor = Color.FromArgb(48, 59, 70),
                SelectionForeColor = TextPrimary,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(5, 0, 5, 0)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(33, 40, 49);

            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "selected",
                HeaderText = "선택",
                Width = 54,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ip",
                HeaderText = "서버 IP",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 45F
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "status",
                HeaderText = "상태",
                ReadOnly = true,
                Width = 105
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "location",
                HeaderText = "데이터센터 / 지역",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 40F
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "blockedAt",
                HeaderText = "차단시각",
                ReadOnly = true,
                Width = 145
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "kind",
                HeaderText = "규칙 구분",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 18F
            });
            var removeColumn = new ContainedActionButtonColumn
            {
                Name = "remove",
                HeaderText = "개별해제",
                Text = "해제",
                UseColumnTextForButtonValue = true,
                ReadOnly = true,
                Width = 88,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Surface,
                    ForeColor = TextPrimary,
                    SelectionBackColor = Color.FromArgb(48, 59, 70),
                    SelectionForeColor = TextPrimary,
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Font = new Font("Malgun Gothic", 8F, FontStyle.Bold)
                }
            };
            removeColumn.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            removeColumn.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            removeColumn.HeaderCell.Style.Padding = new Padding(0);
            grid.Columns.Add(removeColumn);
            grid.HandleCreated += delegate { ApplyGridDpiMetrics(grid); };
            grid.DpiChangedAfterParent += delegate { ApplyGridDpiMetrics(grid); };
            return grid;
        }

        private static void ApplyGridDpiMetrics(DataGridView grid)
        {
            if (grid == null || !grid.Columns.Contains("remove")) return;
            float scale = Math.Max(1F, grid.DeviceDpi / 96F);
            int horizontalInset = Math.Max(5, (int)Math.Round(7F * scale));
            int verticalInset = Math.Max(3, (int)Math.Round(5F * scale));
            DataGridViewColumn removeColumn = grid.Columns["remove"];
            Font headerFont = grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font;
            Font buttonFont = removeColumn.DefaultCellStyle.Font ?? grid.Font;
            Size headerText;
            Size buttonText;
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            using (Graphics graphics = grid.CreateGraphics())
            {
                headerText = TextRenderer.MeasureText(graphics, "개별해제", headerFont, new Size(1000, 1000), flags);
                buttonText = TextRenderer.MeasureText(graphics, "해제", buttonFont, new Size(1000, 1000), flags);
            }

            int headerWidth = headerText.Width + Math.Max(12, (int)Math.Round(18F * scale));
            int buttonWidth = buttonText.Width + horizontalInset * 2 + Math.Max(6, (int)Math.Round(8F * scale));
            removeColumn.Width = Math.Max(
                Math.Max(headerWidth, buttonWidth),
                (int)Math.Round(88F * scale));

            int rowHeight = Math.Max(
                (int)Math.Round(36F * scale),
                buttonText.Height + verticalInset * 2 + Math.Max(2, (int)Math.Round(4F * scale)));
            grid.RowTemplate.Height = rowHeight;
            foreach (DataGridViewRow row in grid.Rows) row.Height = rowHeight;
            grid.ColumnHeadersHeight = Math.Max(
                (int)Math.Round(36F * scale),
                headerText.Height + Math.Max(8, (int)Math.Round(12F * scale)));
        }

        private static Button CreateButton(string text, bool emphasized)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(text.Length > 4 ? 100 : 82, 32),
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

        private async Task RefreshRulesAsync()
        {
            if (_busy) return;
            SetBusy(true, "앱이 관리하는 차단 규칙을 확인하는 중…");
            ManagedBlockedServerQueryResult result = await Task.Run(
                () => FirewallRuleManager.QueryManagedBlockedServers());
            if (IsDisposed) return;
            IDictionary<string, BlockedServerMetadata> metadataByIp = await Task.Run(
                () => BlockedServerMetadataStore.LoadAll());
            if (IsDisposed) return;

            _grid.Rows.Clear();
            if (!result.Success)
            {
                _summaryLabel.Text = "서버차단현황을 확인하지 못했습니다.";
                SetBusy(false, result.ErrorMessage ?? "Windows 방화벽을 확인할 수 없습니다.");
                return;
            }

            foreach (ManagedBlockedServer server in result.Servers)
            {
                BlockedServerMetadata metadata;
                metadataByIp.TryGetValue(server.IpAddress, out metadata);
                int index = _grid.Rows.Add(
                    false,
                    server.IpAddress,
                    server.StatusText,
                    metadata == null ? "-" : metadata.DataCenterLocationText,
                    metadata == null ? "확인 안 됨" : metadata.BlockedAtText,
                    server.RuleKindText,
                    "해제");
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = server;
                row.Cells["status"].Style.ForeColor = Danger;
            }

            _summaryLabel.Text = result.Servers.Count == 0
                ? "현재 앱이 관리하는 차단 서버가 없습니다."
                : string.Format("현재 {0}개 서버가 차단되어 있습니다.", result.Servers.Count);
            SetBusy(false, result.Servers.Count == 0
                ? "현재 규칙과 v1.1.1 호환 규칙을 모두 확인했습니다."
                : "해제할 서버를 선택하거나 행의 ‘해제’를 누르세요.");
        }

        private async void GridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (_busy || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "remove") return;
            ManagedBlockedServer server = _grid.Rows[e.RowIndex].Tag as ManagedBlockedServer;
            if (server == null) return;

            DialogResult answer = MessageBox.Show(
                this,
                server.IpAddress + " 서버의 차단을 해제할까요?",
                "차단 해제 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            await RemoveAddressesAsync(new[] { server.IpAddress });
        }

        private async Task RemoveSelectedAsync()
        {
            IList<string> addresses = GetSelectedAddresses();
            if (addresses.Count == 0)
            {
                MessageBox.Show(this, "해제할 서버를 먼저 선택해 주세요.", "선택 해제",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult answer = MessageBox.Show(
                this,
                string.Format("선택한 {0}개 서버의 차단을 해제할까요?", addresses.Count),
                "선택 차단 해제",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes) await RemoveAddressesAsync(addresses);
        }

        private async Task RemoveAllAsync()
        {
            IList<string> addresses = GetAllAddresses();
            if (addresses.Count == 0) return;

            DialogResult answer = MessageBox.Show(
                this,
                string.Format(
                    "앱이 관리하는 차단 서버 {0}개를 모두 해제할까요?\r\n\r\n"
                    + "Tarkov Server Guard의 현재 규칙과 v1.1.1 호환 규칙만 삭제합니다.\r\n"
                    + "사용자가 직접 만든 다른 방화벽 규칙은 삭제하지 않습니다.",
                    addresses.Count),
                "전체 차단 해제 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes) await RemoveAddressesAsync(addresses);
        }

        private async Task RemoveAddressesAsync(IEnumerable<string> ipAddresses)
        {
            IList<string> addresses = ipAddresses.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            SetBusy(true, string.Format("선택한 {0}개 서버의 차단을 해제하는 중…", addresses.Count));
            FirewallBatchChangeResult result = await FirewallRuleManager.RemoveManyWithElevationAsync(addresses);
            if (IsDisposed) return;

            int succeeded = result.Items.Count(item => item.Success);
            bool metadataRemoved = true;
            if (succeeded > 0)
            {
                metadataRemoved = BlockedServerMetadataStore.Remove(
                    result.Items.Where(item => item.Success).Select(item => item.IpAddress));
                FirewallStateChanged = true;
                EventHandler handler = FirewallRulesChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            if (result.Cancelled)
            {
                SetBusy(false, result.ErrorMessage);
                return;
            }

            await RefreshRulesAfterChangeAsync();
            if (result.Success)
            {
                _statusLabel.ForeColor = metadataRemoved ? Success : Color.FromArgb(231, 184, 73);
                _statusLabel.Text = metadataRemoved
                    ? string.Format("{0}개 서버의 차단을 해제했습니다.", succeeded)
                    : string.Format("{0}개 서버를 해제했지만 로컬 표시 정보 정리에 실패했습니다.", succeeded);
                return;
            }

            IList<FirewallBatchItemResult> failed = result.Items.Where(item => !item.Success).ToList();
            string failureDetail = string.Join(", ", failed.Take(3).Select(item => item.IpAddress));
            if (failed.Count > 3) failureDetail += " 외 " + (failed.Count - 3) + "개";
            _statusLabel.ForeColor = Danger;
            _statusLabel.Text = succeeded > 0
                ? string.Format("{0}개 해제, {1}개 실패: {2}", succeeded, failed.Count, failureDetail)
                : (result.ErrorMessage ?? "차단 해제를 완료하지 못했습니다.");
            if (succeeded > 0 && !metadataRemoved)
                _statusLabel.Text += " · 로컬 표시 정보 정리 실패";
        }

        private async Task RefreshRulesAfterChangeAsync()
        {
            _busy = false;
            await RefreshRulesAsync();
        }

        private IList<string> GetSelectedAddresses()
        {
            var addresses = new List<string>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                bool selected = Convert.ToBoolean(row.Cells["selected"].Value ?? false);
                ManagedBlockedServer server = row.Tag as ManagedBlockedServer;
                if (selected && server != null) addresses.Add(server.IpAddress);
            }
            return addresses;
        }

        private IList<string> GetAllAddresses()
        {
            return _grid.Rows.Cast<DataGridViewRow>()
                .Select(row => row.Tag as ManagedBlockedServer)
                .Where(server => server != null)
                .Select(server => server.IpAddress)
                .ToList();
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            _statusLabel.ForeColor = TextMuted;
            _statusLabel.Text = status ?? string.Empty;
            _grid.Enabled = !busy;
            UpdateButtons();
            UseWaitCursor = busy;
        }

        private void UpdateButtons()
        {
            int count = _grid == null ? 0 : _grid.Rows.Count;
            bool hasSelected = !_busy && count > 0 && GetSelectedAddresses().Count > 0;
            _refreshButton.Enabled = !_busy;
            _removeSelectedButton.Enabled = hasSelected;
            _removeSelectedButton.BackColor = hasSelected ? Success : SurfaceAlt;
            _removeSelectedButton.ForeColor = hasSelected
                ? Color.FromArgb(18, 36, 27)
                : TextMuted;
            _removeSelectedButton.FlatAppearance.BorderColor = hasSelected
                ? Color.FromArgb(42, 137, 87)
                : Border;
            _removeSelectedButton.FlatAppearance.MouseOverBackColor = hasSelected
                ? Color.FromArgb(82, 201, 139)
                : Color.FromArgb(45, 54, 64);
            _removeSelectedButton.FlatAppearance.MouseDownBackColor = hasSelected
                ? Color.FromArgb(57, 164, 108)
                : SurfaceAlt;
            bool canRemoveAll = !_busy && count > 0;
            _removeAllButton.Enabled = canRemoveAll;
            _removeAllButton.BackColor = canRemoveAll ? Success : SurfaceAlt;
            _removeAllButton.ForeColor = canRemoveAll ? Color.FromArgb(18, 36, 27) : TextMuted;
            _removeAllButton.FlatAppearance.BorderColor = canRemoveAll
                ? Color.FromArgb(42, 137, 87)
                : Border;
            _closeButton.Enabled = !_busy;
        }
    }
}
