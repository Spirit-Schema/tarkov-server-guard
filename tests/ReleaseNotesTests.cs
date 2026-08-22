// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class ReleaseNotesTests
    {
        private static int _failed;

        [STAThread]
        private static int Main()
        {
            Run("bundled notes require the exact current version", TestBundledNotes);
            Run("notice eligibility excludes demo preview and portable", TestEligibility);
            Run("completed update notice is claimed exactly once", TestClaimExactlyOnce);
            Run("fresh install and mismatched versions never display", TestFreshAndMismatch);
            Run("receipt failure loses the notice instead of repeating", TestReceiptFailurePolicy);
            Run("corrupt and oversized markers fail closed", TestInvalidMarkers);
            Run("completion dialog has no history or online action", TestCompletionDialog);
            Run("update prompt has no changes button", TestUpdatePrompt);

            if (_failed == 0)
            {
                Console.WriteLine("Release notes tests passed.");
                return 0;
            }
            Console.Error.WriteLine(_failed + " release notes test(s) failed.");
            return 1;
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
