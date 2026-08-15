// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    /// <summary>
    /// Paints only the otherwise unused gap below a visible vertical scrollbar
    /// and to the right of a visible horizontal scrollbar. The native scrollbar
    /// child windows and all of their hit-test bounds remain untouched.
    /// </summary>
    internal sealed class DataGridViewScrollCorner : Control
    {
        private const int WindowHitTestMessage = 0x0084;
        private const int HitTestTransparent = -1;

        private readonly DataGridView _grid;
        private bool _updatingBounds;

        internal DataGridViewScrollCorner(DataGridView grid, Color trackColor)
        {
            if (grid == null) throw new ArgumentNullException("grid");

            _grid = grid;
            Name = "DarkScrollCorner";
            BackColor = trackColor;
            TabStop = false;
            AccessibleRole = AccessibleRole.None;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Opaque
                | ControlStyles.UserPaint,
                true);

            _grid.ControlAdded += GridControlAdded;
            _grid.ControlRemoved += GridControlRemoved;
            _grid.Layout += GridLayoutChanged;
            _grid.Resize += GridLayoutChanged;
            _grid.HandleCreated += GridLayoutChanged;
            foreach (Control child in _grid.Controls)
                ObserveScrollbar(child, true);

            _grid.Controls.Add(this);
            RefreshBounds();
        }

        internal void RefreshBounds()
        {
            if (_updatingBounds || IsDisposed || _grid.IsDisposed) return;
            _updatingBounds = true;
            try
            {
                HScrollBar horizontal = null;
                VScrollBar vertical = null;
                foreach (Control child in _grid.Controls)
                {
                    var horizontalCandidate = child as HScrollBar;
                    if (horizontalCandidate != null && horizontalCandidate.Visible)
                        horizontal = horizontalCandidate;
                    var verticalCandidate = child as VScrollBar;
                    if (verticalCandidate != null && verticalCandidate.Visible)
                        vertical = verticalCandidate;
                }

                bool useDarkCorner = horizontal != null && vertical != null;
                try { useDarkCorner &= !SystemInformation.HighContrast; }
                catch { useDarkCorner = false; }

                Rectangle desired = Rectangle.Empty;
                if (useDarkCorner)
                {
                    // Derive the unused gap from the child scrollbar endpoints,
                    // rather than assuming a DPI-dependent scrollbar size. This
                    // starts after both hit-test rectangles, so no arrow or thumb
                    // input can be intercepted by this filler.
                    desired = Rectangle.FromLTRB(
                        horizontal.Right,
                        vertical.Bottom,
                        vertical.Right,
                        horizontal.Bottom);
                    desired.Intersect(_grid.ClientRectangle);
                    useDarkCorner = desired.Width > 0 && desired.Height > 0;
                }

                if (Bounds != desired) Bounds = desired;
                if (Visible != useDarkCorner) Visible = useDarkCorner;
                if (useDarkCorner)
                {
                    BringToFront();
                    Invalidate();
                }
            }
            finally
            {
                _updatingBounds = false;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WindowHitTestMessage)
            {
                message.Result = new IntPtr(HitTestTransparent);
                return;
            }
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _grid != null)
            {
                _grid.ControlAdded -= GridControlAdded;
                _grid.ControlRemoved -= GridControlRemoved;
                _grid.Layout -= GridLayoutChanged;
                _grid.Resize -= GridLayoutChanged;
                _grid.HandleCreated -= GridLayoutChanged;
                foreach (Control child in _grid.Controls)
                    ObserveScrollbar(child, false);
            }
            base.Dispose(disposing);
        }

        private void GridControlAdded(object sender, ControlEventArgs e)
        {
            ObserveScrollbar(e.Control, true);
            RefreshBounds();
        }

        private void GridControlRemoved(object sender, ControlEventArgs e)
        {
            ObserveScrollbar(e.Control, false);
            RefreshBounds();
        }

        private void GridLayoutChanged(object sender, EventArgs e)
        {
            RefreshBounds();
        }

        private void ObserveScrollbar(Control control, bool observe)
        {
            if (!(control is HScrollBar) && !(control is VScrollBar)) return;
            if (observe)
            {
                control.VisibleChanged -= GridLayoutChanged;
                control.LocationChanged -= GridLayoutChanged;
                control.SizeChanged -= GridLayoutChanged;
                control.VisibleChanged += GridLayoutChanged;
                control.LocationChanged += GridLayoutChanged;
                control.SizeChanged += GridLayoutChanged;
            }
            else
            {
                control.VisibleChanged -= GridLayoutChanged;
                control.LocationChanged -= GridLayoutChanged;
                control.SizeChanged -= GridLayoutChanged;
            }
        }
    }
}
