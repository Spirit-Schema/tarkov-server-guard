// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;

[assembly: System.Reflection.AssemblyVersion("0.8.0.0")]

namespace TarkovServerReporter
{
    internal static class BuildIdentityTests
    {
        private static int _failures;

        private static void Main()
        {
            Run("compiled public identity is consistent", TestCompiledIdentity);
            Run("valid public evidence is consistent", TestValidEvidence);
            Run("Binary Build ID uses all four public components", TestBinaryBuildIdComponents);
            Run("generated constant mismatch is diagnosed", TestCodeMismatch);
            Run("assembly metadata mismatch is diagnosed", TestMetadataMismatch);
            Run("duplicate assembly metadata is diagnosed", TestDuplicateMetadata);
            Run("embedded JSON changes are diagnosed", TestManifestMismatch);
            Run("malformed evidence remains non-blocking", TestMalformedEvidence);
            Run("tooltip exposes provenance and trust boundary", TestDisplayText);
            Run("archive tooltip retains twelve hash digits", TestArchiveDisplay);

            if (_failures != 0)
            {
                Console.Error.WriteLine(_failures + " build identity test(s) failed.");
                Environment.Exit(1);
            }

            Console.WriteLine("All public build identity tests passed.");
        }

        private static void TestCompiledIdentity()
        {
            BuildIdentityInfo current = BuildIdentity.Current;
            Assert(current.EvidenceConsistent,
                "Compiled provenance was inconsistent: " + current.DiagnosticCode);
            Assert(current.ApplicationVersion == "0.8.0", "Target build did not identify as v0.8.0.");
            Assert(current.Channel == "development" || current.Channel == "release",
                "Compiled build had an unknown explicit channel.");
            Assert(current.BinaryBuildId.StartsWith("tsg-bin-v1-", StringComparison.Ordinal),
                "Compiled build did not expose the public Binary Build ID scheme.");
            Assert(current.BinaryBuildId.Length == 75, "Compiled Binary Build ID was not complete.");
            Assert(current.BuildInputManifestSha256.Length == 64,
                "Compiled canonical input manifest hash was not complete.");
        }

        private static void TestValidEvidence()
        {
            BuildIdentityInfo development = BuildIdentity.Evaluate(CreateEvidence("development"));
            Assert(development.EvidenceConsistent,
                "Valid development evidence was rejected: " + development.DiagnosticCode);

            BuildIdentityInfo release = BuildIdentity.Evaluate(CreateEvidence("release"));
            Assert(release.EvidenceConsistent,
                "Valid release evidence was rejected: " + release.DiagnosticCode);
            Assert(release.Channel == "release", "Release channel was not retained.");
        }

        private static void TestBinaryBuildIdComponents()
        {
            const string version = "0.8.0";
            string revision = Repeat('a', 40);
            string inputHash = Repeat('4', 64);
            string baseline = BuildIdentity.ComputeBinaryBuildId(
                version,
                revision,
                "development",
                inputHash);
            Assert(baseline != BuildIdentity.ComputeBinaryBuildId(
                "0.8.1", revision, "development", inputHash),
                "Version was not part of the Binary Build ID.");
            Assert(baseline != BuildIdentity.ComputeBinaryBuildId(
                version, Repeat('b', 40), "development", inputHash),
                "Exact revision was not part of the Binary Build ID.");
            Assert(baseline != BuildIdentity.ComputeBinaryBuildId(
                version, revision, "release", inputHash),
                "Channel was not part of the Binary Build ID.");
            Assert(baseline != BuildIdentity.ComputeBinaryBuildId(
                version, revision, "development", Repeat('5', 64)),
                "Canonical build-input manifest hash was not part of the Binary Build ID.");
        }

        private static void TestCodeMismatch()
        {
            BuildIdentityEvidence evidence = CreateEvidence("development");
            evidence.CodeBinaryBuildId = "tsg-bin-v1-" + Repeat('f', 64);
            BuildIdentityInfo info = BuildIdentity.Evaluate(evidence);
            Assert(!info.EvidenceConsistent && info.DiagnosticCode == "code-binary-id-mismatch",
                "A generated constant mismatch was not diagnosed.");
        }

        private static void TestMetadataMismatch()
        {
            BuildIdentityEvidence evidence = CreateEvidence("development");
            evidence.MetadataEntries[5] = new BuildIdentityMetadataEntry(
                "TSG.BinaryBuildId",
                "tsg-bin-v1-" + Repeat('f', 64));
            BuildIdentityInfo info = BuildIdentity.Evaluate(evidence);
            Assert(!info.EvidenceConsistent && info.DiagnosticCode == "metadata-value-mismatch",
                "A readable assembly metadata mismatch was not diagnosed.");
        }

        private static void TestDuplicateMetadata()
        {
            BuildIdentityEvidence evidence = CreateEvidence("development");
            evidence.MetadataEntries.Add(new BuildIdentityMetadataEntry(
                "TSG.BuildChannel",
                "development"));
            BuildIdentityInfo info = BuildIdentity.Evaluate(evidence);
            Assert(!info.EvidenceConsistent && info.DiagnosticCode == "metadata-count-mismatch",
                "Duplicate assembly metadata was accepted.");
        }

