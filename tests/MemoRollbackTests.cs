// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class MemoRollbackTests
    {
        private static Assembly _app;
        private static string _root;
        private static string _artifacts;
        private static int _assertions;
        private static int _applyCalls;
        private static string _applyError;
        private static object _appliedPreferences;
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 2 || !File.Exists(args[0]))
                    throw new ArgumentException("Usage: MemoRollbackTests <product.exe> <artifact-directory>");
                _app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                _artifacts = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(_artifacts);
                _root = Path.Combine(Path.GetTempPath(), "TSG-MemoRollbackTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                StaUiTestHarness.Run(RunTests);
                Console.WriteLine("MemoRollbackTests: PASS (" + _assertions + " assertions)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("MemoRollbackTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                if (_app != null) SetLanguage("ko-KR");
                if (_root != null)
                {
                    string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                        + Path.DirectorySeparatorChar + "TSG-MemoRollbackTests-";
                    if (!Path.GetFullPath(_root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unsafe fixture cleanup root.");
                    if (Directory.Exists(_root)) Directory.Delete(_root, true);
                }
            }
        }

        private static void RunTests()
        {
            AssertRemovedOperationSubsystem();
            foreach (string language in new[] { "ko-KR", "en" })
            {
                SetLanguage(language);
                TestLegacyMemoFlow(language);
                TestRetainedSettings(language);
                TestMainEntryPoints(language);
            }
            File.WriteAllText(Path.Combine(_artifacts, "memo-rollback-summary.txt"),
                "Product SHA-256: " + ProductHash() + Environment.NewLine
                + "Languages: ko-KR, en" + Environment.NewLine
                + "Verified: legacy create/edit/archive/report, image-only picker, existing video preservation, settings failures, no operation subsystem." + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static void AssertRemovedOperationSubsystem()
        {
            foreach (string name in new[] { "OperationNoteOverlayForm", "OperationPlanEditorForm", "RaidReportForm", "MemoWorkspaceForm", "MemoUiCoordinator", "MemoDocumentStore", "MemoLifecycleCoordinator", "MemoLegacyMigrationService", "GlobalHotKeyService" })
                Assert(_app.GetType("TarkovServerReporter." + name, false) == null,
                    "The rolled-back product must not ship active " + name + ".");
            Assert(_app.GetType("TarkovServerReporter.RaidNoteForm", false) != null
                && _app.GetType("TarkovServerReporter.UserReportMemoForm", false) != null
                && _app.GetType("TarkovServerReporter.RaidNoteArchiveForm", false) != null,
                "All three legacy memo entry points are present in the tested product assembly.");
        }

        private static void TestLegacyMemoFlow(string language)
        {
            string root = Path.Combine(_root, language);
            string raids = Path.Combine(root, "RaidNotes");
            string reports = Path.Combine(root, "UserReportMemos");
            Directory.CreateDirectory(root);
            // These are the real v0.8.4.1 folder names, identifier format,
            // numeric enum values, and serialized field names. They sit beside
            // the injected legacy stores, just as in the user's profile.
            string dormantRoot = Path.Combine(root, "MemosV2");
            string documents = Path.Combine(dormantRoot, "Documents");
            Directory.CreateDirectory(documents);
            const string documentId = "0123456789abcdef0123456789abcdef";
            string dormantDocument = Path.Combine(documents, documentId + ".json");
            string dormantWorkspace = Path.Combine(dormantRoot, "workspace.json");
            File.WriteAllText(dormantDocument,
                "{\"SchemaVersion\":1,\"Id\":\"" + documentId
                + "\",\"Kind\":1,\"State\":0,\"Title\":\"Existing operation draft\",\"BodyMarkdown\":\"Keep the user's test-build draft\",\"Tags\":[],\"Attachments\":[],\"CreatedUtc\":\"\\/Date(1788714000000)\\/\",\"UpdatedUtc\":\"\\/Date(1788714000000)\\/\"}",
                new UTF8Encoding(false));
            File.WriteAllText(dormantWorkspace,
                "{\"SchemaVersion\":1,\"ActivePlanId\":\"" + documentId
                + "\",\"Revision\":1,\"UpdatedUtc\":\"\\/Date(1788714000000)\\/\"}",
                new UTF8Encoding(false));
            byte[] dormantDocumentBytes = File.ReadAllBytes(dormantDocument);
            byte[] dormantWorkspaceBytes = File.ReadAllBytes(dormantWorkspace);
            object raidStore = New("RaidNoteStore", raids);
            object reportStore = New("UserReportMemoStore", reports);
            object session = New("ServerSession");
            Set(session, "Game", Enum.Parse(TypeOf("TarkovGame"), "Eft"));
            Set(session, "SessionFolderName", "log_2026.09.06_fixture_" + language);
            Set(session, "SessionKey", "synthetic-session-" + language);
            Set(session, "SessionStarted", new DateTime(2026, 9, 6, 18, 0, 0, DateTimeKind.Local));
            Set(session, "MapName", "Customs");
            Set(session, "UserReportCount", 2);
            string key = (string)CallStatic("RaidNoteStore", "CreateStableKey", session);
            string primary = Path.Combine(raids, key + ".json");
            string imagePath = Path.Combine(root, "fixture.png");
            using (var image = new Bitmap(48, 48))
            {
                using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.DarkSeaGreen);
                image.Save(imagePath, ImageFormat.Png);
            }
            string videoPath = Path.Combine(root, "existing-recording.mp4");
            File.WriteAllText(videoPath, "synthetic video path fixture - never opened", new UTF8Encoding(false));
            byte[] videoBytes = File.ReadAllBytes(videoPath);

            using (var form = (Form)New("RaidNoteForm", session, raidStore))
            {
                Show(form);
                Assert(!File.Exists(primary), "Opening an empty memo does not create a saved record.");
                Assert(form.Text == (language == "en" ? "Raid Note" : "레이드 메모"), "Legacy raid editor title follows the selected language.");
                TextBox body = Field<TextBox>(form, "_noteTextBox");
                Assert(!body.ReadOnly, "Existing raid memo entry point is editable.");
                body.Text = "사용자 작성 원문 - never translate\r\nfirst raid memo";
                Field<TextBox>(form, "_tagTextBox").Text = "route, loot";
                Assert(!File.Exists(primary), "Typing alone does not autosave or submit an operation plan.");
                var list = Field<ListBox>(form, "_screenshotList");
                Assert(!list.AllowDrop, "The restored attachment list does not introduce drag-and-drop workflow.");
                Call(form, "AddScreenshotPaths", (object)new string[] { imagePath, videoPath });
                Assert(list.Items.Count == 1 && (string)list.Items[0] == imagePath,
                    "The legacy screenshot picker accepts an image and rejects a new video path.");
                Assert(NoOperationActions(form), "The raid editor requires no submit-plan/report/continue action.");
                Capture(form, language + "-raid-note.png");
                Named<Button>(form, "RaidNoteSaveButton").PerformClick();
                Application.DoEvents();
                Assert(File.Exists(primary) && (bool)Get(form, "Changed"), "Explicit Save persists the memo and signals the caller.");
            }
            object record = Call(raidStore, "Load", session);
            Assert((string)Get(record, "NoteText") == "사용자 작성 원문 - never translate\r\nfirst raid memo",
                "User-authored memo text survives localized editing unchanged.");

            // Simulate an already saved v0.8.4.1 media link. Rollback removes
            // new video input but must preserve existing data on an ordinary edit.
            ((IList)Get(record, "ScreenshotPaths")).Add(videoPath);
            Call(raidStore, "Save", key, record);
            using (var editor = (Form)New("RaidNoteForm", record, raidStore))
            {
                Show(editor);
                Assert(Field<ListBox>(editor, "_screenshotList").Items.Count == 2,
                    "Both image and previously stored video links remain visible.");
                Field<TextBox>(editor, "_noteTextBox").AppendText("\r\nedited in legacy UI");
                Named<Button>(editor, "RaidNoteSaveButton").PerformClick();
                Application.DoEvents();
            }
            object edited = Call(raidStore, "Load", key);
            Assert(((IList)Get(edited, "ScreenshotPaths")).Contains(videoPath)
                && ((string)Get(edited, "NoteText")).EndsWith("edited in legacy UI", StringComparison.Ordinal),
                "Editing an existing record preserves video links and commits body changes.");
            Assert(File.ReadAllBytes(videoPath).SequenceEqual(videoBytes), "Memo operations never move, modify, or open attachment originals.");
            byte[] raidBeforeReport = File.ReadAllBytes(primary);

            using (var report = (Form)New("UserReportMemoForm", session, reportStore))
            {
                Show(report);
                Assert(report.Text == (language == "en" ? "Report Note" : "유저신고 메모"), "Report memo title follows the selected language.");
                Named<TextBox>(report, "ReportNickname1").Text = "synthetic-player";
                Named<TextBox>(report, "ReportReason1").Text = "사용자 사유 - preserve text";
                Assert(Descendants(report).OfType<TextBox>().Count(c => c.Name.StartsWith("ReportNickname", StringComparison.Ordinal)) == 2,
                    "Two report events create exactly two editable report rows.");
                Assert(NoOperationActions(report), "A report memo does not require an operation-plan lifecycle.");
                Capture(report, language + "-player-report.png");
                Named<Button>(report, "UserReportMemoSaveButton").PerformClick();
                Application.DoEvents();
                Assert((bool)Get(report, "Changed"), "Saving a player report signals a changed memo.");
            }
            object reportRecord = Call(reportStore, "Load", session);
            IList entries = (IList)Get(reportRecord, "Entries");
            Assert(entries.Count == 2 && (string)Get(entries[0], "Nickname") == "synthetic-player"
                && (string)Get(entries[0], "Reason") == "사용자 사유 - preserve text",
                "Report fields round-trip without modifying their content or inflating the count.");
            Assert(File.ReadAllBytes(primary).SequenceEqual(raidBeforeReport), "Saving a report memo never rewrites the ordinary raid memo.");
            string reportKey = (string)CallStatic("UserReportMemoStore", "CreateStableKey", session);
            string reportPrimary = Path.Combine(reports, reportKey + ".json");
            byte[] reportBeforeArchive = File.ReadAllBytes(reportPrimary);

            using (var archive = (Form)New("RaidNoteArchiveForm", raidStore, reportStore))
            {
                Show(archive);
                Assert(archive.Text == (language == "en" ? "Saved Notes" : "메모 보관함"), "The legacy archive title is localized.");
                DataGridView grid = Field<DataGridView>(archive, "_grid");
                Assert(grid.Rows.Count == 2, "One shared archive lists the raid memo and report memo without duplication.");
                Assert(!Descendants(archive).OfType<TabControl>().Any(), "The restored archive does not impose the postponed separate-tab workflow.");
                Assert(Named<Button>(archive, "MemoBackupImportButton").Enabled
                    && Named<Button>(archive, "MemoBackupExportButton").Enabled,
                    "The legacy archive keeps backup import/export available.");
                if (language == "en")
                {
                    string reportPreview = grid.Rows.Cast<DataGridViewRow>()
                        .Select(row => row.Cells["preview"].ToolTipText)
                        .Single(text => text.Contains("synthetic-player"));
                    Assert(reportPreview.Contains("Player: synthetic-player")
                        && reportPreview.Contains("Reason: 사용자 사유 - preserve text")
                        && !reportPreview.Contains("유저네임:") && !reportPreview.Contains("신고사유:"),
                        "Archive report previews localize system labels while preserving user-authored text.");
                    Button deleteNote = Named<Button>(archive, "NoteArchiveDeleteButton");
                    Button deleteSelected = Named<Button>(archive, "NoteArchiveDeleteSelectedButton");
                    Assert(deleteNote.Text == "Delete Current Note" && deleteSelected.Text == "Delete Selected",
                        "Current-note deletion and checked-note deletion have distinct English labels.");
                    foreach (Button button in new[] { deleteNote, deleteSelected })
                        Assert(TextRenderer.MeasureText(button.Text, button.Font).Width
                            <= button.ClientSize.Width - button.Padding.Horizontal,
                            "The English delete button caption fits its available width: " + button.Text);
                    Assert(TextRenderer.MeasureText("Report Note", grid.DefaultCellStyle.Font).Width
                        <= grid.Columns["kind"].Width - 8,
                        "The English report-note type fits without unnecessary truncation.");
                }
                Capture(archive, language + "-archive.png");
                Assert(!(bool)Get(archive, "Changed"), "Viewing the archive does not mark records changed.");
                archive.Close();
            }
            Assert(File.ReadAllBytes(primary).SequenceEqual(raidBeforeReport), "Archive viewing does not migrate or rewrite existing memo files.");
            Assert(File.ReadAllBytes(reportPrimary).SequenceEqual(reportBeforeArchive),
                "Localized archive previews never rewrite stored report labels or user text.");
            Assert(File.ReadAllBytes(dormantDocument).SequenceEqual(dormantDocumentBytes)
                && File.ReadAllBytes(dormantWorkspace).SequenceEqual(dormantWorkspaceBytes)
                && Directory.GetFiles(dormantRoot, "*", SearchOption.AllDirectories).Length == 2,
                "The complete legacy edit/archive flow leaves adjacent MemosV2 documents and workspace bytes unchanged.");
        }

        private static void TestRetainedSettings(string language)
        {
            object preferences = CallStatic("AppPreferences", "CreateDefault");
            Set(preferences, "Language", language);
            object shortcut = Get(preferences, "OverlayToggleHotKey");
            Set(shortcut, "Key", (int)Keys.F7);
            Type callbackType = typeof(Func<,>).MakeGenericType(TypeOf("AppPreferences"), typeof(string));
            Delegate callback = Delegate.CreateDelegate(callbackType,
                typeof(MemoRollbackTests).GetMethod("ApplyPreferences", BindingFlags.Static | BindingFlags.NonPublic));
            ConstructorInfo constructor = TypeOf("ApplicationSettingsForm").GetConstructor(AllInstance, null,
                new[] { TypeOf("AppPreferences"), callbackType }, null);
            _applyCalls = 0;
            _applyError = "Synthetic settings save failure";
            using (var settings = (Form)constructor.Invoke(new object[] { preferences, callback }))
            {
                Show(settings);
                Assert(_applyCalls == 0, "Opening settings never saves preferences or requests restart.");
                Assert(Descendants(settings).OfType<ComboBox>().Count() == 1
                    && !Descendants(settings).OfType<TextBox>().Any(),
                    "Settings keeps language selection and removes postponed operation shortcut inputs.");
                Capture(settings, language + "-settings.png");
                ComboBox choice = Descendants(settings).OfType<ComboBox>().Single();
                choice.SelectedIndex = language == "en" ? 0 : 1;
                Call(settings, "SaveAndClose", true);
                Assert(_applyCalls == 1 && settings.Visible && settings.DialogResult != DialogResult.OK
                    && !(bool)Get(settings, "RestartRequested"),
                    "A settings save failure keeps the dialog open without approving a restart.");
                Assert(Field<Label>(settings, "_statusLabel").Text == _applyError,
                    "A settings save error remains visible to the user.");
                _applyError = null;
                Call(settings, "SaveAndClose", true);
                Assert(_applyCalls == 2 && settings.DialogResult == DialogResult.OK
                    && (bool)Get(settings, "RestartRequested"), "A successful settings save returns a restart request.");
                Assert((string)Get(_appliedPreferences, "Language") == (language == "en" ? "ko-KR" : "en")
                    && (int)Get(Get(_appliedPreferences, "OverlayToggleHotKey"), "Key") == (int)Keys.F7,
                    "Changing language preserves dormant shortcut preferences from earlier builds.");
                Assert((string)Get(preferences, "Language") == language,
                    "The settings editor never mutates its caller-owned original preferences.");
            }
        }

        private static string ApplyPreferences(object preferences)
        {
            _applyCalls++;
            _appliedPreferences = preferences;
            return _applyError;
        }

        private static void TestMainEntryPoints(string language)
        {
            using (var main = (Form)New("MainForm", true))
            {
                Show(main);
                Button settings = Field<Button>(main, "_settingsButton");
                Assert(settings.Text == (language == "en" ? "Settings" : "설정"), "Main settings entry point is retained in both languages.");
                Assert(Descendants(main).OfType<Button>().Any(button => button.Text == (language == "en" ? "Saved Notes" : "메모 보관함")),
                    "Main exposes the restored memo archive entry point.");
                Assert(!Descendants(main).Any(control => control.Text == "작전노트" || control.Text == "Operation Note"),
                    "Main contains no postponed operation-note entry point.");
                main.Close();
            }
        }

        private static bool NoOperationActions(Control form)
        {
            string[] actions = { "Submit Plan", "Submit Report", "Continue to Next Raid", "작전 제출", "보고서 제출", "다음 레이드로 이어가기" };
            return !Descendants(form).OfType<Button>().Any(button => actions.Contains(button.Text));
        }

        private static void Capture(Form form, string fileName)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(_artifacts, fileName), ImageFormat.Png);
            }
        }

        private static void Show(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20000, -20000);
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
        }

        private static Type TypeOf(string name) { return _app.GetType("TarkovServerReporter." + name, true); }
        private static object New(string name, params object[] args) { return Activator.CreateInstance(TypeOf(name), AllInstance, null, args, null); }
        private static object Get(object target, string name) { return target.GetType().GetProperty(name, AllInstance).GetValue(target, null); }
        private static void Set(object target, string name, object value) { target.GetType().GetProperty(name, AllInstance).SetValue(target, value, null); }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, AllInstance).GetValue(target); }
        private static object Call(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name, AllInstance | BindingFlags.InvokeMethod, null, target, args);
        }
        private static object CallStatic(string type, string name, params object[] args)
        {
            return TypeOf(type).InvokeMember(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.InvokeMethod, null, null, args);
        }
        private static void SetLanguage(string language) { CallStatic("AppText", "SetLanguage", language); }
        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }
        private static T Named<T>(Control root, string name) where T : Control
        {
            return Descendants(root).OfType<T>().Single(control => control.Name == name);
        }
        private static string ProductHash()
        {
            using (SHA256 algorithm = SHA256.Create())
            using (Stream stream = File.OpenRead(_app.Location))
                return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
        }
        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
