using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TarkovServerReporter.Tests
{
    internal static class StorageAndBatchTests
    {
        private static int _failures;

        private static int Main()
        {
            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "TarkovServerGuard-StorageAndBatchTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                ConfigureTemporaryMetadataStore(temporaryRoot);
                TestMetadataRoundTripAndBackupRecovery(temporaryRoot);
                TestBatchTokenBoundaries();
                TestBatchInputNormalization();
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("FAIL: unexpected test exception: " + ex);
            }
            finally
            {
                DeleteTemporaryRoot(temporaryRoot);
            }

            Console.WriteLine(_failures == 0
                ? "ALL STORAGE/BATCH TESTS PASSED"
                : _failures + " STORAGE/BATCH TEST(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }

        private static void ConfigureTemporaryMetadataStore(string temporaryRoot)
        {
            string fullTemporaryRoot = Path.GetFullPath(temporaryRoot);
            string systemTemporaryRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!fullTemporaryRoot.StartsWith(systemTemporaryRoot, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(fullTemporaryRoot).IndexOf(
                    "TarkovServerGuard-StorageAndBatchTests-",
                    StringComparison.Ordinal) != 0)
                throw new InvalidOperationException("Unsafe metadata test directory.");

            string storePath = Path.Combine(fullTemporaryRoot, "blocked-server-metadata.json");
            Type storeType = typeof(BlockedServerMetadataStore);
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            SetStaticField(storeType, "StoreDirectory", fullTemporaryRoot, flags);
            SetStaticField(storeType, "StorePath", storePath, flags);
            SetStaticField(storeType, "BackupPath", storePath + ".bak", flags);
        }

        private static void SetStaticField(
            Type type,
            string fieldName,
            string value,
            BindingFlags flags)
        {
            FieldInfo field = type.GetField(fieldName, flags);
            if (field == null) throw new MissingFieldException(type.FullName, fieldName);
            field.SetValue(null, value);
            Assert(string.Equals(Convert.ToString(field.GetValue(null)), value, StringComparison.Ordinal),
                "metadata store test path redirected: " + fieldName);
        }

        private static void TestMetadataRoundTripAndBackupRecovery(string temporaryRoot)
        {
            const string firstIp = "203.0.113.42";
            const string secondIp = "198.51.100.18";
            string storePath = Path.Combine(temporaryRoot, "blocked-server-metadata.json");
            string backupPath = storePath + ".bak";

            Assert(BlockedServerMetadataStore.MarkBlocked(firstIp, "JP-TK02", "Tokyo, JP"),
                "metadata mark-blocked succeeds in the temporary store");
            Assert(File.Exists(storePath) && File.Exists(backupPath),
                "metadata save creates both primary and backup files");

            IDictionary<string, BlockedServerMetadata> loaded = BlockedServerMetadataStore.LoadAll();
            BlockedServerMetadata first = null;
            Assert(loaded.Count == 1 && loaded.TryGetValue(firstIp, out first),
                "metadata primary round-trip preserves the IP record");
            Assert(first != null
                && first.DataCenter == "JP-TK02"
                && first.Location == "Tokyo, JP"
                && first.BlockedAtUtc.HasValue
                && first.UpdatedAtUtc.Kind == DateTimeKind.Utc,
                "metadata primary round-trip preserves DC, location, and UTC timestamps");

            string primaryJson = File.ReadAllText(storePath, Encoding.UTF8);
            Assert(primaryJson.Contains("\"Ip\"")
                && primaryJson.Contains("\"DataCenter\"")
                && primaryJson.Contains("\"Location\"")
                && primaryJson.Contains("\"BlockedAtUtc\"")
                && primaryJson.Contains("\"UpdatedAtUtc\""),
                "metadata document contains only the intended record fields");
            Assert(primaryJson.IndexOf("Sid", StringComparison.OrdinalIgnoreCase) < 0
                && primaryJson.IndexOf("LogPath", StringComparison.OrdinalIgnoreCase) < 0
                && primaryJson.IndexOf("Account", StringComparison.OrdinalIgnoreCase) < 0,
                "metadata document does not contain log, SID, or account fields");

            File.WriteAllText(storePath, "{ damaged primary", new UTF8Encoding(false));
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded.Count == 1 && loaded.ContainsKey(firstIp),
                "metadata load falls back to backup when the primary is corrupt");

            Assert(BlockedServerMetadataStore.UpdateLocation(firstIp, "JP-TK03", "Osaka, JP"),
                "metadata update repairs a corrupt primary from the valid backup");
            loaded = BlockedServerMetadataStore.LoadAll();
            first = loaded[firstIp];
            Assert(first.DataCenter == "JP-TK03"
                && first.Location == "Osaka, JP"
                && first.BlockedAtUtc.HasValue,
                "location update preserves the original block time");
            Assert(IsValidMetadataFile(storePath) && IsValidMetadataFile(backupPath),
                "metadata repair leaves valid primary and backup documents");

            File.Delete(storePath);
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded.Count == 1 && loaded.ContainsKey(firstIp),
                "metadata load falls back to backup when the primary is missing");
            Assert(BlockedServerMetadataStore.UpdateLocation(firstIp, null, "Osaka, JP"),
                "metadata update recreates a missing primary from backup");

            Assert(BlockedServerMetadataStore.MarkBlocked(secondIp, "SGP", "Singapore, SG"),
                "metadata store accepts a second managed server");
            Assert(BlockedServerMetadataStore.Remove(new[] { firstIp }),
                "metadata selective deletion succeeds");
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded.Count == 1 && !loaded.ContainsKey(firstIp) && loaded.ContainsKey(secondIp),
                "metadata deletion removes only the successful target IP");

            Assert(!BlockedServerMetadataStore.Upsert(
                    "203.0.113.42 & whoami",
                    "unsafe",
                    "unsafe",
                    DateTime.UtcNow)
                && BlockedServerMetadataStore.LoadAll().Count == 1,
                "metadata store rejects a non-canonical or injectable IP");

            Assert(BlockedServerMetadataStore.Remove(new[] { secondIp }),
                "metadata final deletion succeeds");
            Assert(BlockedServerMetadataStore.LoadAll().Count == 0,
                "metadata store is empty after deleting all records");
            Assert(IsValidMetadataFile(storePath) && IsValidMetadataFile(backupPath),
                "empty metadata state is committed to primary and backup");

            string[] leftovers = Directory.GetFiles(temporaryRoot, "*.tmp.*")
                .Concat(Directory.GetFiles(temporaryRoot, "*.previous.*"))
                .ToArray();
            Assert(leftovers.Length == 0,
                "atomic metadata saves leave no temporary or replacement files");
        }

        private static bool IsValidMetadataFile(string path)
        {
            if (!File.Exists(path)) return false;
            string content = File.ReadAllText(path, Encoding.UTF8);
            return content.StartsWith("{", StringComparison.Ordinal)
                && content.Contains("\"Version\":1")
                && content.Contains("\"Items\"");
        }

        private static void TestBatchTokenBoundaries()
        {
            IList<string> parsed;
            Assert(TryParseBatchToken(
                    "batch:203.0.113.42,198.51.100.18",
                    out parsed)
                && parsed.SequenceEqual(new[] { "203.0.113.42", "198.51.100.18" }),
                "batch token accepts ordered canonical unique IPv4 addresses");

            Assert(!TryParseBatchToken("Batch:203.0.113.42", out parsed),
                "batch token prefix is exact and case-sensitive");
            Assert(!TryParseBatchToken("batch:", out parsed)
                && !TryParseBatchToken("batch:203.0.113.42,", out parsed)
                && !TryParseBatchToken("batch:203.0.113.42,203.0.113.42", out parsed),
                "batch token rejects empty items and duplicates");
            Assert(!TryParseBatchToken("batch: 203.0.113.42", out parsed)
                && !TryParseBatchToken("batch:001.002.003.004", out parsed)
                && !TryParseBatchToken("batch:203.0.113.42&whoami", out parsed)
                && !TryParseBatchToken("batch:127.0.0.1", out parsed),
                "batch token rejects whitespace, non-canonical, injectable, and loopback input");

            string maximumToken = "batch:" + string.Join(",", CreateUniqueAddresses(1024));
            Assert(TryParseBatchToken(maximumToken, out parsed) && parsed.Count == 1024,
                "batch token accepts the 1024-address boundary");
            string oversizedToken = "batch:" + string.Join(",", CreateUniqueAddresses(1025));
            Assert(!TryParseBatchToken(oversizedToken, out parsed),
                "batch token rejects more than 1024 addresses");

            bool shouldBlock;
            string argument;
            Assert(FirewallRuleManager.TryParseHelperCommand(
                    new[] { "--firewall-remove", maximumToken },
                    out shouldBlock,
                    out argument)
                && !shouldBlock
                && argument == maximumToken,
                "existing helper command envelope carries a valid batch token as one argument");
        }

        private static void TestBatchInputNormalization()
        {
            MethodInfo method = typeof(FirewallRuleManager).GetMethod(
                "NormalizeBatchAddresses",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(
                typeof(FirewallRuleManager).FullName,
                "NormalizeBatchAddresses");

            var input = new[]
            {
                "203.0.113.42",
                "198.51.100.18",
                "203.0.113.42",
                " 192.0.2.1 ",
                "127.0.0.1",
                null
            };
            var normalized = (IList<string>)method.Invoke(null, new object[] { input });
            Assert(normalized.SequenceEqual(new[] { "203.0.113.42", "198.51.100.18" }),
                "batch normalization preserves order, deduplicates, and drops invalid input");
        }

        private static bool TryParseBatchToken(string token, out IList<string> parsed)
        {
            MethodInfo method = typeof(FirewallRuleManager).GetMethod(
                "TryParseBatchRemoveToken",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(
                typeof(FirewallRuleManager).FullName,
                "TryParseBatchRemoveToken");

            object[] arguments = { token, null };
            bool success = Convert.ToBoolean(method.Invoke(null, arguments));
            parsed = arguments[1] as IList<string>;
            return success;
        }

        private static IEnumerable<string> CreateUniqueAddresses(int count)
        {
            for (int index = 0; index < count; index++)
            {
                int second = index / 256;
                int third = index % 256;
                yield return "11." + second + "." + third + ".1";
            }
        }

        private static void DeleteTemporaryRoot(string temporaryRoot)
        {
            try
            {
                string full = Path.GetFullPath(temporaryRoot);
                string temp = Path.GetFullPath(Path.GetTempPath())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(full).StartsWith(
                        "TarkovServerGuard-StorageAndBatchTests-",
                        StringComparison.Ordinal)
                    && Directory.Exists(full))
                    Directory.Delete(full, true);
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("FAIL: temporary test cleanup: " + ex.Message);
            }
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
