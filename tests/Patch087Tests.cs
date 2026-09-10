using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class Patch087Tests
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
                _artifacts = Path.Combine(args[1], "patch087");
                Directory.CreateDirectory(_artifacts);
                StaUiTestHarness.Run(Run);
                Console.WriteLine("v0.8.7 patch tests passed.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        private static void Run()
        {
            Type catalog = _app.GetType("TarkovServerReporter.PvpSeasonCatalog", true);
            foreach (DateTime? date in new DateTime?[] { new DateTime(2026, 8, 2), DateTime.Today.AddDays(1), null })
                Assert(catalog.GetMethod("Resolve", Static).Invoke(null, new object[] { null, date }) == null,
                    "pre-season, future, and missing raid dates stay unknown: " + date);
            object identity = catalog.GetMethod("Resolve", Static).Invoke(null,
                new object[] { null, new DateTime(2026, 9, 10) });
            Assert(identity != null && (int)identity.GetType().GetProperty("Number").GetValue(identity, null) == 1,
                "reported September raid resolves to season 1 without any game version input");
            var knownSeasons = (Array)catalog.GetField("KnownSeasons", Static).GetValue(null);
            object firstSeason = knownSeasons.GetValue(0);
            try
            {
                // Synthetic transition tests only: September 1 is NOT a real season end.
                Set(firstSeason, "EndsBefore", new DateTime(2026, 9, 1));
                Assert(catalog.GetMethod("Resolve", Static).Invoke(null,
                    new object[] { null, new DateTime(2026, 9, 1) }) == null, "closed season excludes its end boundary");
                Assert(catalog.GetMethod("Resolve", Static).Invoke(null,
                    new object[] { null, new DateTime(2026, 8, 31, 23, 59, 59) }) != null, "historical raid keeps its season after closure");
                object explicitIdentity = catalog.GetMethod("Resolve", Static).Invoke(null,
                    new object[] { 2, new DateTime(2026, 8, 15) });
                Assert((int)explicitIdentity.GetType().GetProperty("Number").GetValue(explicitIdentity, null) == 2,
                    "direct season number takes priority over calendar");
            }
            finally { Set(firstSeason, "EndsBefore", null); }
            Type texts = _app.GetType("TarkovServerReporter.AppText", true);
            Type main = _app.GetType("TarkovServerReporter.MainForm", true);
            foreach (string language in new[] { "ko", "en" })
            {
                texts.GetMethod("SetLanguage", Static).Invoke(null, new object[] { language });
                object session = Activator.CreateInstance(_app.GetType("TarkovServerReporter.ServerSession", true));
                Set(session, "SessionStarted", new DateTime(2026, 9, 10, 21, 0, 0));
                Set(session, "OperationStartedAt", new DateTime(2026, 9, 10, 21, 2, 30));
                Set(session, "OperationEndedAt", new DateTime(2026, 9, 10, 21, 22, 30));
                SetEnum(session, "OperationState", "Completed");
                string summary = Summary(main, session);
                Assert(summary.EndsWith(" / 21:22:30", StringComparison.Ordinal) && summary.Contains("20"), "completed duration and end ordered correctly: " + summary);
                Set(session, "OperationStartedAt", new DateTime(2026, 9, 10, 23, 55, 0));
                Set(session, "OperationEndedAt", new DateTime(2026, 9, 11, 0, 15, 0));
                Assert(Summary(main, session).EndsWith(" / 2026-09-11 00:15:00", StringComparison.Ordinal), "cross-midnight date is explicit");
                SetEnum(session, "OperationState", "InProgress");
                Assert(!Summary(main, session).Contains("00:15"), "in-progress ignores stale end value");
                SetEnum(session, "OperationState", "Completed");
                Set(session, "OperationEndedAt", null);
                Assert(!Summary(main, session).Contains(":"), "missing end never fabricated");
                Set(session, "OperationEndedAt", new DateTime(2026, 9, 10, 23, 50, 0));
                Assert(!Summary(main, session).Contains(":"), "invalid negative duration does not expose end");
                SetEnum(session, "ProgressionMode", "PvpSeason");
                string raw = (string)session.GetType().GetProperty("ProgressionModeText").GetValue(session, null);
                string localized = (string)texts.GetMethod("LocalizeDomainDisplay", Static).Invoke(null, new object[] { raw });
                Assert(localized == "PvP/S?", "unknown season retains v0.8.6 compact notation in both languages");
                SetEnum(session, "CharacterType", "Scav");
                SetEnum(session, "ParticipationType", "Solo");
                Assert((string)session.GetType().GetProperty("RaidTypeAndParticipantText").GetValue(session, null)
                    == "PvP/S? · 스캐브 · 단독", "unknown season keeps its position before character and participation");
                object parsedSession = ReadReportedSeasonFixture();
                Set(session, "PvpSeasonNumber", parsedSession.GetType().GetProperty("PvpSeasonNumber").GetValue(parsedSession, null));
                Assert((string)session.GetType().GetProperty("ProgressionModeText").GetValue(session, null) == "PvP/S1", "numbered season retains compact S1");
                Assert((string)session.GetType().GetProperty("RaidTypeAndParticipantText").GetValue(session, null)
                    == "PvP/S1 · 스캐브 · 단독", "known season keeps its position before character and participation");

                Set(session, "OperationEndedAt", new DateTime(2026, 9, 11, 0, 15, 0));
                Set(session, "IpAddress", "203.0.113.87");
                SetEnum(session, "Game", "Eft");
                Set(session, "MapName", "Factory");
                string row = (string)main.GetMethod("GetMapAndTypeText", Static).Invoke(null, new[] { session });
                Assert(row == (language == "ko" ? "Factory · PvP/S1 · 스캐브 · 단독" : "Factory · PvP/S1 · Scav · Solo"),
                    "log-derived season renders in the original row position: " + row);

                using (var form = (Form)Activator.CreateInstance(main, new object[] { true }))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-2400, -1600);
                    form.ShowInTaskbar = false;
                    form.Show();
                    Application.DoEvents();
                    foreach (int width in new[] { 1180, 1600 })
                    {
                        form.Size = new Size(width, 860);
                        Application.DoEvents();
                        main.GetMethod("SelectSession", Instance).Invoke(form, new[] { session });
                        Application.DoEvents();
                        var labels = (Label[])main.GetField("_detailInfoValueLabels", Instance).GetValue(form);
                        Assert(labels[0].Text.Contains("2026-09-11"), "selected raid shows the full end date");
                        if (labels[0].Visible)
                        {
                            Size text = TextRenderer.MeasureText(labels[0].Text, labels[0].Font,
                                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                            Assert(text.Width <= labels[0].ClientSize.Width, "overnight end fits visible detail at width " + width);
                        }
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                            bitmap.Save(Path.Combine(_artifacts, language + "-" + width + ".png"), ImageFormat.Png);
                        }
                    }
                }
            }
        }

        private static string Summary(Type main, object session)
        { return (string)main.GetMethod("FormatOperationSummary", Static).Invoke(null, new[] { session }); }
        private static object ReadReportedSeasonFixture()
        {
            string root = Path.Combine(_artifacts, "reported-season");
            string folder = Path.Combine(root, "log_2026.09.09_23-59-21_1.1.5.0.47242");
            Directory.CreateDirectory(folder);
            // Observed mode, timestamps, version and map; account/IP/SID/shortId
            // removed or substituted. This is NOT a synthetic PvpSeason1 token.
            File.WriteAllLines(Path.Combine(folder, "application.log"), new[] {
                "2026-09-09 23:59:30.191|1.1.5.0.47242|Info|application|Session mode: PvpSeason",
                "2026-09-10 00:01:01.071|1.1.5.0.47242|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Status: Busy, RaidMode: Online, Ip: 203.0.113.87, Port: 17012, Location: factory4_day, Sid: TEST_season, GameMode: deathmatch, shortId: OBSERVED087'"
            });
            Type pathsType = _app.GetType("TarkovServerReporter.TarkovLogPaths", true);
            object paths = Activator.CreateInstance(pathsType);
            Set(paths, "EftPath", root);
            Type scanner = _app.GetType("TarkovServerReporter.RaidLogScanner", true);
            MethodInfo scan = scanner.GetMethod("Scan", new[] { pathsType, typeof(int) });
            object result = scan.Invoke(null, new object[] { paths, 100 });
            foreach (object raid in (IEnumerable)result.GetType().GetProperty("Sessions").GetValue(result, null))
            {
                Assert((string)raid.GetType().GetProperty("ProgressionModeText").GetValue(raid, null) == "PvP/S1",
                    "observed unnumbered 1.1.5 log produces S1 via the real scanner");
                Assert(raid.GetType().GetProperty("PvpSeasonEvidence", Instance).GetValue(raid, null).ToString() == "SeasonCalendar",
                    "scanner retains calendar evidence separately from direct log evidence");
                return raid;
            }
            throw new InvalidOperationException("Observed season fixture produced no raid");
        }
        private static void Set(object obj, string name, object value)
        { obj.GetType().GetProperty(name, Instance).SetValue(obj, value, null); }
        private static void SetEnum(object obj, string name, string value)
        { Set(obj, name, Enum.Parse(obj.GetType().GetProperty(name, Instance).PropertyType, value)); }
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }
    }
}
