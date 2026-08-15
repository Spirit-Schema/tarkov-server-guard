// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace TarkovServerReporter.Tests
{
    internal static class CoreTests
    {
        private static int _failures;

        private static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            string tempRoot = Path.Combine(Path.GetTempPath(), "TarkovServerReporterTests_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempRoot);
                TestLogScanning(tempRoot);
                TestRootLogScanning(tempRoot);
                TestRichEftRaidScanning(tempRoot);
                TestEftRaidTypesAndUserReports(tempRoot);
                TestNetworkEventBoundariesAndDuplicateMerge(tempRoot);
                TestArenaRaidScanning(tempRoot);
                TestRaidLogQueryFiltering(tempRoot);
                TestLauncherSelectionReading(tempRoot);
                TestPathNormalization(tempRoot);
                TestSteamPathDiscovery(tempRoot);
                TestPingBatchPlanning();
                TestPingCancellation();
                TestPingFormatting();
                TestPingQualityThresholds();
                TestProductUserAgent();
                TestConnectionResultFormatting();
                TestGeoFormatting();
                TestFirewallCommandValidation();
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempRoot) && Path.GetFileName(tempRoot).StartsWith("TarkovServerReporterTests_", StringComparison.Ordinal))
                        Directory.Delete(tempRoot, true);
                }
                catch
                {
                    // Temporary cleanup failure does not invalidate behavior under test.
                }
            }

            if (_failures == 0)
            {
                Console.WriteLine("PASS: all core tests");
                return 0;
            }

            Console.WriteLine("FAIL: " + _failures + " test(s)");
            return 1;
        }

        private static void TestLogScanning(string tempRoot)
        {
            string logs = Path.Combine(tempRoot, "Logs");
            string older = Path.Combine(logs, "log_2026.08.13_20-00-00");
            string newest = Path.Combine(logs, "log_2026.08.14_00-30-00");
            Directory.CreateDirectory(older);
            Directory.CreateDirectory(newest);

            File.WriteAllText(
                Path.Combine(older, "2026.08.13 application.log"),
                "scene preset path:maps/woods_preset.bundle\r\n2026.08.13 20:14:02 Network Ip: 198.51.100.7\r\n",
                Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(newest, "2026.08.14 application.log"),
                "scene preset path:maps/city_preset.bundle\r\n"
                + "2026-08-14 00:31:01 Network Ip: 999.999.999.999\r\n"
                + "2026-08-14 00:32:02 Matched Ip: 203.0.113.42:17000\r\n"
                + "2026-08-14 00:33:04.123 Reconnect Ip:203.0.113.44\r\n",
                Encoding.UTF8);

            Directory.SetCreationTime(older, new DateTime(2026, 8, 13, 20, 0, 0));
            Directory.SetCreationTime(newest, new DateTime(2026, 8, 14, 0, 30, 0));

            IList<ServerSession> sessions = LogScanner.Scan(logs, 10);
            Assert(sessions.Count == 2, "scanner returns both session folders");
            Assert(sessions[0].IpAddress == "203.0.113.44", "scanner keeps the last valid IP in a log");
            Assert(sessions[0].IpDetectedAt == new DateTime(2026, 8, 14, 0, 33, 4),
                "scanner records the timestamp of the matched IP line");
            Assert(sessions[0].MapName == "Streets of Tarkov", "scanner maps preset names");
            Assert(sessions[1].IpAddress == "198.51.100.7", "scanner reads older session IP");

            File.AppendAllText(
                Path.Combine(newest, "2026.08.14 application.log"),
                "2026-08-14 00:35:06 Reconnect Ip: 203.0.113.45\r\n",
                Encoding.UTF8);
            sessions = LogScanner.Scan(logs, 10);
            Assert(sessions[0].IpAddress == "203.0.113.45", "scanner invalidates cache when the active log grows");
        }

        private static void TestRootLogScanning(string tempRoot)
        {
            string direct = Path.Combine(tempRoot, "DirectLogs");
            Directory.CreateDirectory(direct);
            string firstLog = Path.Combine(direct, "application-part1.log");
            string secondLog = Path.Combine(direct, "application-part2.log");
            File.WriteAllText(
                firstLog,
                "2026-08-14 01:00:05 Ip: 192.0.2.55\r\n",
                Encoding.UTF8);
            File.WriteAllText(
                secondLog,
                "scene preset path:maps/laboratory.bundle\r\n",
                Encoding.UTF8);
            File.SetLastWriteTime(firstLog, new DateTime(2026, 8, 14, 1, 0, 0));
            File.SetLastWriteTime(secondLog, new DateTime(2026, 8, 14, 1, 1, 0));

            IList<ServerSession> sessions = LogScanner.Scan(direct, 5);
            Assert(sessions.Count == 1, "scanner supports a selected session folder directly");
            Assert(sessions[0].IpAddress == "192.0.2.55", "scanner preserves IP from an earlier rotated log");
            Assert(sessions[0].IpDetectedAt == new DateTime(2026, 8, 14, 1, 0, 5),
                "scanner preserves IP detection time from an earlier rotated log");
            Assert(sessions[0].MapName == "The Lab", "scanner reads map from the latest rotated log");
        }

        private static void TestRichEftRaidScanning(string tempRoot)
        {
            string logs = Path.Combine(tempRoot, "RichEftLogs");
            string folder = Path.Combine(logs, "log_2026.08.14_09-50-00");
            Directory.CreateDirectory(folder);

            string application = string.Join("\r\n", new[]
            {
                "2026.08.14 09:59:40|1.1.0.1.46699|Info|MatchingCompleted:35.5",
                "2026.08.14 10:00:00|1.1.0.1.46699|Trace|NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.10, Port: 17000, Location: Shoreline, Sid: SG-SIN01G001_raid-one, GameMode: deathmatch, shortId: EFT001'",
                "2026.08.14 10:59:30|1.1.0.1.46701|Info|MatchingCompleted:71.25",
                "2026.08.14 11:00:00|1.1.0.1.46701|Trace|NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.11, Port: 17001, Location: woods_preset, Sid: JP-TK02G005_raid-two, GameMode: pve, shortId: EFT002'",
                string.Empty
            });
            File.WriteAllText(
                Path.Combine(folder, "2026.08.14 application.log"),
                application,
                Encoding.UTF8);

            string network = string.Join("\r\n", new[]
            {
                "2026.08.14 10:00:01|Connect (address: 203.0.113.10:17000)",
                "2026.08.14 10:00:02|Enter to the 'Connected' state (address: 203.0.113.10:17000)",
                "2026.08.14 10:20:00|Statistics (address: 203.0.113.10:17000, rtt: 88.25, lose: 0.01, sent: 1200, received: 1188)",
                "2026.08.14 10:30:00|Enter to the 'Disconnected' state (address: 203.0.113.10:17000, reason: 0)",
                "2026.08.14 11:00:01|Connect (address: 203.0.113.11:17001)",
                "2026.08.14 11:00:02|Enter to the 'Connected' state (address: 203.0.113.11:17001)",
                "2026.08.14 11:10:00|Enter to the 'Disconnected' state (address: 203.0.113.11:17001, reason: 2)",
                "2026.08.14 11:10:05|Connect (address: 203.0.113.11:17001)",
                "2026.08.14 11:10:06|Enter to the 'Connected' state (address: 203.0.113.11:17001)",
                "2026.08.14 11:20:00|Statistics (address: 203.0.113.11:17001, rtt: 155.5, lose: 0.15, sent: 900, received: 765)",
                string.Empty
            });
            File.WriteAllText(
                Path.Combine(folder, "2026.08.14 network-connection.log"),
                network,
                Encoding.UTF8);

            int foldersScanned;
            IList<ServerSession> sessions = RaidLogScanner.ScanGame(
                logs,
                TarkovGame.Eft,
                100,
                out foldersScanned);

            Assert(foldersScanned == 1, "rich EFT scanner visits the selected session folder");
            Assert(sessions.Count == 2, "rich EFT scanner keeps multiple raids from one log folder");
            Assert(sessions.Select(item => item.SessionKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
                "rich EFT raids receive distinct stable session keys");

            ServerSession first = sessions.SingleOrDefault(item => item.ShortId == "EFT001");
            ServerSession second = sessions.SingleOrDefault(item => item.ShortId == "EFT002");
            Assert(first != null && first.Game == TarkovGame.Eft,
                "rich EFT scanner identifies the game and first short id");
            Assert(first != null
                && first.IpAddress == "203.0.113.10"
                && first.Port == 17000
                && first.MapName == "Shoreline"
                && first.GameMode == "Online",
                "rich EFT scanner parses endpoint, map, and meaningful raid mode");
            Assert(first != null
                && first.ClientVersion == "1.1.0.1.46699"
                && first.DataCenterCode == "SG-SIN01"
                && first.MatchmakingSeconds.HasValue
                && Math.Abs(first.MatchmakingSeconds.Value - 35.5) < 0.001,
                "rich EFT scanner parses version, data center, and matching duration");
            Assert(first != null
                && first.ConnectionAttempts == 1
                && first.ReconnectCount == 0
                && first.ConnectedOnce
                && first.HasDisconnectRecord
                && first.DisconnectReason == 0,
                "the first EFT connection is not counted as a reconnect and normal exit is retained");
            Assert(first != null
                && first.ActualRttMs.HasValue
                && Math.Abs(first.ActualRttMs.Value - 88.25) < 0.001
                && first.NetworkSent == 1200
                && first.NetworkReceived == 1188,
                "rich EFT scanner attaches network RTT and counters to the correct raid");
            Assert(second != null
                && second.ConnectionAttempts == 2
                && second.ReconnectCount == 1
                && !second.HasDisconnectRecord
                && !second.DisconnectReason.HasValue,
                "rich EFT scanner counts reconnects and uses the final connection segment state");
            Assert(second != null
                && second.ActualRttMs.HasValue
                && Math.Abs(second.ActualRttMs.Value - 155.5) < 0.001,
                "rich EFT scanner records the latest actual RTT after reconnect");
        }

        private static void TestEftRaidTypesAndUserReports(string tempRoot)
        {
            string logs = Path.Combine(tempRoot, "RaidTypeAndReportLogs");
            string folder = Path.Combine(logs, "log_2026.08.15_07-50-00");
            Directory.CreateDirectory(folder);

            string application = string.Join("\r\n", new[]
            {
                // Mirrors the latest real Regular + Online server sequence.
                "2026.08.15 07:55:00.000|1.1.0.1.46777|Info|application|Session mode: Regular",
                "2026.08.15 08:00:00.000|1.1.0.1.46777|Info|application|MatchingCompleted:30 real:34 diff:4",
                "2026.08.15 08:00:05.000|1.1.0.1.46777|Debug|application|TRACE-NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.101, Port: 17101, Location: factory4_day, Sid: SG-SIN01G001_pvp, GameMode: deathmatch, shortId: MODE001'",
                "2026.08.15 08:40:00.000|1.1.0.1.46777|Info|application|Session mode: PvpSeason",
                "2026.08.15 08:41:00.000|1.1.0.1.46777|Info|application|MatchingCompleted:20 real:24 diff:4",
                "2026.08.15 08:41:05.000|1.1.0.1.46777|Debug|application|TRACE-NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.102, Port: 17102, Location: Woods, Sid: JP-TK02G005_season, GameMode: deathmatch, shortId: MODE002'",
                "2026.08.15 09:20:00.000|1.1.0.1.46777|Info|application|Session mode: Pve",
                "2026.08.15 09:21:00.000|1.1.0.1.46777|Info|application|MatchingCompleted:40 real:44 diff:4",
                "2026.08.15 09:21:05.000|1.1.0.1.46777|Debug|application|TRACE-NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.103, Port: 17103, Location: Shoreline, Sid: KR-SEL01G001_pve-server, GameMode: deathmatch, shortId: MODE003'",
                "2026.08.15 10:00:00.000|1.1.0.1.46777|Info|application|Session mode: Pve",
                "2026.08.15 10:00:10.000|1.1.0.1.46777|Info|application|MatchingCompleted:0 real:0 diff:0",
                "2026.08.15 10:00:10.500|1.1.0.1.46777|Info|application|scene preset path:maps/factory_day_preset.bundle rcid:factory_day.scenespreset.asset",
                "2026.08.15 10:00:25.000|1.1.0.1.46777|Info|application|GameCreated:12.45 real:16.45 diff:4",
                "2026.08.15 10:00:50.000|1.1.0.1.46777|Info|application|GameStarted:32.7 real:41.72 diff:9.02",
                // A truncated local-looking sequence must not become a local raid.
                "2026.08.15 11:00:00.000|1.1.0.1.46777|Info|application|Session mode: Pve",
                "2026.08.15 11:00:10.000|1.1.0.1.46777|Info|application|MatchingCompleted:0 real:0 diff:0",
                "2026.08.15 11:00:10.500|1.1.0.1.46777|Info|application|scene preset path:maps/woods_preset.bundle",
                "2026.08.15 11:00:25.000|1.1.0.1.46777|Info|application|GameCreated:12 real:16 diff:4",
                // Regular is never inferred as a local progression raid from missing IP data.
                "2026.08.15 12:00:00.000|1.1.0.1.46777|Info|application|Session mode: Regular",
                "2026.08.15 12:00:10.000|1.1.0.1.46777|Info|application|MatchingCompleted:0 real:0 diff:0",
                "2026.08.15 12:00:10.500|1.1.0.1.46777|Info|application|scene preset path:maps/woods_preset.bundle",
                "2026.08.15 12:00:25.000|1.1.0.1.46777|Info|application|GameCreated:12 real:16 diff:4",
                "2026.08.15 12:00:50.000|1.1.0.1.46777|Info|application|GameStarted:32 real:41 diff:9",
                // A server assignment suppresses a simultaneous local-looking evidence chain.
                "2026.08.15 13:00:00.000|1.1.0.1.46777|Info|application|Session mode: Pve",
                "2026.08.15 13:00:10.000|1.1.0.1.46777|Info|application|MatchingCompleted:0 real:0 diff:0",
                "2026.08.15 13:00:10.500|1.1.0.1.46777|Info|application|scene preset path:maps/factory_day_preset.bundle",
                "2026.08.15 13:00:11.000|1.1.0.1.46777|Debug|application|TRACE-NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.104, Port: 17104, Location: factory4_day, Sid: KR-SEL01G001_pve-server-zero, GameMode: deathmatch, shortId: MODE004'",
                "2026.08.15 13:00:25.000|1.1.0.1.46777|Info|application|GameCreated:12 real:16 diff:4",
                "2026.08.15 13:00:50.000|1.1.0.1.46777|Info|application|GameStarted:32 real:41 diff:9",
                // Network evidence also prevents an IP-missing chain from being called local.
                "2026.08.15 14:00:00.000|1.1.0.1.46777|Info|application|Session mode: Pve",
                "2026.08.15 14:00:10.000|1.1.0.1.46777|Info|application|MatchingCompleted:0 real:0 diff:0",
                "2026.08.15 14:00:10.500|1.1.0.1.46777|Info|application|scene preset path:maps/factory_day_preset.bundle",
                "2026.08.15 14:00:25.000|1.1.0.1.46777|Info|application|GameCreated:12 real:16 diff:4",
                "2026.08.15 14:00:50.000|1.1.0.1.46777|Info|application|GameStarted:32 real:41 diff:9",
                // Application/output copies of the route are deliberately ignored.
                "2026.08.15 14:10:00.000|1.1.0.1.46777|Info|application|URL: https://lobby.test/client/report/send?",
                string.Empty
            });
            File.WriteAllText(Path.Combine(folder, "application_000.log"), application, Encoding.UTF8);

            File.WriteAllText(
                Path.Combine(folder, "network-connection_000.log"),
                "2026.08.15 14:00:11.000|1.1.0.1.46777|Info|network-connection|Connect (address: 203.0.113.200:17200)\r\n",
                Encoding.UTF8);

            string backend = string.Join("\r\n", new[]
            {
                "2026.08.15 08:20:00.000|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [report-1]: URL: https://lobby.test/client/report/send?",
                "2026.08.15 08:20:00.100|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [report-1]: URL: https://lobby.test/client/report/send?",
                "2026.08.15 08:20:00.300|1.1.0.1.46777|Info|backend|<--- Response HTTPS, id [report-1]: URL: https://lobby.test/client/report/send, DownloadSeconds: 0.3, responseText:",
                "2026.08.15 08:50:00.000|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [report-failed]: URL: https://lobby.test/client/report/send?",
                "2026.08.15 08:50:00.300|1.1.0.1.46777|Error|backend|<--- Response HTTPS, id [report-failed]: Exception occured: 239, URL: https://lobby.test/client/report/send?",
                "2026.08.15 08:50:45.000|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [report-failed]: URL: https://lobby.test/client/report/send?",
                "2026.08.15 08:50:45.300|1.1.0.1.46777|Error|backend|<--- Response HTTPS, id [report-failed]: BackendServerSideException, URL: https://lobby.test/client/report/send?",
                "2026.08.15 09:30:00.000|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [report-2]: URL: https://lobby.test/client/report/send.",
                "2026.08.15 09:30:02.000|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [report-2]: URL: https://lobby.test/client/report/send.",
                "2026.08.15 09:30:03.000|1.1.0.1.46777|Info|backend|<--- Response HTTPS, id [report-2]: URL: https://lobby.test/client/report/send, DownloadSeconds: 1, responseText:",
                "2026.08.15 10:20:00.000|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [report-local]: URL: https://lobby.test/client/report/send?",
                "2026.08.15 10:20:00.500|1.1.0.1.46777|Info|backend|<--- Response HTTPS, id [report-local]: URL: https://lobby.test/client/report/send, DownloadSeconds: 0.5, responseText:",
                "2026.08.15 13:20:00.000|1.1.0.1.46777|Info|backend|---> Request HTTPS, id [wrong-route]: URL: https://lobby.test/client/report/send-extra",
                "2026.08.15 13:20:00.200|1.1.0.1.46777|Info|backend|<--- Response HTTPS, id [wrong-route]: URL: https://lobby.test/client/report/send-extra",
                "2026.08.15 13:30:00.000|1.1.0.1.46777|Info|backend|<--- Response HTTPS, id [response-only]: URL: https://lobby.test/client/report/send, DownloadSeconds: 0.2, responseText:",
                string.Empty
            });
            File.WriteAllText(Path.Combine(folder, "backend_000.log"), backend, Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(folder, "output_000.log"),
                backend,
                Encoding.UTF8);

            RaidLogScanResult result = RaidLogScanner.Scan(
                new TarkovLogPaths { EftPath = logs },
                100);
            Assert(result.Sessions.Count == 5,
                "EFT raid-type parser keeps four server raids and one positively confirmed local raid");

            ServerSession pvp = result.Sessions.Single(item => item.ShortId == "MODE001");
            ServerSession season = result.Sessions.Single(item => item.ShortId == "MODE002");
            ServerSession pveServer = result.Sessions.Single(item => item.ShortId == "MODE003");
            ServerSession zeroPveServer = result.Sessions.Single(item => item.ShortId == "MODE004");
            ServerSession pveLocal = result.Sessions.Single(item => item.HostingMode == TarkovHostingMode.Local);

            Assert(pvp.ProgressionMode == TarkovProgressionMode.Pvp
                && pvp.HostingMode == TarkovHostingMode.Server
                && pvp.RaidPurpose == TarkovRaidPurpose.Progression
                && pvp.ProgressionModeText == "PvP"
                && pvp.RaidTypeText == "PvP",
                "the current Regular + Online sample is classified as PvP server progression");
            Assert(season.ProgressionMode == TarkovProgressionMode.PvpSeason
                && season.HostingMode == TarkovHostingMode.Server
                && season.ProgressionModeText == "PvP시즌"
                && season.RaidTypeText == "PvP시즌",
                "PvpSeason is classified independently from regular PvP");
            Assert(pveServer.ProgressionMode == TarkovProgressionMode.Pve
                && pveServer.HostingMode == TarkovHostingMode.Server
                && pveServer.ProgressionModeText == "PvE(서버)"
                && pveServer.RaidTypeText == "PvE(서버)",
                "Pve with an explicit server assignment is classified as PvE server");
            Assert(pveLocal.ProgressionMode == TarkovProgressionMode.Pve
                && !pveLocal.HasServerIp
                && pveLocal.MapName == "Factory"
                && pveLocal.ProgressionModeText == "PvE(로컬)"
                && pveLocal.RaidTypeText == "PvE(로컬)"
                && pveLocal.ConnectionStateText == "해당 없음",
                "zero matching plus map, GameCreated, and GameStarted confirms the real local PvE flow");
            Assert(zeroPveServer.RaidTypeText == "PvE(서버)",
                "an explicit server assignment wins over simultaneous local-looking milestones");
            Assert(!result.Sessions.Any(item => item.DisplayDetectedAt.Hour == 11
                    || item.DisplayDetectedAt.Hour == 12
                    || item.DisplayDetectedAt.Hour == 14),
                "truncated, non-PvE, and network-backed IP-missing sequences are not called local");
            Assert(new ServerSession().RaidPurpose == TarkovRaidPurpose.Unknown
                && new ServerSession().RaidTypeText == "미확인",
                "practice and progression are not invented without explicit mode evidence");

            Assert(pvp.UserReportCount == 1,
                "a successful report is attached once despite duplicate request lines");
            Assert(season.UserReportCount == 0,
                "failed and retried report requests are not exposed as successful reports");
            Assert(pveServer.UserReportCount == 1,
                "a later successful report is attached to the most recent corresponding server raid");
            Assert(pveLocal.UserReportCount == 1,
                "a successful report after a confirmed local raid is attached to that raid");
            Assert(zeroPveServer.UserReportCount == 0,
                "wrong routes and response-only records do not create user-report counts");
        }

        private static void TestNetworkEventBoundariesAndDuplicateMerge(string tempRoot)
        {
            string boundaryLogs = Path.Combine(tempRoot, "BoundaryLogs");
            string boundaryFolder = Path.Combine(boundaryLogs, "log_boundary");
            Directory.CreateDirectory(boundaryFolder);
            File.WriteAllText(
                Path.Combine(boundaryFolder, "application.log"),
                "2026.08.14 09:00:00.100|1.1.0.1.46699|Trace|NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.90, Port: 17000, Location: Shoreline, Sid: SG-SIN01G001_boundary-one, GameMode: deathmatch, shortId: BND001'\r\n"
                + "2026.08.14 09:00:04.000|1.1.0.1.46699|Trace|NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 203.0.113.90, Port: 17000, Location: Shoreline, Sid: SG-SIN01G001_boundary-two, GameMode: deathmatch, shortId: BND002'\r\n",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(boundaryFolder, "network-connection.log"),
                "2026.08.14 09:00:00.200|Connect (address: 203.0.113.90:17000)\r\n"
                + "2026.08.14 09:00:02.000|Enter to the 'Disconnected' state (address: 203.0.113.90:17000, reason: 0)\r\n"
                + "2026.08.14 09:00:03.900|Connect (address: 203.0.113.90:17000)\r\n"
                + "2026.08.14 09:00:04.100|Enter to the 'Connected' state (address: 203.0.113.90:17000)\r\n",
                Encoding.UTF8);

            RaidLogScanResult boundary = RaidLogScanner.Scan(
                new TarkovLogPaths { EftPath = boundaryLogs },
                100);
            ServerSession first = boundary.Sessions.Single(item => item.ShortId == "BND001");
            ServerSession second = boundary.Sessions.Single(item => item.ShortId == "BND002");
            Assert(first.HasDisconnectRecord && first.DisconnectReason == 0,
                "terminal events before a new assignment stay with the earlier raid");
            Assert(second.ConnectionAttempts == 1 && second.ConnectedOnce && !second.HasDisconnectRecord,
                "a connect just before its assignment can attach without stealing earlier terminal events");

            string duplicateLogs = Path.Combine(tempRoot, "DuplicateLogs");
            foreach (string folderName in new[] { "log_duplicate_a", "log_duplicate_b" })
            {
                string folder = Path.Combine(duplicateLogs, folderName);
                Directory.CreateDirectory(folder);
                File.WriteAllText(
                    Path.Combine(folder, "application.log"),
                    "2026.08.14 15:00:00.100|1.1.0.1.46699|Trace|NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 198.51.100.90, Port: 17000, Location: Woods, Sid: JP-TK02G005_same-raid, GameMode: deathmatch, shortId: DUP001'\r\n",
                    Encoding.UTF8);
                File.WriteAllText(
                    Path.Combine(folder, "network-connection.log"),
                    "2026.08.14 15:00:00.250|Connect (address: 198.51.100.90:17000)\r\n",
                    Encoding.UTF8);
            }

            RaidLogScanResult duplicate = RaidLogScanner.Scan(
                new TarkovLogPaths { EftPath = duplicateLogs },
                100);
            Assert(duplicate.Sessions.Count == 1 && duplicate.Sessions[0].ConnectionAttempts == 1,
                "global SID merge does not double-count an identical copied connect event");
        }

        private static void TestArenaRaidScanning(string tempRoot)
        {
            string logs = Path.Combine(tempRoot, "ArenaLogs");
            string folder = Path.Combine(logs, "log_2026.08.14_12-00-00");
            Directory.CreateDirectory(folder);

            File.WriteAllText(
                Path.Combine(folder, "2026.08.14 application.log"),
                "2026.08.14 12:00:05|1.1.0.1.46699|Info|MatchingCompleted:22.25\r\n",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(folder, "2026.08.14 lifecycle.log"),
                "2026.08.14 12:00:15|1.1.0.1.46699|Info|Server found. ip: 192.0.2.88 port: 17013 sid: JP-TK02G005_arena-raid shortId: 92HY2N map: Arena_Bay5 mode: CheckPoint\r\n",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(folder, "2026.08.14 network-connection.log"),
                "2026.08.14 12:00:16|Connect (address: 192.0.2.88:17013)\r\n"
                + "2026.08.14 12:00:17|Enter to the 'Connected' state (address: 192.0.2.88:17013)\r\n"
                + "2026.08.14 12:05:00|Statistics (address: 192.0.2.88:17013, rtt: 47.75, lose: 0, sent: 400, received: 400)\r\n"
                + "2026.08.14 12:10:00|Enter to the 'Disconnected' state (address: 192.0.2.88:17013, reason: 0)\r\n",
                Encoding.UTF8);

            RaidLogScanResult result = RaidLogScanner.Scan(
                new TarkovLogPaths { ArenaPath = logs },
                100);
            Assert(result.ArenaFoldersScanned == 1, "Arena scanner visits lifecycle log folders");
            Assert(result.Sessions.Count == 1, "Arena scanner returns a lifecycle server session");

            ServerSession session = result.Sessions.SingleOrDefault();
            Assert(session != null
                && session.Game == TarkovGame.Arena
                && session.IpAddress == "192.0.2.88"
                && session.Port == 17013,
                "Arena scanner parses the game and endpoint");
            Assert(session != null
                && session.MapName == "Bay5"
                && session.GameMode == "CheckPoint"
                && session.ServerId == "JP-TK02G005_arena-raid"
                && session.ShortId == "92HY2N",
                "Arena scanner parses map, mode, and server identifiers");
            Assert(session != null
                && session.ProgressionMode == TarkovProgressionMode.Unknown
                && session.RaidTypeText == "미확인",
                "EFT progression labels do not alter Arena sessions");
            Assert(session != null
                && session.DataCenterCode == "JP-TK02"
                && session.ClientVersion == "1.1.0.1.46699"
                && session.MatchmakingSeconds.HasValue
                && Math.Abs(session.MatchmakingSeconds.Value - 22.25) < 0.001,
                "Arena scanner joins data center, version, and matching duration");
            Assert(session != null
                && session.ConnectionAttempts == 1
                && session.ReconnectCount == 0
                && session.ActualRttMs.HasValue
                && Math.Abs(session.ActualRttMs.Value - 47.75) < 0.001
                && session.ConnectionResultText == "정상종료",
                "Arena scanner joins network RTT and normal connection completion");
        }

        private static void TestRaidLogQueryFiltering(string tempRoot)
        {
            string eftLogs = Path.Combine(tempRoot, "QueryEftLogs");
            string arenaLogs = Path.Combine(tempRoot, "QueryArenaLogs");
            WriteQueryEftRaid(
                eftLogs,
                "log_2026.08.10_before",
                new DateTime(2026, 8, 10, 9, 59, 59),
                "203.0.113.210",
                "JP-TK02G005_query-before",
                "QRY000");
            string misleadingFolder = WriteQueryEftRaid(
                eftLogs,
                "log_2000.01.01_misleading-folder-date",
                new DateTime(2026, 8, 10, 10, 0, 0),
                "203.0.113.211",
                "JP-TK02G005_query-start",
                "QRY001");
            WriteQueryEftRaid(
                eftLogs,
                "log_2026.08.10_inside",
                new DateTime(2026, 8, 10, 12, 0, 0),
                "203.0.113.212",
                "JP-TK02G005_query-inside",
                "QRY002");
            WriteQueryEftRaid(
                eftLogs,
                "log_2026.08.10_inside-copy",
                new DateTime(2026, 8, 10, 12, 0, 0),
                "203.0.113.212",
                "JP-TK02G005_query-inside",
                "QRY002");
            WriteQueryEftRaid(
                eftLogs,
                "log_2026.08.10_latest",
                new DateTime(2026, 8, 10, 23, 59, 59),
                "203.0.113.213",
                "JP-TK02G005_query-latest",
                "QRY003");
            WriteQueryEftRaid(
                eftLogs,
                "log_2026.08.11_end",
                new DateTime(2026, 8, 11, 0, 0, 0),
                "203.0.113.214",
                "JP-TK02G005_query-end",
                "QRY004");

            string arenaFolder = Path.Combine(arenaLogs, "log_2026.08.10_arena");
            Directory.CreateDirectory(arenaFolder);
            File.WriteAllText(
                Path.Combine(arenaFolder, "2026.08.10 lifecycle.log"),
                "2026.08.10 18:00:00|1.1.0.1.46777|Info|lifecycle|Server found. ip: 192.0.2.210 port: 17210 sid: JP-TK02G005_query-arena shortId: QRA001 map: Arena_Bay5 mode: CheckPoint\r\n",
                Encoding.UTF8);

            DateTime misleadingTimestamp = new DateTime(2000, 1, 1, 0, 0, 0);
            Directory.SetCreationTime(misleadingFolder, misleadingTimestamp);
            Directory.SetLastWriteTime(misleadingFolder, misleadingTimestamp);

            var paths = new TarkovLogPaths
            {
                EftPath = eftLogs,
                ArenaPath = arenaLogs
            };
            DateTime start = new DateTime(2026, 8, 10, 10, 0, 0);
            DateTime end = new DateTime(2026, 8, 11, 0, 0, 0);
            RaidLogScanResult allGames = RaidLogScanner.Scan(
                paths,
                new RaidLogScanQuery
                {
                    MaximumRecords = 3,
                    StartInclusive = start,
                    EndExclusive = end
                });

            Assert(allGames.TotalMatchingSessions == 4
                && allGames.TotalMatchingSessionsIsExact
                && allGames.HasMoreSessions
                && allGames.Sessions.Count == 3,
                "range query reports the exact deduplicated total before applying its row cap");
            Assert(allGames.Sessions[0].ShortId == "QRY003"
                && allGames.Sessions[1].ShortId == "QRA001"
                && allGames.Sessions[2].ShortId == "QRY002",
                "range query caps only after date filtering and newest-first sorting");

            RaidLogScanResult allRows = RaidLogScanner.Scan(
                paths,
                new RaidLogScanQuery
                {
                    MaximumRecords = 10,
                    StartInclusive = start,
                    EndExclusive = end
                });
            Assert(allRows.Sessions.Any(item => item.ShortId == "QRY001")
                && !allRows.Sessions.Any(item => item.ShortId == "QRY000")
                && !allRows.Sessions.Any(item => item.ShortId == "QRY004"),
                "range query includes the start boundary and excludes both pre-start and end-boundary raids");
            Assert(allRows.Sessions.Count(item => item.ShortId == "QRY002") == 1
                && !allRows.HasMoreSessions,
                "range query deduplicates copied raids before counting and limiting results");
            Assert(allRows.Sessions.Any(item => item.ShortId == "QRY001")
                && allRows.EftFoldersScanned == 6,
                "folder dates are never final inclusion criteria for an exact range query");

            RaidLogScanResult eftOnly = RaidLogScanner.Scan(
                paths,
                new RaidLogScanQuery
                {
                    MaximumRecords = 2,
                    StartInclusive = start,
                    EndExclusive = end,
                    GameFilter = TarkovGame.Eft
                });
            Assert(eftOnly.TotalMatchingSessions == 3
                && eftOnly.Sessions.Count == 2
                && eftOnly.Sessions.All(item => item.Game == TarkovGame.Eft)
                && eftOnly.ArenaFoldersScanned == 0,
                "EFT filter is applied before the row cap and skips Arena folder scans");

            RaidLogScanResult arenaOnly = RaidLogScanner.Scan(
                paths,
                new RaidLogScanQuery
                {
                    MaximumRecords = 10,
                    StartInclusive = start,
                    EndExclusive = end,
                    GameFilter = TarkovGame.Arena
                });
            Assert(arenaOnly.TotalMatchingSessions == 1
                && arenaOnly.Sessions.Count == 1
                && arenaOnly.Sessions[0].ShortId == "QRA001"
                && arenaOnly.EftFoldersScanned == 0,
                "Arena filter is applied before the row cap and skips EFT folder scans");

            RaidLogScanResult legacy = RaidLogScanner.Scan(paths, 2);
            Assert(legacy.EftFoldersScanned == 2
                && legacy.ArenaFoldersScanned == 1
                && legacy.Sessions.Count <= 2
                && !legacy.TotalMatchingSessionsIsExact,
                "legacy Scan(paths, maximumRecords) retains its per-game folder cap and row cap");

            bool reversedRangeRejected = false;
            try
            {
                RaidLogScanner.Scan(
                    paths,
                    new RaidLogScanQuery
                    {
                        StartInclusive = end,
                        EndExclusive = start
                    });
            }
            catch (ArgumentException)
            {
                reversedRangeRejected = true;
            }
            Assert(reversedRangeRejected, "range query rejects reversed boundaries");

            bool unknownGameRejected = false;
            try
            {
                RaidLogScanner.Scan(
                    paths,
                    new RaidLogScanQuery { GameFilter = TarkovGame.Unknown });
            }
            catch (ArgumentOutOfRangeException)
            {
                unknownGameRejected = true;
            }
            Assert(unknownGameRejected, "range query rejects an unknown game filter");

            string bulkLogs = Path.Combine(tempRoot, "QueryBulkLogs");
            string bulkFolder = Path.Combine(bulkLogs, "log_2026.08.12_bulk");
            Directory.CreateDirectory(bulkFolder);
            var bulkApplication = new StringBuilder();
            DateTime bulkStart = new DateTime(2026, 8, 12, 1, 0, 0);
            for (int index = 0; index < 105; index++)
            {
                bulkApplication.Append(bulkStart.AddMinutes(index).ToString("yyyy.MM.dd HH:mm:ss"));
                bulkApplication.Append("|1.1.0.1.46777|Debug|application|TRACE-NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: 198.51.100.");
                bulkApplication.Append(index + 1);
                bulkApplication.Append(", Port: 17300, Location: Woods, Sid: JP-TK02G005_query-bulk-");
                bulkApplication.Append(index.ToString("D3"));
                bulkApplication.Append(", GameMode: deathmatch, shortId: QB");
                bulkApplication.Append(index.ToString("D3"));
                bulkApplication.Append("'\r\n");
            }
            File.WriteAllText(
                Path.Combine(bulkFolder, "application.log"),
                bulkApplication.ToString(),
                Encoding.UTF8);
            RaidLogScanResult overOneHundred = RaidLogScanner.Scan(
                new TarkovLogPaths { EftPath = bulkLogs },
                new RaidLogScanQuery
                {
                    MaximumRecords = 100,
                    StartInclusive = new DateTime(2026, 8, 12),
                    EndExclusive = new DateTime(2026, 8, 13),
                    GameFilter = TarkovGame.Eft
                });
            Assert(overOneHundred.Sessions.Count == 100
                && overOneHundred.TotalMatchingSessions == 105
                && overOneHundred.TotalMatchingSessionsIsExact
                && overOneHundred.HasMoreSessions,
                "range query exposes an exact total above the 100-row display limit");
        }

        private static string WriteQueryEftRaid(
            string logsRoot,
            string folderName,
            DateTime detectedAt,
            string ipAddress,
            string serverId,
            string shortId)
        {
            string folder = Path.Combine(logsRoot, folderName);
            Directory.CreateDirectory(folder);
            string line = detectedAt.ToString("yyyy.MM.dd HH:mm:ss")
                + "|1.1.0.1.46777|Debug|application|TRACE-NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: "
                + ipAddress
                + ", Port: 17200, Location: Woods, Sid: "
                + serverId
                + ", GameMode: deathmatch, shortId: "
                + shortId
                + "'\r\n";
            File.WriteAllText(Path.Combine(folder, "application.log"), line, Encoding.UTF8);
            return folder;
        }

        private static void TestLauncherSelectionReading(string tempRoot)
        {
            string logs = Path.Combine(tempRoot, "LauncherLogs");
            Directory.CreateDirectory(logs);
            string launcherLog = string.Join("\r\n", new[]
            {
                "2026.08.14 13:00:00.000 +09:00 [INFO] Starting launcher v.1.0.0",
                "2026.08.14 13:00:00.050 +09:00 [INFO] Settings loaded: {\"games\":[],\"selectedGame\":\"eft\",\"login\":\"not-inspected\"}",
                "2026.08.14 13:00:01.200 INFO JS->.NET: MainWindow.ShowMatchingConfig()",
                "2026.08.14 13:00:02.300 INFO JS->.NET: MatchingConfigurationWindow.Apply(\"{\\\"dataCenters\\\":[\\\"Asia North East\\\",\\\"Singapore\\\"]}\")",
                "2026.08.14 13:05:00.100 INFO JS->.NET: MainWindow.SelectGame(\"arena\")",
                "2026.08.14 13:05:01.100 INFO JS->.NET: MainWindow.ShowMatchingConfig()",
                "2026.08.14 13:05:02.300 INFO JS->.NET: MatchingConfigurationWindow.Apply(\"{\\\"dataCenters\\\":[\\\"China\\\"]}\")",
                "2026.08.14 13:10:00.000 +09:00 [INFO] Starting launcher v.1.0.0",
                "2026.08.14 13:10:00.050 +09:00 [INFO] Settings loaded: {\"games\":[],\"selectedGame\":\"eft\",\"at\":\"not-inspected\"}",
                "2026.08.14 13:10:01.200 INFO JS->.NET: MainWindow.ShowMatchingConfig()",
                "2026.08.14 13:10:02.300 INFO JS->.NET: MatchingConfigurationWindow.Apply(\"{\\\"dataCenters\\\":[] }\")",
                "2026.08.14 13:20:00.000 +09:00 [INFO] Settings loaded: {\"selectedGame\":\"eft\",\"nested\":{\"selectedGame\":\"arena\"}}",
                "2026.08.14 13:20:01.200 INFO JS->.NET: MainWindow.ShowMatchingConfig()",
                "2026.08.14 13:20:02.300 INFO JS->.NET: MatchingConfigurationWindow.Apply(\"{\\\"dataCenters\\\":[\\\"Malaysia\\\"]}\")",
                string.Empty
            });
            File.WriteAllText(
                Path.Combine(logs, "BSG_Launcher_2026.08.14_13-00-00.log"),
                launcherLog,
                Encoding.UTF8);

            LauncherSelectionInfo info = LauncherSelectionReader.ReadFromDirectory(logs);
            Assert(info.EftSelection == "자동 선택",
                "launcher reader restores EFT from Settings loaded after a restart and preserves empty dataCenters as automatic selection");
            Assert(info.EftUpdatedAt == new DateTime(2026, 8, 14, 13, 10, 2),
                "ambiguous Settings loaded content clears context instead of misattributing a later Apply");
            Assert(info.GetDisplay(TarkovGame.Arena) == "선택 기록 없음",
                "launcher reader never treats an Arena launcher Apply as the Arena source of truth");

            string failedApplyLogs = Path.Combine(tempRoot, "LauncherFailedApplyLogs");
            Directory.CreateDirectory(failedApplyLogs);
            File.WriteAllText(
                Path.Combine(failedApplyLogs, "BSG_Launcher_20260814.log"),
                string.Join("\r\n", new[]
                {
                    "2026.08.14 14:00:00.050 +09:00 [INFO] Settings loaded: {\"selectedGame\":\"eft\"}",
                    "2026.08.14 14:00:01.200 INFO JS->.NET: MainWindow.ShowMatchingConfig()",
                    "2026.08.14 14:00:02.300 INFO JS->.NET: MatchingConfigurationWindow.Apply(\"{\\\"dataCenters\\\":[\\\"Korea\\\"]}\")",
                    "2026.08.14 14:01:01.200 INFO JS->.NET: MainWindow.ShowMatchingConfig()",
                    "2026.08.14 14:01:02.300 INFO JS->.NET: MatchingConfigurationWindow.Apply(\"{\\\"dataCenters\\\":[\\\"China\\\"]}\")",
                    "2026.08.14 14:01:02.600 ERROR .NET->JS: ErrorWindow initialized",
                    string.Empty
                }),
                Encoding.UTF8);
            LauncherSelectionInfo failedApply = LauncherSelectionReader.ReadFromDirectory(failedApplyLogs);
            Assert(failedApply.EftSelection == "Korea"
                && failedApply.EftUpdatedAt == new DateTime(2026, 8, 14, 14, 0, 2),
                "launcher reader keeps the previous EFT selection when Apply is followed by an error");

            string legacyLogs = Path.Combine(tempRoot, "LegacyLauncherLogs");
            Directory.CreateDirectory(legacyLogs);
            File.WriteAllText(
                Path.Combine(legacyLogs, "BSG_Launcher_legacy.log"),
                string.Join("\r\n", new[]
                {
                    "2026.08.14 15:00:00.100 INFO JS->.NET: MainWindow.SelectGame(\"eft\")",
                    "2026.08.14 15:00:01.200 INFO JS->.NET: MainWindow.ShowMatchingConfig()",
                    "2026.08.14 15:00:02.300 INFO JS->.NET: MatchingConfigurationWindow.Apply(\"{\\\"dataCenters\\\":[\\\"Korea\\\",\\\"Japan\\\"]}\")",
                    string.Empty
                }),
                Encoding.UTF8);
            Assert(LauncherSelectionReader.ReadFromDirectory(legacyLogs).EftSelection == "Korea, Japan",
                "legacy launcher logs without Settings loaded still work when an explicit SelectGame event exists");

            string arenaSettings = Path.Combine(tempRoot, "ArenaSettings", "Regions.ini");
            Directory.CreateDirectory(Path.GetDirectoryName(arenaSettings));
            File.WriteAllText(
                arenaSettings,
                "{\"Version\":4,\"RegionSettings\":["
                + "{\"DataCenter\":\"Japan\",\"State\":true},"
                + "{\"DataCenter\":\"China\",\"State\":false},"
                + "{\"DataCenter\":\"Sa   Colombia\",\"State\":false}],"
                + "\"AutoSelectRegionsEnabled\":false}",
                new UTF8Encoding(false));
            DateTime arenaWriteTime = new DateTime(2026, 8, 15, 9, 12, 0);
            File.SetLastWriteTime(arenaSettings, arenaWriteTime);
            LauncherSelectionInfo arenaManual = LauncherSelectionReader.ReadFromSources(logs, arenaSettings);
            Assert(arenaManual.ArenaSelection == "Japan",
                "Arena selection comes from the current Regions.ini DataCenter and State values");
            Assert(arenaManual.ArenaUpdatedAt.HasValue
                && arenaManual.ArenaUpdatedAt.Value == arenaWriteTime,
                "Arena selection uses the Regions.ini last-write time");

            File.WriteAllText(
                arenaSettings,
                "{\"Version\":4,\"RegionSettings\":[{\"DataCenter\":\"Japan\",\"State\":false}],"
                + "\"AutoSelectRegionsEnabled\":true}",
                new UTF8Encoding(false));
            Assert(LauncherSelectionReader.ReadFromSources(logs, arenaSettings).ArenaSelection == "자동 선택",
                "Arena automatic selection overrides individual region states");

            File.WriteAllText(
                arenaSettings,
                "{\"Version\":3,\"RegionSettings\":["
                + "{\"DataCenter\":\"Korea\",\"State\":true},"
                + "{\"DataCenter\":\"Japan\",\"State\":true}]}",
                new UTF8Encoding(false));
            Assert(LauncherSelectionReader.ReadFromSources(logs, arenaSettings).ArenaSelection == "Korea, Japan",
                "legacy Arena settings without AutoSelectRegionsEnabled retain their enabled regions");

            File.WriteAllText(
                arenaSettings,
                "{\"Version\":4,\"RegionSettings\":[{\"DataCenter\":\"China\",\"State\":true}",
                new UTF8Encoding(false));
            LauncherSelectionInfo partialArena = LauncherSelectionReader.ReadFromSources(logs, arenaSettings);
            Assert(partialArena.EftSelection == "자동 선택"
                && partialArena.GetDisplay(TarkovGame.Arena) == "선택 기록 없음"
                && !partialArena.ArenaUpdatedAt.HasValue,
                "a partially written Arena file is ignored without losing the independent EFT result");

            File.WriteAllText(
                arenaSettings,
                "{\"RegionSettings\":[{\"DataCenter\":\"Japan\",\"State\":\"true\"}],"
                + "\"AutoSelectRegionsEnabled\":false}",
                new UTF8Encoding(false));
            Assert(LauncherSelectionReader.ReadFromSources(logs, arenaSettings).ArenaSelection == null,
                "corrupt Arena field types are rejected instead of being guessed");

            File.Delete(arenaSettings);
            Assert(LauncherSelectionReader.ReadFromSources(logs, arenaSettings).GetDisplay(TarkovGame.Arena) == "선택 기록 없음",
                "a missing Arena settings file has the legacy no-record behavior and no launcher fallback");
        }

        private static void TestPathNormalization(string tempRoot)
        {
            string gameRoot = Path.Combine(tempRoot, "Game");
            string logs = Path.Combine(gameRoot, "Logs");
            Directory.CreateDirectory(logs);
            string normalized = LogPathFinder.NormalizeSelectedFolder(gameRoot);
            Assert(string.Equals(normalized, logs, StringComparison.OrdinalIgnoreCase), "game root normalizes to child Logs folder");
        }

        private static void TestSteamPathDiscovery(string tempRoot)
        {
            string steamRoot = Path.Combine(tempRoot, "Steam");
            string extraLibrary = Path.Combine(tempRoot, "게임 라이브러리");
            string steamApps = Path.Combine(steamRoot, "steamapps");
            string extraSteamApps = Path.Combine(extraLibrary, "steamapps");
            Directory.CreateDirectory(steamApps);
            Directory.CreateDirectory(Path.Combine(extraSteamApps, "common"));

            string libraryVdf = "\"libraryfolders\"\r\n{\r\n"
                + "  \"0\" { \"path\" \"" + EscapeVdfPath(steamRoot) + "\" }\r\n"
                + "  \"1\" { \"path\" \"" + EscapeVdfPath(extraLibrary) + "\" }\r\n"
                + "  \"2\" { \"path\" \"..\\\\relative-is-rejected\" }\r\n"
                + "}\r\n";
            File.WriteAllText(Path.Combine(steamApps, "libraryfolders.vdf"), libraryVdf, Encoding.UTF8);

            string eftRoot = Path.Combine(extraSteamApps, "common", "Escape from Tarkov");
            string eftLogs = Path.Combine(eftRoot, "Logs");
            string eftSession = Path.Combine(eftLogs, "log_steam_eft");
            Directory.CreateDirectory(eftSession);
            File.WriteAllText(
                Path.Combine(extraSteamApps, "appmanifest_3932890.acf"),
                "\"AppState\" { \"appid\" \"3932890\" \"name\" \"Escape from Tarkov\" \"installdir\" \"Escape from Tarkov\" }",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(eftSession, "application.log"),
                "2026.08.15 10:00:00|1.1.0.1.47000|Info|test\r\n",
                Encoding.UTF8);

            string discoveredEft = TarkovLogPathFinder.FindSteamForGameFromRoot(TarkovGame.Eft, steamRoot);
            Assert(string.Equals(discoveredEft, eftLogs, StringComparison.OrdinalIgnoreCase),
                "Steam discovery follows libraryfolders.vdf and the official EFT app manifest");

            string arenaRoot = Path.Combine(extraSteamApps, "common", "Escape from Tarkov Arena");
            string arenaLogs = Path.Combine(arenaRoot, "Logs");
            string arenaSession = Path.Combine(arenaLogs, "log_steam_arena");
            Directory.CreateDirectory(arenaSession);
            File.WriteAllText(Path.Combine(arenaRoot, "EscapeFromTarkovArena.exe"), string.Empty);
            File.WriteAllText(
                Path.Combine(arenaSession, "lifecycle.log"),
                "2026.08.15 11:00:00|1.1.0.1.47000|Info|test\r\n",
                Encoding.UTF8);

            string discoveredArena = TarkovLogPathFinder.FindSteamForGameFromRoot(TarkovGame.Arena, steamRoot);
            Assert(string.Equals(discoveredArena, arenaLogs, StringComparison.OrdinalIgnoreCase),
                "Steam discovery recognizes Arena only when its executable and Logs folder agree");

            File.Delete(Path.Combine(arenaRoot, "EscapeFromTarkovArena.exe"));
            Assert(TarkovLogPathFinder.FindSteamForGameFromRoot(TarkovGame.Arena, steamRoot) == null,
                "Steam discovery does not claim a name-only Arena folder");

            string unsafeSteamRoot = Path.Combine(tempRoot, "UnsafeSteam");
            string unsafeApps = Path.Combine(unsafeSteamRoot, "steamapps");
            string unsafeCommon = Path.Combine(unsafeApps, "common");
            Directory.CreateDirectory(unsafeCommon);
            File.WriteAllText(
                Path.Combine(unsafeApps, "appmanifest_3932890.acf"),
                "\"AppState\" { \"installdir\" \"..\\\\outside\" }",
                Encoding.UTF8);
            Assert(TarkovLogPathFinder.FindSteamForGameFromRoot(TarkovGame.Eft, unsafeSteamRoot) == null,
                "Steam discovery rejects an install directory that escapes steamapps common");
            Assert(TarkovLogPathFinder.IsFullyQualifiedLocalPath(@"C:\SteamLibrary")
                && TarkovLogPathFinder.IsFullyQualifiedLocalPath("D:/SteamLibrary")
                && !TarkovLogPathFinder.IsFullyQualifiedLocalPath(@"C:relative-library")
                && !TarkovLogPathFinder.IsFullyQualifiedLocalPath(@"\root-relative-library")
                && !TarkovLogPathFinder.IsFullyQualifiedLocalPath(@"\\server\library"),
                "Steam discovery accepts only fully qualified local drive paths");

            string originalCurrentDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = tempRoot;
                string relativeSteamRoot = Path.Combine(tempRoot, "RelativeSteam");
                string relativeSteamApps = Path.Combine(relativeSteamRoot, "steamapps");
                string relativeLibrary = Path.Combine(tempRoot, "relative-library");
                string relativeApps = Path.Combine(relativeLibrary, "steamapps");
                string relativeGame = Path.Combine(relativeApps, "common", "Escape from Tarkov");
                string relativeLogs = Path.Combine(relativeGame, "Logs", "log_relative");
                Directory.CreateDirectory(relativeSteamApps);
                Directory.CreateDirectory(relativeLogs);
                File.WriteAllText(
                    Path.Combine(relativeSteamApps, "libraryfolders.vdf"),
                    "\"libraryfolders\" { \"1\" { \"path\" \"relative-library\" } }",
                    Encoding.UTF8);
                File.WriteAllText(
                    Path.Combine(relativeApps, "appmanifest_3932890.acf"),
                    "\"AppState\" { \"installdir\" \"Escape from Tarkov\" }",
                    Encoding.UTF8);
                File.WriteAllText(
                    Path.Combine(relativeLogs, "application.log"),
                    "2026.08.15 12:00:00|1.1.0.1.47000|Info|test\r\n",
                    Encoding.UTF8);
                Assert(TarkovLogPathFinder.FindSteamForGameFromRoot(TarkovGame.Eft, relativeSteamRoot) == null,
                    "Steam discovery rejects an existing relative library path");
            }
            finally
            {
                Environment.CurrentDirectory = originalCurrentDirectory;
            }
        }

        private static string EscapeVdfPath(string path)
        {
            return path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void TestPingBatchPlanning()
        {
            var sessions = new List<ServerSession>
            {
                new ServerSession { IpAddress = "203.0.113.42" },
                new ServerSession { IpAddress = "198.51.100.18" },
                new ServerSession { IpAddress = "203.0.113.42" },
                new ServerSession { IpAddress = null }
            };
            IList<string> addresses = PingBatchPlanner.GetUniqueServerIps(sessions);

            Assert(addresses.Count == 2, "batch planner deduplicates repeated server IPs");
            Assert(addresses[0] == "203.0.113.42" && addresses[1] == "198.51.100.18",
                "batch planner preserves recent-session order");
        }

        private static void TestPingCancellation()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                bool cancelled = false;
                try
                {
                    NetworkServices.MeasureAdaptivePingAsync(
                        "203.0.113.42", 3, 2, 700, 120, cancellation.Token)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                Assert(cancelled, "cancelled adaptive ping exits without sending more attempts");
            }
        }

        private static void TestPingFormatting()
        {
            var blocked = new PingResult { Sent = 4, Received = 0 };
            Assert(blocked.ToDisplayText() == "응답 없음", "unanswered ping stays distinct from firewall blocking");
            var available = new PingResult { Sent = 5, Received = 5, MinimumMs = 60, AverageMs = 65, MaximumMs = 70 };
            Assert(!available.ToDisplayText().Contains("손실"), "available ping text omits ICMP loss percentage");
        }

        private static void TestPingQualityThresholds()
        {
            Assert(new PingResult { Received = 1, AverageMs = 99 }.Quality == PingQuality.Good,
                "ping below 100ms is good");
            Assert(new PingResult { Received = 1, AverageMs = 100 }.Quality == PingQuality.Elevated,
                "ping at 100ms is elevated");
            Assert(new PingResult { Received = 1, AverageMs = 149 }.Quality == PingQuality.Elevated,
                "ping below 150ms remains elevated");
            Assert(new PingResult { Received = 1, AverageMs = 150 }.Quality == PingQuality.High,
                "ping at 150ms is high");
        }

        private static void TestProductUserAgent()
        {
            Assert(NetworkServices.ProductUserAgent == "TarkovServerGuard/0.7.2",
                "network requests use the v0.7.2 product user agent");
        }

        private static void TestGeoFormatting()
        {
            var geo = new GeoInfo { Success = true, City = "Singapore", CountryCode = "SG" };
            Assert(geo.ToDisplayText() == "Singapore, SG", "geo display combines city and country code");
        }

        private static void TestConnectionResultFormatting()
        {
            var timedOutAfterConnect = new ServerSession
            {
                ConnectionAttempts = 1,
                ConnectedOnce = true,
                TimedOut = true
            };
            Assert(timedOutAfterConnect.ReconnectCount == 0
                && timedOutAfterConnect.ConnectionStateText == "비정상종료 · 시간초과"
                && timedOutAfterConnect.ConnectionResultText == "비정상종료 · 시간초과",
                "a timeout after a successful connection shows outcome before cause without a reconnect suffix");

            var endedWithoutReason = new ServerSession
            {
                ConnectionAttempts = 1,
                ConnectedOnce = true,
                HasDisconnectRecord = true
            };
            Assert(endedWithoutReason.ConnectionResultText == "종료미확인",
                "a disconnect without an explicit reason is not labeled as a normal exit");

            var pendingReconnect = new ServerSession
            {
                ConnectionAttempts = 2,
                ConnectionAttemptKeys = new HashSet<string> { "first", "second" },
                ConnectedOnce = true,
                CurrentAttemptConnected = false
            };
            Assert(pendingReconnect.ReconnectCount == 1
                && pendingReconnect.ConnectionResultText == "종료미확인 · 재접속 1회",
                "positive reconnects append the count and Korean occurrence suffix");
            Assert(pendingReconnect.ConnectionStateText == "종료미확인",
                "the detail connection state does not duplicate the separate reconnect count");

            var timedOutReconnect = new ServerSession
            {
                ConnectionAttempts = 2,
                ConnectionAttemptKeys = new HashSet<string> { "first", "second" },
                ConnectedOnce = true,
                CurrentAttemptConnected = false,
                TimedOut = true
            };
            Assert(timedOutReconnect.ConnectionStateText == "비정상종료 · 시간초과"
                && timedOutReconnect.ConnectionResultText == "비정상종료 · 시간초과 · 재접속 1회",
                "a timeout after connection keeps outcome, cause, and reconnect count in that order");

            var failedTimedOutReconnect = new ServerSession
            {
                ConnectionAttempts = 2,
                ConnectionAttemptKeys = new HashSet<string> { "first", "second" },
                TimedOut = true
            };
            Assert(failedTimedOutReconnect.ConnectionStateText == "접속실패 · 시간초과"
                && failedTimedOutReconnect.ConnectionResultText == "접속실패 · 시간초과 · 재접속 1회",
                "a timeout without a successful connection shows failure, cause, and retry count in that order");

            var normalEnd = new ServerSession
            {
                ConnectionAttempts = 1,
                ConnectedOnce = true,
                HasDisconnectRecord = true,
                DisconnectReason = 0
            };
            Assert(normalEnd.ConnectionStateText == "정상종료",
                "an explicit zero disconnect reason uses the compact normal-end label");

            var abnormalAfterConnect = new ServerSession
            {
                ConnectionAttempts = 1,
                ConnectedOnce = true,
                HasDisconnectRecord = true,
                DisconnectReason = 5
            };
            Assert(abnormalAfterConnect.ConnectionStateText == "비정상종료",
                "a connected session with an explicit nonzero reason is abnormal when it did not time out");

            abnormalAfterConnect.TimedOut = true;
            Assert(abnormalAfterConnect.ConnectionStateText == "비정상종료 · 시간초과",
                "timeout cause is retained after the abnormal connected outcome");

            var failedBeforeConnect = new ServerSession
            {
                ConnectionAttempts = 1,
                HasDisconnectRecord = true
            };
            Assert(failedBeforeConnect.ConnectionStateText == "접속실패",
                "an explicit end before any connection is labeled as a connection failure");
            failedBeforeConnect.DisconnectReason = 5;
            Assert(failedBeforeConnect.ConnectionStateText == "접속실패",
                "a nonzero reason before any successful connection remains a connection failure");

            var timeoutWithoutAttemptCounter = new ServerSession
            {
                HostingMode = TarkovHostingMode.Server,
                TimedOut = true
            };
            Assert(timeoutWithoutAttemptCounter.ConnectionStateText == "접속실패 · 시간초과",
                "timeout without any successful connection is a connection failure even when the attempt counter is unavailable");

            var endedWithoutAttemptCounter = new ServerSession
            {
                HostingMode = TarkovHostingMode.Server,
                HasDisconnectRecord = true,
                DisconnectReason = 5
            };
            Assert(endedWithoutAttemptCounter.ConnectionStateText == "접속실패",
                "an explicit pre-connect end takes priority over a missing attempt counter");

            Assert(new ServerSession().ConnectionStateText == "접속기록 없음",
                "a server session without network evidence uses the compact no-record label");
            Assert(new ServerSession
                {
                    HostingMode = TarkovHostingMode.Local
                }.ConnectionStateText == "해당 없음",
                "connection state is not applicable to a confirmed local raid");
        }

        private static void TestFirewallCommandValidation()
        {
            Assert(FirewallRuleManager.IsValidIpv4("203.0.113.42"),
                "firewall helper accepts canonical IPv4");
            Assert(!FirewallRuleManager.IsValidIpv4("1")
                && !FirewallRuleManager.IsValidIpv4("001.002.003.004")
                && !FirewallRuleManager.IsValidIpv4(" 203.0.113.42 ")
                && !FirewallRuleManager.IsValidIpv4("203.0.113.42 & whoami")
                && !FirewallRuleManager.IsValidIpv4("2001:db8::1")
                && !FirewallRuleManager.IsValidIpv4("0.0.0.0")
                && !FirewallRuleManager.IsValidIpv4("127.0.0.1")
                && !FirewallRuleManager.IsValidIpv4("224.0.0.1"),
                "firewall helper rejects non-canonical and unsafe addresses");

            Assert(FirewallRuleManager.IsManagedRuleName(
                    "TarkovServerGuard_Block_203.0.113.42",
                    "203.0.113.42")
                && FirewallRuleManager.IsManagedRuleName(
                    "EFT_ExcludeChinaHighPingServer_203.0.113.42",
                    "203.0.113.42"),
                "firewall helper recognizes current and legacy managed rules");
            Assert(!FirewallRuleManager.IsManagedRuleName(
                    "UnrelatedRule_203.0.113.42",
                    "203.0.113.42")
                && !FirewallRuleManager.IsManagedRuleName(
                    "TarkovServerGuard_Block_203.0.113.43",
                    "203.0.113.42"),
                "firewall helper does not claim unrelated rules");

            Assert(FirewallRuleManager.TargetsOnlyAddress("203.0.113.42", "203.0.113.42")
                && FirewallRuleManager.TargetsOnlyAddress("203.0.113.42/32", "203.0.113.42")
                && FirewallRuleManager.TargetsOnlyAddress(
                    "203.0.113.42/255.255.255.255",
                    "203.0.113.42"),
                "firewall helper accepts equivalent single-host address formats");
            Assert(!FirewallRuleManager.TargetsOnlyAddress("203.0.113.42/24", "203.0.113.42")
                && !FirewallRuleManager.TargetsOnlyAddress(
                    "203.0.113.42/255.255.255.0",
                    "203.0.113.42")
                && !FirewallRuleManager.TargetsOnlyAddress(
                    "203.0.113.42,203.0.113.43",
                    "203.0.113.42")
                && !FirewallRuleManager.TargetsOnlyAddress("*", "203.0.113.42"),
                "firewall helper rejects ranges, lists, and wildcards");

            string managedAddress;
            Assert(FirewallRuleManager.TryGetManagedAddress(
                    "TarkovServerGuard_Block_203.0.113.42",
                    out managedAddress)
                && managedAddress == "203.0.113.42"
                && !FirewallRuleManager.TryGetManagedAddress(
                    "TarkovServerGuard_Block_1",
                    out managedAddress),
                "firewall helper extracts only canonical managed addresses");

            bool shouldBlock;
            string ipAddress;
            Assert(FirewallRuleManager.TryParseHelperCommand(
                    new[] { "--firewall-add", "203.0.113.42" },
                    out shouldBlock,
                    out ipAddress)
                && shouldBlock
                && ipAddress == "203.0.113.42",
                "firewall helper parses an add command");
            Assert(FirewallRuleManager.TryParseHelperCommand(
                    new[] { "--firewall-remove", "203.0.113.42" },
                    out shouldBlock,
                    out ipAddress)
                && !shouldBlock,
                "firewall helper parses a remove command");
            Assert(!FirewallRuleManager.TryParseHelperCommand(
                    new[] { "--firewall-add", "203.0.113.42", "extra" },
                    out shouldBlock,
                    out ipAddress),
                "firewall helper rejects extra arguments");
        }

        private static void Assert(bool condition, string name)
        {
            if (condition)
            {
                Console.WriteLine("PASS: " + name);
                return;
            }

            _failures++;
            Console.WriteLine("FAIL: " + name);
        }
    }
}
