// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class ColumnWidthTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Assembly _app;
        private static string _root;
        private static int _checks;
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                _app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                _root = Path.Combine(Path.GetTempPath(), "TSG-Columns-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                TestStorage();
                StaUiTestHarness.Run(delegate
                {
                    foreach (string language in new[] { "ko-KR", "en" })
                    foreach (string view in new[] { "main", "blocked", "notes" }) TestView(view, language);
                    TestUntouchedDefaults();
                });
                Console.WriteLine("ColumnWidthTests: PASS (" + _checks + " assertions)");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally
            {
                if (_root != null && Directory.Exists(_root)
                    && Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\TSG-Columns-", StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(_root, true);
            }
        }

        private static void TestStorage()
        {
            object store = Store("main", "ko-KR");
            string path = PathOf(store);
            Check(Load(store).Count == 0 && !File.Exists(path), "First run creates no column settings.");
            foreach (string invalid in new[] { "", "{", "[]", "null", "{}", new string('x', 17000),
                "{\"Format\":\"future\",\"Columns\":{\"time\":180}}", "{\"Format\":\"column-widths-v1\",\"Columns\":[]}" })
            {
                File.WriteAllText(path, invalid, new UTF8Encoding(false));
                Check(Load(store).Count == 0 && File.ReadAllText(path) == invalid, "Malformed settings fall back without rewriting the file.");
            }
            File.WriteAllText(path, "{\"Format\":\"column-widths-v1\",\"Columns\":{\"time\":181,\"negative\":-2,\"huge\":9000,\"string\":\"200\",\"float\":20.5,\"futureColumn\":180}}");
            var widths = Load(store);
            Check(widths.Count == 2 && widths["time"] == 181, "Valid widths survive alongside invalid or newly introduced entries.");
            Check(Save(store, new Dictionary<string, int> { { "time", 233 }, { "result", 290 } }), "Atomic column save succeeds.");
            Check(Load(Store("main", "ko-KR"))["time"] == 233, "A fresh store reads the exact saved width.");
            byte[] prior = File.ReadAllBytes(path);
            Check(!Save(store, new Dictionary<string, int> { { "time", 0 } }) && File.ReadAllBytes(path).SequenceEqual(prior), "Bad writes preserve existing settings.");
            Check(Load(Store("main", "en")).Count == 0 && Load(Store("blocked", "ko-KR")).Count == 0, "Language and view preferences are independent.");
            object unavailable = New("ColumnWidthStore", Path.Combine(_root, "unwritable"), "notes", "en");
            Directory.CreateDirectory(PathOf(unavailable));
            Check(!Save(unavailable, widths) && Load(unavailable).Count == 0, "Denied destination is non-fatal.");
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            {
                int pixels = (int)TypeOf("ColumnWidthStore").GetMethod("PixelWidth", Static).Invoke(null, new object[] { 240, dpi, 2 });
                int logical = (int)TypeOf("ColumnWidthStore").GetMethod("LogicalWidth", Static).Invoke(null, new object[] { pixels, dpi });
                Check(logical == 240, "DPI round trip preserves logical width at " + dpi);
            }
            Check((int)TypeOf("ColumnWidthStore").GetMethod("PixelWidth", Static).Invoke(null, new object[] { 2, 96, 186 }) == 186,
                "New column minimums override obsolete saved widths.");
            File.Delete(path);
        }

        private static void TestView(string view, string language)
        {
            TypeOf("AppText").GetMethod("SetLanguage", Static).Invoke(null, new object[] { language });
            object store = Store(view, language);
            string fixedColumn = view == "main" ? "time" : view == "blocked" ? "status" : "date";
            string fillColumn = view == "main" ? "result" : view == "blocked" ? "ip" : "preview";
            int expectedFixed, expectedFill;
            using (Form form = CreateView(view))
            {
                Show(form);
                var grid = Grid(form, view);
                Check(!File.Exists(PathOf(store)), view + "/" + language + ": startup does not save default widths.");
                Drag(grid, grid.Columns[fixedColumn], 29);
                expectedFixed = grid.Columns[fixedColumn].Width;
                Check(Load(store).ContainsKey(fixedColumn), "Completed native drag is saved immediately.");
                Drag(grid, grid.Columns[fillColumn], 18, true);
                Check(grid.Columns[fillColumn].AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill && Load(store).Count == 1,
                    "Cancelling a fill-column drag restores automatic sizing without saving it.");
                Drag(grid, grid.Columns[fillColumn], 37);
                expectedFill = grid.Columns[fillColumn].Width;
                Check(grid.Columns[fillColumn].AutoSizeMode == DataGridViewAutoSizeColumnMode.None, "A manually resized fill column keeps its explicit width.");
                Check(Load(store).Count == 2, "Only manually resized columns are remembered.");
                form.Width += 51; Pump();
                Check(grid.Columns[fixedColumn].Width == expectedFixed && grid.Columns[fillColumn].Width == expectedFill, "Window resizing preserves custom widths.");
                byte[] beforeCancel = File.ReadAllBytes(PathOf(store));
                Drag(grid, grid.Columns[fixedColumn], 18, true);
                Check(File.ReadAllBytes(PathOf(store)).SequenceEqual(beforeCancel), "Losing capture does not save an unfinished drag.");
                if (view == "blocked")
                {
                    Drag(grid, grid.Columns["note"], 39);
                    int noteWidth = grid.Columns["note"].Width;
                    Call(grid, "OnDpiChangedAfterParent", EventArgs.Empty);
                    Pump();
                    Check(grid.Columns["note"].Width == noteWidth, "DPI metrics cannot overwrite a customized action column.");
                }
                form.Close(); Pump();
            }
            using (Form form = CreateView(view))
            {
                Show(form);
                var grid = Grid(form, view);
                Check(Math.Abs(grid.Columns[fixedColumn].Width - expectedFixed) <= 1 && Math.Abs(grid.Columns[fillColumn].Width - expectedFill) <= 1,
                    view + "/" + language + ": both fixed and fill-column custom widths survive reopening.");
                Check(!Load(store).ContainsKey("stickyActionSpacer") && !Load(store).ContainsKey("blockAction"), "Internal sticky columns never enter settings.");
                DataGridViewColumn column = grid.Columns[fixedColumn];
                grid.FirstDisplayedScrollingColumnIndex = column.Index;
                Pump();
                Rectangle header = grid.GetCellDisplayRectangle(column.Index, -1, false);
                int x = header.Right - 1, y = header.Top + header.Height / 2;
                int beforeFit = column.Width;
                Call(grid, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
                IntPtr point = new IntPtr((y << 16) | (x & 65535));
                SendMessage(grid.Handle, 0x0201, new IntPtr(1), point);
                SendMessage(grid.Handle, 0x0202, IntPtr.Zero, point);
                SendMessage(grid.Handle, 0x0203, new IntPtr(1), point);
                SendMessage(grid.Handle, 0x0202, IntPtr.Zero, point);
                Pump();
                expectedFixed = column.Width;
                Check(column.Width != beforeFit, "The native double-click actually fits the column to its content.");
                int stored = (int)TypeOf("ColumnWidthStore").GetMethod("LogicalWidth", Static).Invoke(null, new object[] { expectedFixed, grid.DeviceDpi });
                Check(Load(store)[fixedColumn] == stored, "Native divider double-click saves its fitted width.");
                form.Close(); Pump();
            }
            using (Form form = CreateView(view))
            {
                Show(form);
                Check(Math.Abs(Grid(form, view).Columns[fixedColumn].Width - expectedFixed) <= 1, "The auto-fitted width survives another fresh window.");
                form.Close(); Pump();
            }
        }

        private static void TestUntouchedDefaults()
        {
            using (Form form = (Form)New("MainForm", true))
            {
                Show(form);
                var grid = Grid(form, "main");
                Check(grid.Columns["result"].AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill, "Demo defaults retain automatic fill and do not read user custom widths.");
                form.Close(); Pump();
            }
        }

        private static Form CreateView(string view)
        {
            Form form;
            if (view == "main") return (Form)New("MainForm", true, null, _root);
            if (view == "blocked")
            {
                form = (Form)New("BlockedServersForm");
                var events = (EventHandlerList)typeof(Component).GetProperty("Events", Instance).GetValue(form, null);
                FieldInfo key = typeof(Form).GetField("EVENT_SHOWN", Static);
                events.RemoveHandler(key.GetValue(null), events[key.GetValue(null)]);
            }
            else
            {
                object raids = New("RaidNoteStore", Path.Combine(_root, "raid-notes"));
                object record = New("RaidNoteRecord");
                string key = new string('a', 64);
                record.GetType().GetProperty("Key").SetValue(record, key, null);
                record.GetType().GetProperty("NoteText").SetValue(record, "Column width review fixture", null);
                record.GetType().GetProperty("Game").SetValue(record, "EFT", null);
                raids.GetType().InvokeMember("Save", Instance | BindingFlags.InvokeMethod, null, raids, new object[] { key, record });
                form = (Form)New("RaidNoteArchiveForm", raids, New("UserReportMemoStore", Path.Combine(_root, "report-notes")), false);
            }
            TypeOf("ColumnWidthPersistence").GetMethod("Attach", Static).Invoke(null, new object[] { form, view, _root });
            return form;
        }

        private static void Drag(DataGridView grid, DataGridViewColumn column, int distance, bool cancel = false)
        {
            grid.FirstDisplayedScrollingColumnIndex = column.Index;
            Pump();
            Rectangle header = grid.GetCellDisplayRectangle(column.Index, -1, false);
            int x = header.Right - 1, y = header.Top + header.Height / 2, before = column.Width;
            Call(grid, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
            Check(grid.Cursor == Cursors.SizeWE, "Native column boundary is reachable: " + column.Name);
            Call(grid, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            Check(grid.Capture, "Native drag captures the pointer.");
            Call(grid, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x + distance, y, 0));
            if (cancel) grid.Capture = false;
            Call(grid, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x + distance, y, 0));
            Pump();
            if (!cancel) Check(column.Width == before + distance, "Actual native drag applies exact width: " + column.Name + " before=" + before + " actual=" + column.Width + " delta=" + distance);
        }

        private static void Show(Form form) { form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-24000, -24000); form.Show(); Pump(); }
        private static DataGridView Grid(Form form, string view) { return (DataGridView)form.GetType().GetField(view == "main" ? "_historyGrid" : "_grid", Instance).GetValue(form); }
        private static object Store(string view, string language) { return New("ColumnWidthStore", _root, view, language); }
        private static string PathOf(object store) { return (string)store.GetType().GetProperty("SettingsPath", Instance).GetValue(store, null); }
        private static Dictionary<string, int> Load(object store) { return (Dictionary<string, int>)store.GetType().GetMethod("Load", Instance).Invoke(store, null); }
        private static bool Save(object store, Dictionary<string, int> widths) { return (bool)store.GetType().GetMethod("Save", Instance).Invoke(store, new object[] { widths }); }
        private static Type TypeOf(string name) { return _app.GetType("TarkovServerReporter." + name, true); }
        private static object New(string name, params object[] args) { return Activator.CreateInstance(TypeOf(name), Instance, null, args, null); }
        private static void Call(object target, string name, object argument) { target.GetType().GetMethod(name, Instance).Invoke(target, new[] { argument }); }
        private static void Pump() { Application.DoEvents(); Application.DoEvents(); }
        private static void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
    }
}
