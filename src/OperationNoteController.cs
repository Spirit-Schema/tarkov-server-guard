// Copyright © 2026 Spirit-Schema. All rights reserved.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;

namespace TarkovServerReporter
{
    internal sealed class OperationNoteController : IDisposable
    {
        private readonly MainForm _main;
        private readonly bool _demo;
        private readonly OperationNoteStore _store;
        private readonly RaidNoteStore _legacy;
        private readonly UserReportMemoStore _reports;
        private readonly OperationSearchService _search;
        private readonly OperationNoteWindow _window;
        private readonly OperationNoteHotKeys _hotKeys;
        private readonly System.Windows.Forms.Timer _scanTimer;
        private readonly System.Windows.Forms.Timer _saveTimer;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private List<ServerSession> _knownRaids = new List<ServerSession>();
        private List<OperationSearchMemo> _privateMemos = new List<OperationSearchMemo>();
        private ServerSession _target;
        private string _currentMap;
        private bool _scanBusy;
        private bool _saveBusy;
        private bool _dirty;
        private bool _disposed;
        private bool _storageFailed;
        private Form _demoControls;
        private int _demoRound;
        internal OperationNoteWindow Window { get { return _window; } }

        internal OperationNoteController(MainForm main, bool demo, AppPreferences preferences)
        {
            _main = main; _demo = demo;
            string basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TarkovServerGuard");
            string dataPath = demo ? Path.Combine(basePath, "Beta088Demo") : basePath;
            _legacy = new RaidNoteStore(Path.Combine(dataPath, "RaidNotes"));
            _reports = new UserReportMemoStore(Path.Combine(dataPath, "UserReportMemos"));
            _store = new OperationNoteStore(Path.Combine(dataPath, "OperationNotes"), _legacy);
            _search = new OperationSearchService(Path.Combine(basePath, "OperationSearchCache"));
            OperationNoteDraft draft;
            try { draft = _store.LoadDraft(); }
            catch (Exception ex)
            {
                // Existing unreadable data is never replaced by an empty recovery draft.
                draft = new OperationNoteDraft(); _storageFailed = true;
                MessageBox.Show(main, OperationNoteWindow.T("작전노트 초안을 읽지 못했습니다. 기존 파일을 보존하며 이번 창은 자동 저장하지 않습니다.\n", "Cannot read the operation draft. Existing files are preserved; this window will not overwrite them.\n") + ex.Message,
                    OperationNoteWindow.T("작전노트 저장소", "Operation note storage"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _window = new OperationNoteWindow(draft);
            ElementHost.EnableModelessKeyboardInterop(_window);
            _window.DraftChanged += MarkDirty;
            _window.SaveRequested += SaveToRaid;
            _window.ActivatedForEditing += delegate { RefreshPrivateMemos(); if (_search.ShouldRefresh) RefreshSearch(); if (!_demo) ScanLatest(); };
            _window.SearchProvider = query => _search.Search(query, _currentMap,
                _privateMemos.Concat(_window.Draft.Pins.Select(pin => new OperationSearchMemo { Id = "pin:" + pin.Id, Title = pin.Title, Text = OperationNoteDraft.RenderText(pin.BodyText, pin.Checks) })), 12);
            _window.SearchStatusProvider = () => "tarkov.dev · " + _search.StatusText;
            _hotKeys = new OperationNoteHotKeys(preferences);
            _window.SetShortcutHint(FormatShortcut(preferences.OverlayToggleHotKey ?? AppPreferencesStore.CreateDefaultOverlayToggleHotKey()));
            _hotKeys.Toggle += _window.ToggleNote;
            _hotKeys.Edit += _window.ActivateNote;
            _main.OperationNoteRequested += OnOperationNoteRequested;
            _main.CaptureFoldersRequested += ShowCaptureFolders;
            _scanTimer = new System.Windows.Forms.Timer { Interval = 4000 };
            _scanTimer.Tick += delegate { ScanLatest(); };
            _saveTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _saveTimer.Tick += delegate { FlushDraft(); };
            _saveTimer.Start();
            if (!_demo) { _scanTimer.Start(); ScanLatest(); }
            RefreshPrivateMemos();
            RefreshSearch();
            if (!_hotKeys.ToggleRegistered || !_hotKeys.EditRegistered)
                _window.SetStatus(OperationNoteWindow.T("단축키 등록 실패: ", "Shortcut unavailable: ")
                    + (!_hotKeys.ToggleRegistered ? FormatShortcut(preferences.OverlayToggleHotKey) : FormatShortcut(preferences.OverlayEditHotKey))
                    + OperationNoteWindow.T(" · TSG 작전노트 버튼으로 열기", " · use the TSG Operation note button"), true);
            _main.FormClosing += OnMainClosing;
        }

        internal void Open(bool demoControls)
        {
            if (demoControls && _demo) ShowDemoControls();
            _window.ActivateNote();
        }
        private static string FormatShortcut(AppHotKeyPreference value)
        {
            if (value == null) return "Ctrl+Shift+O";
            return ((value.Modifiers & AppPreferencesStore.HotKeyModifierControl) != 0 ? "Ctrl+" : "")
                + ((value.Modifiers & AppPreferencesStore.HotKeyModifierAlt) != 0 ? "Alt+" : "")
                + ((value.Modifiers & AppPreferencesStore.HotKeyModifierShift) != 0 ? "Shift+" : "")
                + ((value.Modifiers & AppPreferencesStore.HotKeyModifierWindows) != 0 ? "Win+" : "") + ((Keys)value.Key).ToString();
        }
        private void OnOperationNoteRequested(object sender, EventArgs e) { _window.ActivateNote(); }
        private void MarkDirty()
        {
            _dirty = true;
            // Flush on transitions as well as every 300 ms while typing.
            if (_window.ViewState != OperationNoteView.Active) FlushDraft();
        }
        internal bool FlushDraft()
        {
            if (!_dirty) return true;
            if (_storageFailed) return false;
            try { _store.SaveDraft(_window.Draft); _dirty = false; return true; }
            catch (Exception ex) { _window.SetStatus(OperationNoteWindow.T("초안 저장 실패 · 내용은 창에 유지됨: ", "Draft save failed · text remains in this window: ") + ex.Message, true); return false; }
        }
        private async void RefreshSearch()
        {
            try
            {
                if (_search.ShouldRefresh) await _search.RefreshAsync(_stop.Token);
                if (_disposed) return;
                UpdateMapInformation();
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!_disposed) _window.SetStatus(OperationNoteWindow.T("검색 갱신 실패 · 저장된 데이터 사용", "Search update failed · using cached data"), false); }
        }
        private async void RefreshPrivateMemos()
        {
            try
            {
                var records = await Task.Run(() => _legacy.LoadAll());
                var reports = await Task.Run(() => _reports.LoadAll());
                if (_disposed) return;
                _privateMemos = records.Select(record => new OperationSearchMemo
                {
                    Id = record.Key,
                    Title = (record.MapName ?? OperationNoteWindow.T("내 메모", "My note")) + " · " + record.RaidStartedUtc.ToLocalTime().ToString("MM.dd HH:mm"),
                    Text = record.NoteText ?? string.Empty,
                    MapId = record.MapName
                }).ToList();
                _privateMemos.AddRange(reports.Select(record => new OperationSearchMemo
                {
                    Id = "report:" + record.Key,
                    Title = OperationNoteWindow.T("신고 메모 · ", "Report note · ") + (record.MapName ?? ""),
                    Text = (record.MemoText ?? "") + Environment.NewLine + string.Join(Environment.NewLine,
                        (record.Entries ?? new List<UserReportMemoEntry>()).Select(entry => (entry.Nickname ?? "") + " · " + (entry.Reason ?? ""))),
                    MapId = record.MapName
                }));
            }
            catch (Exception) { /* Local archive failure must not block draft editing. */ }
        }
        private async void ScanLatest()
        {
            if (_disposed || _demo || _scanBusy) return;
            TarkovLogPaths paths = _main.GetOperationNotePaths();
            if (paths == null || (string.IsNullOrWhiteSpace(paths.EftPath) && string.IsNullOrWhiteSpace(paths.ArenaPath))) return;
            _scanBusy = true;
            try
            {
                RaidLogScanResult result = await Task.Run(() => RaidLogScanner.Scan(paths, 30));
                if (_disposed) return;
                if (!result.ScanCompletedWithoutErrors)
                {
                    _target = null; _currentMap = null;
                    _window.SetRaidTarget(null, false); UpdateMapInformation();
                    _window.SetStatus(OperationNoteWindow.T("레이드 확인 대기 · 메모는 계속 작성할 수 있습니다", "Waiting for raid evidence · keep writing"), false);
                    return;
                }
                ObserveRaids(result.Sessions);
            }
            catch (Exception)
            {
                if (!_disposed) { _target = null; _currentMap = null; _window.SetRaidTarget(null, false); UpdateMapInformation(); }
            }
            finally { _scanBusy = false; }
        }
        internal void ObserveRaids(IEnumerable<ServerSession> sessions)
        {
            _knownRaids = sessions.Where(x => x != null).OrderByDescending(x => x.OperationStartedAt ?? x.SessionStarted).ToList();
            ServerSession latest = _knownRaids.FirstOrDefault();
            _target = null; _currentMap = null;
            if (latest != null)
            {
                DateTime started = latest.OperationStartedAt ?? latest.SessionStarted;
                bool recent = started <= DateTime.Now.AddMinutes(2) && started >= DateTime.Now.AddHours(-12);
                if (recent && latest.Game == TarkovGame.Eft && latest.OperationStartedAt.HasValue)
                {
                    string key = RaidNoteStore.CreateStableKey(latest);
                    if (latest.OperationState == RaidOperationState.InProgress)
                    {
                        if (_window.Draft.ObservedRaidKey != key) { _window.Draft.ObservedRaidKey = key; MarkDirty(); FlushDraft(); }
                        _currentMap = latest.MapName;
                    }
                    if (latest.OperationState == RaidOperationState.Completed && _window.Draft.ObservedRaidKey == key)
                    {
                        _currentMap = latest.MapName;
                        try { if (!_store.IsRaidSaved(latest)) _target = latest; }
                        catch (Exception ex)
                        {
                            _window.SetStatus(OperationNoteWindow.T("기존 작전노트 저장본 확인 실패 · 파일을 보존했습니다: ", "Cannot verify the saved operation note · existing files were preserved: ") + ex.Message, true);
                        }
                    }
                }
            }
            string label = _target == null ? null : (_target.MapName ?? OperationNoteWindow.T("맵 미확인", "Unknown map"))
                + " · " + _target.OperationEndedAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
            _window.SetRaidTarget(label, _target != null && !_saveBusy);
            UpdateMapInformation();
        }
        private void UpdateMapInformation()
        {
            _window.SetExitText(string.IsNullOrWhiteSpace(_currentMap) ? null : _search.GetExitSummary(_currentMap));
        }
        private async void SaveToRaid()
        {
            if (_target == null || _saveBusy || _storageFailed) return;
            if (!FlushDraft()) return;
            _saveBusy = true;
            ServerSession target = _target;
            _window.SetRaidTarget(null, false);
            try
            {
                OperationNoteDraft frozenDraft = _store.CloneDraft(_window.Draft);
                // Recheck the latest log before committing: a new raid may have started since the last tick.
                if (!_demo)
                {
                    TarkovLogPaths paths = _main.GetOperationNotePaths();
                    RaidLogScanResult fresh = await Task.Run(() => RaidLogScanner.Scan(paths, 30));
                    if (!fresh.ScanCompletedWithoutErrors) throw new IOException(OperationNoteWindow.T("최신 레이드를 확인하지 못했습니다. 잠시 후 다시 저장하세요.", "Cannot verify the latest raid. Try saving again shortly."));
                    _knownRaids = fresh.Sessions.ToList();
                    ServerSession same = _knownRaids.FirstOrDefault(x => RaidNoteStore.CreateStableKey(x) == RaidNoteStore.CreateStableKey(target));
                    if (same == null) throw new IOException(OperationNoteWindow.T("저장 대상 레이드 확인 필요", "Raid target could not be verified"));
                    target = same;
                }
                if (_disposed) return;
                // Snapshot is copied by the store before writing. UI editing continues after the commit.
                List<ServerSession> known = _knownRaids.ToList();
                IList<string> folders = new List<string>();
                bool captureSettingsFailed = false;
                if (!_demo)
                {
                    try { folders = OperationCaptureSettings.LoadFolders(); }
                    catch
                    {
                        // Preserve an unreadable custom configuration, but do not let
                        // an optional photo source prevent a durable note snapshot.
                        folders = OperationScreenshotLinker.DiscoverDefaultFolders();
                        captureSettingsFailed = true;
                    }
                }
                OperationNoteSaveResult result = await Task.Run(() => _store.SaveToRaid(frozenDraft, target, known, folders));
                if (_disposed) return;
                _target = null;
                _window.SetStatus(OperationNoteWindow.T("레이드에 저장됨 · 사진 ", "Saved to raid · photos ") + result.ScreenshotCount + OperationNoteWindow.T("장 · 그대로 이어쓰기", " · continue writing")
                    + (captureSettingsFailed ? OperationNoteWindow.T(" · 촬영 폴더 설정 오류로 기본 폴더만 확인", " · custom capture settings unavailable; checked default folders") : string.Empty), captureSettingsFailed);
                _main.NotifyOperationNoteSaved(); RefreshPrivateMemos();
            }
            catch (Exception ex) { if (!_disposed) _window.SetStatus(OperationNoteWindow.T("레이드 저장 실패: ", "Raid save failed: ") + ex.Message, true); }
            finally { _saveBusy = false; if (!_disposed && _target != null) _window.SetRaidTarget((_target.MapName ?? "") + " · " + _target.OperationEndedAt.Value.ToString("HH:mm"), true); }
        }
        private void OnMainClosing(object sender, FormClosingEventArgs e)
        {
            if (_saveBusy)
            {
                e.Cancel = true;
                _window.SetStatus(OperationNoteWindow.T("레이드 저장 중입니다. 완료된 뒤 종료해 주세요.", "Saving the raid note. Close the app after saving finishes."), false);
                return;
            }
            if (!_dirty || FlushDraft()) return;
            if (MessageBox.Show(_main, OperationNoteWindow.T("초안을 저장하지 못했습니다. 창으로 돌아가 내용을 복사하거나 저장을 다시 시도하세요. 저장하지 않고 종료할까요?", "The draft could not be saved. Return to the note to copy it or retry. Exit without saving?"),
                OperationNoteWindow.T("작전노트 저장 실패", "Draft save failed"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) e.Cancel = true;
        }
        private void ShowCaptureFolders()
        {
            try { OperationCaptureSettings.ShowDialog(_main); }
            catch (Exception ex) { MessageBox.Show(_main, ex.Message, OperationNoteWindow.T("촬영 폴더", "Capture folders"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        private void ShowDemoControls()
        {
            if (_demoControls != null) { _demoControls.Show(); return; }
            _demoControls = new Form { Text = "TSG 0.8.8 · " + OperationNoteWindow.T("예시 레이드 체험", "Sample raid controls"), Width = 390, Height = 170, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedToolWindow };
            var label = new Label { Dock = DockStyle.Top, Height = 45, Text = OperationNoteWindow.T("로컬 예시입니다. 실제 기록·사진과 구분됩니다.\n본문 작성 → 예시 종료 → 노트에서 저장", "Local sample, separate from real records/photos.\nWrite → end sample → save in the note"), Padding = new Padding(8) };
            var next = new Button { Text = OperationNoteWindow.T("예시 레이드 시작", "Start sample raid"), Left = 10, Top = 58, Width = 170, Height = 32 };
            var end = new Button { Text = OperationNoteWindow.T("예시 레이드 종료", "End sample raid"), Left = 190, Top = 58, Width = 170, Height = 32 };
            var archive = new Button { Text = OperationNoteWindow.T("예시 보관함 열기", "Open sample archive"), Left = 10, Top = 95, Width = 170, Height = 27 };
            next.Click += delegate
            {
                _demoRound++;
                var raid = new ServerSession { Game = TarkovGame.Eft, SessionFolderName = "beta088-sample-" + Guid.NewGuid().ToString("N"), SessionStarted = DateTime.Now.AddMinutes(-2), OperationStartedAt = DateTime.Now.AddMinutes(-1), OperationState = RaidOperationState.InProgress, MapName = _demoRound % 2 == 1 ? "Customs" : "Woods" };
                _knownRaids.Insert(0, raid); ObserveRaids(_knownRaids.ToList()); _window.ActivateNote();
            };
            end.Click += delegate
            {
                ServerSession raid = _knownRaids.FirstOrDefault();
                if (raid == null) return;
                raid.OperationEndedAt = DateTime.Now; raid.OperationState = RaidOperationState.Completed;
                ObserveRaids(_knownRaids.ToList()); _window.ActivateNote();
            };
            archive.Click += delegate { using (var form = new RaidNoteArchiveForm(_legacy, false)) form.ShowDialog(_demoControls); };
            _demoControls.Controls.AddRange(new Control[] { label, next, end, archive });
            _demoControls.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!_disposed) { e.Cancel = true; _demoControls.Hide(); } };
            _demoControls.Show(_main);
        }
        public void Dispose()
        {
            if (_disposed) return;
            FlushDraft(); _disposed = true;
            _stop.Cancel(); _scanTimer.Dispose(); _saveTimer.Dispose(); _hotKeys.Dispose();
            _main.OperationNoteRequested -= OnOperationNoteRequested; _main.FormClosing -= OnMainClosing;
            _main.CaptureFoldersRequested -= ShowCaptureFolders;
            _window.Shutdown();
            if (_demoControls != null) _demoControls.Dispose();
            _stop.Dispose();
        }
    }
}
