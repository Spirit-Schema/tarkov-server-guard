// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class RaidNoteArchiveForm : BrandedForm
    {
        internal const string BackupScreenshotNotice =
            "연결된 스크린샷 원본 파일은 백업에 포함되지 않으며, 로컬 이미지 연결 경로만 함께 저장됩니다. "
            + "경로에는 사용자명 등 개인정보가 포함될 수 있으므로 공유 전에 확인해 주세요. "
            + "다른 PC에서 같은 경로에 파일이 없으면 스크린샷을 열 수 없습니다.";

        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        // Match the server-block status header without changing archive body lines.
        private static readonly Color HeaderBorder = Color.FromArgb(57, 68, 80);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color Success = Color.FromArgb(78, 201, 134);
        private static readonly Color Warning = Color.FromArgb(247, 190, 79);
        private static readonly Color Danger = Color.FromArgb(192, 68, 75);

        private enum HeaderSelectionState
        {
            None,
            Partial,
            All
        }

        private readonly RaidNoteStore _store;
        private readonly UserReportMemoStore _userReportStore;
        private readonly List<ArchiveItem> _records = new List<ArchiveItem>();
        private DataGridView _grid;
        private Label _statusLabel;
        private Button _openButton;
        private Button _deleteButton;
        private Button _deleteSelectedButton;
        private Button _folderButton;
        private Button _refreshButton;
        private Button _exportButton;
        private Button _importButton;
        private bool _busy;
        private bool _updatingChecks;
        private string _archiveSortColumn;
        private SortOrder _archiveSortOrder = SortOrder.None;

        public RaidNoteArchiveForm(RaidNoteStore store)
            : this(store, new UserReportMemoStore())
        {
        }

        public RaidNoteArchiveForm(RaidNoteStore store, UserReportMemoStore userReportStore)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (userReportStore == null) throw new ArgumentNullException("userReportStore");
            _store = store;
            _userReportStore = userReportStore;
            InitializeWindow();
            BuildInterface();
            Shown += delegate { RefreshRecords(); };
        }

        public bool Changed { get; private set; }

        private void InitializeWindow()
        {
            Text = "메모 보관함";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1220, 600);
            MinimumSize = new Size(820, 460);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowInTaskbar = false;
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (_busy && args.CloseReason == CloseReason.UserClosing)
                    args.Cancel = true;
            };
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Padding = new Padding(18, 14, 18, 14),
                ColumnCount = 1,
                RowCount = 3
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildGrid(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
        }

        private Control BuildHeader()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204F));

            var textPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = Padding.Empty
            };
            textPanel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(1, 0),
                Text = "메모 보관함",
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            textPanel.Controls.Add(new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 26,
                Padding = new Padding(3, 0, 5, 0),
                Text = "게임 로그가 삭제된 뒤에도 일반 레이드 메모와 유저신고 메모를 확인할 수 있습니다.",
                Font = new Font("Malgun Gothic", 8.5F),
                ForeColor = TextMuted
            });
            layout.Controls.Add(textPanel, 0, 0);

            var backupButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = new Padding(0, 7, 0, 0)
            };
            _importButton = CreateButton("불러오기", 96, SurfaceAlt, TextPrimary);
            _importButton.Name = "MemoBackupImportButton";
            _importButton.AccessibleName = "메모 백업 불러오기";
            _importButton.AccessibleDescription =
                "레이드 메모와 유저신고 메모 백업을 검증하고, 없는 메모만 복원합니다. "
                + "스크린샷 원본 파일 없이 검증된 로컬 이미지 연결 경로만 복원합니다.";
            _importButton.Click += async delegate { await ImportBackupAsync(); };
            _exportButton = CreateButton("내보내기", 96, SurfaceAlt, TextPrimary);
            _exportButton.Name = "MemoBackupExportButton";
            _exportButton.AccessibleName = "메모 통합 백업 내보내기";
            _exportButton.AccessibleDescription =
                "레이드 메모와 유저신고 메모를 하나의 JSON 파일로 저장합니다. "
                + BackupScreenshotNotice;
            _exportButton.Click += async delegate { await ExportBackupAsync(); };
            backupButtons.Controls.Add(_importButton);
            backupButtons.Controls.Add(_exportButton);
            layout.Controls.Add(backupButtons, 1, 0);
            return layout;
        }

        private Control BuildGrid()
        {
            _grid = new ArchiveDataGridView
            {
                Dock = DockStyle.Fill,
                AccessibleName = "메모 선택 목록",
                AccessibleDescription = "메모 선택 목록입니다. 전체 메모 0개 중 0개가 선택되었습니다. "
                    + "선택 열 머리글은 전체 선택 또는 전체 해제이며, Ctrl+A는 전체 선택입니다.",
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                ColumnHeadersHeight = 34,
                RowTemplate = { Height = 36 }
            };
            _grid.DefaultCellStyle.BackColor = Surface;
            _grid.DefaultCellStyle.ForeColor = TextPrimary;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(56, 65, 76);
            _grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            _grid.DefaultCellStyle.Font = new Font("Malgun Gothic", 8.5F);
            _grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
            _grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "selected",
                HeaderCell = new ArchiveSelectionHeaderCell
                {
                    ToolTipText = "모든 메모를 선택하거나 전체 해제합니다."
                },
                HeaderText = string.Empty,
                CellTemplate = new ArchiveSelectionCheckBoxCell(),
                Width = 54,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = false
            });
            _grid.Columns.Add(CreateColumn("kind", "종류", 104));
            _grid.Columns.Add(CreateColumn("date", "레이드 시각", 145));
            _grid.Columns.Add(CreateColumn("game", "게임", 64));
            _grid.Columns.Add(CreateColumn("map", "맵 · 게임유형", 180));
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "preview",
                HeaderText = "메모 미리보기",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 220,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                ReadOnly = true
            });
            _grid.Columns.Add(CreateColumn("tags", "태그/신고", 130));
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "updated",
                HeaderText = "최근 수정",
                Width = 150,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                ReadOnly = true
            });
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                if (column.SortMode == DataGridViewColumnSortMode.Programmatic)
                    column.HeaderCell.ToolTipText =
                        "클릭할 때마다 오름차순, 내림차순, 기본 순서로 정렬합니다.";
            }
            _grid.CellPainting += PaintGridHeaderBorder;
            _grid.SelectionChanged += delegate { UpdateButtons(); };
            _grid.CurrentCellDirtyStateChanged += delegate
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += GridCellValueChanged;
            _grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
            _grid.KeyDown += GridKeyDown;
            _grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex >= 0
                    && args.ColumnIndex >= 0
                    && _grid.Columns[args.ColumnIndex].Name != "selected")
                    OpenSelected();
            };
            return _grid;
        }

        private void PaintGridHeaderBorder(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0) return;

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
                e.Paint(
                    e.ClipBounds,
                    e.PaintParts
                    & ~DataGridViewPaintParts.Border
                    & ~DataGridViewPaintParts.ContentForeground);

                DataGridViewColumn column = _grid.Columns[e.ColumnIndex];
                bool activeSort = column.SortMode == DataGridViewColumnSortMode.Programmatic
                    && string.Equals(_archiveSortColumn, column.Name, StringComparison.Ordinal)
                    && _archiveSortOrder != SortOrder.None;
                float scale = Math.Max(1F, e.Graphics.DpiX / 96F);
                int horizontalPadding = Math.Max(4, (int)Math.Round(4F * scale));
                int arrowWidth = Math.Max(7, (int)Math.Round(7F * scale));
                int arrowHeight = Math.Max(5, (int)Math.Round(5F * scale));
                int arrowGap = Math.Max(4, (int)Math.Round(4F * scale));
                Rectangle textBounds = new Rectangle(
                    e.CellBounds.Left + horizontalPadding,
                    e.CellBounds.Top + 2,
                    Math.Max(1, e.CellBounds.Width - horizontalPadding * 2),
                    Math.Max(1, e.CellBounds.Height - 4));
                Rectangle arrowBounds = Rectangle.Empty;
                if (activeSort)
                {
                    arrowBounds = new Rectangle(
                        e.CellBounds.Right - horizontalPadding - arrowWidth,
                        e.CellBounds.Top + (e.CellBounds.Height - arrowHeight) / 2,
                        arrowWidth,
                        arrowHeight);
                    textBounds.Width = Math.Max(
                        1,
                        arrowBounds.Left - arrowGap - textBounds.Left);
                }

                TextFormatFlags textFlags = TextFormatFlags.NoPrefix
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.PreserveGraphicsClipping;
                switch (e.CellStyle.Alignment)
                {
                    case DataGridViewContentAlignment.BottomCenter:
                    case DataGridViewContentAlignment.MiddleCenter:
                    case DataGridViewContentAlignment.TopCenter:
                        textFlags |= TextFormatFlags.HorizontalCenter;
                        break;
                    case DataGridViewContentAlignment.BottomRight:
                    case DataGridViewContentAlignment.MiddleRight:
                    case DataGridViewContentAlignment.TopRight:
                        textFlags |= TextFormatFlags.Right;
                        break;
                    default:
                        textFlags |= TextFormatFlags.Left;
                        break;
                }
                textFlags |= e.CellStyle.WrapMode == DataGridViewTriState.True
                    ? TextFormatFlags.WordBreak
                    : TextFormatFlags.SingleLine;
                TextRenderer.DrawText(
                    e.Graphics,
                    Convert.ToString(e.FormattedValue) ?? string.Empty,
                    e.CellStyle.Font,
                    textBounds,
                    e.CellStyle.ForeColor,
                    textFlags);

                if (column.Name == "selected")
                {
                    PaintSelectionHeaderCheckBox(
                        e.Graphics,
                        e.CellBounds,
                        GetSelectionHeaderState(_grid),
                        _grid.Enabled);
                }

                if (activeSort)
                {
                    Point[] points = _archiveSortOrder == SortOrder.Ascending
                        ? new[]
                        {
                            new Point(arrowBounds.Left + arrowBounds.Width / 2, arrowBounds.Top),
                            new Point(arrowBounds.Left, arrowBounds.Bottom - 1),
                            new Point(arrowBounds.Right - 1, arrowBounds.Bottom - 1)
                        }
                        : new[]
                        {
                            new Point(arrowBounds.Left, arrowBounds.Top),
                            new Point(arrowBounds.Right - 1, arrowBounds.Top),
                            new Point(arrowBounds.Left + arrowBounds.Width / 2, arrowBounds.Bottom - 1)
                        };
                    using (var arrowBrush = new SolidBrush(Accent))
                        e.Graphics.FillPolygon(arrowBrush, points);
                }
                using (var border = new Pen(HeaderBorder))
                {
                    int right = e.CellBounds.Right - 1;
                    int bottom = e.CellBounds.Bottom - 1;
                    DataGridViewAdvancedBorderStyle edges = e.AdvancedBorderStyle;
                    if (edges.Left != DataGridViewAdvancedCellBorderStyle.None)
                        e.Graphics.DrawLine(border, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Left, bottom);
                    if (edges.Top != DataGridViewAdvancedCellBorderStyle.None)
                        e.Graphics.DrawLine(border, e.CellBounds.Left, e.CellBounds.Top, right, e.CellBounds.Top);
                    if (edges.Right != DataGridViewAdvancedCellBorderStyle.None)
                        e.Graphics.DrawLine(border, right, e.CellBounds.Top, right, bottom);
                    if (edges.Bottom != DataGridViewAdvancedCellBorderStyle.None)
                        e.Graphics.DrawLine(border, e.CellBounds.Left, bottom, right, bottom);
                }
            }
            finally
            {
                e.Graphics.Restore(state);
                e.Handled = true;
            }
        }

        private static HeaderSelectionState GetSelectionHeaderState(DataGridView grid)
        {
            if (grid == null || grid.Rows.Count == 0) return HeaderSelectionState.None;
            int selectedCount = grid.Rows.Cast<DataGridViewRow>().Count(IsRowChecked);
            if (selectedCount == 0) return HeaderSelectionState.None;
            return selectedCount == grid.Rows.Count
                ? HeaderSelectionState.All
                : HeaderSelectionState.Partial;
        }

        private static void PaintSelectionHeaderCheckBox(
            Graphics graphics,
            Rectangle cellBounds,
            HeaderSelectionState state,
            bool enabled)
        {
            float scale = Math.Max(1F, graphics.DpiX / 96F);
            int size = Math.Max(15, (int)Math.Round(16F * scale));
            Rectangle box = new Rectangle(
                cellBounds.Left + (cellBounds.Width - size) / 2,
                cellBounds.Top + (cellBounds.Height - size) / 2,
                size,
                size);
            Color outlineColor = enabled ? TextMuted : Border;
            Color fillColor = state == HeaderSelectionState.None
                ? Surface
                : enabled ? Accent : Border;
            using (var fill = new SolidBrush(fillColor))
            using (var outline = new Pen(outlineColor, Math.Max(1F, scale)))
            {
                graphics.FillRectangle(fill, box);
                graphics.DrawRectangle(
                    outline,
                    box.Left,
                    box.Top,
                    box.Width - 1,
                    box.Height - 1);
            }

            if (state == HeaderSelectionState.None) return;
            Color markColor = enabled ? Background : TextMuted;
            SmoothingMode oldMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var mark = new Pen(
                    markColor,
                    Math.Max(2F, 2.2F * scale)))
                {
                    mark.StartCap = LineCap.Round;
                    mark.EndCap = LineCap.Round;
                    if (state == HeaderSelectionState.Partial)
                    {
                        int y = box.Top + box.Height / 2;
                        graphics.DrawLine(
                            mark,
                            box.Left + Math.Max(3, size / 4),
                            y,
                            box.Right - Math.Max(3, size / 4) - 1,
                            y);
                    }
                    else
                    {
                        graphics.DrawLines(
                            mark,
                            new[]
                            {
                                new Point(box.Left + size / 5, box.Top + size / 2),
                                new Point(box.Left + size * 2 / 5, box.Top + size * 7 / 10),
                                new Point(box.Left + size * 4 / 5, box.Top + size * 3 / 10)
                            });
                    }
                }
            }
            finally
            {
                graphics.SmoothingMode = oldMode;
            }
        }

        private Control BuildFooter()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Padding = new Padding(0, 9, 0, 0),
                ColumnCount = 2,
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Text = "메모를 불러오는 중…"
            };
            layout.Controls.Add(_statusLabel, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                BackColor = Background,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            _openButton = CreateButton("열기", 82, Accent, Color.FromArgb(29, 24, 17));
            _openButton.Click += delegate { OpenSelected(); };
            _deleteButton = CreateButton("삭제", 82, Danger, Color.White);
            _deleteButton.AccessibleName = "현재 메모 삭제";
            _deleteButton.AccessibleDescription = "현재 행에 포커스된 메모 1개를 삭제합니다.";
            _deleteButton.Click += delegate { DeleteSelected(); };
            _deleteSelectedButton = CreateButton("선택 삭제", 100, Danger, Color.White);
            _deleteSelectedButton.AccessibleName = "선택 삭제";
            _deleteSelectedButton.AccessibleDescription = "체크박스로 선택한 메모가 없습니다.";
            _deleteSelectedButton.Click += delegate { DeleteCheckedRecords(); };
            _folderButton = CreateButton("저장 폴더 열기", 126, SurfaceAlt, TextPrimary);
            _folderButton.Click += delegate { OpenFolder(); };
            _refreshButton = CreateButton("새로고침", 90, SurfaceAlt, TextPrimary);
            _refreshButton.Click += delegate { RefreshRecords(); };
            buttons.Controls.Add(_openButton);
            buttons.Controls.Add(_deleteButton);
            buttons.Controls.Add(_deleteSelectedButton);
            buttons.Controls.Add(_folderButton);
            buttons.Controls.Add(_refreshButton);
            layout.Controls.Add(buttons, 1, 0);
            UpdateButtons();
            return layout;
        }

        private static DataGridViewTextBoxColumn CreateColumn(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                ReadOnly = true
            };
        }

        private static Button CreateButton(string text, int width, Color backColor, Color foreColor)
        {
            var button = new ArchiveButton
            {
                Dock = DockStyle.None,
                Size = new Size(width, 31),
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                Margin = new Padding(0, 0, 6, 0)
            };
            button.FlatAppearance.BorderColor = backColor == SurfaceAlt ? Border : backColor;
            return button;
        }

        private void GridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_updatingChecks || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name == "selected") UpdateButtons();
        }

        private void GridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_busy || e.Button != MouseButtons.Left || e.ColumnIndex < 0) return;
            DataGridViewColumn column = _grid.Columns[e.ColumnIndex];
            if (column.Name == "selected")
            {
                CommitCurrentCheckBoxEdit();
                bool checkAll = _grid.Rows.Cast<DataGridViewRow>().Any(row => !IsRowChecked(row));
                SetAllRowsChecked(checkAll);
                return;
            }
            if (column.SortMode != DataGridViewColumnSortMode.Programmatic) return;

            CommitCurrentCheckBoxEdit();
            if (string.Equals(_archiveSortColumn, column.Name, StringComparison.Ordinal))
            {
                if (_archiveSortOrder == SortOrder.Ascending)
                {
                    _archiveSortOrder = SortOrder.Descending;
                }
                else
                {
                    _archiveSortColumn = null;
                    _archiveSortOrder = SortOrder.None;
                }
            }
            else
            {
                _archiveSortColumn = column.Name;
                _archiveSortOrder = SortOrder.Ascending;
            }
            ApplyArchiveSort();
        }

        private void ApplyArchiveSort()
        {
            if (_grid == null) return;
            if (_grid.Rows.Count > 1)
                _grid.Sort(new ArchiveRowComparer(this));
            foreach (DataGridViewColumn column in _grid.Columns)
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            _grid.Invalidate();
        }

        private int CompareArchiveRows(DataGridViewRow leftRow, DataGridViewRow rightRow)
        {
            ArchiveItem left = leftRow == null ? null : leftRow.Tag as ArchiveItem;
            ArchiveItem right = rightRow == null ? null : rightRow.Tag as ArchiveItem;
            if (left == null || right == null)
                return left == null ? (right == null ? 0 : 1) : -1;
            if (string.IsNullOrWhiteSpace(_archiveSortColumn)
                || _archiveSortOrder == SortOrder.None)
                return ArchiveItem.CompareNewestFirst(left, right);

            bool leftKnown;
            bool rightKnown;
            int primary = CompareArchiveSortValue(
                leftRow,
                rightRow,
                left,
                right,
                out leftKnown,
                out rightKnown);
            if (leftKnown != rightKnown) return leftKnown ? -1 : 1;
            if (leftKnown && primary != 0)
            {
                int normalized = primary < 0 ? -1 : 1;
                return _archiveSortOrder == SortOrder.Descending
                    ? -normalized
                    : normalized;
            }
            return ArchiveItem.CompareNewestFirst(left, right);
        }

        private int CompareArchiveSortValue(
            DataGridViewRow leftRow,
            DataGridViewRow rightRow,
            ArchiveItem left,
            ArchiveItem right,
            out bool leftKnown,
            out bool rightKnown)
        {
            leftKnown = false;
            rightKnown = false;
            switch (_archiveSortColumn)
            {
                case "date":
                    leftKnown = left.RaidStartedUtc != default(DateTime);
                    rightKnown = right.RaidStartedUtc != default(DateTime);
                    return DateTime.Compare(left.RaidStartedUtc, right.RaidStartedUtc);
                case "updated":
                    leftKnown = left.UpdatedUtc != default(DateTime);
                    rightKnown = right.UpdatedUtc != default(DateTime);
                    return DateTime.Compare(left.UpdatedUtc, right.UpdatedUtc);
                default:
                    string leftText = GetArchiveSortText(leftRow, _archiveSortColumn);
                    string rightText = GetArchiveSortText(rightRow, _archiveSortColumn);
                    leftKnown = !string.IsNullOrWhiteSpace(leftText) && leftText != "-";
                    rightKnown = !string.IsNullOrWhiteSpace(rightText) && rightText != "-";
                    return StringComparer.CurrentCultureIgnoreCase.Compare(leftText, rightText);
            }
        }

        private static string GetArchiveSortText(DataGridViewRow row, string columnName)
        {
            if (row == null || row.DataGridView == null
                || string.IsNullOrWhiteSpace(columnName)
                || !row.DataGridView.Columns.Contains(columnName))
                return string.Empty;
            return Convert.ToString(row.Cells[columnName].Value) ?? string.Empty;
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (_busy) return;
            if (e.Control && e.KeyCode == Keys.A)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SetAllRowsChecked(true);
                return;
            }
            if (e.Modifiers != Keys.None || e.KeyCode != Keys.Space
                || _grid.CurrentCell == null || _grid.CurrentCell.RowIndex < 0)
                return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            DataGridViewRow row = _grid.Rows[_grid.CurrentCell.RowIndex];
            SetRowChecked(row, !IsRowChecked(row));
        }

        private void SetAllRowsChecked(bool isChecked)
        {
            CommitCurrentCheckBoxEdit();
            _updatingChecks = true;
            try
            {
                foreach (DataGridViewRow row in _grid.Rows) SetRowCheckedValue(row, isChecked);
            }
            finally
            {
                _updatingChecks = false;
            }
            UpdateButtons();
            RedrawCheckedCellDisplay();
        }

        private void CommitCurrentCheckBoxEdit()
        {
            if (_grid == null) return;
            if (_grid.IsCurrentCellDirty
                && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
        }

        private void RedrawCheckedCellDisplay()
        {
            if (_grid == null || !_grid.Columns.Contains("selected")) return;
            _grid.InvalidateColumn(_grid.Columns["selected"].Index);
            if (_grid.CurrentCell != null) _grid.InvalidateCell(_grid.CurrentCell);
            _grid.Update();
        }

        private void SetRowChecked(DataGridViewRow row, bool isChecked)
        {
            _updatingChecks = true;
            try { SetRowCheckedValue(row, isChecked); }
            finally { _updatingChecks = false; }
            UpdateButtons();
        }

        private static void SetRowCheckedValue(DataGridViewRow row, bool isChecked)
        {
            if (row == null || row.DataGridView == null
                || !row.DataGridView.Columns.Contains("selected"))
                return;
            row.Cells["selected"].Value = isChecked;
        }

        private static bool IsRowChecked(DataGridViewRow row)
        {
            return row != null
                && row.DataGridView != null
                && row.DataGridView.Columns.Contains("selected")
                && row.Cells["selected"].Value is bool
                && (bool)row.Cells["selected"].Value;
        }

        private HashSet<string> GetCheckedIdentities()
        {
            return new HashSet<string>(
                GetCheckedDeleteTargets().Select(target => target.Identity),
                StringComparer.OrdinalIgnoreCase);
        }

        private IList<ArchiveDeleteTarget> GetCheckedDeleteTargets()
        {
            var targets = new List<ArchiveDeleteTarget>();
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_grid == null) return targets;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (!IsRowChecked(row)) continue;
                ArchiveItem record = row.Tag as ArchiveItem;
                if (record == null || string.IsNullOrWhiteSpace(record.Key)) continue;
                var target = ArchiveDeleteTarget.For(record);
                if (identities.Add(target.Identity)) targets.Add(target);
            }
            return targets;
        }

        private void RefreshRecords()
        {
            if (_busy) return;
            HashSet<string> checkedIdentities = GetCheckedIdentities();
            ArchiveItem selected = GetSelectedRecord();
            string selectedIdentity = selected == null ? null : selected.Identity;
            SetBusy(true, "메모 목록을 새로고치는 중…");
            try { ReloadRecords(checkedIdentities, selectedIdentity); }
            finally { SetBusy(false, null); }
        }

        private bool ReloadRecords(HashSet<string> checkedIdentities, string selectedIdentity)
        {
            try
            {
                IList<RaidNoteRecord> raidNotes = _store.LoadAll();
                IList<UserReportMemoRecord> reportMemos = _userReportStore.LoadAll();
                _records.Clear();
                _records.AddRange(raidNotes.Select(ArchiveItem.ForRaidNote));
                _records.AddRange(reportMemos.Select(ArchiveItem.ForUserReport));
                _records.Sort(ArchiveItem.CompareNewestFirst);
                _updatingChecks = true;
                try
                {
                    _grid.Rows.Clear();
                    foreach (ArchiveItem record in _records)
                    {
                        int rowIndex = _grid.Rows.Add(
                            checkedIdentities != null && checkedIdentities.Contains(record.Identity),
                            record.KindLabel,
                            FormatDate(record.RaidStartedUtc),
                            EmptyFallback(record.Game),
                            BuildMapAndGameType(record.MapName, record.GameType),
                            BuildNotePreview(record.NoteText, record.IsUserReport),
                            record.IsUserReport
                                ? "신고 " + record.ReportCount + "건"
                                : Summarize(record.Tags, 4),
                            FormatDate(record.UpdatedUtc));
                        DataGridViewRow row = _grid.Rows[rowIndex];
                        row.Tag = record;
                        row.Cells["preview"].ToolTipText = BuildFullNoteToolTip(
                            record.NoteText, record.IsUserReport);
                        row.Cells["map"].ToolTipText = BuildMapAndGameType(
                            record.MapName,
                            record.GameType);
                        row.Cells["tags"].ToolTipText = record.IsUserReport
                            ? "유저신고 " + record.ReportCount + "건"
                            : JoinAll(record.Tags);
                        if (!string.IsNullOrWhiteSpace(selectedIdentity)
                            && string.Equals(record.Identity, selectedIdentity, StringComparison.OrdinalIgnoreCase))
                            row.Selected = true;
                    }
                }
                finally
                {
                    _updatingChecks = false;
                }
                ApplyArchiveSort();
                if (_grid.SelectedRows.Count == 0 && _grid.Rows.Count > 0)
                    _grid.Rows[0].Selected = true;
                if (_records.Count == 0)
                    SetStatus("저장된 메모가 없습니다.", TextMuted);
                else
                    SetStatus(
                        "저장된 메모 " + _records.Count + "개 · 레이드 "
                        + raidNotes.Count + "개 · 유저신고 " + reportMemos.Count + "개",
                        TextMuted);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("메모 목록을 불러오지 못했습니다: " + ex.Message, Danger);
                return false;
            }
        }

        private ArchiveItem GetSelectedRecord()
        {
            if (_grid == null || _grid.SelectedRows.Count == 0) return null;
            return _grid.SelectedRows[0].Tag as ArchiveItem;
        }

        private void OpenSelected()
        {
            if (_busy) return;
            ArchiveItem record = GetSelectedRecord();
            if (record == null) return;
            if (record.IsUserReport)
            {
                using (var form = new UserReportMemoForm(record.UserReportMemo, _userReportStore))
                {
                    form.ShowDialog(this);
                    if (form.Changed) Changed = true;
                }
            }
            else
            {
                using (var form = new RaidNoteForm(record.RaidNote, _store))
                {
                    form.ShowDialog(this);
                    if (form.Changed) Changed = true;
                }
            }
            RefreshRecords();
        }

        private void DeleteSelected()
        {
            if (_busy) return;
            ArchiveItem record = GetSelectedRecord();
            if (record == null) return;
            DialogResult answer = MessageBox.Show(
                this,
                record.IsUserReport
                    ? "선택한 유저신고 메모를 삭제할까요? 일반 레이드 메모에는 영향을 주지 않습니다."
                    : "선택한 레이드 메모를 삭제할까요? 첨부한 원본 스크린샷은 삭제하지 않습니다.",
                "메모 삭제",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            try
            {
                if (record.IsUserReport)
                    _userReportStore.Delete(record.Key);
                else
                    _store.Delete(record.Key);
                Changed = true;
                RefreshRecords();
            }
            catch (Exception ex)
            {
                SetStatus("메모를 삭제하지 못했습니다: " + ex.Message, Danger);
            }
        }

        private void DeleteCheckedRecords()
        {
            if (_busy) return;
            IList<ArchiveDeleteTarget> targets = GetCheckedDeleteTargets();
            if (targets.Count == 0) return;

            int raidCount = targets.Count(target => !target.IsUserReport);
            int reportCount = targets.Count - raidCount;
            DialogResult answer = MessageBox.Show(
                this,
                string.Format(
                    "선택한 메모 {0}개를 삭제할까요?\r\n"
                    + "레이드 메모 {1}개 · 유저신고 메모 {2}개\r\n\r\n"
                    + "삭제한 메모는 복구할 수 없습니다. "
                    + "레이드 메모에 첨부한 원본 스크린샷은 삭제하지 않습니다.",
                    targets.Count,
                    raidCount,
                    reportCount),
                "선택 메모 삭제",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            ArchiveItem selected = GetSelectedRecord();
            string selectedIdentity = selected == null ? null : selected.Identity;
            SetBusy(true, "선택한 메모를 다시 확인하고 삭제하는 중…");
            try
            {
                ArchiveDeleteResult result = DeleteRevalidatedTargets(targets);
                if (result.SucceededCount > 0) Changed = true;
                bool refreshed = ReloadRecords(result.RetainedIdentities, selectedIdentity);
                string message = BuildBatchDeleteStatus(result);
                if (!refreshed) message += " · 목록 새로고침 실패";
                Color color = result.SucceededCount == 0
                    ? Danger
                    : (result.FailedCount > 0 || result.MissingCount > 0 || !refreshed
                        ? Warning
                        : Success);
                SetStatus(message, color);
            }
            catch (Exception ex)
            {
                SetStatus("선택한 메모를 삭제하지 못했습니다: " + ex.Message, Danger);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private ArchiveDeleteResult DeleteRevalidatedTargets(IEnumerable<ArchiveDeleteTarget> requestedTargets)
        {
            IList<ArchiveDeleteTarget> targets = (requestedTargets ?? Enumerable.Empty<ArchiveDeleteTarget>())
                .Where(target => target != null)
                .GroupBy(target => target.Identity, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var result = new ArchiveDeleteResult { RequestedCount = targets.Count };
            HashSet<string> raidKeys = null;
            HashSet<string> reportKeys = null;
            string raidLoadError = null;
            string reportLoadError = null;

            if (targets.Any(target => !target.IsUserReport))
            {
                try
                {
                    raidKeys = new HashSet<string>(
                        _store.LoadAll().Select(record => record.Key),
                        StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) { raidLoadError = ex.Message; }
            }
            if (targets.Any(target => target.IsUserReport))
            {
                try
                {
                    reportKeys = new HashSet<string>(
                        _userReportStore.LoadAll().Select(record => record.Key),
                        StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex) { reportLoadError = ex.Message; }
            }

            foreach (ArchiveDeleteTarget target in targets)
            {
                string loadError = target.IsUserReport ? reportLoadError : raidLoadError;
                HashSet<string> currentKeys = target.IsUserReport ? reportKeys : raidKeys;
                if (!string.IsNullOrWhiteSpace(loadError) || currentKeys == null)
                {
                    result.AddFailure(target, loadError ?? "현재 저장 목록을 확인하지 못했습니다.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(target.Key) || !currentKeys.Contains(target.Key))
                {
                    result.MissingCount++;
                    result.RetainedIdentities.Add(target.Identity);
                    continue;
                }

                try
                {
                    DeleteTarget(target);
                    if (TargetExists(target))
                        throw new InvalidOperationException("삭제 후에도 저장 항목이 남아 있습니다.");
                    result.SucceededCount++;
                }
                catch (Exception ex)
                {
                    bool stillExists = true;
                    try { stillExists = TargetExists(target); }
                    catch { }
                    if (!stillExists)
                        result.SucceededCount++;
                    else
                        result.AddFailure(target, ex.Message);
                }
            }
            return result;
        }

        private void DeleteTarget(ArchiveDeleteTarget target)
        {
            if (target.IsUserReport)
                _userReportStore.Delete(target.Key);
            else
                _store.Delete(target.Key);
        }

        private bool TargetExists(ArchiveDeleteTarget target)
        {
            return target.IsUserReport
                ? _userReportStore.Exists(target.Key)
                : _store.Exists(target.Key);
        }

        private static string BuildBatchDeleteStatus(ArchiveDeleteResult result)
        {
            if (result == null) return "선택한 메모 삭제 결과를 확인하지 못했습니다.";
            var parts = new List<string>();
            if (result.SucceededCount > 0) parts.Add(result.SucceededCount + "개 삭제");
            if (result.MissingCount > 0) parts.Add(result.MissingCount + "개 이미 없음");
            if (result.FailedCount > 0) parts.Add(result.FailedCount + "개 실패");
            if (parts.Count == 0) parts.Add("삭제할 현재 항목 없음");
            string message = string.Join(" · ", parts);
            if (result.FailedCount > 0 && !string.IsNullOrWhiteSpace(result.FirstError))
                message += ": " + result.FirstError;
            return message;
        }

        private void OpenFolder()
        {
            if (_busy) return;
            try
            {
                ArchiveItem selected = GetSelectedRecord();
                if (selected != null && selected.IsUserReport)
                    _userReportStore.OpenMemoFolder();
                else
                    _store.OpenNotesFolder();
            }
            catch (Exception ex) { SetStatus("저장 폴더를 열지 못했습니다: " + ex.Message, Danger); }
        }

        private async Task ExportBackupAsync()
        {
            if (_busy) return;
            SetBusy(true, "레이드 메모와 유저신고 메모를 확인하는 중…");
            MemoArchiveBackupExportResult export;
            try
            {
                export = await Task.Run(
                    () => MemoArchiveBackupService.CreateExport(
                        _store,
                        _userReportStore,
                        DateTime.UtcNow));
            }
            catch
            {
                export = null;
            }
            finally
            {
                SetBusy(false, null);
            }
            if (IsDisposed) return;
            if (export == null || !export.Success)
            {
                SetStatus(
                    export == null || string.IsNullOrWhiteSpace(export.ErrorMessage)
                        ? "메모 백업 데이터를 안전하게 만들지 못했습니다."
                        : export.ErrorMessage,
                    Danger);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                this,
                string.Format(
                    "일반 레이드 메모 {0}개와 유저신고 메모 {1}개를 "
                        + "하나의 JSON 파일로 저장합니다.\r\n\r\n{2}",
                    export.RaidNoteCount,
                    export.UserReportMemoCount,
                    BackupScreenshotNotice),
                "메모 통합 백업 내보내기",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (confirmation != DialogResult.OK)
            {
                SetStatus("메모 백업 저장을 취소했습니다.", TextMuted);
                return;
            }

            string selectedPath;
            using (var dialog = new SaveFileDialog
            {
                Title = "메모 통합 백업 저장",
                Filter = "TSG 메모 백업 (*.json)|*.json",
                DefaultExt = "json",
                AddExtension = true,
                OverwritePrompt = true,
                RestoreDirectory = true,
                FileName = MemoArchiveBackupService.CreateDefaultFileName(DateTime.Now)
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selectedPath = dialog.FileName;
            }

            SetBusy(true, "메모 백업 파일을 안전하게 저장하는 중…");
            try
            {
                byte[] bytes = export.Utf8Bytes;
                await Task.Run(() => MemoArchiveBackupService.WriteAtomic(selectedPath, bytes));
                if (IsDisposed) return;
                SetStatus(
                    string.Format(
                        "레이드 메모 {0}개 · 유저신고 메모 {1}개를 {2} 파일로 저장했습니다.",
                        export.RaidNoteCount,
                        export.UserReportMemoCount,
                        Path.GetFileName(selectedPath)),
                    Success);
            }
            catch
            {
                if (!IsDisposed)
                    SetStatus("메모 백업 파일을 안전하게 저장하지 못했습니다.", Danger);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false, null);
            }
        }

        private async Task ImportBackupAsync()
        {
            if (_busy) return;
            string selectedPath;
            using (var dialog = new OpenFileDialog
            {
                Title = "메모 통합 백업 불러오기",
                Filter = "TSG 메모 백업 (*.json)|*.json|모든 파일 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selectedPath = dialog.FileName;
            }

            SetBusy(true, "메모 백업 파일을 검증하는 중…");
            MemoArchiveBackupParseResult parsed;
            IList<MemoArchiveRestoreItem> previewItems = null;
            try
            {
                parsed = await Task.Run(() => MemoArchiveBackupService.ParseFile(selectedPath));
                if (parsed != null && parsed.Success)
                {
                    previewItems = await Task.Run(
                        () => MemoArchiveBackupService.CreateRestorePreview(
                            parsed,
                            _store,
                            _userReportStore));
                }
            }
            catch
            {
                parsed = null;
            }
            finally
            {
                SetBusy(false, null);
            }
            if (IsDisposed) return;
            if (parsed == null || !parsed.Success || previewItems == null)
            {
                SetStatus(
                    parsed == null || string.IsNullOrWhiteSpace(parsed.ErrorMessage)
                        ? "메모 백업 파일을 안전하게 검증하지 못했습니다."
                        : parsed.ErrorMessage,
                    Danger);
                return;
            }

            using (var preview = new MemoArchiveRestorePreviewForm(
                previewItems,
                items => MemoArchiveBackupService.ApplyMissingOnly(
                    items,
                    _store,
                    _userReportStore)))
            {
                preview.ShowDialog(this);
                ApplyRestorePreviewOutcome(preview);
            }
        }

        private void ApplyRestorePreviewOutcome(MemoArchiveRestorePreviewForm preview)
        {
            if (preview == null) return;
            if (!preview.ApplyAttempted)
            {
                SetStatus("메모 복원을 취소했습니다.", TextMuted);
                return;
            }

            // A post-write verification failure can become an exact-existing skip on retry.
            // Refresh and notify the owner conservatively after any apply attempt so note cells
            // cannot remain stale even when the aggregate AddedCount is zero.
            RefreshRecords();
            Changed = true;
            SetStatus(
                preview.ResultSummary,
                preview.FailedCount > 0
                    ? Warning
                    : preview.HasChanges ? Success : TextMuted);
        }

        private void UpdateButtons()
        {
            bool selected = !_busy && GetSelectedRecord() != null;
            int totalCount = _grid == null ? 0 : _grid.Rows.Count;
            int checkedCount = _grid == null
                ? 0
                : _grid.Rows.Cast<DataGridViewRow>().Count(IsRowChecked);
            if (_openButton != null) _openButton.Enabled = selected;
            if (_deleteButton != null) _deleteButton.Enabled = selected;
            if (_deleteSelectedButton != null)
            {
                _deleteSelectedButton.Enabled = !_busy && checkedCount > 0;
                string buttonDescription = checkedCount == 0
                    ? "체크박스로 선택한 메모가 없습니다."
                    : "체크박스로 선택한 메모 " + checkedCount + "개를 삭제합니다.";
                bool buttonDescriptionChanged = !string.Equals(
                    _deleteSelectedButton.AccessibleDescription,
                    buttonDescription,
                    StringComparison.Ordinal);
                _deleteSelectedButton.AccessibleDescription = buttonDescription;
                var accessibleButton = _deleteSelectedButton as ArchiveButton;
                if (buttonDescriptionChanged && accessibleButton != null)
                    accessibleButton.NotifyAccessibleDescriptionChanged();
            }
            if (_folderButton != null) _folderButton.Enabled = !_busy;
            if (_refreshButton != null) _refreshButton.Enabled = !_busy;
            if (_exportButton != null) _exportButton.Enabled = !_busy;
            if (_importButton != null) _importButton.Enabled = !_busy;
            UpdateSelectionAccessibility(totalCount, checkedCount);
        }

        private void UpdateSelectionAccessibility(int totalCount, int checkedCount)
        {
            if (_grid == null) return;
            string description = "메모 선택 목록입니다. "
                + BuildSelectionAccessibilitySummary(totalCount, checkedCount)
                + " 선택 열 머리글은 전체 선택 또는 전체 해제이며, Ctrl+A는 전체 선택입니다.";
            bool changed = !string.Equals(
                _grid.AccessibleDescription,
                description,
                StringComparison.Ordinal);
            _grid.AccessibleDescription = description;
            if (_grid.Columns.Contains("selected"))
                _grid.InvalidateCell(_grid.Columns["selected"].HeaderCell);
            var accessibleGrid = _grid as ArchiveDataGridView;
            if (changed && accessibleGrid != null)
                accessibleGrid.NotifyAccessibleDescriptionChanged();
        }

        private static string BuildSelectionAccessibilitySummary(int totalCount, int checkedCount)
        {
            return "전체 메모 " + totalCount + "개 중 " + checkedCount + "개가 선택되었습니다.";
        }

        private static string BuildSelectionAccessibilitySummary(DataGridView grid)
        {
            if (grid == null) return BuildSelectionAccessibilitySummary(0, 0);
            int checkedCount = grid.Rows.Cast<DataGridViewRow>().Count(IsRowChecked);
            return BuildSelectionAccessibilitySummary(grid.Rows.Count, checkedCount);
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            if (_grid != null) _grid.Enabled = !busy;
            UseWaitCursor = busy;
            if (!string.IsNullOrWhiteSpace(status)) SetStatus(status, TextMuted);
            UpdateButtons();
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusLabel == null) return;
            _statusLabel.Text = message;
            _statusLabel.ForeColor = color;
        }

        private static string FormatDate(DateTime value)
        {
            return value == default(DateTime)
                ? "-"
                : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string EmptyFallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string BuildMapAndGameType(string mapName, string gameType)
        {
            string map = EmptyFallback(mapName);
            return string.IsNullOrWhiteSpace(gameType)
                ? map
                : map + " · " + gameType.Trim();
        }

        private static string BuildNotePreview(string value, bool isUserReport)
        {
            if (!isUserReport) value = RaidNoteStore.NormalizeLegacyNoteText(value);
            if (string.IsNullOrWhiteSpace(value)) return "-";
            string trimmed = value.Trim();
            string compact = string.Join(" ", trimmed
                .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0));
            if (compact.Length == 0) return "-";
            return compact.Length <= 120 ? compact : compact.Substring(0, 117) + "…";
        }

        private static string BuildFullNoteToolTip(string value, bool isUserReport)
        {
            if (!isUserReport) value = RaidNoteStore.NormalizeLegacyNoteText(value);
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            return trimmed.Length <= 500 ? trimmed : trimmed.Substring(0, 497) + "…";
        }

        private static string Summarize(IEnumerable<string> values, int maximum)
        {
            if (values == null) return "-";
            List<string> items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            if (items.Count == 0) return "-";
            string summary = string.Join(", ", items.Take(maximum));
            return items.Count > maximum ? summary + " 외 " + (items.Count - maximum) + "개" : summary;
        }

        private static string JoinAll(IEnumerable<string> values)
        {
            if (values == null) return string.Empty;
            return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private sealed class ArchiveDataGridView : DataGridView
        {
            public void NotifyAccessibleDescriptionChanged()
            {
                if (IsHandleCreated)
                    AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            }
        }

        private sealed class ArchiveButton : Button
        {
            public void NotifyAccessibleDescriptionChanged()
            {
                if (IsHandleCreated)
                    AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
            }
        }

        private sealed class ArchiveSelectionHeaderCell : DataGridViewColumnHeaderCell
        {
            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new ArchiveSelectionHeaderAccessibleObject(this);
            }

            public override object Clone()
            {
                return (ArchiveSelectionHeaderCell)base.Clone();
            }

            private sealed class ArchiveSelectionHeaderAccessibleObject
                : DataGridViewColumnHeaderCellAccessibleObject
            {
                private readonly ArchiveSelectionHeaderCell _owner;

                public ArchiveSelectionHeaderAccessibleObject(ArchiveSelectionHeaderCell owner)
                    : base(owner)
                {
                    _owner = owner;
                }

                public override string Name
                {
                    get { return "메모 전체 선택 또는 전체 해제"; }
                }

                public override string Description
                {
                    get
                    {
                        return "선택 열 머리글입니다. 클릭하면 모든 메모를 선택하거나 선택 해제합니다. "
                            + BuildSelectionAccessibilitySummary(_owner.DataGridView);
                    }
                }

                public override string DefaultAction
                {
                    get
                    {
                        return GetSelectionHeaderState(_owner.DataGridView)
                            == HeaderSelectionState.All
                            ? "전체 해제"
                            : "전체 선택";
                    }
                }

                public override AccessibleStates State
                {
                    get
                    {
                        AccessibleStates selectionState = AccessibleStates.None;
                        switch (GetSelectionHeaderState(_owner.DataGridView))
                        {
                            case HeaderSelectionState.All:
                                selectionState = AccessibleStates.Checked;
                                break;
                            case HeaderSelectionState.Partial:
                                selectionState = AccessibleStates.Mixed;
                                break;
                        }
                        return base.State | selectionState;
                    }
                }
            }
        }

        private sealed class ArchiveSelectionCheckBoxCell : DataGridViewCheckBoxCell
        {
            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new ArchiveSelectionCheckBoxAccessibleObject(this);
            }

            public override object Clone()
            {
                return (ArchiveSelectionCheckBoxCell)base.Clone();
            }

            private sealed class ArchiveSelectionCheckBoxAccessibleObject
                : DataGridViewCheckBoxCellAccessibleObject
            {
                private readonly ArchiveSelectionCheckBoxCell _owner;

                public ArchiveSelectionCheckBoxAccessibleObject(ArchiveSelectionCheckBoxCell owner)
                    : base(owner)
                {
                    _owner = owner;
                }

                public override string Name
                {
                    get
                    {
                        return _owner.RowIndex < 0
                            ? "메모 선택 체크박스"
                            : "메모 선택 체크박스 행 " + (_owner.RowIndex + 1);
                    }
                }

                public override string Description
                {
                    get
                    {
                        bool isChecked = _owner.Value is bool && (bool)_owner.Value;
                        return "이 메모를 선택 삭제 대상에 포함합니다. 현재 "
                            + (isChecked ? "선택되었습니다. " : "선택되지 않았습니다. ")
                            + BuildSelectionAccessibilitySummary(_owner.DataGridView);
                    }
                }
            }
        }

        private sealed class ArchiveRowComparer : System.Collections.IComparer
        {
            private readonly RaidNoteArchiveForm _owner;

            public ArchiveRowComparer(RaidNoteArchiveForm owner)
            {
                _owner = owner;
            }

            public int Compare(object left, object right)
            {
                return _owner.CompareArchiveRows(
                    left as DataGridViewRow,
                    right as DataGridViewRow);
            }
        }

        private sealed class ArchiveDeleteTarget
        {
            private ArchiveDeleteTarget()
            {
            }

            public bool IsUserReport { get; private set; }
            public string Key { get; private set; }
            public string Identity { get; private set; }

            public static ArchiveDeleteTarget For(ArchiveItem record)
            {
                if (record == null) throw new ArgumentNullException("record");
                return new ArchiveDeleteTarget
                {
                    IsUserReport = record.IsUserReport,
                    Key = record.Key,
                    Identity = record.Identity
                };
            }
        }

        private sealed class ArchiveDeleteResult
        {
            public ArchiveDeleteResult()
            {
                RetainedIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            public int RequestedCount { get; set; }
            public int SucceededCount { get; set; }
            public int MissingCount { get; set; }
            public int FailedCount { get; private set; }
            public string FirstError { get; private set; }
            public HashSet<string> RetainedIdentities { get; private set; }

            public void AddFailure(ArchiveDeleteTarget target, string error)
            {
                FailedCount++;
                if (target != null) RetainedIdentities.Add(target.Identity);
                if (string.IsNullOrWhiteSpace(FirstError))
                    FirstError = string.IsNullOrWhiteSpace(error) ? "알 수 없는 오류" : error;
            }
        }

        private sealed class ArchiveItem
        {
            private ArchiveItem()
            {
            }

            public RaidNoteRecord RaidNote { get; private set; }
            public UserReportMemoRecord UserReportMemo { get; private set; }
            public bool IsUserReport { get { return UserReportMemo != null; } }
            public string Key { get { return IsUserReport ? UserReportMemo.Key : RaidNote.Key; } }
            public string Identity { get { return (IsUserReport ? "report:" : "raid:") + Key; } }
            public string KindLabel { get { return IsUserReport ? "유저신고 메모" : "레이드 메모"; } }
            public DateTime RaidStartedUtc
            {
                get { return IsUserReport ? UserReportMemo.RaidStartedUtc : RaidNote.RaidStartedUtc; }
            }
            public DateTime UpdatedUtc
            {
                get { return IsUserReport ? UserReportMemo.UpdatedUtc : RaidNote.UpdatedUtc; }
            }
            public string Game { get { return IsUserReport ? UserReportMemo.Game : RaidNote.Game; } }
            public string MapName { get { return IsUserReport ? UserReportMemo.MapName : RaidNote.MapName; } }
            public string GameType
            {
                get { return IsUserReport ? UserReportMemo.GameType : RaidNote.GameType; }
            }
            public string NoteText
            {
                get
                {
                    return IsUserReport
                        ? UserReportMemoStore.BuildDisplayText(UserReportMemo)
                        : RaidNote.NoteText;
                }
            }
            public IEnumerable<string> Tags
            {
                get { return IsUserReport ? Enumerable.Empty<string>() : RaidNote.Tags; }
            }
            public int ReportCount { get { return IsUserReport ? UserReportMemo.ReportCount : 0; } }

            public static ArchiveItem ForRaidNote(RaidNoteRecord record)
            {
                return new ArchiveItem { RaidNote = record };
            }

            public static ArchiveItem ForUserReport(UserReportMemoRecord record)
            {
                return new ArchiveItem { UserReportMemo = record };
            }

            public static int CompareNewestFirst(ArchiveItem left, ArchiveItem right)
            {
                DateTime leftSort = left.RaidStartedUtc == default(DateTime)
                    ? left.UpdatedUtc : left.RaidStartedUtc;
                DateTime rightSort = right.RaidStartedUtc == default(DateTime)
                    ? right.UpdatedUtc : right.RaidStartedUtc;
                int result = rightSort.CompareTo(leftSort);
                if (result != 0) return result;
                result = right.UpdatedUtc.CompareTo(left.UpdatedUtc);
                if (result != 0) return result;
                return string.Compare(left.Identity, right.Identity, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
