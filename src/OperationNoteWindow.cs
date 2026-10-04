// Copyright © 2026 Spirit-Schema. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;

namespace TarkovServerReporter
{
    internal enum OperationNoteView { Hidden, Locked, Active }

    // A real per-pixel transparent HWND. Background alpha never dims the editor text.
    internal sealed class OperationNoteWindow : Window
    {
        private readonly OperationNoteDraft _draft;
        private readonly Border _paper;
        private readonly Grid _root;
        private readonly Grid _header;
        private readonly StackPanel _tools;
        private readonly TextBox _body;
        private readonly TextBox _search;
        private readonly TextBlock _searchHint;
        private readonly StackPanel _checks;
        private readonly Border _flyout;
        private readonly StackPanel _flyoutItems;
        private readonly TextBlock _clock;
        private readonly TextBlock _status;
        private readonly TextBlock _exits;
        private readonly Button _save;
        private readonly Button _hideButton;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _searchDelay;
        private readonly SolidColorBrush _ink = new SolidColorBrush(Color.FromRgb(243, 243, 237));
        private bool _loading;
        private bool _pointerArmed;
        private bool _closing;
        private DateTime _activatedUtc;
        private IntPtr _returnWindow;
        private uint _returnProcess;
        private System.Drawing.Point _activationPointer;
        private IntPtr _handle;
        private string _flyoutKind;
        private string _exitText;
        private bool _exitsVisible;
        private string _saveLabel;
        internal event Action DraftChanged;
        internal event Action SaveRequested;
        internal event Action ActivatedForEditing;
        internal Func<string, IList<OperationSearchResult>> SearchProvider;
        internal Func<string> SearchStatusProvider;
        internal OperationNoteView ViewState { get; private set; }
        internal TextBox BodyEditor { get { return _body; } }
        internal OperationNoteDraft Draft { get { return _draft; } }
        internal IntPtr NativeHandle { get { return _handle; } }
        internal int VisibleCheckCount { get { return _checks.Children.Count; } }

        internal static string T(string korean, string english)
        {
            return AppText.CurrentLanguage == AppText.KoreanLanguage ? korean : english;
        }

