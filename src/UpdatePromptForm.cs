// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal sealed class UpdatePromptForm : BrandedForm
    {
        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color AccentHover = Color.FromArgb(246, 173, 70);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextSecondary = Color.FromArgb(171, 180, 190);

        private readonly Label _messageLabel;
        private readonly Label _statusLabel;
        private readonly ProgressBar _progressBar;
        private readonly Button _updateButton;
        private readonly Button _laterButton;

        internal event EventHandler UpdateRequested;

        internal UpdatePromptForm(string versionText)
        {
            Text = "Tarkov Server Guard 업데이트";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 260);
            MinimumSize = new Size(500, 260);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(22, 18, 22, 18);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            Controls.Add(layout);

            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "새 버전이 있습니다",
                Font = new Font("Malgun Gothic", 13F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Accent,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);

            _messageLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "Tarkov Server Guard v" + versionText
                    + " 업데이트가 있습니다.\r\n지금 업데이트하시겠습니까?",
                Font = new Font("Malgun Gothic", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = TextPrimary,
                BackColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 10, 14, 10),
                Margin = new Padding(0, 4, 0, 8)
            };
            layout.Controls.Add(_messageLabel, 0, 1);

            var progressHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 8,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous,
                Visible = false,
                Margin = new Padding(0)
            };
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = string.Empty,
                ForeColor = TextSecondary,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
            progressHost.Controls.Add(_statusLabel);
            progressHost.Controls.Add(_progressBar);
            layout.Controls.Add(progressHost, 0, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0, 10, 0, 0)
            };
            layout.Controls.Add(buttons, 0, 3);

            _updateButton = CreateButton("업데이트", Accent, Color.FromArgb(29, 24, 17));
            _updateButton.TabIndex = 0;
            _updateButton.Click += delegate
            {
                EventHandler handler = UpdateRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            _updateButton.FlatAppearance.MouseOverBackColor = AccentHover;

            _laterButton = CreateButton("나중에", Surface, TextPrimary);
            _laterButton.TabIndex = 1;
            _laterButton.DialogResult = DialogResult.Cancel;

            buttons.Controls.Add(_updateButton);
            buttons.Controls.Add(_laterButton);
            EventHandler centerButtons = delegate
            {
                int width = _updateButton.Width + _laterButton.Width
                    + _updateButton.Margin.Horizontal + _laterButton.Margin.Horizontal;
                buttons.Padding = new Padding(
                    Math.Max(0, (buttons.ClientSize.Width - width) / 2), 10, 0, 0);
            };
            buttons.Resize += centerButtons;

            AcceptButton = _updateButton;
            CancelButton = _laterButton;
            Shown += delegate
            {
                centerButtons(null, EventArgs.Empty);
                _updateButton.Focus();
            };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (!_laterButton.Enabled && DialogResult != DialogResult.Cancel)
                    args.Cancel = true;
                else if (DialogResult == DialogResult.None)
                    DialogResult = DialogResult.Cancel;
            };
        }

        internal void BeginDownload()
        {
            RunOnUiThread(delegate
            {
                _messageLabel.Text = "업데이트를 다운로드하고 있습니다.\r\n완료되면 자동으로 다시 시작합니다.";
                _statusLabel.Text = "다운로드 준비 중...";
                _progressBar.Value = 0;
                _progressBar.Visible = true;
                _updateButton.Enabled = false;
                _laterButton.Enabled = false;
                ControlBox = false;
            });
        }

        internal void ReportProgress(int percentage)
        {
            RunOnUiThread(delegate
            {
                int value = Math.Max(0, Math.Min(100, percentage));
                _progressBar.Value = value;
                _statusLabel.Text = "다운로드 " + value + "%";
            });
        }

        internal void ShowDownloadError()
        {
            ShowError("업데이트하지 못했습니다. 현재 버전은 그대로 유지됩니다.\r\n잠시 후 다시 시도해 주세요.");
        }

        internal void ShowApplyDidNotRestartError()
        {
            ShowError("업데이트를 적용하지 못했습니다. 현재 버전은 그대로 유지됩니다.\r\n잠시 후 다시 시도해 주세요.");
        }

        internal void CloseAfterCancellation()
        {
            RunOnUiThread(delegate
            {
                _laterButton.Enabled = true;
                DialogResult = DialogResult.Cancel;
                Close();
            });
        }

        private void ShowError(string message)
        {
            RunOnUiThread(delegate
            {
                _messageLabel.Text = message;
                _statusLabel.Text = string.Empty;
                _progressBar.Visible = false;
                _updateButton.Text = "다시 시도";
                _updateButton.Enabled = true;
                _laterButton.Text = "닫기";
                _laterButton.Enabled = true;
                ControlBox = true;
                _updateButton.Focus();
            });
        }

        private void RunOnUiThread(Action action)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(action); }
                catch (InvalidOperationException) { }
                return;
            }
            action();
        }

        private static Button CreateButton(string text, Color backColor, Color foreColor)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(112, 36),
                BackColor = backColor,
                ForeColor = foreColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold, GraphicsUnit.Point),
                Margin = new Padding(5, 0, 5, 0),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }
    }
}
