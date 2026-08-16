// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    internal sealed class BuildIdentityMetadataEntry
    {
        internal BuildIdentityMetadataEntry(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }

        internal string Key { get; private set; }
        internal string Value { get; private set; }
    }

    internal sealed class BuildIdentityEvidence
    {
        internal string ManifestJson { get; set; }
        internal int CodeSchemaVersion { get; set; }
        internal string CodeIdentityScheme { get; set; }
        internal string CodeApplicationVersion { get; set; }
        internal string CodeSourceRevision { get; set; }
        internal string CodeChannel { get; set; }
        internal string CodeBuildInputManifestSha256 { get; set; }
        internal string CodeBinaryBuildId { get; set; }
        internal string AssemblyApplicationVersion { get; set; }
        internal IList<BuildIdentityMetadataEntry> MetadataEntries { get; set; }
    }

    internal sealed class BuildIdentityInfo
    {
        internal BuildIdentityInfo(
            string channel,
            string binaryBuildId,
            string applicationVersion,
            string sourceRevision,
            string buildInputManifestSha256,
            bool evidenceConsistent,
            string diagnosticCode)
        {
            Channel = channel ?? "unknown";
            BinaryBuildId = binaryBuildId ?? "unavailable";
            ApplicationVersion = applicationVersion ?? string.Empty;
            SourceRevision = sourceRevision ?? "unavailable";
            BuildInputManifestSha256 = buildInputManifestSha256 ?? "unavailable";
            EvidenceConsistent = evidenceConsistent;
            DiagnosticCode = diagnosticCode ?? "unknown";
        }

        internal string Channel { get; private set; }
        internal string BinaryBuildId { get; private set; }
        internal string ApplicationVersion { get; private set; }
        internal string SourceRevision { get; private set; }
        internal string BuildInputManifestSha256 { get; private set; }
        internal bool EvidenceConsistent { get; private set; }
        internal string DiagnosticCode { get; private set; }

        internal string ToDisplayText()
        {
            string revision = ShortRevision(SourceRevision);
            string inputHash = ShortHash(BuildInputManifestSha256);
            string diagnostic = EvidenceConsistent
                ? "공개 출처 정보 일치"
                : "공개 출처 정보 불일치 (" + DiagnosticCode + ")";
            return string.Format(
                "버전: v{0}\r\n채널: {1}\r\n소스: {2}\r\nBinary Build ID: {3}\r\n빌드 입력 manifest: {4}\r\n진단: {5}\r\n이 정보는 코드 서명이나 공식 배포본 인증을 대신하지 않습니다.",
                ApplicationVersion,
                Channel,
                revision,
                BinaryBuildId,
                inputHash,
                diagnostic);
        }

        private static string ShortRevision(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unavailable";
            if (value.StartsWith("tree-", StringComparison.Ordinal))
            {
                string hash = value.Substring(5);
                return "tree-" + (hash.Length > 12 ? hash.Substring(0, 12) : hash);
            }
            return value.Length > 12 ? value.Substring(0, 12) : value;
        }

        private static string ShortHash(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unavailable";
            return value.Length > 12 ? value.Substring(0, 12) : value;
        }
    }

    internal static class BuildIdentity
    {
        private const string ResourceName = "TarkovServerReporter.BuildIdentity.json";
        private const string IdentityScheme = "tsg-binary-build-v1";
        private const int MaximumManifestBytes = 16384;

        private static readonly string[] MetadataKeys =
        {
            "TSG.IdentityScheme",
            "TSG.ApplicationVersion",
            "TSG.SourceRevision",
            "TSG.BuildChannel",
            "TSG.BuildInputManifestSha256",
            "TSG.BinaryBuildId"
        };

        private static readonly Regex VersionPattern = new Regex(
            "^\\d+\\.\\d+\\.\\d+$",
            RegexOptions.CultureInvariant);
        private static readonly Regex RevisionPattern = new Regex(
            "^(?:[0-9a-f]{40}|[0-9a-f]{64}|tree-[0-9a-f]{64})$",
            RegexOptions.CultureInvariant);
        private static readonly Regex HashPattern = new Regex(
            "^[0-9a-f]{64}$",
            RegexOptions.CultureInvariant);
        private static readonly Regex BinaryBuildIdPattern = new Regex(
            "^tsg-bin-v1-[0-9a-f]{64}$",
            RegexOptions.CultureInvariant);

        private static readonly BuildIdentityInfo CurrentValue = InspectAssembly(
            typeof(BuildIdentity).Assembly);

        internal static BuildIdentityInfo Current
        {
            get { return CurrentValue; }
        }

        internal static BuildIdentityInfo Evaluate(BuildIdentityEvidence evidence)
        {
            if (evidence == null)
                return Invalid("evidence-missing", null);

            BuildIdentityManifest manifest;
            if (!TryReadManifest(evidence.ManifestJson, out manifest))
                return Invalid("manifest-invalid", evidence);

            string diagnostic = ValidateManifest(manifest, evidence);
            return new BuildIdentityInfo(
                manifest.Channel,
                manifest.BinaryBuildId,
                manifest.ApplicationVersion,
                manifest.SourceRevision,
                manifest.BuildInputManifestSha256,
                string.Equals(diagnostic, "consistent", StringComparison.Ordinal),
                diagnostic);
        }

        internal static string ComputeBinaryBuildId(
            string version,
            string revision,
            string channel,
            string buildInputManifestSha256)
        {
            string material = string.Join("\n", new[]
            {
                "schema=" + IdentityScheme,
                "version=" + version,
                "revision=" + revision,
                "channel=" + channel,
                "buildInputsSha256=" + buildInputManifestSha256
            });
            return "tsg-bin-v1-" + Sha256Hex(material);
        }

        private static BuildIdentityInfo InspectAssembly(Assembly assembly)
        {
            try
            {
                var metadata = new List<BuildIdentityMetadataEntry>();
                object[] attributes = assembly.GetCustomAttributes(
                    typeof(AssemblyMetadataAttribute),
                    false);
                foreach (object item in attributes)
                {
                    var attribute = item as AssemblyMetadataAttribute;
                    if (attribute != null)
                        metadata.Add(new BuildIdentityMetadataEntry(attribute.Key, attribute.Value));
                }

                return Evaluate(new BuildIdentityEvidence
                {
                    ManifestJson = ReadManifestResource(assembly),
                    CodeSchemaVersion = GeneratedBuildIdentity.SchemaVersion,
                    CodeIdentityScheme = GeneratedBuildIdentity.IdentityScheme,
                    CodeApplicationVersion = GeneratedBuildIdentity.ApplicationVersion,
                    CodeSourceRevision = GeneratedBuildIdentity.SourceRevision,
                    CodeChannel = GeneratedBuildIdentity.Channel,
                    CodeBuildInputManifestSha256 = GeneratedBuildIdentity.BuildInputManifestSha256,
                    CodeBinaryBuildId = GeneratedBuildIdentity.BinaryBuildId,
                    AssemblyApplicationVersion = GetApplicationVersion(assembly),
                    MetadataEntries = metadata
                });
            }
            catch
            {
                // Public build identity is diagnostic evidence only. Inspection
                // failure must never prevent startup or any app operation.
                return Invalid("inspection-failed", null);
            }
        }

        private static string ReadManifestResource(Assembly assembly)
        {
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null || stream.Length > MaximumManifestBytes)
                    return null;
                using (var reader = new StreamReader(
                    stream,
                    new UTF8Encoding(false, true),
                    true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static bool TryReadManifest(string json, out BuildIdentityManifest manifest)
        {
            manifest = null;
            if (string.IsNullOrWhiteSpace(json)
                || Encoding.UTF8.GetByteCount(json) > MaximumManifestBytes)
                return false;

            try
            {
                var serializer = new JavaScriptSerializer
                {
                    MaxJsonLength = MaximumManifestBytes,
                    RecursionLimit = 8
                };
                var document = serializer.DeserializeObject(json) as IDictionary<string, object>;
                if (document == null || document.Count != 7)
                    return false;
                foreach (string property in new[]
                {
                    "schemaVersion",
                    "identityScheme",
                    "applicationVersion",
                    "sourceRevision",
                    "channel",
                    "buildInputManifestSha256",
                    "binaryBuildId"
                })
                {
                    if (!document.ContainsKey(property)) return false;
                }
                manifest = serializer.Deserialize<BuildIdentityManifest>(json);
                return manifest != null;
            }
            catch
            {
                manifest = null;
                return false;
            }
        }

        private static string ValidateManifest(
            BuildIdentityManifest manifest,
            BuildIdentityEvidence evidence)
        {
            if (manifest.SchemaVersion != 1
                || !string.Equals(manifest.IdentityScheme, IdentityScheme, StringComparison.Ordinal)
                || !VersionPattern.IsMatch(manifest.ApplicationVersion ?? string.Empty)
                || !RevisionPattern.IsMatch(manifest.SourceRevision ?? string.Empty)
                || !HashPattern.IsMatch(manifest.BuildInputManifestSha256 ?? string.Empty)
                || !BinaryBuildIdPattern.IsMatch(manifest.BinaryBuildId ?? string.Empty)
                || !(string.Equals(manifest.Channel, "development", StringComparison.Ordinal)
                    || string.Equals(manifest.Channel, "release", StringComparison.Ordinal)))
                return "manifest-values-invalid";

            string expectedId = ComputeBinaryBuildId(
                manifest.ApplicationVersion,
                manifest.SourceRevision,
                manifest.Channel,
                manifest.BuildInputManifestSha256);
            if (!string.Equals(manifest.BinaryBuildId, expectedId, StringComparison.Ordinal))
                return "binary-build-id-invalid";

            if (evidence.CodeSchemaVersion != manifest.SchemaVersion)
                return "code-schema-mismatch";
            if (!string.Equals(evidence.CodeIdentityScheme, manifest.IdentityScheme, StringComparison.Ordinal))
                return "code-scheme-mismatch";
            if (!string.Equals(evidence.CodeApplicationVersion, manifest.ApplicationVersion, StringComparison.Ordinal))
                return "code-version-mismatch";
            if (!string.Equals(evidence.CodeSourceRevision, manifest.SourceRevision, StringComparison.Ordinal))
                return "code-revision-mismatch";
            if (!string.Equals(evidence.CodeChannel, manifest.Channel, StringComparison.Ordinal))
                return "code-channel-mismatch";
            if (!string.Equals(
                evidence.CodeBuildInputManifestSha256,
                manifest.BuildInputManifestSha256,
                StringComparison.Ordinal))
                return "code-input-manifest-mismatch";
            if (!string.Equals(evidence.CodeBinaryBuildId, manifest.BinaryBuildId, StringComparison.Ordinal))
                return "code-binary-id-mismatch";
            if (!string.Equals(
                evidence.AssemblyApplicationVersion,
                manifest.ApplicationVersion,
                StringComparison.Ordinal))
                return "assembly-version-mismatch";

            var expectedMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { MetadataKeys[0], manifest.IdentityScheme },
                { MetadataKeys[1], manifest.ApplicationVersion },
                { MetadataKeys[2], manifest.SourceRevision },
                { MetadataKeys[3], manifest.Channel },
                { MetadataKeys[4], manifest.BuildInputManifestSha256 },
                { MetadataKeys[5], manifest.BinaryBuildId }
            };
            foreach (KeyValuePair<string, string> expected in expectedMetadata)
            {
                int matches = 0;
                bool valueMatches = false;
                foreach (BuildIdentityMetadataEntry entry in evidence.MetadataEntries
                    ?? new List<BuildIdentityMetadataEntry>())
                {
                    if (!string.Equals(entry.Key, expected.Key, StringComparison.Ordinal)) continue;
                    matches++;
                    valueMatches = string.Equals(entry.Value, expected.Value, StringComparison.Ordinal);
                }
                if (matches != 1) return "metadata-count-mismatch";
                if (!valueMatches) return "metadata-value-mismatch";
            }

            return "consistent";
        }

        private static string GetApplicationVersion(Assembly assembly)
        {
            Version version = assembly.GetName().Version;
            if (version == null) return null;
            return string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
        }

        private static string Sha256Hex(string value)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest) builder.Append(item.ToString("x2"));
                return builder.ToString();
            }
        }

        private static BuildIdentityInfo Invalid(
            string diagnosticCode,
            BuildIdentityEvidence evidence)
        {
            return new BuildIdentityInfo(
                evidence == null ? null : evidence.CodeChannel,
                evidence == null ? null : evidence.CodeBinaryBuildId,
                evidence == null ? null : evidence.CodeApplicationVersion,
                evidence == null ? null : evidence.CodeSourceRevision,
                evidence == null ? null : evidence.CodeBuildInputManifestSha256,
                false,
                diagnosticCode);
        }

        private sealed class BuildIdentityManifest
        {
            public int SchemaVersion { get; set; }
            public string IdentityScheme { get; set; }
            public string ApplicationVersion { get; set; }
            public string SourceRevision { get; set; }
            public string Channel { get; set; }
            public string BuildInputManifestSha256 { get; set; }
            public string BinaryBuildId { get; set; }
        }
    }
}