        internal OperationNoteWindow(OperationNoteDraft draft)
        {
            _draft = draft;
            _loading = true;
            Title = T("작전노트 · TSG 0.8.8 베타", "Operation note · TSG 0.8.8 beta");
            Width = 550;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = new FontFamily("Malgun Gothic");
            FontSize = 13;
            Foreground = _ink;
            var area = SystemParameters.WorkArea;
            Left = Math.Max(area.Left, Math.Min(_draft.WindowLeft ?? (area.Right - Width - 40), area.Right - Width));
            Top = Math.Max(area.Top, Math.Min(_draft.WindowTop ?? (area.Top + 90), area.Bottom - 440));
            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(410) });
            _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = _root;
            _paper = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12) };
            _root.Children.Add(_paper);
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            _paper.Child = layout;
            _header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.Children.Add(_header);
            var title = new TextBlock { Text = T("작전노트", "OP NOTE"), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(232, 157, 54)), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll };
            title.ToolTip = T("끌어서 위치 이동", "Drag to move");
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (ViewState != OperationNoteView.Active) return;
                try { DragMove(); } catch (InvalidOperationException) { }
                _draft.WindowLeft = Left; _draft.WindowTop = Top; Changed();
            };
            _header.Children.Add(title);
            _search = MakeTextBox(T("아이템 · 맵 · 보스 · 내 메모 검색", "Search items, maps, bosses, notes"));
            _search.Margin = new Thickness(0, 1, 10, 1);
            _search.MaxWidth = 180;
            _search.Padding = new Thickness(8, 4, 8, 4);
            _search.Background = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            _search.FontSize = 11;
            _search.ToolTip = T("아이템 · 맵 · 보스 · 탈출구 · 내 메모", "Items · maps · bosses · exits · my notes");
            Grid.SetColumn(_search, 1); _header.Children.Add(_search);
            _searchHint = new TextBlock { Text = T("검색 · 아이템/맵/보스", "Search · items/maps/bosses"),
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(151, 161, 158)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Width = 160, Margin = new Thickness(0, 0, 10, 0), IsHitTestVisible = false };
            Grid.SetColumn(_searchHint, 1); _header.Children.Add(_searchHint);
            _tools = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetColumn(_tools, 2); _header.Children.Add(_tools);
            _tools.Children.Add(MakeIcon("\uE73A", T("체크 항목 추가", "Add checklist item"), AddCheck));
            _tools.Children.Add(MakeIcon("\uE718", T("고정 메모", "Pinned notes"), ShowPins));
            _tools.Children.Add(MakeIcon("\uE8AB", T("탈출구 조건 표시", "Show extraction conditions"), ToggleExits));
            _tools.Children.Add(MakeIcon("\uE793", T("배경 투명도", "Background transparency"), ShowOpacity));
            _hideButton = MakeIcon("\uE8BB", T("노트 숨김", "Hide note"), HideNote);
            _tools.Children.Add(_hideButton);
            var content = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            Grid.SetRow(content, 1); layout.Children.Add(content);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 5, 26) };
            content.Children.Add(scroll);
            var writing = new StackPanel(); scroll.Content = writing;
            _body = MakeTextBox(T("작전노트 본문", "Operation note body"));
            _body.AcceptsReturn = true; _body.AcceptsTab = true;
            _body.TextWrapping = TextWrapping.Wrap; _body.MinHeight = 150;
            _body.Padding = new Thickness(0, 0, 0, 8); _body.FontSize = 15;
            _body.MaxLength = OperationNoteStore.MaximumBodyLength;
            _body.Text = _draft.BodyText ?? string.Empty;
            _body.TextChanged += delegate { if (!_loading) { _draft.BodyText = _body.Text; Changed(); } };
            _body.PreviewMouseDown += delegate { CloseFlyout(); };
            writing.Children.Add(_body);
            _checks = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; writing.Children.Add(_checks);
            _clock = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(177, 184, 183)), IsHitTestVisible = false };
            _clock.Text = DateTime.Now.ToString("HH:mm:ss");
            content.Children.Add(_clock);
            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(footer, 2); layout.Children.Add(footer);
            _status = new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(180, 187, 184)), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0), Text = T("바로 적으세요 · 자동 보존", "Start typing · draft saved automatically") };
            footer.Children.Add(_status);
            _save = MakeButton(T("레이드에 저장하고 이어쓰기", "Save to raid and continue"), delegate { if (SaveRequested != null) SaveRequested(); });
            _save.FontSize = 11; _save.Padding = new Thickness(9, 5, 9, 5);
            _save.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_save, 1); footer.Children.Add(_save);
            _flyoutItems = new StackPanel();
            _flyout = new Border { Background = new SolidColorBrush(Color.FromRgb(28, 33, 38)), BorderBrush = new SolidColorBrush(Color.FromRgb(74, 82, 83)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 40, 0, 0), VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, Child = new ScrollViewer { Content = _flyoutItems, MaxHeight = 270, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            Grid.SetRowSpan(_flyout, 3); layout.Children.Add(_flyout);
            _exits = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = _ink, FontSize = 12, Margin = new Thickness(15, 12, 15, 5), Visibility = Visibility.Collapsed, MaxHeight = 240, IsHitTestVisible = false };
            _exits.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 1, Color = Colors.Black };
            Grid.SetRow(_exits, 1); _root.Children.Add(_exits);
            _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _searchDelay.Tick += delegate { _searchDelay.Stop(); ShowSearch(); };
            _search.TextChanged += delegate
            {
                _searchHint.Visibility = ViewState == OperationNoteView.Active && string.IsNullOrEmpty(_search.Text) ? Visibility.Visible : Visibility.Collapsed;
                _searchDelay.Stop(); _searchDelay.Start();
            };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _timer.Tick += delegate { TickInteraction(); };
            SourceInitialized += delegate
            {
                _handle = new WindowInteropHelper(this).Handle;
                OperationNoteNative.SetInputThrough(_handle, ViewState != OperationNoteView.Active);
            };
            Deactivated += delegate { if (ViewState == OperationNoteView.Active && !_closing) LockNote(false); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; if (_flyout.Visibility == Visibility.Visible) { CloseFlyout(); _body.Focus(); } else HideNote(); }
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!_closing) { e.Cancel = true; HideNote(); return; }
                _timer.Stop(); _searchDelay.Stop();
            };
            _loading = false;
            ViewState = OperationNoteView.Hidden;
            RenderState();
        }

        private TextBox MakeTextBox(string name)
        {
            var box = new TextBox { Background = Brushes.Transparent, Foreground = _ink, CaretBrush = _ink, BorderThickness = new Thickness(0), SelectionBrush = new SolidColorBrush(Color.FromRgb(126, 95, 46)), VerticalContentAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(box, name);
            return box;
        }
        private Button MakeButton(string label, Action action)
        {
            var button = new Button { Content = label, Background = new SolidColorBrush(Color.FromArgb(60, 170, 140, 80)), Foreground = _ink, BorderThickness = new Thickness(0), Padding = new Thickness(7), Cursor = Cursors.Hand, HorizontalContentAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(button, label);
            button.Click += delegate { action(); };
            return button;
        }
        private Button MakeIcon(string glyph, string label, Action action)
        {
            Button button = MakeButton(glyph, action);
            button.FontFamily = new FontFamily("Segoe MDL2 Assets"); button.FontSize = 14;
            button.Width = 31; button.Height = 30; button.Background = Brushes.Transparent;
            button.ToolTip = label; AutomationProperties.SetName(button, label);
            return button;
        }
        internal void ActivateNote()
        {
            if (_closing) return;
            IntPtr foreground = OperationNoteNative.GetForegroundWindow();
            if (foreground != _handle)
            {
                _returnWindow = foreground;
                _returnProcess = OperationNoteNative.GetProcessId(foreground);
            }
            ViewState = OperationNoteView.Active;
            _pointerArmed = false; _activatedUtc = DateTime.UtcNow;
            _activationPointer = System.Windows.Forms.Cursor.Position;
            RenderState();
            Show();
            OperationNoteNative.SetInputThrough(_handle, false);
            Activate();
            _body.Focus(); Keyboard.Focus(_body); _body.CaretIndex = _body.Text.Length;
            _timer.Start();
            if (ActivatedForEditing != null) ActivatedForEditing();
        }
        internal void ToggleNote()
        {
            if (ViewState == OperationNoteView.Active) HideNote(); else ActivateNote();
        }
        internal void HideNote()
        {
            ReturnToGameIfAppropriate();
            ViewState = OperationNoteView.Hidden; CloseFlyout(); Hide(); _timer.Stop(); Changed();
        }
        internal void LockNote(bool returnToGame)
        {
            if (ViewState != OperationNoteView.Active) return;
            if (returnToGame) ReturnToGameIfAppropriate();
            ViewState = OperationNoteView.Locked;
            CloseFlyout(); RenderState();
            OperationNoteNative.SetInputThrough(_handle, true);
            Changed();
        }
        private void ReturnToGameIfAppropriate()
        {
            if (OperationNoteNative.MayReturnFocus(OperationNoteNative.GetForegroundWindow(), _handle,
                OperationNoteNative.IsSameReturnTarget(_returnWindow, _returnProcess)))
                OperationNoteNative.SetForegroundWindow(_returnWindow);
        }
        internal static bool ShouldLockForPointer(bool inside, bool armed, double elapsedSeconds)
        {
            return !inside && (armed || elapsedSeconds >= 8);
        }
        private void TickInteraction()
        {
            _clock.Text = DateTime.Now.ToString("HH:mm:ss");
            if (ViewState != OperationNoteView.Active || !IsVisible) return;
            if (Mouse.Captured != null) return;
            var pointer = System.Windows.Forms.Cursor.Position;
            bool pointerMoved = Math.Abs(pointer.X - _activationPointer.X) > 12 || Math.Abs(pointer.Y - _activationPointer.Y) > 12;
            Point local = PointFromScreen(new Point(pointer.X, pointer.Y));
            bool inside = local.X >= -40 && local.X <= ActualWidth + 40 && local.Y >= -40 && local.Y <= ActualHeight + 40;
            if (inside) _pointerArmed = true;
            // Keyboard-only opening gets time to begin typing; after entry, leaving the margin locks immediately.
            if (ShouldLockForPointer(inside, _pointerArmed || pointerMoved, (DateTime.UtcNow - _activatedUtc).TotalSeconds)
                && (_pointerArmed || pointerMoved)) LockNote(true);
        }
        private void RenderState()
        {
            bool active = ViewState == OperationNoteView.Active;
            double opacity = Math.Max(.15, Math.Min(1, _draft.ActiveOpacity));
            _paper.Background = new SolidColorBrush(Color.FromArgb((byte)(255 * (active ? opacity : opacity * .30)), 22, 27, 29));
            _paper.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(active ? 110 : 28), 150, 151, 134));
            _body.IsReadOnly = !active;
            _tools.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            _search.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            _searchHint.Visibility = active && string.IsNullOrEmpty(_search.Text) ? Visibility.Visible : Visibility.Collapsed;
            _status.Visibility = active ? Visibility.Visible : Visibility.Hidden;
            _save.Visibility = active && !string.IsNullOrEmpty(_saveLabel) ? Visibility.Visible : Visibility.Collapsed;
            _paper.IsHitTestVisible = active;
            RenderChecks();
            UpdateExits();
        }
        private void RenderChecks()
        {
            _checks.Children.Clear();
            foreach (OperationNoteCheck item in _draft.Checks)
            {
                if (item.Done && ViewState != OperationNoteView.Active) continue;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var check = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Center, Foreground = _ink, IsEnabled = ViewState == OperationNoteView.Active };
                AutomationProperties.SetName(check, T("완료: ", "Completed: ") + item.Text);
                check.Click += delegate { item.Done = check.IsChecked == true; Changed(); RenderChecks(); };
                row.Children.Add(check);
                if (item.Done)
                {
                    var completed = new TextBlock { Text = item.Text, TextDecorations = TextDecorations.Strikethrough, Foreground = new SolidColorBrush(Color.FromRgb(153, 159, 157)), TextWrapping = TextWrapping.Wrap };
                    Grid.SetColumn(completed, 1); row.Children.Add(completed);
                }
                else
                {
                    TextBox text = MakeTextBox(T("체크 항목", "Checklist item")); text.MaxLength = 10000; text.Text = item.Text;
                    text.IsReadOnly = ViewState != OperationNoteView.Active; text.Tag = item.Id; text.TextWrapping = TextWrapping.Wrap;
                    text.TextChanged += delegate { item.Text = text.Text; Changed(); };
                    text.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
                    {
                        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && !string.IsNullOrWhiteSpace(text.Text)) { e.Handled = true; AddCheck(); }
                        if (e.Key == Key.Back && text.Text.Length == 0) { e.Handled = true; _draft.Checks.Remove(item); Changed(); RenderChecks(); _body.Focus(); }
                    };
                    Grid.SetColumn(text, 1); row.Children.Add(text);
                }
                _checks.Children.Add(row);
            }
        }
        internal void AddCheck()
        {
            CloseFlyout();
            if (_draft.Checks.Count >= OperationNoteStore.MaximumCheckCount) { SetStatus(T("체크 항목은 최대 300개입니다.", "A note can contain up to 300 checklist items."), true); return; }
            var item = new OperationNoteCheck { Id = Guid.NewGuid().ToString("N"), Text = string.Empty };
            _draft.Checks.Add(item); RenderChecks(); Changed();
            Grid row = _checks.Children[_checks.Children.Count - 1] as Grid;
            if (row != null) { TextBox editor = row.Children.OfType<TextBox>().FirstOrDefault(); if (editor != null) { editor.Focus(); editor.BringIntoView(); } }
        }
        private void Changed()
        {
            if (!_loading && DraftChanged != null) DraftChanged();
        }
        internal void SetStatus(string value, bool error)
        {
            _status.Text = value; _status.ToolTip = value;
            _status.Foreground = new SolidColorBrush(error ? Color.FromRgb(255, 162, 139) : Color.FromRgb(180, 187, 184));
        }
        internal void SetShortcutHint(string shortcut)
        {
            _hideButton.ToolTip = T("숨김 · 다시 열기: ", "Hide · reopen: ") + shortcut;
        }
        internal void SetRaidTarget(string label, bool enabled)
        {
            _saveLabel = enabled ? label : null;
            _save.ToolTip = label;
            _save.Visibility = ViewState == OperationNoteView.Active && enabled ? Visibility.Visible : Visibility.Collapsed;
            if (enabled) SetStatus(label, false);
        }
        internal void SetExitText(string text)
        {
            _exitText = text; UpdateExits();
        }
        private void ToggleExits() { _exitsVisible = !_exitsVisible; CloseFlyout(); UpdateExits(); }
        private void UpdateExits()
        {
            string text = string.IsNullOrWhiteSpace(_exitText) ? T("현재 맵 미확인 · 검색으로 맵을 확인하세요.", "Current map unknown · use search to look up a map.") : _exitText;
            string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            _exits.Text = lines.Length <= 8 ? text : string.Join(Environment.NewLine, lines.Take(7))
                + Environment.NewLine + T("… 나머지 조건은 맵·탈출구 이름으로 검색하세요.", "… search the map or exit name for remaining conditions.");
            _exits.Visibility = _exitsVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        private bool OpenFlyout(string kind)
        {
            if (_flyout.Visibility == Visibility.Visible && _flyoutKind == kind && kind != "search") { CloseFlyout(); return false; }
            _flyoutKind = kind; _flyoutItems.Children.Clear(); _flyout.Visibility = Visibility.Visible; return true;
        }
        private void CloseFlyout() { _flyout.Visibility = Visibility.Collapsed; _flyoutKind = null; }
        private void ShowOpacity()
        {
            if (!OpenFlyout("opacity")) return;
            var value = new TextBlock(); _flyoutItems.Children.Add(value);
            var slider = new Slider { Minimum = 0, Maximum = 85, Value = (1 - _draft.ActiveOpacity) * 100, Margin = new Thickness(0, 12, 0, 12), TickFrequency = 5, IsSnapToTickEnabled = false };
            Action update = delegate { value.Text = T("배경 투명도 ", "Background transparency ") + ((int)slider.Value) + "%"; };
            slider.ValueChanged += delegate { _draft.ActiveOpacity = 1 - slider.Value / 100; update(); RenderState(); Changed(); };
            update(); _flyoutItems.Children.Add(slider);
            _flyoutItems.Children.Add(new TextBlock { Text = T("잠기면 배경이 더 옅어집니다. 글자는 그대로 표시됩니다.", "When locked, the background fades further. Text stays readable."), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        }
        private void ShowPins()
        {
            if (!OpenFlyout("pins")) return;
            _flyoutItems.Children.Add(MakeButton(T("+ 현재 내용을 고정", "+ Pin current content"), delegate
            {
                if (string.IsNullOrWhiteSpace(_draft.BodyText) && !_draft.Checks.Any(x => !string.IsNullOrWhiteSpace(x.Text))) return;
                if (!CanPin()) return;
                string title = (_draft.BodyText ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? T("체크리스트", "Checklist");
                if (title.Length > 50) title = title.Substring(0, 50);
                _draft.Pins.Add(new OperationPinnedNote { Id = Guid.NewGuid().ToString("N"), Title = title, BodyText = _draft.BodyText, Checks = _draft.Checks.Select(x => new OperationNoteCheck { Id = Guid.NewGuid().ToString("N"), Text = x.Text, Done = false }).ToList() });
                Changed(); CloseFlyout(); ShowPins();
            }));
            foreach (OperationPinnedNote pin in _draft.Pins.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                Button remove = MakeButton("×", delegate { _draft.Pins.Remove(pin); Changed(); CloseFlyout(); ShowPins(); });
                remove.ToolTip = T("고정 해제", "Unpin"); DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                Button insert = MakeButton(pin.Title, delegate
                {
                    if (_draft.Checks.Count + pin.Checks.Count > OperationNoteStore.MaximumCheckCount) { SetStatus(T("체크 항목이 너무 많아 추가하지 않았습니다.", "Too many checklist items to insert this pin."), true); return; }
                    if (!AppendText(pin.BodyText)) return;
                    _draft.Checks.AddRange(pin.Checks.Select(x => new OperationNoteCheck { Id = Guid.NewGuid().ToString("N"), Text = x.Text, Done = false }));
                    RenderChecks(); Changed(); CloseFlyout(); _body.Focus();
                });
                insert.ToolTip = T("현재 내용 뒤에 추가", "Append to current note"); row.Children.Add(insert); _flyoutItems.Children.Add(row);
            }
            if (_draft.Pins.Count == 0) _flyoutItems.Children.Add(new TextBlock { Text = T("자주 쓰는 내용을 고정해 다시 사용하세요.", "Pin notes you want to reuse."), Margin = new Thickness(3, 12, 3, 4), FontSize = 11 });
        }
        private void ShowSearch()
        {
            if (ViewState != OperationNoteView.Active) return;
            if (string.IsNullOrWhiteSpace(_search.Text)) { CloseFlyout(); return; }
            OpenFlyout("search");
            _flyoutItems.Children.Add(new TextBlock { Text = SearchStatusProvider == null ? "tarkov.dev · " + T("기본 정보 · 일반 조건", "Basic information · general conditions") : SearchStatusProvider(), FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(180, 185, 180)), Margin = new Thickness(0, 0, 0, 10) });
            IList<OperationSearchResult> results = SearchProvider == null ? new List<OperationSearchResult>() : SearchProvider(_search.Text);
            foreach (OperationSearchResult result in results)
            {
                var row = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
                row.Children.Add(new TextBlock { Text = result.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var summary = new TextBlock { Text = result.Summary, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 108, Margin = new Thickness(0, 4, 0, 5) };
                row.Children.Add(summary);
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                actions.Children.Add(MakeButton(T("자세히", "Details"), delegate { summary.MaxHeight = double.IsPositiveInfinity(summary.MaxHeight) ? 108 : double.PositiveInfinity; }));
                actions.Children.Add(MakeButton(T("노트에 추가", "Add to note"), delegate { AppendText(result.ToNoteText()); CloseFlyout(); _body.Focus(); }));
                Button pin = MakeButton(T("고정", "Pin"), delegate
                {
                    if (!CanPin()) return;
                    _draft.Pins.Add(new OperationPinnedNote { Id = Guid.NewGuid().ToString("N"), Title = result.Title, BodyText = result.ToNoteText(), Checks = new List<OperationNoteCheck>() });
                    Changed(); SetStatus(T("고정 메모에 저장됨", "Saved to pinned notes"), false);
                });
                pin.Margin = new Thickness(6, 0, 0, 0); actions.Children.Add(pin); row.Children.Add(actions); _flyoutItems.Children.Add(row);
            }
            if (results.Count == 0) _flyoutItems.Children.Add(new TextBlock { Text = T("검색 결과 없음 · 데이터 준비 중에는 내 메모만 검색됩니다.", "No results · while data loads, only local notes are available."), TextWrapping = TextWrapping.Wrap });
        }
        private bool AppendText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            string combined = string.IsNullOrWhiteSpace(_body.Text) ? value : _body.Text.TrimEnd() + Environment.NewLine + Environment.NewLine + value;
            if (combined.Length > OperationNoteStore.MaximumBodyLength) { SetStatus(T("본문이 너무 길어 추가하지 않았습니다.", "The note is too long to append this content."), true); return false; }
            _body.Text = combined;
            _body.CaretIndex = _body.Text.Length;
            return true;
        }
        private bool CanPin()
        {
            if (_draft.Pins.Count < OperationNoteStore.MaximumPinCount) return true;
            SetStatus(T("고정 메모는 최대 50개입니다.", "You can keep up to 50 pinned notes."), true); return false;
        }
        internal void SavePreview(string path)
        {
            UpdateLayout();
            var image = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(_root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = File.Create(path)) encoder.Save(stream);
        }
        internal void Shutdown() { _closing = true; _timer.Stop(); _searchDelay.Stop(); Close(); }
    }
}
