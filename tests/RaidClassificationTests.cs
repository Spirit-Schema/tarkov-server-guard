// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Linq;
using System.Reflection;

namespace TarkovServerReporter.Tests
{
    internal static class RaidClassificationTests
    {
        private static int Main()
        {
            try
            {
                TestDefaultsAndDisplayText();
                TestCharacterEvidenceCombination();
                TestParticipationEvidenceCombination();
                TestPartySizeValidationAndCombination();
                TestUnknownDoesNotOverwriteKnownValues();
                TestLegacyCacheClone();
                TestModelDoesNotRetainRawParticipantIdentifiers();
                Console.WriteLine("RaidClassificationTests: PASS");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("RaidClassificationTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void TestDefaultsAndDisplayText()
        {
            var session = new ServerSession
            {
                ProgressionMode = TarkovProgressionMode.PvpSeason,
                PvpSeasonNumber = 2
            };
            Assert(session.CharacterType == TarkovCharacterType.Unknown
                && session.ParticipationType == TarkovParticipationType.Unknown
                && !session.PartySize.HasValue,
                "new sessions use backward-compatible unknown classification defaults");
            Assert(session.CharacterTypeText == string.Empty
                && session.ParticipationTypeText == string.Empty
                && session.RaidTypeAndParticipantText == "PvP시즌2",
                "unknown character and participation values are omitted from display text");

            session.CharacterType = TarkovCharacterType.Scav;
            session.ParticipationType = TarkovParticipationType.Party;
            session.PartySize = 3;
            Assert(session.CharacterTypeText == "스캐브"
                && session.ParticipationTypeText == "3인 파티"
                && session.RaidTypeAndParticipantText == "PvP시즌2 · 스캐브 · 3인 파티",
                "display order is game type, character, then participation");

            session.PartySize = null;
            Assert(session.RaidTypeAndParticipantText == "PvP시즌2 · 스캐브 · 파티",
                "a known party with unknown size safely falls back to party");

            session.CharacterType = TarkovCharacterType.Unknown;
            session.ParticipationType = TarkovParticipationType.Solo;
            Assert(session.RaidTypeAndParticipantText == "PvP시즌2 · 솔로",
                "only the unknown segment is omitted");
        }

        private static void TestCharacterEvidenceCombination()
        {
            var session = new ServerSession();
            RaidClassificationModel.AddCharacterEvidence(
                session,
                TarkovCharacterType.Pmc,
                RaidCharacterEvidence.ProfileIdRelation);
            RaidClassificationModel.AddCharacterEvidence(
                session,
                TarkovCharacterType.Pmc,
                RaidCharacterEvidence.PlayerVisualSide);
            Assert(session.CharacterType == TarkovCharacterType.Pmc
                && (session.CharacterEvidence & RaidCharacterEvidence.ProfileIdRelation) != 0
                && (session.CharacterEvidence & RaidCharacterEvidence.PlayerVisualSide) != 0
                && (session.CharacterEvidence & RaidCharacterEvidence.Conflict) == 0,
                "agreeing profile and visual evidence is combined without losing provenance");

            RaidClassificationModel.AddCharacterEvidence(
                session,
                TarkovCharacterType.Scav,
                RaidCharacterEvidence.PlayerVisualSide);
            Assert(session.CharacterType == TarkovCharacterType.Unknown
                && (session.CharacterEvidence & RaidCharacterEvidence.Conflict) != 0,
                "conflicting character evidence resolves to unknown");

            RaidClassificationModel.AddCharacterEvidence(
                session,
                TarkovCharacterType.Pmc,
                RaidCharacterEvidence.ProfileIdRelation);
            Assert(session.CharacterType == TarkovCharacterType.Unknown,
                "a character conflict remains sticky after later matching evidence");
        }

        private static void TestParticipationEvidenceCombination()
        {
            var solo = new ServerSession();
            RaidClassificationModel.AddParticipationEvidence(
                solo,
                RaidParticipationEvidence.MatchJoin);
            Assert(solo.ParticipationType == TarkovParticipationType.Unknown,
                "match/join alone does not guess solo");
            RaidClassificationModel.AddParticipationEvidence(
                solo,
                RaidParticipationEvidence.EmptyGroupId);
            Assert(solo.ParticipationType == TarkovParticipationType.Solo,
                "match/join and an empty group id together confirm solo");

            RaidClassificationModel.AddParticipationEvidence(
                solo,
                RaidParticipationEvidence.GroupStartRoute);
            Assert(solo.ParticipationType == TarkovParticipationType.Unknown
                && (solo.ParticipationEvidence & RaidParticipationEvidence.Conflict) != 0,
                "conflicting solo and party evidence resolves to unknown");

            var party = new ServerSession();
            RaidClassificationModel.AddParticipationEvidence(
                party,
                RaidParticipationEvidence.GroupStartEvent);
            Assert(party.ParticipationType == TarkovParticipationType.Party
                && party.ParticipationTypeText == "파티",
                "a group start confirms party even without a member count");
        }

        private static void TestPartySizeValidationAndCombination()
        {
            for (int size = 2; size <= 5; size++)
            {
                var session = new ServerSession();
                RaidClassificationModel.AddPartySizeEvidence(session, size);
                Assert(session.ParticipationType == TarkovParticipationType.Party
                    && session.PartySize == size
                    && session.ParticipationTypeText == size + "인 파티",
                    "party sizes from two through five are accepted");
            }

            foreach (int invalidSize in new[] { -1, 0, 1, 6 })
            {
                var invalid = new ServerSession();
                RaidClassificationModel.AddPartySizeEvidence(invalid, invalidSize);
                Assert(invalid.ParticipationType == TarkovParticipationType.Party
                    && !invalid.PartySize.HasValue
                    && invalid.ParticipationTypeText == "파티"
                    && (invalid.PartySizeEvidence & RaidPartySizeEvidence.Invalid) != 0,
                    "invalid party sizes are not displayed or corrected heuristically");
            }

            var first = new ServerSession();
            var duplicate = new ServerSession();
            RaidClassificationModel.AddPartySizeEvidence(first, 2);
            RaidClassificationModel.AddPartySizeEvidence(duplicate, 3);
            RaidClassificationModel.MergeInto(first, duplicate);
            Assert(first.ParticipationType == TarkovParticipationType.Party
                && !first.PartySize.HasValue
                && first.ParticipationTypeText == "파티"
                && (first.PartySizeEvidence & RaidPartySizeEvidence.Conflict) != 0,
                "different final member counts retain party but suppress the number");
        }

        private static void TestUnknownDoesNotOverwriteKnownValues()
        {
            var known = new ServerSession();
            RaidClassificationModel.AddCharacterEvidence(
                known,
                TarkovCharacterType.Scav,
                RaidCharacterEvidence.ProfileIdRelation);
            RaidClassificationModel.AddPartySizeEvidence(known, 4);
            RaidClassificationModel.MergeInto(known, new ServerSession());
            Assert(known.CharacterType == TarkovCharacterType.Scav
                && known.ParticipationType == TarkovParticipationType.Party
                && known.PartySize == 4,
                "an unknown duplicate cannot overwrite confirmed classification");

            var conflicting = new ServerSession();
            RaidClassificationModel.AddCharacterEvidence(
                conflicting,
                TarkovCharacterType.Pmc,
                RaidCharacterEvidence.PlayerVisualSide);
            RaidClassificationModel.MergeInto(known, conflicting);
            Assert(known.CharacterType == TarkovCharacterType.Unknown
                && (known.CharacterEvidence & RaidCharacterEvidence.Conflict) != 0,
                "different known character values are not silently selected during merge");
        }

        private static void TestLegacyCacheClone()
        {
            var source = new ServerSession
            {
                CharacterType = TarkovCharacterType.Pmc,
                ParticipationType = TarkovParticipationType.Party,
                PartySize = 5,
                CharacterEvidence = RaidCharacterEvidence.Pmc
                    | RaidCharacterEvidence.ProfileIdRelation,
                ParticipationEvidence = RaidParticipationEvidence.Party
                    | RaidParticipationEvidence.GroupStartEvent,
                PartySizeEvidence = RaidPartySizeEvidence.FinalGroupState
            };
            MethodInfo cloneMethod = typeof(LogScanner).GetMethod(
                "CloneSession",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(cloneMethod != null, "legacy cache clone helper is available");
            var clone = (ServerSession)cloneMethod.Invoke(null, new object[] { source });
            Assert(clone.CharacterType == source.CharacterType
                && clone.ParticipationType == source.ParticipationType
                && clone.PartySize == source.PartySize
                && clone.CharacterEvidence == source.CharacterEvidence
                && clone.ParticipationEvidence == source.ParticipationEvidence
                && clone.PartySizeEvidence == source.PartySizeEvidence,
                "legacy in-memory cache cloning preserves classification atomically");
        }

        private static void TestModelDoesNotRetainRawParticipantIdentifiers()
        {
            string[] prohibitedNames = { "ProfileId", "Aid", "AccountId", "Nickname" };
            string[] propertyNames = typeof(ServerSession)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(property => property.Name)
                .ToArray();
            Assert(!propertyNames.Any(name => prohibitedNames.Any(prohibited =>
                    string.Equals(name, prohibited, StringComparison.OrdinalIgnoreCase))),
                "the session model stores only derived classification, never raw participant identifiers");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
