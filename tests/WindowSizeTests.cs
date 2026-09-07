// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class WindowSizeTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static Assembly _app;
        private static string _root;
        private static string _artifacts;
        private static int _assertions;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 2) throw new ArgumentException("WindowSizeTests <product> <artifacts>");
                _app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                _artifacts = Path.Combine(Path.GetFullPath(args[1]), "window-size");
                Directory.CreateDirectory(_artifacts);
                _root = Path.Combine(Path.GetTempPath(), "TSG-WindowSize-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_root);
                TestStore();
                TestScaling();
                StaUiTestHarness.Run(delegate
                {
                    foreach (string language in new[] { "ko-KR", "en" })
                    {
                        TypeOf("AppText").GetMethod("SetLanguage", Static).Invoke(null, new object[] { language });
                        TestMainWindow(language);
                        TestConstrainedWindow(language);
                    }
                });
                Console.WriteLine("WindowSizeTests: PASS (" + _assertions + " assertions)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("WindowSizeTests: FAIL " + exception);
                return 1;
            }
            finally
            {
                if (_root != null && Directory.Exists(_root))
                {
                    string prefix = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
                        + Path.DirectorySeparatorChar + "TSG-WindowSize-";
                    if (!Path.GetFullPath(_root).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unsafe test cleanup path.");
                    Directory.Delete(_root, true);
                }
            }
        }

        private static void TestStore()
        {
            object store = NewStore("store");
            string path = SettingsPath(store);
            Assert(Path.GetFileName(path) == "main-window-size.json", "Window dimensions use their own settings file.");
            Assert(Load(store) == null && !File.Exists(path), "Missing settings leave the default size untouched without creating a file.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string sibling = Path.Combine(Path.GetDirectoryName(path), "preferences.json");
            byte[] untouched = Encoding.UTF8.GetBytes("{\"Language\":\"en\",\"Unrelated\":\"사용자 설정\"}");
            File.WriteAllBytes(sibling, untouched);

            string[] invalidDocuments =
            {
                "", "{", "null", "[]", "{}",
                "{\"Format\":\"unknown\",\"Width\":1200,\"Height\":800}",
                "{\"Width\":1200,\"Height\":800}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":1200}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":\"1200\",\"Height\":800}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":1200,\"Height\":800.5}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":true,\"Height\":800}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":null,\"Height\":800}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":319,\"Height\":800}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":1200,\"Height\":199}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":16385,\"Height\":800}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":1200,\"Height\":16385}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":2147483648,\"Height\":800}",
                "{\"Format\":\"main-window-size-v1\",\"Width\":1200,\"Height\":800}" + new string(' ', 4096)
            };
            foreach (string json in invalidDocuments)
            {
                File.WriteAllText(path, json, new UTF8Encoding(false));
                byte[] before = File.ReadAllBytes(path);
                Assert(Load(store) == null, "Untrusted or oversized dimensions must not be restored: " + json.Substring(0, Math.Min(110, json.Length)));
                Assert(File.ReadAllBytes(path).SequenceEqual(before), "Reading invalid size settings is non-destructive.");
            }

            foreach (Size expected in new[] { new Size(320, 200), new Size(1203, 807), new Size(16384, 16384) })
            {
                Assert(Save(store, expected), "A valid normal client size can be saved.");
                object reopened = Activator.CreateInstance(TypeOf("WindowSizeStore"), Instance, null,
                    new object[] { Path.GetDirectoryName(path) }, null);
                Assert(Load(reopened) == expected, "The exact client size survives a fresh store instance.");
                var document = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                Assert(document != null && document.Count == 3
                    && Convert.ToString(document["Format"]) == "main-window-size-v1"
                    && document.ContainsKey("Width") && document.ContainsKey("Height"),
                    "Persist only format and client dimensions, with no position, display, columns, or maximized state.");
            }

            byte[] validBefore = File.ReadAllBytes(path);
            foreach (Size invalid in new[] { Size.Empty, new Size(-1, 800), new Size(1200, 199), new Size(16385, 800) })
            {
                Assert(!Save(store, invalid), "Invalid dimensions must fail without throwing.");
                Assert(File.ReadAllBytes(path).SequenceEqual(validBefore), "An invalid save must preserve the prior valid dimensions.");
            }
            Assert(File.ReadAllBytes(sibling).SequenceEqual(untouched), "Window-size storage never rewrites neighboring preferences.");

            object unavailable = NewStore("unwritable-target");
            Directory.CreateDirectory(SettingsPath(unavailable));
            Assert(!Save(unavailable, new Size(1200, 800)), "A destination occupied by a directory reports a save failure without crashing.");
            Assert(Load(unavailable) == null, "An unreadable settings target falls back to the normal default.");
        }

        private static void TestScaling()
        {
            Assert(Logical(new Size(1200, 900), 96) == new Size(1200, 900), "96-DPI dimensions retain their size.");
            Assert(Logical(new Size(1200, 900), 144) == new Size(800, 600), "Persist logical dimensions at 150 percent scaling.");
            Assert(Logical(new Size(1600, 1200), 192) == new Size(800, 600), "Persist logical dimensions at 200 percent scaling.");
            Assert(Restore(new Size(800, 600), 144, new Size(900, 650), new Size(1800, 1200)) == new Size(1200, 900),
                "Restore a logical size at the current DPI.");
            Assert(Restore(new Size(9000, 9000), 96, new Size(960, 700), new Size(1400, 900)) == new Size(1400, 900),
                "A large saved size fits inside the available client area.");
            Assert(Restore(new Size(400, 200), 96, new Size(960, 700), new Size(1400, 900)) == new Size(960, 700),
                "A small saved size respects the current minimum client area.");
            Assert(Restore(new Size(1300, 200), 96, new Size(960, 700), new Size(1400, 900)) == new Size(1300, 700),
                "Clamp the dimensions independently without altering an already valid width.");
            Assert(Restore(new Size(9000, 800), 96, new Size(960, 700), new Size(1400, 900)) == new Size(1400, 800),
                "Clamp an excessive width while preserving a valid height.");
            foreach (int dpi in new[] { 96, 120, 144, 192 })
            {
                Size scaledMinimum = new Size(960 * dpi / 96, 700 * dpi / 96);
                Size available = new Size(900, 650);
                Assert(Restore(new Size(1200, 900), dpi, scaledMinimum, available) == available,
                    "The working area takes precedence over a larger minimum at DPI " + dpi + ".");
            }
            Assert(Restore(new Size(1200, 900), 96, new Size(960, 700), new Size(1280, 650)) == new Size(1200, 650),
                "A short working area lowers only the height minimum.");
            Assert(Restore(new Size(1200, 900), 96, new Size(960, 700), new Size(800, 1000)) == new Size(800, 900),
                "A narrow working area lowers only the width minimum.");
            Assert(Restore(new Size(1200, 900), 96, new Size(960, 700), Size.Empty) == new Size(1, 1),
                "An empty available area does not produce negative or overflowing dimensions.");
        }

        private static void TestMainWindow(string language)
        {
            object store = NewStore("main-" + language);
            Size normalSize;
            using (Form form = NewMain(store))
            {
                ShowOffscreen(form);
                Size desired = new Size(Math.Max(form.MinimumSize.Width + 24, 1180), Math.Max(form.MinimumSize.Height, 780));
                form.ClientSize = desired;
                Pump();
                normalSize = form.ClientSize;
                AssertHeader(form, language);
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(_artifacts, "main-header-" + language + ".png"), ImageFormat.Png);
                }
                Size expected = Logical(normalSize, form.DeviceDpi);
                form.Close();
                Pump();
                Assert(Load(store) == expected, "Closing a resized main window saves its normal client size: " + language);
            }

            using (Form restored = NewMain(store))
            {
                ShowOffscreen(restored);
                Assert(restored.WindowState == FormWindowState.Normal, "Reopening starts normally rather than restoring maximized state.");
                Assert(restored.ClientSize == normalSize, "A newly created main window restores its prior normal client size: " + language);
                restored.Close();
                Pump();
            }

            foreach (FormWindowState ending in new[] { FormWindowState.Maximized, FormWindowState.Minimized })
            {
                using (Form form = NewMain(store))
                {
                    // Opacity prevents a maximize test from flashing a synthetic app over the user's desktop.
                    form.Opacity = 0;
                    ShowOffscreen(form);
                    form.ClientSize = normalSize;
                    Pump();
                    Size expected = Logical(form.ClientSize, form.DeviceDpi);
                    form.WindowState = ending;
                    Pump();
                    Assert(form.WindowState == ending, "The actual main form entered the requested closing state: " + ending);
                    form.Close();
                    Pump();
                    Assert(Load(store) == expected, "Closing while " + ending + " preserves the last normal client size: " + language);
                }
            }

            byte[] beforePreview = File.ReadAllBytes(SettingsPath(store));
            using (Form preview = (Form)Activator.CreateInstance(TypeOf("MainForm"), new object[] { true }))
            {
                FieldInfo persistence = TypeOf("MainForm").GetFields(Instance)
                    .Single(field => field.FieldType == TypeOf("WindowSizeStore"));
                Assert(persistence.GetValue(preview) == null,
                    "An ordinary demo preview has no persistence store and cannot touch the real profile.");
                ShowOffscreen(preview);
                preview.ClientSize = new Size(normalSize.Width + 31, normalSize.Height + 19);
                preview.Close();
                Pump();
            }
            Assert(File.ReadAllBytes(SettingsPath(store)).SequenceEqual(beforePreview), "An ordinary demo preview does not overwrite the injected persistent dimensions.");
        }

        private static void TestConstrainedWindow(string language)
        {
            foreach (bool hasSavedSize in new[] { false, true })
            {
                foreach (Size workingSize in new[] { new Size(1280, 650), new Size(820, 650), new Size(800, 900) })
                {
                    string scenario = language + "-" + workingSize.Width + "x" + workingSize.Height
                        + (hasSavedSize ? "-saved" : "-default");
                    object store = NewStore("small-" + scenario);
                    if (hasSavedSize) Assert(Save(store, new Size(1500, 1000)), "Seed a large prior window for " + scenario);
                    using (Form form = NewMain(store))
                    {
                        ShowOffscreen(form);
                        Size regularMinimum = form.MinimumSize;
                        Size regularClientMinimum = new Size(regularMinimum.Width - (form.Width - form.ClientSize.Width),
                            regularMinimum.Height - (form.Height - form.ClientSize.Height));
                        TypeOf("MainForm").GetMethod("RestoreWindowSizeForWorkingArea", Instance)
                            .Invoke(form, new object[] { Load(store), new Rectangle(Point.Empty, workingSize) });
                        Pump();
                        Assert(form.Width <= workingSize.Width && form.Height <= workingSize.Height,
                            "The entire window, including frame and scrollbars, fits the working area: " + scenario);
                        Assert(form.MinimumSize.Width <= workingSize.Width && form.MinimumSize.Height <= workingSize.Height,
                            "The temporary minimum never pushes the window beyond the available screen: " + scenario);
                        Assert(form.AutoScroll, "A constrained window exposes its content through scrollbars: " + scenario);
                        Control content = form.Controls[0];
                        Assert(content.MinimumSize == regularClientMinimum,
                            "The regular layout keeps its usable content dimensions: " + scenario);
                        Assert(form.VerticalScroll.Visible == (workingSize.Height < regularMinimum.Height),
                            "Vertical scrolling is available only when required: " + scenario);
                        Assert(form.HorizontalScroll.Visible == (workingSize.Width < regularMinimum.Width),
                            "Horizontal scrolling is available only when required: " + scenario);
                        if (workingSize.Width == 820) Capture(form, "small-top-" + scenario);

                        form.AutoScrollPosition = new Point(content.Width, content.Height);
                        Pump();
                        Control status = (Control)TypeOf("MainForm").GetField("_statusLabel", Instance).GetValue(form);
                        Rectangle statusBounds = form.RectangleToClient(status.RectangleToScreen(status.ClientRectangle));
                        Assert(form.ClientRectangle.IntersectsWith(statusBounds)
                            && statusBounds.Top >= 0 && statusBounds.Bottom <= form.ClientSize.Height,
                            "Scrolling exposes the bottom status area without losing it beyond the screen: " + scenario);
                        if (workingSize.Width == 820) Capture(form, "small-bottom-" + scenario);

                        form.Size = new Size(1500, 1000);
                        Pump();
                        Assert(!form.VerticalScroll.Visible && !form.HorizontalScroll.Visible,
                            "Scrollbars disappear when the window grows large enough: " + scenario);
                        Assert(content.Location == Point.Empty && content.Size == form.ClientSize,
                            "The content expands to fill the window after leaving the small-screen size: " + scenario);
                        form.Close();
                        Pump();
                    }
                }
            }
        }

        private static void Capture(Form form, string name)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(Path.Combine(_artifacts, name + ".png"), ImageFormat.Png);
            }
        }

        private static void AssertHeader(Form form, string language)
        {
            Button update = (Button)TypeOf("MainForm").GetField("_manualUpdateButton", Instance).GetValue(form);
            var header = update.Parent.Parent as TableLayoutPanel;
            Assert(header != null && header.RowStyles.Count == 2
                && header.RowStyles.Cast<RowStyle>().All(row => row.SizeType == SizeType.Percent && Math.Abs(row.Height - 50F) < 0.1F),
                "The two right-hand header rows retain the official 0.8.3 vertical proportions.");
            string subtitleText = (string)TypeOf("AppText").GetMethod("Get", Static).Invoke(null, new object[] { "Main.Header.SubtitleFull" });
            Label subtitle = Descendants(form).OfType<Label>().Single(control => control.Text == subtitleText);
            int buttonCenter = update.PointToScreen(new Point(0, update.Height / 2)).Y;
            int subtitleCenter = subtitle.PointToScreen(new Point(0, subtitle.Height / 2)).Y;
            int tolerance = (int)Math.Ceiling(8.0 * form.DeviceDpi / 96.0);
            Assert(Math.Abs(buttonCenter - subtitleCenter) <= tolerance,
                "Version/action controls align vertically with the left subtitle: " + language);
            foreach (Control control in update.Parent.Controls)
                Assert(update.Parent.ClientRectangle.Contains(control.Bounds), "Every version/action control fits the dynamic header width: " + language + " " + control.Text);
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child)) yield return nested;
            }
        }

        private static void ShowOffscreen(Form form)
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-24000, -24000);
            form.Show();
            Pump();
        }

        private static void Pump() { Application.DoEvents(); Application.DoEvents(); }
        private static Type TypeOf(string name) { return _app.GetType("TarkovServerReporter." + name, true); }
        private static object NewStore(string name)
        {
            return Activator.CreateInstance(TypeOf("WindowSizeStore"), Instance, null,
                new object[] { Path.Combine(_root, name) }, null);
        }
        private static Form NewMain(object store)
        {
            return (Form)Activator.CreateInstance(TypeOf("MainForm"), Instance, null, new object[] { true, store }, null);
        }
        private static string SettingsPath(object store)
        {
            return (string)TypeOf("WindowSizeStore").GetProperty("SettingsPath", Instance).GetValue(store, null);
        }
        private static Size? Load(object store) { return (Size?)TypeOf("WindowSizeStore").GetMethod("Load", Instance).Invoke(store, null); }
        private static bool Save(object store, Size value) { return (bool)TypeOf("WindowSizeStore").GetMethod("Save", Instance).Invoke(store, new object[] { value }); }
        private static Size Logical(Size value, int dpi)
        {
            return (Size)TypeOf("WindowSizeStore").GetMethod("ToLogicalClientSize", Static).Invoke(null, new object[] { value, dpi });
        }
        private static Size Restore(Size value, int dpi, Size minimum, Size available)
        {
            return (Size)TypeOf("WindowSizeStore").GetMethod("RestoreClientSize", Static).Invoke(null, new object[] { value, dpi, minimum, available });
        }
        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
