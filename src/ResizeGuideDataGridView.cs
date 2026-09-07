// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    // Keep native column resizing, capture, minimum widths and sorting behavior.
    // Paint its position in the normal paint pass so buffering/repaints cannot
    // erase the guide. An attached width preference allows fill columns to be
    // resized explicitly; cancelled gestures restore their original fill mode.
    internal class ResizeGuideDataGridView : DataGridView
    {
        private DataGridViewColumn _guideColumn;
        private int _pointerStart;
        private int _edgeStart;
        private int _widthStart;
        private Rectangle _guideBounds;
        private DataGridViewColumn _manualFillColumn;
        private bool _completingResize;
        private static readonly Color GuideColor = Color.FromArgb(232, 157, 54);
        internal event DataGridViewColumnEventHandler UserColumnResized;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            DataGridViewColumn candidate = null;
            int edge = 0;
            if (e.Button == MouseButtons.Left && e.Clicks == 1 && AllowUserToResizeColumns
                && HitTest(e.X, e.Y).Type == DataGridViewHitTestType.ColumnHeader)
            {
                int tolerance = Math.Max(4, (int)Math.Round(4F * DeviceDpi / 96F));
                int nearest = tolerance + 1;
                foreach (DataGridViewColumn column in Columns)
                {
                    if (!column.Visible || column.Resizable == DataGridViewTriState.False) continue;
                    Rectangle header = GetCellDisplayRectangle(column.Index, -1, false);
                    int boundary = RightToLeft == RightToLeft.Yes ? header.Left : header.Right - 1;
                    int distance = Math.Abs(e.X - boundary);
                    if (header.Width > 0 && distance < nearest)
                    {
                        nearest = distance;
                        candidate = column;
                        edge = boundary;
                    }
                }
            }
            if (candidate != null && UserColumnResized != null && Cursor == Cursors.SizeWE
                && candidate.InheritedAutoSizeMode == DataGridViewAutoSizeColumnMode.Fill)
            {
                _manualFillColumn = candidate;
                int width = candidate.Width;
                candidate.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                candidate.Width = width;
            }
            base.OnMouseDown(e);
            // Native hit testing is authoritative: do not draw for an ordinary
            // header click, a double-click auto-size, or an unresizable boundary.
            if (candidate != null && Capture && Cursor == Cursors.SizeWE)
            {
                _guideColumn = candidate;
                _pointerStart = e.X;
                _edgeStart = edge;
                _widthStart = candidate.Width;
                MoveGuide(e.X);
            }
            else FinishManualFill(false);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_guideColumn != null && Capture) MoveGuide(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            DataGridViewColumn resized = _guideColumn;
            int previousWidth = _widthStart;
            _completingResize = true;
            try { base.OnMouseUp(e); }
            finally { _completingResize = false; ClearGuide(); }
            bool changed = resized != null && resized.Width != previousWidth;
            FinishManualFill(changed);
            if (changed) NotifyUserResize(resized);
        }

        protected override void OnColumnDividerDoubleClick(DataGridViewColumnDividerDoubleClickEventArgs e)
        {
            base.OnColumnDividerDoubleClick(e);
            if (e.Handled || e.ColumnIndex < 0 || !IsHandleCreated) return;
            DataGridViewColumn column = Columns[e.ColumnIndex];
            // WinForms applies double-click auto-size after raising this event.
            BeginInvoke(new Action(delegate
            {
                if (!IsDisposed && column.DataGridView == this) NotifyUserResize(column);
            }));
        }

        private void NotifyUserResize(DataGridViewColumn column)
        {
            var handler = UserColumnResized;
            if (handler != null) handler(this, new DataGridViewColumnEventArgs(column));
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture)
            {
                if (!_completingResize) FinishManualFill(false);
                ClearGuide();
            }
        }

        private void FinishManualFill(bool keepWidth)
        {
            DataGridViewColumn column = _manualFillColumn;
            _manualFillColumn = null;
            if (!keepWidth && column != null && column.DataGridView == this)
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_guideColumn == null || !Capture || _guideBounds.IsEmpty) return;
            using (var brush = new SolidBrush(SystemInformation.HighContrast ? SystemColors.Highlight : GuideColor))
                e.Graphics.FillRectangle(brush, _guideBounds);
        }

        private void MoveGuide(int pointerX)
        {
            bool rtl = RightToLeft == RightToLeft.Yes;
            int delta = pointerX - _pointerStart;
            int width = Math.Max(_guideColumn.MinimumWidth, _widthStart + (rtl ? -delta : delta));
            int x = _edgeStart + (rtl ? -1 : 1) * (width - _widthStart);
            Rectangle display = DisplayRectangle;
            int thickness = Math.Max(2, (int)Math.Round(2F * DeviceDpi / 96F));
            int left = Math.Max(display.Left, Math.Min(display.Right - thickness, x - thickness / 2));
            Rectangle next = display.Width > 0 && display.Height > 0
                ? Rectangle.Intersect(display, new Rectangle(left, display.Top, thickness, display.Height))
                : Rectangle.Empty;
            if (next == _guideBounds) return;
            Rectangle previous = _guideBounds;
            _guideBounds = next;
            if (!previous.IsEmpty) Invalidate(previous);
            if (!next.IsEmpty) Invalidate(next);
        }

        private void ClearGuide()
        {
            Rectangle previous = _guideBounds;
            _guideColumn = null;
            _guideBounds = Rectangle.Empty;
            if (!previous.IsEmpty && !IsDisposed) Invalidate(previous);
        }
    }
}
