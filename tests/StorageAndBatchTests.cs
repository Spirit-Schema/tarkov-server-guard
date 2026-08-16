// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

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
                TestMetadataMutationFailClosed(temporaryRoot);
                TestBackupExportImportAndPrivacy();
                TestEmptyBackupExportDoesNotOverwrite(temporaryRoot);
                TestAtomicBackupWrite(temporaryRoot);
                TestBackupValidationAndPreview();
                TestBackupMetadataMergeDoesNotOverwrite();
                TestBatchTokenBoundaries();
                TestBatchAddTokenBoundaries();
                TestBatchInputNormalization();
                TestElevatedHelperWaitPolicy();
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
            Assert(BlockedServerMetadataStore.UpdateNote(
                    firstIp,
                    "  높은 핑\0\r\n\u2028패킷손실  "),
                "blocked server note save succeeds");
            Assert(File.Exists(storePath) && File.Exists(backupPath),
                "metadata save creates both primary and backup files");

            IDictionary<string, BlockedServerMetadata> loaded = BlockedServerMetadataStore.LoadAll();
            BlockedServerMetadata first = null;
            Assert(loaded.Count == 1 && loaded.TryGetValue(firstIp, out first),
                "metadata primary round-trip preserves the IP record");
            Assert(first != null
                && first.DataCenter == "JP-TK02"
                && first.Location == "Tokyo, JP"
                && first.Note == "높은 핑 패킷손실"
                && first.BlockedAtUtc.HasValue
                && first.UpdatedAtUtc.Kind == DateTimeKind.Utc,
                "metadata primary round-trip preserves note, DC, location, and UTC timestamps");

            string primaryJson = File.ReadAllText(storePath, Encoding.UTF8);
            Assert(primaryJson.Contains("\"Ip\"")
                && primaryJson.Contains("\"DataCenter\"")
                && primaryJson.Contains("\"Location\"")
                && primaryJson.Contains("\"Note\"")
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
                && first.Note == "높은 핑 패킷손실"
                && first.BlockedAtUtc.HasValue,
                "location update preserves the original block time and note");
            Assert(IsValidMetadataFile(storePath) && IsValidMetadataFile(backupPath),
                "metadata repair leaves valid primary and backup documents");

            File.Delete(storePath);
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded.Count == 1 && loaded.ContainsKey(firstIp),
                "metadata load falls back to backup when the primary is missing");
            Assert(BlockedServerMetadataStore.UpdateLocation(firstIp, null, "Osaka, JP"),
                "metadata update recreates a missing primary from backup");

            string oversizedNote = new string('가', BlockedServerMetadataStore.MaximumNoteLength + 25);
            Assert(BlockedServerMetadataStore.UpdateNote(secondIp, oversizedNote),
                "a legacy managed rule can receive a note before location metadata exists");
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded.ContainsKey(secondIp)
                && loaded[secondIp].Note.Length == BlockedServerMetadataStore.MaximumNoteLength
                && !loaded[secondIp].BlockedAtUtc.HasValue,
                "note input is capped at 300 characters without inventing a block time");
            string surrogateBoundaryNote = new string('a', 299) + "\ud83d\ude00";
            Assert(BlockedServerMetadataStore.UpdateNote(secondIp, surrogateBoundaryNote),
                "note normalization accepts valid surrogate pairs at the length boundary");
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded[secondIp].Note.Length == 299
                && loaded[secondIp].Note.All(character => !char.IsSurrogate(character)),
                "note normalization never persists half of a surrogate pair");
            Assert(BlockedServerMetadataStore.UpdateNote(secondIp, oversizedNote),
                "the maximum-length legacy note can be restored after boundary validation");
            Assert(BlockedServerMetadataStore.MarkBlocked(secondIp, "SGP", "Singapore, SG"),
                "metadata store accepts block details for the legacy managed server");
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded[secondIp].Note.Length == BlockedServerMetadataStore.MaximumNoteLength,
                "adding block details preserves an existing legacy-rule note");
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
            Assert(!BlockedServerMetadataStore.UpdateNote("203.0.113.42 & whoami", "unsafe"),
                "note update rejects a non-canonical or injectable IP");

            Assert(BlockedServerMetadataStore.UpdateNote(secondIp, " \r\n\t "),
                "blank note deletion succeeds");
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded.ContainsKey(secondIp) && loaded[secondIp].Note == null,
                "blank note deletion preserves the remaining metadata record");

            Assert(BlockedServerMetadataStore.Remove(new[] { secondIp }),
                "metadata final deletion succeeds");
            Assert(BlockedServerMetadataStore.LoadAll().Count == 0,
                "metadata store is empty after deleting all records");
            Assert(IsValidMetadataFile(storePath) && IsValidMetadataFile(backupPath),
                "empty metadata state is committed to primary and backup");

            const string legacyIp = "192.0.2.55";
            string legacyTimestamp = DateTime.UtcNow.ToString("o");
            string legacyJson = "{\"Version\":1,\"Items\":[{\"Ip\":\"" + legacyIp
                + "\",\"DataCenter\":\"Legacy\",\"Location\":\"Tokyo, JP\","
                + "\"BlockedAtUtc\":null,\"UpdatedAtUtc\":\"" + legacyTimestamp + "\"}]}";
            File.WriteAllText(storePath, legacyJson, new UTF8Encoding(false));
            File.WriteAllText(backupPath, legacyJson, new UTF8Encoding(false));
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded.ContainsKey(legacyIp) && loaded[legacyIp].Note == null,
                "v1 metadata documents without a Note property remain readable");
            Assert(BlockedServerMetadataStore.UpdateNote(legacyIp, "반복 끊김"),
                "a note can be added after loading metadata written by an older version");
            loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded[legacyIp].Note == "반복 끊김",
                "the note added to legacy metadata round-trips normally");
            Assert(BlockedServerMetadataStore.Remove(new[] { legacyIp }),
                "legacy metadata note cleanup succeeds");

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

        private static void TestMetadataMutationFailClosed(string temporaryRoot)
        {
            string storePath = Path.Combine(temporaryRoot, "blocked-server-metadata.json");
            string backupPath = storePath + ".bak";
            byte[] damagedPrimary = Encoding.UTF8.GetBytes("{ damaged primary");
            byte[] damagedBackup = Encoding.UTF8.GetBytes("{ damaged backup");
            File.WriteAllBytes(storePath, damagedPrimary);
            File.WriteAllBytes(backupPath, damagedBackup);

            bool noteRejected = !BlockedServerMetadataStore.UpdateNote("1.1.1.1", "must not save");
            bool upsertRejected = !BlockedServerMetadataStore.Upsert(
                "8.8.8.8",
                "TEST",
                "Test location",
                DateTime.UtcNow);
            bool mergeRejected = !BlockedServerMetadataStore.MergeMissingFromBackup(new[]
            {
                new BlockedServerBackupEntry
                {
                    IpAddress = "9.9.9.9",
                    Note = "must not merge"
                }
            });
            bool removeRejected = !BlockedServerMetadataStore.Remove(new[] { "1.1.1.1" });
            Assert(noteRejected && upsertRejected && mergeRejected && removeRejected,
                "all metadata mutations fail closed when no valid primary or backup exists");
            Assert(File.ReadAllBytes(storePath).SequenceEqual(damagedPrimary)
                && File.ReadAllBytes(backupPath).SequenceEqual(damagedBackup),
                "failed mutations preserve both damaged recovery artifacts byte-for-byte");

            string timestamp = new DateTime(2026, 8, 16, 1, 2, 3, DateTimeKind.Utc)
                .ToString("o");
            byte[] partiallyDamaged = Encoding.UTF8.GetBytes(
                "{\"Version\":1,\"Items\":["
                + "{\"Ip\":\"1.1.1.1\",\"Note\":\"preserve me\",\"UpdatedAtUtc\":\""
                + timestamp + "\"},"
                + "{\"Ip\":\"8.8.8.8\",\"Note\":\"also preserve\","
                + "\"UpdatedAtUtc\":\"not-a-timestamp\"}]}");
            File.WriteAllBytes(storePath, partiallyDamaged);
            File.WriteAllBytes(backupPath, partiallyDamaged);

            Assert(!BlockedServerMetadataStore.Upsert(
                    "9.9.9.9",
                    "TEST",
                    "Test location",
                    DateTime.UtcNow)
                && !BlockedServerMetadataStore.MergeMissingFromBackup(new[]
                {
                    new BlockedServerBackupEntry
                    {
                        IpAddress = "9.9.9.9",
                        Note = "must not replace partial data"
                    }
                }),
                "a partially invalid metadata document is rejected as a whole before mutation");
            Assert(File.ReadAllBytes(storePath).SequenceEqual(partiallyDamaged)
                && File.ReadAllBytes(backupPath).SequenceEqual(partiallyDamaged),
                "partial-record validation failure preserves primary and backup bytes");

            byte[] invalidUtf8 = { 0x7b, 0xff, 0x7d };
            File.WriteAllBytes(storePath, invalidUtf8);
            File.WriteAllBytes(backupPath, invalidUtf8);
            Assert(!BlockedServerMetadataStore.UpdateNote("1.1.1.1", "must not recode")
                && File.ReadAllBytes(storePath).SequenceEqual(invalidUtf8)
                && File.ReadAllBytes(backupPath).SequenceEqual(invalidUtf8),
                "invalid UTF-8 metadata is rejected without replacement-character data loss");
            Assert(Directory.GetFiles(temporaryRoot, "*.tmp.*").Length == 0
                && Directory.GetFiles(temporaryRoot, "*.previous.*").Length == 0,
                "fail-closed metadata loading creates no temporary replacement files");

            File.Delete(storePath);
            File.Delete(backupPath);
        }

        private static void TestBackupExportImportAndPrivacy()
        {
            DateTime blockedAt = new DateTime(2026, 8, 15, 11, 12, 13, DateTimeKind.Utc);
            var servers = new[]
            {
                new ManagedBlockedServer { IpAddress = "8.8.8.8" },
                new ManagedBlockedServer { IpAddress = "1.1.1.1" },
                new ManagedBlockedServer { IpAddress = "10.0.0.8" }
            };
            var metadata = new Dictionary<string, BlockedServerMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                { "1.1.1.1", new BlockedServerMetadata
                {
                    IpAddress = "1.1.1.1",
                    DataCenter = "JP-TK02",
                    Location = "Tokyo, JP",
                    Note = "높은 핑",
                    BlockedAtUtc = blockedAt,
                    UpdatedAtUtc = blockedAt.AddHours(1)
                } }
            };

            BlockedServerBackupExportResult exported = BlockedServerBackupService.CreateExport(
                servers,
                metadata);
            Assert(exported.Success
                && exported.Entries.Select(item => item.IpAddress)
                    .SequenceEqual(new[] { "1.1.1.1", "8.8.8.8" })
                && exported.ExcludedAddresses.SequenceEqual(new[] { "10.0.0.8" }),
                "backup export uses sorted actual managed public IPv4 rules and reports exclusions");
            Assert(exported.Utf8Bytes != null
                && (exported.Utf8Bytes.Length < 3
                    || !(exported.Utf8Bytes[0] == 0xef
                        && exported.Utf8Bytes[1] == 0xbb
                        && exported.Utf8Bytes[2] == 0xbf)),
                "backup export is UTF-8 without a BOM");

            string json = Encoding.UTF8.GetString(exported.Utf8Bytes);
            Assert(json.Contains("\"Format\":\"TarkovServerGuard.BlockedServers\"")
                && json.Contains("\"Version\":1")
                && json.Contains("\"Ip\":\"1.1.1.1\"")
                && json.Contains("\"DataCenter\":\"JP-TK02\"")
                && json.Contains("\"Location\":\"Tokyo, JP\"")
                && json.Contains("\"Note\":\"높은 핑\"")
                && json.Contains("\"BlockedAtUtc\":\"2026-08-15T11:12:13.0000000Z\""),
                "backup export contains the versioned restore fields");
            Assert(!json.Contains("UpdatedAtUtc")
                && !json.Contains("Sid")
                && !json.Contains("Account")
                && !json.Contains("LogPath")
                && !json.Contains("Machine")
                && !json.Contains("Device"),
                "backup export omits local timestamps, account, SID, path, and device fields");

            BlockedServerBackupParseResult parsed = BlockedServerBackupService.Parse(exported.Utf8Bytes);
            Assert(parsed.Success && parsed.Items.Count == 2 && parsed.Items.All(item => item.IsEligible),
                "exported backup round-trips through the path-independent parser");
            BlockedServerBackupEntry first = parsed.Items[0].Entry;
            Assert(first.IpAddress == "1.1.1.1"
                && first.DataCenter == "JP-TK02"
                && first.Location == "Tokyo, JP"
                && first.Note == "높은 핑"
                && first.BlockedAtUtc == blockedAt,
                "backup metadata and UTC block time survive round-trip");

            BlockedServerBackupExportResult repeated = BlockedServerBackupService.CreateExport(
                servers.Reverse(),
                metadata);
            Assert(repeated.Success && exported.Utf8Bytes.SequenceEqual(repeated.Utf8Bytes),
                "backup bytes are deterministic regardless of managed-rule enumeration order");
        }

        private static void TestEmptyBackupExportDoesNotOverwrite(string temporaryRoot)
        {
            string path = Path.Combine(temporaryRoot, "empty-export-guard.json");
            byte[] original = Encoding.UTF8.GetBytes("{\"known\":\"good existing backup\"}");
            File.WriteAllBytes(path, original);

            BlockedServerBackupExportResult empty = BlockedServerBackupService.CreateExport(
                new ManagedBlockedServer[0],
                new Dictionary<string, BlockedServerMetadata>(StringComparer.OrdinalIgnoreCase));
            Assert(!empty.Success && empty.Utf8Bytes == null
                && !string.IsNullOrWhiteSpace(empty.ErrorMessage),
                "an empty managed-rule snapshot is refused before file output");
            if (empty.Success)
                BlockedServerBackupFile.WriteAtomic(path, empty.Utf8Bytes);
            Assert(File.ReadAllBytes(path).SequenceEqual(original),
                "an empty snapshot cannot replace an existing valid backup");

            BlockedServerBackupExportResult excludedOnly = BlockedServerBackupService.CreateExport(
                new[] { new ManagedBlockedServer { IpAddress = "10.0.0.1" } },
                null);
            Assert(!excludedOnly.Success
                && excludedOnly.ExcludedAddresses.SequenceEqual(new[] { "10.0.0.1" }),
                "a snapshot containing only non-restorable addresses is also refused");
            File.Delete(path);
        }

        private static void TestBackupValidationAndPreview()
        {
            string json = "{\"Format\":\"TarkovServerGuard.BlockedServers\",\"Version\":1,\"Items\":["
                + "{\"Ip\":\"1.1.1.1\",\"Note\":\"첫 항목\"},"
                + "{\"Ip\":\"8.8.8.8\",\"Location\":\"Seoul, KR\"},"
                + "{\"Ip\":\"9.9.9.9\"},"
                + "{\"Ip\":\"1.1.1.1\"},"
                + "{\"Ip\":\"10.0.0.1\"},"
                + "{\"Ip\":\"4.2.2.2\",\"BlockedAtUtc\":\"not-a-time\"}]}";
            BlockedServerBackupParseResult parsed = BlockedServerBackupService.Parse(
                new UTF8Encoding(false, true).GetBytes(json));
            Assert(parsed.Success && parsed.Items.Count == 6,
                "backup parser keeps item-level problems for preview instead of rejecting the whole file");
            Assert(parsed.Items.Count(item => item.IsEligible) == 3
                && parsed.Items.Count(item => !item.IsEligible) == 3
                && parsed.Items.Any(item => item.ExclusionReason != null
                    && item.ExclusionReason.Contains("중복"))
                && parsed.Items.Any(item => item.ExclusionReason != null
                    && item.ExclusionReason.Contains("공인 IPv4"))
                && parsed.Items.Any(item => item.ExclusionReason != null
                    && item.ExclusionReason.Contains("UTC")),
                "backup parser distinguishes duplicate, non-public, and malformed metadata items");

            var states = new Dictionary<string, FirewallQueryResult>(StringComparer.OrdinalIgnoreCase)
            {
                { "1.1.1.1", new FirewallQueryResult { Success = true, IsBlocked = false } },
                { "8.8.8.8", new FirewallQueryResult { Success = true, IsBlocked = true } },
                { "9.9.9.9", new FirewallQueryResult
                {
                    ErrorMessage = "의도된 상태 조회 실패"
                } }
            };
            IList<BlockedServerRestoreItem> preview = BlockedServerBackupService.CreateRestorePreview(
                parsed,
                states);
            Assert(preview.Count(item => item.Status == BlockedServerRestoreStatus.NewBlock) == 1
                && preview.Count(item => item.Status == BlockedServerRestoreStatus.AlreadyBlocked) == 1
                && preview.Count(item => item.Status == BlockedServerRestoreStatus.Excluded) == 4,
                "restore preview separates new, already managed-blocked, and excluded entries");
            Assert(preview.Single(item => item.IpAddress == "8.8.8.8").HasMetadata,
                "restore preview exposes whether backup metadata is present");

            Assert(!FirewallRuleManager.IsPublicIpv4("10.0.0.1")
                && !FirewallRuleManager.IsPublicIpv4("100.64.0.1")
                && !FirewallRuleManager.IsPublicIpv4("127.0.0.1")
                && !FirewallRuleManager.IsPublicIpv4("169.254.1.1")
                && !FirewallRuleManager.IsPublicIpv4("172.16.0.1")
                && !FirewallRuleManager.IsPublicIpv4("192.168.0.1")
                && !FirewallRuleManager.IsPublicIpv4("198.18.0.1")
                && !FirewallRuleManager.IsPublicIpv4("224.0.0.1")
                && !FirewallRuleManager.IsPublicIpv4("192.0.2.1")
                && !FirewallRuleManager.IsPublicIpv4("198.51.100.1")
                && !FirewallRuleManager.IsPublicIpv4("203.0.113.1")
                && FirewallRuleManager.IsPublicIpv4("1.1.1.1")
                && FirewallRuleManager.IsPublicIpv4("8.8.8.8"),
                "backup public IPv4 validation rejects private, shared, local, special, and documentation ranges");

            byte[] oversized = new byte[BlockedServerBackupService.MaximumFileBytes + 1];
            Assert(!BlockedServerBackupService.Parse(oversized).Success,
                "backup parser rejects files over the byte limit before JSON processing");
            Assert(!BlockedServerBackupService.Parse(new byte[] { 0xff, 0xfe }).Success,
                "backup parser rejects invalid UTF-8");
            Assert(!BlockedServerBackupService.Parse(Encoding.UTF8.GetBytes(
                    "{\"Format\":\"TarkovServerGuard.BlockedServers\",\"Version\":2,\"Items\":[]}"))
                .Success,
                "backup parser rejects unsupported versions");

            BlockedServerBackupParseResult duplicateTopLevel = BlockedServerBackupService.Parse(
                Encoding.UTF8.GetBytes(
                    "{\"Format\":\"TarkovServerGuard.BlockedServers\",\"Version\":1,"
                    + "\"\\u0056ersion\":1,\"Items\":[]}"));
            BlockedServerBackupParseResult duplicateItemField = BlockedServerBackupService.Parse(
                Encoding.UTF8.GetBytes(
                    "{\"Format\":\"TarkovServerGuard.BlockedServers\",\"Version\":1,\"Items\":["
                    + "{\"Ip\":\"1.1.1.1\",\"\\u0049p\":\"8.8.8.8\"}]}"));
            Assert(!duplicateTopLevel.Success
                && !duplicateItemField.Success
                && duplicateTopLevel.ErrorMessage.Contains("중복")
                && duplicateItemField.ErrorMessage.Contains("중복"),
                "backup parser explicitly rejects duplicate object members, including escaped names");
            Assert(BlockedServerBackupService.Parse(Encoding.UTF8.GetBytes(
                    "{\"Format\":\"TarkovServerGuard.BlockedServers\",\"Version\":1,\"Items\":["
                    + "{\"Ip\":\"1.1.1.1\",\"Note\":\"\\\"Ip\\\":\\\"8.8.8.8\\\"\"}]}"))
                .Success,
                "duplicate-member validation does not mistake JSON-looking note text for fields");

            string tooManyItems = "{\"Format\":\"TarkovServerGuard.BlockedServers\",\"Version\":1,\"Items\":["
                + string.Join(",", Enumerable.Range(0, BlockedServerBackupService.MaximumItemCount + 1)
                    .Select(index => "{\"Ip\":\"1.1.1.1\"}"))
                + "]}";
            Assert(!BlockedServerBackupService.Parse(Encoding.UTF8.GetBytes(tooManyItems)).Success,
                "backup parser rejects more than the item-count limit");
        }

        private static void TestAtomicBackupWrite(string temporaryRoot)
        {
            string path = Path.Combine(temporaryRoot, "blocked-servers-export.json");
            byte[] original = Encoding.UTF8.GetBytes("{\"known\":\"good\"}");
            byte[] replacement = Encoding.UTF8.GetBytes(
                "{\"Format\":\"TarkovServerGuard.BlockedServers\",\"Version\":1,\"Items\":[]}");
            File.WriteAllBytes(path, original);

            bool failedSafely = false;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    BlockedServerBackupFile.WriteAtomic(path, replacement);
                }
                catch (IOException)
                {
                    failedSafely = true;
                }
            }
            Assert(failedSafely && File.ReadAllBytes(path).SequenceEqual(original),
                "atomic backup write preserves an existing good file when replacement fails");
            Assert(Directory.GetFiles(temporaryRoot, "blocked-servers-export.json.tmp.*").Length == 0,
                "failed atomic backup write removes its same-directory temporary file");

            BlockedServerBackupFile.WriteAtomic(path, replacement);
            Assert(File.ReadAllBytes(path).SequenceEqual(replacement),
                "atomic backup write replaces an existing file after a flushed same-directory commit");
            Assert(Directory.GetFiles(temporaryRoot, "blocked-servers-export.json.tmp.*").Length == 0,
                "successful atomic backup write leaves no temporary file");
            File.Delete(path);
        }

        private static void TestBackupMetadataMergeDoesNotOverwrite()
        {
            DateTime localBlockedAt = new DateTime(2026, 8, 1, 1, 2, 3, DateTimeKind.Utc);
            DateTime backupBlockedAt = new DateTime(2026, 7, 1, 4, 5, 6, DateTimeKind.Utc);
            Assert(BlockedServerMetadataStore.Upsert(
                    "1.1.1.1",
                    "LOCAL-DC",
                    "Local location",
                    localBlockedAt)
                && BlockedServerMetadataStore.UpdateNote("1.1.1.1", "local note")
                && BlockedServerMetadataStore.UpdateNote("8.8.8.8", "keep this note"),
                "metadata merge fixtures are stored in the temporary location");

            var backupEntries = new[]
            {
                new BlockedServerBackupEntry
                {
                    IpAddress = "1.1.1.1",
                    DataCenter = "BACKUP-DC",
                    Location = "Backup location",
                    Note = "backup note",
                    BlockedAtUtc = backupBlockedAt
                },
                new BlockedServerBackupEntry
                {
                    IpAddress = "8.8.8.8",
                    DataCenter = "SGP",
                    Location = "Singapore, SG",
                    Note = "replace attempt",
                    BlockedAtUtc = backupBlockedAt
                },
                new BlockedServerBackupEntry
                {
                    IpAddress = "9.9.9.9",
                    DataCenter = "JP",
                    Location = "Tokyo, JP",
                    Note = "new backup note",
                    BlockedAtUtc = backupBlockedAt
                }
            };
            Assert(BlockedServerMetadataStore.MergeMissingFromBackup(backupEntries),
                "backup metadata merge succeeds without a firewall dependency");

            IDictionary<string, BlockedServerMetadata> loaded = BlockedServerMetadataStore.LoadAll();
            Assert(loaded["1.1.1.1"].DataCenter == "LOCAL-DC"
                && loaded["1.1.1.1"].Location == "Local location"
                && loaded["1.1.1.1"].Note == "local note"
                && loaded["1.1.1.1"].BlockedAtUtc == localBlockedAt,
                "backup merge never overwrites existing local location, note, or block time");
            Assert(loaded["8.8.8.8"].DataCenter == "SGP"
                && loaded["8.8.8.8"].Location == "Singapore, SG"
                && loaded["8.8.8.8"].Note == "keep this note"
                && loaded["8.8.8.8"].BlockedAtUtc == backupBlockedAt,
                "backup merge fills only missing fields on an existing record");
            Assert(loaded["9.9.9.9"].DataCenter == "JP"
                && loaded["9.9.9.9"].Location == "Tokyo, JP"
                && loaded["9.9.9.9"].Note == "new backup note"
                && loaded["9.9.9.9"].BlockedAtUtc == backupBlockedAt,
                "backup merge restores all available metadata for a new record");
            Assert(!BlockedServerMetadataStore.MergeMissingFromBackup(new[]
                {
                    new BlockedServerBackupEntry { IpAddress = "192.168.0.1", Note = "unsafe" }
                }),
                "backup metadata merge rejects non-public addresses atomically");
            Assert(BlockedServerMetadataStore.Remove(new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9" }),
                "backup metadata merge fixtures are removed");
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

        private static void TestBatchAddTokenBoundaries()
        {
            IList<string> parsed;
            Assert(TryParseBatchAddToken(
                    "batch-add:1.1.1.1,8.8.8.8",
                    out parsed)
                && parsed.SequenceEqual(new[] { "1.1.1.1", "8.8.8.8" }),
                "batch add token accepts ordered canonical unique public IPv4 addresses");
            Assert(!TryParseBatchAddToken("Batch-add:1.1.1.1", out parsed)
                && !TryParseBatchAddToken("batch-add:", out parsed)
                && !TryParseBatchAddToken("batch-add:1.1.1.1,1.1.1.1", out parsed)
                && !TryParseBatchAddToken("batch-add: 1.1.1.1", out parsed)
                && !TryParseBatchAddToken("batch-add:10.0.0.1", out parsed)
                && !TryParseBatchAddToken("batch-add:203.0.113.1", out parsed)
                && !TryParseBatchAddToken("batch-add:1.1.1.1&whoami", out parsed),
                "batch add token rejects wrong prefixes, empty, duplicate, whitespace, non-public, and injectable input");

            string maximumToken = "batch-add:" + string.Join(",", CreateUniqueAddresses(1024));
            Assert(TryParseBatchAddToken(maximumToken, out parsed) && parsed.Count == 1024,
                "batch add token accepts the 1024-address boundary");
            string oversizedToken = "batch-add:" + string.Join(",", CreateUniqueAddresses(1025));
            Assert(!TryParseBatchAddToken(oversizedToken, out parsed),
                "batch add token rejects more than 1024 addresses");

            bool shouldBlock;
            string argument;
            Assert(FirewallRuleManager.TryParseHelperCommand(
                    new[] { "--firewall-add", maximumToken },
                    out shouldBlock,
                    out argument)
                && shouldBlock
                && argument == maximumToken,
                "helper command envelope carries one validated batch-add token argument");
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

        private static void TestElevatedHelperWaitPolicy()
        {
            int completedCalls = 0;
            int completedTimeout = -1;
            FirewallRuleManager.ElevatedHelperWaitOutcome completed =
                FirewallRuleManager.WaitForElevatedHelperExit(delegate(int timeoutMilliseconds)
                {
                    completedCalls++;
                    completedTimeout = timeoutMilliseconds;
                    return true;
                });
            Assert(completed == FirewallRuleManager.ElevatedHelperWaitOutcome.Completed
                && completedCalls == 1
                && completedTimeout == 120000
                && completedTimeout == FirewallRuleManager.ElevatedHelperTimeoutMilliseconds,
                "elevated helper completion uses one bounded 120-second wait");

            int timeoutCalls = 0;
            int observedTimeout = -1;
            FirewallRuleManager.ElevatedHelperWaitOutcome timedOut =
                FirewallRuleManager.WaitForElevatedHelperExit(delegate(int timeoutMilliseconds)
                {
                    timeoutCalls++;
                    observedTimeout = timeoutMilliseconds;
                    return false;
                });
            Assert(timedOut == FirewallRuleManager.ElevatedHelperWaitOutcome.TimedOut
                && timeoutCalls == 1
                && observedTimeout == 120000,
                "a false bounded wait is reported as timeout without an unbounded retry");
            string timeoutMessage = FirewallRuleManager.BuildElevatedHelperTimeoutMessage(
                "현재 상태 조회를 완료했습니다.");
            Assert(timeoutMessage.Contains("120초")
                && timeoutMessage.Contains("강제 종료하지 않았")
                && timeoutMessage.Contains("아직 진행 중일 수")
                && timeoutMessage.Contains("현재 상태 조회를 완료했습니다."),
                "timeout failure clearly reports the limit, no-kill policy, and state-query result");

            bool nullRejected = false;
            try
            {
                FirewallRuleManager.WaitForElevatedHelperExit(null);
            }
            catch (ArgumentNullException)
            {
                nullRejected = true;
            }
            Assert(nullRejected, "elevated helper wait policy rejects a missing wait implementation");
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

        private static bool TryParseBatchAddToken(string token, out IList<string> parsed)
        {
            MethodInfo method = typeof(FirewallRuleManager).GetMethod(
                "TryParseBatchAddToken",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(
                typeof(FirewallRuleManager).FullName,
                "TryParseBatchAddToken");

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
