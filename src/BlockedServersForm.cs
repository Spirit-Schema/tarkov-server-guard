// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal static class FirewallPersistenceNotice
    {
        internal const string ProcessExitLine =
            "창을 닫으면 TSG는 완전히 종료됩니다. 백그라운드에 상주하지 않아 "
            + "TSG 프로세스가 CPU·메모리·네트워크를 추가로 사용하지 않습니다.";
        internal const string RulesPersistLine =
            "차단 리스트는 Windows 방화벽에 저장되므로 해당 앱 종료·PC 재부팅 후에도 유지되며, "
            + "앱에서 해제할 때까지 계속 적용됩니다.";
        internal const string FullText = ProcessExitLine + "\r\n" + RulesPersistLine;
    }

    public sealed class BlockedServersForm : BrandedForm
    {
        private static readonly Color Background = Color.FromArgb(22, 27, 33);
        private static readonly Color Surface = Color.FromArgb(30, 37, 45);
        private static readonly Color SurfaceAlt = Color.FromArgb(37, 45, 55);
        private static readonly Color Border = Color.FromArgb(57, 68, 80);
        private static readonly Color TextPrimary = Color.FromArgb(232, 235, 238);
        private static readonly Color TextMuted = Color.FromArgb(158, 169, 180);
        private static readonly Color Accent = Color.FromArgb(232, 156, 55);
        private static readonly Color NoteOrange = Color.FromArgb(205, 132, 48);
        private static readonly Color Success = Color.FromArgb(68, 184, 121);
        private static readonly Color Danger = Color.FromArgb(224, 91, 91);

        private readonly DataGridView _grid;
        private readonly Label _summaryLabel;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _removeSelectedButton;
        private readonly Button _removeAllButton;
        private readonly Button _exportButton;
        private readonly Button _importButton;
        private readonly Button _closeButton;
        private bool _busy;

        private enum NoteEditorAction
        {
            None,
            Saved,
            Deleted
        }

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

        private sealed class BlockNoteEditorForm : BrandedForm
        {
            private const string ExampleText = "높은 핑, 패킷손실, 반복 끊김 등";
            private readonly string _ipAddress;
            private readonly CueTextBox _noteTextBox;
            private readonly Label _statusLabel;
            private readonly Button _deleteButton;

            private sealed class CueTextBox : TextBox
            {
                private const int SetCueBannerMessage = 0x1501;
                private string _cueText;
                private bool _dismissed;

                [DllImport("user32.dll", CharSet = CharSet.Unicode)]
                private static extern IntPtr SendMessage(
                    IntPtr windowHandle,
                    int message,
                    IntPtr wordParameter,
                    string longParameter);

                public string CueText
                {
                    get { return _cueText; }
                    set
                    {
                        _cueText = value ?? string.Empty;
                        ApplyCueBanner();
                    }
                }

                protected override void OnHandleCreated(EventArgs e)
                {
                    base.OnHandleCreated(e);
                    ApplyCueBanner();
                }

                protected override void OnMouseDown(MouseEventArgs e)
                {
                    _dismissed = true;
                    ApplyCueBanner();
                    base.OnMouseDown(e);
                }

                protected override void OnKeyDown(KeyEventArgs e)
                {
                    _dismissed = true;
                    ApplyCueBanner();
                    base.OnKeyDown(e);
                }

                protected override void OnLeave(EventArgs e)
                {
                    if (TextLength == 0) _dismissed = false;
                    ApplyCueBanner();
                    base.OnLeave(e);
                }

                private void ApplyCueBanner()
                {
                    if (!IsHandleCreated) return;
                    SendMessage(
                        Handle,
                        SetCueBannerMessage,
                        new IntPtr(1),
                        _dismissed ? string.Empty : _cueText);
                }
            }

            public BlockNoteEditorForm(string ipAddress, string note)
            {
                _ipAddress = ipAddress;
                Text = "차단 메모";
                StartPosition = FormStartPosition.CenterParent;
                ClientSize = new Size(560, 220);
                MinimumSize = new Size(480, 210);
                BackColor = Background;
                ForeColor = TextPrimary;
                Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
                AutoScaleMode = AutoScaleMode.Dpi;
                ShowInTaskbar = false;
                MinimizeBox = false;
                MaximizeBox = false;

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Background,
                    Padding = new Padding(18, 14, 18, 12),
                    ColumnCount = 1,
                    RowCount = 4
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
                Controls.Add(root);

                var header = new Panel { Dock = DockStyle.Fill, BackColor = Background };
                header.Controls.Add(new Label
                {
                    AutoSize = true,
                    Location = new Point(0, 0),
                    Text = "차단 메모",
                    Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                    ForeColor = TextPrimary
                });
                header.Controls.Add(new Label
                {
                    AutoSize = true,
                    Location = new Point(2, 37),
                    Text = ipAddress + " · 이 PC에만 저장됩니다.",
                    ForeColor = TextMuted
                });
                root.Controls.Add(header, 0, 0);

                var editorHost = new Panel
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 2, 0, 6),
                    BackColor = Background,
                    Padding = new Padding(0)
                };
                _noteTextBox = new CueTextBox
                {
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Surface,
                    ForeColor = TextPrimary,
                    Font = new Font("Malgun Gothic", 10F),
                    MaxLength = BlockedServerMetadataStore.MaximumNoteLength,
                    Multiline = false,
                    Dock = DockStyle.Top,
                    Text = note ?? string.Empty,
                    CueText = ExampleText,
                    AccessibleName = "차단 메모",
                    AccessibleDescription = ExampleText,
                    TabIndex = 0
                };
                editorHost.Controls.Add(_noteTextBox);
                root.Controls.Add(editorHost, 0, 1);

                _statusLabel = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = "선택 입력 · 한 줄, 최대 300자",
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
                    Margin = new Padding(0),
                    Padding = new Padding(0, 6, 0, 0),
                    BackColor = Background
                };
                Button cancelButton = CreateEditorButton("취소", SurfaceAlt, TextPrimary, Border);
                Button saveButton = CreateEditorButton(
                    "저장",
                    Accent,
                    Color.FromArgb(43, 29, 13),
                    Color.FromArgb(185, 116, 38));
                _deleteButton = CreateEditorButton("삭제", SurfaceAlt, Danger, Danger);
                _deleteButton.Enabled = !string.IsNullOrWhiteSpace(note);
                actions.Controls.Add(cancelButton);
                actions.Controls.Add(saveButton);
                actions.Controls.Add(_deleteButton);
                root.Controls.Add(actions, 0, 3);

                AcceptButton = saveButton;
                CancelButton = cancelButton;
                cancelButton.DialogResult = DialogResult.Cancel;
                saveButton.Click += delegate { PersistNote(_noteTextBox.Text, NoteEditorAction.Saved); };
                _deleteButton.Click += delegate { PersistNote(null, NoteEditorAction.Deleted); };
                Shown += delegate
                {
                    _noteTextBox.SelectionStart = _noteTextBox.TextLength;
                };
            }

            public NoteEditorAction Action { get; private set; }

            private static Button CreateEditorButton(
                string text,
                Color background,
                Color foreground,
                Color border)
            {
                var button = new Button
                {
                    Text = text,
                    Size = new Size(78, 32),
                    Margin = new Padding(8, 0, 0, 0),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = background,
                    ForeColor = foreground,
                    Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    TabStop = true
                };
                button.FlatAppearance.BorderColor = border;
                button.FlatAppearance.MouseOverBackColor = background == Accent
                    ? Color.FromArgb(242, 170, 72)
                    : Color.FromArgb(45, 54, 64);
                return button;
            }

            private void PersistNote(string note, NoteEditorAction action)
            {
                if (!BlockedServerMetadataStore.UpdateNote(_ipAddress, note))
                {
                    _statusLabel.ForeColor = Danger;
                    _statusLabel.Text = action == NoteEditorAction.Deleted
                        ? "차단 메모를 삭제하지 못했습니다."
                        : "차단 메모를 저장하지 못했습니다.";
                    return;
                }

                Action = action;
                DialogResult = DialogResult.OK;
                Close();
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
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
            header.Controls.Add(new Label
            {
                Name = "firewallPersistenceNotice",
                Dock = DockStyle.Bottom,
                Height = 38,
                Text = FirewallPersistenceNotice.FullText,
                Font = new Font("Malgun Gothic", 8F),
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false,
                AccessibleName = "앱 종료와 차단 리스트 유지 안내",
                AccessibleDescription = FirewallPersistenceNotice.FullText
            });
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
            _exportButton = CreateButton("내보내기", false);
            _importButton = CreateButton("불러오기", false);
            actions.Controls.Add(_closeButton);
            actions.Controls.Add(_removeAllButton);
            actions.Controls.Add(_removeSelectedButton);
            actions.Controls.Add(_refreshButton);
            actions.Controls.Add(_exportButton);
            actions.Controls.Add(_importButton);
            root.Controls.Add(actions, 0, 3);

            _closeButton.Click += delegate { Close(); };
            _refreshButton.Click += async delegate { await RefreshRulesAsync(); };
            _removeSelectedButton.Click += async delegate { await RemoveSelectedAsync(); };
            _removeAllButton.Click += async delegate { await RemoveAllAsync(); };
            _exportButton.Click += async delegate { await ExportBackupAsync(); };
            _importButton.Click += async delegate { await ImportBackupAsync(); };
            _grid.CellContentClick += GridCellContentClick;
            _grid.CellMouseClick += GridCellMouseClick;
            _grid.CellPainting += GridCellPainting;
            _grid.CellMouseMove += GridCellMouseMove;
            _grid.MouseLeave += delegate { _grid.Cursor = Cursors.Default; };
            _grid.KeyDown += GridKeyDown;
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += delegate { UpdateButtons(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!_busy || e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                _statusLabel.ForeColor = Color.FromArgb(231, 184, 73);
                _statusLabel.Text = "방화벽·파일 작업과 최종 확인이 끝난 뒤 창을 닫을 수 있습니다.";
            };
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
                Name = "note",
                HeaderText = "메모",
                ReadOnly = true,
                Width = 58,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Surface,
                    ForeColor = NoteOrange,
                    SelectionBackColor = Color.FromArgb(48, 59, 70),
                    SelectionForeColor = NoteOrange,
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    Padding = new Padding(0)
                }
            });
            grid.Columns["note"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["note"].HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            grid.Columns["note"].HeaderCell.Style.Padding = new Padding(0);
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
            DataGridViewColumn noteColumn = grid.Columns["note"];
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
            noteColumn.Width = Math.Max(58, (int)Math.Round(58F * scale));

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
                    string.Empty,
                    server.RuleKindText,
                    "해제");
                DataGridViewRow row = _grid.Rows[index];
                row.Tag = server;
                row.Cells["status"].Style.ForeColor = Danger;
                UpdateNoteCell(row, metadata == null ? null : metadata.Note);
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

        private void GridCellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_busy || e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (_grid.Columns[e.ColumnIndex].Name == "note")
                ShowNoteEditor(_grid.Rows[e.RowIndex]);
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (_busy || _grid.CurrentCell == null
                || _grid.CurrentCell.RowIndex < 0
                || _grid.CurrentCell.OwningColumn == null
                || _grid.CurrentCell.OwningColumn.Name != "note"
                || (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space))
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            ShowNoteEditor(_grid.Rows[_grid.CurrentCell.RowIndex]);
        }

        private void GridCellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            bool interactive = !_busy && e.RowIndex >= 0 && e.ColumnIndex >= 0
                && (_grid.Columns[e.ColumnIndex].Name == "note"
                    || _grid.Columns[e.ColumnIndex].Name == "remove");
            _grid.Cursor = interactive ? Cursors.Hand : Cursors.Default;
        }

        private void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0
                || _grid.Columns[e.ColumnIndex].Name != "note")
                return;

            Rectangle clip = Rectangle.Intersect(e.CellBounds, e.ClipBounds);
            if (clip.Width <= 0 || clip.Height <= 0)
            {
                e.Handled = true;
                return;
            }

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(clip);
                e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
                DataGridViewCell cell = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                bool hasNote = cell.Tag is bool && (bool)cell.Tag;
                float scale = Math.Max(1F, e.Graphics.DpiX / 96F);
                int iconWidth = Math.Max(16, (int)Math.Round(18F * scale));
                int iconHeight = Math.Max(14, (int)Math.Round(16F * scale));
                Rectangle iconBounds = new Rectangle(
                    e.CellBounds.Left + (e.CellBounds.Width - iconWidth) / 2,
                    e.CellBounds.Top + (e.CellBounds.Height - iconHeight) / 2,
                    iconWidth,
                    iconHeight);
                using (GraphicsPath bubble = CreateSpeechBubblePath(iconBounds))
                {
                    SmoothingMode oldMode = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (hasNote)
                    {
                        using (var brush = new SolidBrush(NoteOrange))
                            e.Graphics.FillPath(brush, bubble);
                    }
                    else
                    {
                        using (var pen = new Pen(NoteOrange, Math.Max(1.4F, 1.5F * scale)))
                            e.Graphics.DrawPath(pen, bubble);
                    }
                    e.Graphics.SmoothingMode = oldMode;
                }
            }
            finally
            {
                e.Graphics.Restore(state);
                e.Handled = true;
            }
        }

        private static GraphicsPath CreateSpeechBubblePath(Rectangle bounds)
        {
            int radius = Math.Max(2, bounds.Width / 6);
            int tail = Math.Max(3, bounds.Height / 4);
            int left = bounds.Left;
            int top = bounds.Top;
            int right = bounds.Right - 1;
            int bodyBottom = bounds.Bottom - tail - 1;
            int diameter = radius * 2;
            int center = left + bounds.Width / 2;

            var path = new GraphicsPath();
            path.StartFigure();
            path.AddLine(left + radius, top, right - radius, top);
            path.AddArc(right - diameter, top, diameter, diameter, 270, 90);
            path.AddLine(right, top + radius, right, bodyBottom - radius);
            path.AddArc(right - diameter, bodyBottom - diameter, diameter, diameter, 0, 90);
            path.AddLine(right - radius, bodyBottom, center + 2, bodyBottom);
            path.AddLine(center + 2, bodyBottom, center - 3, bounds.Bottom - 1);
            path.AddLine(center - 3, bounds.Bottom - 1, center - 4, bodyBottom);
            path.AddLine(center - 4, bodyBottom, left + radius, bodyBottom);
            path.AddArc(left, bodyBottom - diameter, diameter, diameter, 90, 90);
            path.AddLine(left, bodyBottom - radius, left, top + radius);
            path.AddArc(left, top, diameter, diameter, 180, 90);
            path.CloseFigure();
            return path;
        }

        private void ShowNoteEditor(DataGridViewRow row)
        {
            ManagedBlockedServer server = row == null ? null : row.Tag as ManagedBlockedServer;
            if (server == null) return;

            IDictionary<string, BlockedServerMetadata> records = BlockedServerMetadataStore.LoadAll();
            BlockedServerMetadata metadata;
            records.TryGetValue(server.IpAddress, out metadata);
            using (var form = new BlockNoteEditorForm(
                server.IpAddress,
                metadata == null ? null : metadata.Note))
            {
                if (form.ShowDialog(this) != DialogResult.OK
                    || form.Action == NoteEditorAction.None)
                    return;
            }

            records = BlockedServerMetadataStore.LoadAll();
            records.TryGetValue(server.IpAddress, out metadata);
            UpdateNoteCell(row, metadata == null ? null : metadata.Note);
            _statusLabel.ForeColor = Success;
            _statusLabel.Text = metadata == null || string.IsNullOrWhiteSpace(metadata.Note)
                ? server.IpAddress + " 서버의 차단 메모를 삭제했습니다."
                : server.IpAddress + " 서버의 차단 메모를 저장했습니다.";
        }

        private void UpdateNoteCell(DataGridViewRow row, string note)
        {
            if (row == null || !_grid.Columns.Contains("note")) return;
            bool hasNote = !string.IsNullOrWhiteSpace(note);
            DataGridViewCell cell = row.Cells["note"];
            cell.Value = hasNote ? "저장된 차단 메모" : "차단 메모 추가";
            cell.Tag = hasNote;
            cell.ToolTipText = hasNote ? note : "차단 메모를 추가합니다.";
            cell.Style.ForeColor = NoteOrange;
            cell.Style.SelectionForeColor = NoteOrange;
            _grid.InvalidateCell(cell);
        }

        private async Task ExportBackupAsync()
        {
            if (_busy) return;
            string selectedPath;
            using (var dialog = new SaveFileDialog
            {
                Title = "차단 목록 내보내기",
                Filter = "Tarkov Server Guard 차단 목록 (*.json)|*.json|모든 파일 (*.*)|*.*",
                DefaultExt = "json",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = "TarkovServerGuard-blocked-servers-"
                    + DateTime.Now.ToString("yyyyMMdd") + ".json"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selectedPath = dialog.FileName;
            }

            SetBusy(true, "Windows 방화벽의 실제 관리 규칙을 확인하는 중…");
            ManagedBlockedServerQueryResult query = await Task.Run(
                () => FirewallRuleManager.QueryManagedBlockedServers());
            if (IsDisposed) return;
            if (!query.Success)
            {
                SetBusy(false, query.ErrorMessage ?? "현재 차단 규칙을 확인하지 못했습니다.");
                MessageBox.Show(this, _statusLabel.Text, "차단 목록 내보내기",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            IDictionary<string, BlockedServerMetadata> metadata = await Task.Run(
                () => BlockedServerMetadataStore.LoadAll());
            BlockedServerBackupExportResult export = await Task.Run(
                () => BlockedServerBackupService.CreateExport(query.Servers, metadata));
            if (IsDisposed) return;
            if (!export.Success)
            {
                SetBusy(false, export.ErrorMessage ?? "차단 목록 백업을 만들지 못했습니다.");
                MessageBox.Show(this, _statusLabel.Text, "차단 목록 내보내기",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (export.Entries.Count == 0)
            {
                SetBusy(false, "현재 백업할 복원 가능한 앱 관리 차단 규칙이 없어 파일을 저장하지 않았습니다.");
                MessageBox.Show(this, _statusLabel.Text, "차단 목록 내보내기",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                byte[] bytes = export.Utf8Bytes;
                await Task.Run(() => BlockedServerBackupFile.WriteAtomic(selectedPath, bytes));
            }
            catch (Exception ex)
            {
                SetBusy(false, "백업 파일 저장 실패: " + ex.Message);
                MessageBox.Show(this, _statusLabel.Text, "차단 목록 내보내기",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (IsDisposed) return;

            string status = string.Format("차단 서버 {0}개를 백업 파일로 저장했습니다.", export.Entries.Count);
            if (export.ExcludedAddresses.Count > 0)
                status += string.Format(" 공인 IPv4가 아닌 관리 규칙 {0}개는 제외했습니다.",
                    export.ExcludedAddresses.Count);
            SetBusy(false, status);
            _statusLabel.ForeColor = export.ExcludedAddresses.Count == 0 ? Success : Color.FromArgb(231, 184, 73);
        }

        private async Task ImportBackupAsync()
        {
            if (_busy) return;
            string selectedPath;
            using (var dialog = new OpenFileDialog
            {
                Title = "차단 목록 불러오기",
                Filter = "Tarkov Server Guard 차단 목록 (*.json)|*.json|모든 파일 (*.*)|*.*",
                DefaultExt = "json",
                CheckFileExists = true,
                Multiselect = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selectedPath = dialog.FileName;
            }

            SetBusy(true, "백업 파일의 형식·크기·주소를 검증하는 중…");
            BlockedServerBackupParseResult parsed;
            try
            {
                parsed = await Task.Run(() =>
                    BlockedServerBackupService.Parse(ReadBackupFileBytes(selectedPath)));
            }
            catch (Exception ex)
            {
                SetBusy(false, "백업 파일을 읽지 못했습니다: " + ex.Message);
                MessageBox.Show(this, _statusLabel.Text, "차단 목록 불러오기",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (IsDisposed) return;
            if (!parsed.Success)
            {
                SetBusy(false, parsed.ErrorMessage ?? "지원하지 않는 차단 목록 백업입니다.");
                MessageBox.Show(this, _statusLabel.Text, "차단 목록 불러오기",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string[] eligibleAddresses = parsed.Items
                .Where(item => item.IsEligible)
                .Select(item => item.Entry.IpAddress)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Dictionary<string, FirewallQueryResult> states = eligibleAddresses.Length == 0
                ? new Dictionary<string, FirewallQueryResult>(StringComparer.OrdinalIgnoreCase)
                : await Task.Run(() => FirewallRuleManager.QueryMany(eligibleAddresses));
            if (IsDisposed) return;

            IList<BlockedServerRestoreItem> preview = BlockedServerBackupService.CreateRestorePreview(
                parsed,
                states);
            int newCount = preview.Count(item => item.Status == BlockedServerRestoreStatus.NewBlock);
            int existingCount = preview.Count(
                item => item.Status == BlockedServerRestoreStatus.AlreadyBlocked);
            int excludedCount = preview.Count(item => item.Status == BlockedServerRestoreStatus.Excluded);
            SetBusy(false, string.Format(
                "새로 차단 {0}개 · 이미 차단됨 {1}개 · 적용 제외 {2}개를 확인했습니다.",
                newCount,
                existingCount,
                excludedCount));

            IList<BlockedServerRestoreItem> selected;
            using (var form = new BlockedServerRestorePreviewForm(preview))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                selected = form.SelectedItems.ToList();
            }
            if (selected.Count == 0) return;
            await ApplyRestoreAsync(selected);
        }

        private async Task ApplyRestoreAsync(IList<BlockedServerRestoreItem> selected)
        {
            IList<BlockedServerRestoreItem> newItems = selected
                .Where(item => item.Status == BlockedServerRestoreStatus.NewBlock)
                .ToList();
            SetBusy(true, newItems.Count == 0
                ? "이미 차단된 서버의 백업 정보를 최종 확인하는 중…"
                : string.Format("선택한 {0}개 서버를 한 번의 관리자 권한 요청으로 차단하는 중…",
                    newItems.Count));

            var batch = new FirewallBatchChangeResult { Success = true };
            if (newItems.Count > 0)
            {
                batch = await FirewallRuleManager.AddManyWithElevationAsync(
                    newItems.Select(item => item.Entry.IpAddress));
                if (IsDisposed) return;
                if (batch.Cancelled)
                {
                    SetBusy(false, batch.ErrorMessage ?? "관리자 권한 요청이 취소되었습니다.");
                    return;
                }
            }

            string[] selectedAddresses = selected
                .Select(item => item.Entry.IpAddress)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Dictionary<string, FirewallQueryResult> finalStates = await Task.Run(
                () => FirewallRuleManager.QueryMany(selectedAddresses));
            if (IsDisposed) return;

            var verified = new List<BlockedServerRestoreItem>();
            var failures = new List<KeyValuePair<string, string>>();
            var batchErrors = batch.Items
                .Where(item => item != null && !item.Success)
                .GroupBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().ErrorMessage,
                    StringComparer.OrdinalIgnoreCase);
            foreach (BlockedServerRestoreItem item in selected)
            {
                FirewallQueryResult state;
                if (finalStates.TryGetValue(item.Entry.IpAddress, out state)
                    && state != null
                    && state.Success
                    && state.IsBlocked)
                {
                    verified.Add(item);
                    continue;
                }

                string error;
                if (!batchErrors.TryGetValue(item.Entry.IpAddress, out error))
                    error = state == null || string.IsNullOrWhiteSpace(state.ErrorMessage)
                        ? "최종 앱 관리 차단 규칙이 확인되지 않았습니다."
                        : state.ErrorMessage;
                failures.Add(new KeyValuePair<string, string>(item.Entry.IpAddress, error));
            }

            bool metadataSaved = await Task.Run(() =>
                BlockedServerMetadataStore.MergeMissingFromBackup(
                    verified.Where(item => item.Entry.HasMetadata).Select(item => item.Entry)));
            int newSucceeded = verified.Count(
                item => item.Status == BlockedServerRestoreStatus.NewBlock);
            if (newSucceeded > 0)
            {
                FirewallStateChanged = true;
                EventHandler handler = FirewallRulesChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            await RefreshRulesAfterChangeAsync();
            if (IsDisposed) return;
            _statusLabel.ForeColor = failures.Count == 0 && metadataSaved
                ? Success
                : Color.FromArgb(231, 184, 73);
            _statusLabel.Text = string.Format(
                "새 차단 {0}개 · 최종 확인 {1}개 · 실패 {2}개",
                newSucceeded,
                verified.Count,
                failures.Count);
            if (!metadataSaved) _statusLabel.Text += " · 백업 정보 저장 실패";

            if (failures.Count > 0)
            {
                using (var form = new BlockedServerRestoreFailuresForm(failures))
                    form.ShowDialog(this);
            }
            else if (!metadataSaved)
            {
                MessageBox.Show(
                    this,
                    "방화벽 차단은 최종 확인했지만 비어 있던 지역·메모·차단시각 일부를 저장하지 못했습니다.",
                    "차단 목록 복원 결과",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private static byte[] ReadBackupFileBytes(string path)
        {
            using (var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                if (stream.Length < 2)
                    throw new InvalidDataException("비어 있거나 올바르지 않은 백업 파일입니다.");
                if (stream.Length > BlockedServerBackupService.MaximumFileBytes)
                    throw new InvalidDataException("백업 파일 크기 한도를 초과했습니다.");

                var bytes = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < bytes.Length)
                {
                    int read = stream.Read(bytes, offset, bytes.Length - offset);
                    if (read <= 0) throw new EndOfStreamException("백업 파일을 끝까지 읽지 못했습니다.");
                    offset += read;
                }
                if (stream.ReadByte() >= 0)
                    throw new InvalidDataException("읽는 동안 백업 파일 크기가 변경되었습니다.");
                return bytes;
            }
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
            _exportButton.Enabled = !_busy && count > 0;
            _importButton.Enabled = !_busy;
            _closeButton.Enabled = !_busy;
        }
    }
}
