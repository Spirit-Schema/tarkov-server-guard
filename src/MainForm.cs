// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    public sealed class MainForm : BrandedForm
    {
        private static string HeaderSortToolTip
        {
            get { return AppText.Get("Main.Sort.HeaderHelp"); }
        }

        private static readonly Color Background = Color.FromArgb(15, 18, 22);
        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color SurfaceAlt = Color.FromArgb(31, 38, 46);
        private static readonly Color Border = Color.FromArgb(54, 63, 74);
        private static readonly Color HeaderBevelHighlight = Color.FromArgb(72, 82, 94);
        private static readonly Color HeaderBevelShadow = Color.FromArgb(13, 17, 22);
        private static readonly Color Accent = Color.FromArgb(232, 157, 54);
        private static readonly Color AccentHover = Color.FromArgb(246, 173, 70);
        private static readonly Color TextPrimary = Color.FromArgb(244, 246, 248);
        private static readonly Color TextMuted = Color.FromArgb(157, 168, 181);
        private static readonly Color Success = Color.FromArgb(78, 201, 134);
        private static readonly Color Warning = Color.FromArgb(247, 190, 79);
        private static readonly Color Danger = Color.FromArgb(238, 106, 112);
        private static readonly Color ReportOrange = Color.FromArgb(205, 132, 48);
        private static readonly Color NativeScrollTrack = Color.FromArgb(23, 23, 23);

        private readonly bool _demoMode;
        private readonly WindowSizeStore _windowSizeStore;
        private Size _lastNormalClientSize;
        private bool _windowSizeReady;
        private readonly TarkovLogSettingsStore _settingsStore = new TarkovLogSettingsStore();
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly Dictionary<string, PingResult> _pingResults =
            new Dictionary<string, PingResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, GeoInfo> _geoResults =
            new Dictionary<string, GeoInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FirewallQueryResult> _firewallStates =
            new Dictionary<string, FirewallQueryResult>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _measuringIpAddresses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _firewallBusyIpAddresses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly RaidNoteStore _noteStore;
        // Narrow service seams keep the real action workflow testable without touching
        // Windows Firewall or user metadata. Production defaults remain the same services.
        private Func<string, bool, Task<FirewallChangeResult>> _changeFirewallState =
            FirewallRuleManager.ChangeWithElevationAsync;
        private Func<string, FirewallQueryResult> _queryFirewallState = FirewallRuleManager.Query;
        private Action<string> _saveBlockedMetadata;
        private Action<string> _removeBlockedMetadata =
            ipAddress => BlockedServerMetadataStore.Remove(new[] { ipAddress });
        private Func<string, Task<RaidQualityEvidenceSummary>> _loadRaidQualityEvidence;
        private Task _pendingQualityEvidence;
        private Task<RaidLogScanResult> _qualityEvidenceScan;
        private string _qualityEvidenceEftPath;
        private string _qualityEvidenceArenaPath;
        private long _statusRevision;

        internal Task PendingQualityEvidenceUpdate { get { return _pendingQualityEvidence; } }
        private readonly InitialFirewallStateRefreshCoordinator
            _initialFirewallStateRefresh = new InitialFirewallStateRefreshCoordinator(
                new SystemInitialFirewallStateQueryGateway());

        private TextBox _eftPathTextBox;
        private TextBox _arenaPathTextBox;
        private Button _eftBrowseButton;
        private Button _arenaBrowseButton;
        private Button _rediscoverPathButton;
        private Button _applyPathButton;
        private Label _launcherSelectionLabel;
        private Label _statusLabel;
        private TableLayoutPanel _historyLayout;
        private bool _updatingStatusAreaHeight;
        private Label _ipLabel;
        private Label _mapValueLabel;
        private Label _timeValueLabel;
        private Label _pingValueLabel;
        private Label _locationValueLabel;
        private Label _actualRttValueLabel;
        private Label _packetLossValueLabel;
        private Label[] _detailInfoValueLabels;
        private DataGridView _historyGrid;
        private StickyActionGrid _stickyActionGrid;
        private DataGridViewScrollCorner _historyScrollCorner;
        private bool _syncingStickyActionGrid;
        private bool _stickyActionLayoutUpdatePending;
        private Button _queryButton;
        private Button _copyIpButton;
        private Button _blockedServersButton;
        private Button _notesArchiveButton;
        private Button _allFilterButton;
        private Button _eftFilterButton;
        private Button _arenaFilterButton;
        private Button _regionFilterButton;
        private Button _manualUpdateButton;
        private Button _settingsButton;
        private Button _recentPeriodButton;
        private Button _todayPeriodButton;
        private Button _sevenDaysPeriodButton;
        private Button _thirtyDaysPeriodButton;
        private Button _customPeriodButton;

        private IList<ServerSession> _allSessions = new List<ServerSession>();
        private IList<ServerSession> _visibleSessions = new List<ServerSession>();
        private ServerSession _selectedSession;
        private TarkovGame? _gameFilter;
        private readonly HashSet<string> _selectedRegionCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _regionSourceCount;
        private SessionPeriodPreset _sessionPeriod = SessionPeriodPreset.Recent100;
        private DateTime? _customPeriodStart;
        private DateTime? _customPeriodEnd;
        private int _periodMatchCount;
        private bool _periodResultsTruncated;
        private bool _dateRangeScanIncomplete;
        private bool _logScanIncomplete;
        private string _historySortColumn;
        private SortOrder _historySortOrder = SortOrder.None;
        private bool _isRefreshing;
        private bool _isMeasuring;
        private bool _isFirewallChanging;
        private CancellationTokenSource _queryCancellation;
        private CancellationTokenSource _updateCheckCancellation;
        private TableLayoutPanel _advancedDetailsLayout;
        private ContextMenuStrip _regionFilterMenu;
        private string _appliedEftPath;
        private string _appliedArenaPath;
        private readonly object _launcherSelectionRefreshSync = new object();
        private FileSystemWatcher _launcherLogWatcher;
        private FileSystemWatcher _arenaRegionsWatcher;
        private System.Threading.Timer _launcherSelectionDebounceTimer;
        private System.Windows.Forms.Timer _launcherSelectionWatcherHealthTimer;
        private bool _launcherSelectionRefreshRunning;
        private bool _launcherSelectionRefreshPending;
        private volatile bool _launcherSelectionMonitoringStopped;
        private string _lastEftLauncherSelection;
        private int _launcherSelectionReadVersion;
        private string _lastArenaLauncherSelection;
        private DateTime? _lastEftLauncherSelectionAt;
        private DateTime? _lastArenaLauncherSelectionAt;
        private enum SessionPeriodPreset
        {
            Recent100,
            Today,
            Last7Days,
            Last30Days,
            Custom
        }

        private enum ToolIconKind
        {
            Note,
            Shield
        }

        private struct FirewallActionPresentationState
        {
            public bool HasResult;
            public bool Known;
            public bool Busy;
            public bool IsBlocked;
            public bool BlockAvailable;
            public bool UnblockAvailable;

            public bool SortKnown
            {
                get { return BlockAvailable != UnblockAvailable; }
            }
        }

        private sealed class RegionMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return SurfaceAlt; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(47, 56, 67); } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color ImageMarginGradientBegin { get { return SurfaceAlt; } }
            public override Color ImageMarginGradientMiddle { get { return SurfaceAlt; } }
            public override Color ImageMarginGradientEnd { get { return SurfaceAlt; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
        }

        private class BufferedDataGridView : ResizeGuideDataGridView
        {
            public BufferedDataGridView()
            {
                DoubleBuffered = true;
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer,
                    true);
                UpdateStyles();
            }
        }

        private sealed class StickyActionGrid : BufferedDataGridView
        {
            private readonly MainForm _owner;

            public StickyActionGrid(MainForm owner)
            {
                if (owner == null) throw new ArgumentNullException("owner");
                _owner = owner;
                TabStop = true;
                AccessibleName = AppText.Get("Main.Action.HeaderAccessibleName");
                AccessibleDescription = AppText.Get("Main.Action.HeaderAccessibleDescription");
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                _owner.ScrollHistoryRowsFromStickyActions(e.Delta);
            }

            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                Keys keyCode = keyData & Keys.KeyCode;
                Keys modifiers = keyData & Keys.Modifiers;
                if (modifiers == Keys.None
                    && (keyCode == Keys.Enter || keyCode == Keys.Space)
                    && CurrentCell is DataGridViewButtonCell)
                {
                    OnCellContentClick(new DataGridViewCellEventArgs(
                        CurrentCell.ColumnIndex,
                        CurrentCell.RowIndex));
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        private sealed class HeaderLinkButton : Button
        {
            public HeaderLinkButton()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                    true);
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                int dpi = DeviceDpi <= 0 ? 96 : DeviceDpi;
                Size glyph = TextRenderer.MeasureText(
                    Text ?? string.Empty,
                    Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine);
                return new Size(
                    glyph.Width + ScaleLogical(12, dpi),
                    glyph.Height + ScaleLogical(8, dpi));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                int dpi = DeviceDpi <= 0 ? 96 : DeviceDpi;
                Size glyph = TextRenderer.MeasureText(
                    Text ?? string.Empty,
                    Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine);
                int reservedBottom = ScaleLogical(3, dpi);
                int availableHeight = Math.Max(1, ClientSize.Height - reservedBottom);
                int textTop = Math.Max(0, (availableHeight - glyph.Height) / 2);
                var textBounds = new Rectangle(0, textTop, ClientSize.Width, glyph.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text ?? string.Empty,
                    Font,
                    textBounds,
                    Enabled ? ForeColor : SystemColors.GrayText,
                    TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine
                    | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter);

                if (Focused && ShowFocusCues)
                {
                    Rectangle focusBounds = Rectangle.Inflate(textBounds, -1, -1);
                    if (focusBounds.Width > 0 && focusBounds.Height > 0)
                        ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, ForeColor, BackColor);
                }
            }
        }

        private sealed class DetailValueLabel : Label
        {
            private string _primaryText = string.Empty;
            private string _suffixText = string.Empty;

            public Color SuffixColor { get; set; }
            public bool FitTextToWidth { get; set; }

            public override string Text
            {
                get { return base.Text; }
                set
                {
                    _primaryText = value ?? string.Empty;
                    _suffixText = string.Empty;
                    base.Text = _primaryText;
                    Invalidate();
                }
            }

            public void SetTextSegments(string primaryText, string suffixText)
            {
                _primaryText = primaryText ?? string.Empty;
                _suffixText = suffixText ?? string.Empty;
                base.Text = _primaryText + _suffixText;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                var bounds = new Rectangle(
                    Padding.Left,
                    Padding.Top,
                    Math.Max(0, ClientSize.Width - Padding.Horizontal),
                    Math.Max(0, ClientSize.Height - Padding.Vertical));
                if (bounds.Width <= 0 || bounds.Height <= 0) return;

                const TextFormatFlags measureFlags = TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine;
                const TextFormatFlags drawFlags = measureFlags
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis;
                Font fittedFont = null;
                Font drawFont = Font;
                if (FitTextToWidth)
                {
                    float fittedSize = Font.Size;
                    string completeText = _primaryText + _suffixText;
                    while (TextRenderer.MeasureText(
                            e.Graphics,
                            completeText,
                            drawFont,
                            Size.Empty,
                            measureFlags).Width > bounds.Width
                        && fittedSize > (IsEnglishUi ? 8F : 6F))
                    {
                        if (fittedFont != null) fittedFont.Dispose();
                        fittedSize = Math.Max(IsEnglishUi ? 8F : 6F, fittedSize - 0.25F);
                        fittedFont = new Font(
                            Font.FontFamily,
                            fittedSize,
                            Font.Style,
                            GraphicsUnit.Point);
                        drawFont = fittedFont;
                    }
                }
                int primaryWidth = TextRenderer.MeasureText(
                    e.Graphics,
                    _primaryText,
                    drawFont,
                    Size.Empty,
                    measureFlags).Width;
                int suffixWidth = TextRenderer.MeasureText(
                    e.Graphics,
                    _suffixText,
                    drawFont,
                    Size.Empty,
                    measureFlags).Width;

                if (string.IsNullOrEmpty(_suffixText))
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        _primaryText,
                        drawFont,
                        bounds,
                        ForeColor,
                        drawFlags);
                    if (fittedFont != null) fittedFont.Dispose();
                    return;
                }

                bool fits = primaryWidth + suffixWidth <= bounds.Width;
                int visibleSuffixWidth = Math.Min(suffixWidth, bounds.Width);
                int primaryBoundsWidth = fits
                    ? primaryWidth
                    : Math.Max(0, bounds.Width - visibleSuffixWidth);
                if (primaryBoundsWidth > 0)
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        _primaryText,
                        drawFont,
                        new Rectangle(bounds.Left, bounds.Top, primaryBoundsWidth, bounds.Height),
                        ForeColor,
                        drawFlags);
                }

                int suffixLeft = fits
                    ? bounds.Left + primaryBoundsWidth
                    : bounds.Right - visibleSuffixWidth;
                TextRenderer.DrawText(
                    e.Graphics,
                    _suffixText,
                    drawFont,
                    new Rectangle(suffixLeft, bounds.Top, visibleSuffixWidth, bounds.Height),
                    SuffixColor,
                    drawFlags);
                if (fittedFont != null) fittedFont.Dispose();
            }
        }

        private sealed class FittedDetailLabel : Label
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                var bounds = new Rectangle(
                    Padding.Left,
                    Padding.Top,
                    Math.Max(0, ClientSize.Width - Padding.Horizontal),
                    Math.Max(0, ClientSize.Height - Padding.Vertical));
                if (bounds.Width <= 0 || bounds.Height <= 0) return;

                const TextFormatFlags measureFlags = TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix
                    | TextFormatFlags.SingleLine;
                const TextFormatFlags drawFlags = measureFlags
                    | TextFormatFlags.VerticalCenter;
                Font fittedFont = null;
                Font drawFont = Font;
                float fittedSize = Font.Size;
                while (TextRenderer.MeasureText(
                        e.Graphics,
                        Text ?? string.Empty,
                        drawFont,
                        Size.Empty,
                        measureFlags).Width > bounds.Width
                    && fittedSize > (IsEnglishUi ? 8F : 6F))
                {
                    if (fittedFont != null) fittedFont.Dispose();
                    fittedSize = Math.Max(IsEnglishUi ? 8F : 6F, fittedSize - 0.25F);
                    fittedFont = new Font(
                        Font.FontFamily,
                        fittedSize,
                        Font.Style,
                        GraphicsUnit.Point);
                    drawFont = fittedFont;
                }

                TextRenderer.DrawText(
                    e.Graphics,
                    Text ?? string.Empty,
                    drawFont,
                    bounds,
                    ForeColor,
                    drawFlags);
                if (fittedFont != null) fittedFont.Dispose();
            }
        }

        private sealed class ToolIconButton : Button
        {
            private readonly ToolIconKind _iconKind;
            private readonly Color _iconColor;
            private bool _hovered;
            private bool _pressed;

            public ToolIconButton(string text, ToolIconKind iconKind, Color iconColor)
            {
                Text = text;
                _iconKind = iconKind;
                _iconColor = iconColor;
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.UserPaint,
                    true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Rectangle bounds = ClientRectangle;
                if (bounds.Width <= 1 || bounds.Height <= 1) return;

                Color back = !Enabled
                    ? Color.FromArgb(27, 32, 38)
                    : (_pressed
                        ? FlatAppearance.MouseDownBackColor
                        : (_hovered ? FlatAppearance.MouseOverBackColor : BackColor));
                Color border = Enabled ? FlatAppearance.BorderColor : Color.FromArgb(48, 56, 65);
                Color text = Enabled ? TextPrimary : Color.FromArgb(105, 115, 126);
                Color icon = Enabled ? _iconColor : Color.FromArgb(92, 101, 111);
                using (var backgroundBrush = new SolidBrush(back))
                    e.Graphics.FillRectangle(backgroundBrush, bounds);
                using (var borderPen = new Pen(border))
                    e.Graphics.DrawRectangle(borderPen, 0, 0, bounds.Width - 1, bounds.Height - 1);

                int dpi = DeviceDpi <= 0 ? 96 : DeviceDpi;
                int iconSize = Math.Max(12, (int)Math.Round(14F * dpi / 96F));
                int gap = Math.Max(5, (int)Math.Round(6F * dpi / 96F));
                Size textSize = TextRenderer.MeasureText(
                    e.Graphics,
                    Text,
                    Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                int groupWidth = iconSize + gap + textSize.Width;
                int groupLeft = Math.Max(5, (bounds.Width - groupWidth) / 2);
                int offsetY = _pressed && Enabled ? 1 : 0;
                var iconBounds = new Rectangle(
                    groupLeft,
                    Math.Max(1, (bounds.Height - iconSize) / 2) + offsetY,
                    iconSize,
                    iconSize);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (_iconKind == ToolIconKind.Note)
                    DrawNoteIcon(e.Graphics, iconBounds, icon);
                else
                    DrawShieldIcon(e.Graphics, iconBounds, icon);

                var textBounds = new Rectangle(
                    iconBounds.Right + gap,
                    offsetY,
                    Math.Max(1, bounds.Width - iconBounds.Right - gap - 4),
                    bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    textBounds,
                    text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                    | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3), text, back);
            }

            private static void DrawNoteIcon(Graphics graphics, Rectangle bounds, Color color)
            {
                Rectangle bubble = new Rectangle(
                    bounds.Left + 1,
                    bounds.Top + 1,
                    Math.Max(5, bounds.Width - 3),
                    Math.Max(5, bounds.Height - 5));
                int radius = Math.Max(2, bounds.Width / 6);
                using (GraphicsPath path = CreateIconRoundedPath(bubble, radius))
                using (var pen = new Pen(color, Math.Max(1.4F, bounds.Width / 10F)))
                {
                    graphics.DrawPath(pen, path);
                    graphics.DrawLine(
                        pen,
                        bubble.Left + bubble.Width / 3,
                        bubble.Bottom,
                        bubble.Left + bubble.Width / 4,
                        bounds.Bottom - 1);
                    graphics.DrawLine(
                        pen,
                        bubble.Left + bubble.Width / 4,
                        bounds.Bottom - 1,
                        bubble.Left + bubble.Width / 2,
                        bubble.Bottom);
                }
            }

            private static void DrawShieldIcon(Graphics graphics, Rectangle bounds, Color color)
            {
                Point[] points =
                {
                    new Point(bounds.Left + bounds.Width / 2, bounds.Top + 1),
                    new Point(bounds.Right - 2, bounds.Top + bounds.Height / 4),
                    new Point(bounds.Right - 3, bounds.Top + bounds.Height * 2 / 3),
                    new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - 1),
                    new Point(bounds.Left + 2, bounds.Top + bounds.Height * 2 / 3),
                    new Point(bounds.Left + 1, bounds.Top + bounds.Height / 4)
                };
                using (var brush = new SolidBrush(color))
                    graphics.FillPolygon(brush, points);
                using (var highlight = new Pen(Color.FromArgb(90, Color.White), 1F))
                    graphics.DrawLine(
                        highlight,
                        bounds.Left + bounds.Width / 2,
                        bounds.Top + 3,
                        bounds.Left + bounds.Width / 2,
                        bounds.Bottom - 4);
            }

            private static GraphicsPath CreateIconRoundedPath(Rectangle bounds, int radius)
            {
                int safeRadius = Math.Max(1, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
                int diameter = safeRadius * 2;
                var path = new GraphicsPath();
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

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hovered = false;
                _pressed = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left) _pressed = true;
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _pressed = false;
                Invalidate();
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) _pressed = true;
                Invalidate();
            }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                base.OnKeyUp(e);
                _pressed = false;
                Invalidate();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                _pressed = false;
                Invalidate();
            }
        }

        public MainForm(bool demoMode)
            : this(demoMode, demoMode ? null : new WindowSizeStore(AppPreferencesStore.GetDefaultStorageRoot()),
                demoMode ? null : AppPreferencesStore.GetDefaultStorageRoot())
        {
        }

        internal MainForm(bool demoMode, WindowSizeStore windowSizeStore)
            : this(demoMode, windowSizeStore, null)
        {
        }

        internal MainForm(bool demoMode, WindowSizeStore windowSizeStore, string columnStorageRoot)
        {
            _demoMode = demoMode;
            _windowSizeStore = windowSizeStore;
            _saveBlockedMetadata = SaveBlockedServerMetadata;
            _loadRaidQualityEvidence = LoadRecentRaidQualityEvidenceAsync;
            // Preview sessions must never inspect the user's real notes. The store does not
            // create this disposable path, and preview entry points cannot write to it.
            _noteStore = demoMode
                ? new RaidNoteStore(Path.Combine(Path.GetTempPath(),
                    "TarkovServerGuard", "PreviewNotes-" + Guid.NewGuid().ToString("N")))
                : new RaidNoteStore();
            InitializeWindow();
            BuildInterface();
            ColumnWidthPersistence.Attach(this, "main", columnStorageRoot);

            Shown += async delegate
            {
                if (_demoMode)
                    LoadDemoData();
                else
                {
                    await InitializeAsync();
                    StartLauncherSelectionMonitoring();
                    ShowUsageNoticeOnce();
                    StartAutomaticUpdateCheck();
                }
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Size? saved = _windowSizeStore == null ? null : _windowSizeStore.Load();
            RestoreWindowSizeForWorkingArea(saved, Screen.FromControl(this).WorkingArea);
            if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
            _windowSizeReady = _windowSizeStore != null;
            RememberNormalWindowSize();
        }

        private void RestoreWindowSizeForWorkingArea(Size? saved, Rectangle area)
        {
            Size frame = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
            Size minimum = new Size(Math.Max(1, MinimumSize.Width - frame.Width),
                Math.Max(1, MinimumSize.Height - frame.Height));
            Size available = new Size(Math.Max(1, area.Width - frame.Width), Math.Max(1, area.Height - frame.Height));
            Size desired = saved ?? WindowSizeStore.ToLogicalClientSize(ClientSize, DeviceDpi);
            if (available.Width < minimum.Width || available.Height < minimum.Height)
            {
                // Keep the existing layout usable on a small working area. The window
                // fits the screen, while scrollbars expose its full minimum-size content.
                MinimumSize = new Size(Math.Min(MinimumSize.Width, Math.Max(1, area.Width)),
                    Math.Min(MinimumSize.Height, Math.Max(1, area.Height)));
                AutoScroll = true;
                AutoScrollMinSize = minimum;
                if (Controls.Count > 0)
                {
                    Control content = Controls[0];
                    content.Dock = DockStyle.None;
                    content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    content.Location = Point.Empty;
                    content.Margin = Padding.Empty;
                    content.MinimumSize = minimum;
                    ClientSizeChanged -= LayoutScrollableWindowContent;
                    ClientSizeChanged += LayoutScrollableWindowContent;
                }
            }
            ClientSize = WindowSizeStore.RestoreClientSize(desired, DeviceDpi, minimum, available);
            // WinForms may add scrollbar thickness when assigning ClientSize.
            Size = new Size(Math.Min(Width, Math.Max(1, area.Width)), Math.Min(Height, Math.Max(1, area.Height)));
            LayoutScrollableWindowContent(this, EventArgs.Empty);
            PerformLayout();
            if (AutoScroll) AutoScrollPosition = Point.Empty;
        }

        private void LayoutScrollableWindowContent(object sender, EventArgs e)
        {
            if (!AutoScroll || Controls.Count == 0) return;
            Control content = Controls[0];
            content.Size = new Size(Math.Max(content.MinimumSize.Width, ClientSize.Width),
                Math.Max(content.MinimumSize.Height, ClientSize.Height));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RememberNormalWindowSize();
        }

        private void RememberNormalWindowSize()
        {
            if (!_windowSizeReady || WindowState != FormWindowState.Normal
                || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            _lastNormalClientSize = WindowSizeStore.ToLogicalClientSize(ClientSize, DeviceDpi);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || !_windowSizeReady || _windowSizeStore == null) return;
            RememberNormalWindowSize();
            _windowSizeStore.Save(_lastNormalClientSize);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _initialFirewallStateRefresh.Dispose();
                StopLauncherSelectionMonitoring();
                CancellationTokenSource cancellation = _queryCancellation;
                if (cancellation != null)
                {
                    try { cancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                CancellationTokenSource updateCancellation = _updateCheckCancellation;
                if (updateCancellation != null)
                {
                    try { updateCancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                    updateCancellation.Dispose();
                    _updateCheckCancellation = null;
                }
                if (_regionFilterMenu != null)
                {
                    _regionFilterMenu.Dispose();
                    _regionFilterMenu = null;
                }
            }
            base.Dispose(disposing);
        }

        private async void StartAutomaticUpdateCheck()
        {
            if (_demoMode || IsDisposed || Disposing || _updateCheckCancellation != null) return;
            var cancellation = new CancellationTokenSource();
            _updateCheckCancellation = cancellation;
            try
            {
                await Task.Delay(900, cancellation.Token);
                if (IsDisposed || Disposing) return;
                GitHubUpdateService service = GitHubUpdateService.CreateProduction(
                    GetApplicationSemanticVersion());
                await service.CheckAfterUiShownAsync(this, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Closing the application cancels a pending check without user-visible errors.
            }
            catch
            {
                // Update availability must never prevent normal application use.
            }
            finally
            {
                if (ReferenceEquals(_updateCheckCancellation, cancellation))
                    _updateCheckCancellation = null;
                cancellation.Dispose();
            }
        }

        private void InitializeWindow()
        {
            Text = "Tarkov Server Guard";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 740);
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(
                Math.Min(1540, Math.Max(960, workingArea.Width - 40)),
                Math.Min(950, Math.Max(700, workingArea.Height - 40)));
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Malgun Gothic", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        private void BuildInterface()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(22, 16, 22, 16)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, IsEnglishUi ? 110F : 126F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, IsEnglishUi ? 236F : 260F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildPathBar(), 0, 1);
            root.Controls.Add(BuildCurrentServerCard(), 0, 2);
            root.Controls.Add(BuildHistoryCard(), 0, 3);
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            var title = new Label
            {
                AutoSize = true,
                Location = new Point(2, 0),
                Text = "Tarkov Server Guard",
                Font = new Font("Malgun Gothic", 18F, FontStyle.Bold),
                ForeColor = TextPrimary
            };
            panel.Controls.Add(title);

            var usageNoticeButton = CreateUsageGuideButton();
            EventHandler positionUsageNoticeButton = delegate
            {
                Size currentTitleTextSize = TextRenderer.MeasureText(
                    title.Text,
                    title.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int titleToButtonOffset = Math.Max(
                    4,
                    (int)Math.Round(4F * title.DeviceDpi / 96F));
                // The title label's larger glyphs sit optically lower than the
                // center of its control.  Keep the chip visually aligned with
                // the rendered title text rather than the two control bounds.
                int opticalTextOffset = Math.Max(
                    1,
                    (int)Math.Round(4F * title.DeviceDpi / 96F));
                int centeredTop = title.Top + (title.Height - usageNoticeButton.Height) / 2;
                int visuallyAlignedTop = centeredTop + opticalTextOffset;
                usageNoticeButton.Location = new Point(
                    title.Left + currentTitleTextSize.Width + titleToButtonOffset,
                    Math.Max(0, Math.Min(title.Bottom - usageNoticeButton.Height, visuallyAlignedTop)));
            };
            title.SizeChanged += positionUsageNoticeButton;
            usageNoticeButton.SizeChanged += positionUsageNoticeButton;
            usageNoticeButton.Click += delegate
            {
                ShowUsageNoticeDialog();
            };
            _toolTip.SetToolTip(usageNoticeButton, AppText.Get("Main.Header.UsageTooltip"));
            panel.Controls.Add(usageNoticeButton);
            positionUsageNoticeButton(null, EventArgs.Empty);

            var subtitle = new Label
            {
                AutoSize = true,
                Location = new Point(4, 38),
                Text = AppText.Get("Main.Header.SubtitleFull"),
                Font = new Font("Malgun Gothic", 9F),
                ForeColor = TextMuted
            };
            panel.Controls.Add(subtitle);

            var rightHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Right,
                BackColor = Background,
                ColumnCount = 1,
                RowCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0),
                Padding = new Padding(0, 1, 4, 1)
            };
            rightHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rightHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            rightHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            var attributionRow = CreateRightAlignedHeaderRow();
            var copyright = CreateHeaderTextLabel(
                "© 2026 Spirit-Schema",
                new Font("Segoe UI", 8.5F),
                TextMuted);
            attributionRow.Controls.Add(copyright);
            rightHeader.Controls.Add(attributionRow, 0, 0);

            var updateRow = CreateRightAlignedHeaderRow();
            var version = CreateHeaderTextLabel(
                "v" + GetApplicationDisplayVersion(),
                new Font("Segoe UI", 9F, FontStyle.Bold),
                TextMuted);
            version.AccessibleName = AppText.Get("Main.A11y.ProgramVersion");
            version.AccessibleDescription = AppText.Format("Main.A11y.CurrentVersion", version.Text);
            _toolTip.SetToolTip(version, version.AccessibleDescription);
            var versionSeparator = CreateHeaderTextLabel(
                "·",
                new Font("Segoe UI", 9F),
                TextMuted);
            _manualUpdateButton = CreateHeaderLinkButton(
                AppText.Get("Main.Update.Button"),
                AppText.Get("Main.Update.Tooltip"));
            _manualUpdateButton.AccessibleName = AppText.Get("Main.Update.Button");
            _manualUpdateButton.AccessibleDescription = AppText.Get("Main.Update.AccessibleDescription");
            _manualUpdateButton.Click += async delegate { await CheckForUpdatesManuallyAsync(); };
            var licenseSeparator = CreateHeaderTextLabel(
                "·",
                new Font("Segoe UI", 9F),
                TextMuted);
            Button licenseButton = CreateHeaderLinkButton(
                AppText.Get("Main.License.Button"),
                AppText.Get("Main.License.Tooltip"));
            licenseButton.AccessibleName = AppText.Get("Main.License.Button");
            licenseButton.AccessibleDescription = AppText.Get("Main.License.AccessibleDescription");
            licenseButton.Click += delegate { ShowLicenseDialog(); };
            var settingsSeparator = CreateHeaderTextLabel(
                "·",
                new Font("Segoe UI", 9F),
                TextMuted);
            _settingsButton = CreateHeaderLinkButton(
                AppText.Get("Settings.Button"),
                AppText.Get("Settings.Tooltip"));
            _settingsButton.AccessibleName = AppText.Get("Settings.Button");
            _settingsButton.AccessibleDescription = AppText.Get("Settings.Tooltip");
            _settingsButton.Click += delegate { ShowSettingsDialog(); };
            // The row flows right-to-left. Add controls in reverse so the visible order is:
            // version · update · license · settings.
            updateRow.Controls.Add(_settingsButton);
            updateRow.Controls.Add(settingsSeparator);
            updateRow.Controls.Add(licenseButton);
            updateRow.Controls.Add(licenseSeparator);
            updateRow.Controls.Add(_manualUpdateButton);
            updateRow.Controls.Add(versionSeparator);
            updateRow.Controls.Add(version);
            rightHeader.Controls.Add(updateRow, 0, 1);

            panel.Controls.Add(rightHeader);
            return panel;
        }

        private static FlowLayoutPanel CreateRightAlignedHeaderRow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Background,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private static Label CreateHeaderTextLabel(string text, Font font, Color color)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                Font = font,
                ForeColor = color,
                BackColor = Background,
                Margin = new Padding(2, 4, 2, 0),
                Padding = new Padding(0)
            };
        }

        internal static Button CreateUsageGuideButton()
        {
            var usageNoticeButton = new Button
            {
                AutoSize = false,
                Text = AppText.Get("Main.Button.UsageGuide"),
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                ForeColor = Accent,
                BackColor = Background,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = true,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            Size usageNoticeTextSize = TextRenderer.MeasureText(
                usageNoticeButton.Text,
                usageNoticeButton.Font,
                Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            usageNoticeButton.Size = new Size(usageNoticeTextSize.Width + 20, 28);
            usageNoticeButton.FlatAppearance.BorderSize = 0;
            usageNoticeButton.UseVisualStyleBackColor = false;
            bool usageNoticeHovered = false;
            bool usageNoticePressed = false;
            usageNoticeButton.MouseEnter += delegate
            {
                usageNoticeHovered = true;
                usageNoticeButton.Invalidate();
            };
            usageNoticeButton.MouseLeave += delegate
            {
                usageNoticeHovered = false;
                usageNoticePressed = false;
                usageNoticeButton.Invalidate();
            };
            usageNoticeButton.MouseDown += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Left) return;
                usageNoticePressed = true;
                usageNoticeButton.Invalidate();
            };
            usageNoticeButton.MouseUp += delegate
            {
                usageNoticePressed = false;
                usageNoticeButton.Invalidate();
            };
            usageNoticeButton.Paint += delegate(object sender, PaintEventArgs args)
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                args.Graphics.Clear(Background);
                int scaleOne = Math.Max(1, (int)Math.Round(usageNoticeButton.DeviceDpi / 96F));
                int pressedOffset = usageNoticePressed ? scaleOne : 0;
                Rectangle chipBounds = new Rectangle(
                    scaleOne,
                    scaleOne + pressedOffset,
                    Math.Max(1, usageNoticeButton.ClientSize.Width - (scaleOne * 2) - 1),
                    Math.Max(1, usageNoticeButton.ClientSize.Height - (scaleOne * 2) - 1));
                using (GraphicsPath chip = CreateRoundedRectanglePath(
                    chipBounds,
                    Math.Max(6, (int)Math.Round(6F * usageNoticeButton.DeviceDpi / 96F))))
                using (var fill = new SolidBrush(
                    usageNoticePressed ? Surface : usageNoticeHovered ? SurfaceAlt : Background))
                using (var outline = new Pen(
                    usageNoticeHovered ? AccentHover : Color.FromArgb(160, Accent),
                    scaleOne))
                {
                    args.Graphics.FillPath(fill, chip);
                    args.Graphics.DrawPath(outline, chip);
                }
                Rectangle textBounds = chipBounds;
                textBounds.Offset(0, pressedOffset);
                TextRenderer.DrawText(args.Graphics, usageNoticeButton.Text, usageNoticeButton.Font,
                    textBounds, usageNoticeHovered ? AccentHover : Accent,
                    TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter
                    | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            };
            return usageNoticeButton;
        }

        private Button CreateHeaderLinkButton(string text, string toolTip)
        {
            var buttonFont = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
            var button = new HeaderLinkButton
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Text = text,
                Font = buttonFont,
                ForeColor = Accent,
                BackColor = Background,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TabStop = true,
                UseVisualStyleBackColor = false,
                // The custom-painted glyph box reserves logical bottom space at
                // every DPI, independently of the native flat-button text inset.
                Margin = new Padding(0),
                Padding = new Padding(0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = SurfaceAlt;
            button.FlatAppearance.MouseDownBackColor = Surface;
            button.MouseEnter += delegate { button.ForeColor = AccentHover; };
            button.MouseLeave += delegate { button.ForeColor = button.Focused ? AccentHover : Accent; };
            button.GotFocus += delegate { button.ForeColor = AccentHover; };
            button.LostFocus += delegate { button.ForeColor = Accent; };
            _toolTip.SetToolTip(button, toolTip);
            return button;
        }

        private void ShowUsageNoticeDialog()
        {
            using (var dialog = new UsageNoticeForm())
                dialog.ShowDialog(this);
        }

        private void ShowLicenseDialog()
        {
            using (var dialog = new LicenseForm())
                dialog.ShowDialog(this);
        }

        private void ShowSettingsDialog()
        {
            if (_demoMode)
            {
                ShowActionNotice(AppText.Get("Main.Preview.NoSettings"));
                return;
            }
            var store = new AppPreferencesStore();
            AppPreferences previous = store.Load();
            using (var dialog = new ApplicationSettingsForm(
                previous,
                delegate(AppPreferences selected)
                {
                    return TryApplyApplicationSettings(store, previous, selected);
                }))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (dialog.RestartRequested)
                {
                    Application.Restart();
                    Close();
                }
            }
        }

        private string TryApplyApplicationSettings(
            AppPreferencesStore store,
            AppPreferences previous,
            AppPreferences selected)
        {
            if (store == null || previous == null || selected == null)
                return AppText.Get("Settings.SaveFailedUnknown");
            try
            {
                store.Save(selected);
                return string.Empty;
            }
            catch (Exception ex)
            {
                return AppText.Format("Settings.SaveFailed", ex.Message);
            }
        }

        private async Task CheckForUpdatesManuallyAsync()
        {
            if (_demoMode || IsDisposed || Disposing) return;
            if (_updateCheckCancellation != null)
            {
                SetStatus(AppText.Get("Main.Update.AlreadyRunning"), Warning);
                return;
            }

            var cancellation = new CancellationTokenSource();
            _updateCheckCancellation = cancellation;
            if (_manualUpdateButton != null)
            {
                _manualUpdateButton.Text = AppText.Get("Main.Update.ButtonChecking");
                _manualUpdateButton.Enabled = false;
            }
            SetStatus(AppText.Get("Main.Update.Checking"), Accent);
            try
            {
                GitHubUpdateService service = GitHubUpdateService.CreateProduction(
                    GetApplicationSemanticVersion());
                ManualUpdateCheckResult result = await service.CheckForUpdateManuallyAsync(
                    cancellation.Token);
                if (IsDisposed || Disposing || cancellation.IsCancellationRequested) return;

                switch (result.Status)
                {
                    case ManualUpdateCheckStatus.UpdateAvailable:
                        SetStatus(AppText.Format("Main.Update.Available", result.Update.VersionText), Accent);
                        await service.ShowUpdatePromptAsync(this, result.Update, cancellation.Token);
                        break;
                    case ManualUpdateCheckStatus.UpToDate:
                        SetStatus(AppText.Format("Main.Update.UpToDate", GetApplicationSemanticVersion()), Success);
                        break;
                    case ManualUpdateCheckStatus.AlreadyRunning:
                        SetStatus(AppText.Get("Main.Update.AlreadyRunning"), Warning);
                        break;
                    default:
                        SetStatus(AppText.Get("Main.Update.Failed"), Warning);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing)
                    SetStatus(AppText.Get("Main.Update.Cancelled"), Warning);
            }
            catch
            {
                if (!IsDisposed && !Disposing)
                    SetStatus(AppText.Get("Main.Update.Failed"), Warning);
            }
            finally
            {
                if (ReferenceEquals(_updateCheckCancellation, cancellation))
                    _updateCheckCancellation = null;
                cancellation.Dispose();
                if (_manualUpdateButton != null && !_manualUpdateButton.IsDisposed)
                {
                    _manualUpdateButton.Text = AppText.Get("Main.Update.Button");
                    _manualUpdateButton.Enabled = true;
                }
            }
        }

        private void ShowUsageNoticeOnce()
        {
            if (_demoMode) return;
            string markerPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard",
                "usage-guide.shown");
            try
            {
                if (File.Exists(markerPath)) return;
            }
            catch
            {
                // If the marker cannot be checked, showing the local help remains safe.
            }

            ShowUsageNoticeDialog();
            try
            {
                string folder = Path.GetDirectoryName(markerPath);
                if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(markerPath, string.Empty);
            }
            catch
            {
                // A read-only profile must not prevent the app from continuing normally.
            }
        }

        private Control BuildPathBar()
        {
            var card = CreateCardPanel();
            card.Padding = new Padding(14, 8, 14, 8);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 4,
                RowCount = 3
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsEnglishUi ? 30F : 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsEnglishUi ? 30F : 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            card.Controls.Add(layout);

            layout.Controls.Add(CreatePathLabel(AppText.Get("Main.Label.EftLogPath")), 0, 0);
            _eftPathTextBox = CreatePathTextBox();
            layout.Controls.Add(_eftPathTextBox, 1, 0);
            _eftBrowseButton = CreateButton(AppText.Get("Main.Path.Browse"), false);
            AlignPathButtonToTextBox(_eftBrowseButton, _eftPathTextBox, 8);
            _eftBrowseButton.Click += delegate { BrowseForGame(TarkovGame.Eft); };
            layout.Controls.Add(_eftBrowseButton, 2, 0);

            layout.Controls.Add(CreatePathLabel(AppText.Get("Main.Label.ArenaLogPath")), 0, 1);
            _arenaPathTextBox = CreatePathTextBox();
            layout.Controls.Add(_arenaPathTextBox, 1, 1);
            _arenaBrowseButton = CreateButton(AppText.Get("Main.Path.Browse"), false);
            AlignPathButtonToTextBox(_arenaBrowseButton, _arenaPathTextBox, 8);
            _arenaBrowseButton.Click += delegate { BrowseForGame(TarkovGame.Arena); };
            layout.Controls.Add(_arenaBrowseButton, 2, 1);

            _rediscoverPathButton = CreateButton(AppText.Get("Main.Path.AutoDetect"), false);
            AlignPathButtonToTextBox(_rediscoverPathButton, _eftPathTextBox, 0);
            _rediscoverPathButton.Click += async delegate { await RediscoverLogPathsAsync(); };
            layout.Controls.Add(_rediscoverPathButton, 3, 0);

            _applyPathButton = CreateButton(AppText.Get("Common.Button.Apply"), false);
            AlignPathButtonToTextBox(_applyPathButton, _arenaPathTextBox, 0);
            _applyPathButton.Click += async delegate { await ApplyLogPathsAsync(); };
            layout.Controls.Add(_applyPathButton, 3, 1);

            // A single-line TextBox keeps its native font-dependent height even
            // in a taller table row. Keep the neighboring actions on that same
            // top/bottom line at every DPI instead of filling the whole row.
            layout.Layout += delegate
            {
                AlignPathButtonToTextBox(_eftBrowseButton, _eftPathTextBox, 8);
                AlignPathButtonToTextBox(_rediscoverPathButton, _eftPathTextBox, 0);
                AlignPathButtonToTextBox(_arenaBrowseButton, _arenaPathTextBox, 8);
                AlignPathButtonToTextBox(_applyPathButton, _arenaPathTextBox, 0);
            };

            string pathHelp = AppText.Get("Main.Path.Help");
            _toolTip.SetToolTip(_eftPathTextBox, pathHelp);
            _toolTip.SetToolTip(_arenaPathTextBox, pathHelp);
            _toolTip.SetToolTip(_rediscoverPathButton, AppText.Get("Main.Path.AutoDetectTooltip"));
            _toolTip.SetToolTip(_applyPathButton, AppText.Get("Main.Path.ApplyTooltip"));

            _launcherSelectionLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("Main.LauncherSelection.Loading"),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 8.5F),
                AutoEllipsis = true,
                Padding = new Padding(0, 2, 0, 0)
            };
            layout.Controls.Add(_launcherSelectionLabel, 1, 2);
            layout.SetColumnSpan(_launcherSelectionLabel, 3);
            _toolTip.SetToolTip(
                _launcherSelectionLabel,
                AppText.Get("Main.LauncherSelection.Help"));
            return card;
        }

        private static Label CreatePathLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                AutoSize = false,
                Size = new Size(87, 23),
                Margin = new Padding(3, 4, 0, 0),
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold)
            };
        }

        private static TextBox CreatePathTextBox()
        {
            return new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 9F),
                Margin = new Padding(0, 4, 10, 3)
            };
        }

        private static void AlignPathButtonToTextBox(
            Button button,
            TextBox textBox,
            int rightMargin)
        {
            if (button == null || textBox == null) return;
            button.Dock = DockStyle.None;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            // Path actions are intentionally aligned against a native single-line
            // TextBox instead of filling the table row. Keep the caption centered
            // inside that shorter button at every layout/DPI recalculation.
            button.TextAlign = ContentAlignment.MiddleCenter;
            // The default GDI flat-button path places Malgun Gothic's visual weight
            // low at 120 DPI. Compatible rendering centers the complete glyph for
            // these four short captions without the clipping or over-correction
            // caused by asymmetric padding.
            button.UseCompatibleTextRendering = true;
            Padding targetPadding = new Padding(0);
            if (button.Padding != targetPadding)
                button.Padding = targetPadding;
            int scaledRightMargin = rightMargin <= 0
                ? 0
                : Math.Max(1, (int)Math.Round(
                    textBox.Margin.Right * rightMargin / 10F));
            Padding targetMargin = new Padding(
                0,
                textBox.Margin.Top,
                scaledRightMargin,
                textBox.Margin.Bottom);
            if (button.Margin != targetMargin)
                button.Margin = targetMargin;
            if (button.Height != textBox.Height)
                button.Height = textBox.Height;
        }

        private Control BuildCurrentServerCard()
        {
            var card = CreateCardPanel();
            card.Padding = new Padding(18, 12, 18, 12);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 2,
                RowCount = 1
            };
            // Keep the locally processed privacy notice readable at the minimum
            // supported window width.  The action column needs enough room for
            // each explicit notice line to remain a single line at 96/120 DPI.
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36F));
            card.Controls.Add(layout);

            var details = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
            layout.Controls.Add(details, 0, 0);
            details.Controls.Add(new Label
            {
                AutoSize = true,
                Text = AppText.Get("Main.Current.Title"),
                ForeColor = Accent,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                Location = new Point(0, 0)
            });

            _ipLabel = new Label
            {
                AutoSize = true,
                Text = AppText.Get("Main.Current.Finding"),
                ForeColor = TextPrimary,
                Font = new Font("Segoe UI", 23F, FontStyle.Bold),
                Location = new Point(IsEnglishUi ? 0 : -2, 22)
            };
            details.Controls.Add(_ipLabel);

            int detailValueLeft = IsEnglishUi ? 150 : 126;
            int detailRowStep = IsEnglishUi ? 20 : 24;
            _mapValueLabel = AddDetailLine(details, AppText.Get("Main.Current.GameMapType"), "-", 72, detailValueLeft);
            _mapValueLabel.AccessibleName = AppText.Get("Main.Current.MapTypeAccessibleName");
            _mapValueLabel.AccessibleDescription = "-";
            _timeValueLabel = AddDetailLine(details, AppText.Get("Main.Current.ConnectionTime"), "-", 72 + detailRowStep, detailValueLeft);
            _locationValueLabel = AddDetailLine(
                details,
                AppText.Get("Main.Current.DataCenterRegion"),
                AppText.Get("Main.Current.BeforeScan"),
                72 + detailRowStep * 2,
                detailValueLeft);
            _pingValueLabel = AddDetailLine(details, AppText.Get("Main.Label.CurrentPing"), AppText.Get("Main.Current.BeforeScan"), 72 + detailRowStep * 3, detailValueLeft);
            _actualRttValueLabel = AddDetailLine(details, AppText.Get("Main.Label.InRaidRtt"), "-", 72 + detailRowStep * 4, detailValueLeft);
            _packetLossValueLabel = AddDetailLine(details, AppText.Get("Main.Current.InRaidPacketLoss"), "-", 72 + detailRowStep * 5, detailValueLeft);
            _actualRttValueLabel.AccessibleName = AppText.Get("Main.Current.RttAccessibleName");
            _actualRttValueLabel.AccessibleDescription = "-";
            _packetLossValueLabel.AccessibleName = AppText.Get("Main.Current.PacketLossAccessibleName");
            _packetLossValueLabel.AccessibleDescription = "-";

            _advancedDetailsLayout = CreateDetailInfoLayout(out _detailInfoValueLabels);
            _advancedDetailsLayout.Location = new Point(466, 40);
            _advancedDetailsLayout.Size = new Size(500, 154);
            _advancedDetailsLayout.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

            details.Resize += delegate
            {
                int dpi = details.DeviceDpi <= 0 ? 96 : details.DeviceDpi;
                int leftShift = ScaleLogical(12, dpi);
                int preferredLeft = ScaleLogical(478, dpi) - leftShift;
                bool constrainedDetails = details.ClientSize.Width < ScaleLogical(760, dpi);
                int minimumLeft = ScaleLogical(constrainedDetails ? 370 : 405, dpi) - leftShift;
                int rightInset = ScaleLogical(8, dpi);
                int bottomInset = ScaleLogical(4, dpi);
                int centeredLeft = Math.Max(0, details.ClientSize.Width / 2 - leftShift);
                _advancedDetailsLayout.Left = Math.Min(preferredLeft, Math.Max(minimumLeft, centeredLeft));
                _advancedDetailsLayout.Width = Math.Max(
                    ScaleLogical(180, dpi),
                    details.ClientSize.Width - _advancedDetailsLayout.Left - rightInset);
                _advancedDetailsLayout.Height = ScaleLogical(154, dpi);
                _advancedDetailsLayout.Top = Math.Max(0, details.ClientSize.Height - _advancedDetailsLayout.Height - bottomInset);
                _advancedDetailsLayout.ColumnStyles[0].Width = ScaleLogical(
                    constrainedDetails ? 112 : 138,
                    dpi);
                UpdateCurrentServerResponsiveLayout(details);
            };
            details.Controls.Add(_advancedDetailsLayout);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(14, 2, 0, 0)
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            layout.Controls.Add(actions, 1, 0);

            var actionButtons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            actionButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            actions.Controls.Add(actionButtons, 0, 1);

            _queryButton = CreateButton(AppText.Get("Common.Button.Scan"), true);
            _queryButton.Margin = new Padding(0, 3, 4, 5);
            _queryButton.Click += async delegate
            {
                if (_isMeasuring)
                {
                    CancelVisibleServerQuery();
                    return;
                }
                await QueryVisibleServersAsync();
            };
            _toolTip.SetToolTip(
                _queryButton,
                AppText.Get("Main.Scan.Tooltip"));
            actionButtons.Controls.Add(_queryButton, 0, 0);

            _copyIpButton = CreateButton(AppText.Get("Common.Button.CopyIp"), false);
            _copyIpButton.Margin = new Padding(4, 3, 0, 5);
            _copyIpButton.Click += delegate { CopySelectedIp(); };
            actionButtons.Controls.Add(_copyIpButton, 1, 0);

            string privacyNoticeText = AppText.Get(IsEnglishUi
                ? "Main.Privacy.CompactNotice" : "Main.Privacy.FullNotice");
            string[] privacyNoticeLines = privacyNoticeText.Split(
                new[] { "\r\n" },
                StringSplitOptions.None);
            var privacyNotice = new Label
            {
                Name = "PrivacyNotice",
                Dock = IsEnglishUi ? DockStyle.None : DockStyle.Fill,
                Text = privacyNoticeText,
                ForeColor = TextMuted,
                Font = new Font(IsEnglishUi ? "Segoe UI" : "Malgun Gothic", IsEnglishUi ? 8.5F : 7.5F),
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(1, 4, 0, 3),
                AutoEllipsis = false,
                AccessibleDescription = AppText.Get("Main.Privacy.FullNotice")
            };
            if (IsEnglishUi)
            {
                // A TableLayoutPanel can retain a Label's design-time 100px width
                // after an English font swap when Dock=Fill. Four-way anchoring
                // lets the full six-line notice use its cell without altering the
                // established Korean layout.
                privacyNotice.Anchor = AnchorStyles.Top | AnchorStyles.Bottom
                    | AnchorStyles.Left | AnchorStyles.Right;
            }
            actions.Controls.Add(privacyNotice, 0, 2);
            _toolTip.SetToolTip(
                privacyNotice,
                AppText.Get("Main.Privacy.FullNotice") + "\r\n\r\n"
                + GitHubUpdateService.RepositoryUrl + "\r\n"
                + DbIpLiteGeoService.AttributionUrl + "\r\n"
                + DbIpLiteGeoService.LicenseUrl);

            bool fittingPrivacyNotice = false;
            EventHandler fitPrivacyNotice = delegate
            {
                if (fittingPrivacyNotice || privacyNotice.ClientSize.Width <= 1
                    || actions.ClientSize.Height <= 1)
                    return;
                fittingPrivacyNotice = true;
                try
                {
                    int availableWidth = Math.Max(
                        1,
                        privacyNotice.ClientSize.Width - privacyNotice.Padding.Horizontal - 2);
                    int preserveSpacer = Math.Max(
                        18,
                        (int)Math.Round(18F * actions.DeviceDpi / 96F));
                    int maximumNoticeHeight = Math.Max(
                        72,
                        actions.ClientSize.Height - 50 - preserveSpacer);
                    if (IsEnglishUi)
                    {
                        // Keep a readable font. Wrap the compact notice and let its
                        // row consume the unused spacer above the action buttons.
                        int measuredHeight = TextRenderer.MeasureText(privacyNotice.Text,
                            privacyNotice.Font, new Size(availableWidth, int.MaxValue),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height
                            + privacyNotice.Padding.Vertical + ScaleLogical(8, actions.DeviceDpi);
                        float height = Math.Max(72, Math.Min(maximumNoticeHeight, measuredHeight));
                        if (Math.Abs(actions.RowStyles[2].Height - height) > 0.5F)
                            actions.RowStyles[2].Height = height;
                        return;
                    }
                    float selectedSize = 7.5F;
                    int requiredHeight = maximumNoticeHeight;
                    for (float candidate = 7.5F; candidate >= 5.5F; candidate -= 0.25F)
                    {
                        using (var candidateFont = new Font("Malgun Gothic", candidate))
                        {
                            int widestLine = 0;
                            foreach (string line in privacyNoticeLines)
                            {
                                Size measuredLine = TextRenderer.MeasureText(
                                    line,
                                    candidateFont,
                                    new Size(int.MaxValue, int.MaxValue),
                                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
                                    | TextFormatFlags.NoPrefix);
                                widestLine = Math.Max(widestLine, measuredLine.Width);
                            }
                            selectedSize = candidate;
                            requiredHeight = (candidateFont.Height * privacyNoticeLines.Length)
                                + privacyNotice.Padding.Vertical + 2;
                            if (widestLine <= availableWidth
                                && requiredHeight <= maximumNoticeHeight)
                                break;
                        }
                    }

                    if (Math.Abs(privacyNotice.Font.Size - selectedSize) > 0.01F)
                    {
                        Font previous = privacyNotice.Font;
                        privacyNotice.Font = new Font("Malgun Gothic", selectedSize);
                        previous.Dispose();
                    }
                    float desiredHeight = Math.Min(
                        maximumNoticeHeight,
                        Math.Max(72, requiredHeight));
                    if (Math.Abs(actions.RowStyles[2].Height - desiredHeight) > 0.5F)
                        actions.RowStyles[2].Height = desiredHeight;
                }
                finally
                {
                    fittingPrivacyNotice = false;
                }
            };
            actions.Resize += fitPrivacyNotice;
            actions.Layout += delegate { fitPrivacyNotice(null, EventArgs.Empty); };
            privacyNotice.Resize += fitPrivacyNotice;

            UpdateActionButtons();
            return card;
        }

        private Control BuildHistoryCard()
        {
            var outer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Background,
                // Match the card panels' zero horizontal margin. The default
                // Panel margin narrowed this section by three pixels per side.
                Margin = new Padding(0, 3, 0, 3),
                Padding = new Padding(0, 12, 0, 0)
            };
            var card = CreateCardPanel();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(14, 9, 14, 9);
            outer.Controls.Add(card);

            _historyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 1,
                RowCount = 3
            };
            _historyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            _historyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _historyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            card.Controls.Add(_historyLayout);
            TableLayoutPanel layout = _historyLayout;

            var titleBar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                // Keep the existing top/side inset, but do not let the default bottom
                // margin reduce the 34 px header row and clip flat-button borders.
                Margin = new Padding(3, 3, 3, 0)
            };
            var titleLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            titleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            titleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));
            titleBar.Controls.Add(titleLayout);

            var periodAndTools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Surface,
                Margin = new Padding(0),
                Padding = new Padding(0, 1, 0, 1)
            };
            _recentPeriodButton = CreatePeriodButton(AppText.Get("Main.Period.Latest100"), 96);
            // The toolbar host and the grid share the same three-pixel card
            // inset. Only the first button's default four-pixel FlowLayout
            // margin shifted the whole left group past the first grid column.
            _recentPeriodButton.Margin = new Padding(0);
            _todayPeriodButton = CreatePeriodButton(
                AppText.Get("Main.Filter.Today"),
                IsEnglishUi ? 60 : 52);
            _sevenDaysPeriodButton = CreatePeriodButton(AppText.Get("Main.Period.SevenDays"), 52);
            _thirtyDaysPeriodButton = CreatePeriodButton(AppText.Get("Main.Period.ThirtyDays"), 52);
            _customPeriodButton = CreatePeriodButton(AppText.Get("Main.Period.Custom"), 76);
            _recentPeriodButton.Click += async delegate { await SelectSessionPeriodAsync(SessionPeriodPreset.Recent100); };
            _todayPeriodButton.Click += async delegate { await SelectSessionPeriodAsync(SessionPeriodPreset.Today); };
            _sevenDaysPeriodButton.Click += async delegate { await SelectSessionPeriodAsync(SessionPeriodPreset.Last7Days); };
            _thirtyDaysPeriodButton.Click += async delegate { await SelectSessionPeriodAsync(SessionPeriodPreset.Last30Days); };
            _customPeriodButton.Click += async delegate { await SelectCustomSessionPeriodAsync(); };
            _toolTip.SetToolTip(_recentPeriodButton, AppText.Get("Main.Period.Latest100Tooltip"));
            _toolTip.SetToolTip(_todayPeriodButton, AppText.Get("Main.Period.TodayTooltip"));
            _toolTip.SetToolTip(_sevenDaysPeriodButton, AppText.Get("Main.Period.SevenDaysTooltip"));
            _toolTip.SetToolTip(_thirtyDaysPeriodButton, AppText.Get("Main.Period.ThirtyDaysTooltip"));
            _toolTip.SetToolTip(_customPeriodButton, AppText.Get("Main.Period.CustomTooltip"));
            periodAndTools.Controls.Add(_recentPeriodButton);
            periodAndTools.Controls.Add(_todayPeriodButton);
            periodAndTools.Controls.Add(_sevenDaysPeriodButton);
            periodAndTools.Controls.Add(_thirtyDaysPeriodButton);
            periodAndTools.Controls.Add(_customPeriodButton);

            periodAndTools.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(1, 18),
                Margin = new Padding(16, 5, 12, 5)
            });

            _notesArchiveButton = CreateToolButton(
                AppText.Get("Notes.Navigation.Label"),
                ToolIconKind.Note,
                Color.FromArgb(190, 119, 42),
                112);
            _notesArchiveButton.Click += delegate
            {
                if (_demoMode)
                {
                    ShowActionNotice(AppText.Get("Main.Preview.NoNotes"));
                    return;
                }
                try
                {
                    if (RaidNoteUi.ShowArchive(this)) RefreshNoteCells();
                }
                catch (Exception ex)
                {
                    SetStatus(AppText.Format("Notes.OpenFailed", ex.Message), Danger);
                }
            };
            _toolTip.SetToolTip(
                _notesArchiveButton,
                AppText.Get("Notes.Navigation.Tooltip"));
            periodAndTools.Controls.Add(_notesArchiveButton);

            _blockedServersButton = CreateToolButton(
                AppText.Get("Main.BlockedServers.Button"),
                ToolIconKind.Shield,
                Color.FromArgb(190, 88, 94),
                116);
            _blockedServersButton.Click += async delegate { await ShowBlockedServersAsync(); };
            _toolTip.SetToolTip(_blockedServersButton, AppText.Get("Main.BlockedServers.Tooltip"));
            periodAndTools.Controls.Add(_blockedServersButton);
            titleLayout.Controls.Add(periodAndTools, 0, 0);
            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Surface,
                Margin = new Padding(0),
                Padding = new Padding(0, 1, 0, 1)
            };
            _allFilterButton = CreateFilterButton(AppText.Get("Main.Filter.All"));
            _eftFilterButton = CreateFilterButton("EFT");
            _arenaFilterButton = CreateFilterButton("Arena");
            _regionFilterButton = CreateFilterButton(AppText.Get("Main.Filter.RegionAll"));
            _regionFilterButton.Size = new Size(IsEnglishUi ? 136 : 116, 28);
            _regionFilterButton.TabStop = true;
            _regionFilterButton.AccessibleName = AppText.Get("Main.Filter.RegionAccessibleName");
            _regionFilterButton.AccessibleDescription = AppText.Get("Main.Filter.RegionAccessibleDescription");
            _allFilterButton.Click += delegate { SetGameFilter(null); };
            _eftFilterButton.Click += delegate { SetGameFilter(TarkovGame.Eft); };
            _arenaFilterButton.Click += delegate { SetGameFilter(TarkovGame.Arena); };
            _regionFilterButton.Click += delegate { ShowRegionFilterMenu(); };
            filters.Controls.Add(_allFilterButton);
            filters.Controls.Add(_eftFilterButton);
            filters.Controls.Add(_arenaFilterButton);
            filters.Controls.Add(_regionFilterButton);
            titleLayout.Controls.Add(filters, 1, 0);
            layout.Controls.Add(titleBar, 0, 0);

            bool updatingToolbarLayout = false;
            EventHandler updateToolbarLayout = delegate
            {
                if (updatingToolbarLayout) return;
                updatingToolbarLayout = true;
                try
                {
                    int dpi = titleBar.DeviceDpi <= 0 ? 96 : titleBar.DeviceDpi;
                    bool compact = Width > 0
                        && Width < ScaleLogical(1180, dpi);
                    if (compact)
                    {
                        titleLayout.SetCellPosition(periodAndTools, new TableLayoutPanelCellPosition(0, 0));
                        titleLayout.SetColumnSpan(periodAndTools, 2);
                        titleLayout.SetCellPosition(filters, new TableLayoutPanelCellPosition(0, 1));
                        titleLayout.SetColumnSpan(filters, 2);
                        titleLayout.RowStyles[0].SizeType = SizeType.Percent;
                        titleLayout.RowStyles[0].Height = 50F;
                        titleLayout.RowStyles[1].SizeType = SizeType.Percent;
                        titleLayout.RowStyles[1].Height = 50F;
                        float compactHeight = ScaleLogical(68, dpi);
                        if (Math.Abs(layout.RowStyles[0].Height - compactHeight) > 0.5F)
                            layout.RowStyles[0].Height = compactHeight;
                    }
                    else
                    {
                        titleLayout.SetCellPosition(periodAndTools, new TableLayoutPanelCellPosition(0, 0));
                        titleLayout.SetColumnSpan(periodAndTools, 1);
                        titleLayout.SetCellPosition(filters, new TableLayoutPanelCellPosition(1, 0));
                        titleLayout.SetColumnSpan(filters, 1);
                        titleLayout.RowStyles[0].SizeType = SizeType.Percent;
                        titleLayout.RowStyles[0].Height = 100F;
                        titleLayout.RowStyles[1].SizeType = SizeType.Absolute;
                        titleLayout.RowStyles[1].Height = 0F;
                        float regularHeight = ScaleLogical(34, dpi);
                        if (Math.Abs(layout.RowStyles[0].Height - regularHeight) > 0.5F)
                            layout.RowStyles[0].Height = regularHeight;
                    }
                    // A width change can arrive while the parent TableLayoutPanel is
                    // already laying out its rows. Force the newly selected two-row
                    // height through the nested layouts in the same pass so the first
                    // compact render cannot retain the preceding 34px header height.
                    layout.PerformLayout();
                    titleBar.PerformLayout();
                    titleLayout.PerformLayout();
                }
                finally
                {
                    updatingToolbarLayout = false;
                }
            };
            titleBar.Resize += updateToolbarLayout;
            layout.Resize += updateToolbarLayout;
            outer.Resize += updateToolbarLayout;

            _historyGrid = new BufferedDataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 44,
                ScrollBars = ScrollBars.Both,
                RowTemplate = { Height = 38 }
            };
            _historyGrid.DefaultCellStyle.BackColor = Surface;
            _historyGrid.DefaultCellStyle.ForeColor = TextPrimary;
            _historyGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(56, 65, 76);
            _historyGrid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            _historyGrid.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            _historyGrid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            _historyGrid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            _historyGrid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            _historyGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold);
            _historyGrid.Columns.Add(CreateTextColumn("game", AppText.Get("Common.Label.Game"), 68));
            _historyGrid.Columns.Add(CreateTextColumn("time", AppText.Get("Main.Column.ConnectionTime"), 154));
            var reportColumn = CreateTextColumn("userReport", AppText.Get("Main.Column.PlayerReports"), 86);
            reportColumn.DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.BottomCenter,
                Font = new Font("Malgun Gothic", IsEnglishUi ? 8.5F : 7.5F, FontStyle.Bold),
                Padding = new Padding(1, 0, 1, 3)
            };
            _historyGrid.Columns.Add(reportColumn);
            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "note",
                HeaderText = AppText.Get("Main.Column.Note"),
                Width = 58,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    BackColor = Surface,
                    ForeColor = ReportOrange,
                    SelectionBackColor = Color.FromArgb(56, 65, 76),
                    SelectionForeColor = ReportOrange,
                    Padding = new Padding(0)
                }
            });
            _historyGrid.Columns["note"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            _historyGrid.Columns["note"].HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            _historyGrid.Columns["note"].HeaderCell.Style.Padding = new Padding(0);
            DataGridViewTextBoxColumn mapModeColumn = CreateTextColumn(
                "mapMode",
                AppText.Get("Main.Column.MapGameType"),
                220);
            mapModeColumn.CellTemplate = new RaidContextTextBoxCell();
            mapModeColumn.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _historyGrid.Columns.Add(mapModeColumn);
            _historyGrid.Columns.Add(CreateTextColumn("ip", AppText.Get("Main.Column.ServerIp"), 124));
            _historyGrid.Columns.Add(CreateTextColumn("location", AppText.Get("Main.Column.DataCenterRegion"), 172));
            _historyGrid.Columns.Add(CreateTextColumn("ping", AppText.Get("Main.Label.CurrentPing"), 92));
            DataGridViewTextBoxColumn actualRttColumn = CreateTwoLineTextColumn(
                "actualRtt",
                AppText.Get("Main.Column.InRaidRtt"),
                96);
            actualRttColumn.CellTemplate = new MetricTextBoxCell();
            _historyGrid.Columns.Add(actualRttColumn);
            DataGridViewTextBoxColumn packetLossColumn = CreateTwoLineTextColumn(
                "packetLoss",
                AppText.Get("Main.Column.InRaidPacketLoss"),
                IsEnglishUi ? 108 : 96);
            packetLossColumn.CellTemplate = new MetricTextBoxCell();
            _historyGrid.Columns.Add(packetLossColumn);
            DataGridViewButtonColumn blockActionColumn = CreateConnectionActionColumn("blockAction", AppText.Get("Common.Button.Block"));
            DataGridViewButtonColumn unblockActionColumn = CreateConnectionActionColumn("unblockAction", AppText.Get("Main.Column.Unblock"));
            // The action state remains on the main row. A small native DataGridView
            // mirrors these two ButtonCells at the fixed right edge because WinForms
            // only supports freezing columns on the left.
            blockActionColumn.Visible = false;
            unblockActionColumn.Visible = false;
            _historyGrid.Columns.Add(blockActionColumn);
            _historyGrid.Columns.Add(unblockActionColumn);
            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "result",
                HeaderText = IsEnglishUi
                    ? AppText.Get("Main.Column.ConnectionResult").Replace(" ", "\r\n")
                    : AppText.Get("Main.Column.ConnectionResult"),
                ToolTipText = AppText.Get("Main.Column.ConnectionResultHelp"),
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = IsEnglishUi ? 186 : 150,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Font = new Font("Malgun Gothic", IsEnglishUi ? 8.5F : 7.5F),
                    Padding = new Padding(2, 0, 2, 0)
                }
            });
            _historyGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "stickyActionSpacer",
                HeaderText = string.Empty,
                Width = GetStickyActionLogicalWidth(),
                MinimumWidth = 2,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Resizable = DataGridViewTriState.False,
                ReadOnly = true,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Surface,
                    ForeColor = Surface,
                    SelectionBackColor = Color.FromArgb(56, 65, 76),
                    SelectionForeColor = Color.FromArgb(56, 65, 76),
                    Padding = new Padding(0)
                }
            });
            foreach (string sortableColumn in new[]
            {
                "game", "time", "userReport", "note", "mapMode", "ip",
                "location", "ping", "actualRtt", "packetLoss", "result"
            })
            {
                DataGridViewColumn column = _historyGrid.Columns[sortableColumn];
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
                AppendHeaderSortToolTip(column);
            }
            _historyGrid.ColumnHeaderMouseClick += HistoryGridColumnHeaderMouseClick;
            _historyGrid.CellMouseClick += async delegate(object sender, DataGridViewCellMouseEventArgs args)
            {
                await HistoryGridCellMouseClickAsync(args);
            };
            _historyGrid.CellPainting += HistoryGridCellPainting;
            _historyGrid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs args)
            {
                if (args.RowIndex < 0 || args.CellStyle == null) return;
                args.CellStyle.SelectionForeColor = args.CellStyle.ForeColor;
            };
            _historyGrid.CellMouseMove += delegate(object sender, DataGridViewCellMouseEventArgs args)
            {
                // Header dividers and captured drags own the native resize cursor.
                // Overwriting it on each move makes the cursor/resize feedback flicker.
                if (args.RowIndex < 0 || args.ColumnIndex < 0 || _historyGrid.Capture) return;
                bool clickable = IsInteractiveCellPoint(args);
                Cursor cursor = clickable ? Cursors.Hand : Cursors.Default;
                if (_historyGrid.Cursor != cursor) _historyGrid.Cursor = cursor;
            };
            _historyGrid.CellMouseLeave += delegate
            {
                if (!_historyGrid.Capture && _historyGrid.Cursor == Cursors.Hand)
                    _historyGrid.Cursor = Cursors.Default;
            };
            _historyGrid.Scroll += HistoryGridScroll;
            _historyGrid.SelectionChanged += delegate { SyncStickySelectionFromHistory(); };
            _historyGrid.RowsAdded += delegate
            {
                QueueStickyActionGridBoundsUpdate();
            };
            _historyGrid.RowsRemoved += delegate
            {
                QueueStickyActionGridBoundsUpdate();
            };
            _historyGrid.Resize += delegate { QueueStickyActionGridBoundsUpdate(); };
            _historyGrid.Layout += delegate { QueueStickyActionGridBoundsUpdate(); };

            _stickyActionGrid = CreateStickyActionGrid();
            _stickyActionGrid.ColumnHeaderMouseClick += StickyActionGridColumnHeaderMouseClick;
            _stickyActionGrid.CellPainting += StickyActionGridCellPainting;
            _stickyActionGrid.CurrentCellChanged += delegate { SyncHistorySelectionFromStickyActions(); };
            _stickyActionGrid.SelectionChanged += delegate { SyncHistorySelectionFromStickyActions(); };
            _stickyActionGrid.CellContentClick += async delegate(object sender, DataGridViewCellEventArgs args)
            {
                await StickyActionGridCellContentClickAsync(args);
            };
            _stickyActionGrid.CellMouseMove += delegate(object sender, DataGridViewCellMouseEventArgs args)
            {
                _stickyActionGrid.Cursor = args.RowIndex >= 0 && args.ColumnIndex >= 0
                    ? Cursors.Hand
                    : Cursors.Default;
            };
            _stickyActionGrid.MouseLeave += delegate { _stickyActionGrid.Cursor = Cursors.Default; };
            _historyGrid.Controls.Add(_stickyActionGrid);
            _historyScrollCorner = new DataGridViewScrollCorner(
                _historyGrid,
                NativeScrollTrack);
            UpdateStickyActionGridBounds();
            layout.Controls.Add(_historyGrid, 0, 1);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = AppText.Get("Main.Status.Preparing"),
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 8.5F),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false,
                AccessibleName = AppText.Get("Main.Status.AccessibleName"),
                AccessibleDescription = AppText.Get("Main.Status.Preparing"),
                Margin = new Padding(0)
            };
            _toolTip.SetToolTip(_statusLabel, _statusLabel.Text);
            layout.Controls.Add(_statusLabel, 0, 2);
            layout.Resize += delegate { UpdateStatusAreaHeight(); };
            _statusLabel.FontChanged += delegate { UpdateStatusAreaHeight(); };
            UpdateStatusAreaHeight();
            UpdateFilterButtons();
            UpdatePeriodButtons();
            return outer;
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(string name, string header, int width)
        {
            return new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
        }

        private sealed class RaidContextTextBoxCell : DataGridViewTextBoxCell
        {
            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new RaidContextCellAccessibleObject(this);
            }

            public override object Clone()
            {
                return (RaidContextTextBoxCell)base.Clone();
            }

            private sealed class RaidContextCellAccessibleObject
                : DataGridViewCellAccessibleObject
            {
                private readonly RaidContextTextBoxCell _owner;

                internal RaidContextCellAccessibleObject(RaidContextTextBoxCell owner)
                    : base(owner)
                {
                    _owner = owner;
                }

                public override string Description
                {
                    get
                    {
                        string value = Convert.ToString(_owner.FormattedValue);
                        return string.IsNullOrWhiteSpace(value)
                            ? base.Description
                            : value;
                    }
                }
            }
        }

        private sealed class MetricTextBoxCell : DataGridViewTextBoxCell
        {
            protected override AccessibleObject CreateAccessibilityInstance()
            {
                return new MetricCellAccessibleObject(this);
            }

            public override object Clone()
            {
                return (MetricTextBoxCell)base.Clone();
            }

            private sealed class MetricCellAccessibleObject
                : DataGridViewCellAccessibleObject
            {
                private readonly MetricTextBoxCell _owner;

                internal MetricCellAccessibleObject(MetricTextBoxCell owner)
                    : base(owner)
                {
                    _owner = owner;
                }

                public override string Description
                {
                    get
                    {
                        return CreateMetricAccessibleDescription(
                            Convert.ToString(_owner.FormattedValue),
                            _owner.ToolTipText);
                    }
                }
            }
        }

        private static void AppendHeaderSortToolTip(DataGridViewColumn column)
        {
            string existing = column.HeaderCell.ToolTipText;
            column.HeaderCell.ToolTipText = string.IsNullOrEmpty(existing)
                ? HeaderSortToolTip
                : existing + "\r\n" + HeaderSortToolTip;
        }

        private static DataGridViewButtonColumn CreateConnectionActionColumn(string name, string text)
        {
            var column = new DataGridViewButtonColumn
            {
                Name = name,
                HeaderText = text,
                Text = text,
                UseColumnTextForButtonValue = true,
                Width = 58,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleCenter,
                    BackColor = Surface,
                    ForeColor = TextMuted,
                    SelectionBackColor = Color.FromArgb(56, 65, 76),
                    SelectionForeColor = TextMuted,
                    Font = new Font("Malgun Gothic", 8F, FontStyle.Bold),
                    Padding = new Padding(2, 3, 2, 3)
                }
            };
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.HeaderCell.Style.WrapMode = DataGridViewTriState.False;
            column.HeaderCell.Style.Padding = new Padding(0);
            return column;
        }

        private static DataGridViewTextBoxColumn CreateTwoLineTextColumn(
            string name,
            string header,
            int width)
        {
            var column = CreateTextColumn(name, header, width);
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            column.HeaderCell.Style.WrapMode = DataGridViewTriState.True;
            column.HeaderCell.Style.Padding = new Padding(0);
            return column;
        }

        private StickyActionGrid CreateStickyActionGrid()
        {
            var grid = new StickyActionGrid(this)
            {
                BackgroundColor = Surface,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeColumns = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = _historyGrid.ColumnHeadersHeight,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ScrollBars = ScrollBars.None,
                StandardTab = false,
                RowTemplate = { Height = _historyGrid.RowTemplate.Height }
            };
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = _historyGrid.DefaultCellStyle.SelectionBackColor;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.DefaultCellStyle.Font = _historyGrid.DefaultCellStyle.Font;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceAlt;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            grid.ColumnHeadersDefaultCellStyle.Font = IsEnglishUi
                ? new Font("Segoe UI", 8.5F, FontStyle.Bold)
                : _historyGrid.ColumnHeadersDefaultCellStyle.Font;
            grid.Columns.Add(CreateConnectionActionColumn("blockAction", AppText.Get("Common.Button.Block")));
            grid.Columns.Add(CreateConnectionActionColumn("unblockAction", AppText.Get("Main.Column.Unblock")));
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.Programmatic;
                AppendHeaderSortToolTip(column);
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                column.MinimumWidth = 2;
                column.Resizable = DataGridViewTriState.False;
            }
            return grid;
        }

        private bool IsInteractiveCellPoint(DataGridViewCellMouseEventArgs args)
        {
            if (_historyGrid == null || args == null || args.RowIndex < 0 || args.ColumnIndex < 0)
                return false;
            if (args.ColumnIndex == _historyGrid.Columns["note"].Index)
                return true;
            if (args.ColumnIndex == _historyGrid.Columns["userReport"].Index)
            {
                var reportSession = _historyGrid.Rows[args.RowIndex].Tag as ServerSession;
                return reportSession != null && reportSession.UserReportCount > 0;
            }
            if (args.ColumnIndex != _historyGrid.Columns["blockAction"].Index
                && args.ColumnIndex != _historyGrid.Columns["unblockAction"].Index)
                return false;

            DataGridViewCell cell = _historyGrid.Rows[args.RowIndex].Cells[args.ColumnIndex];
            Rectangle localBounds = new Rectangle(0, 0, cell.Size.Width, cell.Size.Height);
            return GetActionButtonBounds(localBounds).Contains(args.X, args.Y);
        }

        private Rectangle GetActionButtonBounds(Rectangle cellBounds)
        {
            return GetActionButtonBounds(
                cellBounds,
                _historyGrid == null || _historyGrid.DeviceDpi <= 0 ? 96 : _historyGrid.DeviceDpi);
        }

        private static Rectangle GetActionButtonBounds(Rectangle cellBounds, int dpi)
        {
            int safeDpi = dpi <= 0 ? 96 : dpi;
            int horizontal = Math.Max(2, (int)Math.Round(3.0 * safeDpi / 96.0));
            int vertical = Math.Max(3, (int)Math.Round(4.0 * safeDpi / 96.0));
            Rectangle result = Rectangle.Inflate(cellBounds, -horizontal, -vertical);
            if (result.Width < 1 || result.Height < 1) return Rectangle.Empty;
            return result;
        }

        private void StickyActionGridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            var grid = sender as DataGridView;
            if (grid == null || e.ColumnIndex < 0) return;

            Rectangle clip = Rectangle.Intersect(e.CellBounds, e.ClipBounds);
            if (clip.Width <= 0 || clip.Height <= 0)
            {
                e.Handled = true;
                return;
            }

            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(clip);
                if (e.RowIndex < 0)
                {
                    DataGridViewColumn column = grid.Columns[e.ColumnIndex];
                    bool active = column.SortMode == DataGridViewColumnSortMode.Programmatic
                        && string.Equals(_historySortColumn, column.Name, StringComparison.Ordinal)
                        && _historySortOrder != SortOrder.None;
                    PaintDarkGridHeader(
                        e,
                        active ? _historySortOrder : SortOrder.None,
                        IsEnglishUi);
                    return;
                }

                bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                Color rowBackground = selected
                    ? grid.DefaultCellStyle.SelectionBackColor
                    : Surface;
                using (var rowBrush = new SolidBrush(rowBackground))
                    e.Graphics.FillRectangle(rowBrush, e.CellBounds);
                using (var rowBorder = new Pen(Border))
                    e.Graphics.DrawLine(
                        rowBorder,
                        e.CellBounds.Left,
                        e.CellBounds.Bottom - 1,
                        e.CellBounds.Right - 1,
                        e.CellBounds.Bottom - 1);

                DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                bool enabled = cell.Tag is bool && (bool)cell.Tag;
                bool isBlock = string.Equals(
                    grid.Columns[e.ColumnIndex].Name,
                    "blockAction",
                    StringComparison.Ordinal);
                Rectangle buttonBounds = GetActionButtonBounds(
                    e.CellBounds,
                    grid.DeviceDpi <= 0 ? 96 : grid.DeviceDpi);
                if (!buttonBounds.IsEmpty)
                {
                    Color background = enabled
                        ? (isBlock ? Danger : Success)
                        : Color.FromArgb(37, 44, 53);
                    Color foreground = enabled
                        ? (isBlock ? Color.White : Color.FromArgb(18, 50, 34))
                        : TextMuted;
                    Color outline = enabled
                        ? (isBlock ? Color.FromArgb(202, 78, 86) : Color.FromArgb(53, 170, 110))
                        : Border;
                    using (var fill = new SolidBrush(background))
                        e.Graphics.FillRectangle(fill, buttonBounds);
                    using (var border = new Pen(outline, 1F))
                        e.Graphics.DrawRectangle(
                            border,
                            buttonBounds.X,
                            buttonBounds.Y,
                            buttonBounds.Width - 1,
                            buttonBounds.Height - 1);
                    TextRenderer.DrawText(
                        e.Graphics,
                        isBlock ? AppText.Get("Common.Button.Block") : AppText.Get("Main.Column.Unblock"),
                        e.CellStyle.Font,
                        buttonBounds,
                        foreground,
                        TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine
                        | TextFormatFlags.NoPrefix);

                    if (grid.Focused
                        && grid.CurrentCell != null
                        && grid.CurrentCell.RowIndex == e.RowIndex
                        && grid.CurrentCell.ColumnIndex == e.ColumnIndex)
                    {
                        Rectangle focusBounds = Rectangle.Inflate(buttonBounds, -2, -2);
                        if (focusBounds.Width > 0 && focusBounds.Height > 0)
                            ControlPaint.DrawFocusRectangle(
                                e.Graphics,
                                focusBounds,
                                foreground,
                                background);
                    }
                }
            }
            finally
            {
                e.Graphics.Restore(state);
                e.Handled = true;
            }
        }

        private void HistoryGridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (_historyGrid == null || e.ColumnIndex < 0) return;
            if (e.RowIndex == -1)
            {
                DataGridViewColumn column = _historyGrid.Columns[e.ColumnIndex];
                bool active = column.SortMode == DataGridViewColumnSortMode.Programmatic
                    && string.Equals(_historySortColumn, column.Name, StringComparison.Ordinal)
                    && _historySortOrder != SortOrder.None;
                PaintDarkGridHeader(
                    e,
                    active ? _historySortOrder : SortOrder.None,
                    false);
                return;
            }

            if (e.ColumnIndex == _historyGrid.Columns["note"].Index)
            {
                PaintNoteCell(e);
                return;
            }
            if (e.ColumnIndex == _historyGrid.Columns["blockAction"].Index
                || e.ColumnIndex == _historyGrid.Columns["unblockAction"].Index)
                PaintConnectionActionCell(e);
        }

        private static void PaintDarkGridHeader(
            DataGridViewCellPaintingEventArgs e,
            SortOrder sortOrder,
            bool compactHorizontalPadding)
        {
            Rectangle clip = Rectangle.Intersect(e.CellBounds, e.ClipBounds);
            if (clip.Width <= 0 || clip.Height <= 0) { e.Handled = true; return; }
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(clip);
                using (var background = new SolidBrush(SurfaceAlt))
                    e.Graphics.FillRectangle(background, e.CellBounds);
                // Recreate the familiar raised header without the native light
                // theme: each shared boundary is exactly one shadow pixel from
                // the preceding cell plus one muted-highlight pixel from the
                // leading edge of the next cell.
                using (var highlight = new Pen(HeaderBevelHighlight))
                using (var shadow = new Pen(HeaderBevelShadow))
                {
                    int right = e.CellBounds.Right - 1;
                    int bottom = e.CellBounds.Bottom - 1;
                    e.Graphics.DrawLine(
                        highlight,
                        e.CellBounds.Left,
                        e.CellBounds.Top,
                        right,
                        e.CellBounds.Top);
                    e.Graphics.DrawLine(
                        highlight,
                        e.CellBounds.Left,
                        e.CellBounds.Top,
                        e.CellBounds.Left,
                        bottom);
                    e.Graphics.DrawLine(
                        shadow,
                        e.CellBounds.Left,
                        bottom,
                        right,
                        bottom);
                    e.Graphics.DrawLine(shadow, right, e.CellBounds.Top, right, bottom);
                }

                bool active = sortOrder != SortOrder.None;
                float scale = Math.Max(1F, e.Graphics.DpiX / 96F);
                int edgePadding = compactHorizontalPadding
                    ? Math.Max(2, (int)Math.Round(2F * scale))
                    : Math.Max(4, (int)Math.Round(4F * scale));
                int arrowWidth = Math.Max(7, (int)Math.Round(7F * scale));
                int arrowHeight = Math.Max(5, (int)Math.Round(5F * scale));
                int arrowGap = compactHorizontalPadding
                    ? Math.Max(2, (int)Math.Round(2F * scale))
                    : Math.Max(4, (int)Math.Round(4F * scale));
                int horizontalInset = Math.Min(
                    edgePadding,
                    Math.Max(0, (e.CellBounds.Width - 1) / 2));
                Rectangle textBounds = new Rectangle(
                    e.CellBounds.Left + horizontalInset,
                    e.CellBounds.Top + 2,
                    Math.Max(1, e.CellBounds.Width - horizontalInset * 2),
                    Math.Max(1, e.CellBounds.Height - 4));
                Rectangle arrowBounds = Rectangle.Empty;
                if (active)
                {
                    arrowBounds = new Rectangle(
                        e.CellBounds.Right - edgePadding - arrowWidth,
                        e.CellBounds.Top + (e.CellBounds.Height - arrowHeight) / 2,
                        arrowWidth,
                        arrowHeight);
                    textBounds.Width = Math.Max(1, arrowBounds.Left - arrowGap - textBounds.Left);
                }

                TextFormatFlags flags = TextFormatFlags.NoPrefix
                    | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.PreserveGraphicsClipping;
                switch (e.CellStyle.Alignment)
                {
                    case DataGridViewContentAlignment.BottomCenter:
                    case DataGridViewContentAlignment.MiddleCenter:
                    case DataGridViewContentAlignment.TopCenter:
                        flags |= TextFormatFlags.HorizontalCenter;
                        break;
                    case DataGridViewContentAlignment.BottomRight:
                    case DataGridViewContentAlignment.MiddleRight:
                    case DataGridViewContentAlignment.TopRight:
                        flags |= TextFormatFlags.Right;
                        break;
                    default:
                        flags |= TextFormatFlags.Left;
                        break;
                }
                string headerText = Convert.ToString(e.FormattedValue) ?? string.Empty;
                // Explicit line breaks are layout, not wrapping hints. Paint each
                // declared line separately so narrowing a column cannot create a
                // third line inside the fixed-height header.
                string[] fixedLines = headerText
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split(new[] { '\n' }, StringSplitOptions.None);
                if (fixedLines.Length > 1)
                {
                    TextFormatFlags lineFlags = flags | TextFormatFlags.SingleLine;
                    int lineHeight = Math.Max(1, TextRenderer.MeasureText(
                        e.Graphics,
                        "가A",
                        e.CellStyle.Font,
                        Size.Empty,
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height);
                    int blockHeight = Math.Min(textBounds.Height, lineHeight * fixedLines.Length);
                    int lineTop = textBounds.Top + Math.Max(0, (textBounds.Height - blockHeight) / 2);
                    for (int lineIndex = 0; lineIndex < fixedLines.Length; lineIndex++)
                    {
                        int top = lineTop + lineIndex * lineHeight;
                        if (top >= textBounds.Bottom) break;
                        TextRenderer.DrawText(
                            e.Graphics,
                            fixedLines[lineIndex],
                            e.CellStyle.Font,
                            new Rectangle(
                                textBounds.Left,
                                top,
                                textBounds.Width,
                                Math.Min(lineHeight, textBounds.Bottom - top)),
                            e.CellStyle.ForeColor,
                            lineFlags);
                    }
                }
                else
                {
                    TextFormatFlags singleLineFlags = e.CellStyle.WrapMode == DataGridViewTriState.True
                        ? flags | TextFormatFlags.WordBreak
                        : flags | TextFormatFlags.SingleLine;
                    TextRenderer.DrawText(
                        e.Graphics,
                        headerText,
                        e.CellStyle.Font,
                        textBounds,
                        e.CellStyle.ForeColor,
                        singleLineFlags);
                }

                if (active)
                {
                    Point[] points;
                    if (sortOrder == SortOrder.Ascending)
                    {
                        points = new[]
                        {
                            new Point(arrowBounds.Left + arrowBounds.Width / 2, arrowBounds.Top),
                            new Point(arrowBounds.Left, arrowBounds.Bottom - 1),
                            new Point(arrowBounds.Right - 1, arrowBounds.Bottom - 1)
                        };
                    }
                    else
                    {
                        points = new[]
                        {
                            new Point(arrowBounds.Left, arrowBounds.Top),
                            new Point(arrowBounds.Right - 1, arrowBounds.Top),
                            new Point(arrowBounds.Left + arrowBounds.Width / 2, arrowBounds.Bottom - 1)
                        };
                    }
                    using (var arrowBrush = new SolidBrush(Accent))
                        e.Graphics.FillPolygon(arrowBrush, points);
                }
            }
            finally
            {
                e.Graphics.Restore(state);
                e.Handled = true;
            }
        }

        private void PaintGridCellBase(DataGridViewCellPaintingEventArgs e)
        {
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            Color background = selected
                ? _historyGrid.DefaultCellStyle.SelectionBackColor
                : Surface;
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.CellBounds);
            using (var border = new Pen(Border))
                e.Graphics.DrawLine(border, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right - 1, e.CellBounds.Bottom - 1);
        }

        private void PaintConnectionActionCell(DataGridViewCellPaintingEventArgs e)
        {
            Rectangle clip = Rectangle.Intersect(e.CellBounds, e.ClipBounds);
            if (clip.Width <= 0 || clip.Height <= 0) { e.Handled = true; return; }
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(clip);
                PaintGridCellBase(e);
                DataGridViewCell cell = _historyGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                bool enabled = cell.Tag is bool && (bool)cell.Tag;
                bool isBlock = e.ColumnIndex == _historyGrid.Columns["blockAction"].Index;
                Rectangle buttonBounds = GetActionButtonBounds(e.CellBounds);
                if (!buttonBounds.IsEmpty)
                {
                    Color background = enabled ? (isBlock ? Danger : Success) : Color.FromArgb(37, 44, 53);
                    Color foreground = enabled
                        ? (isBlock ? Color.White : Color.FromArgb(18, 50, 34))
                        : TextMuted;
                    Color outline = enabled ? (isBlock ? Color.FromArgb(202, 78, 86) : Color.FromArgb(53, 170, 110)) : Border;
                    using (var brush = new SolidBrush(background))
                        e.Graphics.FillRectangle(brush, buttonBounds);
                    using (var pen = new Pen(outline))
                        e.Graphics.DrawRectangle(pen, buttonBounds.X, buttonBounds.Y, buttonBounds.Width - 1, buttonBounds.Height - 1);
                    TextRenderer.DrawText(
                        e.Graphics,
                        isBlock ? AppText.Get("Common.Button.Block") : AppText.Get("Main.Column.Unblock"),
                        e.CellStyle.Font,
                        buttonBounds,
                        foreground,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
            finally
            {
                e.Graphics.Restore(state);
                e.Handled = true;
            }
        }

        private void PaintNoteCell(DataGridViewCellPaintingEventArgs e)
        {
            Rectangle clip = Rectangle.Intersect(e.CellBounds, e.ClipBounds);
            if (clip.Width <= 0 || clip.Height <= 0) { e.Handled = true; return; }
            GraphicsState state = e.Graphics.Save();
            try
            {
                e.Graphics.SetClip(clip);
                PaintGridCellBase(e);
                DataGridViewCell cell = _historyGrid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                bool hasNote = cell.Tag is bool && (bool)cell.Tag;
                int iconWidth = Math.Max(16, (int)Math.Round(18.0 * _historyGrid.DeviceDpi / 96.0));
                int iconHeight = Math.Max(14, (int)Math.Round(16.0 * _historyGrid.DeviceDpi / 96.0));
                Rectangle iconBounds = new Rectangle(
                    e.CellBounds.Left + (e.CellBounds.Width - iconWidth) / 2,
                    e.CellBounds.Top + (e.CellBounds.Height - iconHeight) / 2,
                    iconWidth,
                    iconHeight);
                using (GraphicsPath bubble = CreateSpeechBubblePath(iconBounds))
                {
                    SmoothingMode oldMode = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    if (hasNote)
                    {
                        using (var brush = new SolidBrush(ReportOrange))
                            e.Graphics.FillPath(brush, bubble);
                    }
                    else
                    {
                        using (var pen = new Pen(ReportOrange, Math.Max(1.4F, 1.5F * _historyGrid.DeviceDpi / 96F)))
                            e.Graphics.DrawPath(pen, bubble);
                    }
                    e.Graphics.SmoothingMode = oldMode;
                }
            }
            finally
            {
                e.Graphics.Restore(state);
                e.Handled = true;
            }
        }

        private static GraphicsPath CreateSpeechBubblePath(Rectangle bounds)
        {
            int radius = Math.Max(2, bounds.Width / 6);
            int tail = Math.Max(3, bounds.Height / 4);
            int left = bounds.Left;
            int top = bounds.Top;
            int right = bounds.Right - 1;
            int bodyBottom = bounds.Bottom - tail - 1;
            int diameter = radius * 2;
            int center = left + bounds.Width / 2;

            var path = new GraphicsPath();
            path.StartFigure();
            path.AddLine(left + radius, top, right - radius, top);
            path.AddArc(right - diameter, top, diameter, diameter, 270, 90);
            path.AddLine(right, top + radius, right, bodyBottom - radius);
            path.AddArc(right - diameter, bodyBottom - diameter, diameter, diameter, 0, 90);
            path.AddLine(right - radius, bodyBottom, center + 2, bodyBottom);
            path.AddLine(center + 2, bodyBottom, center - 3, bounds.Bottom - 1);
            path.AddLine(center - 3, bounds.Bottom - 1, center - 4, bodyBottom);
            path.AddLine(center - 4, bodyBottom, left + radius, bodyBottom);
            path.AddArc(left, bodyBottom - diameter, diameter, diameter, 90, 90);
            path.AddLine(left, bodyBottom - radius, left, top + radius);
            path.AddArc(left, top, diameter, diameter, 180, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
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

        private static Panel CreateCardPanel()
        {
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 4, 0, 4)
            };
        }

        private static Label AddDetailLine(Panel parent, string key, string value, int top, int valueLeft)
        {
            var keyLabel = new Label
            {
                AutoSize = true,
                Text = key,
                ForeColor = TextMuted,
                // Malgun Gothic bold glyphs can overhang their layout box slightly.
                // Keep every detail key on the same safe inset from the card edge.
                Location = new Point(4, top),
                Font = new Font(
                    "Malgun Gothic",
                    8.5F,
                    FontStyle.Bold)
            };
            parent.Controls.Add(keyLabel);

            var valueLabel = new Label
            {
                AutoSize = true,
                Text = value,
                ForeColor = TextPrimary,
                // Leave a small internal inset so the first glyph's antialiasing
                // overhang is not clipped at any DPI.
                Location = new Point(valueLeft - 3, top - 1),
                Padding = new Padding(3, 0, 1, 0),
                Font = new Font("Malgun Gothic", 9F)
            };
            parent.Controls.Add(valueLabel);
            return valueLabel;
        }

        private TableLayoutPanel CreateDetailInfoLayout(out Label[] valueLabels)
        {
            string[] keys =
            {
                AppText.Get("Main.Advanced.OperationTime"),
                AppText.Get("Main.Advanced.AssignmentEntryTime"),
                AppText.Get("Main.Column.ConnectionResult"),
                AppText.Get("Main.Advanced.GameVersion"),
                AppText.Get("Main.Advanced.PortDataCenter"),
                "shortId",
                "SID"
            };
            valueLabels = new Label[keys.Length];
            var layout = new TableLayoutPanel
            {
                BackColor = Surface,
                ColumnCount = 2,
                RowCount = keys.Length,
                Margin = new Padding(0),
                Padding = new Padding(0),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int index = 0; index < keys.Length; index++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / keys.Length));
                Label keyLabel = new FittedDetailLabel();
                keyLabel.Dock = DockStyle.Fill;
                keyLabel.AutoSize = false;
                keyLabel.Text = keys[index];
                keyLabel.ForeColor = TextMuted;
                keyLabel.BackColor = Surface;
                keyLabel.Font = new Font("Malgun Gothic", 8.5F);
                keyLabel.TextAlign = ContentAlignment.MiddleLeft;
                keyLabel.Margin = new Padding(0);
                keyLabel.Padding = new Padding(0);
                keyLabel.AutoEllipsis = false;
                Label valueLabel = new DetailValueLabel
                {
                    SuffixColor = TextMuted,
                    FitTextToWidth = index == 2
                };
                valueLabel.Dock = DockStyle.Fill;
                valueLabel.AutoSize = false;
                valueLabel.Text = "-";
                valueLabel.ForeColor = TextMuted;
                valueLabel.BackColor = Surface;
                valueLabel.Font = new Font("Malgun Gothic", 8.5F);
                valueLabel.TextAlign = ContentAlignment.MiddleLeft;
                valueLabel.Margin = new Padding(0);
                // Every value row shares the same owner-draw origin and flags.
                // This inset preserves the existing visual column while avoiding
                // per-control native Label padding differences.
                valueLabel.Padding = new Padding(1, 0, 0, 0);
                valueLabel.AutoEllipsis = false;
                layout.Controls.Add(keyLabel, 0, index);
                layout.Controls.Add(valueLabel, 1, index);
                if (index == 0)
                {
                    string operationTimeHelp = AppText.Get("Main.Advanced.OperationTimeHelp");
                    _toolTip.SetToolTip(keyLabel, operationTimeHelp);
                    _toolTip.SetToolTip(valueLabel, operationTimeHelp);
                }
                else if (index == 1)
                {
                    string entryTimeHelp = AppText.Get("Main.Advanced.AssignmentEntryHelp");
                    _toolTip.SetToolTip(keyLabel, entryTimeHelp);
                    _toolTip.SetToolTip(valueLabel, entryTimeHelp);
                }
                valueLabels[index] = valueLabel;
            }
            return layout;
        }

        private void UpdateCurrentServerResponsiveLayout(Panel details)
        {
            if (details == null || _advancedDetailsLayout == null) return;
            int dpi = details.DeviceDpi <= 0 ? 96 : details.DeviceDpi;
            int responsiveThreshold = ScaleLogical(1180, dpi);
            int availableWindowWidth = Width;
            bool compact = availableWindowWidth > 0 && availableWindowWidth < responsiveThreshold;
            _advancedDetailsLayout.Visible = !compact;
            if (IsEnglishUi)
            {
                int right = compact ? details.ClientSize.Width - ScaleLogical(8, dpi)
                    : _advancedDetailsLayout.Left - ScaleLogical(14, dpi);
                foreach (Label value in new[] { _mapValueLabel, _timeValueLabel,
                    _locationValueLabel, _pingValueLabel, _actualRttValueLabel, _packetLossValueLabel })
                {
                    if (value == null) continue;
                    value.AutoSize = false;
                    value.AutoEllipsis = true;
                    value.UseMnemonic = false;
                    value.Width = Math.Max(1, right - value.Left);
                    value.Height = Math.Max(value.Font.Height + ScaleLogical(3, dpi), ScaleLogical(20, dpi));
                }
            }
        }

        private static int ScaleLogical(int value, int dpi)
        {
            return Math.Max(1, (int)Math.Round(value * Math.Max(96, dpi) / 96F));
        }

        private static bool IsEnglishUi
        {
            get
            {
                return string.Equals(
                    AppText.CurrentLanguage,
                    AppText.EnglishLanguage,
                    StringComparison.Ordinal);
            }
        }

        private static int GetStickyActionLogicalWidth()
        {
            return IsEnglishUi ? 156 : 116;
        }

        private static Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                Dock = DockStyle.Fill,
                Text = text,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Malgun Gothic", 9F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            StyleButton(button, primary);
            return button;
        }

        private static void StyleButton(Button button, bool primary)
        {
            if (button == null) return;
            button.BackColor = primary ? Accent : SurfaceAlt;
            button.ForeColor = primary ? Color.FromArgb(29, 24, 17) : TextPrimary;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Color.FromArgb(42, 50, 60);
            button.FlatAppearance.MouseDownBackColor = primary
                ? Color.FromArgb(213, 139, 39)
                : Color.FromArgb(48, 57, 68);
        }

        private static void StyleDangerButton(Button button)
        {
            if (button == null) return;
            Color dangerBackground = Color.FromArgb(192, 68, 75);
            button.BackColor = dangerBackground;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = dangerBackground;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(213, 82, 90);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(164, 55, 62);
        }

        private static Button CreateFilterButton(string text)
        {
            var button = CreateButton(text, false);
            button.Dock = DockStyle.None;
            button.Size = new Size(text == "Arena" ? 66 : 56, 28);
            button.Margin = new Padding(4, 0, 0, 0);
            button.Font = new Font("Malgun Gothic", 8F, FontStyle.Bold);
            return button;
        }

        private static Button CreatePeriodButton(string text, int width)
        {
            Button button = CreateFilterButton(text);
            button.Size = new Size(width, 28);
            return button;
        }

        private static Button CreateToolButton(
            string text,
            ToolIconKind iconKind,
            Color iconColor,
            int minimumWidth)
        {
            var button = new ToolIconButton(text, iconKind, iconColor)
            {
                Dock = DockStyle.None,
                Size = new Size(minimumWidth, 28),
                Margin = new Padding(4, 0, 0, 0),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Malgun Gothic", 8F, FontStyle.Bold),
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            Size measured = TextRenderer.MeasureText(
                text,
                button.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            button.Width = Math.Max(minimumWidth, measured.Width + 14 + 6 + 20);
            StyleButton(button, false);
            return button;
        }

        private async Task InitializeAsync()
        {
            _isRefreshing = true;
            UpdateActionButtons();
            SetStatus(AppText.Get("Main.Paths.FindingInitial"), TextMuted);
            TarkovLogPaths found;
            try
            {
                found = await Task.Run(() => TarkovLogPathFinder.Find(_settingsStore.Load()));
                _eftPathTextBox.Text = found.EftPath ?? string.Empty;
                _arenaPathTextBox.Text = found.ArenaPath ?? string.Empty;
                _settingsStore.Save(found);
                SetAppliedPaths(found);
                await RefreshLauncherSelectionAsync();
            }
            catch (Exception ex)
            {
                SetStatus(AppText.Format("Main.Paths.InitialCheckFailed", ex.Message), Danger);
                return;
            }
            finally
            {
                _isRefreshing = false;
                UpdateActionButtons();
            }

            if (string.IsNullOrWhiteSpace(found.EftPath) && string.IsNullOrWhiteSpace(found.ArenaPath))
            {
                ShowNoServer(AppText.Get("Main.Paths.AutoDetectFailed"));
                return;
            }
            await LoadSessionsAsync();
        }

        private async Task RediscoverLogPathsAsync()
        {
            if (_isRefreshing || _isMeasuring || _isFirewallChanging) return;

            TarkovLogPaths current = GetPathsFromInputs();
            TarkovLogPaths found = null;
            bool foundEftAutomatically = false;
            bool foundArenaAutomatically = false;
            _isRefreshing = true;
            UpdateActionButtons();
            SetStatus(AppText.Get("Main.Paths.Rediscovering"), TextMuted);
            try
            {
                found = await Task.Run(() => TarkovLogPathFinder.Find(null));
                foundEftAutomatically = !string.IsNullOrWhiteSpace(found.EftPath);
                foundArenaAutomatically = !string.IsNullOrWhiteSpace(found.ArenaPath);

                // If one game is not installed in a standard location, preserve its valid manual path.
                if (!foundEftAutomatically) found.EftPath = current.EftPath;
                if (!foundArenaAutomatically) found.ArenaPath = current.ArenaPath;

                _eftPathTextBox.Text = found.EftPath ?? string.Empty;
                _arenaPathTextBox.Text = found.ArenaPath ?? string.Empty;
                _settingsStore.Save(found);
                SetAppliedPaths(found);
                await RefreshLauncherSelectionAsync();
            }
            catch (Exception ex)
            {
                SetStatus(AppText.Format("Main.Paths.RediscoverFailed", ex.Message), Danger);
                return;
            }
            finally
            {
                _isRefreshing = false;
                UpdateActionButtons();
            }

            if (found == null
                || (string.IsNullOrWhiteSpace(found.EftPath) && string.IsNullOrWhiteSpace(found.ArenaPath)))
            {
                ShowNoServer(AppText.Get("Main.Paths.InstallNotFound"));
                return;
            }

            string detected = foundEftAutomatically && foundArenaAutomatically
                ? "EFT·Arena"
                : (foundEftAutomatically ? "EFT" : (foundArenaAutomatically ? "Arena" : AppText.Get("Main.Paths.SavedSource")));
            SetStatus(AppText.Format("Main.Paths.Detected", detected), Success);
            await LoadSessionsAsync();
        }

        private TarkovLogPaths GetPathsFromInputs()
        {
            return new TarkovLogPaths
            {
                EftPath = Directory.Exists(_eftPathTextBox.Text) ? _eftPathTextBox.Text : null,
                ArenaPath = Directory.Exists(_arenaPathTextBox.Text) ? _arenaPathTextBox.Text : null
            };
        }

        private async Task ApplyLogPathsAsync()
        {
            if (_isRefreshing || _isMeasuring || _isFirewallChanging) return;
            string eft = NormalizeOptionalPath(_eftPathTextBox.Text);
            string arena = NormalizeOptionalPath(_arenaPathTextBox.Text);
            if (string.IsNullOrWhiteSpace(eft) && string.IsNullOrWhiteSpace(arena))
            {
                MessageBox.Show(
                    this,
                    AppText.Get("Main.Paths.SelectAtLeastOne"),
                    AppText.Get("Main.Paths.DialogTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _eftPathTextBox.Text = eft ?? string.Empty;
            _arenaPathTextBox.Text = arena ?? string.Empty;
            var applied = new TarkovLogPaths { EftPath = eft, ArenaPath = arena };
            _settingsStore.Save(applied);
            SetAppliedPaths(applied);
            _selectedSession = null;
            await LoadSessionsAsync();
        }

        private static string NormalizeOptionalPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return LogPathFinder.NormalizeSelectedFolder(value);
        }

        private void BrowseForGame(TarkovGame game)
        {
            if (_isRefreshing || _isMeasuring || _isFirewallChanging) return;
            TextBox target = game == TarkovGame.Arena ? _arenaPathTextBox : _eftPathTextBox;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = AppText.Format(
                    "Main.Paths.SelectPrompt",
                    game == TarkovGame.Arena ? "Escape From Tarkov Arena" : "Escape From Tarkov");
                dialog.ShowNewFolderButton = false;
                if (!string.IsNullOrWhiteSpace(target.Text) && Directory.Exists(target.Text))
                    dialog.SelectedPath = target.Text;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                string normalized = LogPathFinder.NormalizeSelectedFolder(dialog.SelectedPath);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    MessageBox.Show(
                        this,
                        AppText.Get("Main.Paths.NoLogsInSelection"),
                        AppText.Get("Main.Paths.DialogTitle"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
                target.Text = normalized;
                UpdateActionButtons();
                if (HasPendingPathChanges())
                    SetStatus(AppText.Get("Main.Paths.SelectionPending"), Accent);
                else
                    SetStatus(AppText.Get("Main.Paths.SelectionUnchanged"), TextMuted);
            }
        }

        private void SetAppliedPaths(TarkovLogPaths paths)
        {
            _appliedEftPath = paths == null ? null : paths.EftPath;
            _appliedArenaPath = paths == null ? null : paths.ArenaPath;
            if (_applyPathButton != null) StyleButton(_applyPathButton, false);
        }

        private bool HasPendingPathChanges()
        {
            if (_demoMode) return false;
            return !PathsEqual(_eftPathTextBox == null ? null : _eftPathTextBox.Text, _appliedEftPath)
                || !PathsEqual(_arenaPathTextBox == null ? null : _arenaPathTextBox.Text, _appliedArenaPath);
        }

        private static bool PathsEqual(string left, string right)
        {
            string normalizedLeft = NormalizePathForComparison(left);
            string normalizedRight = NormalizePathForComparison(right);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePathForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path.Trim().Trim('"'))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }

        private async Task LoadSessionsAsync()
        {
            if (_demoMode || _isRefreshing) return;
            TarkovLogPaths paths = GetPathsFromInputs();
            if (string.IsNullOrWhiteSpace(paths.EftPath) && string.IsNullOrWhiteSpace(paths.ArenaPath)) return;

            // A new log scan invalidates any read-only firewall lookup that still belongs
            // to the previously displayed session list.
            _initialFirewallStateRefresh.Invalidate();

            SessionPeriodPreset requestedPeriod = _sessionPeriod;
            DateTime? requestedStart = _customPeriodStart;
            DateTime? requestedEnd = _customPeriodEnd;
            IList<ServerSession> initialFirewallCandidates = null;

            _isRefreshing = true;
            UpdateActionButtons();
            SetStatus(AppText.Format("Main.Logs.Reading", GetSessionPeriodLabel()), Accent);
            try
            {
                RaidLogScanResult scan = await Task.Run(() => ScanSessionsForPeriod(
                    paths,
                    requestedPeriod,
                    requestedStart,
                    requestedEnd));
                _logScanIncomplete = !scan.ScanCompletedWithoutErrors;
                _dateRangeScanIncomplete = requestedPeriod != SessionPeriodPreset.Recent100
                    && (!scan.TotalMatchingSessionsIsExact || _logScanIncomplete);
                _allSessions = scan.Sessions.ToList();
                initialFirewallCandidates = _allSessions.ToList();
                // A previous period/path may contain the same IP, but its cached state is
                // not proof of the current Windows Firewall configuration. Render the
                // safe unknown fallback until this load's read-only batch succeeds.
                _firewallStates.Clear();
                RefreshVisibleSessions();
                await RefreshLauncherSelectionAsync();

                if (_allSessions.Count == 0)
                {
                    ShowNoServer(AddLogScanWarning(
                        requestedPeriod == SessionPeriodPreset.Recent100
                            ? AppText.Get("Main.Logs.NoServerIp")
                            : AppText.Format("Main.Logs.NoConnectionsForPeriod", GetSessionPeriodLabel())));
                    return;
                }

                if (_visibleSessions.Count == 0)
                {
                    ShowNoServer(AddLogScanWarning(
                        AppText.Format("Main.Logs.NoConnectionsForPeriod", GetSessionPeriodLabel())));
                    return;
                }

                SetHistorySummaryStatus(Success, true);
            }
            catch (Exception ex)
            {
                SetStatus(AppText.Format("Main.Logs.ReadFailed", ex.Message), Danger);
            }
            finally
            {
                _isRefreshing = false;
                UpdateActionButtons();
                if (initialFirewallCandidates != null)
                    StartInitialFirewallStateRefresh(initialFirewallCandidates);
            }
        }

        private async void StartInitialFirewallStateRefresh(
            IEnumerable<ServerSession> sessions)
        {
            if (_demoMode || IsDisposed || Disposing) return;
            IList<string> candidates = sessions == null
                ? new List<string>()
                : sessions.Where(session => session != null && session.HasServerIp)
                    .Select(session => session.IpAddress)
                    .ToList();
            InitialFirewallStateRefreshResult result;
            try
            {
                result = await _initialFirewallStateRefresh
                    .RefreshAsync(candidates)
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!_initialFirewallStateRefresh.IsCurrent(result))
                return;

            try
            {
                if (!IsHandleCreated || IsDisposed || Disposing) return;
                BeginInvoke(new Action(() => ApplyInitialFirewallStateRefresh(result)));
            }
            catch (ObjectDisposedException)
            {
                // The form closed between the handle check and the UI dispatch.
            }
            catch (InvalidOperationException)
            {
                // The form closed between the handle check and the UI dispatch.
            }
        }

        private void ApplyInitialFirewallStateRefresh(
            InitialFirewallStateRefreshResult result)
        {
            if (IsDisposed || Disposing
                || !_initialFirewallStateRefresh.IsCurrent(result))
                return;

            IList<string> currentCandidates = _allSessions
                .Where(session => session != null && session.HasServerIp)
                .Select(session => session.IpAddress)
                .ToList();
            IDictionary<string, FirewallQueryResult> applicable =
                InitialFirewallStateRefreshPolicy.GetApplicableSuccessfulStates(
                    result,
                    currentCandidates);
            foreach (KeyValuePair<string, FirewallQueryResult> pair in applicable)
            {
                _firewallStates[pair.Key] = pair.Value;
                UpdateServerRowsFromCache(pair.Key);
            }
            if (applicable.Count > 0)
            {
                RefreshActionCells();
                if (IsFirewallActionSortColumn(_historySortColumn))
                    ReapplyHistorySort(false, true);
            }
        }

        private static RaidLogScanResult ScanSessionsForPeriod(
            TarkovLogPaths paths,
            SessionPeriodPreset period,
            DateTime? customStart,
            DateTime? customEnd)
        {
            if (period == SessionPeriodPreset.Recent100)
                return RaidLogScanner.Scan(paths, 100);

            DateTime? startInclusive;
            DateTime? endExclusive;
            GetPeriodBounds(period, customStart, customEnd, out startInclusive, out endExclusive);
            return RaidLogScanner.Scan(paths, new RaidLogScanQuery
            {
                // Keep all matches in memory so switching EFT/Arena remains immediate and
                // each game filter can independently select its newest 100 rows. The grid
                // cap and its explicit overflow notice are applied in RefreshVisibleSessions.
                MaximumRecords = int.MaxValue,
                StartInclusive = startInclusive,
                EndExclusive = endExclusive,
                GameFilter = null
            });
        }

        private sealed class SessionRefreshScanResult
        {
            public RaidLogScanResult Scan { get; set; }
            public bool AllConfiguredPathsAvailable { get; set; }
        }

        private static SessionRefreshScanResult ScanSessionsForRefresh(
            TarkovLogPaths paths,
            SessionPeriodPreset period,
            DateTime? customStart,
            DateTime? customEnd)
        {
            bool eftAvailable = string.IsNullOrWhiteSpace(paths.EftPath)
                || Directory.Exists(paths.EftPath);
            bool arenaAvailable = string.IsNullOrWhiteSpace(paths.ArenaPath)
                || Directory.Exists(paths.ArenaPath);
            return new SessionRefreshScanResult
            {
                Scan = ScanSessionsForPeriod(paths, period, customStart, customEnd),
                AllConfiguredPathsAvailable = eftAvailable && arenaAvailable
            };
        }

        private static IList<ServerSession> MergeRefreshedSessions(
            IEnumerable<ServerSession> refreshed,
            IEnumerable<ServerSession> existing)
        {
            var merged = new List<ServerSession>();
            var sessionsByIdentity = new Dictionary<string, ServerSession>(
                StringComparer.OrdinalIgnoreCase);
            foreach (IEnumerable<ServerSession> source in new[] { refreshed, existing })
            {
                if (source == null) continue;
                foreach (ServerSession session in source.Where(item => item != null))
                {
                    string identity = GetSessionRefreshIdentity(session);
                    ServerSession retained;
                    if (sessionsByIdentity.TryGetValue(identity, out retained))
                    {
                        // This path is used only when a refresh is incomplete. Keep the
                        // freshly parsed row, but do not discard classification evidence
                        // that an earlier complete read found for the same raid.
                        RaidClassificationModel.MergeInto(retained, session);
                        continue;
                    }
                    sessionsByIdentity.Add(identity, session);
                    merged.Add(session);
                }
            }
            return merged;
        }

        private static string GetSessionRefreshIdentity(ServerSession session)
        {
            if (!string.IsNullOrWhiteSpace(session.SessionKey)) return session.SessionKey;
            return session.Game
                + "|" + (session.LogFilePath ?? string.Empty)
                + "|" + session.DisplayDetectedAt.Ticks;
        }

        private async Task RefreshLauncherSelectionAsync()
        {
            if (_demoMode) return;
            await RefreshLauncherSelectionFromAsync(Task.Run(() => LauncherSelectionReader.ReadCurrent()));
        }

        private async Task RefreshLauncherSelectionFromAsync(Task<LauncherSelectionInfo> read)
        {
            int version = ++_launcherSelectionReadVersion;
            LauncherSelectionInfo selection = await read;
            if (version != _launcherSelectionReadVersion || selection == null
                || _launcherSelectionLabel == null || IsDisposed || Disposing) return;

            // FileSystemWatcher can fire while a file is being atomically replaced. Keep
            // the last successfully parsed value for that source until a complete retry
            // is available instead of briefly flashing "선택 기록 없음".
            // A failed launcher Apply can restore a valid selection with an older
            // timestamp. Order reads, not recorded selections, to reject stale work.
            if (selection.EftSelectionInvalidated || !string.IsNullOrWhiteSpace(selection.EftSelection))
            {
                _lastEftLauncherSelection = selection.EftSelection;
                _lastEftLauncherSelectionAt = selection.EftUpdatedAt;
            }
            if (!string.IsNullOrWhiteSpace(selection.ArenaSelection))
            {
                _lastArenaLauncherSelection = selection.ArenaSelection;
                _lastArenaLauncherSelectionAt = selection.ArenaUpdatedAt;
            }

            string eftDisplay = string.IsNullOrWhiteSpace(_lastEftLauncherSelection)
                ? selection.GetDisplay(TarkovGame.Eft)
                : _lastEftLauncherSelection;
            string arenaDisplay = string.IsNullOrWhiteSpace(_lastArenaLauncherSelection)
                ? selection.GetDisplay(TarkovGame.Arena)
                : _lastArenaLauncherSelection;
            _launcherSelectionLabel.Text = AppText.Format(
                "Main.LauncherSelection.Format",
                FormatLauncherSelection(eftDisplay, _lastEftLauncherSelectionAt),
                FormatLauncherSelection(arenaDisplay, _lastArenaLauncherSelectionAt));
            _launcherSelectionLabel.ForeColor = TextMuted;
        }

        private void StartLauncherSelectionMonitoring()
        {
            if (_demoMode || IsDisposed || Disposing) return;
            _launcherSelectionMonitoringStopped = false;
            EnsureLauncherSelectionWatchers();

            if (_launcherSelectionWatcherHealthTimer != null) return;
            _launcherSelectionWatcherHealthTimer = new System.Windows.Forms.Timer
            {
                Interval = 30000
            };
            _launcherSelectionWatcherHealthTimer.Tick += delegate
            {
                EnsureLauncherSelectionWatchers();
                ScheduleLauncherSelectionRefresh();
            };
            _launcherSelectionWatcherHealthTimer.Start();
        }

        private void StopLauncherSelectionMonitoring()
        {
            _launcherSelectionMonitoringStopped = true;

            if (_launcherSelectionWatcherHealthTimer != null)
            {
                _launcherSelectionWatcherHealthTimer.Stop();
                _launcherSelectionWatcherHealthTimer.Dispose();
                _launcherSelectionWatcherHealthTimer = null;
            }

            DisposeLauncherSelectionWatcher(ref _launcherLogWatcher);
            DisposeLauncherSelectionWatcher(ref _arenaRegionsWatcher);

            lock (_launcherSelectionRefreshSync)
            {
                if (_launcherSelectionDebounceTimer != null)
                {
                    _launcherSelectionDebounceTimer.Dispose();
                    _launcherSelectionDebounceTimer = null;
                }
            }
        }

        private void EnsureLauncherSelectionWatchers()
        {
            if (_launcherSelectionMonitoringStopped || _demoMode || IsDisposed || Disposing) return;

            string launcherLogDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Battlestate Games",
                "BsgLauncher",
                "Logs");
            EnsureLauncherSelectionWatcher(
                ref _launcherLogWatcher,
                launcherLogDirectory,
                "BSG_Launcher_*.log");

            string arenaSettingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Battlestate Games",
                "Escape from Tarkov Arena",
                "Settings");
            EnsureLauncherSelectionWatcher(
                ref _arenaRegionsWatcher,
                arenaSettingsDirectory,
                "Regions.ini");
        }

        private void EnsureLauncherSelectionWatcher(
            ref FileSystemWatcher watcher,
            string directory,
            string filter)
        {
            bool watcherReady = false;
            if (watcher != null)
            {
                try
                {
                    watcherReady = Directory.Exists(directory)
                        && watcher.EnableRaisingEvents
                        && string.Equals(watcher.Path, directory, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    watcherReady = false;
                }
            }
            if (watcherReady) return;

            DisposeLauncherSelectionWatcher(ref watcher);
            if (!Directory.Exists(directory)) return;

            FileSystemWatcher created = null;
            try
            {
                created = new FileSystemWatcher(directory, filter)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                        | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                created.Changed += LauncherSelectionSourceChanged;
                created.Created += LauncherSelectionSourceChanged;
                created.Deleted += LauncherSelectionSourceChanged;
                created.Renamed += LauncherSelectionSourceRenamed;
                created.Error += LauncherSelectionWatcherError;
                created.EnableRaisingEvents = true;
                watcher = created;
                ScheduleLauncherSelectionRefresh();
            }
            catch
            {
                if (created != null) created.Dispose();
            }
        }

        private void DisposeLauncherSelectionWatcher(ref FileSystemWatcher watcher)
        {
            FileSystemWatcher disposing = watcher;
            watcher = null;
            if (disposing == null) return;
            try
            {
                disposing.EnableRaisingEvents = false;
                disposing.Changed -= LauncherSelectionSourceChanged;
                disposing.Created -= LauncherSelectionSourceChanged;
                disposing.Deleted -= LauncherSelectionSourceChanged;
                disposing.Renamed -= LauncherSelectionSourceRenamed;
                disposing.Error -= LauncherSelectionWatcherError;
                disposing.Dispose();
            }
            catch
            {
                // A watcher can already be closing while the form is disposed.
            }
        }

        private void LauncherSelectionSourceChanged(object sender, FileSystemEventArgs args)
        {
            ScheduleLauncherSelectionRefresh();
        }

        private void LauncherSelectionSourceRenamed(object sender, RenamedEventArgs args)
        {
            ScheduleLauncherSelectionRefresh();
        }

        private void LauncherSelectionWatcherError(object sender, ErrorEventArgs args)
        {
            ScheduleLauncherSelectionRefresh();
            QueueLauncherSelectionWatcherRecovery(sender as FileSystemWatcher);
        }

        private void QueueLauncherSelectionWatcherRecovery(FileSystemWatcher failedWatcher)
        {
            if (_launcherSelectionMonitoringStopped || !IsHandleCreated || IsDisposed || Disposing) return;
            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (_launcherSelectionMonitoringStopped || IsDisposed || Disposing) return;
                    if (ReferenceEquals(failedWatcher, _launcherLogWatcher))
                        DisposeLauncherSelectionWatcher(ref _launcherLogWatcher);
                    if (ReferenceEquals(failedWatcher, _arenaRegionsWatcher))
                        DisposeLauncherSelectionWatcher(ref _arenaRegionsWatcher);
                    EnsureLauncherSelectionWatchers();
                }));
            }
            catch (InvalidOperationException)
            {
                // The window handle was destroyed or disposed before BeginInvoke completed.
            }
        }

        private void ScheduleLauncherSelectionRefresh()
        {
            if (_launcherSelectionMonitoringStopped || _demoMode || IsDisposed || Disposing) return;
            lock (_launcherSelectionRefreshSync)
            {
                if (_launcherSelectionMonitoringStopped) return;
                if (_launcherSelectionDebounceTimer == null)
                {
                    _launcherSelectionDebounceTimer = new System.Threading.Timer(
                        LauncherSelectionRefreshTimerElapsed,
                        null,
                        Timeout.Infinite,
                        Timeout.Infinite);
                }
                try
                {
                    _launcherSelectionDebounceTimer.Change(450, Timeout.Infinite);
                }
                catch (ObjectDisposedException)
                {
                    // Form disposal won the race with this file-system event.
                }
            }
        }

        private void LauncherSelectionRefreshTimerElapsed(object state)
        {
            if (_launcherSelectionMonitoringStopped || !IsHandleCreated || IsDisposed || Disposing) return;
            try
            {
                BeginInvoke(new Action(BeginLauncherSelectionRefresh));
            }
            catch (InvalidOperationException)
            {
                // The window can close or be disposed after IsHandleCreated was checked.
            }
        }

        private async void BeginLauncherSelectionRefresh()
        {
            if (_launcherSelectionMonitoringStopped || IsDisposed || Disposing) return;
            if (_launcherSelectionRefreshRunning)
            {
                _launcherSelectionRefreshPending = true;
                return;
            }

            _launcherSelectionRefreshRunning = true;
            try
            {
                await RefreshLauncherSelectionAsync();
            }
            catch
            {
                // A transient rotate/write race should keep the last valid display.
            }
            finally
            {
                _launcherSelectionRefreshRunning = false;
                if (_launcherSelectionRefreshPending && !_launcherSelectionMonitoringStopped)
                {
                    _launcherSelectionRefreshPending = false;
                    ScheduleLauncherSelectionRefresh();
                }
            }
        }

        private static string FormatLauncherSelection(string value, DateTime? updatedAt)
        {
            value = AppText.TranslateLiteral(value);
            return updatedAt.HasValue
                ? value + " (" + updatedAt.Value.ToString("MM-dd HH:mm") + ")"
                : value;
        }

        private async Task SelectSessionPeriodAsync(SessionPeriodPreset period)
        {
            if (_isRefreshing || _isMeasuring || _isFirewallChanging) return;
            _sessionPeriod = period;
            UpdatePeriodButtons();

            if (_demoMode)
            {
                _dateRangeScanIncomplete = false;
                _logScanIncomplete = false;
                RefreshVisibleSessions();
                if (_visibleSessions.Count > 0) SetHistorySummaryStatus(TextMuted, false);
                return;
            }

            await LoadSessionsAsync();
        }

        private async Task SelectCustomSessionPeriodAsync()
        {
            if (_isRefreshing || _isMeasuring || _isFirewallChanging) return;

            DateTime start;
            DateTime end;
            if (!TryShowCustomPeriodDialog(out start, out end)) return;

            _customPeriodStart = start.Date;
            _customPeriodEnd = end.Date;
            _toolTip.SetToolTip(
                _customPeriodButton,
                AppText.Format("Main.Period.RangeLabel", start, end));
            await SelectSessionPeriodAsync(SessionPeriodPreset.Custom);
        }

        private bool TryShowCustomPeriodDialog(out DateTime start, out DateTime end)
        {
            DateTime selectedEnd = (_customPeriodEnd ?? DateTime.Today).Date;
            DateTime selectedStart = (_customPeriodStart ?? selectedEnd.AddDays(-6)).Date;

            using (var dialog = new BrandedForm())
            {
                dialog.Text = AppText.Get("Main.Period.DialogTitle");
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.ClientSize = new Size(410, 240);
                dialog.BackColor = Surface;
                dialog.ForeColor = TextPrimary;
                dialog.Font = Font;
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.Padding = new Padding(18, 14, 18, 14);

                var layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Surface,
                    ColumnCount = 2,
                    RowCount = 5,
                    Margin = new Padding(0),
                    Padding = new Padding(0)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
                dialog.Controls.Add(layout);

                var heading = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = AppText.Get("Main.Period.DialogPrompt"),
                    ForeColor = TextPrimary,
                    Font = new Font("Malgun Gothic", 11F, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0)
                };
                layout.Controls.Add(heading, 0, 0);
                layout.SetColumnSpan(heading, 2);

                var startPicker = CreatePeriodDatePicker(selectedStart);
                var endPicker = CreatePeriodDatePicker(selectedEnd);
                layout.Controls.Add(CreatePeriodDialogLabel(AppText.Get("Main.Period.StartDate")), 0, 1);
                layout.Controls.Add(startPicker, 1, 1);
                layout.Controls.Add(CreatePeriodDialogLabel(AppText.Get("Main.Period.EndDate")), 0, 2);
                layout.Controls.Add(endPicker, 1, 2);

                var hint = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = AppText.Get("Main.Period.DialogHelp"),
                    ForeColor = TextMuted,
                    Font = new Font("Malgun Gothic", 8F),
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = false,
                    Margin = new Padding(0, 2, 0, 0)
                };
                layout.Controls.Add(hint, 0, 3);
                layout.SetColumnSpan(hint, 2);

                var buttons = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = Surface,
                    ColumnCount = 3,
                    RowCount = 1,
                    Margin = new Padding(0),
                    Padding = new Padding(0)
                };
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
                buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
                buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                Button apply = CreateButton(AppText.Get("Common.Button.Apply"), true);
                apply.Dock = DockStyle.Fill;
                apply.Margin = new Padding(8, 5, 0, 5);
                Button cancel = CreateButton(AppText.Get("Common.Button.Cancel"), false);
                cancel.Dock = DockStyle.Fill;
                cancel.Margin = new Padding(8, 5, 0, 5);
                cancel.DialogResult = DialogResult.Cancel;
                apply.Click += delegate
                {
                    if (startPicker.Value.Date > endPicker.Value.Date)
                    {
                        MessageBox.Show(
                            dialog,
                            AppText.Get("Main.Period.InvalidRange"),
                            AppText.Get("Main.Period.ValidationTitle"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                        startPicker.Focus();
                        return;
                    }

                    selectedStart = startPicker.Value.Date;
                    selectedEnd = endPicker.Value.Date;
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                };
                buttons.Controls.Add(cancel, 1, 0);
                buttons.Controls.Add(apply, 2, 0);
                layout.Controls.Add(buttons, 0, 4);
                layout.SetColumnSpan(buttons, 2);
                dialog.AcceptButton = apply;
                dialog.CancelButton = cancel;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    start = default(DateTime);
                    end = default(DateTime);
                    return false;
                }
            }

            start = selectedStart;
            end = selectedEnd;
            return true;
        }

        private static DateTimePicker CreatePeriodDatePicker(DateTime value)
        {
            return new DateTimePicker
            {
                Dock = DockStyle.Fill,
                Format = DateTimePickerFormat.Custom,
                CustomFormat = string.Equals(AppText.CurrentLanguage, AppText.EnglishLanguage, StringComparison.Ordinal)
                    ? "yyyy-MM-dd"
                    : "yyyy-MM-dd  dddd",
                Value = value,
                MinDate = new DateTime(2000, 1, 1),
                MaxDate = DateTime.Today,
                CalendarMonthBackground = Surface,
                CalendarForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 9F),
                Margin = new Padding(0, 6, 0, 6)
            };
        }

        private static Label CreatePeriodDialogLabel(string text)
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                ForeColor = TextMuted,
                Font = new Font("Malgun Gothic", 8.5F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0)
            };
        }

        private void UpdatePeriodButtons()
        {
            if (_recentPeriodButton == null) return;
            StyleFilterButton(_recentPeriodButton, _sessionPeriod == SessionPeriodPreset.Recent100);
            StyleFilterButton(_todayPeriodButton, _sessionPeriod == SessionPeriodPreset.Today);
            StyleFilterButton(_sevenDaysPeriodButton, _sessionPeriod == SessionPeriodPreset.Last7Days);
            StyleFilterButton(_thirtyDaysPeriodButton, _sessionPeriod == SessionPeriodPreset.Last30Days);
            StyleFilterButton(_customPeriodButton, _sessionPeriod == SessionPeriodPreset.Custom);
        }

        private string GetSessionPeriodLabel()
        {
            switch (_sessionPeriod)
            {
                case SessionPeriodPreset.Today:
                    return AppText.Get("Main.Period.TodayLabel");
                case SessionPeriodPreset.Last7Days:
                    return AppText.Get("Main.Period.Last7DaysLabel");
                case SessionPeriodPreset.Last30Days:
                    return AppText.Get("Main.Period.Last30DaysLabel");
                case SessionPeriodPreset.Custom:
                    return _customPeriodStart.HasValue && _customPeriodEnd.HasValue
                        ? string.Format("{0:yyyy-MM-dd} ~ {1:yyyy-MM-dd}", _customPeriodStart.Value, _customPeriodEnd.Value)
                        : AppText.Get("Main.Period.CustomLabel");
                default:
                    return AppText.Get("Main.Period.Latest100Label");
            }
        }

        private static void GetPeriodBounds(
            SessionPeriodPreset period,
            DateTime? customStart,
            DateTime? customEnd,
            out DateTime? startInclusive,
            out DateTime? endExclusive)
        {
            DateTime today = DateTime.Today;
            switch (period)
            {
                case SessionPeriodPreset.Today:
                    startInclusive = today;
                    endExclusive = today.AddDays(1);
                    return;
                case SessionPeriodPreset.Last7Days:
                    startInclusive = today.AddDays(-6);
                    endExclusive = today.AddDays(1);
                    return;
                case SessionPeriodPreset.Last30Days:
                    startInclusive = today.AddDays(-29);
                    endExclusive = today.AddDays(1);
                    return;
                case SessionPeriodPreset.Custom:
                    startInclusive = customStart.HasValue ? customStart.Value.Date : today;
                    endExclusive = customEnd.HasValue ? customEnd.Value.Date.AddDays(1) : today.AddDays(1);
                    return;
                default:
                    startInclusive = null;
                    endExclusive = null;
                    return;
            }
        }

        private bool IsSessionInSelectedPeriod(ServerSession session)
        {
            if (session == null) return false;
            DateTime? start;
            DateTime? end;
            GetPeriodBounds(_sessionPeriod, _customPeriodStart, _customPeriodEnd, out start, out end);
            return (!start.HasValue || session.DisplayDetectedAt >= start.Value)
                && (!end.HasValue || session.DisplayDetectedAt < end.Value);
        }

        private void SetHistorySummaryStatus(Color color, bool includeQueryState)
        {
            string game = _gameFilter.HasValue
                ? (_gameFilter.Value == TarkovGame.Eft ? "EFT" : "Arena")
                : AppText.Get("Main.Filter.All");
            string count;
            if (_selectedRegionCodes.Count > 0)
            {
                count = AppText.Format(
                    "Main.Summary.RegionFiltered",
                    _visibleSessions.Count,
                    _regionSourceCount);
                if (_periodResultsTruncated)
                    count += AppText.Format("Main.Summary.MatchingRecentCap", _periodMatchCount);
            }
            else if (_dateRangeScanIncomplete)
            {
                count = _periodResultsTruncated
                    ? AppText.Format("Main.Summary.RecentConfirmedAtLeast", _periodMatchCount)
                    : AppText.Format("Main.Summary.ConfirmedCount", _visibleSessions.Count);
            }
            else
            {
                count = _periodResultsTruncated
                    ? AppText.Format("Main.Summary.VisibleOfMatches", _visibleSessions.Count, _periodMatchCount)
                    : AppText.Format("Main.Summary.VisibleCount", _visibleSessions.Count);
            }
            bool hasScanWarning = _dateRangeScanIncomplete || _logScanIncomplete;
            string scanWarning = _dateRangeScanIncomplete
                ? AppText.Get("Main.Summary.PartialMayOmit")
                : (_logScanIncomplete
                    ? AppText.Get("Main.Summary.PartialMayOmitLatest")
                    : string.Empty);
            SetStatus(
                AppText.Format(
                    "Main.Summary.Status",
                    GetSessionPeriodLabel(),
                    game,
                    count,
                    includeQueryState ? AppText.Get("Main.Summary.ScanPending") : string.Empty,
                    scanWarning),
                hasScanWarning ? Warning : color);
        }

        private string AddLogScanWarning(string message)
        {
            if (_dateRangeScanIncomplete)
                return message + AppText.Get("Main.Summary.PartialMayOmit");
            if (_logScanIncomplete)
                return message + AppText.Get("Main.Summary.PartialMayOmitLatest");
            return message;
        }

        private static string AppendLogRefreshSummary(
            string message,
            bool refreshPerformed,
            bool refreshReadSucceeded,
            int newLogCount,
            int capExcludedCount)
        {
            if (!refreshPerformed) return message;
            if (newLogCount <= 0)
                return refreshReadSucceeded ? message + AppText.Get("Main.Summary.NoNewLogs") : message;
            string result = message + AppText.Format("Main.Summary.NewLogs", newLogCount);
            if (capExcludedCount > 0)
                result += AppText.Format("Main.Summary.OldestExcluded", capExcludedCount);
            return result;
        }

        private void HistoryGridScroll(object sender, ScrollEventArgs args)
        {
            if (args == null || args.ScrollOrientation == ScrollOrientation.VerticalScroll)
            {
                // Vertical scrolling only changes the first visible row. Re-running
                // overlay layout here causes z-order changes and full-grid repaints
                // on every wheel/scrollbar tick.
                SyncStickyActionVerticalScroll();
                return;
            }

            // Horizontal activity can coincide with native scrollbar layout changes.
            // Coalesce those checks so rapid thumb movement performs at most one
            // geometry pass per UI message cycle.
            QueueStickyActionGridBoundsUpdate();
        }

        private void QueueStickyActionGridBoundsUpdate()
        {
            if (_stickyActionLayoutUpdatePending || IsDisposed || Disposing
                || _historyGrid == null || _stickyActionGrid == null)
                return;

            if (!IsHandleCreated)
            {
                UpdateStickyActionGridBounds();
                return;
            }

            _stickyActionLayoutUpdatePending = true;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    try
                    {
                        if (!IsDisposed && !Disposing)
                            UpdateStickyActionGridBounds();
                    }
                    finally
                    {
                        _stickyActionLayoutUpdatePending = false;
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                _stickyActionLayoutUpdatePending = false;
            }
        }

        private void UpdateStickyActionGridBounds()
        {
            if (_historyGrid == null || _stickyActionGrid == null
                || _historyGrid.ClientSize.Width <= 0 || _historyGrid.ClientSize.Height <= 0)
                return;

            bool geometryChanged = false;
            int dpi = _historyGrid.DeviceDpi <= 0 ? 96 : _historyGrid.DeviceDpi;
            int overlayWidth = ScaleLogical(GetStickyActionLogicalWidth(), dpi);
            if (_historyGrid.Columns.Contains("stickyActionSpacer"))
            {
                DataGridViewColumn spacer = _historyGrid.Columns["stickyActionSpacer"];
                if (spacer.Width != overlayWidth)
                {
                    spacer.Width = overlayWidth;
                    geometryChanged = true;
                }
            }

            int verticalScrollbarWidth = 0;
            int horizontalScrollbarHeight = 0;
            foreach (Control control in _historyGrid.Controls)
            {
                var vertical = control as VScrollBar;
                if (vertical != null && vertical.Visible)
                    verticalScrollbarWidth = Math.Max(verticalScrollbarWidth, vertical.Width);
                var horizontal = control as HScrollBar;
                if (horizontal != null && horizontal.Visible)
                    horizontalScrollbarHeight = Math.Max(horizontalScrollbarHeight, horizontal.Height);
            }

            int right = Math.Max(0, _historyGrid.ClientSize.Width - verticalScrollbarWidth);
            int height = Math.Max(0, _historyGrid.ClientSize.Height - horizontalScrollbarHeight);
            int width = Math.Min(overlayWidth, right);
            Rectangle desired = new Rectangle(Math.Max(0, right - width), 0, width, height);
            if (_stickyActionGrid.Bounds != desired)
            {
                _stickyActionGrid.Bounds = desired;
                geometryChanged = true;
            }
            if (geometryChanged)
            {
                _stickyActionGrid.BringToFront();
                _stickyActionGrid.Invalidate();
                if (_historyScrollCorner != null)
                    _historyScrollCorner.RefreshBounds();
            }
        }

        private void SyncStickyActionRows()
        {
            if (_historyGrid == null || _stickyActionGrid == null || _syncingStickyActionGrid)
                return;
            _syncingStickyActionGrid = true;
            try
            {
                string currentColumnName = _stickyActionGrid.CurrentCell == null
                    ? "blockAction"
                    : _stickyActionGrid.Columns[_stickyActionGrid.CurrentCell.ColumnIndex].Name;
                _stickyActionGrid.Rows.Clear();
                foreach (DataGridViewRow historyRow in _historyGrid.Rows)
                {
                    int rowIndex = _stickyActionGrid.Rows.Add();
                    DataGridViewRow actionRow = _stickyActionGrid.Rows[rowIndex];
                    actionRow.Tag = historyRow.Tag;
                    actionRow.Height = historyRow.Height;
                    CopyStickyActionCellState(historyRow, actionRow, "blockAction", true);
                    CopyStickyActionCellState(historyRow, actionRow, "unblockAction", false);
                }
                SyncStickySelectionFromHistoryCore(currentColumnName);
                SyncStickyActionVerticalScrollCore();
            }
            finally
            {
                _syncingStickyActionGrid = false;
            }
            UpdateStickyActionGridBounds();
        }

        private void UpdateStickyActionRow(DataGridViewRow historyRow)
        {
            if (historyRow == null || _stickyActionGrid == null || _syncingStickyActionGrid)
                return;
            if (_stickyActionGrid.Rows.Count != _historyGrid.Rows.Count)
                return;
            DataGridViewRow actionRow = FindStickyActionRow(
                historyRow.Tag as ServerSession,
                historyRow.Index);
            if (actionRow == null) return;
            actionRow.Tag = historyRow.Tag;
            actionRow.Height = historyRow.Height;
            CopyStickyActionCellState(historyRow, actionRow, "blockAction", true);
            CopyStickyActionCellState(historyRow, actionRow, "unblockAction", false);
            _stickyActionGrid.InvalidateRow(actionRow.Index);
        }

        private static void CopyStickyActionCellState(
            DataGridViewRow historyRow,
            DataGridViewRow actionRow,
            string columnName,
            bool isBlock)
        {
            DataGridViewCell source = historyRow.Cells[columnName];
            DataGridViewCell target = actionRow.Cells[columnName];
            bool enabled = source.Tag is bool && (bool)source.Tag;
            Color background = enabled ? (isBlock ? Danger : Success) : Color.FromArgb(37, 44, 53);
            Color foreground = enabled
                ? (isBlock ? Color.White : Color.FromArgb(18, 50, 34))
                : TextMuted;
            target.Tag = enabled;
            target.ToolTipText = source.ToolTipText;
            target.Style.BackColor = background;
            target.Style.ForeColor = foreground;
            target.Style.SelectionBackColor = background;
            target.Style.SelectionForeColor = foreground;
        }

        private void SyncStickySelectionFromHistory()
        {
            if (_syncingStickyActionGrid || _historyGrid == null || _stickyActionGrid == null)
                return;
            _syncingStickyActionGrid = true;
            try
            {
                string currentColumnName = _stickyActionGrid.CurrentCell == null
                    ? "blockAction"
                    : _stickyActionGrid.Columns[_stickyActionGrid.CurrentCell.ColumnIndex].Name;
                SyncStickySelectionFromHistoryCore(currentColumnName);
                SyncStickyActionVerticalScrollCore();
            }
            finally
            {
                _syncingStickyActionGrid = false;
            }
        }

        private void SyncStickySelectionFromHistoryCore(string preferredColumnName)
        {
            if (_historyGrid == null || _stickyActionGrid == null) return;
            DataGridViewRow historyRow = _historyGrid.SelectedRows
                .Cast<DataGridViewRow>()
                .OrderBy(row => row.Index)
                .FirstOrDefault();
            if (historyRow == null) historyRow = _historyGrid.CurrentRow;
            _stickyActionGrid.ClearSelection();
            if (historyRow == null
                || historyRow.Index < 0
                || historyRow.Index >= _stickyActionGrid.Rows.Count)
                return;
            string columnName = _stickyActionGrid.Columns.Contains(preferredColumnName)
                ? preferredColumnName
                : "blockAction";
            DataGridViewRow actionRow = FindStickyActionRow(
                historyRow.Tag as ServerSession,
                historyRow.Index);
            if (actionRow == null) return;
            _stickyActionGrid.CurrentCell = actionRow.Cells[columnName];
            actionRow.Selected = true;
        }

        private void SyncHistorySelectionFromStickyActions()
        {
            if (_syncingStickyActionGrid || _historyGrid == null || _stickyActionGrid == null
                || _stickyActionGrid.CurrentCell == null)
                return;
            int actionRowIndex = _stickyActionGrid.CurrentCell.RowIndex;
            if (actionRowIndex < 0 || actionRowIndex >= _stickyActionGrid.Rows.Count) return;
            var actionSession = _stickyActionGrid.Rows[actionRowIndex].Tag as ServerSession;
            DataGridViewRow historyRow = FindHistoryRow(actionSession, actionRowIndex);
            if (historyRow == null) return;
            _syncingStickyActionGrid = true;
            try
            {
                int horizontalOffset = _historyGrid.HorizontalScrollingOffset;
                _historyGrid.ClearSelection();
                historyRow.Selected = true;
                int columnIndex = GetHistoryCurrentColumnIndex();
                if (columnIndex >= 0)
                    _historyGrid.CurrentCell = historyRow.Cells[columnIndex];
                try { _historyGrid.HorizontalScrollingOffset = horizontalOffset; }
                catch { }
                var session = historyRow.Tag as ServerSession;
                if (session != null) SelectSession(session);
                SyncHistoryVerticalScrollToRow(historyRow.Index);
            }
            finally
            {
                _syncingStickyActionGrid = false;
            }
        }

        private int GetHistoryCurrentColumnIndex()
        {
            int columnIndex = _historyGrid.CurrentCell == null
                ? _historyGrid.FirstDisplayedScrollingColumnIndex
                : _historyGrid.CurrentCell.ColumnIndex;
            if (columnIndex >= 0 && columnIndex < _historyGrid.Columns.Count
                && _historyGrid.Columns[columnIndex].Visible
                && !string.Equals(
                    _historyGrid.Columns[columnIndex].Name,
                    "stickyActionSpacer",
                    StringComparison.Ordinal))
                return columnIndex;
            DataGridViewColumn first = _historyGrid.Columns
                .Cast<DataGridViewColumn>()
                .Where(column => column.Visible
                    && !string.Equals(column.Name, "stickyActionSpacer", StringComparison.Ordinal))
                .OrderBy(column => column.DisplayIndex)
                .FirstOrDefault();
            return first == null ? -1 : first.Index;
        }

        private DataGridViewRow FindStickyActionRow(ServerSession session, int fallbackIndex)
        {
            if (_stickyActionGrid == null) return null;
            DataGridViewRow matched = _stickyActionGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row => SameSessionKey(row.Tag as ServerSession, session));
            if (matched != null) return matched;
            if (session != null && !string.IsNullOrWhiteSpace(session.SessionKey))
                return null;
            return fallbackIndex >= 0 && fallbackIndex < _stickyActionGrid.Rows.Count
                ? _stickyActionGrid.Rows[fallbackIndex]
                : null;
        }

        private DataGridViewRow FindHistoryRow(ServerSession session, int fallbackIndex)
        {
            if (_historyGrid == null) return null;
            DataGridViewRow matched = _historyGrid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row => SameSessionKey(row.Tag as ServerSession, session));
            if (matched != null) return matched;
            if (session != null && !string.IsNullOrWhiteSpace(session.SessionKey))
                return null;
            return fallbackIndex >= 0 && fallbackIndex < _historyGrid.Rows.Count
                ? _historyGrid.Rows[fallbackIndex]
                : null;
        }

        private static bool SameSessionKey(ServerSession left, ServerSession right)
        {
            if (left == null || right == null) return false;
            if (!string.IsNullOrWhiteSpace(left.SessionKey)
                && !string.IsNullOrWhiteSpace(right.SessionKey))
                return string.Equals(left.SessionKey, right.SessionKey, StringComparison.Ordinal);
            return ReferenceEquals(left, right);
        }

        private void SyncHistoryVerticalScrollToRow(int rowIndex)
        {
            if (_historyGrid == null || rowIndex < 0 || rowIndex >= _historyGrid.Rows.Count) return;
            if (!_historyGrid.Rows[rowIndex].Displayed)
            {
                try { _historyGrid.FirstDisplayedScrollingRowIndex = rowIndex; }
                catch { }
            }
            SyncStickyActionVerticalScrollCore();
        }

        private void SyncStickyActionVerticalScroll()
        {
            if (_syncingStickyActionGrid || _historyGrid == null || _stickyActionGrid == null)
                return;
            _syncingStickyActionGrid = true;
            try { SyncStickyActionVerticalScrollCore(); }
            finally { _syncingStickyActionGrid = false; }
        }

        private void SyncStickyActionVerticalScrollCore()
        {
            if (_historyGrid == null || _stickyActionGrid == null || _stickyActionGrid.Rows.Count == 0)
                return;
            int firstRow;
            try { firstRow = _historyGrid.FirstDisplayedScrollingRowIndex; }
            catch { return; }
            if (firstRow < 0 || firstRow >= _stickyActionGrid.Rows.Count) return;
            try
            {
                if (_stickyActionGrid.FirstDisplayedScrollingRowIndex != firstRow)
                    _stickyActionGrid.FirstDisplayedScrollingRowIndex = firstRow;
            }
            catch { }
        }

        private async Task StickyActionGridCellContentClickAsync(DataGridViewCellEventArgs args)
        {
            if (args == null || args.RowIndex < 0 || args.ColumnIndex < 0
                || _stickyActionGrid == null || _historyGrid == null
                || args.RowIndex >= _historyGrid.Rows.Count)
                return;
            string columnName = _stickyActionGrid.Columns[args.ColumnIndex].Name;
            PingKickAction action;
            if (string.Equals(columnName, "blockAction", StringComparison.Ordinal))
                action = PingKickAction.Block;
            else if (string.Equals(columnName, "unblockAction", StringComparison.Ordinal))
                action = PingKickAction.Unblock;
            else
                return;
            SyncHistorySelectionFromStickyActions();
            var actionSession = _stickyActionGrid.Rows[args.RowIndex].Tag as ServerSession;
            DataGridViewRow historyRow = FindHistoryRow(actionSession, args.RowIndex);
            if (historyRow == null) return;
            var session = historyRow.Tag as ServerSession;
            if (session != null)
                await ExecuteHistoryConnectionActionAsync(historyRow, session, action);
        }

        private void ScrollHistoryRowsFromStickyActions(int wheelDelta)
        {
            if (_historyGrid == null || _historyGrid.Rows.Count == 0 || wheelDelta == 0) return;
            int current;
            try { current = _historyGrid.FirstDisplayedScrollingRowIndex; }
            catch { return; }
            if (current < 0) return;
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines == -1)
                lines = Math.Max(1, _historyGrid.DisplayedRowCount(false));
            else
                lines = Math.Max(1, lines);
            int notches = Math.Max(1, Math.Abs(wheelDelta) / SystemInformation.MouseWheelScrollDelta);
            int direction = wheelDelta > 0 ? -1 : 1;
            int target = Math.Max(
                0,
                Math.Min(_historyGrid.Rows.Count - 1, current + (direction * lines * notches)));
            if (target == current) return;
            try { _historyGrid.FirstDisplayedScrollingRowIndex = target; }
            catch { }
            SyncStickyActionVerticalScroll();
        }

        private string AppendRegionFilterSummary(string message)
        {
            if (_selectedRegionCodes.Count == 0) return message;
            return message + AppText.Format(
                "Main.Summary.RegionFilterRatio",
                _visibleSessions.Count,
                _regionSourceCount);
        }

        private void SetGameFilter(TarkovGame? game)
        {
            if (_isRefreshing || _isMeasuring || _isFirewallChanging) return;
            _gameFilter = game;
            UpdateFilterButtons();
            RefreshVisibleSessions();
            if (_visibleSessions.Count > 0) SetHistorySummaryStatus(TextMuted, false);
        }

        private void ShowRegionFilterMenu()
        {
            if (_regionFilterButton == null || !_regionFilterButton.Enabled) return;
            if (_regionFilterMenu != null)
            {
                _regionFilterMenu.Dispose();
                _regionFilterMenu = null;
            }

            IList<ServerSession> source = GetRegionFilterSourceSessions();
            var counts = source
                .GroupBy(session => DataCenterRegionClassifier.GetRegionCode(
                    session == null ? null : session.DataCenterCode),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            IList<string> regionCodes = counts.Keys
                .Concat(_selectedRegionCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(DataCenterRegionClassifier.GetSortOrder)
                .ThenBy(AppText.GetRegionDisplayLabel, StringComparer.CurrentCulture)
                .ToList();

            _regionFilterMenu = new ContextMenuStrip
            {
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Font = new Font("Malgun Gothic", 8.5F),
                ShowCheckMargin = true,
                ShowImageMargin = false,
                Renderer = new ToolStripProfessionalRenderer(new RegionMenuColorTable())
            };
            _regionFilterMenu.AccessibleName = AppText.Get("Main.Region.MenuAccessibleName");
            _regionFilterMenu.Closing += delegate(object sender, ToolStripDropDownClosingEventArgs args)
            {
                if (args.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                    args.Cancel = true;
            };

            var allItem = new ToolStripMenuItem(AppText.Get("Main.Region.All"))
            {
                CheckOnClick = false,
                Checked = _selectedRegionCodes.Count == 0,
                BackColor = SurfaceAlt,
                ForeColor = TextPrimary,
                Tag = string.Empty
            };
            allItem.Click += delegate
            {
                _selectedRegionCodes.Clear();
                ApplyRegionFilterChange();
                UpdateRegionMenuChecks();
            };
            _regionFilterMenu.Items.Add(allItem);
            _regionFilterMenu.Items.Add(new ToolStripSeparator());

            if (regionCodes.Count == 0)
            {
                _regionFilterMenu.Items.Add(new ToolStripMenuItem(AppText.Get("Main.Region.None"))
                {
                    Enabled = false,
                    BackColor = SurfaceAlt,
                    ForeColor = TextMuted
                });
            }
            else
            {
                foreach (string regionCode in regionCodes)
                {
                    int count;
                    counts.TryGetValue(regionCode, out count);
                    var item = new ToolStripMenuItem(AppText.Format(
                        "Main.Region.ItemCount",
                        AppText.GetRegionDisplayLabel(regionCode),
                        count))
                    {
                        CheckOnClick = true,
                        Checked = _selectedRegionCodes.Contains(regionCode),
                        BackColor = SurfaceAlt,
                        ForeColor = TextPrimary,
                        Tag = regionCode
                    };
                    item.Click += delegate(object sender, EventArgs args)
                    {
                        var selectedItem = sender as ToolStripMenuItem;
                        string code = selectedItem == null ? null : selectedItem.Tag as string;
                        if (string.IsNullOrWhiteSpace(code)) return;
                        if (selectedItem.Checked)
                            _selectedRegionCodes.Add(code);
                        else
                            _selectedRegionCodes.Remove(code);
                        ApplyRegionFilterChange();
                        UpdateRegionMenuChecks();
                    };
                    _regionFilterMenu.Items.Add(item);
                }
            }

            _regionFilterMenu.Show(_regionFilterButton, new Point(0, _regionFilterButton.Height));
        }

        private void UpdateRegionMenuChecks()
        {
            if (_regionFilterMenu == null) return;
            foreach (ToolStripItem rawItem in _regionFilterMenu.Items)
            {
                var item = rawItem as ToolStripMenuItem;
                if (item == null) continue;
                if (!(item.Tag is string)) continue;
                string code = item.Tag as string;
                item.Checked = string.IsNullOrEmpty(code)
                    ? _selectedRegionCodes.Count == 0
                    : _selectedRegionCodes.Contains(code);
            }
        }

        private void ApplyRegionFilterChange()
        {
            UpdateRegionFilterButton();
            RefreshVisibleSessions();
            SetHistorySummaryStatus(TextMuted, false);
        }

        private IList<ServerSession> GetRegionFilterSourceSessions()
        {
            return _allSessions
                .Where(item => !_gameFilter.HasValue || item.Game == _gameFilter.Value)
                .Where(IsSessionInSelectedPeriod)
                .OrderByDescending(item => item.DisplayDetectedAt)
                .ThenByDescending(item => item.LastUpdated)
                .Take(100)
                .ToList();
        }

        private void UpdateRegionFilterButton()
        {
            if (_regionFilterButton == null) return;
            string text;
            if (_selectedRegionCodes.Count == 0)
            {
                text = AppText.Get("Main.Filter.RegionAll");
            }
            else if (_selectedRegionCodes.Count == 1)
            {
                string regionCode = _selectedRegionCodes.First();
                bool isLocalPveOrUnknown = string.Equals(
                    regionCode,
                    DataCenterRegionClassifier.UnknownCode,
                    StringComparison.OrdinalIgnoreCase);
                string displayName = isLocalPveOrUnknown
                    ? AppText.Get("Region.Name.LocalOrOther")
                    : AppText.GetRegionDisplayName(regionCode);
                string candidate = AppText.Format("Main.Region.ButtonOne", displayName);
                int availableTextWidth = Math.Max(
                    1,
                    _regionFilterButton.ClientSize.Width - ScaleLogical(
                        14,
                        _regionFilterButton.DeviceDpi <= 0 ? 96 : _regionFilterButton.DeviceDpi));
                Size measured = TextRenderer.MeasureText(
                    candidate,
                    _regionFilterButton.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                string compactName = isLocalPveOrUnknown ? AppText.Get("Main.Region.LocalOrOtherCompact") : regionCode;
                text = measured.Width <= availableTextWidth
                    ? candidate
                    : AppText.Format("Main.Region.ButtonOne", compactName);
            }
            else
            {
                text = AppText.Format("Main.Region.ButtonCount", _selectedRegionCodes.Count);
            }
            _regionFilterButton.Text = text;
            StyleFilterButton(_regionFilterButton, _selectedRegionCodes.Count > 0);
            _toolTip.SetToolTip(
                _regionFilterButton,
                _selectedRegionCodes.Count == 0
                    ? AppText.Get("Main.Region.AllTooltip")
                    : AppText.Format("Main.Region.SelectedTooltip", string.Join(", ", _selectedRegionCodes
                        .OrderBy(DataCenterRegionClassifier.GetSortOrder)
                        .Select(code => string.Equals(
                            code,
                            DataCenterRegionClassifier.UnknownCode,
                            StringComparison.OrdinalIgnoreCase)
                                ? AppText.Get("Region.Name.LocalOrOther")
                                : AppText.GetRegionDisplayLabel(code)))));
        }

        private void UpdateFilterButtons()
        {
            if (_allFilterButton == null) return;
            StyleFilterButton(_allFilterButton, !_gameFilter.HasValue);
            StyleFilterButton(_eftFilterButton, _gameFilter == TarkovGame.Eft);
            StyleFilterButton(_arenaFilterButton, _gameFilter == TarkovGame.Arena);
            UpdateRegionFilterButton();
        }

        private static void StyleFilterButton(Button button, bool selected)
        {
            button.BackColor = selected ? Accent : SurfaceAlt;
            button.ForeColor = selected ? Color.FromArgb(29, 24, 17) : TextPrimary;
            button.FlatAppearance.BorderColor = selected ? Accent : Border;
        }

        private void RefreshVisibleSessions()
        {
            RefreshVisibleSessionsAfterScan(null);
        }

        private ServerSession RefreshVisibleSessionsAfterScan(
            ISet<string> preferredNewSessionIdentities)
        {
            bool preserveViewportWhenNoNewSession = preferredNewSessionIdentities != null;
            string viewportAnchorIdentity;
            int viewportAnchorIndex;
            int horizontalOffset;
            CaptureHistoryViewport(
                out viewportAnchorIdentity,
                out viewportAnchorIndex,
                out horizontalOffset);
            string selectedIdentity = _selectedSession == null
                ? null
                : GetSessionRefreshIdentity(_selectedSession);
            IList<ServerSession> matchingSessions = _allSessions
                .Where(item => !_gameFilter.HasValue || item.Game == _gameFilter.Value)
                .Where(IsSessionInSelectedPeriod)
                .OrderByDescending(item => item.DisplayDetectedAt)
                .ToList();
            _periodMatchCount = matchingSessions.Count;
            _periodResultsTruncated = _periodMatchCount > 100;
            IList<ServerSession> latestSessions = matchingSessions.Take(100).ToList();
            _regionSourceCount = latestSessions.Count;
            IEnumerable<ServerSession> regionFilteredSessions = _selectedRegionCodes.Count == 0
                ? latestSessions
                : latestSessions.Where(session => _selectedRegionCodes.Contains(
                    DataCenterRegionClassifier.GetRegionCode(
                        session == null ? null : session.DataCenterCode)));
            _visibleSessions = ApplyHistorySort(regionFilteredSessions.ToList());
            PopulateHistoryGrid(_visibleSessions);
            UpdateHistorySortGlyph();

            ServerSession newestVisibleNewSession = preferredNewSessionIdentities == null
                ? null
                : _visibleSessions
                    .Where(item => preferredNewSessionIdentities.Contains(
                        GetSessionRefreshIdentity(item)))
                    .OrderByDescending(item => item.DisplayDetectedAt)
                    .ThenByDescending(item => item.LastUpdated)
                    .ThenBy(GetSessionRefreshIdentity, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            ServerSession desired = newestVisibleNewSession
                ?? _visibleSessions.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(selectedIdentity)
                    && string.Equals(
                        GetSessionRefreshIdentity(item),
                        selectedIdentity,
                        StringComparison.OrdinalIgnoreCase));
            if (desired == null) desired = _visibleSessions.FirstOrDefault();
            if (desired == null)
                ShowNoServer(AddLogScanWarning(AppText.Get("Main.Logs.NoFilteredConnections")));
            else
            {
                SelectGridRow(
                    desired,
                    newestVisibleNewSession != null || !preserveViewportWhenNoNewSession);
                SelectSession(desired);
            }
            if (preserveViewportWhenNoNewSession && newestVisibleNewSession == null)
                RestoreHistoryViewport(
                    viewportAnchorIdentity,
                    viewportAnchorIndex,
                    horizontalOffset);
            UpdateActionButtons();
            return newestVisibleNewSession;
        }

        private void HistoryGridColumnHeaderMouseClick(
            object sender,
            DataGridViewCellMouseEventArgs args)
        {
            if (_historyGrid == null || args.Button != MouseButtons.Left
                || args.ColumnIndex < 0) return;
            DataGridViewColumn column = _historyGrid.Columns[args.ColumnIndex];
            if (column.SortMode != DataGridViewColumnSortMode.Programmatic) return;

            CycleHistorySort(column.Name);
        }

        private void StickyActionGridColumnHeaderMouseClick(
            object sender,
            DataGridViewCellMouseEventArgs args)
        {
            if (_stickyActionGrid == null || args.Button != MouseButtons.Left
                || args.ColumnIndex < 0) return;
            DataGridViewColumn column = _stickyActionGrid.Columns[args.ColumnIndex];
            if (column.SortMode != DataGridViewColumnSortMode.Programmatic) return;

            CycleHistorySort(column.Name);
        }

        private void CycleHistorySort(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName)) return;

            bool restoreDefaultOrder = false;
            if (string.Equals(_historySortColumn, columnName, StringComparison.Ordinal))
            {
                if (_historySortOrder == SortOrder.Ascending)
                {
                    _historySortOrder = SortOrder.Descending;
                }
                else
                {
                    _historySortColumn = null;
                    _historySortOrder = SortOrder.None;
                    restoreDefaultOrder = true;
                }
            }
            else
            {
                _historySortColumn = columnName;
                _historySortOrder = SortOrder.Ascending;
            }

            if (IsFirewallActionSortColumn(columnName))
            {
                // Keep the selected raid, but show the beginning of the newly sorted
                // result. Revealing the previous selection here can scroll the leading
                // action group above the viewport and make a successful sort look inert.
                ReapplyHistorySort(false, true);
            }
            else if (restoreDefaultOrder)
                RefreshVisibleSessions();
            else
                ReapplyHistorySort();
        }

        private void ReapplyHistorySort(
            bool scrollSelectedIntoView = true,
            bool resetViewportToTop = false)
        {
            if (_historyGrid == null || _visibleSessions == null) return;
            string viewportAnchorIdentity;
            int viewportAnchorIndex;
            int horizontalOffset;
            CaptureHistoryViewport(
                out viewportAnchorIdentity,
                out viewportAnchorIndex,
                out horizontalOffset);
            string selectedIdentity = _selectedSession == null
                ? null
                : GetSessionRefreshIdentity(_selectedSession);
            _visibleSessions = ApplyHistorySort(_visibleSessions);
            PopulateHistoryGrid(_visibleSessions);
            UpdateHistorySortGlyph();

            ServerSession selected = _visibleSessions.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(selectedIdentity)
                && string.Equals(
                    GetSessionRefreshIdentity(item),
                    selectedIdentity,
                    StringComparison.OrdinalIgnoreCase));
            if (selected != null)
                SelectGridRow(selected, scrollSelectedIntoView && !resetViewportToTop);
            if (resetViewportToTop)
                RestoreHistoryViewport(null, 0, horizontalOffset);
            else if (!scrollSelectedIntoView)
                RestoreHistoryViewport(
                    viewportAnchorIdentity,
                    viewportAnchorIndex,
                    horizontalOffset);
        }

        private IList<ServerSession> ApplyHistorySort(IEnumerable<ServerSession> sessions)
        {
            var result = sessions == null
                ? new List<ServerSession>()
                : sessions.Where(item => item != null).ToList();
            if (string.IsNullOrWhiteSpace(_historySortColumn)
                || _historySortOrder == SortOrder.None)
                return result
                    .OrderByDescending(item => item.DisplayDetectedAt)
                    .ThenBy(item => item.SessionKey ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            result.Sort(CompareHistorySessions);
            return result;
        }

        private int CompareHistorySessions(ServerSession left, ServerSession right)
        {
            bool leftKnown;
            bool rightKnown;
            int primary = CompareHistorySortValue(
                left,
                right,
                out leftKnown,
                out rightKnown);
            if (leftKnown != rightKnown) return leftKnown ? -1 : 1;
            if (leftKnown && primary != 0)
            {
                int normalized = primary < 0 ? -1 : 1;
                return _historySortOrder == SortOrder.Descending ? -normalized : normalized;
            }

            int dateTieBreak = DateTime.Compare(right.DisplayDetectedAt, left.DisplayDetectedAt);
            bool reverseActionTieBreak = IsFirewallActionSortColumn(_historySortColumn)
                && _historySortOrder == SortOrder.Descending;
            if (dateTieBreak != 0)
                return reverseActionTieBreak ? -dateTieBreak : dateTieBreak;
            int identityTieBreak = StringComparer.OrdinalIgnoreCase.Compare(
                left.SessionKey ?? string.Empty,
                right.SessionKey ?? string.Empty);
            return reverseActionTieBreak ? -identityTieBreak : identityTieBreak;
        }

        private int CompareHistorySortValue(
            ServerSession left,
            ServerSession right,
            out bool leftKnown,
            out bool rightKnown)
        {
            leftKnown = false;
            rightKnown = false;
            switch (_historySortColumn)
            {
                case "time":
                    leftKnown = left.DisplayDetectedAt != default(DateTime);
                    rightKnown = right.DisplayDetectedAt != default(DateTime);
                    return DateTime.Compare(left.DisplayDetectedAt, right.DisplayDetectedAt);
                case "userReport":
                    leftKnown = true;
                    rightKnown = true;
                    return left.UserReportCount.CompareTo(right.UserReportCount);
                case "note":
                    leftKnown = true;
                    rightKnown = true;
                    return _noteStore.Exists(left).CompareTo(_noteStore.Exists(right));
                case "blockAction":
                case "unblockAction":
                    bool leftActionAvailable;
                    bool rightActionAvailable;
                    bool isUnblockAction = string.Equals(
                        _historySortColumn,
                        "unblockAction",
                        StringComparison.Ordinal);
                    leftKnown = TryGetFirewallActionSortValue(
                        left,
                        isUnblockAction,
                        out leftActionAvailable);
                    rightKnown = TryGetFirewallActionSortValue(
                        right,
                        isUnblockAction,
                        out rightActionAvailable);
                    // For an action column the useful first view is the action the
                    // user can currently perform. Treat that as the ascending lead;
                    // the second click reverses the two known groups.
                    return rightActionAvailable.CompareTo(leftActionAvailable);
                case "ping":
                    double leftPing;
                    double rightPing;
                    leftKnown = TryGetSortablePing(left, out leftPing);
                    rightKnown = TryGetSortablePing(right, out rightPing);
                    return leftPing.CompareTo(rightPing);
                case "actualRtt":
                    double leftActualRtt;
                    double rightActualRtt;
                    leftKnown = RaidMetricPresentation.TryGetActualRtt(left, out leftActualRtt);
                    rightKnown = RaidMetricPresentation.TryGetActualRtt(right, out rightActualRtt);
                    return leftActualRtt.CompareTo(rightActualRtt);
                case "packetLoss":
                    double leftPacketLoss;
                    double rightPacketLoss;
                    leftKnown = RaidMetricPresentation.TryGetPacketLoss(left, out leftPacketLoss);
                    rightKnown = RaidMetricPresentation.TryGetPacketLoss(right, out rightPacketLoss);
                    return leftPacketLoss.CompareTo(rightPacketLoss);
                default:
                    string leftText = GetHistorySortText(left, _historySortColumn, out leftKnown);
                    string rightText = GetHistorySortText(right, _historySortColumn, out rightKnown);
                    return StringComparer.CurrentCultureIgnoreCase.Compare(leftText, rightText);
            }
        }

        private bool TryGetFirewallActionSortValue(
            ServerSession session,
            bool isUnblockAction,
            out bool actionAvailable)
        {
            FirewallActionPresentationState state =
                GetFirewallActionPresentationState(session);
            actionAvailable = isUnblockAction
                ? state.UnblockAvailable
                : state.BlockAvailable;
            return state.SortKnown;
        }

        private FirewallActionPresentationState GetFirewallActionPresentationState(
            ServerSession session)
        {
            var state = new FirewallActionPresentationState();
            if (session == null || !session.HasServerIp) return state;

            FirewallQueryResult firewall;
            state.HasResult = _firewallStates.TryGetValue(
                session.IpAddress,
                out firewall);
            state.Known = state.HasResult
                && firewall != null
                && firewall.Success;
            state.Busy = _firewallBusyIpAddresses.Contains(session.IpAddress)
                || _isFirewallChanging;
            state.IsBlocked = state.Known && firewall.IsBlocked;
            bool canChange = state.Known
                && !state.Busy
                && !_isMeasuring
                && !_isRefreshing;
            state.BlockAvailable = canChange && !state.IsBlocked;
            state.UnblockAvailable = canChange && state.IsBlocked;
            return state;
        }

        private static bool IsFirewallActionSortColumn(string columnName)
        {
            return string.Equals(columnName, "blockAction", StringComparison.Ordinal)
                || string.Equals(columnName, "unblockAction", StringComparison.Ordinal);
        }

        private bool TryGetSortablePing(ServerSession session, out double averageMs)
        {
            averageMs = 0;
            if (session == null || !session.HasServerIp) return false;
            FirewallQueryResult firewall;
            if (_firewallStates.TryGetValue(session.IpAddress, out firewall)
                && firewall != null
                && firewall.Success
                && firewall.IsBlocked)
                return false;
            PingResult ping;
            if (!_pingResults.TryGetValue(session.IpAddress, out ping)
                || ping == null
                || !ping.IsAvailable)
                return false;
            averageMs = ping.AverageMs;
            return true;
        }

        private string GetHistorySortText(ServerSession session, string columnName, out bool known)
        {
            known = false;
            if (session == null) return string.Empty;
            string value;
            switch (columnName)
            {
                case "game":
                    value = session.GameDisplayName;
                    break;
                case "mapMode":
                    if (string.IsNullOrWhiteSpace(session.MapName)) return string.Empty;
                    value = GetMapAndTypeText(session);
                    break;
                case "ip":
                    if (!session.HasServerIp) return string.Empty;
                    value = session.IpAddress;
                    break;
                case "location":
                    if (!session.HasServerIp) return string.Empty;
                    GeoInfo geo;
                    if (!_geoResults.TryGetValue(session.IpAddress, out geo)
                        || geo == null
                        || !geo.Success)
                        return string.Empty;
                    value = GetLocationCellText(session, geo);
                    break;
                case "result":
                    value = FormatConnectionResultCell(session);
                    break;
                default:
                    return string.Empty;
            }
            known = !string.IsNullOrWhiteSpace(value) && value != "-";
            return known ? value : string.Empty;
        }

        private void UpdateHistorySortGlyph()
        {
            if (_historyGrid == null) return;
            foreach (DataGridViewColumn column in _historyGrid.Columns)
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            _historyGrid.Invalidate();
            if (_stickyActionGrid != null)
            {
                foreach (DataGridViewColumn column in _stickyActionGrid.Columns)
                    column.HeaderCell.SortGlyphDirection = SortOrder.None;
                _stickyActionGrid.Invalidate();
            }
        }

        private void PopulateHistoryGrid(IList<ServerSession> sessions)
        {
            _historyGrid.Rows.Clear();
            foreach (ServerSession session in sessions)
            {
                PingResult ping;
                GeoInfo geo;
                FirewallQueryResult firewall;
                _pingResults.TryGetValue(session.IpAddress ?? string.Empty, out ping);
                _geoResults.TryGetValue(session.IpAddress ?? string.Empty, out geo);
                _firewallStates.TryGetValue(session.IpAddress ?? string.Empty, out firewall);
                bool hasNote = _noteStore.Exists(session);
                string mapAndTypeText = GetMapAndTypeText(session);
                int rowIndex = _historyGrid.Rows.Add(
                    session.GameDisplayName,
                    session.DisplayDetectedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    session.UserReportCount > 0 ? AppText.Format("Main.Row.PlayerReportCount", session.UserReportCount) : string.Empty,
                    string.Empty,
                    mapAndTypeText,
                    session.HasServerIp ? session.IpAddress : "-",
                    GetLocationCellText(session, geo),
                    GetPingCellText(session, ping, firewall),
                    FormatActualRttDisplay(session),
                    FormatPacketLossDisplay(session),
                    AppText.Get("Common.Button.Block"),
                    AppText.Get("Main.Column.Unblock"),
                    FormatConnectionResultCell(session));
                DataGridViewRow row = _historyGrid.Rows[rowIndex];
                row.Tag = session;
                row.Cells["mapMode"].ToolTipText = mapAndTypeText;
                row.Cells["userReport"].ToolTipText = session.UserReportCount > 0
                    ? AppText.Get("Main.Row.PlayerReportTooltip")
                    : string.Empty;
                row.Cells["result"].ToolTipText = GetConnectionResultHelp(session);
                row.Cells["actualRtt"].ToolTipText = GetActualRttHelp(session);
                row.Cells["packetLoss"].ToolTipText = GetPacketLossHelp(session);
                ApplyResultRowStyle(row, session, ping, geo, firewall);
                ApplyMissingMetricCellFonts(row);
                UpdateNoteCell(row, hasNote);
                UpdateActionCells(row);
            }
            SyncStickyActionRows();
        }

        private static string GetMapAndTypeText(ServerSession session)
        {
            if (session == null) return "-";
            string map = string.IsNullOrWhiteSpace(session.MapName) ? "-" : session.MapName;
            if (session.Game == TarkovGame.Eft)
                return map + " · " + AppText.LocalizeDomainDisplay(session.RaidTypeAndParticipantText);
            return string.IsNullOrWhiteSpace(session.GameMode)
                ? map
                : map + " · " + session.GameMode;
        }

        private static string FormatConnectionResultCell(ServerSession session)
        {
            if (session == null) return "-";
            string result = FormatConnectionResult(session);
            return string.Equals(AppText.CurrentLanguage, AppText.KoreanLanguage, StringComparison.Ordinal)
                ? result.Replace(" · ", "·")
                : result;
        }

        private enum ConnectionPresentationState
        {
            Normal,
            Abnormal,
            Failed,
            NoRecord,
            LogUnavailable,
            NotApplicable
        }

        private static ConnectionPresentationState GetConnectionPresentationState(
            ServerSession session)
        {
            if (session == null) return ConnectionPresentationState.LogUnavailable;
            if (session.HostingMode == TarkovHostingMode.Local)
                return ConnectionPresentationState.NotApplicable;

            bool currentAttemptConnected = session.CurrentAttemptConnected
                || (session.ConnectionAttemptKeys == null && session.ConnectedOnce);
            bool hasSuccessfulConnection = session.ConnectedOnce
                || session.CurrentAttemptConnected;
            if (session.TimedOut)
                return hasSuccessfulConnection
                    ? ConnectionPresentationState.Abnormal
                    : ConnectionPresentationState.Failed;
            if (!currentAttemptConnected && session.HasDisconnectRecord)
                return ConnectionPresentationState.Failed;
            if (session.ConnectionAttempts <= 0)
                return ConnectionPresentationState.NoRecord;
            if (!currentAttemptConnected)
                return ConnectionPresentationState.LogUnavailable;
            if (session.HasDisconnectRecord && session.DisconnectReason.HasValue)
                return session.DisconnectReason.Value == 0
                    ? ConnectionPresentationState.Normal
                    : ConnectionPresentationState.Abnormal;
            return ConnectionPresentationState.LogUnavailable;
        }

        private static string FormatConnectionResult(ServerSession session)
        {
            ConnectionPresentationState state = GetConnectionPresentationState(session);
            string stateText;
            if (session != null && session.TimedOut)
            {
                stateText = AppText.Get(state == ConnectionPresentationState.Abnormal
                    ? "Main.Connection.State.AbnormalTimeout"
                    : "Main.Connection.State.FailedTimeout");
            }
            else
            {
                switch (state)
                {
                    case ConnectionPresentationState.Normal:
                        stateText = AppText.Get("Main.Connection.State.Normal");
                        break;
                    case ConnectionPresentationState.Abnormal:
                        stateText = AppText.Get("Main.Connection.State.Abnormal");
                        break;
                    case ConnectionPresentationState.Failed:
                        stateText = AppText.Get("Main.Connection.State.Failed");
                        break;
                    case ConnectionPresentationState.NoRecord:
                        stateText = AppText.Get("Main.Connection.State.NoRecord");
                        break;
                    case ConnectionPresentationState.NotApplicable:
                        stateText = AppText.Get("Main.Connection.State.NotApplicable");
                        break;
                    default:
                        stateText = AppText.Get("Main.Connection.State.LogUnavailable");
                        break;
                }
            }

            if (session == null || session.ReconnectCount <= 0) return stateText;
            return AppText.Format(
                session.ReconnectCount == 1
                    ? "Main.Connection.ResultReconnect.One"
                    : "Main.Connection.ResultReconnect.Other",
                stateText,
                session.ReconnectCount);
        }

        private static string FormatActualRttDisplay(ServerSession session)
        {
            double value;
            if (RaidMetricPresentation.TryGetActualRtt(session, out value))
                return Math.Round(value) + " ms";
            return GetMetricUnavailableText(session);
        }

        private static string FormatPacketLossDisplay(ServerSession session)
        {
            double value;
            if (RaidMetricPresentation.TryGetPacketLoss(session, out value))
            {
                if (value == 0) return "0%";
                double percent = value * 100.0;
                return percent < 0.01
                    ? "0.01%"
                    : string.Format("{0:0.##}%", percent);
            }
            return GetMetricUnavailableText(session);
        }

        private static string GetMetricUnavailableText(ServerSession session)
        {
            if (session == null) return "-";
            return AppText.Get(session.HostingMode == TarkovHostingMode.Local
                ? "Main.Connection.State.NotApplicable"
                : "Main.Connection.State.LogUnavailable");
        }

        private static string GetActualRttHelp(ServerSession session)
        {
            if (session == null) return string.Empty;
            if (session.HostingMode == TarkovHostingMode.Local)
                return AppText.Get("Main.Metric.LocalRaidHelp");
            double ignored;
            return RaidMetricPresentation.TryGetActualRtt(session, out ignored)
                ? string.Empty
                : AppText.Get("Main.Metric.MissingLogHelp");
        }

        private static string GetPacketLossHelp(ServerSession session)
        {
            if (session == null) return string.Empty;
            if (session.HostingMode == TarkovHostingMode.Local)
                return AppText.Get("Main.Metric.LocalRaidHelp");
            double ignored;
            return RaidMetricPresentation.TryGetPacketLoss(session, out ignored)
                ? string.Empty
                : AppText.Get("Main.Metric.MissingLogHelp");
        }

        private void CaptureHistoryViewport(
            out string anchorIdentity,
            out int anchorIndex,
            out int horizontalOffset)
        {
            anchorIdentity = null;
            anchorIndex = -1;
            horizontalOffset = 0;
            if (_historyGrid == null) return;
            try { horizontalOffset = _historyGrid.HorizontalScrollingOffset; }
            catch { }
            try { anchorIndex = _historyGrid.FirstDisplayedScrollingRowIndex; }
            catch { anchorIndex = -1; }
            if (anchorIndex < 0 || anchorIndex >= _historyGrid.Rows.Count) return;
            var anchorSession = _historyGrid.Rows[anchorIndex].Tag as ServerSession;
            if (anchorSession != null)
                anchorIdentity = GetSessionRefreshIdentity(anchorSession);
        }

        private void RestoreHistoryViewport(
            string anchorIdentity,
            int fallbackIndex,
            int horizontalOffset)
        {
            if (_historyGrid == null || _historyGrid.Rows.Count == 0) return;
            int targetIndex = -1;
            if (!string.IsNullOrWhiteSpace(anchorIdentity))
            {
                DataGridViewRow anchoredRow = _historyGrid.Rows
                    .Cast<DataGridViewRow>()
                    .FirstOrDefault(row =>
                    {
                        var session = row.Tag as ServerSession;
                        return session != null
                            && string.Equals(
                                GetSessionRefreshIdentity(session),
                                anchorIdentity,
                                StringComparison.OrdinalIgnoreCase);
                    });
                if (anchoredRow != null) targetIndex = anchoredRow.Index;
            }
            if (targetIndex < 0 && fallbackIndex >= 0)
                targetIndex = Math.Min(fallbackIndex, _historyGrid.Rows.Count - 1);
            if (targetIndex >= 0)
            {
                try { _historyGrid.FirstDisplayedScrollingRowIndex = targetIndex; }
                catch { }
            }
            try { _historyGrid.HorizontalScrollingOffset = Math.Max(0, horizontalOffset); }
            catch { }
            UpdateStickyActionGridBounds();
            SyncStickyActionVerticalScroll();
        }

        private void SelectGridRow(ServerSession session, bool scrollIntoView = true)
        {
            if (session == null || _historyGrid == null) return;
            string targetIdentity = GetSessionRefreshIdentity(session);
            _historyGrid.ClearSelection();
            foreach (DataGridViewRow row in _historyGrid.Rows)
            {
                var rowSession = row.Tag as ServerSession;
                bool matches = rowSession != null
                    && string.Equals(
                        GetSessionRefreshIdentity(rowSession),
                        targetIdentity,
                        StringComparison.OrdinalIgnoreCase);
                if (!matches) continue;
                row.Selected = true;
                if (scrollIntoView && row.Index >= 0)
                {
                    try { _historyGrid.FirstDisplayedScrollingRowIndex = row.Index; }
                    catch { }
                }
                break;
            }
        }

        private void SelectSession(ServerSession session)
        {
            _selectedSession = session;
            _ipLabel.Text = session.HasServerIp
                ? session.IpAddress
                : (session.HostingMode == TarkovHostingMode.Local
                    ? AppText.Get("Main.Current.LocalRun")
                    : AppText.Get("Main.Current.NoMatchIp"));
            _ipLabel.ForeColor = session.HasServerIp ? TextPrimary : TextMuted;
            string mapAndTypeText = session.GameDisplayName + " · " + GetMapAndTypeText(session);
            _mapValueLabel.Text = mapAndTypeText;
            _mapValueLabel.AccessibleDescription = mapAndTypeText;
            _toolTip.SetToolTip(_mapValueLabel, mapAndTypeText);
            _timeValueLabel.Text = session.DisplayDetectedAt.ToString("yyyy-MM-dd HH:mm:ss");
            _actualRttValueLabel.Text = FormatActualRttDisplay(session);
            _actualRttValueLabel.ForeColor = GetLatencyColor(session);
            _packetLossValueLabel.Text = FormatPacketLossDisplay(session);
            _packetLossValueLabel.ForeColor = GetPacketLossColor(session);
            string actualRttHelp = GetActualRttHelp(session);
            string packetLossHelp = GetPacketLossHelp(session);
            _toolTip.SetToolTip(
                _actualRttValueLabel,
                actualRttHelp);
            _toolTip.SetToolTip(
                _packetLossValueLabel,
                packetLossHelp);
            _actualRttValueLabel.AccessibleDescription = CreateMetricAccessibleDescription(
                _actualRttValueLabel.Text,
                actualRttHelp);
            _packetLossValueLabel.AccessibleDescription = CreateMetricAccessibleDescription(
                _packetLossValueLabel.Text,
                packetLossHelp);
            UpdateDetailInfo(session);
            ApplySelectedMetricFonts();

            if (!session.HasServerIp)
            {
                _pingValueLabel.Text = "-";
                _locationValueLabel.Text = "-";
                _pingValueLabel.ForeColor = TextMuted;
                _locationValueLabel.ForeColor = TextMuted;
                UpdateActionButtons();
                return;
            }

            if (_measuringIpAddresses.Contains(session.IpAddress))
            {
                GeoInfo existingGeo;
                _pingValueLabel.Text = AppText.Get("Main.Current.Measuring");
                _locationValueLabel.Text = _geoResults.TryGetValue(session.IpAddress, out existingGeo) && existingGeo.Success
                    ? GetLocationCellText(session, existingGeo)
                    : AppText.Get("Main.Current.Scanning");
                _pingValueLabel.ForeColor = Accent;
                _locationValueLabel.ForeColor = Accent;
                UpdateActionButtons();
                return;
            }

            PingResult ping;
            GeoInfo geo;
            FirewallQueryResult firewall;
            _pingResults.TryGetValue(session.IpAddress, out ping);
            _geoResults.TryGetValue(session.IpAddress, out geo);
            _firewallStates.TryGetValue(session.IpAddress, out firewall);
            ShowSelectedResults(session, ping, geo, firewall);
            UpdateActionButtons();
        }

        private void UpdateDetailInfo(ServerSession session)
        {
            if (_detailInfoValueLabels == null || _detailInfoValueLabels.Length < 7) return;
            if (session == null)
            {
                foreach (Label label in _detailInfoValueLabels)
                {
                    DetailValueLabel detailValueLabel = label as DetailValueLabel;
                    if (detailValueLabel != null)
                        detailValueLabel.SetTextSegments("-", string.Empty);
                    else
                        label.Text = "-";
                    label.ForeColor = TextMuted;
                }
                _toolTip.SetToolTip(_detailInfoValueLabels[2], string.Empty);
                return;
            }

            string matching = session.MatchmakingSeconds.HasValue
                ? FormatDuration(session.MatchmakingSeconds.Value)
                : AppText.Get("Common.Status.NotVerified");
            TimeSpan? raidEntryDuration = session.RaidEntryDuration;
            string entry = raidEntryDuration.HasValue
                ? FormatDuration(raidEntryDuration.Value.TotalSeconds)
                : AppText.Get("Common.Status.NotVerified");
            string port = session.Port > 0 ? session.Port.ToString() : "-";
            string version = string.IsNullOrWhiteSpace(session.ClientVersion)
                ? AppText.Get("Common.Status.NotVerified")
                : session.ClientVersion;
            string sid = string.IsNullOrWhiteSpace(session.ServerId) ? "-" : session.ServerId;
            string shortId = string.IsNullOrWhiteSpace(session.ShortId) ? "-" : session.ShortId;
            string[] values =
            {
                FormatOperationDuration(session),
                matching + " / " + entry,
                string.IsNullOrWhiteSpace(session.ConnectionResultText)
                    ? "-"
                    : FormatConnectionResult(session),
                version,
                port + " / " + (string.IsNullOrWhiteSpace(session.DataCenterCode) ? "-" : session.DataCenterCode),
                shortId,
                sid
            };
            for (int index = 0; index < _detailInfoValueLabels.Length && index < values.Length; index++)
            {
                DetailValueLabel detailValueLabel =
                    _detailInfoValueLabels[index] as DetailValueLabel;
                if (detailValueLabel != null)
                    detailValueLabel.SetTextSegments(
                        values[index],
                        index == 1 ? AppText.Get("Main.Advanced.ElapsedSuffix") : string.Empty);
                else
                    _detailInfoValueLabels[index].Text = values[index];
                _detailInfoValueLabels[index].ForeColor = TextPrimary;
            }
            _toolTip.SetToolTip(_detailInfoValueLabels[2], GetConnectionResultHelp(session));
        }

        private static string FormatOperationDuration(ServerSession session)
        {
            if (session == null) return AppText.Get("Common.Status.NotVerified");
            if (session.OperationState == RaidOperationState.InProgress)
                return AppText.Get("Main.Detail.InProgress");
            TimeSpan? duration = session.OperationDuration;
            if (session.OperationState != RaidOperationState.Completed || !duration.HasValue)
                return AppText.Get("Common.Status.NotVerified");

            long totalSeconds = Math.Max(0L, (long)Math.Floor(duration.Value.TotalSeconds));
            long totalMinutes = totalSeconds / 60L;
            long remainingSeconds = totalSeconds % 60L;
            return totalMinutes > 0
                ? AppText.Format("Main.Duration.MinutesSeconds", totalMinutes, remainingSeconds)
                : AppText.Format("Main.Duration.Seconds", remainingSeconds);
        }

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds)
                || double.IsInfinity(seconds)
                || seconds < 0
                || seconds > int.MaxValue)
                return AppText.Get("Common.Status.NotVerified");

            long totalSeconds = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
            long totalMinutes = totalSeconds / 60L;
            long remainingSeconds = totalSeconds % 60L;
            return totalMinutes > 0
                ? AppText.Format("Main.Duration.MinutesSeconds", totalMinutes, remainingSeconds)
                : AppText.Format("Main.Duration.Seconds", remainingSeconds);
        }

        private async Task QueryVisibleServersAsync()
        {
            if (_isMeasuring || _isRefreshing || _isFirewallChanging) return;

            // The explicit query performs its own firewall read after refreshing logs.
            // Prevent an older startup decoration from racing with that authoritative pass.
            _initialFirewallStateRefresh.Invalidate();

            var queryCancellation = new CancellationTokenSource();
            _queryCancellation = queryCancellation;
            CancellationToken cancellationToken = queryCancellation.Token;
            _isMeasuring = true;
            UpdateActionButtons();
            IList<string> ipAddresses = new List<string>();
            int completed = 0;
            int answered = 0;
            int located = 0;
            int blocked = 0;
            int firewallKnown = 0;
            int refreshedNewLogCount = 0;
            int refreshCapExcludedCount = 0;
            bool logRefreshPerformed = false;
            bool logRefreshReadSucceeded = false;
            ServerSession refreshedVisibleNewSession = null;
            bool geoDatabaseReady = NetworkServices.HasUsableGeoDatabase;
            try
            {
                if (!_demoMode)
                {
                    TarkovLogPaths paths = new TarkovLogPaths
                    {
                        EftPath = _appliedEftPath,
                        ArenaPath = _appliedArenaPath
                    };
                    if (!string.IsNullOrWhiteSpace(paths.EftPath)
                        || !string.IsNullOrWhiteSpace(paths.ArenaPath))
                    {
                        var previousSessionIds = new HashSet<string>(
                            _allSessions.Where(session => session != null)
                                .Select(GetSessionRefreshIdentity),
                            StringComparer.OrdinalIgnoreCase);
                        SessionPeriodPreset requestedPeriod = _sessionPeriod;
                        DateTime? requestedStart = _customPeriodStart;
                        DateTime? requestedEnd = _customPeriodEnd;
                        SetStatus(AppText.Get("Main.Query.RefreshingLogs"), Accent);
                        SessionRefreshScanResult refreshScan = await Task.Run(
                            () => ScanSessionsForRefresh(
                                paths,
                                requestedPeriod,
                                requestedStart,
                                requestedEnd),
                            cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        RaidLogScanResult scan = refreshScan.Scan;
                        var scannedNewSessionIdentities = new HashSet<string>(
                            scan.Sessions
                                .Where(session => session != null)
                                .Select(GetSessionRefreshIdentity)
                                .Where(identity => !previousSessionIds.Contains(identity)),
                            StringComparer.OrdinalIgnoreCase);
                        logRefreshPerformed = true;
                        bool scanReadSucceeded = scan.ScanCompletedWithoutErrors
                            && refreshScan.AllConfiguredPathsAvailable;
                        bool scanResultsAreComplete = scan.TotalMatchingSessionsIsExact
                            && scanReadSucceeded;
                        logRefreshReadSucceeded = scanReadSucceeded
                            && (requestedPeriod == SessionPeriodPreset.Recent100
                                || scanResultsAreComplete);

                        // RaidLogScanner reuses unchanged directory results, so a warm
                        // refresh only reparses new or modified raid folders. Refreshed
                        // records win by stable identity; retaining unmatched existing rows
                        // only for an explicitly incomplete scan prevents a transient read
                        // failure from erasing the current list without keeping stale rows
                        // after a complete scan.
                        _logScanIncomplete = !scanReadSucceeded;
                        _dateRangeScanIncomplete = requestedPeriod != SessionPeriodPreset.Recent100
                            && !scanResultsAreComplete;
                        IList<ServerSession> refreshedSessions = scanReadSucceeded
                            ? scan.Sessions.ToList()
                            : MergeRefreshedSessions(scan.Sessions, _allSessions);
                        if (requestedPeriod == SessionPeriodPreset.Recent100)
                        {
                            refreshedSessions = refreshedSessions
                                .OrderByDescending(session => session.DisplayDetectedAt)
                                .ThenByDescending(session => session.LastUpdated)
                                .Take(100)
                                .ToList();
                        }
                        var refreshedSessionIds = new HashSet<string>(
                            refreshedSessions.Where(session => session != null)
                                .Select(GetSessionRefreshIdentity),
                            StringComparer.OrdinalIgnoreCase);
                        refreshedNewLogCount = refreshedSessionIds.Count(identity =>
                            !previousSessionIds.Contains(identity));
                        int removedPreviousCount = previousSessionIds.Count(identity =>
                            !refreshedSessionIds.Contains(identity));
                        if (requestedPeriod == SessionPeriodPreset.Recent100
                            && previousSessionIds.Count >= 100
                            && refreshedSessionIds.Count >= 100
                            && refreshedNewLogCount > 0)
                        {
                            refreshCapExcludedCount = Math.Min(
                                refreshedNewLogCount,
                                removedPreviousCount);
                        }
                        _allSessions = refreshedSessions;
                        refreshedVisibleNewSession = RefreshVisibleSessionsAfterScan(
                            scannedNewSessionIdentities);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                ipAddresses = PingBatchPlanner.GetUniqueServerIps(_visibleSessions);
                if (ipAddresses.Count == 0)
                {
                    SetStatus(
                        AddLogScanWarning(AppendLogRefreshSummary(
                            AppendRegionFilterSummary(
                                AppText.Get("Main.Query.NoServerIp")),
                            logRefreshPerformed,
                            logRefreshReadSucceeded,
                            refreshedNewLogCount,
                            refreshCapExcludedCount)),
                        Warning);
                    return;
                }

                _measuringIpAddresses.Clear();
                foreach (string ipAddress in ipAddresses) _measuringIpAddresses.Add(ipAddress);
                MarkRowsChecking(ipAddresses);

                if (!geoDatabaseReady)
                {
                    SetStatus(AppText.Get("Main.Query.PreparingGeoDb"), Accent);
                    try
                    {
                        await NetworkServices.UpdateGeoDatabaseIfDueAsync(true, cancellationToken);
                        geoDatabaseReady = NetworkServices.HasUsableGeoDatabase;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        geoDatabaseReady = false;
                    }
                }
                else
                {
                    StartGeoDatabaseUpdateIfDue(cancellationToken);
                }

                SetStatus(AppText.Get("Main.Query.CheckingFirewall"), Accent);
                Dictionary<string, FirewallQueryResult> queriedStates = await Task.Run(
                    () => FirewallRuleManager.QueryMany(ipAddresses), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (KeyValuePair<string, FirewallQueryResult> pair in queriedStates)
                {
                    _firewallStates[pair.Key] = pair.Value;
                    if (pair.Value.Success)
                    {
                        firewallKnown++;
                        if (pair.Value.IsBlocked) blocked++;
                    }
                }

                using (var gate = new SemaphoreSlim(6, 6))
                {
                    Task[] tasks = ipAddresses.Select(async ipAddress =>
                    {
                        await gate.WaitAsync(cancellationToken);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            FirewallQueryResult firewall;
                            _firewallStates.TryGetValue(ipAddress, out firewall);
                            bool isBlocked = firewall != null && firewall.Success && firewall.IsBlocked;
                            GeoInfo cachedGeo;
                            Task<PingResult> pingTask = isBlocked
                                ? Task.FromResult<PingResult>(null)
                                : NetworkServices.MeasureAdaptivePingAsync(
                                    ipAddress, 3, 2, 700, 120, cancellationToken);
                            Task<GeoInfo> geoTask = _geoResults.TryGetValue(ipAddress, out cachedGeo) && cachedGeo.Success
                                ? Task.FromResult(cachedGeo)
                                : NetworkServices.LookupGeoAsync(ipAddress);

                            await Task.WhenAll(pingTask, geoTask);
                            cancellationToken.ThrowIfCancellationRequested();
                            PingResult ping = pingTask.Result;
                            GeoInfo geo = geoTask.Result;
                            if (ping == null)
                                _pingResults.Remove(ipAddress);
                            else
                                _pingResults[ipAddress] = ping;
                            _geoResults[ipAddress] = geo;
                            _measuringIpAddresses.Remove(ipAddress);
                            UpdateServerRows(ipAddress, ping, geo);
                            if (ping != null && ping.IsAvailable) answered++;
                            if (geo != null && geo.Success) located++;
                            completed++;
                            SetStatus(AppText.Format("Main.Query.Progress", completed, ipAddresses.Count), Accent);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }).ToArray();
                    await Task.WhenAll(tasks);
                }

                CacheBlockedServerMetadata(ipAddresses);

                await RefreshLauncherSelectionAsync();
                string summary = AppText.Format(
                    "Main.Query.Complete",
                    ipAddresses.Count,
                    answered,
                    blocked,
                    located);
                if (!geoDatabaseReady) summary += AppText.Get("Main.Query.GeoDbFailedSuffix");
                summary = AppendRegionFilterSummary(summary);
                summary = AppendLogRefreshSummary(
                    summary,
                    logRefreshPerformed,
                    logRefreshReadSucceeded,
                    refreshedNewLogCount,
                    refreshCapExcludedCount);
                summary = AddLogScanWarning(summary);
                SetStatus(
                    summary,
                    !_dateRangeScanIncomplete
                        && !_logScanIncomplete
                        && firewallKnown == ipAddresses.Count
                        && geoDatabaseReady
                            ? Success
                            : Warning);
            }
            catch (OperationCanceledException)
            {
                SetStatus(AppText.Get("Main.Query.Cancelled"), Warning);
                RestoreRowsAfterCancelledQuery(ipAddresses);
            }
            catch (Exception ex)
            {
                SetStatus(AppText.Format("Main.Query.Failed", ex.Message), Danger);
                RestoreRowsAfterCancelledQuery(ipAddresses);
            }
            finally
            {
                _measuringIpAddresses.Clear();
                _isMeasuring = false;
                if (ReferenceEquals(_queryCancellation, queryCancellation))
                    _queryCancellation = null;
                queryCancellation.Dispose();
                UpdateActionButtons();
                RefreshActionCells();
                if (IsFirewallActionSortColumn(_historySortColumn))
                    ReapplyHistorySort(false, true);
                else if (_historySortColumn == "ping"
                    || _historySortColumn == "location")
                    ReapplyHistorySort(refreshedVisibleNewSession != null);
            }
        }

        private void CancelVisibleServerQuery()
        {
            CancellationTokenSource cancellation = _queryCancellation;
            if (cancellation == null || cancellation.IsCancellationRequested) return;
            SetStatus(AppText.Get("Main.Query.Cancelling"), Warning);
            if (_queryButton != null)
            {
                _queryButton.Text = AppText.Get("Main.Query.ButtonCancelling");
                _queryButton.Enabled = false;
            }
            cancellation.Cancel();
        }

        private void RestoreRowsAfterCancelledQuery(IEnumerable<string> ipAddresses)
        {
            foreach (string ipAddress in ipAddresses)
            {
                PingResult ping;
                GeoInfo geo;
                _pingResults.TryGetValue(ipAddress, out ping);
                _geoResults.TryGetValue(ipAddress, out geo);
                UpdateServerRows(ipAddress, ping, geo);
            }
        }

        private static void StartGeoDatabaseUpdateIfDue(CancellationToken cancellationToken)
        {
            try
            {
                NetworkServices.UpdateGeoDatabaseIfDueAsync(true, cancellationToken)
                    .ContinueWith(
                        task =>
                        {
                            if (task.Exception != null)
                                task.Exception.Handle(delegate { return true; });
                        },
                        TaskContinuationOptions.OnlyOnFaulted);
            }
            catch
            {
                // The existing local database remains usable when an update cannot start.
            }
        }

        private void MarkRowsChecking(IEnumerable<string> ipAddresses)
        {
            var measuring = new HashSet<string>(ipAddresses, StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _historyGrid.Rows)
            {
                var session = row.Tag as ServerSession;
                if (session == null || !session.HasServerIp || !measuring.Contains(session.IpAddress)) continue;
                GeoInfo cachedGeo;
                row.Cells["location"].Value = _geoResults.TryGetValue(session.IpAddress, out cachedGeo) && cachedGeo.Success
                    ? GetLocationCellText(session, cachedGeo)
                    : AppText.Get("Main.Current.Scanning");
                row.Cells["ping"].Value = AppText.Get("Main.Current.Measuring");
                row.Cells["location"].Style.ForeColor = Accent;
                row.Cells["ping"].Style.ForeColor = Accent;
                UpdateActionCells(row);
            }

            if (_selectedSession != null && _selectedSession.HasServerIp && measuring.Contains(_selectedSession.IpAddress))
            {
                _pingValueLabel.Text = AppText.Get("Main.Current.Measuring");
                _locationValueLabel.Text = AppText.Get("Main.Current.Scanning");
                _pingValueLabel.ForeColor = Accent;
                _locationValueLabel.ForeColor = Accent;
            }
        }

        private void UpdateServerRows(string ipAddress, PingResult ping, GeoInfo geo)
        {
            FirewallQueryResult firewall;
            _firewallStates.TryGetValue(ipAddress, out firewall);
            foreach (DataGridViewRow row in _historyGrid.Rows)
            {
                var session = row.Tag as ServerSession;
                if (session == null || !string.Equals(session.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase)) continue;
                row.Cells["location"].Value = GetLocationCellText(session, geo);
                row.Cells["ping"].Value = GetPingCellText(session, ping, firewall);
                ApplyResultRowStyle(row, session, ping, geo, firewall);
                UpdateActionCells(row);
            }
            if (_selectedSession != null && string.Equals(_selectedSession.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase))
                ShowSelectedResults(_selectedSession, ping, geo, firewall);
        }

        private static string GetLocationCellText(ServerSession session, GeoInfo geo)
        {
            if (session == null || !session.HasServerIp) return "-";
            string dataCenter = session == null || string.IsNullOrWhiteSpace(session.DataCenterCode)
                ? null
                : session.DataCenterCode;
            string location = geo == null
                ? AppText.Get("Main.Current.BeforeScan")
                : LocalizeGeoDisplay(geo);
            return string.IsNullOrWhiteSpace(dataCenter) ? location : dataCenter + " / " + location;
        }

        private static string LocalizeGeoDisplay(GeoInfo geo)
        {
            if (geo == null) return AppText.Get("Main.Current.BeforeScan");
            string display = geo.ToDisplayText();
            if (geo.Success
                || string.Equals(AppText.CurrentLanguage, AppText.KoreanLanguage, StringComparison.Ordinal))
                return display;

            string translated = AppText.TranslateLiteral(display);
            return !string.Equals(translated, display, StringComparison.Ordinal)
                ? translated
                : AppText.Get("Common.Status.NotVerified");
        }

        private static string GetPingCellText(ServerSession session, PingResult ping, FirewallQueryResult firewall)
        {
            if (session == null || !session.HasServerIp) return "-";
            if (firewall != null && firewall.Success && firewall.IsBlocked)
                return AppText.Get("Main.Current.Blocked");
            if (ping == null) return AppText.Get("Main.Current.BeforeScan");
            return ping.IsAvailable
                ? ping.AverageMs + " ms"
                : AppText.Get("Main.Current.NoResponse");
        }

        private static void ApplyResultRowStyle(
            DataGridViewRow row,
            ServerSession session,
            PingResult ping,
            GeoInfo geo,
            FirewallQueryResult firewall)
        {
            row.DefaultCellStyle.ForeColor = TextPrimary;
            row.Cells["game"].Style.ForeColor = session.Game == TarkovGame.Arena ? Accent : TextPrimary;
            row.Cells["location"].Style.ForeColor = geo != null && geo.Success ? TextPrimary : TextMuted;
            bool isBlocked = firewall != null && firewall.Success && firewall.IsBlocked;
            row.Cells["ping"].Style.ForeColor = isBlocked ? Danger : GetPingColor(ping);
            row.Cells["actualRtt"].Style.ForeColor = GetLatencyColor(session);
            row.Cells["packetLoss"].Style.ForeColor = GetPacketLossColor(session);
            row.Cells["userReport"].Style.ForeColor = session.UserReportCount > 0 ? ReportOrange : TextMuted;
            row.Cells["result"].Style.ForeColor = GetConnectionResultColor(session);
        }

        private void ApplyMissingMetricCellFonts(DataGridViewRow row)
        {
            if (row == null || _historyGrid == null) return;
            Font resultFont = _historyGrid.Columns["result"].DefaultCellStyle.Font;
            ApplyMissingMetricCellFont(row.Cells["actualRtt"], resultFont);
            ApplyMissingMetricCellFont(row.Cells["packetLoss"], resultFont);
        }

        private static void ApplyMissingMetricCellFont(DataGridViewCell cell, Font resultFont)
        {
            if (cell == null) return;
            string text = Convert.ToString(cell.Value);
            bool missing = string.Equals(
                text,
                AppText.Get("Main.Connection.State.LogUnavailable"),
                StringComparison.Ordinal);
            // Null restores the normal inherited metric font for measured,
            // local-PvE, and neutral values. Only the compact missing state uses
            // the connection-result font beside it.
            cell.Style.Font = missing ? resultFont : null;
        }

        private void ApplySelectedMetricFonts()
        {
            if (_actualRttValueLabel == null
                || _packetLossValueLabel == null
                || _mapValueLabel == null
                || _detailInfoValueLabels == null
                || _detailInfoValueLabels.Length < 3)
                return;

            Font regularFont = _mapValueLabel.Font;
            Font resultFont = _detailInfoValueLabels[2].Font;
            _actualRttValueLabel.Font = IsCompactMetricState(_actualRttValueLabel.Text)
                    ? resultFont
                    : regularFont;
            _packetLossValueLabel.Font = IsCompactMetricState(_packetLossValueLabel.Text)
                    ? resultFont
                    : regularFont;
        }

        private static bool IsCompactMetricState(string text)
        {
            return string.Equals(
                text,
                AppText.Get("Main.Connection.State.LogUnavailable"),
                StringComparison.Ordinal);
        }

        private static string CreateMetricAccessibleDescription(string value, string help)
        {
            string normalizedValue = value ?? string.Empty;
            return string.IsNullOrWhiteSpace(help)
                ? normalizedValue
                : normalizedValue + ". " + help;
        }

        private static Color GetPacketLossColor(ServerSession session)
        {
            double value;
            if (!RaidMetricPresentation.TryGetPacketLoss(session, out value)) return TextMuted;
            if (value >= 0.05) return Danger;
            if (value > 0) return Warning;
            return Success;
        }

        private static Color GetConnectionResultColor(ServerSession session)
        {
            if (session == null) return TextMuted;
            ConnectionPresentationState state = GetConnectionPresentationState(session);
            return session.TimedOut
                    || state == ConnectionPresentationState.Failed
                    || state == ConnectionPresentationState.Abnormal
                ? Danger
                : TextMuted;
        }

        private static string GetConnectionResultHelp(ServerSession session)
        {
            if (session == null) return string.Empty;
            ConnectionPresentationState state = GetConnectionPresentationState(session);
            if (state == ConnectionPresentationState.Normal)
                return AppText.Get("Main.Connection.Help.Normal");
            if (session.TimedOut)
            {
                bool connected = session.ConnectedOnce || session.CurrentAttemptConnected;
                return connected
                    ? AppText.Get("Main.Connection.Help.AbnormalTimeout")
                    : AppText.Get("Main.Connection.Help.FailedTimeout");
            }
            if (state == ConnectionPresentationState.Failed)
                return AppText.Get("Main.Connection.Help.Failed");
            if (state == ConnectionPresentationState.Abnormal)
                return AppText.Get("Main.Connection.Help.Abnormal");
            if (state == ConnectionPresentationState.NoRecord)
                return AppText.Get("Main.Connection.Help.NoRecord");
            if (state == ConnectionPresentationState.NotApplicable)
                return AppText.Get("Main.Connection.Help.NotApplicable");
            if (state == ConnectionPresentationState.LogUnavailable)
                return AppText.Get("Main.Metric.MissingLogHelp");
            return AppText.Get("Main.Connection.Help.Unknown");
        }

        private void ShowSelectedResults(ServerSession session, PingResult ping, GeoInfo geo, FirewallQueryResult firewall)
        {
            _locationValueLabel.Text = GetLocationCellText(session, geo);
            _locationValueLabel.ForeColor = geo != null && geo.Success ? TextPrimary : TextMuted;
            if (firewall != null && firewall.Success && firewall.IsBlocked)
            {
                _pingValueLabel.Text = AppText.Get("Main.Current.Blocked");
                _pingValueLabel.ForeColor = Danger;
                return;
            }
            if (ping == null)
            {
                _pingValueLabel.Text = AppText.Get("Main.Current.BeforeScan");
                _pingValueLabel.ForeColor = TextMuted;
                return;
            }
            _pingValueLabel.Text = ping.IsAvailable
                ? AppText.Format("Main.Current.PingDetails", ping.AverageMs, ping.MinimumMs, ping.MaximumMs)
                : AppText.Get("Main.Current.NoResponse");
            _pingValueLabel.ForeColor = GetPingColor(ping);
        }

        private static Color GetPingColor(PingResult ping)
        {
            if (ping == null) return TextMuted;
            switch (ping.Quality)
            {
                case PingQuality.Good: return Success;
                case PingQuality.Elevated: return Warning;
                case PingQuality.High: return Danger;
                default: return TextMuted;
            }
        }

        private static Color GetLatencyColor(ServerSession session)
        {
            double value;
            if (!RaidMetricPresentation.TryGetActualRtt(session, out value)) return TextMuted;
            if (value >= 150) return Danger;
            if (value >= 100) return Warning;
            return Success;
        }

        private async Task HistoryGridCellMouseClickAsync(DataGridViewCellMouseEventArgs args)
        {
            if (args.RowIndex < 0 || args.Button != MouseButtons.Left) return;
            DataGridViewRow row = _historyGrid.Rows[args.RowIndex];
            var session = row.Tag as ServerSession;
            if (session == null) return;
            SelectSession(session);
            if (_demoMode && (args.ColumnIndex == _historyGrid.Columns["note"].Index
                || args.ColumnIndex == _historyGrid.Columns["userReport"].Index))
            {
                ShowActionNotice(AppText.Get("Main.Preview.NoNotes"));
                return;
            }
            if (args.ColumnIndex == _historyGrid.Columns["note"].Index)
            {
                try
                {
                    if (RaidNoteUi.ShowFor(this, session))
                    {
                        if (_historySortColumn == "note") ReapplyHistorySort();
                        else UpdateNoteCell(row, _noteStore.Exists(session));
                    }
                }
                catch (Exception ex)
                {
                    SetStatus(AppText.Format("Notes.OpenFailed", ex.Message), Danger);
                }
                return;
            }
            if (args.ColumnIndex == _historyGrid.Columns["userReport"].Index)
            {
                try
                {
                    if (session.UserReportCount > 0)
                        UserReportMemoUi.ShowFor(this, session);
                }
                catch (Exception ex)
                {
                    SetStatus(AppText.Format("Notes.OpenFailed", ex.Message), Danger);
                }
                return;
            }

            PingKickAction action;
            if (args.ColumnIndex == _historyGrid.Columns["blockAction"].Index)
            {
                action = PingKickAction.Block;
            }
            else if (args.ColumnIndex == _historyGrid.Columns["unblockAction"].Index)
            {
                action = PingKickAction.Unblock;
            }
            else return;
            if (!IsInteractiveCellPoint(args)) return;

            await ExecuteHistoryConnectionActionAsync(row, session, action);
        }

        private async Task ExecuteHistoryConnectionActionAsync(
            DataGridViewRow row,
            ServerSession session,
            PingKickAction action)
        {
            if (row == null || session == null) return;
            string actionColumn = action == PingKickAction.Block
                ? "blockAction"
                : "unblockAction";
            if (_demoMode)
            {
                ShowActionNotice(AppText.Get("Main.Preview.NoFirewall"));
                return;
            }
            bool enabled = row.Cells[actionColumn].Tag is bool && (bool)row.Cells[actionColumn].Tag;
            if (action == PingKickAction.Block && enabled)
            {
                if (session.Game == TarkovGame.Arena && !ConfirmArenaServerBlock()) return;
                await ChangeFirewallStateAsync(session.IpAddress, true);
            }
            else if (action == PingKickAction.Unblock && enabled)
                await ChangeFirewallStateAsync(session.IpAddress, false);
            else
                ShowDisabledActionReason(session.IpAddress, action);
        }

        private bool ConfirmArenaServerBlock()
        {
            using (var dialog = new ArenaBlockWarningForm())
                return dialog.ShowDialog(this) == DialogResult.OK;
        }

        private void ShowDisabledActionReason(string ipAddress, PingKickAction action)
        {
            string message;
            if (!FirewallRuleManager.IsValidIpv4(ipAddress))
                message = AppText.Get("Main.Action.NoServerIp");
            else if (_isMeasuring)
                message = AppText.Get("Main.Action.Scanning");
            else if (_isFirewallChanging || _firewallBusyIpAddresses.Contains(ipAddress))
                message = AppText.Get("Main.Action.FirewallBusy");
            else if (_isRefreshing)
                message = AppText.Get("Main.Action.LogsBusy");
            else
            {
                FirewallQueryResult firewall;
                if (!_firewallStates.TryGetValue(ipAddress, out firewall))
                    message = AppText.Get("Main.Action.ScanFirst");
                else if (!firewall.Success)
                    message = AppText.Get("Main.Action.FirewallUnknown");
                else if (action == PingKickAction.Block && firewall.IsBlocked)
                    message = AppText.Get("Main.Action.AlreadyBlocked");
                else if (action == PingKickAction.Unblock && !firewall.IsBlocked)
                    message = AppText.Get("Main.Action.NotBlocked");
                else
                    message = AppText.Get("Main.Action.Unavailable");
            }
            ShowActionNotice(message);
        }

        private void ShowActionNotice(string message)
        {
            SetStatus(message, Warning);
            MessageBox.Show(
                this,
                message,
                AppText.Get("Main.Action.DialogTitle"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async Task ChangeFirewallStateAsync(string ipAddress, bool shouldBlock)
        {
            if (!FirewallRuleManager.IsValidIpv4(ipAddress)
                || _isMeasuring || _isRefreshing || _isFirewallChanging
                || _firewallBusyIpAddresses.Contains(ipAddress)) return;
            FirewallQueryResult current;
            if (!_firewallStates.TryGetValue(ipAddress, out current) || !current.Success) return;
            if (current.IsBlocked == shouldBlock) return;

            bool requestEvidence = false;
            long evidenceRevision = 0;
            _isFirewallChanging = true;
            _firewallBusyIpAddresses.Add(ipAddress);
            UpdateActionButtons();
            SetStatus(AppText.Format(shouldBlock ? "Main.Firewall.ChangingBlock"
                : "Main.Firewall.ChangingUnblock", ipAddress), Accent);
            try
            {
                FirewallChangeResult result = await _changeFirewallState(ipAddress, shouldBlock);
                if (IsDisposed || Disposing) return;
                if (result == null || !result.Success)
                {
                    if (result == null || !result.Cancelled)
                        _firewallStates[ipAddress] = await Task.Run(() => _queryFirewallState(ipAddress));
                    if (!IsDisposed && !Disposing)
                        SetStatus(GetFirewallFailureDisplay(result == null ? null : result.ErrorMessage), Danger);
                    return;
                }
                _firewallStates[ipAddress] = new FirewallQueryResult { Success = true, IsBlocked = result.IsBlocked };
                _pingResults.Remove(ipAddress);
                if (result.IsBlocked != shouldBlock)
                {
                    SetStatus(AppText.Get("Main.Firewall.StateMismatch"), Warning);
                    return;
                }

                bool metadataSaved = true;
                try
                {
                    if (shouldBlock) _saveBlockedMetadata(ipAddress);
                    else _removeBlockedMetadata(ipAddress);
                }
                catch { metadataSaved = false; }
                string message = GetFirewallChangeSuccessMessage(ipAddress, shouldBlock);
                if (!metadataSaved) message += "\r\n" + AppText.Get("Main.Firewall.MetadataFailed");
                SetStatus(message, metadataSaved ? (shouldBlock ? Danger : Success) : Warning);
                evidenceRevision = _statusRevision;
                requestEvidence = shouldBlock && metadataSaved;
            }
            catch (Exception ex)
            {
                // A failed native/helper operation leaves the previous cached state uncertain.
                _firewallStates[ipAddress] = new FirewallQueryResult { Success = false, ErrorMessage = ex.Message };
                if (!IsDisposed && !Disposing) SetStatus(GetFirewallFailureDisplay(ex.Message), Danger);
            }
            finally
            {
                _firewallBusyIpAddresses.Remove(ipAddress);
                _isFirewallChanging = false;
                if (!IsDisposed && !Disposing)
                {
                    UpdateActionButtons();
                    UpdateServerRowsFromCache(ipAddress);
                    if (IsFirewallActionSortColumn(_historySortColumn)) ReapplyHistorySort(false, true);
                    else if (_historySortColumn == "ping") ReapplyHistorySort();
                }
            }

            // Optional history analysis never keeps the successfully completed action busy.
            if (requestEvidence && !IsDisposed && !Disposing && _statusRevision == evidenceRevision)
                _pendingQualityEvidence = PublishFirewallEvidenceAsync(ipAddress, evidenceRevision);
        }

        private async Task PublishFirewallEvidenceAsync(string ipAddress, long expectedRevision)
        {
            string eftPath = _appliedEftPath;
            string arenaPath = _appliedArenaPath;
            try
            {
                RaidQualityEvidenceSummary evidence = await _loadRaidQualityEvidence(ipAddress);
                if (evidence == null || IsDisposed || Disposing || _statusRevision != expectedRevision
                    || !string.Equals(eftPath, _appliedEftPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(arenaPath, _appliedArenaPath, StringComparison.OrdinalIgnoreCase)) return;
                FirewallQueryResult current;
                if (!_firewallStates.TryGetValue(ipAddress, out current)
                    || !current.Success || !current.IsBlocked) return;
                SetStatus(GetFirewallChangeSuccessMessageWithEvidence(ipAddress, true, evidence), Danger);
            }
            catch
            {
                // An optional evidence failure must not replace the confirmed firewall result.
            }
        }

        private static string GetFirewallChangeSuccessMessage(string ipAddress, bool shouldBlock)
        {
            return GetFirewallChangeSuccessMessageWithEvidence(ipAddress, shouldBlock, null);
        }

        private static string GetFirewallChangeSuccessMessageWithEvidence(
            string ipAddress,
            bool shouldBlock,
            RaidQualityEvidenceSummary qualityEvidence)
        {
            if (!shouldBlock)
                return AppText.Format("Main.Firewall.Unblocked", ipAddress);

            string result = AppText.Format("Main.Firewall.Blocked", ipAddress);
            if (qualityEvidence != null)
            {
                result += AppText.Format(
                    "Main.Firewall.QualityEvidence",
                    qualityEvidence.WindowRaidCount,
                    qualityEvidence.MatchingRaidCount,
                    qualityEvidence.ProblemRaidCount) + "\r\n";
            }
            return result + AppText.Get("Main.Firewall.Persistence");
        }

        private static string GetFirewallFailureDisplay(string serviceMessage)
        {
            if (string.Equals(
                AppText.CurrentLanguage,
                AppText.KoreanLanguage,
                StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(serviceMessage))
                return serviceMessage;
            return AppText.Get("Main.Firewall.Failed");
        }

        private async Task<RaidQualityEvidenceSummary> LoadRecentRaidQualityEvidenceAsync(
            string ipAddress)
        {
            try
            {
                RaidLogScanResult scan = await LoadRecentSessionsForQualityEvidenceAsync();
                if (scan == null
                    || !scan.ScanCompletedWithoutErrors
                    || !scan.TotalMatchingSessionsIsExact
                    || scan.Sessions == null)
                    return null;
                return RaidQualityEvidence.Analyze(scan.Sessions, ipAddress);
            }
            catch
            {
                // A successful firewall change must not be reported as failed merely because
                // the optional read-only history summary could not be rebuilt.
                return null;
            }
        }

        private void UpdateServerRowsFromCache(string ipAddress)
        {
            PingResult ping;
            GeoInfo geo;
            _pingResults.TryGetValue(ipAddress, out ping);
            _geoResults.TryGetValue(ipAddress, out geo);
            UpdateServerRows(ipAddress, ping, geo);
        }

        private void CacheBlockedServerMetadata(IEnumerable<string> ipAddresses)
        {
            if (ipAddresses == null) return;
            foreach (string ipAddress in ipAddresses)
            {
                FirewallQueryResult firewall;
                if (!_firewallStates.TryGetValue(ipAddress, out firewall)
                    || !firewall.Success
                    || !firewall.IsBlocked) continue;

                ServerSession session = _visibleSessions.FirstOrDefault(item =>
                    string.Equals(item.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase));
                GeoInfo geo;
                _geoResults.TryGetValue(ipAddress, out geo);
                BlockedServerMetadataStore.UpdateLocation(
                    ipAddress,
                    session == null ? null : session.DataCenterCode,
                    geo != null && geo.Success ? geo.ToDisplayText() : null);
            }
        }

        private void SaveBlockedServerMetadata(string ipAddress)
        {
            ServerSession session = _visibleSessions.FirstOrDefault(item =>
                string.Equals(item.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase));
            GeoInfo geo;
            _geoResults.TryGetValue(ipAddress, out geo);
            BlockedServerMetadataStore.MarkBlocked(
                ipAddress,
                session == null ? null : session.DataCenterCode,
                geo != null && geo.Success ? geo.ToDisplayText() : null);
        }

        private void RefreshActionCells()
        {
            if (_historyGrid == null) return;
            foreach (DataGridViewRow row in _historyGrid.Rows) UpdateActionCells(row);
            if (_stickyActionGrid != null) _stickyActionGrid.Invalidate();
        }

        private void UpdateActionCells(DataGridViewRow row)
        {
            if (row == null || _historyGrid == null
                || !_historyGrid.Columns.Contains("blockAction")
                || !_historyGrid.Columns.Contains("unblockAction")) return;
            var session = row.Tag as ServerSession;
            if (session == null) return;

            DataGridViewCell blockCell = row.Cells["blockAction"];
            DataGridViewCell unblockCell = row.Cells["unblockAction"];
            if (!session.HasServerIp)
            {
                ApplyActionCellStyle(blockCell, false, true);
                ApplyActionCellStyle(unblockCell, false, false);
                blockCell.ToolTipText = AppText.Get("Main.Action.NoServerIp");
                unblockCell.ToolTipText = blockCell.ToolTipText;
                UpdateStickyActionRow(row);
                return;
            }

            FirewallActionPresentationState actionState =
                GetFirewallActionPresentationState(session);
            ApplyActionCellStyle(blockCell, actionState.BlockAvailable, true);
            ApplyActionCellStyle(unblockCell, actionState.UnblockAvailable, false);
            string help = actionState.Busy
                ? AppText.Get("Main.Action.FirewallBusy")
                : (_isMeasuring
                    ? AppText.Get("Main.Action.Scanning")
                    : (!actionState.HasResult
                        ? AppText.Get("Main.Action.ScanFirst")
                        : (!actionState.Known
                            ? AppText.Get("Main.Action.FirewallUnknown")
                            : AppText.Get(actionState.IsBlocked
                                ? "Main.Action.AlreadyBlocked"
                                : "Main.Action.NotBlocked"))));
            blockCell.ToolTipText = help;
            unblockCell.ToolTipText = help;
            _historyGrid.InvalidateCell(blockCell);
            _historyGrid.InvalidateCell(unblockCell);
            UpdateStickyActionRow(row);
        }

        private static void ApplyActionCellStyle(DataGridViewCell cell, bool enabled, bool isBlock)
        {
            if (cell == null) return;
            cell.Tag = enabled;
            cell.Style.BackColor = Surface;
            cell.Style.ForeColor = TextMuted;
            cell.Style.SelectionBackColor = Color.FromArgb(56, 65, 76);
            cell.Style.SelectionForeColor = TextMuted;
        }

        private void UpdateNoteCell(DataGridViewRow row, bool hasNote)
        {
            if (row == null || !_historyGrid.Columns.Contains("note")) return;
            DataGridViewCell cell = row.Cells["note"];
            cell.Value = string.Empty;
            cell.Tag = hasNote;
            cell.Style.BackColor = Surface;
            cell.Style.SelectionBackColor = Color.FromArgb(56, 65, 76);
            cell.Style.ForeColor = ReportOrange;
            cell.Style.SelectionForeColor = ReportOrange;
            cell.ToolTipText = AppText.Get(hasNote
                ? "Main.Note.OpenTooltip"
                : "Main.Note.AddTooltip");
            _historyGrid.InvalidateCell(cell);
        }

        private void RefreshNoteCells()
        {
            if (_historyGrid == null) return;
            if (_historySortColumn == "note")
            {
                ReapplyHistorySort();
                return;
            }
            foreach (DataGridViewRow row in _historyGrid.Rows)
            {
                var session = row.Tag as ServerSession;
                if (session != null) UpdateNoteCell(row, _noteStore.Exists(session));
            }
        }

        private async Task ShowBlockedServersAsync()
        {
            if (_isRefreshing || _isMeasuring || _isFirewallChanging) return;
            if (_demoMode)
            {
                ShowActionNotice(AppText.Get("Main.Preview.NoBlockedServers"));
                return;
            }

            bool changed;
            using (var form = new BlockedServersForm())
            {
                ColumnWidthPersistence.Attach(form, "blocked", AppPreferencesStore.GetDefaultStorageRoot());
                form.ShowDialog(this);
                changed = form.FirewallStateChanged;
            }
            if (!changed)
            {
                SetHistorySummaryStatus(TextMuted, false);
                return;
            }

            _initialFirewallStateRefresh.Invalidate();

            IList<string> ipAddresses = PingBatchPlanner.GetUniqueServerIps(_visibleSessions);
            _isRefreshing = true;
            UpdateActionButtons();
            SetStatus(AppText.Get("Main.BlockedServers.Refreshing"), Accent);
            try
            {
                Dictionary<string, FirewallQueryResult> states = await Task.Run(
                    () => FirewallRuleManager.QueryMany(ipAddresses));
                foreach (string ipAddress in ipAddresses)
                {
                    FirewallQueryResult state;
                    if (states.TryGetValue(ipAddress, out state))
                        _firewallStates[ipAddress] = state;
                    _pingResults.Remove(ipAddress);
                    UpdateServerRowsFromCache(ipAddress);
                }
                SetStatus(AppText.Get("Main.BlockedServers.Refreshed"), Success);
            }
            catch (Exception ex)
            {
                _firewallStates.Clear();
                SetStatus(AppText.Format("Main.BlockedServers.RefreshFailed", ex.Message), Warning);
            }
            finally
            {
                _isRefreshing = false;
                UpdateActionButtons();
                RefreshActionCells();
                if (IsFirewallActionSortColumn(_historySortColumn))
                    ReapplyHistorySort(false, true);
            }
        }

        private void CopySelectedIp()
        {
            if (_selectedSession == null || !_selectedSession.HasServerIp) return;
            try
            {
                Clipboard.SetText(_selectedSession.IpAddress);
                SetStatus(AppText.Get("Main.Clipboard.Copied"), Success);
            }
            catch (Exception ex)
            {
                SetStatus(AppText.Format("Main.Clipboard.Failed", ex.Message), Danger);
            }
        }

        private void ShowNoServer(string message)
        {
            _selectedSession = null;
            _ipLabel.Text = AppText.Get("Main.Current.NotFound");
            _ipLabel.ForeColor = TextMuted;
            _mapValueLabel.Text = "-";
            _mapValueLabel.AccessibleDescription = "-";
            _toolTip.SetToolTip(_mapValueLabel, string.Empty);
            _timeValueLabel.Text = "-";
            _pingValueLabel.Text = "-";
            _locationValueLabel.Text = "-";
            _actualRttValueLabel.Text = "-";
            _packetLossValueLabel.Text = "-";
            _actualRttValueLabel.AccessibleDescription = "-";
            _packetLossValueLabel.AccessibleDescription = "-";
            UpdateDetailInfo(null);
            _pingValueLabel.ForeColor = TextMuted;
            _locationValueLabel.ForeColor = TextMuted;
            _actualRttValueLabel.ForeColor = TextMuted;
            _packetLossValueLabel.ForeColor = TextMuted;
            _toolTip.SetToolTip(_actualRttValueLabel, string.Empty);
            _toolTip.SetToolTip(_packetLossValueLabel, string.Empty);
            ApplySelectedMetricFonts();
            UpdateActionButtons();
            SetStatus(message, Warning);
        }

        private void UpdateActionButtons()
        {
            bool controlsAvailable = !_isRefreshing && !_isMeasuring && !_isFirewallChanging;
            if (_copyIpButton != null)
                _copyIpButton.Enabled = controlsAvailable && _selectedSession != null && _selectedSession.HasServerIp;
            if (_queryButton != null)
            {
                if (_isMeasuring)
                {
                    bool cancellationRequested = _queryCancellation != null
                        && _queryCancellation.IsCancellationRequested;
                    _queryButton.Text = AppText.Get(cancellationRequested
                        ? "Main.Query.ButtonCancelling"
                        : "Main.Query.ButtonCancel");
                    _queryButton.Enabled = !cancellationRequested;
                    StyleDangerButton(_queryButton);
                    _toolTip.SetToolTip(
                        _queryButton,
                        cancellationRequested
                            ? AppText.Get("Main.Query.WaitingForStop")
                            : AppText.Get("Main.Query.CancelTooltip"));
                }
                else
                {
                    _queryButton.Text = AppText.Get("Common.Button.Scan");
                    bool canRefreshLogs = !_demoMode
                        && (!string.IsNullOrWhiteSpace(_appliedEftPath)
                            || !string.IsNullOrWhiteSpace(_appliedArenaPath));
                    _queryButton.Enabled = controlsAvailable
                        && (canRefreshLogs || _visibleSessions.Any(session => session.HasServerIp));
                    StyleButton(_queryButton, true);
                    _toolTip.SetToolTip(
                        _queryButton,
                        AppText.Get("Main.Scan.Tooltip"));
                }
            }
            if (_applyPathButton != null)
            {
                _applyPathButton.Enabled = controlsAvailable;
                StyleButton(_applyPathButton, controlsAvailable && HasPendingPathChanges());
            }
            if (_rediscoverPathButton != null) _rediscoverPathButton.Enabled = controlsAvailable;
            if (_eftBrowseButton != null) _eftBrowseButton.Enabled = controlsAvailable;
            if (_arenaBrowseButton != null) _arenaBrowseButton.Enabled = controlsAvailable;
            if (_blockedServersButton != null) _blockedServersButton.Enabled = controlsAvailable;
            if (_notesArchiveButton != null) _notesArchiveButton.Enabled = true;
            if (_allFilterButton != null) _allFilterButton.Enabled = controlsAvailable;
            if (_eftFilterButton != null) _eftFilterButton.Enabled = controlsAvailable;
            if (_arenaFilterButton != null) _arenaFilterButton.Enabled = controlsAvailable;
            if (_regionFilterButton != null) _regionFilterButton.Enabled = controlsAvailable;
            if (_recentPeriodButton != null) _recentPeriodButton.Enabled = controlsAvailable;
            if (_todayPeriodButton != null) _todayPeriodButton.Enabled = controlsAvailable;
            if (_sevenDaysPeriodButton != null) _sevenDaysPeriodButton.Enabled = controlsAvailable;
            if (_thirtyDaysPeriodButton != null) _thirtyDaysPeriodButton.Enabled = controlsAvailable;
            if (_customPeriodButton != null) _customPeriodButton.Enabled = controlsAvailable;
            RefreshActionCells();
        }

        private void SetStatus(string message, Color color)
        {
            if (_statusLabel == null) return;
            _statusRevision++;
            message = message ?? string.Empty;
            _statusLabel.Text = message;
            _statusLabel.ForeColor = color;
            _statusLabel.AccessibleDescription = message;
            _toolTip.SetToolTip(_statusLabel, message);
            UpdateStatusAreaHeight();
        }

        private void UpdateStatusAreaHeight()
        {
            if (_updatingStatusAreaHeight || _historyLayout == null || _statusLabel == null)
                return;

            int availableWidth = _historyLayout.ClientSize.Width
                - _historyLayout.Padding.Horizontal
                - _statusLabel.Margin.Horizontal;
            if (availableWidth <= 1) return;

            _updatingStatusAreaHeight = true;
            try
            {
                int dpi = _statusLabel.DeviceDpi <= 0 ? 96 : _statusLabel.DeviceDpi;
                int requiredHeight = CalculateStatusAreaHeight(
                    _statusLabel.Text,
                    _statusLabel.Font,
                    availableWidth,
                    dpi);
                RowStyle statusRow = _historyLayout.RowStyles[2];
                if (statusRow.SizeType != SizeType.Absolute)
                    statusRow.SizeType = SizeType.Absolute;
                if (Math.Abs(statusRow.Height - requiredHeight) > 0.5F)
                    statusRow.Height = requiredHeight;
            }
            finally
            {
                _updatingStatusAreaHeight = false;
            }
        }

        private async Task<RaidLogScanResult> LoadRecentSessionsForQualityEvidenceAsync()
        {
            if (!AreConfiguredLogSourcesAvailable(
                _appliedEftPath,
                _appliedArenaPath))
            {
                return new RaidLogScanResult
                {
                    Sessions = new List<ServerSession>(),
                    TotalMatchingSessionsIsExact = false,
                    ScanCompletedWithoutErrors = false
                };
            }

            try
            {
                var paths = new TarkovLogPaths
                {
                    EftPath = _appliedEftPath,
                    ArenaPath = _appliedArenaPath
                };
                Task<RaidLogScanResult> pending = _qualityEvidenceScan;
                if (pending == null || pending.IsCompleted
                    || !string.Equals(_qualityEvidenceEftPath, paths.EftPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(_qualityEvidenceArenaPath, paths.ArenaPath, StringComparison.OrdinalIgnoreCase))
                {
                    _qualityEvidenceEftPath = paths.EftPath;
                    _qualityEvidenceArenaPath = paths.ArenaPath;
                    pending = Task.Run(() => RaidLogScanner.Scan(paths,
                        new RaidLogScanQuery { MaximumRecords = RaidQualityEvidence.MaximumRecentRaids }));
                    _qualityEvidenceScan = pending;
                }
                RaidLogScanResult scan;
                try { scan = await pending; }
                finally { if (ReferenceEquals(_qualityEvidenceScan, pending)) _qualityEvidenceScan = null; }
                if (scan == null || scan.Sessions == null)
                {
                    return new RaidLogScanResult
                    {
                        Sessions = new List<ServerSession>(),
                        TotalMatchingSessionsIsExact = false,
                        ScanCompletedWithoutErrors = false
                    };
                }
                if (!scan.TotalMatchingSessionsIsExact)
                    scan.ScanCompletedWithoutErrors = false;
                return scan;
            }
            catch
            {
                // An incomplete scan must not be presented as conclusive recent-raid
                // evidence in the block-completion message.
                return new RaidLogScanResult
                {
                    Sessions = new List<ServerSession>(),
                    TotalMatchingSessionsIsExact = false,
                    ScanCompletedWithoutErrors = false
                };
            }
        }

        private static bool AreConfiguredLogSourcesAvailable(
            string eftPath,
            string arenaPath)
        {
            bool hasConfiguredPath = false;
            foreach (string path in new[] { eftPath, arenaPath })
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                hasConfiguredPath = true;
                try
                {
                    if (!Directory.Exists(path)) return false;
                }
                catch
                {
                    return false;
                }
            }
            return hasConfiguredPath;
        }

        private static int CalculateStatusAreaHeight(
            string message,
            Font font,
            int availableWidth,
            int dpi)
        {
            int scaledDpi = Math.Max(96, dpi);
            int minimumHeight = ScaleLogical(28, scaledDpi);
            if (font == null || availableWidth <= 1)
                return minimumHeight;

            string measuredText = string.IsNullOrEmpty(message) ? " " : message;
            Size textSize = TextRenderer.MeasureText(
                measuredText,
                font,
                new Size(Math.Max(1, availableWidth), int.MaxValue),
                TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.WordBreak);
            return Math.Max(minimumHeight, textSize.Height + ScaleLogical(6, scaledDpi));
        }

        private static string GetApplicationSemanticVersion()
        {
            Version version = typeof(MainForm).Assembly.GetName().Version;
            return string.Format(
                "{0}.{1}.{2}",
                Math.Max(0, version.Major),
                Math.Max(0, version.Minor),
                Math.Max(0, version.Build));
        }

        private static string GetApplicationDisplayVersion()
        {
            Version version = typeof(MainForm).Assembly.GetName().Version;
            if (version == null) return "0.0.0";
            string semanticVersion = string.Format(
                "{0}.{1}.{2}",
                Math.Max(0, version.Major),
                Math.Max(0, version.Minor),
                Math.Max(0, version.Build));
            return version.Revision > 0
                ? semanticVersion + "." + version.Revision.ToString(CultureInfo.InvariantCulture)
                : semanticVersion;
        }

        private void LoadDemoData()
        {
            DateTime now = new DateTime(2026, 8, 14, 0, 32, 10);
            string previewSuffix = AppText.Get("Main.Preview.PathSuffix");
            _eftPathTextBox.Text = @"C:\Battlestate Games\EFT\Logs  (" + previewSuffix + ")";
            _arenaPathTextBox.Text = @"C:\Battlestate Games\Escape from Tarkov Arena\Logs  (" + previewSuffix + ")";
            _launcherSelectionLabel.Text = AppText.Get("Main.Preview.LauncherSelection");
            _allSessions = new List<ServerSession>
            {
                new ServerSession
                {
                    Game = TarkovGame.Eft,
                    SessionStarted = now,
                    LastUpdated = now.AddMinutes(34),
                    SessionFolderName = "log_2026.08.14_00-32-10",
                    SessionKey = "eft-demo-1",
                    LogFilePath = "application.log",
                    IpAddress = "203.0.113.42",
                    Port = 17000,
                    MapName = "Streets of Tarkov",
                    GameMode = "Online",
                    ProgressionMode = TarkovProgressionMode.PvpSeason,
                    PvpSeasonNumber = 1,
                    CharacterType = TarkovCharacterType.Pmc,
                    ParticipationType = TarkovParticipationType.Party,
                    PartySize = 2,
                    HostingMode = TarkovHostingMode.Server,
                    RaidPurpose = TarkovRaidPurpose.Progression,
                    ServerId = "SG-SIN01G001_demo",
                    ShortId = "DDR02W",
                    DataCenterCode = "SG-SIN01",
                    ClientVersion = "1.1.0.1.46699",
                    MatchmakingSeconds = 82.4,
                    ActualRttMs = 147.2,
                    NetworkLoss = 0.02,
                    ConnectionAttempts = 2,
                    UserReportCount = 1,
                    ConnectedOnce = true,
                    HasDisconnectRecord = true,
                    TimedOut = true,
                    DisconnectReason = 2,
                    IpDetectedAt = now.AddMinutes(2),
                    OperationStartedAt = now.AddMinutes(5),
                    OperationEndedAt = now.AddMinutes(23).AddSeconds(42),
                    OperationState = RaidOperationState.Completed
                },
                new ServerSession
                {
                    Game = TarkovGame.Arena,
                    SessionStarted = now.AddHours(-1.5),
                    LastUpdated = now.AddHours(-1.2),
                    SessionFolderName = "log_2026.08.13_23-02-44",
                    SessionKey = "arena-demo-1",
                    LogFilePath = "lifecycle.log",
                    IpAddress = "198.51.100.18",
                    Port = 17013,
                    MapName = "Bay 5",
                    GameMode = "CheckPoint",
                    HostingMode = TarkovHostingMode.Server,
                    ServerId = "JP-TK02G005_demo",
                    ShortId = "92HY2N",
                    DataCenterCode = "JP-TK02",
                    ActualRttMs = 68.3,
                    NetworkLoss = 0,
                    ConnectionAttempts = 1,
                    ConnectedOnce = true,
                    HasDisconnectRecord = true,
                    DisconnectReason = 0,
                    IpDetectedAt = now.AddHours(-1.4),
                    OperationStartedAt = now.AddHours(-1.35),
                    OperationEndedAt = now.AddHours(-1.05),
                    OperationState = RaidOperationState.Completed
                },
                new ServerSession
                {
                    Game = TarkovGame.Eft,
                    SessionStarted = now.AddHours(-3),
                    LastUpdated = now.AddHours(-2.8),
                    SessionFolderName = "log_2026.08.13_21-33-01",
                    SessionKey = "eft-demo-2",
                    LogFilePath = "application.log",
                    IpAddress = "192.0.2.77",
                    Port = 17000,
                    MapName = "Woods",
                    GameMode = "Online",
                    ProgressionMode = TarkovProgressionMode.Pve,
                    CharacterType = TarkovCharacterType.Scav,
                    ParticipationType = TarkovParticipationType.Party,
                    PartySize = 3,
                    HostingMode = TarkovHostingMode.Server,
                    RaidPurpose = TarkovRaidPurpose.Progression,
                    ServerId = "KR-SEL01G002_demo",
                    ShortId = "A71P4C",
                    DataCenterCode = "KR-SEL01",
                    MatchmakingSeconds = 31.8,
                    ActualRttMs = 82.1,
                    NetworkLoss = 0,
                    ConnectionAttempts = 1,
                    ConnectedOnce = true,
                    HasDisconnectRecord = true,
                    DisconnectReason = 5,
                    IpDetectedAt = now.AddHours(-2.9),
                    OperationStartedAt = now.AddHours(-2.85),
                    OperationState = RaidOperationState.InProgress
                },
                new ServerSession
                {
                    Game = TarkovGame.Eft,
                    SessionStarted = now.AddHours(-4),
                    LastUpdated = now.AddHours(-3.6),
                    SessionFolderName = "log_2026.08.13_20-31-12",
                    SessionKey = "eft-demo-local",
                    LogFilePath = "application.log",
                    MapName = "Factory",
                    ProgressionMode = TarkovProgressionMode.Pve,
                    CharacterType = TarkovCharacterType.Pmc,
                    ParticipationType = TarkovParticipationType.Solo,
                    HostingMode = TarkovHostingMode.Local,
                    RaidPurpose = TarkovRaidPurpose.Progression,
                    ClientVersion = "1.1.0.1.46699",
                    MatchmakingSeconds = 0,
                    IpDetectedAt = now.AddHours(-3.9)
                }
            };

            _pingResults["203.0.113.42"] = new PingResult { Sent = 5, Received = 5, MinimumMs = 142, AverageMs = 147, MaximumMs = 153 };
            _pingResults["198.51.100.18"] = new PingResult { Sent = 5, Received = 5, MinimumMs = 63, AverageMs = 68, MaximumMs = 73 };
            _pingResults["192.0.2.77"] = new PingResult { Sent = 5, Received = 5, MinimumMs = 78, AverageMs = 82, MaximumMs = 89 };
            _geoResults["203.0.113.42"] = new GeoInfo { Success = true, City = "Singapore", CountryCode = "SG" };
            _geoResults["198.51.100.18"] = new GeoInfo { Success = true, City = "Tokyo", CountryCode = "JP" };
            _geoResults["192.0.2.77"] = new GeoInfo { Success = true, City = "Seoul", CountryCode = "KR" };
            _firewallStates["203.0.113.42"] = new FirewallQueryResult { Success = true, IsBlocked = true };
            _firewallStates["198.51.100.18"] = new FirewallQueryResult { Success = true, IsBlocked = false };
            _firewallStates["192.0.2.77"] = new FirewallQueryResult { Success = true, IsBlocked = false };

            RefreshVisibleSessions();
            SetStatus(AppText.Get("Main.Preview.Status"), Accent);
        }

        public void SavePreview(string outputPath)
        {
            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            using (var bitmap = new Bitmap(Width, Height))
            {
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
