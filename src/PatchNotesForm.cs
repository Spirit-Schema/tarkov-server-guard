// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal sealed class PatchNotesForm : BrandedForm
    {
        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color AccentHover = Color.FromArgb(246, 173, 70);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color DarkButtonText = Color.FromArgb(29, 24, 17);

        private readonly RichTextBox _notesTextBox;
        private readonly Button _closeButton;

        internal PatchNotesForm(ReleaseNotesEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");

            Text = "Tarkov Server Guard 업데이트 완료";
            AccessibleName = "업데이트 완료 안내";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 500);
            MinimumSize = new Size(680, 500);
            MaximumSize = new Size(680, 500);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(22, 18, 22, 16),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "v" + entry.VersionText + " 업데이트가 완료되었습니다.",
                Font = new Font("Malgun Gothic", 15F, FontStyle.Bold),
                ForeColor = Accent,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
                AccessibleName = "업데이트 완료 버전"
            }, 0, 0);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "이번 업데이트에 포함된 변경 사항입니다. 이 안내는 해당 버전 업데이트 후 한 번만 표시됩니다.",
                ForeColor = TextMuted,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 8),
                AccessibleName = "일회성 업데이트 안내"
            }, 0, 1);

            _notesTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.ForcedVertical,
                BorderStyle = BorderStyle.None,
                BackColor = Surface,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 10F),
                Text = ReleaseNotesCatalog.NormalizeNotesText(entry.NotesText),
                Margin = new Padding(0),
                AccessibleName = "업데이트 변경 사항",
                AccessibleDescription = "앱에 포함된 읽기 전용 업데이트 변경 사항"
            };
            var notesBorder = new Panel
            {
                Name = "notesBorderPanel",
                Dock = DockStyle.Fill,
                BackColor = Border,
                Padding = new Padding(1),
                Margin = new Padding(0)
            };
            notesBorder.Controls.Add(_notesTextBox);
            root.Controls.Add(notesBorder, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Background,
                Padding = new Padding(0, 12, 0, 0),
                Margin = new Padding(0)
            };
            root.Controls.Add(buttons, 0, 3);

            _closeButton = new Button
            {
                Text = "확인",
                Size = new Size(104, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = DarkButtonText,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                DialogResult = DialogResult.OK,
                AccessibleName = "업데이트 완료 안내 확인",
                Margin = new Padding(0)
            };
            _closeButton.FlatAppearance.BorderColor = Border;
            _closeButton.FlatAppearance.MouseOverBackColor = AccentHover;
            buttons.Controls.Add(_closeButton);

            AcceptButton = _closeButton;
            CancelButton = _closeButton;
            Shown += delegate
            {
                _notesTextBox.SelectionStart = 0;
                _notesTextBox.ScrollToCaret();
                _closeButton.Focus();
            };
        }

        internal static void ShowCompleted(IWin32Window owner, ReleaseNotesEntry entry)
        {
            using (var form = new PatchNotesForm(entry))
            {
                if (owner == null) form.ShowDialog();
                else form.ShowDialog(owner);
            }
        }
    }
}
