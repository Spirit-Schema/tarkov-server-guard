using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class UsageNoticeForm : Form
    {
        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color AccentBright = Color.FromArgb(255, 184, 76);
        private static readonly Color AccentDark = Color.FromArgb(169, 105, 31);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color ButtonText = Color.FromArgb(29, 24, 17);

        internal const string MatchLine = "매칭 로딩 중 차단한 서버가 매칭되면";
        internal const string ErrorLine = "화면이 잠시 멈춘 후 접속 오류가 표시될 수 있습니다.";
        internal const string NormalLine = "이는 차단한 서버로의 접속을 막아주는 정상 작동입니다.";
        internal const string Step1Line = "1. 접속 오류가 표시되면 종료 대신 ESCAPE FROM TARKOV 선택";
        internal const string Step2Line = "2. 다음 화면에서 재진입 대신 나가기 확인 선택";
        internal const string Step3Line = "3. 축하합니다! 차단한 서버로의 접속을 막았습니다. 다시 매칭해 주세요.";
        internal const string NoticeText =
            "- " + MatchLine + "\r\n"
            + "  " + ErrorLine + "\r\n\r\n"
            + "- " + NormalLine + "\r\n\r\n\r\n"
            + Step1Line + "\r\n\r\n"
            + Step2Line + "\r\n\r\n"
            + Step3Line;

        public UsageNoticeForm()
        {
            Text = "사용방법";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(570, 620);
            MinimumSize = new Size(540, 580);
            BackColor = Border;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Padding = new Padding(1);

            var outer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            Controls.Add(outer);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 8.1F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 5F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 71.1F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 15.8F));
            outer.Controls.Add(content);
            EventHandler positionContent = delegate
            {
                float scale = Math.Max(1F, outer.DeviceDpi / 96F);
                int marginX = Math.Max(20, (int)Math.Round(20F * scale));
                int marginY = Math.Max(18, (int)Math.Round(18F * scale));
                int availableWidth = Math.Max(1, outer.ClientSize.Width - (marginX * 2));
                int availableHeight = Math.Max(1, outer.ClientSize.Height - (marginY * 2));
                int desiredWidth = Math.Max(498, (int)Math.Round(498F * scale));
                int desiredHeight = Math.Max(520, (int)Math.Round(520F * scale));
                content.Size = new Size(
                    Math.Min(desiredWidth, availableWidth),
                    Math.Min(desiredHeight, availableHeight));
                content.Location = new Point(
                    Math.Max(0, (outer.ClientSize.Width - content.Width) / 2),
                    Math.Max(0, (outer.ClientSize.Height - content.Height) / 2));
            };
            outer.Resize += positionContent;
            positionContent(null, EventArgs.Empty);

            content.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "사용방법",
                Font = new Font("Malgun Gothic", 16F, FontStyle.Bold),
                ForeColor = AccentBright,
                BackColor = Background,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0)
            }, 0, 0);

            var noticeHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            content.Controls.Add(noticeHost, 0, 2);
            TableLayoutPanel notice = BuildNoticeBody();
            notice.Dock = DockStyle.None;
            noticeHost.Controls.Add(notice);
            EventHandler positionNotice = delegate
            {
                float scale = Math.Max(1F, noticeHost.DeviceDpi / 96F);
                int baseWidth = Math.Max(426, (int)Math.Round(426F * scale));
                int measuredWidth = MeasureNoticeRequiredWidth(notice)
                    + Math.Max(8, (int)Math.Round(8F * scale));
                int desiredWidth = Math.Max(baseWidth, measuredWidth);
                notice.Bounds = new Rectangle(
                    Math.Max(0, (noticeHost.ClientSize.Width - Math.Min(
                        desiredWidth,
                        noticeHost.ClientSize.Width)) / 2),
                    0,
                    Math.Min(desiredWidth, noticeHost.ClientSize.Width),
                    noticeHost.ClientSize.Height);
            };
            noticeHost.Resize += positionNotice;
            noticeHost.HandleCreated += delegate { positionNotice(null, EventArgs.Empty); };
            noticeHost.DpiChangedAfterParent += delegate { positionNotice(null, EventArgs.Empty); };
            positionNotice(null, EventArgs.Empty);

            var buttonRow = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                Margin = new Padding(0)
            };
            content.Controls.Add(buttonRow, 0, 3);

            var confirmButton = new Button
            {
                Text = "확인",
                DialogResult = DialogResult.OK,
                Size = new Size(104, 38),
                BackColor = Accent,
                ForeColor = ButtonText,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0)
            };
            confirmButton.FlatAppearance.BorderColor = Accent;
            confirmButton.FlatAppearance.MouseOverBackColor = AccentBright;
            EventHandler positionConfirmButton = delegate
            {
                confirmButton.Location = new Point(
                    Math.Max(0, (buttonRow.ClientSize.Width - confirmButton.Width) / 2),
                    Math.Max(0, (buttonRow.ClientSize.Height - confirmButton.Height) / 2));
            };
            buttonRow.Resize += positionConfirmButton;
            buttonRow.Layout += delegate { positionConfirmButton(null, EventArgs.Empty); };
            confirmButton.SizeChanged += positionConfirmButton;
            buttonRow.Controls.Add(confirmButton);
            positionConfirmButton(null, EventArgs.Empty);

            AcceptButton = confirmButton;
            CancelButton = confirmButton;
            Shown += delegate { confirmButton.Focus(); };
        }

        private static TableLayoutPanel BuildNoticeBody()
        {
            var notice = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 9,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            notice.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 18F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 0.8F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 19.7F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 3F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 18F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 2.5F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 18F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 2.5F));
            notice.RowStyles.Add(new RowStyle(SizeType.Percent, 17.5F));
            notice.Controls.Add(CreateBodyLabel("- " + MatchLine + "\r\n  " + ErrorLine), 0, 0);
            notice.Controls.Add(CreateBodyLabel("- " + NormalLine), 0, 2);
            notice.Controls.Add(CreateStepBlock(
                new[] { "1. 접속 오류가 표시되면 ", "종료", " 대신" },
                new[] { "   ", "ESCAPE FROM TARKOV", " 선택" }), 0, 4);
            notice.Controls.Add(CreateStepBlock(
                new[] { "2. 다음 화면에서 ", "재진입", " 대신" },
                new[] { "   ", "나가기 확인", " 선택" }), 0, 6);
            notice.Controls.Add(CreateBodyLabel(
                "3. 축하합니다! 차단한 서버로의 접속을 막았습니다.\r\n   다시 매칭해 주세요."), 0, 8);
            return notice;
        }

        private static Label CreateBodyLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                BackColor = Background,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 10.5F),
                TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private static FlowLayoutPanel CreateInlineStep(params string[] parts)
        {
            var row = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            foreach (string part in parts)
            {
                bool emphasized = IsInlineToken(part);
                if (emphasized)
                {
                    row.Controls.Add(new InlineTokenLabel(part));
                    continue;
                }
                row.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = part,
                    BackColor = Background,
                    ForeColor = TextPrimary,
                    Font = new Font("Malgun Gothic", 10.5F),
                    Margin = new Padding(0, 2, 0, 0),
                    Padding = new Padding(0)
                });
            }
            return row;
        }

        private static bool IsInlineToken(string text)
        {
            return string.Equals(text, "종료", StringComparison.Ordinal)
                || string.Equals(text, "ESCAPE FROM TARKOV", StringComparison.Ordinal)
                || string.Equals(text, "재진입", StringComparison.Ordinal)
                || string.Equals(text, "나가기 확인", StringComparison.Ordinal);
        }

        private static TableLayoutPanel CreateStepBlock(
            string[] firstLineParts,
            string[] secondLineParts)
        {
            var block = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            block.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            block.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            block.Controls.Add(CreateInlineStep(firstLineParts), 0, 0);
            block.Controls.Add(CreateInlineStep(secondLineParts), 0, 1);
            return block;
        }

        private static int MeasureNoticeRequiredWidth(TableLayoutPanel notice)
        {
            int requiredWidth = 0;
            foreach (Control control in notice.Controls)
            {
                var label = control as Label;
                if (label != null)
                {
                    string[] lines = (label.Text ?? string.Empty).Split(
                        new[] { "\r\n", "\n" },
                        StringSplitOptions.None);
                    foreach (string line in lines)
                    {
                        Size measured = TextRenderer.MeasureText(
                            line,
                            label.Font,
                            Size.Empty,
                            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix
                            | TextFormatFlags.SingleLine);
                        requiredWidth = Math.Max(requiredWidth, measured.Width);
                    }
                    continue;
                }

                var step = control as TableLayoutPanel;
                if (step == null) continue;
                foreach (Control stepChild in step.Controls)
                {
                    var flow = stepChild as FlowLayoutPanel;
                    if (flow == null) continue;
                    int lineWidth = 0;
                    foreach (Control part in flow.Controls)
                        lineWidth += part.GetPreferredSize(Size.Empty).Width + part.Margin.Horizontal;
                    requiredWidth = Math.Max(requiredWidth, lineWidth);
                }
            }
            return requiredWidth;
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
            int diameter = safeRadius * 2;
            var arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class InlineTokenLabel : Control
        {
            public InlineTokenLabel(string text)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                Text = text;
                Font = new Font("Malgun Gothic", 10F, FontStyle.Bold);
                ForeColor = TextPrimary;
                BackColor = Background;
                AutoSize = true;
                Margin = new Padding(2, 0, 2, 0);
                Size = GetPreferredSize(Size.Empty);
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                Size textSize = TextRenderer.MeasureText(Text ?? string.Empty, Font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int horizontal = Math.Max(12, (int)Math.Round(12F * DeviceDpi / 96F));
                int vertical = Math.Max(6, (int)Math.Round(6F * DeviceDpi / 96F));
                return new Size(textSize.Width + horizontal, textSize.Height + vertical);
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                Size = GetPreferredSize(Size.Empty);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Background);
                int one = Math.Max(1, (int)Math.Round(DeviceDpi / 96F));
                Rectangle bounds = new Rectangle(one, one, Math.Max(1, Width - (one * 2) - 1),
                    Math.Max(1, Height - (one * 2) - 1));
                using (GraphicsPath path = CreateRoundedPath(bounds,
                    Math.Max(4, (int)Math.Round(4F * DeviceDpi / 96F))))
                using (var fill = new SolidBrush(SurfaceAlt))
                using (var outline = new Pen(Border, one))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(outline, path);
                }
                TextRenderer.DrawText(e.Graphics, Text, Font, bounds, TextPrimary,
                    TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private sealed class AccentConfirmButton : Button
        {
            private bool _hovered;
            private bool _pressed;

            public AccentConfirmButton()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                BackColor = Background;
                ForeColor = ButtonText;
                TabStop = true;
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hovered = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hovered = false;
                _pressed = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) _pressed = true;
                Invalidate();
                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                _pressed = false;
                Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnKeyDown(KeyEventArgs kevent)
            {
                if (kevent.KeyCode == Keys.Space) _pressed = true;
                Invalidate();
                base.OnKeyDown(kevent);
            }

            protected override void OnKeyUp(KeyEventArgs kevent)
            {
                _pressed = false;
                Invalidate();
                base.OnKeyUp(kevent);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(Background);
                int one = Math.Max(1, (int)Math.Round(DeviceDpi / 96F));
                int pressedOffset = _pressed ? one : 0;
                Rectangle shadowBounds = new Rectangle(one * 3, one * 4,
                    Math.Max(1, Width - (one * 6)), Math.Max(1, Height - (one * 7)));
                using (GraphicsPath shadow = CreateRoundedPath(shadowBounds,
                    Math.Max(8, (int)Math.Round(8F * DeviceDpi / 96F))))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(115, 0, 0, 0)))
                    e.Graphics.FillPath(shadowBrush, shadow);

                Rectangle faceBounds = new Rectangle(one * 2, one + pressedOffset,
                    Math.Max(1, Width - (one * 5)), Math.Max(1, Height - (one * 6)));
                Color top = _hovered ? Color.FromArgb(255, 194, 91) : AccentBright;
                Color bottom = _pressed ? AccentDark : Accent;
                using (GraphicsPath face = CreateRoundedPath(faceBounds,
                    Math.Max(8, (int)Math.Round(8F * DeviceDpi / 96F))))
                using (var gradient = new LinearGradientBrush(faceBounds, top, bottom,
                    LinearGradientMode.Vertical))
                using (var outline = new Pen(AccentDark, one))
                {
                    e.Graphics.FillPath(gradient, face);
                    e.Graphics.DrawPath(outline, face);
                }
                Rectangle highlight = faceBounds;
                highlight.Inflate(-one * 2, -one * 2);
                highlight.Height = Math.Max(one, highlight.Height / 3);
                using (GraphicsPath sheen = CreateRoundedPath(highlight,
                    Math.Max(4, (int)Math.Round(4F * DeviceDpi / 96F))))
                using (var sheenBrush = new SolidBrush(Color.FromArgb(35, Color.White)))
                    e.Graphics.FillPath(sheenBrush, sheen);

                Rectangle textBounds = faceBounds;
                textBounds.Offset(0, pressedOffset);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ButtonText,
                    TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (Focused && ShowFocusCues)
                {
                    Rectangle focus = faceBounds;
                    focus.Inflate(-one * 5, -one * 4);
                    ControlPaint.DrawFocusRectangle(e.Graphics, focus, ButtonText, Color.Transparent);
                }
            }
        }
    }
}
