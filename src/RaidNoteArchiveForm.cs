// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class RaidNoteArchiveForm : BrandedForm
    {
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
        private readonly bool _sourceReadOnly;
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
        private TextBox _searchTextBox;
        private ComboBox _typeFilter;
        private Button _clearFilterButton;
        private Label _resultCountLabel;
        private Label _emptyStateLabel;
        private readonly Timer _searchTimer = new Timer { Interval = 180 };
        private readonly ToolTip _archiveToolTip = new ToolTip();
        private bool _filterPending;
        private bool _suppressFilterEvents;
        private bool _busy;
        private bool _updatingChecks;
        private string _archiveSortColumn;
        private SortOrder _archiveSortOrder = SortOrder.None;

        public RaidNoteArchiveForm(RaidNoteStore store)
            : this(store, new UserReportMemoStore(), false)
        {
        }

        public RaidNoteArchiveForm(RaidNoteStore store, bool sourceReadOnly)
            : this(store, new UserReportMemoStore(), sourceReadOnly)
        {
        }

        public RaidNoteArchiveForm(RaidNoteStore store, UserReportMemoStore userReportStore)
            : this(store, userReportStore, false)
        {
        }

        public RaidNoteArchiveForm(
            RaidNoteStore store,
            UserReportMemoStore userReportStore,
            bool sourceReadOnly)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (userReportStore == null) throw new ArgumentNullException("userReportStore");
            _store = store;
            _userReportStore = userReportStore;
            _sourceReadOnly = sourceReadOnly;
            InitializeWindow();
            BuildInterface();
            _searchTimer.Tick += delegate { ApplyFilterNow(); };
            ApplySourceReadOnlyMode();
            Shown += delegate { RefreshRecords(); };
        }

        public bool Changed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _searchTimer.Stop();
                _searchTimer.Dispose();
                _archiveToolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F) && _searchTextBox.Enabled)
            {
                _searchTextBox.Focus();
                _searchTextBox.SelectAll();
                return true;
            }
            if (keyData == Keys.Escape && _searchTextBox.TextLength > 0 && !_busy)
            {
                _searchTextBox.Clear();
                ApplyFilterNow();
                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }

        private void InitializeWindow()
        {
            Text = AppText.Get("NoteArchive.Title");
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
                RowCount = 5
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildSearchRow(), 0, 1);
            root.Controls.Add(BuildResultsSummary(), 0, 2);
            var gridHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            gridHost.Controls.Add(BuildGrid());
            _emptyStateLabel = new Label
            {
                Name = "NoteArchiveEmptyState", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter, BackColor = Surface,
                ForeColor = TextMuted, Padding = new Padding(30), Visible = false,
                AccessibleRole = AccessibleRole.StaticText
            };
            gridHost.Controls.Add(_emptyStateLabel);
            _emptyStateLabel.BringToFront();
            root.Controls.Add(gridHost, 0, 3);
            root.Controls.Add(BuildFooter(), 0, 4);
        }

        private Control BuildSearchRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1,
                Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 4)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            row.Controls.Add(new Label { Text = AppText.Get("NoteArchive.Search.Label"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _searchTextBox = new TextBox
            {
                Name = "NoteArchiveSearch", Dock = DockStyle.Fill, MaxLength = 256,
                BackColor = SurfaceAlt, ForeColor = TextPrimary, BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 5, 12, 0), TabIndex = 0,
                AccessibleName = AppText.Get("NoteArchive.Search.Label"),
                AccessibleDescription = AppText.Get("NoteArchive.Search.Help")
            };
            _searchTextBox.TextChanged += delegate { FilterChanged(false); };
            _archiveToolTip.SetToolTip(_searchTextBox, AppText.Get("NoteArchive.Search.Help"));
            row.Controls.Add(_searchTextBox, 1, 0);
            row.Controls.Add(new Label { Name = "NoteArchiveTypeLabel", Text = AppText.Get("NoteArchive.Filter.Label"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 12, 0), TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
            _typeFilter = new ComboBox
            {
                Name = "NoteArchiveTypeFilter", Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList, BackColor = SurfaceAlt,
                FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed,
                ForeColor = TextPrimary, Margin = new Padding(0, 4, 0, 0), TabIndex = 1,
                Cursor = Cursors.Hand,
                AccessibleName = AppText.Get("NoteArchive.Filter.Label")
            };
            _typeFilter.Items.AddRange(new object[]
            {
                AppText.Get("NoteArchive.Filter.All"), AppText.Get("NoteArchive.Filter.Raid"),
                AppText.Get("NoteArchive.Filter.Report")
            });
            _typeFilter.SelectedIndex = 0;
            bool hovered = false;
            Action refreshFilterFeedback = delegate
            {
                _typeFilter.BackColor = SystemInformation.HighContrast ? SystemColors.Window
                    : hovered || _typeFilter.Focused || _typeFilter.DroppedDown
                        ? Color.FromArgb(49, 61, 75) : SurfaceAlt;
                _typeFilter.Invalidate();
            };
            _typeFilter.MouseEnter += delegate { hovered = true; refreshFilterFeedback(); };
            _typeFilter.MouseLeave += delegate { hovered = false; refreshFilterFeedback(); };
            _typeFilter.GotFocus += delegate { refreshFilterFeedback(); };
            _typeFilter.LostFocus += delegate { refreshFilterFeedback(); };
            _typeFilter.DropDown += delegate { refreshFilterFeedback(); };
            _typeFilter.DropDownClosed += delegate { refreshFilterFeedback(); };
            _typeFilter.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                if (args.Index < 0) return;
                bool menuItem = (args.State & DrawItemState.ComboBoxEdit) == 0;
                bool selected = menuItem && (args.State & DrawItemState.Selected) != 0;
                Color background = selected
                    ? (SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(64, 80, 99))
                    : menuItem ? (SystemInformation.HighContrast ? SystemColors.Window : SurfaceAlt) : _typeFilter.BackColor;
                Color foreground = SystemInformation.HighContrast
                    ? (selected ? SystemColors.HighlightText : SystemColors.WindowText) : TextPrimary;
                using (var brush = new SolidBrush(background))
                    args.Graphics.FillRectangle(brush, args.Bounds);
                TextRenderer.DrawText(args.Graphics, Convert.ToString(_typeFilter.Items[args.Index]),
                    _typeFilter.Font, args.Bounds, foreground,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                args.DrawFocusRectangle();
            };
            _typeFilter.SelectedIndexChanged += delegate { FilterChanged(true); };
            row.Controls.Add(_typeFilter, 3, 0);
            _clearFilterButton = CreateButton(AppText.Get("NoteArchive.Search.Clear"), 82, SurfaceAlt, TextPrimary);
            _clearFilterButton.Name = "NoteArchiveClearFilter";
            _clearFilterButton.TabIndex = 2;
            _clearFilterButton.Click += delegate
            {
                _suppressFilterEvents = true;
                _searchTextBox.Clear();
                _typeFilter.SelectedIndex = 0;
                _suppressFilterEvents = false;
                FilterChanged(true);
                _searchTextBox.Focus();
            };
            row.Controls.Add(_clearFilterButton, 5, 0);
            return row;
        }

        private Control BuildResultsSummary()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _resultCountLabel = new Label { Name = "NoteArchiveResultCount", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TextMuted, AutoEllipsis = true };
            row.Controls.Add(_resultCountLabel, 0, 0);
            row.Controls.Add(new Label { Text = AppText.Get("NoteArchive.Backup.AllScope"), AutoSize = true, Anchor = AnchorStyles.Right, ForeColor = TextMuted }, 1, 0);
            return row;
        }

        private void FilterChanged(bool immediate)
        {
            if (_suppressFilterEvents || _grid == null) return;
            _filterPending = true;
            _searchTimer.Stop();
            _updatingChecks = true;
            try { foreach (DataGridViewRow row in _grid.Rows) row.Cells["selected"].Value = false; }
            finally { _updatingChecks = false; }
            UpdateButtons();
            if (immediate) ApplyFilterNow();
            else _searchTimer.Start();
        }

        private void ApplyFilterNow()
        {
            _searchTimer.Stop();
            _filterPending = false;
            RenderRecords(null, null);
        }

        private bool MatchesFilter(ArchiveItem record, string[] terms)
        {
            if ((_typeFilter.SelectedIndex == 1 && record.IsUserReport)
                || (_typeFilter.SelectedIndex == 2 && !record.IsUserReport)) return false;
            return terms.All(term => record.SearchText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
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
                Text = AppText.Get("NoteArchive.Title"),
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
                Text = _sourceReadOnly
                    ? AppText.Get("Memo.Legacy.ReadOnlySourceNotice")
                    : AppText.Get("NoteArchive.Description"),
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
            _importButton = CreateButton(
                AppText.Get("Common.Button.Import"), 96, SurfaceAlt, TextPrimary);
            _importButton.Name = "MemoBackupImportButton";
            _importButton.AccessibleName = AppText.Get("NoteArchive.Backup.ImportA11y");
            _importButton.AccessibleDescription =
                AppText.Get("NoteArchive.Backup.ImportDescription");
            _importButton.Click += async delegate { await ImportBackupAsync(); };
            _exportButton = CreateButton(
                AppText.Get("Common.Button.Export"), 96, SurfaceAlt, TextPrimary);
            _exportButton.Name = "MemoBackupExportButton";
            _exportButton.AccessibleName = AppText.Get("NoteArchive.Backup.ExportA11y");
            _exportButton.AccessibleDescription = AppText.Format(
                "NoteArchive.Backup.ExportDescription",
                AppText.Get("NoteArchive.Backup.ScreenshotNotice")) + " "
                + AppText.Get("NoteArchive.Backup.AllScope");
            _archiveToolTip.SetToolTip(_exportButton, AppText.Get("NoteArchive.Backup.AllScope"));
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
                AccessibleName = AppText.Get("NoteArchive.Grid.Name"),
                AccessibleDescription = AppText.Format(
                    "NoteArchive.Grid.Description",
                    BuildSelectionAccessibilitySummary(0, 0)),
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
                    ToolTipText = AppText.Get("NoteArchive.Grid.SelectHeaderTooltip")
                },
                HeaderText = string.Empty,
                CellTemplate = new ArchiveSelectionCheckBoxCell(),
                Width = 54,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = false
            });
            _grid.Columns.Add(CreateColumn(
                "kind", AppText.Get("NoteArchive.Column.Kind"),
                AppText.CurrentLanguage == AppText.EnglishLanguage ? 136 : 104));
            _grid.Columns.Add(CreateColumn(
                "date", AppText.Get("NoteArchive.Column.RaidTime"), 145));
            _grid.Columns.Add(CreateColumn(
                "game", AppText.Get("NoteArchive.Column.Game"), 64));
            _grid.Columns.Add(CreateColumn(
                "map", AppText.Get("NoteArchive.Column.MapGameType"), 180));
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "preview",
                HeaderText = AppText.Get("NoteArchive.Column.Preview"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 220,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                ReadOnly = true
            });
            _grid.Columns.Add(CreateColumn(
                "tags", AppText.Get("NoteArchive.Column.TagsReports"), 130));
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "updated",
                HeaderText = AppText.Get("NoteArchive.Column.Updated"),
                Width = 150,
                SortMode = DataGridViewColumnSortMode.Programmatic,
                ReadOnly = true
            });
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                if (column.SortMode == DataGridViewColumnSortMode.Programmatic)
                    column.HeaderCell.ToolTipText =
                        AppText.Get("NoteArchive.Sort.Help");
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
                        !_sourceReadOnly && _grid.Enabled);
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
                Text = AppText.Get("NoteArchive.Loading")
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
            _openButton = CreateButton(
                AppText.Get("Common.Button.Open"), 82, Accent, Color.FromArgb(29, 24, 17));
            _openButton.Click += delegate { OpenSelected(); };
            _deleteButton = CreateButton(
                AppText.Get(AppText.CurrentLanguage == AppText.EnglishLanguage
                    ? "NoteArchive.Button.DeleteCurrentA11y" : "Common.Button.Delete"),
                AppText.CurrentLanguage == AppText.EnglishLanguage ? 152 : 82,
                Danger, Color.White);
            _deleteButton.Name = "NoteArchiveDeleteButton";
            _deleteButton.AccessibleName = AppText.Get(
                "NoteArchive.Button.DeleteCurrentA11y");
            _deleteButton.AccessibleDescription = AppText.Get(
                "NoteArchive.Button.DeleteCurrentDescription");
            _deleteButton.Click += delegate { DeleteSelected(); };
            _deleteSelectedButton = CreateButton(
                AppText.Get("NoteArchive.Button.DeleteSelected"),
                AppText.CurrentLanguage == AppText.EnglishLanguage ? 140 : 100,
                Danger, Color.White);
            _deleteSelectedButton.Name = "NoteArchiveDeleteSelectedButton";
            _deleteSelectedButton.AccessibleName = AppText.Get(
                "NoteArchive.Button.DeleteSelected");
            _deleteSelectedButton.AccessibleDescription = AppText.Get(
                "NoteArchive.Button.DeleteSelectedNone");
            _deleteSelectedButton.Click += delegate { DeleteCheckedRecords(); };
            _folderButton = CreateButton(
                AppText.Get("NoteArchive.Button.OpenFolder"), 126, SurfaceAlt, TextPrimary);
            _folderButton.Click += delegate { OpenFolder(); };
            _refreshButton = CreateButton(
                AppText.Get("NoteArchive.Button.Refresh"), 90, SurfaceAlt, TextPrimary);
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

        private void ApplySourceReadOnlyMode()
        {
            if (!_sourceReadOnly) return;
            string notice = AppText.Get("Memo.Legacy.ReadOnlySourceNotice");
            _grid.ReadOnly = true;
            if (_grid.Columns.Contains("selected"))
            {
                DataGridViewColumn selection = _grid.Columns["selected"];
                selection.ReadOnly = true;
                selection.HeaderCell.ToolTipText = notice;
            }
            foreach (Button button in new[]
            {
                _importButton, _deleteButton, _deleteSelectedButton
            })
            {
                button.Enabled = false;
                button.AccessibleDescription = notice;
            }
            _grid.AccessibleDescription = notice;
            UpdateButtons();
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
                if (_sourceReadOnly) return;
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
            if (_sourceReadOnly
                && ((e.Control && e.KeyCode == Keys.A) || e.KeyCode == Keys.Space))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
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
            SetBusy(true, AppText.Get("NoteArchive.Refreshing"));
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
                _searchTimer.Stop();
                _filterPending = false;
                RenderRecords(checkedIdentities, selectedIdentity);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(AppText.Format(
                    "NoteArchive.LoadFailed",
                    LocalizeArchiveError(ex.Message, "NoteArchive.UnknownError")), Danger);
                return false;
            }
        }

        private void RenderRecords(HashSet<string> checkedIdentities, string selectedIdentity)
        {
            string[] terms = _searchTextBox.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            List<ArchiveItem> visible = _records.Where(record => MatchesFilter(record, terms)).ToList();
            _updatingChecks = true;
            _grid.SuspendLayout();
            try
            {
                _grid.Rows.Clear();
                foreach (ArchiveItem record in visible)
                {
                    int rowIndex = _grid.Rows.Add(
                        checkedIdentities != null && checkedIdentities.Contains(record.Identity),
                        record.KindLabel,
                        FormatDate(record.RaidStartedUtc),
                        EmptyFallback(record.Game),
                        BuildMapAndGameType(record.MapName, record.GameType),
                        BuildNotePreview(record.NoteText, record.IsUserReport),
                        record.IsUserReport
                            ? AppText.Format(
                                "NoteArchive.ReportCount", record.ReportCount)
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
                        ? AppText.Format(
                            "NoteArchive.ReportCountTooltip", record.ReportCount)
                        : JoinAll(record.Tags);
                    if (!string.IsNullOrWhiteSpace(selectedIdentity)
                        && string.Equals(record.Identity, selectedIdentity, StringComparison.OrdinalIgnoreCase))
                        row.Selected = true;
                }
                ApplyArchiveSort();
                if (_grid.SelectedRows.Count == 0 && _grid.Rows.Count > 0)
                    _grid.Rows[0].Selected = true;
            }
            finally
            {
                _updatingChecks = false;
                _grid.ResumeLayout();
            }
            _resultCountLabel.Text = AppText.Format("NoteArchive.Search.Count", visible.Count, _records.Count);
            _resultCountLabel.AccessibleName = _resultCountLabel.Text;
            _emptyStateLabel.Text = AppText.Get(_records.Count == 0
                ? "NoteArchive.Search.Empty" : "NoteArchive.Search.NoResults");
            _emptyStateLabel.AccessibleName = _emptyStateLabel.Text;
            _emptyStateLabel.Visible = visible.Count == 0;
            _grid.Visible = visible.Count > 0;
            if (_records.Count == 0)
                SetStatus(AppText.Get("NoteArchive.Empty"), TextMuted);
            else
                SetStatus(
                    AppText.Format(
                        "NoteArchive.Count",
                        _records.Count,
                        _records.Count(record => !record.IsUserReport),
                        _records.Count(record => record.IsUserReport)),
                    TextMuted);
            UpdateButtons();
        }

        private ArchiveItem GetSelectedRecord()
        {
            if (_grid == null || _grid.SelectedRows.Count == 0) return null;
            return _grid.SelectedRows[0].Tag as ArchiveItem;
        }

        private void OpenSelected()
        {
            if (_busy || _filterPending) return;
            ArchiveItem record = GetSelectedRecord();
            if (record == null) return;
            if (record.IsUserReport)
            {
                using (var form = new UserReportMemoForm(
                    record.UserReportMemo,
                    _userReportStore,
                    _sourceReadOnly))
                {
                    form.ShowDialog(this);
                    if (form.Changed) Changed = true;
                }
            }
            else
            {
                using (var form = new RaidNoteForm(
                    record.RaidNote,
                    _store,
                    _sourceReadOnly))
                {
                    form.ShowDialog(this);
                    if (form.Changed) Changed = true;
                }
            }
            RefreshRecords();
        }

        private void DeleteSelected()
        {
            if (_busy || _sourceReadOnly || _filterPending) return;
            ArchiveItem record = GetSelectedRecord();
            if (record == null) return;
            DialogResult answer = MessageBox.Show(
                this,
                record.IsUserReport
                    ? AppText.Get("NoteArchive.Delete.PlayerPrompt")
                    : AppText.Get("NoteArchive.Delete.RaidPrompt"),
                AppText.Get("NoteArchive.Delete.Title"),
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
                SetStatus(AppText.Format(
                    "NoteArchive.Delete.Failed",
                    LocalizeArchiveError(ex.Message, "NoteArchive.UnknownError")), Danger);
            }
        }

        private void DeleteCheckedRecords()
        {
            if (_busy || _sourceReadOnly || _filterPending) return;
            IList<ArchiveDeleteTarget> targets = GetCheckedDeleteTargets();
            if (targets.Count == 0) return;

            int raidCount = targets.Count(target => !target.IsUserReport);
            int reportCount = targets.Count - raidCount;
            DialogResult answer = MessageBox.Show(
                this,
                AppText.Format(
                    "NoteArchive.Delete.BatchPrompt",
                    targets.Count,
                    raidCount,
                    reportCount),
                AppText.Get("NoteArchive.Delete.BatchTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;

            ArchiveItem selected = GetSelectedRecord();
            string selectedIdentity = selected == null ? null : selected.Identity;
            SetBusy(true, AppText.Get("NoteArchive.Delete.Revalidating"));
            try
            {
                ArchiveDeleteResult result = DeleteRevalidatedTargets(targets);
                if (result.SucceededCount > 0) Changed = true;
                bool refreshed = ReloadRecords(result.RetainedIdentities, selectedIdentity);
                string message = BuildBatchDeleteStatus(result);
                if (!refreshed)
                    message += AppText.Get("NoteArchive.Delete.RefreshFailedSuffix");
                Color color = result.SucceededCount == 0
                    ? Danger
                    : (result.FailedCount > 0 || result.MissingCount > 0 || !refreshed
                        ? Warning
                        : Success);
                SetStatus(message, color);
            }
            catch (Exception ex)
            {
                SetStatus(AppText.Format(
                    "NoteArchive.Delete.BatchFailed",
                    LocalizeArchiveError(ex.Message, "NoteArchive.UnknownError")), Danger);
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
                    result.AddFailure(target, LocalizeArchiveError(
                        loadError,
                        "NoteArchive.Delete.StoreUnavailable"));
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
                        throw new InvalidOperationException(
                            AppText.Get("NoteArchive.Delete.StillExists"));
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
            if (result == null)
                return AppText.Get("NoteArchive.Delete.ResultUnknown");
            var parts = new List<string>();
            if (result.SucceededCount > 0)
                parts.Add(AppText.Format(
                    "NoteArchive.Delete.SucceededCount", result.SucceededCount));
            if (result.MissingCount > 0)
                parts.Add(AppText.Format(
                    "NoteArchive.Delete.MissingCount", result.MissingCount));
            if (result.FailedCount > 0)
                parts.Add(AppText.Format(
                    "NoteArchive.Delete.FailedCount", result.FailedCount));
            if (parts.Count == 0)
                parts.Add(AppText.Get("NoteArchive.Delete.NothingCurrent"));
            string message = string.Join(" · ", parts);
            if (result.FailedCount > 0 && !string.IsNullOrWhiteSpace(result.FirstError))
                message += ": " + LocalizeArchiveError(
                    result.FirstError,
                    "NoteArchive.UnknownError");
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
            catch (Exception ex)
            {
                SetStatus(AppText.Format(
                    "NoteArchive.OpenFolderFailed",
                    LocalizeArchiveError(ex.Message, "NoteArchive.UnknownError")), Danger);
            }
        }

        private async Task ExportBackupAsync()
        {
            if (_busy) return;
            SetBusy(true, AppText.Get("NoteArchive.Backup.Checking"));
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
                        ? AppText.Get("NoteArchive.Backup.CreateFailed")
                        : LocalizeArchiveError(
                            export.ErrorMessage,
                            "NoteArchive.Backup.CreateFailed"),
                    Danger);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                this,
                AppText.Format(
                    "NoteArchive.Backup.ExportPrompt",
                    export.RaidNoteCount,
                    export.UserReportMemoCount,
                    AppText.Get("NoteArchive.Backup.ScreenshotNotice")),
                AppText.Get("NoteArchive.Backup.ExportTitle"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (confirmation != DialogResult.OK)
            {
                SetStatus(AppText.Get("NoteArchive.Backup.Cancelled"), TextMuted);
                return;
            }

            string selectedPath;
            using (var dialog = new SaveFileDialog
            {
                Title = AppText.Get("NoteArchive.Backup.SaveTitle"),
                Filter = AppText.Get("NoteArchive.Backup.SaveFilter"),
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

            SetBusy(true, AppText.Get("NoteArchive.Backup.Saving"));
            try
            {
                byte[] bytes = export.Utf8Bytes;
                await Task.Run(() => MemoArchiveBackupService.WriteAtomic(selectedPath, bytes));
                if (IsDisposed) return;
                SetStatus(
                    AppText.Format(
                        "NoteArchive.Backup.Saved",
                        export.RaidNoteCount,
                        export.UserReportMemoCount,
                        Path.GetFileName(selectedPath)),
                    Success);
            }
            catch
            {
                if (!IsDisposed)
                    SetStatus(AppText.Get("NoteArchive.Backup.SaveFailed"), Danger);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false, null);
            }
        }

        private async Task ImportBackupAsync()
        {
            if (_busy || _sourceReadOnly) return;
            string selectedPath;
            using (var dialog = new OpenFileDialog
            {
                Title = AppText.Get("NoteArchive.Backup.ImportTitle"),
                Filter = AppText.Get("NoteArchive.Backup.ImportFilter"),
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selectedPath = dialog.FileName;
            }

            SetBusy(true, AppText.Get("NoteArchive.Backup.Validating"));
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
                        ? AppText.Get("NoteArchive.Backup.ValidationFailed")
                        : LocalizeArchiveError(
                            parsed.ErrorMessage,
                            "NoteArchive.Backup.ValidationFailed"),
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
                SetStatus(AppText.Get("NoteArchive.Backup.RestoreCancelled"), TextMuted);
                return;
            }

            // A post-write verification failure can become an exact-existing skip on retry.
            // Refresh and notify the owner conservatively after any apply attempt so note cells
            // cannot remain stale even when the aggregate AddedCount is zero.
            RefreshRecords();
            Changed = true;
            SetStatus(
                AppText.Format(
                    "NoteArchive.Backup.RestoreSummary",
                    preview.AddedCount,
                    preview.SkippedCount,
                    preview.FailedCount),
                preview.FailedCount > 0
                    ? Warning
                    : preview.HasChanges ? Success : TextMuted);
        }

        private void UpdateButtons()
        {
            if (_updatingChecks) return;
            bool selected = !_busy && !_filterPending && GetSelectedRecord() != null;
            int totalCount = _grid == null ? 0 : _grid.Rows.Count;
            int checkedCount = _grid == null
                ? 0
                : _grid.Rows.Cast<DataGridViewRow>().Count(IsRowChecked);
            if (_openButton != null) _openButton.Enabled = selected;
            if (_deleteButton != null)
                _deleteButton.Enabled = !_sourceReadOnly && selected;
            if (_deleteSelectedButton != null)
            {
                _deleteSelectedButton.Enabled = !_sourceReadOnly
                    && !_busy
                    && !_filterPending
                    && checkedCount > 0;
                string buttonDescription = _sourceReadOnly
                    ? AppText.Get("Memo.Legacy.ReadOnlySourceNotice")
                    : checkedCount == 0
                    ? AppText.Get("NoteArchive.Button.DeleteSelectedNone")
                    : AppText.Format(
                        "NoteArchive.Button.DeleteSelectedCount", checkedCount);
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
            if (_importButton != null)
                _importButton.Enabled = !_sourceReadOnly && !_busy;
            if (_searchTextBox != null) _searchTextBox.Enabled = !_busy;
            if (_typeFilter != null) _typeFilter.Enabled = !_busy;
            if (_clearFilterButton != null)
                _clearFilterButton.Enabled = !_busy && (_searchTextBox.TextLength > 0 || _typeFilter.SelectedIndex != 0);
            UpdateSelectionAccessibility(totalCount, checkedCount);
        }

        private void UpdateSelectionAccessibility(int totalCount, int checkedCount)
        {
            if (_grid == null) return;
            string description = _sourceReadOnly
                ? AppText.Get("Memo.Legacy.ReadOnlySourceNotice")
                : AppText.Format(
                    "NoteArchive.Grid.Description",
                    BuildSelectionAccessibilitySummary(totalCount, checkedCount));
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
            return AppText.Format(
                "NoteArchive.Grid.SelectionSummary", totalCount, checkedCount);
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

        private static string LocalizeArchiveError(string error, string fallbackKey)
        {
            return AppText.TranslateDiagnostic(error, fallbackKey);
        }

        private static string BuildMapAndGameType(string mapName, string gameType)
        {
            string map = EmptyFallback(mapName);
            string normalizedGameType = string.IsNullOrWhiteSpace(gameType)
                ? string.Empty
                : AppText.NormalizePvpSeasonDisplay(gameType.Trim());
            return string.IsNullOrWhiteSpace(gameType)
                ? map
                : map + " · " + (string.Equals(
                    AppText.CurrentLanguage,
                    AppText.EnglishLanguage,
                    StringComparison.OrdinalIgnoreCase)
                        ? AppText.LocalizeDomainDisplay(normalizedGameType)
                        : normalizedGameType);
        }

        private static string BuildUserReportDisplayText(UserReportMemoRecord record)
        {
            if (record == null) return string.Empty;
            if (AppText.CurrentLanguage != AppText.EnglishLanguage)
                return UserReportMemoStore.BuildDisplayText(record);

            // Labels belong to the UI. Never translate the stored user fields
            // or the legacy free-form memo to change the display language.
            var builder = new StringBuilder();
            IList<UserReportMemoEntry> entries = record.Entries;
            if (entries != null)
            {
                for (int index = 0; index < entries.Count; index++)
                {
                    UserReportMemoEntry entry = entries[index];
                    string nickname = entry == null ? string.Empty : (entry.Nickname ?? string.Empty).Trim();
                    string reason = entry == null ? string.Empty : (entry.Reason ?? string.Empty).Trim();
                    if (nickname.Length == 0 && reason.Length == 0) continue;
                    if (builder.Length > 0) builder.AppendLine();
                    builder.Append(AppText.Format("UserReport.PlayerLabel", index + 1));
                    builder.Append(' ').Append(nickname.Length == 0 ? "-" : nickname);
                    builder.Append(" · ").Append(AppText.Get("UserReport.ReasonLabel"));
                    builder.Append(' ').Append(reason.Length == 0 ? "-" : reason);
                }
            }
            if (!string.IsNullOrWhiteSpace(record.MemoText))
            {
                if (builder.Length > 0) builder.AppendLine().AppendLine();
                builder.Append(record.MemoText.Trim());
            }
            return builder.ToString();
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
            return items.Count > maximum
                ? summary + AppText.Format(
                    "NoteArchive.More", items.Count - maximum)
                : summary;
        }

        private static string JoinAll(IEnumerable<string> values)
        {
            if (values == null) return string.Empty;
            return string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private sealed class ArchiveDataGridView : ResizeGuideDataGridView
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
                    get { return AppText.Get("NoteArchive.A11y.HeaderName"); }
                }

                public override string Description
                {
                    get
                    {
                        return AppText.Format(
                            "NoteArchive.A11y.HeaderDescription",
                            BuildSelectionAccessibilitySummary(_owner.DataGridView));
                    }
                }

                public override string DefaultAction
                {
                    get
                    {
                        return GetSelectionHeaderState(_owner.DataGridView)
                            == HeaderSelectionState.All
                            ? AppText.Get("BlockedServers.Selection.ClearAll")
                            : AppText.Get("BlockedServers.Selection.SelectAll");
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
                            ? AppText.Get("NoteArchive.A11y.Checkbox")
                            : AppText.Format(
                                "NoteArchive.A11y.CheckboxRow", _owner.RowIndex + 1);
                    }
                }

                public override string Description
                {
                    get
                    {
                        bool isChecked = _owner.Value is bool && (bool)_owner.Value;
                        return AppText.Format(
                            "NoteArchive.A11y.CheckboxDescription",
                            isChecked
                                ? AppText.Get("NoteArchive.A11y.Selected")
                                : AppText.Get("NoteArchive.A11y.NotSelected"),
                            BuildSelectionAccessibilitySummary(_owner.DataGridView));
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
                    FirstError = string.IsNullOrWhiteSpace(error)
                        ? AppText.Get("NoteArchive.UnknownError")
                        : error;
            }
        }

        private sealed class ArchiveItem
        {
            private ArchiveItem()
            {
            }

            public RaidNoteRecord RaidNote { get; private set; }
            public UserReportMemoRecord UserReportMemo { get; private set; }
            public string SearchText { get; private set; }
            public bool IsUserReport { get { return UserReportMemo != null; } }
            public string Key { get { return IsUserReport ? UserReportMemo.Key : RaidNote.Key; } }
            public string Identity { get { return (IsUserReport ? "report:" : "raid:") + Key; } }
            public string KindLabel
            {
                get
                {
                    return IsUserReport
                        ? AppText.Get("NoteArchive.Kind.PlayerReport")
                        : AppText.Get("NoteArchive.Kind.Raid");
                }
            }
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
                        ? BuildUserReportDisplayText(UserReportMemo)
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
                var item = new ArchiveItem { RaidNote = record };
                item.SearchText = string.Join("\n", new[] { item.Game, item.GameType, item.MapName,
                    record.NoteText, string.Join(" ", record.Tags ?? new List<string>()) });
                return item;
            }

            public static ArchiveItem ForUserReport(UserReportMemoRecord record)
            {
                var item = new ArchiveItem { UserReportMemo = record };
                item.SearchText = string.Join("\n", new[] { item.Game, item.GameType, item.MapName,
                    UserReportMemoStore.BuildDisplayText(record) });
                return item;
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