        private static void TestManifestMismatch()
        {
            BuildIdentityEvidence evidence = CreateEvidence("development");
            evidence.ManifestJson = evidence.ManifestJson.Replace(
                "\"channel\":\"development\"",
                "\"channel\":\"release\"");
            BuildIdentityInfo changed = BuildIdentity.Evaluate(evidence);
            Assert(!changed.EvidenceConsistent && changed.DiagnosticCode == "binary-build-id-invalid",
                "An embedded JSON component change was not diagnosed.");

            evidence = CreateEvidence("development");
            evidence.ManifestJson = evidence.ManifestJson.Substring(
                0,
                evidence.ManifestJson.Length - 1) + ",\"unexpected\":true}";
            BuildIdentityInfo extended = BuildIdentity.Evaluate(evidence);
            Assert(!extended.EvidenceConsistent && extended.DiagnosticCode == "manifest-invalid",
                "An undocumented embedded JSON field was accepted.");
        }

        private static void TestMalformedEvidence()
        {
            BuildIdentityInfo missing = BuildIdentity.Evaluate(null);
            Assert(!missing.EvidenceConsistent && missing.DiagnosticCode == "evidence-missing",
                "Missing evidence was not returned as a diagnostic result.");

            BuildIdentityEvidence malformed = CreateEvidence("development");
            malformed.ManifestJson = "{not-json";
            BuildIdentityInfo invalid = BuildIdentity.Evaluate(malformed);
            Assert(!invalid.EvidenceConsistent && invalid.DiagnosticCode == "manifest-invalid",
                "Malformed JSON did not return a non-throwing diagnostic result.");
        }

        private static void TestDisplayText()
        {
            string text = BuildIdentity.Current.ToDisplayText();
            Assert(text.Contains("버전: v0.8.0"), "Tooltip omitted the application version.");
            Assert(text.Contains("채널:"), "Tooltip omitted the explicit channel.");
            Assert(text.Contains("소스:"), "Tooltip omitted the short source revision.");
            Assert(text.Contains("Binary Build ID:"), "Tooltip omitted the Binary Build ID.");
            Assert(text.Contains("빌드 입력 manifest:"), "Tooltip omitted the canonical input hash.");
            Assert(text.Contains("코드 서명"), "Tooltip omitted the code-signing limitation.");
            Assert(!text.Contains("인증됨"), "Tooltip made an authentication claim.");
        }

        private static void TestArchiveDisplay()
        {
            var info = new BuildIdentityInfo(
                "development",
                "tsg-bin-v1-" + Repeat('b', 64),
                "0.8.0",
                "tree-" + Repeat('a', 64),
                Repeat('4', 64),
                true,
                "consistent");
            string text = info.ToDisplayText();
            Assert(text.Contains("tree-" + Repeat('a', 12)),
                "Archive display did not retain twelve hash digits after the prefix.");
            Assert(!text.Contains("tree-" + Repeat('a', 13)),
                "Archive display retained more than the intended revision summary.");
        }

        private static BuildIdentityEvidence CreateEvidence(string channel)
        {
            const string version = "0.8.0";
            string revision = Repeat('a', 40);
            string inputHash = Repeat('4', 64);
            string binaryBuildId = BuildIdentity.ComputeBinaryBuildId(
                version,
                revision,
                channel,
                inputHash);
            string json = string.Format(
                "{{\"schemaVersion\":1,\"identityScheme\":\"tsg-binary-build-v1\",\"applicationVersion\":\"{0}\",\"sourceRevision\":\"{1}\",\"channel\":\"{2}\",\"buildInputManifestSha256\":\"{3}\",\"binaryBuildId\":\"{4}\"}}",
                version,
                revision,
                channel,
                inputHash,
                binaryBuildId);

            return new BuildIdentityEvidence
            {
                ManifestJson = json,
                CodeSchemaVersion = 1,
                CodeIdentityScheme = "tsg-binary-build-v1",
                CodeApplicationVersion = version,
                CodeSourceRevision = revision,
                CodeChannel = channel,
                CodeBuildInputManifestSha256 = inputHash,
                CodeBinaryBuildId = binaryBuildId,
                AssemblyApplicationVersion = version,
                MetadataEntries = new List<BuildIdentityMetadataEntry>
                {
                    new BuildIdentityMetadataEntry("TSG.IdentityScheme", "tsg-binary-build-v1"),
                    new BuildIdentityMetadataEntry("TSG.ApplicationVersion", version),
                    new BuildIdentityMetadataEntry("TSG.SourceRevision", revision),
                    new BuildIdentityMetadataEntry("TSG.BuildChannel", channel),
                    new BuildIdentityMetadataEntry("TSG.BuildInputManifestSha256", inputHash),
                    new BuildIdentityMetadataEntry("TSG.BinaryBuildId", binaryBuildId)
                }
            };
        }

        private static string Repeat(char value, int count)
        {
            return new string(value, count);
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                Console.WriteLine("PASS: " + name);
            }
            catch (Exception exception)
            {
                _failures++;
                Console.Error.WriteLine("FAIL: " + name + " - " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
