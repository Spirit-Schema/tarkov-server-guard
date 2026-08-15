// SPDX-License-Identifier: MPL-2.0
// Copyright 2026 Spirit-Schema

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal enum PingKickAction
    {
        None,
        Block,
        Unblock
    }

    internal sealed class PingKickActionColumn : DataGridViewColumn
    {
        public PingKickActionColumn()
            : base(new PingKickActionCell())
        {
            Name = "connectionControl";
            HeaderText = "접속 제어";
            Width = 158;
            MinimumWidth = 150;
            SortMode = DataGridViewColumnSortMode.NotSortable;
        }
    }

    internal sealed class PingKickActionCell : DataGridViewCell
    {
        private static readonly Color BlockColor = Color.FromArgb(192, 68, 75);
        private static readonly Color UnblockColor = Color.FromArgb(58, 151, 96);
        private static readonly Color DisabledColor = Color.FromArgb(67, 75, 85);
        private static readonly Color DisabledTextColor = Color.FromArgb(151, 159, 169);
        private static readonly Color ButtonBorder = Color.FromArgb(92, 101, 112);

        public bool BlockEnabled { get; set; }
        public bool UnblockEnabled { get; set; }

        public override Type ValueType
        {
            get { return typeof(string); }
        }

        public override object DefaultNewRowValue
        {
            get { return string.Empty; }
        }

        public override object Clone()
        {
            var clone = (PingKickActionCell)base.Clone();
            clone.BlockEnabled = BlockEnabled;
            clone.UnblockEnabled = UnblockEnabled;
            return clone;
        }

        public PingKickAction HitTestAction(int x, int y, int width, int height)
        {
            Rectangle blockBounds;
            Rectangle unblockBounds;
            GetButtonBounds(
                new Rectangle(0, 0, width, height),
                GetDpiScale(),
                out blockBounds,
                out unblockBounds);
            if (blockBounds.Contains(x, y)) return PingKickAction.Block;
            if (unblockBounds.Contains(x, y)) return PingKickAction.Unblock;
            return PingKickAction.None;
        }

        protected override void Paint(
            Graphics graphics,
            Rectangle clipBounds,
            Rectangle cellBounds,
            int rowIndex,
            DataGridViewElementStates cellState,
            object value,
            object formattedValue,
            string errorText,
            DataGridViewCellStyle cellStyle,
            DataGridViewAdvancedBorderStyle advancedBorderStyle,
            DataGridViewPaintParts paintParts)
        {
            base.Paint(
                graphics,
                clipBounds,
                cellBounds,
                rowIndex,
                cellState,
                value,
                formattedValue,
                errorText,
                cellStyle,
                advancedBorderStyle,
                paintParts & ~DataGridViewPaintParts.ContentForeground);

            if ((paintParts & DataGridViewPaintParts.ContentForeground) == 0) return;

            Rectangle blockBounds;
            Rectangle unblockBounds;
            GetButtonBounds(cellBounds, GetDpiScale(), out blockBounds, out unblockBounds);

            Rectangle visibleBounds = Rectangle.Intersect(cellBounds, clipBounds);
            if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0) return;

            GraphicsState state = graphics.Save();
            try
            {
                graphics.SetClip(visibleBounds, CombineMode.Intersect);
                Font font = cellStyle.Font
                    ?? (DataGridView == null ? Control.DefaultFont : DataGridView.Font);
                DrawButton(graphics, blockBounds, "차단", BlockEnabled, BlockColor, font);
                DrawButton(graphics, unblockBounds, "해제", UnblockEnabled, UnblockColor, font);
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private float GetDpiScale()
        {
            int dpi = DataGridView == null ? 96 : DataGridView.DeviceDpi;
            if (dpi <= 0) dpi = 96;
            return dpi / 96F;
        }

        private static void GetButtonBounds(
            Rectangle cellBounds,
            float dpiScale,
            out Rectangle blockBounds,
            out Rectangle unblockBounds)
        {
            int horizontalPadding = ScalePixel(4, dpiScale);
            int verticalPadding = ScalePixel(4, dpiScale);
            int gap = ScalePixel(4, dpiScale);
            int availableWidth = cellBounds.Width - (horizontalPadding * 2) - gap;
            int height = cellBounds.Height - (verticalPadding * 2);
            int buttonWidth = availableWidth / 2;

            if (buttonWidth <= 0 || height <= 0)
            {
                blockBounds = Rectangle.Empty;
                unblockBounds = Rectangle.Empty;
                return;
            }

            // Keep both buttons exactly the same width. If an odd pixel remains,
            // it stays in the trailing padding instead of deforming one button.
            blockBounds = new Rectangle(
                cellBounds.X + horizontalPadding,
                cellBounds.Y + verticalPadding,
                buttonWidth,
                height);
            unblockBounds = new Rectangle(
                blockBounds.Right + gap,
                cellBounds.Y + verticalPadding,
                buttonWidth,
                height);
        }

        private static int ScalePixel(int logicalPixels, float dpiScale)
        {
            if (dpiScale <= 0 || float.IsNaN(dpiScale) || float.IsInfinity(dpiScale))
                dpiScale = 1F;
            return Math.Max(1, (int)Math.Round(
                logicalPixels * dpiScale,
                MidpointRounding.AwayFromZero));
        }

        private static void DrawButton(
            Graphics graphics,
            Rectangle bounds,
            string text,
            bool enabled,
            Color enabledColor,
            Font font)
        {
            if (bounds.Width <= 1 || bounds.Height <= 1) return;

            Color backColor = enabled ? enabledColor : DisabledColor;
            Color textColor = enabled ? Color.White : DisabledTextColor;
            using (var backgroundBrush = new SolidBrush(backColor))
            using (var borderPen = new Pen(enabled ? enabledColor : ButtonBorder))
            {
                graphics.FillRectangle(backgroundBrush, bounds);
                graphics.DrawRectangle(borderPen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
            }

            TextRenderer.DrawText(
                graphics,
                text,
                font,
                bounds,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
