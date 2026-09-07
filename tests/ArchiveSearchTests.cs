// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class ArchiveSearchTests
    {
        private static Assembly _app;
        private static string _root;
        private static string _artifacts;
        private static int _assertions;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 2) throw new ArgumentException("ArchiveSearchTests <product> <artifacts>");
                _app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                _artifacts = Path.GetFullPath(args[1]);
                Directory.CreateDirectory(_artifacts);
                _root = Path.Combine(Path.GetTempPath(), "TSG-ArchiveSearch-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                StaUiTestHarness.Run(delegate
                {
                    foreach (string language in new[] { "ko-KR", "en" })
                    {
                        TypeOf("AppText").GetMethod("SetLanguage").Invoke(null, new object[] { language });
                        TestSearch(language);
                        TestEmpty(language);
                    }
                });
                Console.WriteLine("ArchiveSearchTests: PASS (" + _assertions + " assertions)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("ArchiveSearchTests: FAIL " + exception);
                return 1;
            }
            finally
            {
                if (_root != null && Directory.Exists(_root))
                {
                    string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "TSG-ArchiveSearch-";
                    if (!Path.GetFullPath(_root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unsafe test cleanup path.");
                    Directory.Delete(_root, true);
                }
            }
        }

        private static void TestSearch(string language)
        {
            string raidPath = Path.Combine(_root, language, "RaidNotes");
            string reportPath = Path.Combine(_root, language, "UserReportMemos");
            object raids = New("RaidNoteStore", raidPath);
            object reports = New("UserReportMemoStore", reportPath);
            SaveRaid(raids, 'a', "Alpha route 구간", "Customs", "EFT", "loot");
            SaveRaid(raids, 'b', "Bravo boss", "Woods", "EFT", "night");
            SaveRaid(raids, 'c', "alpha extraction", "Customs", "Arena", "loot");
            SaveReport(reports, 'd', "player-one", "Speed issue 신고사유: preserve", "Factory", "EFT");
            SaveReport(reports, 'e', "FOXTROT", "loot route", "Customs", "Arena");
            string reportFile = Path.Combine(reportPath, new string('d', 64) + ".json");
            byte[] reportBytes = File.ReadAllBytes(reportFile);
            using (var archive = (Form)New("RaidNoteArchiveForm", raids, reports))
            {
                Show(archive);
                DataGridView grid = Field<DataGridView>(archive, "_grid");
                TextBox search = Field<TextBox>(archive, "_searchTextBox");
                ComboBox type = Field<ComboBox>(archive, "_typeFilter");
                CheckSearchLayoutAndFeedback(archive, search, type, language);
                Assert(grid.Rows.Count == 5 && search.MaxLength == 256, "Initial archive shows all notes and bounds search input.");
                Assert(search.AccessibleName.Length > 0 && type.AccessibleName.Length > 0, "Search and type controls have accessible labels.");
                string exportHelp = Field<Button>(archive, "_exportButton").AccessibleDescription;
                Assert(exportHelp.Contains(language == "en" ? "all saved notes" : "전체 메모"), "Export explicitly includes all notes independently of filtering.");

                // Rename both owned stores after the initial load. Any file-backed
                // filtering would lose results, fail, or recreate the old path.
                Directory.Move(raidPath, raidPath + ".offline");
                Directory.Move(reportPath, reportPath + ".offline");
                Search(search, grid, " alpha\tCUSTOMS ", 2);
                Search(search, grid, "EFT loot", 1);
                Search(search, grid, "player-ONE speed", 1);
                Assert(Convert.ToString(grid.Rows[0].Cells["preview"].Value).Contains("player-one"), "Search matches a structured report nickname and reason without changing their text.");
                Search(search, grid, "구간", 1);
                Search(search, grid, "foxtrot", 1);
                type.SelectedIndex = 1;
                Assert(grid.Rows.Count == 0 && Field<Label>(archive, "_emptyStateLabel").Visible, "Combined text/type filters show a clear no-results state.");
                Assert(!Field<Button>(archive, "_deleteButton").Enabled && !Field<Button>(archive, "_deleteSelectedButton").Enabled,
                    "No-results state disables both delete actions.");
                Capture(archive, language + "-archive-no-results.png");
                Field<Button>(archive, "_clearFilterButton").PerformClick();
                Assert(grid.Rows.Count == 5 && type.SelectedIndex == 0 && search.TextLength == 0, "Clear resets both filters using the loaded snapshot.");

                grid.Rows[0].Cells["selected"].Value = true;
                Assert(((IList)Call(archive, "GetCheckedDeleteTargets")).Count == 1, "Visible checked note is a deletion target before filtering.");
                search.Text = "EFT";
                Assert(!Field<Button>(archive, "_deleteSelectedButton").Enabled
                    && ((IList)Call(archive, "GetCheckedDeleteTargets")).Count == 0,
                    "Typing immediately clears checked targets before the debounce renders.");
                grid.Rows[0].Cells["selected"].Value = true;
                int openForms = Application.OpenForms.Count;
                Call(archive, "OpenSelected");
                Call(archive, "DeleteSelected");
                Call(archive, "DeleteCheckedRecords");
                typeof(DataGridView).GetMethod("OnCellDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(grid, new object[] { new DataGridViewCellEventArgs(grid.Columns["preview"].Index, 0) });
                Call(archive, "GridKeyDown", grid, new KeyEventArgs(Keys.Enter));
                Call(archive, "GridKeyDown", grid, new KeyEventArgs(Keys.Delete));
                Assert(Application.OpenForms.Count == openForms && !(bool)Get(archive, "Changed")
                    && (bool)FieldValue(archive, "_filterPending"),
                    "Pending filters reject direct open/delete, double-click, and keyboard entry paths even when a stale row is checked.");
                WaitFor(() => grid.Rows.Count == 3);
                Assert(((IList)Call(archive, "GetCheckedDeleteTargets")).Count == 0,
                    "Debounce completion clears a checkbox selected on a stale row during the delay.");
                type.SelectedIndex = 1;
                Assert(grid.Rows.Count == 2 && ((IList)Call(archive, "GetCheckedDeleteTargets")).Count == 0,
                    "Changing type also clears checked targets so hidden notes cannot be deleted.");
                grid.Rows[0].Cells["selected"].Value = true;
                IList targets = (IList)Call(archive, "GetCheckedDeleteTargets");
                Assert(targets.Count == 1 && !(bool)Get(targets[0], "IsUserReport"), "Deletion target collection contains only a visible filtered raid note.");

                type.SelectedIndex = 0;
                SetField(archive, "_archiveSortColumn", "map");
                SetField(archive, "_archiveSortOrder", SortOrder.Ascending);
                Call(archive, "ApplyArchiveSort");
                Search(search, grid, "EFT ", 3);
                string[] maps = grid.Rows.Cast<DataGridViewRow>().Select(row => Convert.ToString(row.Cells["map"].Value)).ToArray();
                Assert(maps.SequenceEqual(maps.OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)), "Filtering preserves the selected archive sort.");
                Assert((string)FieldValue(archive, "_archiveSortColumn") == "map", "The chosen sort column survives filtering.");
                object[] escape = { Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), Keys.Escape };
                Assert((bool)archive.GetType().GetMethod("ProcessCmdKey", Instance).Invoke(archive, escape)
                    && search.TextLength == 0 && grid.Rows.Count == 5, "Escape clears a nonempty search and restores the snapshot.");
                object[] find = { Message.Create(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.F };
                Assert((bool)archive.GetType().GetMethod("ProcessCmdKey", Instance).Invoke(archive, find), "Ctrl+F is handled by the archive search entry point.");
                Assert(!Directory.Exists(raidPath) && !Directory.Exists(reportPath), "Filtering never reads/recreates unavailable store folders.");
                Assert(File.ReadAllBytes(Path.Combine(reportPath + ".offline", Path.GetFileName(reportFile))).SequenceEqual(reportBytes),
                    "Search, sort, type changes, and preview leave stored user text byte for byte unchanged.");
                foreach (int width in new[] { 900, 1220 })
                {
                    archive.ClientSize = new Size(width, 600);
                    Application.DoEvents();
                    Assert(search.Width > 140 && type.Width >= 140, "Search controls remain usable at width " + width);
                    Capture(archive, language + "-archive-" + width + ".png");
                }
                archive.Close();
            }
        }

        private static void TestEmpty(string language)
        {
            object raids = New("RaidNoteStore", Path.Combine(_root, language, "empty-raids"));
            object reports = New("UserReportMemoStore", Path.Combine(_root, language, "empty-reports"));
            using (var archive = (Form)New("RaidNoteArchiveForm", raids, reports))
            {
                Show(archive);
                Label empty = Field<Label>(archive, "_emptyStateLabel");
                Assert(empty.Visible && empty.Text.Contains(language == "en" ? "No saved notes yet" : "저장된 메모가 없습니다"),
                    "A new archive explains how to create the first note rather than showing a blank grid.");
                Assert(!Field<Button>(archive, "_openButton").Enabled, "An empty archive has no active open action.");
                Capture(archive, language + "-archive-empty.png");
                archive.Close();
            }
        }

        private static void CheckSearchLayoutAndFeedback(Form archive, TextBox search, ComboBox type, string language)
        {
            int originalWidth = archive.Width;
            var label = (Label)archive.Controls.Find("NoteArchiveTypeLabel", true).Single();
            foreach (int width in new[] { 900, 1220 })
            {
                archive.Width = width; Application.DoEvents();
                Assert(label.Width >= label.GetPreferredSize(Size.Empty).Width,
                    "The type label fits its actual text at " + width + ": " + language);
                Assert(label.Right <= type.Left && search.Right <= label.Left,
                    "Search, type label and selector have separate space: " + language);
                Assert(type.Width >= 150 && search.Width >= 150,
                    "Both search and type selection remain usable in a narrow archive.");
                Capture(archive, language + "-archive-search-row-" + width + ".png");
            }
            search.Select(); Application.DoEvents();
            Color resting = type.BackColor;
            Call(type, "OnMouseEnter", EventArgs.Empty);
            Assert(type.BackColor != resting, "Hover visibly changes the closed selector.");
            Capture(archive, language + "-archive-type-hover.png");
            Call(type, "OnMouseLeave", EventArgs.Empty);
            Assert(type.BackColor == resting, "Leaving the selector restores its resting color.");
            Color[] colors = new Color[2];
            for (int selected = 0; selected < 2; selected++)
            using (var bitmap = new Bitmap(type.Width, type.ItemHeight))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                Call(type, "OnDrawItem", new DrawItemEventArgs(graphics, type.Font,
                    new Rectangle(Point.Empty, bitmap.Size), 1, selected == 0 ? DrawItemState.None : DrawItemState.Selected));
                colors[selected] = bitmap.GetPixel(bitmap.Width - 2, bitmap.Height / 2);
                bitmap.Save(Path.Combine(_artifacts, language + "-type-option-" + selected + ".png"));
            }
            Assert(colors[0] != colors[1], "The highlighted dropdown option differs visibly from other options.");
            archive.Width = originalWidth; Application.DoEvents();
        }

        private static void SaveRaid(object store, char keyChar, string body, string map, string game, string tag)
        {
            object record = New("RaidNoteRecord");
            string key = new string(keyChar, 64);
            Set(record, "Key", key); Set(record, "NoteText", body); Set(record, "MapName", map); Set(record, "Game", game);
            Set(record, "GameType", "PvP/S1"); Set(record, "CreatedUtc", DateTime.UtcNow.AddDays(-1)); Set(record, "UpdatedUtc", DateTime.UtcNow);
            ((IList)Get(record, "Tags")).Add(tag);
            Call(store, "Save", key, record);
        }

        private static void SaveReport(object store, char keyChar, string player, string reason, string map, string game)
        {
            object record = New("UserReportMemoRecord");
            string key = new string(keyChar, 64);
            Set(record, "Key", key); Set(record, "ReportCount", 1); Set(record, "MapName", map); Set(record, "Game", game);
            Set(record, "CreatedUtc", DateTime.UtcNow.AddDays(-1)); Set(record, "UpdatedUtc", DateTime.UtcNow);
            object entry = New("UserReportMemoEntry"); Set(entry, "Nickname", player); Set(entry, "Reason", reason);
            Type listType = typeof(List<>).MakeGenericType(TypeOf("UserReportMemoEntry"));
            IList entries = (IList)Activator.CreateInstance(listType); entries.Add(entry); Set(record, "Entries", entries);
            Call(store, "Save", key, record);
        }

        private static void Search(TextBox search, DataGridView grid, string text, int expected)
        {
            search.Text = text;
            WaitFor(() => grid.Rows.Count == expected && !(bool)FieldValue(search.FindForm(), "_filterPending"));
            Assert(grid.Rows.Count == expected, "Search matches expected visible count for: " + text.Replace('\t', ' '));
        }
        private static void WaitFor(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(5); }
            if (!condition()) throw new TimeoutException("Debounced filter did not render the expected state.");
        }
        private static void Show(Form form) { form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents(); }
        private static void Capture(Form form, string name) { using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(_artifacts, name), ImageFormat.Png); } }
        private static Type TypeOf(string name) { return _app.GetType("TarkovServerReporter." + name, true); }
        private static object New(string name, params object[] args) { return Activator.CreateInstance(TypeOf(name), Instance, null, args, null); }
        private static object Get(object target, string name) { return target.GetType().GetProperty(name, Instance).GetValue(target, null); }
        private static void Set(object target, string name, object value) { target.GetType().GetProperty(name, Instance).SetValue(target, value, null); }
        private static object FieldValue(object target, string name) { return target.GetType().GetField(name, Instance).GetValue(target); }
        private static T Field<T>(object target, string name) { return (T)FieldValue(target, name); }
        private static void SetField(object target, string name, object value) { target.GetType().GetField(name, Instance).SetValue(target, value); }
        private static object Call(object target, string name, params object[] args) { return target.GetType().InvokeMember(name, Instance | BindingFlags.InvokeMethod, null, target, args); }
        private static void Assert(bool condition, string message) { _assertions++; if (!condition) throw new InvalidOperationException(message); }
    }
}
