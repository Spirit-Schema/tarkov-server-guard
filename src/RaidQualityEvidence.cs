// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TarkovServerReporter
{
    public sealed class RaidQualityEvidenceSummary
    {
        internal RaidQualityEvidenceSummary(
            int windowRaidCount,
            int matchingRaidCount,
            int problemRaidCount)
        {
            WindowRaidCount = Math.Max(0, windowRaidCount);
            MatchingRaidCount = Math.Max(0, matchingRaidCount);
            ProblemRaidCount = Math.Max(0, Math.Min(problemRaidCount, MatchingRaidCount));
        }

        public int WindowRaidCount { get; private set; }
        public int MatchingRaidCount { get; private set; }
        public int ProblemRaidCount { get; private set; }

        public string ToDisplayText()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "최근 레이드 {0}개 중 이 IP가 사용된 {1}개를 확인했고, 그중 {2}개에서 높은 지연·패킷 손실·시간초과 징후가 확인되었습니다.",
                WindowRaidCount,
                MatchingRaidCount,
                ProblemRaidCount);
        }
    }

    public static class RaidQualityEvidence
    {
        public const int MaximumRecentRaids = 100;
        public const double HighRttThresholdMs = 150.0;
        public const double HighPacketLossThreshold = 0.05;

        public static RaidQualityEvidenceSummary Analyze(
            IEnumerable<ServerSession> sessions,
            string targetIpAddress)
        {
            IList<ServerSession> recentRaids = GetRecentRaids(sessions);
            string normalizedTarget = string.IsNullOrWhiteSpace(targetIpAddress)
                ? string.Empty
                : targetIpAddress.Trim();
            IList<ServerSession> matching = recentRaids
                .Where(session => session.HostingMode == TarkovHostingMode.Server
                    && session.HasServerIp
                    && string.Equals(
                        session.IpAddress,
                        normalizedTarget,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new RaidQualityEvidenceSummary(
                recentRaids.Count,
                matching.Count,
                matching.Count(IsProblemRaid));
        }

        internal static bool IsProblemRaid(ServerSession session)
        {
            if (session == null) return false;
            double actualRtt;
            double packetLoss;
            return RaidMetricPresentation.TryGetActualRtt(session, out actualRtt)
                    && actualRtt >= HighRttThresholdMs
                || RaidMetricPresentation.TryGetPacketLoss(session, out packetLoss)
                    && packetLoss >= HighPacketLossThreshold
                || session.TimedOut;
        }

        private static IList<ServerSession> GetRecentRaids(
            IEnumerable<ServerSession> sessions)
        {
            var seenSessionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenInstances = new HashSet<ServerSession>();
            return (sessions ?? Enumerable.Empty<ServerSession>())
                .Where(session => session != null
                    && (session.HostingMode == TarkovHostingMode.Local
                        || session.HostingMode == TarkovHostingMode.Server
                            && session.HasServerIp))
                .OrderByDescending(session => session.DisplayDetectedAt)
                .ThenByDescending(session => session.LastUpdated)
                .Where(session => IsFirstOccurrence(
                    session,
                    seenSessionKeys,
                    seenInstances))
                .Take(MaximumRecentRaids)
                .ToList();
        }

        private static bool IsFirstOccurrence(
            ServerSession session,
            ISet<string> seenSessionKeys,
            ISet<ServerSession> seenInstances)
        {
            if (!string.IsNullOrWhiteSpace(session.SessionKey))
            {
                string key = ((int)session.Game).ToString(CultureInfo.InvariantCulture)
                    + "|" + session.SessionKey.Trim();
                return seenSessionKeys.Add(key);
            }
            return seenInstances.Add(session);
        }

    }
}
