// SPDX-License-Identifier: MPL-2.0
// Copyright 2026 Spirit-Schema

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public static class RaidNoteUi
    {
        public static bool ShowFor(IWin32Window owner, ServerSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            using (var form = new RaidNoteForm(session, new RaidNoteStore()))
            {
                form.ShowDialog(owner);
                return form.Changed;
            }
        }

        public static bool HasNote(ServerSession session)
        {
            if (session == null) return false;
            return new RaidNoteStore().Exists(session);
        }

        public static bool ShowArchive(IWin32Window owner)
        {
            using (var form = new RaidNoteArchiveForm(new RaidNoteStore()))
            {
                form.ShowDialog(owner);
                return form.Changed;
            }
        }
    }

    public sealed class RaidNoteForm : BrandedForm
    {
        internal const string DefaultNoteTemplate = RaidNoteStore.LegacyDefaultNoteTemplate;
        internal const string NicknamePlaceholderText = "유저닉네임\r\n1.\r\n2.\r\n3.";

        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color Danger = Color.FromArgb(192, 68, 75);

        private readonly ServerSession _session;
        private readonly RaidNoteStore _store;
        private RaidNoteRecord _record;
        private TextBox _noteTextBox;
        private Label _notePlaceholderLabel;
        private Label _nicknamePlaceholderLabel;
        private ListBox _screenshotList;
        private TextBox _tagTextBox;
        private Label _timestampLabel;
        private Label _statusLabel;
        private bool _loading;
        private bool _dirty;

        public RaidNoteForm(ServerSession session, RaidNoteStore store)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (store == null) throw new ArgumentNullException("store");
            _session = session;
            _store = store;
            _record = _store.Load(session) ?? _store.CreateFor(session);
            InitializeWindow();
            BuildInterface();
            LoadRecord();
            FormClosing += RaidNoteFormClosing;
        }

        public RaidNoteForm(RaidNoteRecord record, RaidNoteStore store)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (store == null) throw new ArgumentNullException("store");
            if (string.IsNullOrWhiteSpace(record.Key))
                throw new ArgumentException("저장된 메모 키가 없습니다.", "record");
            _session = null;
            _store = store;
            _record = record;
            InitializeWindow();
            BuildInterface();
            LoadRecord();
            FormClosing += RaidNoteFormClosing;
        }

        public bool Changed { get; private set; }

        private void InitializeWindow()
        {
            Text = "레이드 메모";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 750);
            MinimumSize = new Size(680, 620);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowInTaskbar = false;
            Shown += delegate
            {
                BeginInvoke(new Action(delegate
                {
                    if (_noteTextBox == null) return;
                    _noteTextBox.Focus();
                    _noteTextBox.SelectionStart = 0;
                    _noteTextBox.SelectionLength = 0;
                    _noteTextBox.ScrollToCaret();
                }));
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 168F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildNoteSection(), 0, 1);
            root.Controls.Add(BuildScreenshotSection(), 0, 2);
            root.Controls.Add(BuildTagSection(), 0, 3);
            root.Controls.Add(BuildFooter(), 0, 4);
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(1, 0),
                Text = "레이드 메모",
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(3, 33),
                Text = BuildRaidSummary(),
                Font = new Font("Malgun Gothic", 8.5F),
                ForeColor = TextMuted
            });
            return panel;
        }

        private Control BuildNoteSection()
        {
            TableLayoutPanel section = CreateSection("자유메모", 24);
            var noteHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceAlt,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _noteTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 10F),
                MaxLength = 200000,
                Margin = new Padding(0)
            };
            _noteTextBox.TextChanged += delegate
            {
                if (!_loading && _noteTextBox.TextLength > 0) HideNotePlaceholders();
                MarkDirty();
            };
            _noteTextBox.MouseDown += delegate { HideNotePlaceholders(); };
            noteHost.Controls.Add(_noteTextBox);

            _notePlaceholderLabel = new Label
            {
                AutoSize = false,
                Location = new Point(8, 5),
                Height = 26,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = SurfaceAlt,
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 8.5F),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Cursor = Cursors.IBeam,
                UseMnemonic = false,
                Text = "예시) 귀중품 파밍 장소, 이동 동선, 저격 포인트, 보스 스폰 위치 등을 자유롭게 기록해 보세요."
            };
            _notePlaceholderLabel.Click += delegate
            {
                HideNotePlaceholders();
                _noteTextBox.Focus();
            };

            int noteLineHeight = TextRenderer.MeasureText(
                "Ag", _noteTextBox.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
            _nicknamePlaceholderLabel = new Label
            {
                AutoSize = true,
                Location = new Point(8, 5 + (noteLineHeight * 9)),
                BackColor = SurfaceAlt,
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 10F),
                TextAlign = ContentAlignment.TopLeft,
                Cursor = Cursors.IBeam,
                UseMnemonic = false,
                Text = NicknamePlaceholderText
            };
            _nicknamePlaceholderLabel.Click += delegate
            {
                HideNotePlaceholders();
                _noteTextBox.Focus();
            };
            _noteTextBox.Resize += delegate
            {
                if (_notePlaceholderLabel != null)
                    _notePlaceholderLabel.Width = Math.Max(0, _noteTextBox.ClientSize.Width - 34);
            };
            _noteTextBox.Controls.Add(_notePlaceholderLabel);
            _noteTextBox.Controls.Add(_nicknamePlaceholderLabel);
            _notePlaceholderLabel.BringToFront();
            _nicknamePlaceholderLabel.BringToFront();
            section.Controls.Add(noteHost, 0, 1);
            return section;
        }

        private void HideNotePlaceholders()
        {
            SetNotePlaceholdersVisible(false);
        }

        private void SetNotePlaceholdersVisible(bool visible)
        {
            if (_notePlaceholderLabel != null)
            {
                _notePlaceholderLabel.Visible = visible;
                if (visible) _notePlaceholderLabel.BringToFront();
            }
            if (_nicknamePlaceholderLabel != null)
            {
                _nicknamePlaceholderLabel.Visible = visible;
                if (visible) _nicknamePlaceholderLabel.BringToFront();
            }
        }

        private Control BuildScreenshotSection()
        {
            TableLayoutPanel section = CreateSection(
                "스크린샷 첨부 · 파일은 복사하거나 업로드하지 않고 경로만 저장합니다",
                24);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            section.Controls.Add(layout, 0, 1);

            _screenshotList = CreateListBox();
            _screenshotList.HorizontalScrollbar = true;
            _screenshotList.DoubleClick += delegate { OpenSelectedScreenshot(); };
            layout.Controls.Add(_screenshotList, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 5, 0, 5),
                Margin = new Padding(0)
            };
            Button attach = CreateSmallButton("첨부");
            attach.Click += delegate { AttachScreenshots(); };
            Button open = CreateSmallButton("열기");
            open.Click += delegate { OpenSelectedScreenshot(); };
            Button openFolder = CreateSmallButton("폴더 열기");
            openFolder.Click += delegate { OpenSelectedScreenshotFolder(); };
            Button detach = CreateSmallButton("첨부 해제");
            detach.Click += delegate { DetachSelectedScreenshot(); };
            buttons.Controls.Add(attach);
            buttons.Controls.Add(open);
            buttons.Controls.Add(openFolder);
            buttons.Controls.Add(detach);
            layout.Controls.Add(buttons, 0, 1);
            return section;
        }

        private Control BuildTagSection()
        {
            TableLayoutPanel section = CreateSection("태그 (,)쉼표로 구분", 24);
            _tagTextBox = CreateTextBox();
            _tagTextBox.MaxLength = 6499;
            _tagTextBox.TextChanged += delegate { MarkDirty(); };
            section.Controls.Add(_tagTextBox, 0, 1);
            return section;
        }

        private Control BuildFooter()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0, 8, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _timestampLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                AutoEllipsis = true
            };
            layout.Controls.Add(_timestampLabel, 0, 0);
            layout.SetColumnSpan(_timestampLabel, 2);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                AutoEllipsis = true,
                Text = "이 메모는 게임 로그와 별도로 로컬에 보관됩니다."
            };
            layout.Controls.Add(_statusLabel, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                BackColor = Background,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 1, 0, 0),
                Margin = new Padding(8, 0, 0, 0)
            };
            Button save = CreateSmallButton("저장");
            save.BackColor = Accent;
            save.ForeColor = Color.FromArgb(29, 24, 17);
            save.FlatAppearance.BorderColor = Accent;
            save.Click += delegate { SaveNote(true); };
            Button delete = CreateSmallButton("삭제");
            delete.BackColor = Danger;
            delete.FlatAppearance.BorderColor = Danger;
            delete.Click += delegate { DeleteNote(); };
            Button folder = CreateSmallButton("메모 저장 폴더 열기");
            folder.Click += delegate { OpenNoteFolder(); };
            buttons.Controls.Add(save);
            buttons.Controls.Add(delete);
            buttons.Controls.Add(folder);
            layout.Controls.Add(buttons, 1, 1);
            return layout;
        }

        private static TableLayoutPanel CreateSection(string text, int labelHeight)
        {
            var section = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 3, 0, 3),
                Padding = new Padding(0)
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            section.RowStyles.Add(new RowStyle(SizeType.Absolute, labelHeight));
            section.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            section.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                AutoEllipsis = true,
                Margin = new Padding(1, 0, 0, 0)
            }, 0, 0);
            return section;
        }

        private static TextBox CreateTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F),
                Margin = new Padding(0, 0, 0, 4)
            };
        }

        private static ListBox CreateListBox()
        {
            return new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F),
                IntegralHeight = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private static Button CreateSmallButton(string text)
        {
            var button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.None,
                MinimumSize = new Size(82, 30),
                Padding = new Padding(12, 0, 12, 0),
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                Margin = new Padding(0, 0, 7, 0)
            };
            button.FlatAppearance.BorderColor = Border;
            return button;
        }

        private void LoadRecord()
        {
            _loading = true;
            try
            {
                string normalizedNote = RaidNoteStore.NormalizeLegacyNoteText(_record.NoteText);
                _record.NoteText = normalizedNote;
                bool emptyNoteText = string.IsNullOrWhiteSpace(normalizedNote);
                _noteTextBox.Text = normalizedNote;
                SetNotePlaceholdersVisible(emptyNoteText);
                if (emptyNoteText)
                {
                    _noteTextBox.SelectionStart = 0;
                    _noteTextBox.SelectionLength = 0;
                    _noteTextBox.ScrollToCaret();
                }
                _screenshotList.Items.Clear();
                foreach (string path in _record.ScreenshotPaths ?? new List<string>())
                    _screenshotList.Items.Add(path);
                _tagTextBox.Text = string.Join(", ", _record.Tags ?? new List<string>());
                UpdateTimestampText();
                _dirty = false;
            }
            finally
            {
                _loading = false;
            }
        }

        private string BuildRaidSummary()
        {
            if (_session != null)
            {
                string game = _session.GameDisplayName;
                string map = string.IsNullOrWhiteSpace(_session.MapName) ? "-" : _session.MapName;
                string type = _session.Game == TarkovGame.Eft
                    ? _session.RaidTypeText
                    : _session.GameMode;
                string mapAndType = string.IsNullOrWhiteSpace(type) ? map : map + " · " + type;
                return string.Format("{0} · {1:yyyy-MM-dd HH:mm:ss} · {2}", game, _session.DisplayDetectedAt, mapAndType);
            }
            DateTime started = _record.RaidStartedUtc == default(DateTime)
                ? default(DateTime)
                : _record.RaidStartedUtc.ToLocalTime();
            string storedDate = started == default(DateTime) ? "시각 확인 안 됨" : started.ToString("yyyy-MM-dd HH:mm:ss");
            return string.Format("{0} · {1} · {2}",
                string.IsNullOrWhiteSpace(_record.Game) ? "-" : _record.Game,
                storedDate,
                string.IsNullOrWhiteSpace(_record.MapName) ? "-" : _record.MapName);
        }

        private void AttachScreenshots()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "스크린샷 첨부";
                dialog.Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|모든 파일|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var existing = new HashSet<string>(
                    _screenshotList.Items.Cast<object>().Select(Convert.ToString),
                    StringComparer.OrdinalIgnoreCase);
                foreach (string selected in dialog.FileNames)
                {
                    string fullPath;
                    try { fullPath = Path.GetFullPath(selected); }
                    catch { continue; }
                    if (existing.Add(fullPath)) _screenshotList.Items.Add(fullPath);
                }
                MarkDirty();
            }
        }

        private void DetachSelectedScreenshot()
        {
            int index = _screenshotList.SelectedIndex;
            if (index < 0) return;
            _screenshotList.Items.RemoveAt(index);
            MarkDirty();
        }

        private void OpenSelectedScreenshot()
        {
            string path = GetSelectedScreenshotPath();
            if (path == null) return;
            if (!File.Exists(path))
            {
                ShowStatus("첨부한 파일을 찾을 수 없습니다. 경로가 이동되었는지 확인해 주세요.", Danger);
                return;
            }
            TryOpen(path, null);
        }

        private void OpenSelectedScreenshotFolder()
        {
            string path = GetSelectedScreenshotPath();
            if (path == null) return;
            if (File.Exists(path))
                TryOpen("explorer.exe", "/select,\"" + path.Replace("\"", string.Empty) + "\"");
            else
            {
                string directory = null;
                try { directory = Path.GetDirectoryName(path); }
                catch { }
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    ShowStatus("첨부 경로의 폴더를 찾을 수 없습니다.", Danger);
                    return;
                }
                TryOpen("explorer.exe", "\"" + directory.Replace("\"", string.Empty) + "\"");
            }
        }

        private string GetSelectedScreenshotPath()
        {
            return _screenshotList.SelectedItem == null
                ? null
                : Convert.ToString(_screenshotList.SelectedItem);
        }

        private void SaveNote(bool closeAfterSave)
        {
            try
            {
                _record.NoteText = RaidNoteStore.NormalizeLegacyNoteText(
                    _noteTextBox.Text ?? string.Empty);
                _record.ScreenshotPaths = _screenshotList.Items.Cast<object>()
                    .Select(Convert.ToString).ToList();
                _record.Tags = SplitTags(_tagTextBox.Text);
                if (_session == null)
                    _store.Save(_record.Key, _record);
                else
                    _store.Save(_session, _record);
                Changed = true;
                _dirty = false;
                UpdateTimestampText();
                ShowStatus("메모를 저장했습니다.", Accent);
                if (closeAfterSave) Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "메모를 저장하지 못했습니다.\r\n" + ex.Message,
                    "레이드 메모", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteNote()
        {
            bool exists = _session == null
                ? _store.Exists(_record.Key)
                : _store.Exists(_session);
            if (!exists && !_dirty)
            {
                ShowStatus("삭제할 저장 메모가 없습니다.", TextMuted);
                return;
            }
            DialogResult answer = MessageBox.Show(
                this,
                "이 레이드의 메모를 삭제할까요? 첨부한 원본 스크린샷은 삭제하지 않습니다.",
                "레이드 메모 삭제",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
            try
            {
                if (_session == null)
                    _store.Delete(_record.Key);
                else
                    _store.Delete(_session);
                Changed = true;
                _dirty = false;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "메모를 삭제하지 못했습니다.\r\n" + ex.Message,
                    "레이드 메모", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenNoteFolder()
        {
            try { _store.OpenNotesFolder(); }
            catch (Exception ex) { ShowStatus("메모 폴더를 열지 못했습니다: " + ex.Message, Danger); }
        }

        private void RaidNoteFormClosing(object sender, FormClosingEventArgs args)
        {
            if (!_dirty) return;
            DialogResult answer = MessageBox.Show(
                this,
                "저장하지 않은 변경 사항이 있습니다. 저장하고 닫을까요?",
                "레이드 메모",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                args.Cancel = true;
                return;
            }
            if (answer == DialogResult.Yes)
            {
                SaveNote(false);
                if (_dirty) args.Cancel = true;
            }
        }

        private static List<string> SplitTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();
            return value.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToList();
        }

        private void UpdateTimestampText()
        {
            string created = _record.CreatedUtc == default(DateTime)
                ? "-"
                : _record.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            string updated = _record.UpdatedUtc == default(DateTime)
                ? "-"
                : _record.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            _timestampLabel.Text = "생성 " + created + "   ·   수정 " + updated;
        }

        private void MarkDirty()
        {
            if (_loading) return;
            _dirty = true;
            ShowStatus("저장하지 않은 변경 사항이 있습니다.", Accent);
        }

        private void ShowStatus(string message, Color color)
        {
            if (_statusLabel == null) return;
            _statusLabel.Text = message;
            _statusLabel.ForeColor = color;
        }

        private void TryOpen(string fileName, string arguments)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(arguments))
                    Process.Start(fileName);
                else
                    Process.Start(fileName, arguments);
            }
            catch (Exception ex)
            {
                ShowStatus("열지 못했습니다: " + ex.Message, Danger);
            }
        }
    }
}
