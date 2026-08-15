using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class RaidNoteArchiveForm : Form
    {
        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color Danger = Color.FromArgb(192, 68, 75);

        private readonly RaidNoteStore _store;
        private readonly UserReportMemoStore _userReportStore;
        private readonly List<ArchiveItem> _records = new List<ArchiveItem>();
        private DataGridView _grid;
        private Label _statusLabel;
        private Button _openButton;
        private Button _deleteButton;

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
            ClientSize = new Size(980, 600);
            MinimumSize = new Size(720, 460);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowInTaskbar = false;
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
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(1, 0),
                Text = "메모 보관함",
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(3, 34),
                Text = "게임 로그가 삭제된 뒤에도 일반 레이드 메모와 유저신고 메모를 확인할 수 있습니다.",
                Font = new Font("Malgun Gothic", 8.5F),
                ForeColor = TextMuted
            });
            return panel;
        }

        private Control BuildGrid()
        {
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true,
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
            _grid.Columns.Add(CreateColumn("kind", "종류", 104));
            _grid.Columns.Add(CreateColumn("date", "레이드 시각", 145));
            _grid.Columns.Add(CreateColumn("game", "게임", 64));
            _grid.Columns.Add(CreateColumn("map", "맵", 120));
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "preview",
                HeaderText = "메모 미리보기",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 220,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(CreateColumn("tags", "태그/신고", 130));
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "updated",
                HeaderText = "최근 수정",
                Width = 150,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.SelectionChanged += delegate { UpdateButtons(); };
            _grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex >= 0) OpenSelected();
            };
            return _grid;
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
            _deleteButton.Click += delegate { DeleteSelected(); };
            Button folder = CreateButton("저장 폴더 열기", 126, SurfaceAlt, TextPrimary);
            folder.Click += delegate { OpenFolder(); };
            Button refresh = CreateButton("새로고침", 90, SurfaceAlt, TextPrimary);
            refresh.Click += delegate { RefreshRecords(); };
            buttons.Controls.Add(_openButton);
            buttons.Controls.Add(_deleteButton);
            buttons.Controls.Add(folder);
            buttons.Controls.Add(refresh);
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
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private static Button CreateButton(string text, int width, Color backColor, Color foreColor)
        {
            var button = new Button
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

        private void RefreshRecords()
        {
            ArchiveItem selected = GetSelectedRecord();
            string selectedIdentity = selected == null ? null : selected.Identity;
            try
            {
                IList<RaidNoteRecord> raidNotes = _store.LoadAll();
                IList<UserReportMemoRecord> reportMemos = _userReportStore.LoadAll();
                _records.Clear();
                _records.AddRange(raidNotes.Select(ArchiveItem.ForRaidNote));
                _records.AddRange(reportMemos.Select(ArchiveItem.ForUserReport));
                _records.Sort(ArchiveItem.CompareNewestFirst);
                _grid.Rows.Clear();
                foreach (ArchiveItem record in _records)
                {
                    int rowIndex = _grid.Rows.Add(
                        record.KindLabel,
                        FormatDate(record.RaidStartedUtc),
                        EmptyFallback(record.Game),
                        EmptyFallback(record.MapName),
                        BuildNotePreview(record.NoteText, record.IsUserReport),
                        record.IsUserReport
                            ? "신고 " + record.ReportCount + "건"
                            : Summarize(record.Tags, 4),
                        FormatDate(record.UpdatedUtc));
                    DataGridViewRow row = _grid.Rows[rowIndex];
                    row.Tag = record;
                    row.Cells["preview"].ToolTipText = BuildFullNoteToolTip(
                        record.NoteText, record.IsUserReport);
                    row.Cells["tags"].ToolTipText = record.IsUserReport
                        ? "유저신고 " + record.ReportCount + "건"
                        : JoinAll(record.Tags);
                    if (!string.IsNullOrWhiteSpace(selectedIdentity)
                        && string.Equals(record.Identity, selectedIdentity, StringComparison.OrdinalIgnoreCase))
                        row.Selected = true;
                }
                if (_grid.SelectedRows.Count == 0 && _grid.Rows.Count > 0)
                    _grid.Rows[0].Selected = true;
                if (_records.Count == 0)
                    SetStatus("저장된 메모가 없습니다.", TextMuted);
                else
                    SetStatus(
                        "저장된 메모 " + _records.Count + "개 · 레이드 "
                        + raidNotes.Count + "개 · 유저신고 " + reportMemos.Count + "개",
                        TextMuted);
            }
            catch (Exception ex)
            {
                SetStatus("메모 목록을 불러오지 못했습니다: " + ex.Message, Danger);
            }
            UpdateButtons();
        }

        private ArchiveItem GetSelectedRecord()
        {
            if (_grid == null || _grid.SelectedRows.Count == 0) return null;
            return _grid.SelectedRows[0].Tag as ArchiveItem;
        }

        private void OpenSelected()
        {
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

        private void OpenFolder()
        {
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

        private void UpdateButtons()
        {
            bool selected = GetSelectedRecord() != null;
            if (_openButton != null) _openButton.Enabled = selected;
            if (_deleteButton != null) _deleteButton.Enabled = selected;
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
