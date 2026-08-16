// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TarkovServerReporter.Tests
{
    internal static class BuildIdentityAppInspector
    {
        private const BindingFlags InternalInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static int Main(string[] args)
        {
            try
            {
                if (args == null
                    || (args.Length != 3 && args.Length != 6)
                    || !File.Exists(args[0]))
                {
                    throw new InvalidOperationException(
                        "Usage: BuildIdentityAppInspector.exe <app.exe> <version> "
                        + "<development|release> [<binary-build-id> <revision> <build-input-hash>]");
                }
                Assembly application = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                AssertAssembly(
                    application,
                    args[1],
                    args[2],
                    args.Length == 6 ? args[3] : null,
                    args.Length == 6 ? args[4] : null,
                    args.Length == 6 ? args[5] : null);
                Console.WriteLine("PASS: actual app public build identity (" + args[2] + ")");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: actual app public build identity");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        internal static void AssertAssembly(
            Assembly application,
            string expectedVersion,
            string expectedChannel)
        {
            AssertAssembly(
                application,
                expectedVersion,
                expectedChannel,
                null,
                null,
                null);
        }

        internal static void AssertAssembly(
            Assembly application,
            string expectedVersion,
            string expectedChannel,
            string expectedBinaryBuildId,
            string expectedRevision,
            string expectedBuildInputManifestSha256)
        {
            if (application == null) throw new ArgumentNullException("application");
            Type identityType = application.GetType(
                "TarkovServerReporter.BuildIdentity",
                true);
            PropertyInfo currentProperty = identityType.GetProperty(
                "Current",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert(currentProperty != null, "BuildIdentity.Current was not found.");
            object current = currentProperty.GetValue(null, null);
            Assert(current != null, "BuildIdentity.Current returned null.");

            bool consistent = GetProperty<bool>(current, "EvidenceConsistent");
            string diagnostic = GetProperty<string>(current, "DiagnosticCode");
            string version = GetProperty<string>(current, "ApplicationVersion");
            string channel = GetProperty<string>(current, "Channel");
            string revision = GetProperty<string>(current, "SourceRevision");
            string inputHash = GetProperty<string>(current, "BuildInputManifestSha256");
            string binaryBuildId = GetProperty<string>(current, "BinaryBuildId");

            Assert(consistent, "Actual app provenance was inconsistent: " + diagnostic);
            Assert(string.Equals(version, expectedVersion, StringComparison.Ordinal),
                "Actual app version did not match the build request.");
            Assert(string.Equals(channel, expectedChannel, StringComparison.Ordinal),
                "Actual app channel did not match the build request.");
            Assert(Regex.IsMatch(
                    revision ?? string.Empty,
                    "^(?:[0-9a-f]{40}|[0-9a-f]{64}|tree-[0-9a-f]{64})$",
                    RegexOptions.CultureInvariant),
                "Actual app source revision was not complete.");
            Assert(Regex.IsMatch(
                    inputHash ?? string.Empty,
                    "^[0-9a-f]{64}$",
                    RegexOptions.CultureInvariant),
                "Actual app build-input manifest hash was not complete.");
            Assert(Regex.IsMatch(
                    binaryBuildId ?? string.Empty,
                    "^tsg-bin-v1-[0-9a-f]{64}$",
                    RegexOptions.CultureInvariant),
                "Actual app Binary Build ID was not complete.");

            bool hasExternalExpectation = !string.IsNullOrWhiteSpace(expectedBinaryBuildId)
                || !string.IsNullOrWhiteSpace(expectedRevision)
                || !string.IsNullOrWhiteSpace(expectedBuildInputManifestSha256);
            if (hasExternalExpectation)
            {
                Assert(!string.IsNullOrWhiteSpace(expectedBinaryBuildId)
                        && !string.IsNullOrWhiteSpace(expectedRevision)
                        && !string.IsNullOrWhiteSpace(expectedBuildInputManifestSha256),
                    "External build identity expectations must be supplied together.");
                Assert(string.Equals(
                        binaryBuildId,
                        expectedBinaryBuildId,
                        StringComparison.Ordinal),
                    "Actual app Binary Build ID did not match the external build manifest.");
                Assert(string.Equals(
                        revision,
                        expectedRevision,
                        StringComparison.Ordinal),
                    "Actual app source revision did not match the external build manifest.");
                Assert(string.Equals(
                        inputHash,
                        expectedBuildInputManifestSha256,
                        StringComparison.Ordinal),
                    "Actual app build-input hash did not match the external build manifest.");
            }

            using (Stream resource = application.GetManifestResourceStream(
                "TarkovServerReporter.BuildIdentity.json"))
            {
                Assert(resource != null && resource.Length > 0,
                    "Actual app did not contain the public JSON identity resource.");
            }
        }

        private static T GetProperty<T>(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, InternalInstance);
            Assert(property != null, "Build identity property was not found: " + name);
            object value = property.GetValue(instance, null);
            if (!(value is T))
                throw new InvalidOperationException("Build identity property had an unexpected type: " + name);
            return (T)value;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
