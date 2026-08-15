// SPDX-License-Identifier: MPL-2.0
// Copyright 2026 Spirit-Schema

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class ArenaBlockWarningForm : BrandedForm
    {
        internal const string DialogTitle = "아레나 서버 차단";
        internal const string WarningText =
            "아레나 서버 차단 시 탈주 페널티 적용 여부는 확인되지 않았습니다. 그래도 차단하시겠습니까?";

        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color Success = Color.FromArgb(68, 193, 130);
        private static readonly Color Danger = Color.FromArgb(173, 62, 68);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);

        private readonly Button _cancelButton;

        public ArenaBlockWarningForm()
        {
            Text = DialogTitle;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 220);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(22, 18, 22, 18);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 3,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            Controls.Add(layout);

            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = DialogTitle,
                Font = new Font("Malgun Gothic", 13F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Accent,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);

            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = WarningText,
                Font = new Font("Malgun Gothic", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = TextPrimary,
                BackColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 8, 14, 8),
                Margin = new Padding(0, 4, 0, 10)
            }, 0, 1);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0, 10, 0, 0)
            };
            layout.Controls.Add(buttons, 0, 2);

            var blockButton = CreateButton("차단", Danger, Color.White);
            blockButton.TabIndex = 1;
            blockButton.Click += delegate
            {
                DialogResult = DialogResult.OK;
                Close();
            };

            _cancelButton = CreateButton("취소", Success, Color.FromArgb(16, 38, 29));
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.TabIndex = 0;

            buttons.Controls.Add(blockButton);
            buttons.Controls.Add(_cancelButton);
            buttons.Resize += delegate
            {
                int width = blockButton.Width + _cancelButton.Width + blockButton.Margin.Horizontal + _cancelButton.Margin.Horizontal;
                buttons.Padding = new Padding(Math.Max(0, (buttons.ClientSize.Width - width) / 2), 10, 0, 0);
            };

            AcceptButton = null;
            CancelButton = _cancelButton;
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (DialogResult != DialogResult.OK) DialogResult = DialogResult.Cancel;
            };
        }

        private static Button CreateButton(string text, Color backColor, Color foreColor)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(104, 36),
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Margin = new Padding(5, 0, 5, 0),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }
    }
}
