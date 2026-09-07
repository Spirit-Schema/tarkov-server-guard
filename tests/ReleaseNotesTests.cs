// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class ReleaseNotesTests
    {
        private static int _failed;
        private static Assembly _application;
        private static string _currentVersion;
        private static string _artifacts;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr window, IntPtr destination, uint flags);

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Run("actual application version has bilingual bundled notes", delegate
            {
                Assert(args.Length >= 2, "Pass the actual built application and artifact directory; current-version verification may not be skipped.");
                _application = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                Version version = _application.GetName().Version;
                _currentVersion = string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
                _artifacts = Path.Combine(Path.GetFullPath(args[1]), "release-notes");
                Directory.CreateDirectory(_artifacts);
                TestCurrentBundledNotes();
            });
            Run("retained notes require an exact version match", TestBundledNotes);
            Run("notice eligibility excludes demo preview and portable", TestEligibility);
            Run("completed update notice is claimed exactly once", TestClaimExactlyOnce);
            Run("0.8.3 to current update displays current notes once", TestUpgradeFrom083);
            Run("parallel starts claim only one completion notice", TestConcurrentClaim);
            Run("missing bundled notes never consume the pending notice", TestMissingNotes);
            Run("fresh install and mismatched versions never display", TestFreshAndMismatch);
            Run("receipt failure loses the notice instead of repeating", TestReceiptFailurePolicy);
            Run("corrupt and oversized markers fail closed", TestInvalidMarkers);
            Run("completion dialog has no history or online action", TestCompletionDialog);
            Run("actual current completion dialogs render in Korean and English", TestCurrentCompletionDialogs);
            Run("update prompt has no changes button", TestUpdatePrompt);

            if (_failed == 0)
            {
                Console.WriteLine("Release notes tests passed.");
                return 0;
            }
            Console.Error.WriteLine(_failed + " release notes test(s) failed.");
            return 1;
        }

        private static void TestCurrentBundledNotes()
        {
            Type catalog = _application.GetType("TarkovServerReporter.ReleaseNotesCatalog", true);
            object current = catalog.GetMethod("FindBundled", Static).Invoke(null, new object[] { _currentVersion });
            Assert(current != null, "The actual application version " + _currentVersion + " has no bundled completion notes.");
            string korean = GetApplicationNotes(current, AppText.KoreanLanguage);
            string english = GetApplicationNotes(current, AppText.EnglishLanguage);
            Assert(korean.Contains("0.8.3 이후") && korean.Contains("열 너비")
                && korean.Contains("메모 보관함") && korean.Contains("파티") && korean.Contains("영어"),
                "Current notes do not cover the cumulative update from the last public release.");
            Assert(english.Contains("since 0.8.3") && english.Contains("column widths")
                && english.Contains("Saved Notes") && english.Contains("party"),
                "Current English notes do not cover the cumulative update.");
            Assert(english != korean && !english.Any(c => c >= '\uac00' && c <= '\ud7a3'),
                "The actual English completion notice fell back to Korean.");
            Assert(korean.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal))
                == english.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal)),
                "The translated completion notes omit an item.");
            Assert(ReleaseNotesCatalog.FindBundled(_currentVersion) != null,
                "The test and actual application catalogs do not share the current version.");
            if (_currentVersion == "0.8.6")
            {
                Assert(korean.Contains("핫픽스") && korean.Contains("단계별 사용방법")
                    && english.Contains("hotfix") && english.Contains("step-by-step usage guide"),
                    "Hotfix completion notes must retain cumulative notes and include the new party guide.");
                Assert(ReleaseNotesCatalog.FindBundled("0.8.5") != null,
                    "The previous public release notes remain available.");
            }
        }

        private static string GetApplicationNotes(object entry, string language)
        {
            _application.GetType("TarkovServerReporter.AppText", true)
                .GetMethod("SetLanguage", Static).Invoke(null, new object[] { language });
            return (string)_application.GetType("TarkovServerReporter.ReleaseNotesCatalog", true)
                .GetMethod("GetDisplayNotes", Static).Invoke(null, new[] { entry });
        }

        private static void TestBundledNotes()
        {
            ReleaseNotesEntry current = ReleaseNotesCatalog.FindBundled("v0.8.3");
            Assert(current != null && current.VersionText == "0.8.3",
                "The v0.8.3 bundled completion notes are missing.");
            string[] expectedCurrentLines =
            {
                "- EFT 접속 기록의 맵·게임유형 뒤에 로그로 확인된 PMC·스캐브와 솔로·2인~5인 정보를 함께 표시합니다.",
                "- 로그에 명시된 시즌 번호가 있으면 PvP시즌1·PvP시즌2처럼 표시하고, 확인되지 않은 정보는 추측하지 않고 생략합니다.",
                "- 길어진 레이드 정보를 읽기 쉽도록 맵·게임유형 열을 조금 넓혔으며, 새로 저장하는 메모에도 같은 표시를 보존합니다.",
                "- 서버 차단 완료 뒤 최근 레이드 최대 100개에서 해당 IP의 사용 횟수와 높은 지연·패킷 손실·시간초과 징후가 관찰된 횟수를 안내합니다.",
                "- 차단 리스트는 이 PC에만 적용되므로 파티원 모두의 접속을 막으려면 각 파티원이 같은 서버를 차단해야 합니다.",
                "- 사용방법 2번의 나가기 확인 뒤에 (장비는 보존됩니다) 안내를 같은 줄에 추가했습니다."
            };
            Assert(current.NotesText.Split(new[] { "\r\n" }, StringSplitOptions.None)
                    .SequenceEqual(expectedCurrentLines),
                "The v0.8.3 bundled completion notes lost or changed an exact required line.");

            ReleaseNotesEntry prior = ReleaseNotesCatalog.FindBundled("v0.8.2");
            Assert(prior != null && prior.VersionText == "0.8.2"
                && prior.NotesText.Contains("세로 위치를 중단")
                && prior.NotesText.Contains("하나의 파일")
                && prior.NotesText.Contains("로컬 이미지 연결 경로"),
                "The retained v0.8.2 bundled completion notes are incomplete.");

            ReleaseNotesEntry previous = ReleaseNotesCatalog.FindBundled("v0.8.1");
            Assert(previous != null && previous.VersionText == "0.8.1",
                "The retained v0.8.1 bundled completion notes are missing.");
            Assert(previous.NotesText.Contains("로그 없음")
                && previous.NotesText.Contains("Ctrl+A")
                && previous.NotesText.Contains("저장 시간")
                && previous.NotesText.Contains("화면 읽기 프로그램")
                && previous.NotesText.Contains("3단계 정렬")
                && previous.NotesText.Contains("주황색 방향 표시"),
                "The retained v0.8.1 notes lost a required improvement.");
            Assert(ReleaseNotesCatalog.FindBundled("0.7.5") == null,
                "An older or unknown version must not borrow the current notes.");
            string bounded = ReleaseNotesCatalog.NormalizeNotesText(
                new string('x', ReleaseNotesCatalog.MaximumNotesCharacters + 100));
            Assert(bounded.Length < ReleaseNotesCatalog.MaximumNotesCharacters + 200,
                "Bundled note normalization is not bounded.");
        }

        private static void TestEligibility()
        {
            Assert(UpdateCompletionNotice.ShouldConsume(false, true),
                "An installed update cannot consume its notice.");
            Assert(!UpdateCompletionNotice.ShouldConsume(true, true),
                "Demo or screenshot mode may not consume a real update notice.");
            Assert(!UpdateCompletionNotice.ShouldConsume(false, false),
                "A portable or fresh non-installed app may not consume a notice.");
        }

        private static void TestClaimExactlyOnce()
        {
            WithTemporaryRoot(delegate(string root)
            {
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, "0.8.2"),
                    "The after-update marker was not recorded.");
                string claimed;
                Assert(UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.2", out claimed)
                    && claimed == "0.8.2",
                    "The matching completed update was not claimed.");
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.2", out claimed),
                    "The same update notice was displayed more than once.");
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, "0.8.2"),
                    "Replaying the same Velopack hook should remain harmless.");
                Assert(!File.Exists(Path.Combine(root, UpdateCompletionNotice.PendingFileName)),
                    "An acknowledged version was queued again.");

                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, "0.8.3"),
                    "A later version was not queued independently.");
                Assert(UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.3", out claimed)
                    && claimed == "0.8.3",
                    "A later version did not receive its own one-time claim.");
            });
        }

        private static void TestUpgradeFrom083()
        {
            Assert(!string.IsNullOrEmpty(_currentVersion), "The current application version was not verified.");
            WithTemporaryRoot(delegate(string root)
            {
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, "0.8.3"), "Could not prepare the prior update.");
                ReleaseNotesEntry entry;
                Assert(UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, "0.8.3", false, true, out entry),
                    "Could not prepare the prior version's consumed receipt.");
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, _currentVersion), "The current update hook was not recorded.");
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, _currentVersion, true, true, out entry)
                    && File.Exists(Path.Combine(root, UpdateCompletionNotice.PendingFileName)),
                    "A preview consumed the installed update notice.");
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, _currentVersion, false, false, out entry)
                    && File.Exists(Path.Combine(root, UpdateCompletionNotice.PendingFileName)),
                    "A portable launch consumed the installed update notice.");
                Assert(UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, _currentVersion, false, true, out entry)
                    && entry != null && entry.VersionText == _currentVersion && entry.NotesText.Contains("0.8.3 이후"),
                    "Updating directly from 0.8.3 did not return the cumulative current-version notes.");
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, _currentVersion, false, true, out entry),
                    "Restarting displayed the current update notice again.");
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, _currentVersion)
                    && !UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, _currentVersion, false, true, out entry),
                    "Replaying the update hook displayed the current notice again.");
            });
        }

        private static void TestConcurrentClaim()
        {
            Assert(!string.IsNullOrEmpty(_currentVersion), "The current application version was not verified.");
            WithTemporaryRoot(delegate(string root)
            {
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, _currentVersion), "Could not prepare a pending notice.");
                using (var ready = new CountdownEvent(2))
                using (var start = new ManualResetEventSlim(false))
                {
                    Func<bool> claim = delegate
                    {
                        ready.Signal();
                        if (!start.Wait(5000)) throw new TimeoutException("Concurrent claim start was not signaled.");
                        ReleaseNotesEntry entry;
                        return UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, _currentVersion, false, true, out entry);
                    };
                    Task<bool> first = Task.Factory.StartNew(claim);
                    Task<bool> second = Task.Factory.StartNew(claim);
                    bool bothReady = ready.Wait(5000);
                    start.Set();
                    Assert(Task.WaitAll(new Task[] { first, second }, 10000) && bothReady,
                        "Concurrent notice claims timed out.");
                    Assert(first.Result != second.Result, "Parallel launches did not produce exactly one notice.");
                }
            });
        }

        private static void TestMissingNotes()
        {
            WithTemporaryRoot(delegate(string root)
            {
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, "99.0.0"), "Could not prepare a future version marker.");
                ReleaseNotesEntry entry;
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdateEntry(root, "99.0.0", false, true, out entry)
                    && entry == null && File.Exists(Path.Combine(root, UpdateCompletionNotice.PendingFileName))
                    && !File.Exists(Path.Combine(root, UpdateCompletionNotice.ConsumedFileName)),
                    "A missing bundle consumed a notice that could not be displayed.");
            });
        }

        private static void TestFreshAndMismatch()
        {
            WithTemporaryRoot(delegate(string root)
            {
                string claimed;
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.1", out claimed),
                    "A fresh install without after-update evidence displayed notes.");
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, "0.8.1"),
                    "The fixture marker was not recorded.");
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.7.5", out claimed),
                    "A different running version consumed the update notice.");
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.1", out claimed),
                    "A stale mismatched marker survived for a later fresh install.");
            });
        }

        private static void TestReceiptFailurePolicy()
        {
            WithTemporaryRoot(delegate(string root)
            {
                Assert(UpdateCompletionNotice.TryRecordCompletedUpdate(root, "0.8.1"),
                    "The fixture marker was not recorded.");
                Directory.CreateDirectory(Path.Combine(root, UpdateCompletionNotice.ConsumedFileName));
                string claimed;
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.1", out claimed),
                    "A notice was shown without first persisting its consumed receipt.");
                Assert(!File.Exists(Path.Combine(root, UpdateCompletionNotice.PendingFileName)),
                    "A failed receipt write left an endlessly repeating pending marker.");
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.1", out claimed),
                    "The failed receipt policy retried a possibly displayed notice.");
            });
        }

        private static void TestInvalidMarkers()
        {
            WithTemporaryRoot(delegate(string root)
            {
                string pending = Path.Combine(root, UpdateCompletionNotice.PendingFileName);
                File.WriteAllText(pending, "{not-json}");
                string claimed;
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.1", out claimed),
                    "A corrupt local marker enabled the completion dialog.");

                File.WriteAllText(pending, new string('x', 5000));
                Assert(!UpdateCompletionNotice.TryClaimCompletedUpdate(root, "0.8.1", out claimed),
                    "An oversized local marker enabled the completion dialog.");
            });
        }

        private static void TestCompletionDialog()
        {
            using (var form = new PatchNotesForm(
                ReleaseNotesCatalog.FindBundled("0.8.3")))
            {
                Button[] buttons = Descendants(form).OfType<Button>().ToArray();
                Assert(buttons.Length == 1 && buttons[0].Text == "확인",
                    "The one-time completion dialog exposed a history or refresh action.");
                Assert(form.Text.Contains("업데이트 완료"),
                    "The dialog is not presented as a completed update confirmation.");
                Panel border = Descendants(form)
                    .OfType<Panel>()
                    .SingleOrDefault(panel => panel.Name == "notesBorderPanel");
                RichTextBox notes = Descendants(form).OfType<RichTextBox>().SingleOrDefault();
                Assert(border != null && notes != null && notes.Parent == border,
                    "The patch notes text is not hosted by its dark-theme border container.");
                Assert(border.BackColor == Color.FromArgb(54, 63, 74)
                    && border.Padding == new Padding(1),
                    "The patch notes border does not use the established one-pixel dark border color.");
                Assert(notes.BorderStyle == BorderStyle.None,
                    "The native light RichTextBox border is still enabled.");
                Assert(notes.ReadOnly && notes.AccessibleName == "업데이트 변경 사항",
                    "The border change altered read-only or accessibility behavior.");
            }
        }

        private static void TestUpdatePrompt()
        {
            using (var form = new UpdatePromptForm("0.8.3"))
            {
                string[] labels = Descendants(form)
                    .OfType<Button>()
                    .Select(button => button.Text)
                    .ToArray();
                Assert(labels.Contains("업데이트") && labels.Contains("나중에"),
                    "The update decision actions are incomplete.");
                Assert(!labels.Contains("변경 사항") && labels.Length == 2,
                    "The pre-update prompt still exposes arbitrary patch notes.");
            }
        }

        private static void TestCurrentCompletionDialogs()
        {
            Assert(_application != null && !string.IsNullOrEmpty(_currentVersion),
                "The current application was not loaded for dialog verification.");
            Type catalog = _application.GetType("TarkovServerReporter.ReleaseNotesCatalog", true);
            object entry = catalog.GetMethod("FindBundled", Static).Invoke(null, new object[] { _currentVersion });
            Assert(entry != null, "The current application's completion entry is missing.");
            foreach (string language in new[] { AppText.KoreanLanguage, AppText.EnglishLanguage })
            {
                string expected = GetApplicationNotes(entry, language);
                using (var form = (Form)Activator.CreateInstance(
                    _application.GetType("TarkovServerReporter.PatchNotesForm", true), Instance, null, new[] { entry }, null))
                {
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    RichTextBox notes = Descendants(form).OfType<RichTextBox>().Single();
                    Assert(notes.Text.Replace("\r\n", "\n") == expected.Replace("\r\n", "\n"),
                        language + ": The actual dialog does not display the complete current notes.");
                    Assert(notes.ReadOnly && notes.WordWrap && notes.ScrollBars == RichTextBoxScrollBars.ForcedVertical,
                        language + ": Long cumulative notes cannot be read using the established scrolling area.");
                    Assert(Descendants(form).OfType<Label>().Any(label => label.Text.Contains("v" + _currentVersion)),
                        language + ": The completion heading shows the wrong version.");
                    Button close = Descendants(form).OfType<Button>().Single();
                    Assert(close.Visible && form.RectangleToScreen(form.ClientRectangle)
                        .Contains(close.RectangleToScreen(close.ClientRectangle)),
                        language + ": The confirmation button is outside the completion dialog.");
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        {
                            IntPtr dc = graphics.GetHdc();
                            try { Assert(PrintWindow(form.Handle, dc, 0), "Could not capture " + language + " completion dialog."); }
                            finally { graphics.ReleaseHdc(dc); }
                        }
                        bitmap.Save(Path.Combine(_artifacts, "update-complete-" + language + ".png"), ImageFormat.Png);
                    }
                    form.Close();
                }
            }
            GetApplicationNotes(entry, AppText.KoreanLanguage);
        }

        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }

        private static void WithTemporaryRoot(Action<string> action)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "tsg-update-completion-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { action(root); }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception exception)
            {
                _failed++;
                Console.Error.WriteLine("FAIL: " + name + " - " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
