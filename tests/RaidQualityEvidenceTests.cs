// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;

namespace TarkovServerReporter.Tests
{
    internal static class RaidQualityEvidenceTests
    {
        private static int Main()
        {
            try
            {
                TestSignalBoundaries();
                TestLatestRaidWindowIncludesLocalButMatchesOnlyServers();
                TestRecentCapPrecedesTargetIpFiltering();
                TestMixedLocalAndServerRaidsShareTheSameRecentCap();
                TestCompositeSignalsAndDuplicateRaidsCountOnce();
                TestInsufficientMetricsAreNotProblemEvidence();
                TestExactNonCausalMessage();
                Console.WriteLine("RaidQualityEvidenceTests: PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("RaidQualityEvidenceTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void TestSignalBoundaries()
        {
            DateTime now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Local);
            var sessions = new[]
            {
                CreateServerRaid("rtt-below", "1.1.1.1", now.AddMinutes(-1), 149.999, 0.0, false),
                CreateServerRaid("rtt-boundary", "1.1.1.1", now.AddMinutes(-2), 150.0, 0.0, false),
                CreateServerRaid("loss-below", "1.1.1.1", now.AddMinutes(-3), 40.0, 0.049999, false),
                CreateServerRaid("loss-boundary", "1.1.1.1", now.AddMinutes(-4), 40.0, 0.05, false),
                CreateServerRaid("timeout", "1.1.1.1", now.AddMinutes(-5), 40.0, 0.0, true),
                CreateServerRaid("nan", "1.1.1.1", now.AddMinutes(-6), double.NaN, double.NaN, false),
                CreateServerRaid("infinity", "1.1.1.1", now.AddMinutes(-7), double.PositiveInfinity, double.PositiveInfinity, false)
            };

            RaidQualityEvidenceSummary result = RaidQualityEvidence.Analyze(sessions, "1.1.1.1");
            Assert(result.WindowRaidCount == 7
                    && result.MatchingRaidCount == 7
                    && result.ProblemRaidCount == 3,
                "high RTT, packet loss, and timeout boundaries must be exact and non-finite values ignored");
        }

        private static void TestLatestRaidWindowIncludesLocalButMatchesOnlyServers()
        {
            DateTime now = new DateTime(2026, 8, 22, 13, 0, 0, DateTimeKind.Local);
            var local = CreateServerRaid("local", "1.1.1.1", now, 900, 0.9, true);
            local.HostingMode = TarkovHostingMode.Local;
            var unknown = CreateServerRaid("unknown", "1.1.1.1", now.AddSeconds(-1), 900, 0.9, true);
            unknown.HostingMode = TarkovHostingMode.Unknown;
            var missingIp = CreateServerRaid("missing-ip", null, now.AddSeconds(-2), 900, 0.9, true);
            var target = CreateServerRaid("target", "1.1.1.1", now.AddSeconds(-3), 70, 0, false);
            var other = CreateServerRaid("other", "8.8.8.8", now.AddSeconds(-4), 170, 0, false);
            var arena = CreateServerRaid("arena", "1.1.1.1", now.AddSeconds(-5), 180, 0, false);
            arena.Game = TarkovGame.Arena;

            RaidQualityEvidenceSummary result = RaidQualityEvidence.Analyze(
                new[] { local, unknown, missingIp, target, other, arena },
                "1.1.1.1");
            Assert(result.WindowRaidCount == 4
                    && result.MatchingRaidCount == 2
                    && result.ProblemRaidCount == 1,
                "the recent-raid denominator includes local raids while only server-hosted EFT and Arena raids can match an IP");
        }

        private static void TestRecentCapPrecedesTargetIpFiltering()
        {
            DateTime now = new DateTime(2026, 8, 22, 14, 0, 0, DateTimeKind.Local);
            var sessions = new List<ServerSession>();
            for (int index = 0; index < RaidQualityEvidence.MaximumRecentRaids; index++)
            {
                sessions.Add(CreateServerRaid(
                    "recent-" + index,
                    "8.8.8.8",
                    now.AddMinutes(-index),
                    40,
                    0,
                    false));
            }
            sessions.Add(CreateServerRaid(
                "older-target",
                "1.1.1.1",
                now.AddMinutes(-RaidQualityEvidence.MaximumRecentRaids),
                300,
                0.2,
                true));

            RaidQualityEvidenceSummary result = RaidQualityEvidence.Analyze(sessions, "1.1.1.1");
            Assert(result.WindowRaidCount == RaidQualityEvidence.MaximumRecentRaids
                    && result.MatchingRaidCount == 0
                    && result.ProblemRaidCount == 0,
                "the global recent-server-raid cap must be applied before filtering for the target IP");
        }

        private static void TestMixedLocalAndServerRaidsShareTheSameRecentCap()
        {
            DateTime now = new DateTime(2026, 8, 22, 14, 30, 0, DateTimeKind.Local);
            var sessions = new List<ServerSession>();
            for (int index = 0; index < 40; index++)
            {
                ServerSession local = CreateServerRaid(
                    "local-recent-" + index,
                    null,
                    now.AddSeconds(-index),
                    0,
                    0,
                    false);
                local.HostingMode = TarkovHostingMode.Local;
                sessions.Add(local);
            }
            for (int index = 0; index < 60; index++)
            {
                sessions.Add(CreateServerRaid(
                    "server-recent-" + index,
                    "8.8.8.8",
                    now.AddMinutes(-1).AddSeconds(-index),
                    40,
                    0,
                    false));
            }
            sessions.Add(CreateServerRaid(
                "older-target",
                "1.1.1.1",
                now.AddHours(-1),
                300,
                0.2,
                true));

            RaidQualityEvidenceSummary result = RaidQualityEvidence.Analyze(
                sessions,
                "1.1.1.1");
            Assert(result.WindowRaidCount == RaidQualityEvidence.MaximumRecentRaids
                    && result.MatchingRaidCount == 0
                    && result.ProblemRaidCount == 0,
                "local raids and server raids must share the same latest-100 window before target-IP filtering");
        }

        private static void TestCompositeSignalsAndDuplicateRaidsCountOnce()
        {
            DateTime now = new DateTime(2026, 8, 22, 15, 0, 0, DateTimeKind.Local);
            ServerSession problem = CreateServerRaid(
                "same-problem",
                "1.1.1.1",
                now,
                200,
                0.25,
                true);
            ServerSession copiedProblem = CreateServerRaid(
                "same-problem",
                "1.1.1.1",
                now.AddSeconds(-1),
                200,
                0.25,
                true);
            ServerSession good = CreateServerRaid(
                "good",
                "1.1.1.1",
                now.AddSeconds(-2),
                50,
                0,
                false);

            RaidQualityEvidenceSummary result = RaidQualityEvidence.Analyze(
                new[] { problem, problem, copiedProblem, good },
                "1.1.1.1");
            Assert(result.WindowRaidCount == 2
                    && result.MatchingRaidCount == 2
                    && result.ProblemRaidCount == 1,
                "duplicate session identities and multiple problem signals in one raid must each count once");
        }

        private static void TestInsufficientMetricsAreNotProblemEvidence()
        {
            DateTime now = new DateTime(2026, 8, 22, 15, 30, 0, DateTimeKind.Local);
            ServerSession insufficient = CreateServerRaid(
                "insufficient",
                "1.1.1.1",
                now,
                300,
                0.5,
                false);
            insufficient.NetworkStatisticsObserved = true;
            insufficient.NetworkStatisticsAt = now;
            insufficient.NetworkReceived = 0;

            RaidQualityEvidenceSummary ignored = RaidQualityEvidence.Analyze(
                new[] { insufficient },
                "1.1.1.1");
            Assert(ignored.WindowRaidCount == 1
                    && ignored.MatchingRaidCount == 1
                    && ignored.ProblemRaidCount == 0,
                "zero-receive RTT and loss reference values must not become server-quality problem evidence");

            insufficient.NetworkReceived = 1;
            RaidQualityEvidenceSummary measured = RaidQualityEvidence.Analyze(
                new[] { insufficient },
                "1.1.1.1");
            Assert(measured.ProblemRaidCount == 1,
                "one received sample makes the logged values eligible without an arbitrary minimum threshold");

            insufficient.NetworkReceived = 0;
            insufficient.TimedOut = true;
            RaidQualityEvidenceSummary timeout = RaidQualityEvidence.Analyze(
                new[] { insufficient },
                "1.1.1.1");
            Assert(timeout.ProblemRaidCount == 1,
                "an explicit timeout remains evidence even when RTT and loss samples are insufficient");
        }

        private static void TestExactNonCausalMessage()
        {
            DateTime now = new DateTime(2026, 8, 22, 16, 0, 0, DateTimeKind.Local);
            RaidQualityEvidenceSummary result = RaidQualityEvidence.Analyze(
                new[]
                {
                    CreateServerRaid("target-problem", "1.1.1.1", now, 150, 0, false),
                    CreateServerRaid("target-good", "1.1.1.1", now.AddSeconds(-1), 50, 0, false),
                    CreateServerRaid("other", "8.8.8.8", now.AddSeconds(-2), 50, 0, false)
                },
                "1.1.1.1");
            Assert(result.ToDisplayText()
                    == "최근 레이드 3개 중 이 IP가 사용된 2개를 확인했고, 그중 1개에서 높은 지연·패킷 손실·시간초과 징후가 확인되었습니다.",
                "the user-facing evidence text must report observation without claiming causation");

            RaidQualityEvidenceSummary emptyTarget = RaidQualityEvidence.Analyze(null, null);
            Assert(emptyTarget.WindowRaidCount == 0
                    && emptyTarget.MatchingRaidCount == 0
                    && emptyTarget.ProblemRaidCount == 0
                    && emptyTarget.ToDisplayText()
                        == "최근 레이드 0개 중 이 IP가 사용된 0개를 확인했고, 그중 0개에서 높은 지연·패킷 손실·시간초과 징후가 확인되었습니다.",
                "empty input must remain deterministic and safe");
        }

        private static ServerSession CreateServerRaid(
            string key,
            string ipAddress,
            DateTime detectedAt,
            double actualRttMs,
            double networkLoss,
            bool timedOut)
        {
            return new ServerSession
            {
                Game = TarkovGame.Eft,
                SessionKey = key,
                SessionStarted = detectedAt,
                IpDetectedAt = detectedAt,
                LastUpdated = detectedAt,
                HostingMode = TarkovHostingMode.Server,
                IpAddress = ipAddress,
                ActualRttMs = actualRttMs,
                NetworkLoss = networkLoss,
                TimedOut = timedOut
            };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
