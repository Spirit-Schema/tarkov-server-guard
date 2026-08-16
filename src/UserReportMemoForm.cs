// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public static class UserReportMemoUi
    {
        public static bool ShowFor(IWin32Window owner, ServerSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            using (var form = new UserReportMemoForm(session, new UserReportMemoStore()))
            {
                form.ShowDialog(owner);
                return form.Changed;
            }
        }

        public static bool HasMemo(ServerSession session)
        {
            return session != null && new UserReportMemoStore().Exists(session);
        }

        // Retained for callers that need a plain-text representation. The editor itself
        // uses fixed labels and separate input controls, so these labels cannot be erased.
        public static string BuildDefaultTemplate(int reportCount)
        {
            int count = Math.Max(1, Math.Min(reportCount, UserReportMemoStore.MaximumReportCount));
            var builder = new StringBuilder();
            for (int index = 1; index <= count; index++)
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(index);
                builder.Append(". 유저네임:                         신고사유:");
            }
            return builder.ToString();
        }
    }

    public sealed class UserReportMemoForm : BrandedForm
    {
        private const int MaximumVisibleEntryCount = 50;

        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color Danger = Color.FromArgb(192, 68, 75);

        private readonly ServerSession _session;
        private readonly UserReportMemoStore _store;
        private readonly List<EntryEditor> _entryEditors = new List<EntryEditor>();
        private UserReportMemoRecord _record;
        private readonly bool _existedAtOpen;
        private TextBox _legacyMemoTextBox;
        private Panel _legacySection;
        private TableLayoutPanel _editorLayout;
        private Label _timestampLabel;
        private Label _statusLabel;
        private bool _loading;
        private bool _dirty;

        public UserReportMemoForm(ServerSession session, UserReportMemoStore store)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (store == null) throw new ArgumentNullException("store");
            _session = session;
            _store = store;
            _record = _store.Load(session);
            _existedAtOpen = _record != null;
            if (_record == null) _record = _store.CreateFor(session);

            InitializeWindow();
            BuildInterface();
            LoadRecord();
            FormClosing += UserReportMemoFormClosing;
        }

        public UserReportMemoForm(UserReportMemoRecord record, UserReportMemoStore store)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (store == null) throw new ArgumentNullException("store");
            if (string.IsNullOrWhiteSpace(record.Key))
                throw new ArgumentException("저장된 유저신고 메모 키가 없습니다.", "record");
            _session = null;
            _store = store;
            _record = record;
            _existedAtOpen = true;

            InitializeWindow();
            BuildInterface();
            LoadRecord();
            FormClosing += UserReportMemoFormClosing;
        }

        public bool Changed { get; private set; }

        private void InitializeWindow()
        {
            Text = "유저신고 메모";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(680, 450);
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
                RowCount = 5
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(1, 0),
                Text = "유저신고 메모",
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = TextPrimary
            });
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(3, 36),
                Text = BuildHeaderDescription(),
                ForeColor = TextMuted
            });
            root.Controls.Add(header, 0, 0);

            _editorLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0, 3, 0, 5)
            };
            _editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            bool hasLegacyMemo = !string.IsNullOrWhiteSpace(_record.MemoText);
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, hasLegacyMemo ? 142F : 0F));
            _editorLayout.Controls.Add(BuildStructuredEditor(), 0, 0);
            _legacySection = BuildLegacyEditor();
            _legacySection.Visible = hasLegacyMemo;
            _editorLayout.Controls.Add(_legacySection, 0, 1);
            root.Controls.Add(_editorLayout, 0, 1);

            _timestampLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                AutoEllipsis = true
            };
            root.Controls.Add(_timestampLabel, 0, 2);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 7.5F),
                AutoEllipsis = false,
                Margin = new Padding(0, 3, 0, 3),
                Text = "유저네임·신고사유·게임·맵·레이드 시각·신고 건수만 로컬 저장합니다.\r\n"
                    + "게임 로그 원문·계정 ID·SID·경로는 저장·전송하지 않습니다."
            };
            root.Controls.Add(_statusLabel, 0, 3);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0, 7, 0, 0)
            };
            Button close = CreateButton("닫기", false, false);
            Button save = CreateButton("저장", true, false);
            Button delete = CreateButton("삭제", false, true);
            Button folder = CreateButton("보관 폴더 열기", false, false);
            close.Click += delegate { Close(); };
            save.Click += delegate
            {
                if (SaveMemo()) Close();
            };
            delete.Click += delegate { DeleteMemo(); };
            folder.Click += delegate { OpenMemoFolder(); };
            buttons.Controls.Add(close);
            buttons.Controls.Add(save);
            buttons.Controls.Add(delete);
            buttons.Controls.Add(folder);
            root.Controls.Add(buttons, 0, 4);
        }

        private string BuildHeaderDescription()
        {
            int reportCount = GetReportCount();
            int visibleCount = GetVisibleEntryCount();
            if (reportCount > visibleCount)
            {
                return string.Format(
                    "성공 확인된 신고 {0}건 · 화면 과부하를 막기 위해 앞의 {1}건을 표시합니다.",
                    reportCount,
                    visibleCount);
            }
            return string.Format(
                "성공 확인된 신고 {0}건 · 일반 레이드 메모와 함께 메모보관함에 저장됩니다.",
                reportCount);
        }

        private Control BuildStructuredEditor()
        {
            var surface = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            surface.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            surface.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            surface.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            surface.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "신고 정보",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold)
            }, 0, 0);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Surface,
                Padding = new Padding(8, 8, 8, 4),
                Margin = new Padding(0)
            };
            var entries = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Surface,
                ColumnCount = 1,
                RowCount = 0,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            entries.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            int visibleCount = GetVisibleEntryCount();
            for (int index = 0; index < visibleCount; index++)
                entries.Controls.Add(BuildEntryRow(index), 0, index);
            scroll.Controls.Add(entries);
            surface.Controls.Add(scroll, 0, 1);
            return surface;
        }

        private Control BuildEntryRow(int index)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Surface,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 5),
                Padding = new Padding(0)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));

            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = (index + 1) + ". 유저네임:",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                Margin = new Padding(0),
                Padding = new Padding(10, 0, 0, 0)
            }, 0, 0);
            TextBox nickname = CreateEntryTextBox(
                "ReportNickname" + (index + 1), UserReportMemoStore.MaximumNicknameLength);
            row.Controls.Add(nickname, 1, 0);
            row.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "신고사유:",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                Margin = new Padding(0),
                Padding = new Padding(12, 0, 0, 0)
            }, 2, 0);
            TextBox reason = CreateEntryTextBox(
                "ReportReason" + (index + 1), UserReportMemoStore.MaximumReasonLength);
            row.Controls.Add(reason, 3, 0);
            _entryEditors.Add(new EntryEditor(nickname, reason));
            return row;
        }

        private TextBox CreateEntryTextBox(string name, int maximumLength)
        {
            var textBox = new TextBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F),
                MaxLength = maximumLength,
                Margin = new Padding(0, 5, 0, 4)
            };
            textBox.TextChanged += delegate
            {
                if (!_loading) _dirty = true;
            };
            return textBox;
        }

        private Panel BuildLegacyEditor()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = new Padding(0, 8, 0, 0)
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "기존 메모 (v0.6.6) · 원문을 그대로 보존합니다.",
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0),
                ForeColor = TextMuted
            }, 0, 0);
            _legacyMemoTextBox = new TextBox
            {
                Name = "LegacyMemoText",
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F),
                MaxLength = UserReportMemoStore.MaximumMemoLength,
                Margin = new Padding(8, 5, 8, 7)
            };
            _legacyMemoTextBox.TextChanged += delegate
            {
                if (!_loading) _dirty = true;
            };
            layout.Controls.Add(_legacyMemoTextBox, 0, 1);
            panel.Controls.Add(layout);
            return panel;
        }

        private static Button CreateButton(string text, bool emphasized, bool destructive)
        {
            Color background = emphasized ? Accent : destructive ? Danger : SurfaceAlt;
            var button = new Button
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(text.Length > 6 ? 112 : 72, 32),
                Padding = new Padding(12, 0, 12, 0),
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = background,
                ForeColor = emphasized ? Color.FromArgb(29, 24, 17) : TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                Margin = new Padding(7, 0, 0, 0)
            };
            button.FlatAppearance.BorderColor = background == SurfaceAlt ? Border : background;
            return button;
        }

        private int GetReportCount()
        {
            int count = _session == null ? _record.ReportCount : _session.UserReportCount;
            if (count < 1) return 1;
            return Math.Min(count, UserReportMemoStore.MaximumReportCount);
        }

        private int GetVisibleEntryCount()
        {
            int storedCount = _record.Entries == null ? 0 : _record.Entries.Count;
            return Math.Min(
                Math.Max(GetReportCount(), storedCount),
                MaximumVisibleEntryCount);
        }

        private void LoadRecord()
        {
            _loading = true;
            try
            {
                IList<UserReportMemoEntry> entries = _record.Entries;
                for (int index = 0; index < _entryEditors.Count; index++)
                {
                    UserReportMemoEntry entry = entries != null && index < entries.Count
                        ? entries[index]
                        : null;
                    _entryEditors[index].Nickname.Text = entry == null ? string.Empty : entry.Nickname;
                    _entryEditors[index].Reason.Text = entry == null ? string.Empty : entry.Reason;
                }
                _legacyMemoTextBox.Text = _record.MemoText ?? string.Empty;
                if (_entryEditors.Count > 0)
                {
                    _entryEditors[0].Nickname.SelectionStart = 0;
                    _entryEditors[0].Nickname.SelectionLength = 0;
                }
                UpdateTimestampText(_existedAtOpen);
                _dirty = false;
            }
            finally
            {
                _loading = false;
            }
        }

        private bool SaveMemo()
        {
            try
            {
                var entries = new List<UserReportMemoEntry>();
                foreach (EntryEditor editor in _entryEditors)
                {
                    entries.Add(new UserReportMemoEntry
                    {
                        Nickname = editor.Nickname.Text ?? string.Empty,
                        Reason = editor.Reason.Text ?? string.Empty
                    });
                }
                if (_record.Entries != null)
                {
                    for (int index = _entryEditors.Count; index < _record.Entries.Count; index++)
                    {
                        UserReportMemoEntry preserved = _record.Entries[index];
                        entries.Add(new UserReportMemoEntry
                        {
                            Nickname = preserved == null ? string.Empty : preserved.Nickname,
                            Reason = preserved == null ? string.Empty : preserved.Reason
                        });
                    }
                }
                _record.Entries = entries;
                _record.MemoText = _legacyMemoTextBox.Text ?? string.Empty;
                if (_session == null)
                    _store.Save(_record.Key, _record);
                else
                    _store.Save(_session, _record);
                _dirty = false;
                Changed = true;
                UpdateTimestampText(true);
                ShowStatus("유저신고 메모를 저장했습니다.", TextMuted);
                return true;
            }
            catch (Exception exception)
            {
                ShowStatus("저장하지 못했습니다: " + exception.Message, Danger);
                return false;
            }
        }

        private void DeleteMemo()
        {
            bool exists = _session == null
                ? _store.Exists(_record.Key)
                : _store.Exists(_session);
            if (!exists)
            {
                if (_session == null)
                {
                    ShowStatus("저장된 유저신고 메모가 없습니다.", TextMuted);
                    return;
                }
                ClearEditors();
                ShowStatus("저장된 유저신고 메모가 없습니다.", TextMuted);
                return;
            }

            DialogResult result = MessageBox.Show(
                this,
                "이 유저신고 메모를 삭제할까요? 일반 레이드 메모에는 영향을 주지 않습니다.",
                "유저신고 메모 삭제",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes) return;

            try
            {
                if (_session == null)
                    _store.Delete(_record.Key);
                else
                    _store.Delete(_session);
                if (_session == null)
                {
                    _dirty = false;
                    Changed = true;
                    Close();
                    return;
                }
                _record = _store.CreateFor(_session);
                ClearEditors();
                Changed = true;
                UpdateTimestampText(false);
                ShowStatus("유저신고 메모를 삭제했습니다.", TextMuted);
            }
            catch (Exception exception)
            {
                ShowStatus("삭제하지 못했습니다: " + exception.Message, Danger);
            }
        }

        private void ClearEditors()
        {
            _loading = true;
            try
            {
                foreach (EntryEditor editor in _entryEditors)
                {
                    editor.Nickname.Clear();
                    editor.Reason.Clear();
                }
                _legacyMemoTextBox.Clear();
                _legacySection.Visible = false;
                _editorLayout.RowStyles[1].Height = 0F;
                _dirty = false;
            }
            finally
            {
                _loading = false;
            }
        }

        private void OpenMemoFolder()
        {
            try
            {
                _store.OpenMemoFolder();
            }
            catch (Exception exception)
            {
                ShowStatus("보관 폴더를 열지 못했습니다: " + exception.Message, Danger);
            }
        }

        private void UpdateTimestampText(bool saved)
        {
            if (!saved)
            {
                _timestampLabel.Text = "생성: 저장 전 · 수정: 저장 전";
                return;
            }
            _timestampLabel.Text = string.Format(
                "생성: {0:yyyy-MM-dd HH:mm:ss} · 수정: {1:yyyy-MM-dd HH:mm:ss}",
                _record.CreatedUtc.ToLocalTime(),
                _record.UpdatedUtc.ToLocalTime());
        }

        private void ShowStatus(string text, Color color)
        {
            _statusLabel.Text = text;
            _statusLabel.ForeColor = color;
        }

        private void UserReportMemoFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_dirty) return;
            DialogResult result = MessageBox.Show(
                this,
                "변경한 유저신고 메모를 저장할까요?",
                "저장 확인",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == DialogResult.Yes)
            {
                if (!SaveMemo()) e.Cancel = true;
            }
        }

        private sealed class EntryEditor
        {
            public EntryEditor(TextBox nickname, TextBox reason)
            {
                Nickname = nickname;
                Reason = reason;
            }

            public TextBox Nickname { get; private set; }
            public TextBox Reason { get; private set; }
        }
    }
}
