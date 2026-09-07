// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal sealed class MemoArchiveRestorePreviewForm : BrandedForm
    {
        internal const string ResultSummaryFormat =
            "추가 {0}개 · 기존 항목 건너뜀 {1}개 · 실패 {2}개";
        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color Success = Color.FromArgb(78, 201, 134);
        private static readonly Color Warning = Color.FromArgb(247, 190, 79);
        private static readonly Color Danger = Color.FromArgb(224, 91, 91);

        private enum HeaderSelectionState
        {
            None,
            Partial,
            All
        }

        private readonly IList<MemoArchiveRestoreItem> _items;
        private readonly Func<IEnumerable<MemoArchiveRestoreItem>, MemoArchiveRestoreResult> _apply;
        private readonly HashSet<MemoArchiveRestoreItem> _completedItems =
            new HashSet<MemoArchiveRestoreItem>();
        private readonly HashSet<MemoArchiveRestoreItem> _skippedItems =
            new HashSet<MemoArchiveRestoreItem>();
        private readonly HashSet<MemoArchiveRestoreItem> _failedItems =
            new HashSet<MemoArchiveRestoreItem>();
        private DataGridView _grid;
        private Label _countLabel;
        private Label _resultLabel;
        private Button _applyButton;
        private Button _selectAllButton;
        private Button _selectNoneButton;
        private Button _closeButton;
        private bool _updatingSelection;
        private bool _busy;

        internal MemoArchiveRestorePreviewForm(
            IEnumerable<MemoArchiveRestoreItem> items,
            Func<IEnumerable<MemoArchiveRestoreItem>, MemoArchiveRestoreResult> apply)
        {
            if (apply == null) throw new ArgumentNullException("apply");
            _items = (items ?? Enumerable.Empty<MemoArchiveRestoreItem>())
                .Where(item => item != null && item.Source != null)
                .ToList();
            _apply = apply;
            foreach (MemoArchiveRestoreItem item in _items)
            {
                if (item.Status == MemoArchiveRestoreStatus.Existing)
                    _skippedItems.Add(item);
                item.Selected = item.Status == MemoArchiveRestoreStatus.New && item.Selected;
            }

            InitializeWindow();
            BuildInterface();
            PopulateRows();
            UpdateSelectionState();
        }

        internal int AddedCount { get; private set; }
        internal int SkippedCount { get { return _skippedItems.Count; } }
        internal int FailedCount { get { return _failedItems.Count; } }
        internal bool HasChanges { get { return AddedCount > 0; } }
        internal bool ApplyAttempted { get; private set; }
        internal bool ApplyCycleCompleted { get; private set; }
        internal int LastCompletionThreadId { get; private set; }
        internal MemoArchiveRestoreResult LastResult { get; private set; }

        internal string ResultSummary
        {
            get
            {
                return AppText.Format(
                    "NoteRestore.ResultSummary",
                    AddedCount,
                    SkippedCount,
                    FailedCount);
            }
        }

        private void InitializeWindow()
        {
            Text = AppText.Get("NoteRestore.Title");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1180, 610);
            MinimumSize = new Size(820, 480);
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
                Padding = new Padding(18),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(0, 0),
                Text = AppText.Get("NoteRestore.Title"),
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            _countLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 43,
                Padding = new Padding(2, 0, 0, 0),
                ForeColor = TextMuted,
                Text = BuildCountText()
            };
            header.Controls.Add(_countLabel);
            root.Controls.Add(header, 0, 0);

            _grid = BuildGrid();
            root.Controls.Add(_grid, 0, 1);

            _resultLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                AccessibleName = AppText.Get("NoteRestore.ResultA11y"),
                AccessibleDescription = AppText.Get("NoteRestore.Policy"),
                Text = AppText.Get("NoteRestore.Policy")
            };
            root.Controls.Add(_resultLabel, 0, 2);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 8, 0, 0)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var selectionActions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty
            };
            _selectAllButton = CreateButton(
                AppText.Get("BlockedServers.Selection.SelectAll"), 88, false);
            _selectAllButton.Name = "MemoRestoreSelectAllButton";
            _selectAllButton.Click += delegate { SetAllSelectable(true); };
            _selectNoneButton = CreateButton(
                AppText.Get("BlockedServers.Selection.ClearAll"), 88, false);
            _selectNoneButton.Name = "MemoRestoreSelectNoneButton";
            _selectNoneButton.Click += delegate { SetAllSelectable(false); };
            selectionActions.Controls.Add(_selectAllButton);
            selectionActions.Controls.Add(_selectNoneButton);
            actions.Controls.Add(selectionActions, 0, 0);

            var confirmActions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                BackColor = Background,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = Padding.Empty
            };
            _closeButton = CreateButton(AppText.Get("Common.Button.Cancel"), 82, false);
            _closeButton.Name = "MemoRestoreCloseButton";
            _closeButton.DialogResult = DialogResult.Cancel;
            _applyButton = CreateButton(AppText.Get("NoteRestore.Button.Apply"), 126, true);
            _applyButton.Name = "MemoRestoreApplyButton";
            _applyButton.Click += ApplyClicked;
            confirmActions.Controls.Add(_closeButton);
            confirmActions.Controls.Add(_applyButton);
            actions.Controls.Add(confirmActions, 1, 0);
            root.Controls.Add(actions, 0, 3);
            CancelButton = _closeButton;
        }

        private DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Name = "MemoRestorePreviewGrid",
                Dock = DockStyle.Fill,
                AccessibleName = AppText.Get("NoteRestore.Grid.Name"),
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
                SelectionForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Surface,
                ForeColor = TextPrimary,
                SelectionBackColor = Color.FromArgb(56, 65, 76),
                SelectionForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F),
                Padding = new Padding(5, 0, 5, 0)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(27, 33, 40);
            grid.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "selected",
                HeaderCell = new MemoSelectionHeaderCell(),
                HeaderText = string.Empty,
                ToolTipText = AppText.Get("NoteRestore.Grid.SelectTooltip"),
                Width = 54,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(CreateColumn(
                "status", AppText.Get("NoteRestore.Column.Status"), 112));
            grid.Columns.Add(CreateColumn(
                "kind", AppText.Get("NoteRestore.Column.Kind"), 112));
            grid.Columns.Add(CreateColumn(
                "game", AppText.Get("NoteRestore.Column.Game"), 72));
            grid.Columns.Add(CreateColumn(
                "map", AppText.Get("NoteRestore.Column.MapGameType"), 180));
            grid.Columns.Add(CreateColumn(
                "date", AppText.Get("NoteRestore.Column.RaidTime"), 145));
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "preview",
                HeaderText = AppText.Get("NoteRestore.Column.Preview"),
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 180,
                FillWeight = 58F
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "detail",
                HeaderText = AppText.Get("NoteRestore.Column.Result"),
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 170,
                FillWeight = 42F
            });
            grid.CellPainting += PaintSelectionHeader;
            grid.ColumnHeaderMouseClick += GridColumnHeaderMouseClick;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (_updatingSelection || args.RowIndex < 0 || args.ColumnIndex < 0) return;
                if (grid.Columns[args.ColumnIndex].Name != "selected") return;
                SyncItemSelection(grid.Rows[args.RowIndex]);
                UpdateSelectionState();
            };
            grid.KeyDown += GridKeyDown;
            return grid;
        }

        private static DataGridViewTextBoxColumn CreateColumn(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private void PopulateRows()
        {
            _updatingSelection = true;
            try
            {
                foreach (MemoArchiveRestoreItem item in _items)
                {
                    MemoArchiveBackupParsedItem source = item.Source;
                    bool selectable = item.Status == MemoArchiveRestoreStatus.New;
                    string mapAndGameType = BuildMapAndGameType(
                        source.Kind == MemoArchiveBackupKind.RaidNote
                            ? source.RaidNote.MapName
                            : source.UserReportMemo.MapName,
                        source.Kind == MemoArchiveBackupKind.RaidNote
                            ? source.RaidNote.GameType
                            : source.UserReportMemo.GameType);
                    int rowIndex = _grid.Rows.Add(
                        selectable && item.Selected,
                        GetStatusText(item.Status),
                        source.Kind == MemoArchiveBackupKind.RaidNote
                            ? AppText.Get("NoteArchive.Kind.Raid")
                            : AppText.Get("NoteArchive.Kind.PlayerReport"),
                        Empty(source.Kind == MemoArchiveBackupKind.RaidNote
                            ? source.RaidNote.Game
                            : source.UserReportMemo.Game),
                        mapAndGameType,
                        FormatDate(source.Kind == MemoArchiveBackupKind.RaidNote
                            ? source.RaidNote.RaidStartedUtc
                            : source.UserReportMemo.RaidStartedUtc),
                        CreateSafePreview(source.PreviewText),
                        LocalizeRestoreDetail(
                            item.Detail,
                            "NoteRestore.Detail.ItemFailed"));
                    DataGridViewRow row = _grid.Rows[rowIndex];
                    row.Tag = item;
                    row.Cells["map"].ToolTipText = mapAndGameType;
                    row.Cells["selected"].ReadOnly = !selectable;
                    row.Cells["status"].Style.ForeColor = item.Status == MemoArchiveRestoreStatus.New
                        ? Success
                        : item.Status == MemoArchiveRestoreStatus.Existing ? Warning : Danger;
                    if (!selectable) row.DefaultCellStyle.ForeColor = TextMuted;
                }
            }
            finally
            {
                _updatingSelection = false;
            }
        }

        private string BuildCountText()
        {
            int raidCount = _items.Count(item => item.Source.Kind == MemoArchiveBackupKind.RaidNote);
            int reportCount = _items.Count - raidCount;
            int newCount = _grid == null
                ? _items.Count(item => item.Status == MemoArchiveRestoreStatus.New)
                : GetSelectableRows().Count();
            int existingCount = _skippedItems.Count;
            return AppText.Format(
                "NoteRestore.Count",
                _items.Count,
                raidCount,
                reportCount,
                newCount,
                existingCount);
        }

        private void GridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (_busy || e.Button != MouseButtons.Left || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "selected") return;
            CommitCurrentCheckBoxEdit();
            bool selectAll = GetSelectableRows().Any(row => !IsRowChecked(row));
            SetAllSelectable(selectAll);
        }

        private void GridKeyDown(object sender, KeyEventArgs e)
        {
            if (_busy || !e.Control || e.KeyCode != Keys.A) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            SetAllSelectable(true);
        }

        private void SetAllSelectable(bool selected)
        {
            if (_busy) return;
            CommitCurrentCheckBoxEdit();
            _updatingSelection = true;
            try
            {
                foreach (DataGridViewRow row in GetSelectableRows())
                {
                    row.Cells["selected"].Value = selected;
                    MemoArchiveRestoreItem item = row.Tag as MemoArchiveRestoreItem;
                    if (item != null) item.Selected = selected;
                    _grid.InvalidateCell(row.Cells["selected"]);
                }
            }
            finally
            {
                _updatingSelection = false;
            }
            UpdateSelectionState();
        }

        private IEnumerable<DataGridViewRow> GetSelectableRows()
        {
            return _grid.Rows.Cast<DataGridViewRow>()
                .Where(row => !row.Cells["selected"].ReadOnly);
        }

        private void CommitCurrentCheckBoxEdit()
        {
            if (_grid == null || !_grid.IsCurrentCellInEditMode) return;
            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            _grid.EndEdit();
        }

        private void SyncItemSelection(DataGridViewRow row)
        {
            MemoArchiveRestoreItem item = row == null ? null : row.Tag as MemoArchiveRestoreItem;
            if (item == null) return;
            item.Selected = !row.Cells["selected"].ReadOnly && IsRowChecked(row);
        }

        private void UpdateSelectionState()
        {
            if (_grid == null) return;
            int selectableCount = GetSelectableRows().Count();
            int selectedCount = GetSelectableRows().Count(IsRowChecked);
            _grid.AccessibleDescription = AppText.Format(
                "NoteRestore.Selection.Description", selectableCount, selectedCount);
            if (_applyButton != null)
            {
                _applyButton.Enabled = !_busy && selectedCount > 0;
                _applyButton.BackColor = _applyButton.Enabled ? Success : SurfaceAlt;
                _applyButton.ForeColor = _applyButton.Enabled
                    ? Color.FromArgb(18, 36, 27)
                    : TextMuted;
                _applyButton.AccessibleDescription = selectedCount > 0
                    ? AppText.Format(
                        "NoteRestore.Selection.ApplyCount", selectedCount)
                    : AppText.Get("NoteRestore.Selection.None");
            }
            if (_selectAllButton != null) _selectAllButton.Enabled = !_busy && selectableCount > 0;
            if (_selectNoneButton != null)
                _selectNoneButton.Enabled = !_busy && selectedCount > 0;
            if (_grid.Columns.Contains("selected"))
                _grid.InvalidateCell(_grid.Columns["selected"].HeaderCell);
        }

        private async void ApplyClicked(object sender, EventArgs e)
        {
            if (_busy) return;
            CommitCurrentCheckBoxEdit();
            foreach (DataGridViewRow row in _grid.Rows) SyncItemSelection(row);
            if (!GetSelectableRows().Any(IsRowChecked)) return;

            ApplyAttempted = true;
            ApplyCycleCompleted = false;
            SetBusy(true);
            MemoArchiveRestoreResult result;
            try
            {
                result = await Task.Run(() => _apply(_items)).ConfigureAwait(false);
            }
            catch
            {
                result = null;
            }

            DispatchApplyCompletion(result);
        }

        private void DispatchApplyCompletion(MemoArchiveRestoreResult result)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke((Action)(() => CompleteApply(result)));
                    return;
                }
                CompleteApply(result);
            }
            catch (InvalidOperationException)
            {
                // The preview was disposed after the background restore completed.
            }
        }

        private void CompleteApply(MemoArchiveRestoreResult result)
        {
            if (IsDisposed || Disposing) return;
            LastCompletionThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (result == null)
            {
                foreach (DataGridViewRow row in GetSelectableRows().Where(IsRowChecked))
                {
                    MemoArchiveRestoreItem item = row.Tag as MemoArchiveRestoreItem;
                    if (item == null) continue;
                    _failedItems.Add(item);
                    row.Cells["status"].Value = AppText.Get("NoteRestore.Status.Retry");
                    row.Cells["status"].Style.ForeColor = Danger;
                    row.Cells["detail"].Value = AppText.Get(
                        "NoteRestore.Detail.SelectionFailed");
                }
                _resultLabel.Text = ResultSummary;
                _resultLabel.AccessibleDescription = _resultLabel.Text;
                _resultLabel.ForeColor = Danger;
                SetBusy(false);
                UpdateCountLabel();
                ApplyCycleCompleted = true;
                return;
            }

            LastResult = result;
            ApplyResultToRows(result);
            _resultLabel.Text = ResultSummary;
            _resultLabel.AccessibleDescription = _resultLabel.Text;
            _resultLabel.ForeColor = FailedCount > 0
                ? Warning
                : AddedCount > 0 ? Success : TextMuted;
            SetBusy(false);
            UpdateCountLabel();
            if (FailedCount == 0)
            {
                DialogResult = DialogResult.OK;
                if (!Modal) Hide();
            }
            ApplyCycleCompleted = true;
        }

        private void ApplyResultToRows(MemoArchiveRestoreResult result)
        {
            if (result == null || result.ItemResults == null) return;
            foreach (MemoArchiveRestoreItemResult itemResult in result.ItemResults)
            {
                MemoArchiveRestoreItem item = itemResult == null ? null : itemResult.Item;
                DataGridViewRow row = FindRow(item);
                if (item == null || row == null) continue;
                if (itemResult.Added)
                {
                    if (_completedItems.Add(item)) AddedCount++;
                    _failedItems.Remove(item);
                    item.Selected = false;
                    row.Cells["selected"].Value = false;
                    row.Cells["selected"].ReadOnly = true;
                    row.Cells["status"].Value = AppText.Get("NoteRestore.Status.Added");
                    row.Cells["status"].Style.ForeColor = Success;
                    row.Cells["detail"].Value = AppText.Get("NoteRestore.Detail.Added");
                    row.DefaultCellStyle.ForeColor = TextMuted;
                }
                else if (itemResult.Skipped)
                {
                    _skippedItems.Add(item);
                    _failedItems.Remove(item);
                    item.Selected = false;
                    row.Cells["selected"].Value = false;
                    row.Cells["selected"].ReadOnly = true;
                    row.Cells["status"].Value = AppText.Get("NoteRestore.Status.Existing");
                    row.Cells["status"].Style.ForeColor = Warning;
                    row.Cells["detail"].Value = AppText.Get("NoteRestore.Detail.Skipped");
                    row.DefaultCellStyle.ForeColor = TextMuted;
                }
                else
                {
                    _failedItems.Add(item);
                    bool retryable = item.Status == MemoArchiveRestoreStatus.New;
                    item.Selected = retryable;
                    row.Cells["selected"].ReadOnly = !retryable;
                    row.Cells["selected"].Value = retryable;
                    row.Cells["status"].Value = retryable
                        ? AppText.Get("NoteRestore.Status.Retry")
                        : item.Status == MemoArchiveRestoreStatus.ExistingConflict
                            ? AppText.Get("NoteRestore.Status.Conflict")
                            : AppText.Get("NoteRestore.Status.Failed");
                    row.Cells["status"].Style.ForeColor = Danger;
                    row.Cells["detail"].Value = string.IsNullOrWhiteSpace(itemResult.ErrorMessage)
                        ? AppText.Get("NoteRestore.Detail.ItemFailed")
                        : LocalizeRestoreDetail(
                            itemResult.ErrorMessage,
                            "NoteRestore.Detail.ItemFailed");
                }
            }
        }

        private DataGridViewRow FindRow(MemoArchiveRestoreItem item)
        {
            return _grid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(row => ReferenceEquals(row.Tag, item));
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _grid.Enabled = !busy;
            if (_closeButton != null) _closeButton.Enabled = !busy;
            UseWaitCursor = busy;
            UpdateSelectionState();
        }

        private void UpdateCountLabel()
        {
            if (_countLabel != null) _countLabel.Text = BuildCountText();
        }

        private void PaintSelectionHeader(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0
                || _grid.Columns[e.ColumnIndex].Name != "selected") return;
            e.Paint(e.CellBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
            PaintHeaderCheckBox(
                e.Graphics,
                e.CellBounds,
                GetHeaderSelectionState(),
                _grid.Enabled);
            e.Handled = true;
        }

        private HeaderSelectionState GetHeaderSelectionState()
        {
            IList<DataGridViewRow> selectable = GetSelectableRows().ToList();
            if (selectable.Count == 0) return HeaderSelectionState.None;
            int selected = selectable.Count(IsRowChecked);
            if (selected == 0) return HeaderSelectionState.None;
            return selected == selectable.Count ? HeaderSelectionState.All : HeaderSelectionState.Partial;
        }

        private static void PaintHeaderCheckBox(
            Graphics graphics,
            Rectangle bounds,
            HeaderSelectionState state,
            bool enabled)
        {
            float scale = Math.Max(1F, graphics.DpiX / 96F);
            int size = Math.Max(15, (int)Math.Round(16F * scale));
            var box = new Rectangle(
                bounds.Left + (bounds.Width - size) / 2,
                bounds.Top + (bounds.Height - size) / 2,
                size,
                size);
            using (var fill = new SolidBrush(
                state == HeaderSelectionState.None ? Surface : enabled ? Accent : Border))
            using (var outline = new Pen(enabled ? TextMuted : Border, Math.Max(1F, scale)))
            {
                graphics.FillRectangle(fill, box);
                graphics.DrawRectangle(outline, box.Left, box.Top, box.Width - 1, box.Height - 1);
            }
            if (state == HeaderSelectionState.None) return;
            SmoothingMode oldMode = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var mark = new Pen(
                    enabled ? Background : TextMuted,
                    Math.Max(2F, 2.2F * scale)))
                {
                    mark.StartCap = LineCap.Round;
                    mark.EndCap = LineCap.Round;
                    if (state == HeaderSelectionState.Partial)
                    {
                        int y = box.Top + box.Height / 2;
                        graphics.DrawLine(mark, box.Left + 4, y, box.Right - 5, y);
                    }
                    else
                    {
                        graphics.DrawLines(mark, new[]
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

        private static bool IsRowChecked(DataGridViewRow row)
        {
            return row != null && Convert.ToBoolean(row.Cells["selected"].Value ?? false);
        }

        private static string GetStatusText(MemoArchiveRestoreStatus status)
        {
            if (status == MemoArchiveRestoreStatus.New)
                return AppText.Get("NoteRestore.Status.New");
            if (status == MemoArchiveRestoreStatus.Existing)
                return AppText.Get("NoteRestore.Status.Existing");
            if (status == MemoArchiveRestoreStatus.ExistingConflict)
                return AppText.Get("NoteRestore.Status.Conflict");
            return AppText.Get("NoteRestore.Status.Unavailable");
        }

        private static string FormatDate(DateTime value)
        {
            return value == default(DateTime)
                ? "-"
                : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
        }

        private static string Empty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value;
        }

        private static string LocalizeRestoreDetail(string detail, string fallbackKey)
        {
            if (string.IsNullOrWhiteSpace(detail)) return AppText.Get(fallbackKey);
            if (detail == "없는 메모이므로 새로 추가할 수 있습니다.")
                return AppText.Get("NoteRestore.Detail.New");
            if (detail == "같은 종류와 키의 기존 메모가 있어 건너뜁니다.")
                return AppText.Get("NoteRestore.Detail.Exists");
            if (detail == "현재 저장소 상태를 확인하지 못했습니다.")
                return AppText.Get("NoteRestore.Detail.StoreUnavailable");
            if (detail == "같은 메모가 먼저 저장되어 안전하게 건너뛰었습니다.")
                return AppText.Get("NoteRestore.Detail.RaceSkipped");
            if (detail == "적용 전에 같은 종류와 키의 다른 메모가 생겨 덮어쓰지 않았습니다.")
                return AppText.Get("NoteRestore.Detail.RaceConflict");
            if (string.Equals(
                AppText.CurrentLanguage,
                AppText.KoreanLanguage,
                StringComparison.OrdinalIgnoreCase))
                return detail;
            for (int index = 0; index < detail.Length; index++)
            {
                char value = detail[index];
                if ((value >= '\u3131' && value <= '\u318E')
                    || (value >= '\uAC00' && value <= '\uD7A3'))
                    return AppText.Get(fallbackKey);
            }
            return detail;
        }

        private static string BuildMapAndGameType(string mapName, string gameType)
        {
            string map = Empty(mapName);
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

        private static string CreateSafePreview(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return AppText.Get("NoteRestore.EmptyPreview");
            string compact = string.Join(
                " ",
                value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Trim())
                    .Where(part => part.Length > 0));
            if (compact.Length == 0) return AppText.Get("NoteRestore.EmptyPreview");
            return compact.Length <= 100 ? compact : compact.Substring(0, 97) + "…";
        }

        private static Button CreateButton(string text, int width, bool emphasized)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(width, 32),
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = emphasized ? Success : SurfaceAlt,
                ForeColor = emphasized ? Color.FromArgb(18, 36, 27) : TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = emphasized
                ? Color.FromArgb(42, 137, 87)
                : Border;
            return button;
        }

        private sealed class MemoSelectionHeaderCell : DataGridViewColumnHeaderCell
        {
            public MemoSelectionHeaderCell()
            {
                ToolTipText = AppText.Get("NoteRestore.Header.Tooltip");
            }

            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new MemoSelectionHeaderAccessibleObject(this);
            }

            public override object Clone()
            {
                return (MemoSelectionHeaderCell)base.Clone();
            }

            private sealed class MemoSelectionHeaderAccessibleObject
                : DataGridViewColumnHeaderCellAccessibleObject
            {
                private readonly MemoSelectionHeaderCell _owner;

                internal MemoSelectionHeaderAccessibleObject(MemoSelectionHeaderCell owner)
                    : base(owner)
                {
                    _owner = owner;
                }

                public override string Name
                {
                    get { return AppText.Get("NoteRestore.Header.Name"); }
                }

                public override string Description
                {
                    get
                    {
                        DataGridView grid = _owner.DataGridView;
                        if (grid == null)
                            return AppText.Get("NoteRestore.Header.DescriptionEmpty");
                        int selectable = grid.Rows.Cast<DataGridViewRow>()
                            .Count(row => !row.Cells["selected"].ReadOnly);
                        int selected = grid.Rows.Cast<DataGridViewRow>()
                            .Count(row => !row.Cells["selected"].ReadOnly && IsRowChecked(row));
                        return AppText.Format(
                            "NoteRestore.Header.Description", selectable, selected);
                    }
                }

                public override string DefaultAction
                {
                    get
                    {
                        DataGridView grid = _owner.DataGridView;
                        if (grid == null)
                            return AppText.Get("BlockedServers.Selection.SelectAll");
                        IList<DataGridViewRow> selectable = grid.Rows.Cast<DataGridViewRow>()
                            .Where(row => !row.Cells["selected"].ReadOnly)
                            .ToList();
                        return selectable.Count > 0 && selectable.All(IsRowChecked)
                            ? AppText.Get("BlockedServers.Selection.ClearAll")
                            : AppText.Get("BlockedServers.Selection.SelectAll");
                    }
                }

                public override AccessibleStates State
                {
                    get
                    {
                        DataGridView grid = _owner.DataGridView;
                        if (grid == null) return base.State;
                        IList<DataGridViewRow> selectable = grid.Rows.Cast<DataGridViewRow>()
                            .Where(row => !row.Cells["selected"].ReadOnly)
                            .ToList();
                        int selected = selectable.Count(IsRowChecked);
                        AccessibleStates selection = AccessibleStates.None;
                        if (selected > 0)
                        {
                            selection = selected == selectable.Count
                                ? AccessibleStates.Checked
                                : AccessibleStates.Mixed;
                        }
                        return base.State | selection;
                    }
                }

                public override void DoDefaultAction()
                {
                    DataGridView grid = _owner.DataGridView;
                    var form = grid == null
                        ? null
                        : grid.FindForm() as MemoArchiveRestorePreviewForm;
                    if (form == null || form._busy) return;
                    IList<DataGridViewRow> selectable = grid.Rows.Cast<DataGridViewRow>()
                        .Where(row => !row.Cells["selected"].ReadOnly)
                        .ToList();
                    form.SetAllSelectable(
                        selectable.Count == 0 || selectable.Any(row => !IsRowChecked(row)));
                }
            }
        }
    }
}
