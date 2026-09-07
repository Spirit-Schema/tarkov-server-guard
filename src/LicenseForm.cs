// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class LicenseForm : BrandedForm
    {
        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color AccentHover = Color.FromArgb(246, 173, 70);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color DarkButtonText = Color.FromArgb(29, 24, 17);

        private const string LicenseResourceName = "TarkovServerReporter.LICENSE.txt";
        private const string ThirdPartyResourceName = "TarkovServerReporter.THIRD_PARTY_NOTICES.md";

        private readonly RichTextBox _documentTextBox;
        private readonly Label _documentTitleLabel;
        private readonly Button _licenseButton;
        private readonly Button _thirdPartyButton;

        public LicenseForm()
        {
            Text = AppText.Get("License.Title");
            AccessibleName = AppText.Get("License.Title");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(790, 680);
            MinimumSize = new Size(640, 540);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(24, 20, 24, 18),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,
                AppText.CurrentLanguage == AppText.EnglishLanguage ? 230F : 210F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("License.Title"),
                UseMnemonic = false,
                Font = new Font("Malgun Gothic", 16F, FontStyle.Bold),
                ForeColor = Accent,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            }, 0, 0);

            var summaryPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                Padding = new Padding(18, 12, 18, 12),
                Margin = new Padding(0, 4, 0, 10)
            };
            summaryPanel.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (var pen = new Pen(Border))
                    args.Graphics.DrawRectangle(pen, 0, 0,
                        Math.Max(0, summaryPanel.ClientSize.Width - 1),
                        Math.Max(0, summaryPanel.ClientSize.Height - 1));
            };
            summaryPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = false,
                Text =
                    AppText.Get("License.Summary"),
                Font = new Font("Malgun Gothic", 9.5F),
                ForeColor = TextPrimary,
                BackColor = Surface,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            });
            root.Controls.Add(summaryPanel, 0, 1);

            _documentTitleLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("License.Document"),
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Background,
                TextAlign = ContentAlignment.BottomLeft,
                Margin = new Padding(0, 0, 0, 6)
            };
            root.Controls.Add(_documentTitleLabel, 0, 2);

            _documentTextBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                DetectUrls = false,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.ForcedVertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Surface,
                ForeColor = TextPrimary,
                Font = new Font("Consolas", 9F),
                Margin = new Padding(0),
                TabStop = true,
                AccessibleName = AppText.Get("License.DocumentA11y"),
                AccessibleDescription = AppText.Get("License.DocumentHelp")
            };
            root.Controls.Add(_documentTextBox, 0, 3);

            var buttonHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = new Padding(0)
            };
            root.Controls.Add(buttonHost, 0, 4);

            _licenseButton = CreateDocumentButton(AppText.Get("License.Document"), 0);
            _licenseButton.AccessibleDescription = AppText.Get("License.ShowHelp");
            _licenseButton.Click += delegate { ShowLicenseDocument(); };
            buttonHost.Controls.Add(_licenseButton);

            _thirdPartyButton = CreateDocumentButton(AppText.Get("License.ThirdParty"), 1);
            _thirdPartyButton.AccessibleDescription = AppText.Get("License.ThirdPartyHelp");
            _thirdPartyButton.Click += delegate { ShowThirdPartyDocument(); };
            buttonHost.Controls.Add(_thirdPartyButton);

            var confirmButton = new Button
            {
                Text = AppText.Get("License.CloseButton"),
                DialogResult = DialogResult.OK,
                Size = new Size(104, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = DarkButtonText,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabIndex = 2,
                AccessibleName = AppText.Get("License.CloseButton"),
                AccessibleDescription = AppText.Get("License.CloseHelp")
            };
            confirmButton.FlatAppearance.BorderColor = Accent;
            confirmButton.FlatAppearance.MouseOverBackColor = AccentHover;
            confirmButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(213, 137, 40);
            buttonHost.Controls.Add(confirmButton);

            EventHandler positionButtons = delegate
            {
                int gap = Math.Max(8, (int)Math.Round(10F * buttonHost.DeviceDpi / 96F));
                int totalWidth = _licenseButton.Width + _thirdPartyButton.Width
                    + confirmButton.Width + (gap * 2);
                int left = Math.Max(0, (buttonHost.ClientSize.Width - totalWidth) / 2);
                int top = Math.Max(0, (buttonHost.ClientSize.Height - confirmButton.Height) / 2 + 4);
                _licenseButton.Location = new Point(left, top);
                _thirdPartyButton.Location = new Point(_licenseButton.Right + gap, top);
                confirmButton.Location = new Point(_thirdPartyButton.Right + gap, top);
            };
            buttonHost.Resize += positionButtons;
            buttonHost.DpiChangedAfterParent += delegate { positionButtons(null, EventArgs.Empty); };
            positionButtons(null, EventArgs.Empty);

            AcceptButton = confirmButton;
            CancelButton = confirmButton;
            ShowLicenseDocument();
            Shown += delegate { _licenseButton.Focus(); };
        }

        private static Button CreateDocumentButton(string text, int tabIndex)
        {
            var button = new Button
            {
                Text = text,
                Size = new Size(AppText.CurrentLanguage == AppText.EnglishLanguage ? 148 : 132, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
                TabIndex = tabIndex,
                AccessibleName = text
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(39, 47, 56);
            button.FlatAppearance.MouseDownBackColor = Surface;
            return button;
        }

        private void ShowLicenseDocument()
        {
            _documentTitleLabel.Text = AppText.Get("License.BilingualTitle");
            _documentTextBox.Text = ReadEmbeddedDocument(
                LicenseResourceName,
                new[] { "LICENSE.txt", "LICENSE" });
            int englishStart = AppText.CurrentLanguage == AppText.EnglishLanguage
                ? _documentTextBox.Text.IndexOf("English Version", StringComparison.Ordinal) : -1;
            _documentTextBox.SelectionStart = Math.Max(0, englishStart);
            _documentTextBox.ScrollToCaret();
            SetSelectedButton(_licenseButton);
        }

        private void ShowThirdPartyDocument()
        {
            _documentTitleLabel.Text = AppText.Get("License.ThirdParty");
            _documentTextBox.Text = ReadEmbeddedDocument(
                ThirdPartyResourceName,
                new[] { "THIRD_PARTY_NOTICES.md" });
            _documentTextBox.SelectionStart = 0;
            _documentTextBox.ScrollToCaret();
            SetSelectedButton(_thirdPartyButton);
        }

        private void SetSelectedButton(Button selected)
        {
            _licenseButton.ForeColor = ReferenceEquals(selected, _licenseButton) ? Accent : TextPrimary;
            _licenseButton.FlatAppearance.BorderColor = ReferenceEquals(selected, _licenseButton)
                ? Accent : Border;
            _thirdPartyButton.ForeColor = ReferenceEquals(selected, _thirdPartyButton) ? Accent : TextPrimary;
            _thirdPartyButton.FlatAppearance.BorderColor = ReferenceEquals(selected, _thirdPartyButton)
                ? Accent : Border;
        }

        private static string ReadEmbeddedDocument(string resourceName, string[] fallbackNames)
        {
            try
            {
                Assembly assembly = typeof(LicenseForm).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                            return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                // A local packaged copy is tried below.
            }

            foreach (string fileName in fallbackNames)
            {
                try
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                    if (File.Exists(path)) return File.ReadAllText(path, Encoding.UTF8);
                }
                catch
                {
                    // Continue to the next local filename without network access.
                }
            }

            return AppText.Get("License.LoadFailed");
        }
    }
}
