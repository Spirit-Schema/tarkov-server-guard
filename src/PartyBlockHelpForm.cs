// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal sealed class PartyBlockHelpForm : BrandedForm
    {
        internal PartyBlockHelpForm()
        {
            Text = AppText.Get("PartyHelp.Title");
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(700, 690);
            MinimumSize = new Size(570, 460);
            BackColor = Color.FromArgb(15, 18, 22);
            ForeColor = Color.FromArgb(244, 246, 248);
            Font = new Font("Malgun Gothic", 10F);
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(20), BackColor = BackColor
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.Controls.Add(new Label
            {
                Text = Text, AutoSize = true, Anchor = AnchorStyles.Left,
                Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(232, 157, 54)
            }, 0, 0);
            var body = new RichTextBox
            {
                Name = "PartyHelpBody", Dock = DockStyle.Fill, ReadOnly = true,
                BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = ForeColor,
                Font = Font, Text = AppText.Get("PartyHelp.Body"), DetectUrls = false,
                WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical,
                AccessibleName = Text, Margin = new Padding(0, 8, 0, 8)
            };
            root.Controls.Add(body, 0, 1);
            var close = new Button
            {
                Text = AppText.Get("Common.Button.Close"),
                AutoSize = true, MinimumSize = new Size(90, 32), Anchor = AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 38, 46),
                ForeColor = ForeColor, Cursor = Cursors.Hand, DialogResult = DialogResult.Cancel
            };
            root.Controls.Add(close, 0, 2);
            Controls.Add(root);
            CancelButton = close;
        }
    }
}
