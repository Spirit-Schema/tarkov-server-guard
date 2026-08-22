// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TarkovServerReporter.Tests
{
    internal static class RaidParticipantLogTests
    {
        private static int _failures;
        private static int _sequence;

        public static int Main()
        {
            Run("24-digit profile relation and carry", TestProfileIdRelations);
            Run("PvP PMC solo", TestPmcSolo);
            Run("explicit PvP season Scav solo", TestPvpSeasonScavSolo);
            Run("server PvE profile roles", TestServerPveProfileRoles);
            Run("2-5 member party snapshots", TestPartySizes);
            Run("numeric aid party member", TestNumericAidPartyMember);
            Run("observed two-member party event order", TestObservedTwoMemberPartyFlow);
            Run("unknown push type is ignored", TestUnknownPushTypeIgnored);
            Run("mismatched push payload type is rejected", TestPayloadTypeMismatchRejected);
            Run("visual side conflict is Unknown", TestVisualSideConflict);
            Run("ready transitions and member removal", TestReadyTransitionsAndRemoval);
            Run("incomplete party retains party without size", TestIncompleteParty);
            Run("expired ready state does not leak into a new party",
                TestExpiredReadyStateDoesNotLeak);
            Run("cancelled generation does not leak", TestCancelledGenerationDoesNotLeak);
            Run("multiple raids remain independent", TestMultipleRaids);
            Run("push changes invalidate warm directory cache", TestPushCacheInvalidation);
            Run("duplicate event ids are deduplicated", TestDuplicateEventIds);
            Run("duplicate event ids across rotated push logs are deduplicated",
                TestDuplicateEventIdsAcrossRotatedPushLogs);
            Run("invalid push payload fields are ignored", TestInvalidPushPayloadFields);
            Run("malformed and oversized push events are ignored", TestMalformedAndOversizedPush);
            Run("oversized push file is ignored", TestOversizedPushFile);
            Run("local PvE without direct evidence stays Unknown", TestLocalPveUnknown);
            Run("local PvE solo PMC", TestLocalPveSoloPmc);
            Run("local PvE solo Scav", TestLocalPveSoloScav);
            Run("derived sessions retain no transient identifiers", TestNoTransientIdentifiersPersisted);

            Console.WriteLine(_failures == 0
                ? "Raid participant log tests passed."
                : "Raid participant log tests failed: " + _failures);
            return _failures == 0 ? 0 : 1;
        }

        private static void TestProfileIdRelations()
        {
            Assert(RaidLogScanner.CompareProfileIds(
                "00112233445566778899aabb",
                "00112233445566778899aabb") == TarkovCharacterType.Pmc,
                "equal profile IDs must be PMC");
            Assert(RaidLogScanner.CompareProfileIds(
                "00112233445566778899aaff",
                "00112233445566778899ab00") == TarkovCharacterType.Scav,
                "carry across multiple hexadecimal digits must be Scav");
            Assert(RaidLogScanner.CompareProfileIds(
                "00ffffffffffffffffffffff",
                "010000000000000000000000") == TarkovCharacterType.Scav,
                "96-bit carry must not be truncated to UInt64");
            Assert(RaidLogScanner.CompareProfileIds(
                "ffffffffffffffffffffffff",
                "000000000000000000000000") == TarkovCharacterType.Unknown,
                "overflow must not wrap around");
            Assert(RaidLogScanner.CompareProfileIds(
                "00112233445566778899aabb",
                "00112233445566778899aabd") == TarkovCharacterType.Unknown,
                "unrelated profile IDs must stay Unknown");
            Assert(RaidLogScanner.CompareProfileIds("invalid", "also-invalid")
                == TarkovCharacterType.Unknown,
                "malformed profile IDs must stay Unknown");
        }

        private static void TestPmcSolo()
        {
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899aabb",
                "00112233445566778899aabb",
                false,
                true,
                null,
                "SOLO-PMC");
            Assert(session.CharacterType == TarkovCharacterType.Pmc, "PMC was not inferred");
            Assert(session.ParticipationType == TarkovParticipationType.Solo,
                "empty group plus match/join must be Solo");
            Assert(!session.PartySize.HasValue, "solo raid retained a party size");
        }

        private static void TestPvpSeasonScavSolo()
        {
            ServerSession session = ScanSingleServerRaid(
                "PvpSeason2",
                "00112233445566778899aaff",
                "00112233445566778899ab00",
                false,
                true,
                null,
                "SOLO-SCAV-SEASON2");
            Assert(session.ProgressionMode == TarkovProgressionMode.PvpSeason
                && session.PvpSeasonNumber == 2,
                "explicit PvpSeason2 was not retained");
            Assert(session.CharacterType == TarkovCharacterType.Scav,
                "full-width +1 relation was not inferred as Scav");
            Assert(session.ParticipationType == TarkovParticipationType.Solo,
                "season Scav solo was not inferred");
        }

        private static void TestPartySizes()
        {
            for (int remoteMembers = 1; remoteMembers <= 4; remoteMembers++)
            {
                string push = BuildReadyPartyPush(remoteMembers, "Usec", false, false);
                ServerSession session = ScanSingleServerRaid(
                    "Pve",
                    "00112233445566778899aa10",
                    "00112233445566778899aa10",
                    true,
                    false,
                    push,
                    "PARTY-" + remoteMembers);
                Assert(session.ParticipationType == TarkovParticipationType.Party,
                    "group start was not Party for remote count " + remoteMembers);
                Assert(session.PartySize == remoteMembers + 1,
                    "wrong party size for remote count " + remoteMembers);
                Assert(session.CharacterType == TarkovCharacterType.Pmc,
                    "matching profile and Usec side did not remain PMC");
            }
        }

        private static void TestServerPveProfileRoles()
        {
            ServerSession pmc = ScanSingleServerRaid(
                "Pve",
                "00112233445566778899aa05",
                "00112233445566778899aa05",
                false,
                true,
                null,
                "PVE-SERVER-PMC");
            Assert(pmc.HostingMode == TarkovHostingMode.Server
                    && pmc.CharacterType == TarkovCharacterType.Pmc
                    && pmc.ParticipationType == TarkovParticipationType.Solo,
                "server PvE equal ProfileId must remain PMC solo");

            ServerSession scav = ScanSingleServerRaid(
                "Pve",
                "00112233445566778899aa0f",
                "00112233445566778899aa10",
                false,
                true,
                null,
                "PVE-SERVER-SCAV");
            Assert(scav.HostingMode == TarkovHostingMode.Server
                    && scav.CharacterType == TarkovCharacterType.Scav
                    && scav.ParticipationType == TarkovParticipationType.Solo,
                "server PvE +1 ProfileId must remain Scav solo");
        }

        private static void TestNumericAidPartyMember()
        {
            string push = PushEvent(
                    "10:00:03.100",
                    "GroupMatchRaidReady",
                    ReadyJsonNumeric("numeric-ready", 900000001L, "Usec", "Usec"))
                + PushEvent("10:00:04.000", "GroupMatchStartGame", StartJson("numeric-start"));
            ServerSession session = ScanSingleServerRaid(
                "Pve",
                "00112233445566778899aa11",
                "00112233445566778899aa11",
                true,
                false,
                push,
                "NUMERIC-AID");
            Assert(session.ParticipationType == TarkovParticipationType.Party
                    && session.PartySize == 2,
                "an integral numeric aid from the real push schema must count once");
        }

        private static void TestObservedTwoMemberPartyFlow()
        {
            string root = CreateRoot();
            try
            {
                const long remoteAid = 5000000001L;
                string directory = CreateSessionDirectory(root);
                var application = new StringBuilder();
                application.Append(App("09:59:59.000", "Session mode: Pve"));
                application.Append(App("10:00:00.000",
                    "PrepareSelectedProfileLocally ProfileId:00112233445566778899aa20 AccountId:100"));
                application.Append(App("10:00:01.500", "MatchingCompleted:0 real:0 diff:0"));
                application.Append(App("10:00:02.100",
                    "scene preset path:maps/factory_day_preset.bundle rcid:factory4_day"));
                application.Append(App("10:00:02.500", "GameCreated:1 real:1 diff:0"));
                application.Append(App("10:00:03.700",
                    "Matching with group id: synthetic-observed-group"));
                application.Append(ServerAssignment(
                    "10:01:12.790",
                    "00112233445566778899aa20",
                    "OBSERVED-TWO",
                    "OBSERVED"));
                application.Append(App("10:01:45.000", "GameStarted:1 real:1 diff:0"));
                Write(directory, "synthetic application_000.log", application.ToString());
                Write(directory, "synthetic backend_000.log",
                    Backend("10:00:03.750", "/client/match/group/start_game")
                    + Backend("10:00:03.850", "/client/match/group/start_game"));
                Write(directory, "synthetic push-notifications_000.log",
                    PushEvent("10:00:01.000", "GroupMatchInviteAccept",
                        InviteAcceptJsonNumeric(
                            "observed-invite", remoteAid, false))
                    + PushEvent("10:00:03.100", "GroupMatchRaidReady",
                        ReadyJsonNumeric(
                            "observed-ready-1", remoteAid, "Usec", "Usec"))
                    + PushEvent("10:00:03.400", "GroupMatchRaidNotReady",
                        SimpleAidJsonNumeric(
                            "GroupMatchRaidNotReady", "observed-not-ready", remoteAid))
                    + PushEvent("10:00:03.600", "GroupMatchRaidReady",
                        ReadyJsonNumeric(
                            "observed-ready-2", remoteAid, "Usec", "Usec"))
                    + PushEvent("10:00:03.790", "GroupMatchStartGame",
                        StartJson("observed-start")));

                ServerSession session = Scan(root).Single();
                Assert(session.ParticipationType == TarkovParticipationType.Party
                        && session.PartySize == 2
                        && session.ParticipationTypeText == "2인",
                    "the observed Ready/group/routes/Start/assignment order lost its two-member snapshot");
                Assert(session.CharacterType == TarkovCharacterType.Pmc,
                    "the observed nested visual side no longer agrees with the profile relation");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestUnknownPushTypeIgnored()
        {
            string push = PushEvent("10:00:03.000", "GroupMatchUnknown", "{}")
                + PushEvent("10:00:03.100", "GroupMatchRaidReady",
                    ReadyJson("ready", "member-unknown", "Usec", "Usec"))
                + PushEvent("10:00:03.200", "GroupMatchStartGame", StartJson("start"));
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899ab01",
                "00112233445566778899ab01",
                true,
                false,
                push,
                "UNKNOWN-PUSH");
            Assert(session.ParticipationType == TarkovParticipationType.Party,
                "unknown push type was not safely ignored");
            Assert(session.PartySize == 2,
                "valid push event was not counted after unknown push");
        }

        private static void TestPayloadTypeMismatchRejected()
        {
            string push = PushEvent("10:00:03.000", "GroupMatchInviteAccept",
                    InviteAcceptJson("invite-1", "member-mismatch", "false", "GroupMatchRaidReady"))
                + PushEvent("10:00:03.100", "GroupMatchInviteAccept",
                    InviteAcceptJson("invite-2", "member-valid", "true", "GroupMatchInviteAccept"))
                + PushEvent("10:00:03.200", "GroupMatchStartGame", StartJson("start"));
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899ab02",
                "00112233445566778899ab02",
                true,
                false,
                push,
                "MISMATCH");
            Assert(session.ParticipationType == TarkovParticipationType.Party,
                "party start was not recognized when first payload type mismatched");
            Assert(session.PartySize == 2,
                "only correctly-typed invite should be counted");
        }

        private static void TestVisualSideConflict()
        {
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899aa20",
                "00112233445566778899aa20",
                true,
                false,
                BuildReadyPartyPush(1, "Savage", true, false),
                "CONFLICT");
            Assert(session.CharacterType == TarkovCharacterType.Unknown,
                "profile/PlayerVisualRepresentation conflict must be Unknown");
            Assert(session.ParticipationType == TarkovParticipationType.Party
                && session.PartySize == 2,
                "role conflict must not discard a valid party size");
        }

        private static void TestReadyTransitionsAndRemoval()
        {
            string push = PushEvent("10:00:03.100", "GroupMatchRaidReady",
                    ReadyJson("ready-a", "member-a", "Usec", "Savage"))
                + PushEvent("10:00:03.200", "GroupMatchRaidReady",
                    ReadyJson("ready-a-repeat", "member-a", "Usec", "Savage"))
                + PushEvent("10:00:03.300", "GroupMatchRaidNotReady",
                    SimpleAidJson("GroupMatchRaidNotReady", "not-a", "member-a"))
                + PushEvent("10:00:03.400", "GroupMatchRaidReady",
                    ReadyJson("ready-a-again", "member-a", "Usec", "Savage"))
                + PushEvent("10:00:03.500", "GroupMatchRaidReady",
                    ReadyJson("ready-b", "member-b", "Usec", "Savage"))
                + PushEvent("10:00:03.600", "GroupMatchUserLeave",
                    SimpleAidJson("GroupMatchUserLeave", "leave-b", "member-b"))
                + PushEvent("10:00:04.000", "GroupMatchStartGame", StartJson("start-final"));
            ServerSession session = ScanSingleServerRaid(
                "PvpSeason1",
                "00112233445566778899aa30",
                "00112233445566778899aa31",
                true,
                false,
                push,
                "TRANSITIONS");
            Assert(session.PartySize == 2,
                "duplicate Ready or a departed member was counted more than once");
            Assert(session.CharacterType == TarkovCharacterType.Scav,
                "final Savage state and +1 profile should be Scav");
        }

        private static void TestIncompleteParty()
        {
            string push = PushEvent("10:00:03.100", "GroupMatchRaidReady",
                    ReadyJson("ready", "member-a", "Usec", "Usec"))
                + PushEvent("10:00:03.200", "GroupMatchRaidNotReady",
                    SimpleAidJson("GroupMatchRaidNotReady", "not-ready", "member-a"))
                + PushEvent("10:00:04.000", "GroupMatchStartGame", StartJson("start"));
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899aa40",
                "00112233445566778899aa40",
                true,
                false,
                push,
                "INCOMPLETE");
            Assert(session.ParticipationType == TarkovParticipationType.Party,
                "a confirmed group start must remain Party");
            Assert(!session.PartySize.HasValue,
                "NotReady final state must not be converted to a numeric party size");
        }

        private static void TestDuplicateEventIds()
        {
            string push = PushEvent("10:00:03.100", "GroupMatchRaidReady",
                    ReadyJson("dup", "member-a", "Usec", "Usec"))
                + PushEvent("10:00:03.200", "GroupMatchRaidReady",
                    ReadyJson("dup", "member-b", "Usec", "Usec"))
                + PushEvent("10:00:03.300", "GroupMatchStartGame", StartJson("start"));
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899ab10",
                "00112233445566778899ab10",
                true,
                false,
                push,
                "DUP-EID");
            Assert(session.PartySize == 2,
                "deduplication by eventId was skipped");
        }

        private static void TestDuplicateEventIdsAcrossRotatedPushLogs()
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateBasicServerRaid(
                    root,
                    "Regular",
                    "00112233445566778899ab11",
                    "00112233445566778899ab11",
                    true,
                    false,
                    "DUP-ROTATED");
                string push = BuildReadyPartyPush(1, "Usec", false, false);
                Write(directory, "synthetic push-notifications_000.log", push);
                Write(directory, "synthetic push-notifications_001.log", push);

                ServerSession session = Scan(root).Single();
                Assert(session.ParticipationType == TarkovParticipationType.Party,
                    "a duplicated rotated group-start event removed confirmed Party state");
                Assert(session.PartySize == 2,
                    "a duplicated rotated group-start event erased the final member count");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestInvalidPushPayloadFields()
        {
            string push = PushEvent("10:00:03.100", "GroupMatchInviteAccept",
                    InviteAcceptJson("bad-ready-nonbool", "member-bad", "notbool", "GroupMatchInviteAccept"))
                + PushEvent("10:00:03.200", "GroupMatchInviteAccept",
                    InviteAcceptJson("bad-event-id", "member-bad", "false", "GroupMatchRaidReady"))
                + PushEvent("10:00:03.300", "GroupMatchInviteAccept",
                    InviteAcceptJson("bad-aid", "member-bad", "false", "GroupMatchInviteAccept", true))
                + PushEvent("10:00:03.400", "GroupMatchInviteAccept",
                    InviteAcceptJson("invite-good", "member-good", "true", "GroupMatchInviteAccept", false))
                + PushEvent("10:00:03.500", "GroupMatchStartGame", StartJson("start"));
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899ab20",
                "00112233445566778899ab20",
                true,
                false,
                push,
                "INVALID-FIELDS");
            Assert(session.ParticipationType == TarkovParticipationType.Party,
                "valid party event was blocked by invalid payload fields");
            Assert(session.PartySize == 2,
                "only valid invite event should contribute one remote member");
        }

        private static void TestCancelledGenerationDoesNotLeak()
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateSessionDirectory(root);
                var application = new StringBuilder();
                application.Append(App("09:59:59.000", "Session mode: Regular"));
                application.Append(App("10:00:00.000",
                    "PrepareSelectedProfileLocally ProfileId:00112233445566778899aa50 AccountId:100"));
                application.Append(App("10:00:01.000", "Matching with group id: synthetic-group"));
                application.Append(App("10:00:02.000", "Network game matching cancelled."));
                application.Append(App("10:00:03.000",
                    "PrepareSelectedProfileLocally ProfileId:00112233445566778899aa60 AccountId:100"));
                application.Append(App("10:00:04.000", "Matching with group id:"));
                application.Append(ServerAssignment(
                    "10:00:06.000",
                    "00112233445566778899aa60",
                    "STALE-FINAL",
                    "FINAL"));
                Write(directory, "synthetic application_000.log", application.ToString());
                Write(directory, "synthetic backend_000.log",
                    Backend("10:00:04.500", "/client/match/join"));
                Write(directory, "synthetic push-notifications_000.log",
                    PushEvent("10:00:01.500", "GroupMatchRaidReady",
                        ReadyJson("stale-ready", "stale-member", "Usec", "Savage")));

                ServerSession session = Scan(root).Single();
                Assert(session.CharacterType == TarkovCharacterType.Pmc,
                    "cancelled generation leaked stale Savage evidence");
                Assert(session.ParticipationType == TarkovParticipationType.Solo,
                    "cancelled party generation leaked into the next solo raid");
                Assert(!session.PartySize.HasValue, "cancelled party size leaked");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestExpiredReadyStateDoesNotLeak()
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateSessionDirectory(root);
                var application = new StringBuilder();
                application.Append(App("09:59:00.000", "Session mode: Regular"));
                application.Append(App("10:03:00.000",
                    "Matching with group id: synthetic-new-group"));
                application.Append(ServerAssignment(
                    "10:03:30.000",
                    "00112233445566778899aa40",
                    "EXPIRED-READY",
                    "EXPIRED"));
                application.Append(App("10:04:00.000", "GameStarted:1 real:1 diff:0"));
                Write(directory, "synthetic application_000.log", application.ToString());
                Write(directory, "synthetic backend_000.log",
                    Backend("10:03:00.500", "/client/match/group/start_game"));
                Write(directory, "synthetic push-notifications_000.log",
                    PushEvent("10:00:00.000", "GroupMatchRaidReady",
                        ReadyJsonNumeric(
                            "expired-ready", 5000000002L, "Usec", "Savage"))
                    + PushEvent("10:03:01.000", "GroupMatchStartGame",
                        StartJson("new-start")));

                ServerSession session = Scan(root).Single();
                Assert(session.ParticipationType == TarkovParticipationType.Party
                        && !session.PartySize.HasValue,
                    "a stale Ready event must yield a non-numeric Party for the new generation");
                Assert(session.CharacterType == TarkovCharacterType.Unknown,
                    "a stale visual Side leaked into a new generation without direct role evidence");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestMultipleRaids()
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateSessionDirectory(root);
                var application = new StringBuilder();
                application.Append(App("09:59:59.000", "Session mode: Pve"));
                application.Append(App("10:00:00.000",
                    "PrepareSelectedProfileLocally ProfileId:00112233445566778899aa70 AccountId:100"));
                application.Append(App("10:00:01.000", "Matching with group id:"));
                application.Append(ServerAssignment(
                    "10:00:03.000", "00112233445566778899aa70", "MULTI-ONE", "ONE"));
                application.Append(App("10:00:04.000", "GameStarted:1 real:1 diff:0"));
                application.Append(App("11:00:00.000",
                    "PrepareSelectedProfileLocally ProfileId:00112233445566778899aaff AccountId:100"));
                application.Append(App("11:00:01.000", "Matching with group id:"));
                application.Append(ServerAssignment(
                    "11:00:03.000", "00112233445566778899ab00", "MULTI-TWO", "TWO"));
                Write(directory, "synthetic application_000.log", application.ToString());
                Write(directory, "synthetic backend_000.log",
                    Backend("10:00:01.500", "/client/match/join")
                    + Backend("11:00:01.500", "/client/match/join"));

                IList<ServerSession> sessions = Scan(root).OrderBy(item => item.DisplayDetectedAt).ToList();
                Assert(sessions.Count == 2, "two synthetic assignments did not produce two sessions");
                Assert(sessions[0].CharacterType == TarkovCharacterType.Pmc
                    && sessions[0].ParticipationType == TarkovParticipationType.Solo,
                    "first raid classification was lost");
                Assert(sessions[1].CharacterType == TarkovCharacterType.Scav
                    && sessions[1].ParticipationType == TarkovParticipationType.Solo,
                    "second raid reused the first raid state");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestPushCacheInvalidation()
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateBasicServerRaid(
                    root,
                    "Regular",
                    "00112233445566778899aa80",
                    "00112233445566778899aa80",
                    true,
                    false,
                    "CACHE");
                string pushPath = Path.Combine(directory, "synthetic push-notifications_000.log");
                Write(directory, "synthetic push-notifications_000.log", string.Empty);
                ServerSession cold = Scan(root).Single();
                Assert(cold.ParticipationType == TarkovParticipationType.Party
                    && !cold.PartySize.HasValue,
                    "cold scan should retain a non-numeric Party signal");

                File.WriteAllText(
                    pushPath,
                    BuildReadyPartyPush(1, "Usec", false, false),
                    new UTF8Encoding(false));
                File.SetLastWriteTimeUtc(pushPath, DateTime.UtcNow.AddSeconds(2));
                ServerSession warm = Scan(root).Single();
                Assert(warm.PartySize == 2,
                    "changed push file did not invalidate the directory cache");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestMalformedAndOversizedPush()
        {
            string malformed = PushMarker("10:00:03.000", "GroupMatchRaidReady")
                + "{\r\n  \"type\": \"GroupMatchRaidReady\",\r\n";
            string padding = new string('x', 270000);
            string oversized = PushMarker("10:00:03.100", "GroupMatchRaidReady")
                + "{\r\n"
                + "  \"type\": \"GroupMatchRaidReady\",\r\n"
                + "  \"eventId\": \"oversized\",\r\n"
                + "  \"padding\": \"" + padding + "\"\r\n"
                + "}\r\n";
            ServerSession session = ScanSingleServerRaid(
                "Regular",
                "00112233445566778899aa90",
                "00112233445566778899aa90",
                true,
                false,
                malformed + oversized,
                "BAD-PUSH");
            Assert(session.CharacterType == TarkovCharacterType.Pmc,
                "malformed push discarded valid ProfileId evidence");
            Assert(session.ParticipationType == TarkovParticipationType.Party
                && !session.PartySize.HasValue,
                "malformed or oversized event produced a guessed party size");
        }

        private static void TestOversizedPushFile()
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateBasicServerRaid(
                    root,
                    "Regular",
                    "00112233445566778899aaa0",
                    "00112233445566778899aaa0",
                    true,
                    false,
                    "BIG-FILE");
                string filler = new string('z', 4 * 1024 * 1024 + 1);
                Write(directory, "synthetic push-notifications_000.log",
                    filler + BuildReadyPartyPush(1, "Usec", false, false));
                ServerSession session = Scan(root).Single();
                Assert(session.ParticipationType == TarkovParticipationType.Party
                    && !session.PartySize.HasValue,
                    "an oversized push file was partially trusted");
                Assert(session.CharacterType == TarkovCharacterType.Pmc,
                    "oversized push file suppressed independent profile evidence");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestLocalPveUnknown()
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateSessionDirectory(root);
                string application = App("09:59:59.000", "Session mode: Pve")
                    + App("10:00:00.000",
                        "PrepareSelectedProfileLocally ProfileId:00112233445566778899aab0 AccountId:100")
                    + App("10:00:01.000", "MatchingCompleted:0 real:0 diff:0")
                    + App("10:00:01.500",
                        "scene preset path:maps/factory_day_preset.bundle rcid:factory_day.scenespreset.asset")
                    + App("10:00:02.000", "GameCreated:1 real:1 diff:0")
                    + App("10:00:03.000", "GameStarted:1 real:1 diff:0");
                Write(directory, "synthetic application_000.log", application);
                ServerSession session = Scan(root).Single();
                Assert(session.HostingMode == TarkovHostingMode.Local,
                    "fixture did not create a confirmed local PvE session");
                Assert(session.CharacterType == TarkovCharacterType.Unknown,
                    "local PvE without assignment guessed a character");
                Assert(session.ParticipationType == TarkovParticipationType.Unknown,
                    "local PvE without direct group/join evidence guessed Solo");
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static void TestLocalPveSoloPmc()
        {
            ServerSession session = ScanSingleServerRaid(
                "Pve",
                "00112233445566778899aab1",
                "00112233445566778899aab1",
                false,
                true,
                null,
                "PVE-LOCAL-PMC",
                false);
            Assert(session.HostingMode == TarkovHostingMode.Local,
                "fixture did not create a local PvE session");
            Assert(session.CharacterType == TarkovCharacterType.Unknown,
                "real-style local PvE logs without a raid ProfileId must not guess PMC");
            Assert(session.ParticipationType == TarkovParticipationType.Solo,
                "local PvE match/local/start should directly infer Solo");
            Assert(!session.PartySize.HasValue, "local solo must not expose party size");
        }

        private static void TestLocalPveSoloScav()
        {
            ServerSession session = ScanSingleServerRaid(
                "Pve",
                "00112233445566778899aab2",
                "00112233445566778899aab3",
                false,
                true,
                null,
                "PVE-LOCAL-SCAV",
                false);
            Assert(session.HostingMode == TarkovHostingMode.Local,
                "fixture did not create a local PvE session");
            Assert(session.CharacterType == TarkovCharacterType.Unknown,
                "real-style local PvE logs without a raid ProfileId must not guess Scav");
            Assert(session.ParticipationType == TarkovParticipationType.Solo,
                "local PvE match/local/start should directly infer Solo");
            Assert(!session.PartySize.HasValue, "local solo must not expose party size");
        }

        private static void TestNoTransientIdentifiersPersisted()
        {
            const string profile = "00112233445566778899aac0";
            const string member = "synthetic-member-private";
            string push = PushEvent("10:00:03.000", "GroupMatchRaidReady",
                    ReadyJson("privacy-ready", member, "Usec", "Usec"))
                + PushEvent("10:00:04.000", "GroupMatchStartGame", StartJson("privacy-start"));
            ServerSession session = ScanSingleServerRaid(
                "Regular", profile, profile, true, false, push, "PRIVACY");
            string publicProjection = string.Join("|", new[]
            {
                session.SessionKey,
                session.MapName,
                session.GameMode,
                session.RaidTypeAndParticipantText,
                session.ServerId,
                session.ShortId
            }.Where(item => item != null));
            Assert(publicProjection.IndexOf(profile, StringComparison.OrdinalIgnoreCase) < 0,
                "ProfileId persisted in the derived session");
            Assert(publicProjection.IndexOf(member, StringComparison.OrdinalIgnoreCase) < 0,
                "party aid persisted in the derived session");
        }

        private static ServerSession ScanSingleServerRaid(
            string mode,
            string baseProfile,
            string raidProfile,
            bool party,
            bool matchJoin,
            string push,
            string suffix,
            bool includeServerAssignment = true)
        {
            string root = CreateRoot();
            try
            {
                string directory = CreateBasicServerRaid(
                    root, mode, baseProfile, raidProfile, party, matchJoin, suffix,
                    includeServerAssignment);
                if (push != null)
                    Write(directory, "synthetic push-notifications_000.log", push);
                return Scan(root).Single();
            }
            finally
            {
                DeleteRoot(root);
            }
        }

        private static string CreateBasicServerRaid(
            string root,
            string mode,
            string baseProfile,
            string raidProfile,
            bool party,
            bool matchJoin,
            string suffix,
            bool includeServerAssignment = true)
        {
            string directory = CreateSessionDirectory(root);
            var applicationBuilder = new StringBuilder();
            applicationBuilder.Append(App("09:59:59.000", "Session mode: " + mode));
            applicationBuilder.Append(App("10:00:00.000",
                "PrepareSelectedProfileLocally ProfileId:" + baseProfile + " AccountId:100"));
            if (string.Equals(mode, "Pve", StringComparison.OrdinalIgnoreCase))
            {
                applicationBuilder.Append(App("10:00:01.000", "MatchingCompleted:0 real:0 diff:0"));
            }
            if (includeServerAssignment)
            {
                applicationBuilder.Append(App("10:00:02.000",
                    "Matching with group id:" + (party ? " synthetic-group" : string.Empty)));
            }
            if (string.Equals(mode, "Pve", StringComparison.OrdinalIgnoreCase))
            {
                applicationBuilder.Append(App("10:00:02.100",
                    "scene preset path:maps/factory_day_preset.bundle rcid:factory4_day"));
                applicationBuilder.Append(App("10:00:02.500", "GameCreated:1 real:1 diff:0"));
            }
            if (includeServerAssignment)
            {
                applicationBuilder.Append(ServerAssignment(
                    "10:00:05.000", raidProfile, "SID-" + suffix, "SHORT-" + suffix));
            }
            if (string.Equals(mode, "Pve", StringComparison.OrdinalIgnoreCase))
            {
                applicationBuilder.Append(App("10:00:06.000", "GameStarted:1 real:1 diff:0"));
            }
            string application = applicationBuilder.ToString();
            Write(directory, "synthetic application_000.log", application);
            if (party)
            {
                Write(directory, "synthetic backend_000.log",
                    Backend("10:00:03.000", "/client/match/group/start_game"));
            }
            else if (matchJoin)
            {
                Write(directory, "synthetic backend_000.log",
                    Backend(
                        "10:00:03.000",
                        includeServerAssignment
                            ? "/client/match/join"
                            : "/client/match/local/start"));
            }
            return directory;
        }

        private static string BuildReadyPartyPush(
            int remoteMembers,
            string visualSide,
            bool misleadingBaseSide,
            bool includeNotReady)
        {
            var result = new StringBuilder();
            for (int index = 0; index < remoteMembers; index++)
            {
                string id = "member-" + index;
                result.Append(PushEvent(
                    "10:00:03." + (100 + index).ToString("000"),
                    "GroupMatchRaidReady",
                    ReadyJson(
                        "ready-" + index,
                        id,
                        misleadingBaseSide ? "Usec" : visualSide,
                        visualSide)));
                if (includeNotReady)
                {
                    result.Append(PushEvent(
                        "10:00:03." + (500 + index).ToString("000"),
                        "GroupMatchRaidNotReady",
                        SimpleAidJson("GroupMatchRaidNotReady", "not-" + index, id)));
                }
            }
            result.Append(PushEvent("10:00:04.000", "GroupMatchStartGame", StartJson("start")));
            return result.ToString();
        }

        private static string ReadyJson(
            string eventId,
            string aid,
            string baseSide,
            string visualSide)
        {
            return "{\r\n"
                + "  \"type\": \"GroupMatchRaidReady\",\r\n"
                + "  \"eventId\": \"" + eventId + "\",\r\n"
                + "  \"extendedProfile\": {\r\n"
                + "    \"aid\": \"" + aid + "\",\r\n"
                + "    \"Info\": { \"Side\": \"" + baseSide + "\" },\r\n"
                + "    \"PlayerVisualRepresentation\": {\r\n"
                + "      \"Info\": { \"Side\": \"" + visualSide + "\" }\r\n"
                + "    }\r\n"
                + "  }\r\n"
                + "}";
        }

        private static string ReadyJsonNumeric(
            string eventId,
            long aid,
            string baseSide,
            string visualSide)
        {
            return "{\r\n"
                + "  \"type\": \"GroupMatchRaidReady\",\r\n"
                + "  \"eventId\": \"" + eventId + "\",\r\n"
                + "  \"extendedProfile\": {\r\n"
                + "    \"aid\": " + aid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\r\n"
                + "    \"Info\": { \"Side\": \"" + baseSide + "\" },\r\n"
                + "    \"PlayerVisualRepresentation\": {\r\n"
                + "      \"Info\": { \"Side\": \"" + visualSide + "\" }\r\n"
                + "    }\r\n"
                + "  }\r\n"
                + "}";
        }

        private static string InviteAcceptJson(
            string eventId,
            string aid,
            string isReady,
            string payloadType,
            bool invalidAid = false)
        {
            string aidValue = invalidAid
                ? "true"
                : "\"" + aid + "\"";

            return "{\r\n"
                + "  \"type\": \"" + payloadType + "\",\r\n"
                + "  \"eventId\": \"" + eventId + "\",\r\n"
                + "  \"aid\": " + aidValue + ",\r\n"
                + "  \"isReady\": " + isReady + "\r\n"
                + "}";
        }

        private static string InviteAcceptJsonNumeric(
            string eventId,
            long aid,
            bool isReady)
        {
            return "{\r\n"
                + "  \"type\": \"GroupMatchInviteAccept\",\r\n"
                + "  \"eventId\": \"" + eventId + "\",\r\n"
                + "  \"aid\": " + aid.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\r\n"
                + "  \"isReady\": " + (isReady ? "true" : "false") + "\r\n"
                + "}";
        }

        private static string SimpleAidJson(string type, string eventId, string aid)
        {
            return "{\r\n"
                + "  \"type\": \"" + type + "\",\r\n"
                + "  \"eventId\": \"" + eventId + "\",\r\n"
                + "  \"aid\": \"" + aid + "\"\r\n"
                + "}";
        }

        private static string SimpleAidJsonNumeric(
            string type,
            string eventId,
            long aid)
        {
            return "{\r\n"
                + "  \"type\": \"" + type + "\",\r\n"
                + "  \"eventId\": \"" + eventId + "\",\r\n"
                + "  \"aid\": " + aid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n"
                + "}";
        }

        private static string StartJson(string eventId)
        {
            return "{\r\n"
                + "  \"type\": \"GroupMatchStartGame\",\r\n"
                + "  \"eventId\": \"" + eventId + "\",\r\n"
                + "  \"groupId\": \"synthetic-group\",\r\n"
                + "  \"estimate\": 1\r\n"
                + "}";
        }

        private static string PushEvent(string time, string type, string json)
        {
            return PushMarker(time, type) + json + "\r\n";
        }

        private static string PushMarker(string time, string type)
        {
            return "2026.08.17 " + time
                + "|1.1.0.1.46777|Info|push-notifications|Got notification | "
                + type + "\r\n";
        }

        private static string App(string time, string message)
        {
            return "2026.08.17 " + time
                + "|1.1.0.1.46777|Info|application|" + message + "\r\n";
        }

        private static string Backend(string time, string route)
        {
            return "2026.08.17 " + time
                + "|1.1.0.1.46777|Info|backend|id [synthetic-request-" + (++_sequence)
                + "] : ---> Request HTTPS " + route + "\r\n";
        }

        private static string ServerAssignment(
            string time,
            string profile,
            string sid,
            string shortId)
        {
            return App(time,
                "TRACE-NetworkGameCreate profileStatus: 'Profileid:" + profile
                + ", Status: Busy, RaidMode: Online, Ip: 203.0.113.42, Port: 17000"
                + ", Location: factory4_day, Sid:" + sid
                + ", GameMode: deathmatch, shortId:" + shortId + "'");
        }

        private static IList<ServerSession> Scan(string root)
        {
            int folders;
            return RaidLogScanner.ScanGame(root, TarkovGame.Eft, 100, out folders);
        }

        private static string CreateRoot()
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "TarkovServerGuard-participant-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static string CreateSessionDirectory(string root)
        {
            string directory = Path.Combine(root, "synthetic-session-" + (++_sequence));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void Write(string directory, string name, string content)
        {
            File.WriteAllText(
                Path.Combine(directory, name),
                content ?? string.Empty,
                new UTF8Encoding(false));
        }

        private static void DeleteRoot(string root)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(root)
                    && root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(root))
                    Directory.Delete(root, true);
            }
            catch
            {
                // Test cleanup must not hide the assertion result.
            }
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("FAIL " + name + ": " + ex.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
