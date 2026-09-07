// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

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
            return ShowArchive(owner, false);
        }

        public static bool ShowArchive(IWin32Window owner, bool sourceReadOnly)
        {
            using (var form = new RaidNoteArchiveForm(new RaidNoteStore(), sourceReadOnly))
            {
                ColumnWidthPersistence.Attach(form, "notes", AppPreferencesStore.GetDefaultStorageRoot());
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
        private readonly bool _readOnly;
        private RaidNoteRecord _record;
        private TextBox _noteTextBox;
        private Label _notePlaceholderLabel;
        private Label _nicknamePlaceholderLabel;
        private ListBox _screenshotList;
        private TextBox _tagTextBox;
        private Label _timestampLabel;
        private Label _statusLabel;
        private Button _attachButton;
        private Button _detachButton;
        private Button _saveButton;
        private Button _deleteButton;
        private bool _loading;
        private bool _dirty;

        public RaidNoteForm(ServerSession session, RaidNoteStore store)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (store == null) throw new ArgumentNullException("store");
            _session = session;
            _store = store;
            _readOnly = false;
            _record = _store.Load(session) ?? _store.CreateFor(session);
            InitializeWindow();
            BuildInterface();
            LoadRecord();
            FormClosing += RaidNoteFormClosing;
        }

        public RaidNoteForm(RaidNoteRecord record, RaidNoteStore store)
            : this(record, store, false)
        {
        }

        public RaidNoteForm(RaidNoteRecord record, RaidNoteStore store, bool readOnly)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (store == null) throw new ArgumentNullException("store");
            if (string.IsNullOrWhiteSpace(record.Key))
                throw new ArgumentException(AppText.Get("RaidNote.MissingRecordKey"), "record");
            _session = null;
            _store = store;
            _readOnly = readOnly;
            _record = record;
            InitializeWindow();
            BuildInterface();
            LoadRecord();
            ApplyReadOnlyMode();
            FormClosing += RaidNoteFormClosing;
        }

        public bool Changed { get; private set; }

        private void InitializeWindow()
        {
            Text = AppText.Get("Memo.Legacy.RaidNoteTitle");
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
                Text = AppText.Get("Memo.Legacy.RaidNoteTitle"),
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
            TableLayoutPanel section = CreateSection(AppText.Get("RaidNote.Section.Free"), 24);
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
                Text = AppText.Get("RaidNote.Placeholder")
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
                Text = AppText.Get("RaidNote.NicknamePlaceholder")
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
                if (_nicknamePlaceholderLabel != null)
                    _nicknamePlaceholderLabel.Top = Math.Max(_notePlaceholderLabel.Bottom + 8,
                        Math.Min(5 + noteLineHeight * 9,
                            _noteTextBox.ClientSize.Height - _nicknamePlaceholderLabel.Height - 8));
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
                AppText.Get("RaidNote.Attachments.Section"),
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
            _attachButton = CreateSmallButton(AppText.Get("RaidNote.Attach"));
            _attachButton.Name = "RaidNoteAttachButton";
            _attachButton.Click += delegate { AttachScreenshots(); };
            Button open = CreateSmallButton(AppText.Get("Common.Open"));
            open.Click += delegate { OpenSelectedScreenshot(); };
            Button openFolder = CreateSmallButton(AppText.Get("RaidNote.OpenFolder"));
            openFolder.Click += delegate { OpenSelectedScreenshotFolder(); };
            _detachButton = CreateSmallButton(AppText.Get("RaidNote.Detach"));
            _detachButton.Name = "RaidNoteDetachButton";
            _detachButton.Click += delegate { DetachSelectedScreenshot(); };
            buttons.Controls.Add(_attachButton);
            buttons.Controls.Add(open);
            buttons.Controls.Add(openFolder);
            buttons.Controls.Add(_detachButton);
            layout.Controls.Add(buttons, 0, 1);
            return section;
        }

        private Control BuildTagSection()
        {
            TableLayoutPanel section = CreateSection(AppText.Get("RaidNote.Tags"), 24);
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
                Text = AppText.Get("RaidNote.LocalOnly")
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
            _saveButton = CreateSmallButton(AppText.Get("Common.Save"));
            _saveButton.Name = "RaidNoteSaveButton";
            _saveButton.BackColor = Accent;
            _saveButton.ForeColor = Color.FromArgb(29, 24, 17);
            _saveButton.FlatAppearance.BorderColor = Accent;
            _saveButton.Click += delegate { SaveNote(true); };
            _deleteButton = CreateSmallButton(AppText.Get("Common.Delete"));
            _deleteButton.Name = "RaidNoteDeleteButton";
            _deleteButton.BackColor = Danger;
            _deleteButton.FlatAppearance.BorderColor = Danger;
            _deleteButton.Click += delegate { DeleteNote(); };
            Button folder = CreateSmallButton(AppText.Get("RaidNote.OpenStorage"));
            folder.Click += delegate { OpenNoteFolder(); };
            buttons.Controls.Add(_saveButton);
            buttons.Controls.Add(_deleteButton);
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

        private void ApplyReadOnlyMode()
        {
            if (!_readOnly) return;
            _noteTextBox.ReadOnly = true;
            _tagTextBox.ReadOnly = true;
            _screenshotList.AllowDrop = false;
            _attachButton.Enabled = false;
            _detachButton.Enabled = false;
            _saveButton.Enabled = false;
            _deleteButton.Enabled = false;
            string notice = AppText.Get("Memo.Legacy.ReadOnlySourceNotice");
            _statusLabel.Text = notice;
            _statusLabel.ForeColor = Accent;
            _statusLabel.AccessibleName = notice;
            foreach (Button button in new[]
            {
                _attachButton, _detachButton, _saveButton, _deleteButton
            })
            {
                button.AccessibleDescription = notice;
            }
        }

        private string BuildRaidSummary()
        {
            if (_session != null)
            {
                string game = _session.GameDisplayName;
                string map = string.IsNullOrWhiteSpace(_session.MapName) ? "-" : _session.MapName;
                string type = _session.Game == TarkovGame.Eft
                    ? AppText.LocalizeDomainDisplay(_session.RaidTypeText)
                    : _session.GameMode;
                string mapAndType = string.IsNullOrWhiteSpace(type) ? map : map + " · " + type;
                return string.Format("{0} · {1:yyyy-MM-dd HH:mm:ss} · {2}", game, _session.DisplayDetectedAt, mapAndType);
            }
            DateTime started = _record.RaidStartedUtc == default(DateTime)
                ? default(DateTime)
                : _record.RaidStartedUtc.ToLocalTime();
            string storedDate = started == default(DateTime)
                ? AppText.Get("RaidNote.TimeUnknown")
                : started.ToString("yyyy-MM-dd HH:mm:ss");
            return string.Format("{0} · {1} · {2}",
                string.IsNullOrWhiteSpace(_record.Game) ? "-" : _record.Game,
                storedDate,
                string.IsNullOrWhiteSpace(_record.MapName) ? "-" : _record.MapName);
        }

        private void AttachScreenshots()
        {
            if (_readOnly) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = AppText.Get("RaidNote.AttachDialog.Title");
                dialog.Filter = AppText.Get("RaidNote.AttachDialog.Filter");
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                AddScreenshotPaths(dialog.FileNames);
            }
        }

        private void AddScreenshotPaths(IEnumerable<string> candidates)
        {
            if (_readOnly) return;
            var existing = new HashSet<string>(
                _screenshotList.Items.Cast<object>().Select(Convert.ToString),
                StringComparer.OrdinalIgnoreCase);
            int added = 0;
            int rejected = 0;
            foreach (string selected in candidates ?? Enumerable.Empty<string>())
            {
                string fullPath;
                try { fullPath = Path.GetFullPath(selected); }
                catch
                {
                    rejected++;
                    continue;
                }
                if (!File.Exists(fullPath)
                    || !RaidNoteStore.IsSafeScreenshotAttachmentPath(fullPath)
                    || existing.Count >= RaidNoteStore.MaximumScreenshotPathCount)
                {
                    rejected++;
                    continue;
                }
                if (!existing.Add(fullPath)) continue;
                _screenshotList.Items.Add(fullPath);
                added++;
            }
            if (added > 0)
            {
                MarkDirty();
                ShowStatus(AppText.Format("RaidNote.AttachedCount", added), Accent);
            }
            if (rejected > 0)
                ShowStatus(AppText.Format("RaidNote.AttachmentRejected", rejected), Danger);
        }

        private void DetachSelectedScreenshot()
        {
            if (_readOnly) return;
            int index = _screenshotList.SelectedIndex;
            if (index < 0) return;
            _screenshotList.Items.RemoveAt(index);
            MarkDirty();
        }

        private void OpenSelectedScreenshot()
        {
            string path = GetSelectedScreenshotPath();
            if (path == null) return;
            if (!RaidNoteStore.IsSafeAttachmentPath(path))
            {
                ShowStatus(AppText.Get("RaidNote.AttachmentUnsafe"), Danger);
                return;
            }
            if (!File.Exists(path))
            {
                ShowStatus(AppText.Get("RaidNote.AttachmentMissing"), Danger);
                return;
            }
            TryOpen(path, null);
        }

        private void OpenSelectedScreenshotFolder()
        {
            string path = GetSelectedScreenshotPath();
            if (path == null) return;
            if (!RaidNoteStore.IsSafeAttachmentPath(path))
            {
                ShowStatus(AppText.Get("RaidNote.AttachmentFolderUnsafe"), Danger);
                return;
            }
            if (File.Exists(path))
                TryOpen("explorer.exe", "/select,\"" + path.Replace("\"", string.Empty) + "\"");
            else
            {
                string directory = null;
                try { directory = Path.GetDirectoryName(path); }
                catch { }
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    ShowStatus(AppText.Get("RaidNote.AttachmentFolderMissing"), Danger);
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
            if (_readOnly) return;
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
                ShowStatus(AppText.Get("RaidNote.Saved"), Accent);
                if (closeAfterSave) Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, AppText.Format("RaidNote.SaveFailed", AppText.TranslateDiagnostic(ex.Message, null)),
                    AppText.Get("Memo.Legacy.RaidNoteTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DeleteNote()
        {
            if (_readOnly) return;
            bool exists = _session == null
                ? _store.Exists(_record.Key)
                : _store.Exists(_session);
            if (!exists && !_dirty)
            {
                ShowStatus(AppText.Get("RaidNote.NoSavedNote"), TextMuted);
                return;
            }
            DialogResult answer = MessageBox.Show(
                this,
                AppText.Get("RaidNote.DeletePrompt"),
                AppText.Get("RaidNote.DeleteTitle"),
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
                MessageBox.Show(this, AppText.Format("RaidNote.DeleteFailed", AppText.TranslateDiagnostic(ex.Message, null)),
                    AppText.Get("Memo.Legacy.RaidNoteTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenNoteFolder()
        {
            try { _store.OpenNotesFolder(); }
            catch (Exception ex) { ShowStatus(AppText.Format("RaidNote.OpenFolderFailed", AppText.TranslateDiagnostic(ex.Message, null)), Danger); }
        }

        private void RaidNoteFormClosing(object sender, FormClosingEventArgs args)
        {
            if (_readOnly) return;
            if (!_dirty) return;
            DialogResult answer = MessageBox.Show(
                this,
                AppText.Get("RaidNote.UnsavedPrompt"),
                AppText.Get("Memo.Legacy.RaidNoteTitle"),
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
            _timestampLabel.Text = AppText.Format("RaidNote.Timestamp", created, updated);
        }

        private void MarkDirty()
        {
            if (_loading || _readOnly) return;
            _dirty = true;
            ShowStatus(AppText.Get("RaidNote.Unsaved"), Accent);
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
                ShowStatus(AppText.Format("RaidNote.OpenFailed", AppText.TranslateDiagnostic(ex.Message, null)), Danger);
            }
        }
    }
}
