using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class OperationNoteUiTests
    {
        private static int _checks;
        private static string _artifacts;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                _artifacts = args[0]; Directory.CreateDirectory(_artifacts);
                StaUiTestHarness.Run(Run);
                Console.WriteLine("OperationNoteUiTests: PASS (" + _checks + " assertions)"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void Assert(bool value, string message)
        {
            _checks++; if (!value) throw new Exception(message);
        }
        private static void Invoke(OperationNoteWindow window, string method)
        {
            typeof(OperationNoteWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, null);
            Forms.Application.DoEvents();
        }
        private static T Field<T>(OperationNoteWindow window, string name)
        {
            return (T)typeof(OperationNoteWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
        }
        private static void Run()
        {
            TestNativeHotKeyOwnership();
            TestExactForegroundRestoration();
            foreach (string language in new[] { "ko-KR", "en" })
            {
                AppText.SetLanguage(language);
                var draft = new OperationNoteDraft { BodyText = language == "ko-KR" ? "세관 · 다음 레이드\n기숙사 3층 열쇠 챙기기\n북쪽 울타리에서 합류" : "Customs · next raid\nBring the dorm room key\nMeet at the north fence" };
                draft.Checks.Add(new OperationNoteCheck { Text = language == "ko-KR" ? "의약품 보충" : "Restock meds", Done = true });
                draft.Checks.Add(new OperationNoteCheck { Text = language == "ko-KR" ? "카메라 설치" : "Place the camera", Done = false });
                var window = new OperationNoteWindow(draft);
                int changes = 0;
                window.DraftChanged += delegate { changes++; };
                try
                {
                    ElementHost.EnableModelessKeyboardInterop(window);
                    window.ActivateNote(); Forms.Application.DoEvents();
                    Assert(window.ViewState == OperationNoteView.Active, "Opening activates note.");
                    Assert(window.BodyEditor.IsKeyboardFocusWithin, "Opening immediately focuses body, not search.");
                    window.BodyEditor.AppendText("\n검수 입력");
                    Assert(draft.BodyText.EndsWith("검수 입력"), "Real textbox changes update draft.");
                    Assert(changes > 0, "Editing requests durable save.");
                    window.SetRaidTarget("Customs · 19:28", true);
                    window.SavePreview(Path.Combine(_artifacts, "operation-note-active-" + language + ".png"));
                    Assert(window.VisibleCheckCount == 2, "Active state shows completed and pending items.");
                    window.LockNote(false); Forms.Application.DoEvents();
                    Assert(window.ViewState == OperationNoteView.Locked && window.BodyEditor.IsReadOnly, "Locked state is read-only.");
                    Assert((OperationNoteNative.GetStyle(window.NativeHandle) & OperationNoteNative.Transparent) != 0, "Locked HWND passes pointer input through.");
                    Assert((OperationNoteNative.GetStyle(window.NativeHandle) & OperationNoteNative.NoActivate) != 0, "Locked HWND cannot activate.");
                    Assert(window.VisibleCheckCount == 1, "Completed item hidden when locked.");
                    window.SavePreview(Path.Combine(_artifacts, "operation-note-locked-" + language + ".png"));
                    window.ActivateNote(); Forms.Application.DoEvents();
                    Assert(window.BodyEditor.IsKeyboardFocusWithin, "Reactivation restores body focus.");
                    Assert((OperationNoteNative.GetStyle(window.NativeHandle) & OperationNoteNative.Transparent) == 0, "Active HWND accepts input again.");
                    window.AddCheck(); Forms.Application.DoEvents();
                    Assert(draft.Checks.Count == 3 && draft.Checks[2].Text == "", "Check icon creates blank immediately editable item.");
                    Assert(Keyboard.FocusedElement is TextBox && Keyboard.FocusedElement != window.BodyEditor, "New checkbox text receives focus.");
                    Invoke(window, "ShowPins");
                    var pinButton = (Button)Field<StackPanel>(window, "_flyoutItems").Children[0];
                    pinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert(draft.Pins.Count == 1, "Pin current note.");
                    string pinnedText = draft.Pins[0].BodyText;
                    window.BodyEditor.Text = "새로 이어쓴 본문";
                    Assert(draft.Pins[0].BodyText == pinnedText, "Pins own independent copy.");
                    Assert(!object.ReferenceEquals(draft.Pins[0].Checks[0], draft.Checks[0]), "Pinned checklist is independent.");
                    Invoke(window, "CloseFlyout");
                    window.SearchProvider = query => new List<OperationSearchResult> { new OperationSearchResult { Id = "test", Kind = "item", Title = "Salewa 응급 치료 키트", EnglishTitle = "Salewa first aid kit", Summary = "퀘스트: Shortage ×3\n은신처: 의료시설 Lv.1 ×1\n검수용 합성 데이터", SourceUrl = "https://tarkov.dev" } };
                    Field<TextBox>(window, "_search").Text = "salewa";
                    Invoke(window, "ShowSearch");
                    window.SavePreview(Path.Combine(_artifacts, "operation-note-search-" + language + ".png"));
                    Assert(Field<Border>(window, "_flyout").Visibility == Visibility.Visible, "Inline search result opens below header.");
                    Invoke(window, "CloseFlyout");
                    window.SetExitText(string.Join("\n", Enumerable.Range(1, 15).Select(x => "Exit " + x + " · 일반 조건 확인")));
                    Invoke(window, "ToggleExits");
                    Assert(Field<TextBlock>(window, "_exits").Text.Contains("…"), "Long exit list has explicit continuation rather than silent clipping.");
                    window.SavePreview(Path.Combine(_artifacts, "operation-note-exits-" + language + ".png"));
                    window.Close(); Forms.Application.DoEvents();
                    Assert(window.ViewState == OperationNoteView.Hidden, "Alt+F4/window close hides rather than destroys note.");
                    window.ActivateNote(); Forms.Application.DoEvents();
                    Assert(window.IsVisible && window.BodyEditor.IsKeyboardFocusWithin, "Hidden note reopens and remains editable.");
                    window.HideNote(); Assert(!window.IsVisible && draft.BodyText == "새로 이어쓴 본문", "Hide preserves working text.");
                }
                finally { window.Shutdown(); }
            }
            Assert(OperationNoteWindow.ShouldLockForPointer(false, true, .1), "Leaving armed margin locks immediately.");
            Assert(!OperationNoteWindow.ShouldLockForPointer(true, true, 20), "Internal popup and margin stay interactive.");
            Assert(!OperationNoteNative.MayReturnFocus(new IntPtr(2), new IntPtr(1), true), "Alt+Tab to other app must never be stolen.");
            Assert(!OperationNoteNative.MayReturnFocus(new IntPtr(1), new IntPtr(1), false), "No guessed game focus target.");
            Assert(OperationNoteNative.MayReturnFocus(new IntPtr(1), new IntPtr(1), true), "Verified game can regain focus only while note owns it.");
            AppText.SetLanguage("ko-KR");
        }

        private static AppPreferences NativeTestHotKeyPreferences()
        {
            AppPreferences preferences = AppPreferences.CreateDefault();
            // Test-only combinations; production shortcut preferences remain untouched.
            preferences.OverlayToggleHotKey = new AppHotKeyPreference { Modifiers = 7, Key = (int)Forms.Keys.F23 };
            preferences.OverlayEditHotKey = new AppHotKeyPreference { Modifiers = 7, Key = (int)Forms.Keys.F24 };
            return preferences;
        }

        private static void QueueHotKey(OperationNoteHotKeys owner, int id, Forms.Keys key)
        {
            Assert(PostMessage(owner.Handle, 0x0312, new IntPtr(id), new IntPtr(((int)key << 16) | 7)),
                "Registered native message window accepts queued WM_HOTKEY.");
            Forms.Application.DoEvents();
        }

        private static void TestNativeHotKeyOwnership()
        {
            int toggleCount = 0;
            int editCount = 0;
            var first = new OperationNoteHotKeys(NativeTestHotKeyPreferences());
            try
            {
                Assert(first.ToggleRegistered && first.EditRegistered,
                    "Windows registers both temporary Ctrl+Alt+Shift+F23/F24 shortcuts.");
                first.Toggle += delegate { toggleCount++; };
                first.Edit += delegate { editCount++; };
                using (var competing = new OperationNoteHotKeys(NativeTestHotKeyPreferences()))
                {
                    Assert(!competing.ToggleRegistered && !competing.EditRegistered,
                        "A second native owner cannot claim already registered shortcuts.");
                    QueueHotKey(first, 881, Forms.Keys.F23);
                    QueueHotKey(first, 882, Forms.Keys.F24);
                    Assert(toggleCount == 1 && editCount == 1,
                        "Queued WM_HOTKEY reaches the real registered window and dispatches both callbacks.");
                }
                using (var afterFailedOwnerDisposal = new OperationNoteHotKeys(NativeTestHotKeyPreferences()))
                    Assert(!afterFailedOwnerDisposal.ToggleRegistered && !afterFailedOwnerDisposal.EditRegistered,
                        "Disposing a failed claimant never unregisters the original owner's shortcuts.");
                QueueHotKey(first, 881, Forms.Keys.F23);
                Assert(toggleCount == 2 && editCount == 1,
                    "Original native owner still dispatches after a competing owner is disposed.");
            }
            finally { first.Dispose(); }
            using (var replacement = new OperationNoteHotKeys(NativeTestHotKeyPreferences()))
                Assert(replacement.ToggleRegistered && replacement.EditRegistered,
                    "Disposing the registered owner releases both shortcuts for reacquisition.");
            Console.WriteLine("PASS: real Windows shortcut registration, ownership, disposal and queued WM_HOTKEY dispatch (no physical key synthesis).");
        }

        private static Forms.Form NewForegroundFixture(string name, int left)
        {
            return new Forms.Form
            {
                Text = name, StartPosition = Forms.FormStartPosition.Manual,
                Location = new System.Drawing.Point(left, 20), Size = new System.Drawing.Size(260, 120),
                ShowInTaskbar = false
            };
        }

        private static bool TryActivateFixture(Forms.Form form)
        {
            form.Show();
            form.Activate();
            OperationNoteNative.SetForegroundWindow(form.Handle);
            Forms.Application.DoEvents();
            if (OperationNoteNative.GetForegroundWindow() != form.Handle)
            {
                form.Activate();
                OperationNoteNative.SetForegroundWindow(form.Handle);
                Forms.Application.DoEvents();
            }
            return OperationNoteNative.GetForegroundWindow() == form.Handle;
        }

        private static void TestExactForegroundRestoration()
        {
            using (Forms.Form original = NewForegroundFixture("TSG focus test · original", 20))
            using (Forms.Form switched = NewForegroundFixture("TSG focus test · switched app", 300))
            {
                var window = new OperationNoteWindow(new OperationNoteDraft { BodyText = "Local focus fixture" });
                try
                {
                    ElementHost.EnableModelessKeyboardInterop(window);
                    bool acquiredForeground = TryActivateFixture(original);
                    uint originalProcess = OperationNoteNative.GetProcessId(original.Handle);
                    Assert(OperationNoteNative.IsSameReturnTarget(original.Handle, originalProcess),
                        "Visible original HWND and its captured PID form a verified return target.");
                    Assert(!OperationNoteNative.IsSameReturnTarget(original.Handle, originalProcess + 1),
                        "A target with a different process identity is rejected.");
                    if (!acquiredForeground)
                    {
                        original.Hide(); Forms.Application.DoEvents();
                        Assert(!OperationNoteNative.IsSameReturnTarget(original.Handle, originalProcess),
                            "A hidden previous window is rejected even when foreground tests are unavailable.");
                        Console.WriteLine("SKIP: exact foreground restoration/app-switch test. Windows foreground policy denied the initial fixture activation before product code ran. Run this suite from an interactive foreground process to verify it; no foreground-stealing workaround was used.");
                        return;
                    }
                    Assert(OperationNoteNative.GetForegroundWindow() == original.Handle,
                        "Initial fixture owns the expected foreground HWND before product activation.");
                    window.ActivateNote(); Forms.Application.DoEvents();
                    Assert(OperationNoteNative.GetForegroundWindow() == window.NativeHandle,
                        "The real note HWND becomes foreground when activated.");
                    Assert(Field<IntPtr>(window, "_returnWindow") == original.Handle,
                        "Activation captures the exact previous non-game window.");
                    window.ActivateNote(); Forms.Application.DoEvents();
                    Assert(Field<IntPtr>(window, "_returnWindow") == original.Handle,
                        "Repeated activation while editing preserves the original target.");
                    window.LockNote(true); Forms.Application.DoEvents();
                    Assert(window.ViewState == OperationNoteView.Locked
                        && OperationNoteNative.GetForegroundWindow() == original.Handle,
                        "Locking returns foreground to the exact previous window and locks the note.");
                    window.ActivateNote(); Forms.Application.DoEvents();
                    Assert(TryActivateFixture(switched), "The explicit second-window activation succeeds during the app-switch test.");
                    Assert(window.ViewState == OperationNoteView.Locked,
                        "Switching to another foreground window locks the note without an explicit focus return.");
                    Invoke(window, "ReturnToGameIfAppropriate");
                    Assert(OperationNoteNative.GetForegroundWindow() == switched.Handle,
                        "A later return attempt cannot steal foreground from the deliberately selected second window.");
                    window.ActivateNote(); Forms.Application.DoEvents();
                    window.HideNote(); Forms.Application.DoEvents();
                    Assert(!window.IsVisible && OperationNoteNative.GetForegroundWindow() == switched.Handle,
                        "Hiding restores the latest explicitly captured foreground target.");
                    original.Hide(); Forms.Application.DoEvents();
                    Assert(!OperationNoteNative.IsSameReturnTarget(original.Handle, originalProcess),
                        "A hidden previous window is no longer a valid return target.");
                }
                finally { window.Shutdown(); }
            }
            Console.WriteLine("PASS: exact native HWND/PID restoration, repeated activation and simulated app-switch preservation using local fixture windows.");
        }
    }
}
