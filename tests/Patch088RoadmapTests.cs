using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class Patch088RoadmapTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Assembly _app;
        private static string _artifacts;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                _app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                _artifacts = Path.Combine(args[1], "patch088-roadmap");
                Directory.CreateDirectory(_artifacts);
                StaUiTestHarness.Run(Run);
                Console.WriteLine("v0.8.8 small roadmap tests passed.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static void Run()
        {
            Type main = _app.GetType("TarkovServerReporter.MainForm", true);
            Type texts = _app.GetType("TarkovServerReporter.AppText", true);
            foreach (string language in new[] { "ko", "en" })
            {
                texts.GetMethod("SetLanguage", Static).Invoke(null, new object[] { language });
                object session = Activator.CreateInstance(_app.GetType("TarkovServerReporter.ServerSession", true));
                Set(session, "SessionStarted", new DateTime(2025, 12, 30));
                Set(session, "OperationStartedAt", new DateTime(2025, 12, 31, 23, 55, 0));
                Set(session, "OperationEndedAt", new DateTime(2026, 1, 1, 0, 15, 0));
                SetEnum(session, "OperationState", "Completed");
                Assert(Call(main, "FormatOperationSummary", session).ToString().EndsWith(" / 26.01.01 00:15"),
                    "year crossing uses the operation's own start and end years");
                Set(session, "OperationStartedAt", new DateTime(2024, 2, 3, 20, 0, 0));
                Set(session, "OperationEndedAt", new DateTime(2024, 2, 3, 20, 20, 0));
                Assert(Call(main, "FormatOperationSummary", session).ToString().EndsWith(" / 20:20"),
                    "historical year and unrelated detected date do not expand same-day display");
                Set(session, "NetworkStatisticsObserved", true);
                Set(session, "NetworkReceived", 20L);
                double[] values = { 0, double.Epsilon, 0.000099, 0.0001, 0.0009999, 0.001, 0.0499999, 0.05 };
                foreach (double value in values)
                {
                    Set(session, "NetworkLoss", value);
                    Color actual = (Color)Call(main, "GetPacketLossColor", session);
                    Color expected = (Color)main.GetField(value >= 0.05 ? "Danger"
                        : value >= 0.001 ? "Warning" : "Success", Static).GetValue(null);
                    Assert(actual == expected, "loss threshold compares original value " + value);
                    string display = Call(main, "FormatPacketLossDisplay", session).ToString();
                    if (value > 0 && value < 0.0001)
                        Assert(display == (language == "ko" ? "0.01% 미만" : "<0.01%"),
                            "tiny positive value has explicit less-than wording");
                }
                Set(session, "NetworkLoss", null);
                Assert((Color)Call(main, "GetPacketLossColor", session) == (Color)main.GetField("TextMuted", Static).GetValue(null),
                    "unmeasured loss stays distinct from measured zero");
                Set(session, "NetworkStatisticsAt", null);
                Assert(!Call(main, "FormatHistorySummary", session).ToString().Contains("0%"),
                    "copied summary does not fabricate missing packet loss");

                Type metadataType = _app.GetType("TarkovServerReporter.BlockedServerMetadata", true);
                Type blockedType = _app.GetType("TarkovServerReporter.BlockedServersForm", true);
                object metadata = Activator.CreateInstance(metadataType);
                Assert(Call(blockedType, "GetObservedGameText", metadata).ToString() == (language == "ko" ? "미확인" : "Unknown"),
                    "missing game evidence displays unknown");
                Set(metadata, "ObservedEft", true);
                Set(metadata, "ObservedArena", true);
                Assert(Call(blockedType, "GetObservedGameText", metadata).ToString() == (language == "ko" ? "본편·아레나" : "EFT·Arena"),
                    "shared IP keeps both observed games");

                using (var form = (Form)Activator.CreateInstance(main, new object[] { true }))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-2400, -1600);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    Button noteButton = (Button)main.GetField("_operationNoteButton", Instance).GetValue(form);
                    bool requested = false;
                    EventHandler handler = delegate { requested = true; };
                    main.GetEvent("OperationNoteRequested", Instance).GetAddMethod(true).Invoke(form, new object[] { handler });
                    noteButton.PerformClick();
                    Assert(requested, "operation note button raises the integration event without requiring a raid/IP");
                    var grid = (DataGridView)main.GetField("_historyGrid", Instance).GetValue(form);
                    Assert(grid.Columns["userReport"].Width <= 70, "compact report column uses less space");
                    foreach (int width in new[] { 1180, 1600 })
                    {
                        form.Size = new Size(width, 860);
                        Application.DoEvents();
                        Assert(noteButton.Visible && noteButton.Width > 70 && noteButton.Height > 20,
                            "operation note action remains usable at width " + width);
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(_artifacts, language + "-" + width + ".png"), ImageFormat.Png);
                        }
                    }
                    main.GetMethod("ShowHistoryContextMenu", Instance).Invoke(form,
                        new object[] { grid.Rows[0], new Point(20, 50) });
                    var menu = (ContextMenuStrip)main.GetField("_historyActionMenu", Instance).GetValue(form);
                    Assert(menu.Items.Count == 10, "history menu provides lookup, action, memo, screenshots and separate copy commands");
                    menu.Close();
                }
            }
        }

        private static object Call(Type type, string name, object argument)
        { return type.GetMethod(name, Static).Invoke(null, new[] { argument }); }
        private static void Set(object target, string name, object value)
        { target.GetType().GetProperty(name, Instance).SetValue(target, value, null); }
        private static void SetEnum(object target, string name, string value)
        { Set(target, name, Enum.Parse(target.GetType().GetProperty(name).PropertyType, value)); }
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
    }
}
