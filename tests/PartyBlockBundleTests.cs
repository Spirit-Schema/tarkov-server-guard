// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TarkovServerReporter.Tests
{
    internal static class PartyBlockBundleTests
    {
        private static int _failures;

        private static int Main()
        {
            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "TarkovServerGuard-PartyBlockBundleTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporaryRoot);
                TestParser();
                TestBaselineAndOverlapLifecycle(Path.Combine(temporaryRoot, "lifecycle"));
                TestPartialPendingReconcile(Path.Combine(temporaryRoot, "pending"));
                TestPromotionAndManualForget(Path.Combine(temporaryRoot, "promotion"));
                TestBackupRecoveryAndFailClosed(Path.Combine(temporaryRoot, "recovery"));
                TestOwnershipFlagsMustBeExplicit(Path.Combine(temporaryRoot, "ownership-flags"));
                TestFailedSavePreservesOwnership(Path.Combine(temporaryRoot, "failed-save"));
                TestFailedRollbackRequiresRecovery(Path.Combine(temporaryRoot, "failed-rollback"));
                TestEveryReleaseOrder(Path.Combine(temporaryRoot, "release-orders"));
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
                ? "ALL PARTY BLOCK BUNDLE TESTS PASSED"
                : _failures + " PARTY BLOCK BUNDLE TEST(S) FAILED");
            return _failures == 0 ? 0 : 1;
        }

        private static void TestEveryReleaseOrder(string directory)
        {
            int[][] orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            const string ip = "1.1.1.1";
            for (int mode = 0; mode < 3; mode++)
            for (int order = 0; order < orders.Length; order++)
            {
                string path = Path.Combine(directory, mode + "-" + order);
                var store = new PartyBlockBundleStore(path);
                var ids = new List<string>();
                bool blocked = mode == 1; // Existing personal block before any party list.
                for (int index = 0; index < 3; index++)
                {
                    var begin = store.BeginApply("Party " + index, "party-member", new[] { ip },
                        blocked ? States(Blocked(ip)) : States(Unblocked(ip)));
                    Assert(begin.Success && begin.AddressesToChange.Count == (blocked ? 0 : 1),
                        "overlapping lists only create the initially missing block");
                    ids.Add(begin.BundleId);
                    blocked = true;
                    Assert(store.CompleteApply(begin.BundleId, States(Blocked(ip)), BatchSuccess(ip)).Success,
                        "each party list is saved before the simulated restart");
                    store = new PartyBlockBundleStore(path);
                }
                if (mode == 2)
                    Assert(store.PromoteAddresses(new[] { ip }).Success, "personal promotion persists across release orders");
                for (int index = 0; index < 3; index++)
                {
                    string id = ids[orders[order][index]];
                    var plan = store.BeginRelease(id, States(Blocked(ip)));
                    bool shouldRemove = mode == 0 && index == 2;
                    Assert(plan.Success && plan.AddressesToChange.Count == (shouldRemove ? 1 : 0),
                        "all six release orders preserve personal and remaining party references");
                    store = new PartyBlockBundleStore(path);
                    if (shouldRemove)
                    {
                        // Elevation canceled: the rule still exists. Reopen the app and retry.
                        var canceled = store.CompleteRelease(id, States(Blocked(ip)), null);
                        Assert(canceled.Success && !canceled.BundleCompleted,
                            "a canceled final removal must retain its retryable journal");
                        store = new PartyBlockBundleStore(path);
                        var retry = store.BeginRelease(id, States(Blocked(ip)));
                        Assert(retry.Success && retry.AddressesToChange.SequenceEqual(new[] { ip }),
                            "retry after restart still owns only the final party block");
                        Assert(store.CompleteRelease(id, States(Unblocked(ip)), BatchSuccess(ip)).BundleCompleted,
                            "confirmed retry completes the final release");
                        blocked = false;
                    }
                    Assert(store.LoadSnapshot().Bundles.Count == 2 - index,
                        "one and only one party list is removed per release");
                }
                Assert(blocked == (mode != 0) && store.LoadSnapshot().AddressLeases.Count == 0,
                    "finished party lists leave personal rules intact and clear their own metadata");
            }
        }

        private static void TestParser()
        {
            PartyBlockInputParseResult parsed = PartyBlockInputParser.Parse(
                "8.8.8.8, 1.1.1.1\r\n8.8.8.8;10.0.0.1 127.0.0.1 "
                + "9.9.9.9/32 8.8.4.4:443 example.com");
            Assert(parsed.UniqueAddresses.SequenceEqual(new[] { "8.8.8.8", "1.1.1.1" }),
                "parser preserves public IPv4 order and deduplicates");
            Assert(parsed.Items.Count(item => item.Status == PartyBlockInputTokenStatus.Duplicate) == 1,
                "parser reports duplicate tokens in preview");
            Assert(parsed.Items.Count(
                    item => item.Status == PartyBlockInputTokenStatus.InvalidPublicIpv4) == 5,
                "parser rejects private, loopback, CIDR, host:port, and hostname tokens");

            string overLimit = string.Join(" ", CreateUniqueAddresses(
                PartyBlockInputParser.MaximumAddressCount + 1));
            parsed = PartyBlockInputParser.Parse(overLimit);
            Assert(parsed.UniqueAddresses.Count == PartyBlockInputParser.MaximumAddressCount
                && parsed.Items.Count(
                    item => item.Status == PartyBlockInputTokenStatus.AddressLimitExceeded) == 1,
                "parser enforces the same 1024-address batch boundary as the firewall helper");

            parsed = PartyBlockInputParser.Parse(new string('1',
                PartyBlockInputParser.MaximumInputLength + 1));
            Assert(parsed.UniqueAddresses.Count == 0
                && parsed.Items.Count == 1
                && parsed.Items[0].Status == PartyBlockInputTokenStatus.InputTooLong,
                "parser rejects oversized pasted input before tokenization");
        }

        private static void TestBaselineAndOverlapLifecycle(string directory)
        {
            var store = new PartyBlockBundleStore(directory);
            const string baselineIp = "8.8.8.8";
            const string sharedIp = "1.1.1.1";

            PartyBlockStoreResult first = store.BeginApply(
                "Alpha",
                "party-leader",
                new[] { baselineIp, sharedIp },
                States(Blocked(baselineIp), Unblocked(sharedIp)));
            Assert(first.Success
                && first.AddressesToChange.SequenceEqual(new[] { sharedIp }),
                "first bundle journals only addresses that were not already blocked");
            PartyBlockStoreResult firstComplete = store.CompleteApply(
                first.BundleId,
                States(Blocked(baselineIp), Blocked(sharedIp)),
                BatchSuccess(sharedIp));
            Assert(firstComplete.Success && firstComplete.PendingCount == 0,
                "verified first bundle becomes active");

            PartyBlockStoreResult second = store.BeginApply(
                "Bravo",
                "party-member",
                new[] { sharedIp },
                States(Blocked(sharedIp)));
            Assert(second.Success && second.AddressesToChange.Count == 0,
                "overlapping bundle reuses the existing managed block without another change");
            PartyBlockBundleSnapshot snapshot = store.LoadSnapshot();
            PartyBlockAddressLease sharedLease = snapshot.AddressLeases.Single(
                item => item.IpAddress == sharedIp);
            Assert(!sharedLease.WasBlockedBeforeFirstBundle && sharedLease.BundleIds.Count == 2,
                "overlap retains the original pre-first-bundle baseline and both references");

            PartyBlockReleasePreview firstPreview = store.CreateReleasePreview(
                first.BundleId,
                States(Blocked(baselineIp), Blocked(sharedIp)));
            Assert(firstPreview.Success
                && firstPreview.Items.Single(item => item.IpAddress == baselineIp).Disposition
                    == PartyBlockReleaseDisposition.PreserveBaseline
                && firstPreview.Items.Single(item => item.IpAddress == sharedIp).Disposition
                    == PartyBlockReleaseDisposition.PreserveOverlap,
                "release preview preserves both a pre-existing block and an overlapping reference");
            PartyBlockStoreResult firstRelease = store.BeginRelease(
                first.BundleId,
                States(Blocked(baselineIp), Blocked(sharedIp)));
            Assert(firstRelease.Success
                && firstRelease.BundleCompleted
                && firstRelease.AddressesToChange.Count == 0,
                "ending the first overlapping bundle never requests firewall removal");

            PartyBlockReleasePreview secondPreview = store.CreateReleasePreview(
                second.BundleId,
                States(Blocked(sharedIp)));
            Assert(secondPreview.Items.Single().Disposition == PartyBlockReleaseDisposition.Remove,
                "last non-baseline reference is eligible for removal");
            PartyBlockStoreResult secondRelease = store.BeginRelease(
                second.BundleId,
                States(Blocked(sharedIp)));
            Assert(secondRelease.AddressesToChange.SequenceEqual(new[] { sharedIp }),
                "last reference produces one removal candidate");
            PartyBlockStoreResult secondComplete = store.CompleteRelease(
                second.BundleId,
                States(Unblocked(sharedIp)),
                BatchSuccess(sharedIp));
            Assert(secondComplete.Success && secondComplete.BundleCompleted,
                "verified last-reference removal completes the bundle");
            snapshot = store.LoadSnapshot();
            Assert(snapshot.Success && snapshot.Bundles.Count == 0 && snapshot.AddressLeases.Count == 0,
                "completed bundles leave no stale address lease metadata");
        }

        private static void TestPartialPendingReconcile(string directory)
        {
            var store = new PartyBlockBundleStore(directory);
            const string firstIp = "1.0.0.1";
            const string secondIp = "9.9.9.9";
            PartyBlockStoreResult begin = store.BeginApply(
                "Charlie",
                "manual-confirmed",
                new[] { firstIp, secondIp },
                States(Unblocked(firstIp), Unblocked(secondIp)));
            PartyBlockStoreResult partial = store.CompleteApply(
                begin.BundleId,
                States(Blocked(firstIp), Unblocked(secondIp)),
                BatchPartial(firstIp, secondIp));
            Assert(partial.Success && partial.ActiveCount == 1 && partial.PendingCount == 1,
                "partial apply remains durably pending per IP");
            PartyBlockBundleSnapshot snapshot = store.LoadSnapshot();
            Assert(snapshot.Bundles.Single().State == PartyBlockBundleLifecycleState.NeedsReconcile,
                "partial apply exposes needs-reconcile bundle state");

            PartyBlockStoreResult reconciled = store.ReconcileStates(
                States(Blocked(firstIp), Blocked(secondIp)));
            Assert(reconciled.Success && reconciled.PendingCount == 0,
                "later read-only state verification activates a delayed helper result");

            PartyBlockStoreResult release = store.BeginRelease(
                begin.BundleId,
                States(Blocked(firstIp), Blocked(secondIp)));
            Assert(release.AddressesToChange.Count == 2,
                "release batches all last-reference non-baseline addresses");
            PartyBlockStoreResult partialRelease = store.CompleteRelease(
                begin.BundleId,
                States(Unblocked(firstIp), Blocked(secondIp)),
                BatchPartial(firstIp, secondIp));
            Assert(partialRelease.Success
                && !partialRelease.BundleCompleted
                && partialRelease.PendingCount == 1,
                "partial removal keeps only the unresolved member pending");
            PartyBlockStoreResult finalReconcile = store.ReconcileStates(
                States(Unblocked(secondIp)));
            Assert(finalReconcile.Success && store.LoadSnapshot().Bundles.Count == 0,
                "later missing-rule verification completes pending removal");
        }

        private static void TestPromotionAndManualForget(string directory)
        {
            var store = new PartyBlockBundleStore(directory);
            const string promotedIp = "8.8.4.4";
            PartyBlockStoreResult begin = store.BeginApply(
                "Delta",
                "party-leader",
                new[] { promotedIp },
                States(Unblocked(promotedIp)));
            store.CompleteApply(
                begin.BundleId,
                States(Blocked(promotedIp)),
                BatchSuccess(promotedIp));
            Assert(store.PromoteAddresses(new[] { promotedIp }).Success,
                "an explicitly restored/permanent address can be promoted");
            PartyBlockReleasePreview preview = store.CreateReleasePreview(
                begin.BundleId,
                States(Blocked(promotedIp)));
            Assert(preview.Items.Single().Disposition
                    == PartyBlockReleaseDisposition.PreservePermanent,
                "promoted address is preserved when its party bundle ends");

            PartyBlockStoreResult ended = store.BeginRelease(
                begin.BundleId,
                States(Blocked(promotedIp)));
            Assert(ended.Success && ended.BundleCompleted && ended.AddressesToChange.Count == 0,
                "promoted address never enters the removal batch");

            const string forgottenIp = "208.67.222.222";
            PartyBlockStoreResult another = store.BeginApply(
                "Echo",
                "party-member",
                new[] { forgottenIp },
                States(Blocked(forgottenIp)));
            Assert(store.ForgetAddresses(new[] { forgottenIp }).Success
                && store.LoadSnapshot().Bundles.All(item => item.BundleId != another.BundleId),
                "explicit generic unblock detaches stale party membership");
        }

        private static void TestBackupRecoveryAndFailClosed(string directory)
        {
            var store = new PartyBlockBundleStore(directory);
            const string ip = "1.1.1.1";
            PartyBlockStoreResult begin = store.BeginApply(
                "Foxtrot",
                "party-leader",
                new[] { ip },
                States(Blocked(ip)));
            Assert(begin.Success && File.Exists(store.StorePath) && File.Exists(store.BackupPath),
                "store writes both primary and recovery copy before any elevation");

            File.WriteAllText(store.StorePath, "{ damaged primary", new UTF8Encoding(false));
            Assert(store.LoadSnapshot().Success && store.LoadSnapshot().Bundles.Count == 1,
                "valid backup recovers a damaged primary");
            Assert(store.PromoteAddresses(new[] { ip }).Success,
                "mutation from backup repairs the primary atomically");

            File.WriteAllText(store.StorePath, "{ damaged primary", new UTF8Encoding(false));
            File.WriteAllText(store.BackupPath, "{ damaged backup", new UTF8Encoding(false));
            byte[] primaryBefore = File.ReadAllBytes(store.StorePath);
            byte[] backupBefore = File.ReadAllBytes(store.BackupPath);
            PartyBlockStoreResult rejected = store.BeginApply(
                "Golf",
                "party-member",
                new[] { "9.9.9.9" },
                States(Unblocked("9.9.9.9")));
            Assert(!rejected.Success
                && primaryBefore.SequenceEqual(File.ReadAllBytes(store.StorePath))
                && backupBefore.SequenceEqual(File.ReadAllBytes(store.BackupPath)),
                "corrupt primary and backup fail closed without overwriting recovery evidence");
        }

        private static void TestOwnershipFlagsMustBeExplicit(string root)
        {
            var store = new PartyBlockBundleStore(root);
            const string ip = "8.8.8.8";
            PartyBlockStoreResult initial = store.BeginApply("Preexisting block", "party-leader",
                new[] { ip }, States(Blocked(ip)));
            Assert(initial.Success, "ownership fixture creates a baseline-protected bundle");
            string valid = File.ReadAllText(store.StorePath, Encoding.UTF8);
            foreach (string damaged in new[]
            {
                valid.Replace("\"BaselineBlocked\":true,", string.Empty),
                valid.Replace("\"Preserve\":false,", string.Empty),
                valid.Replace("\"BaselineBlocked\":true", "\"BaselineBlocked\":null"),
                valid.Replace("\"Preserve\":false", "\"Preserve\":null"),
                valid.Replace("\"BaselineBlocked\":true", "\"BaselineBlocked\":\"false\""),
                valid.Replace("\"Preserve\":false", "\"Preserve\":\"false\""),
                valid.Replace("\"BaselineBlocked\":true", "\"BaselineBlocked\":0"),
                valid.Replace("\"Preserve\":false", "\"Preserve\":0")
            })
            {
                Assert(damaged != valid, "ownership fixture damages an actual protection flag");
                File.WriteAllText(store.StorePath, damaged, new UTF8Encoding(false));
                File.WriteAllText(store.BackupPath, valid, new UTF8Encoding(false));
                PartyBlockReleasePreview recovered = store.CreateReleasePreview(initial.BundleId, States(Blocked(ip)));
                Assert(recovered.Success && recovered.Items.Single().Disposition == PartyBlockReleaseDisposition.PreserveBaseline,
                    "missing ownership flags recover from the intact backup without authorizing removal");
                File.WriteAllText(store.BackupPath, damaged, new UTF8Encoding(false));
                Assert(!store.LoadSnapshot().Success
                    && !store.BeginRelease(initial.BundleId, States(Blocked(ip))).Success,
                    "missing ownership flags in both copies fail closed before a release can be planned");
            }
        }

        private static void TestFailedSavePreservesOwnership(string root)
        {
            var store = new PartyBlockBundleStore(root);
            const string ip = "1.1.1.1";
            Assert(store.BeginApply("Initial", "party-member", new[] { ip }, States(Unblocked(ip))).Success,
                "failed-save fixture creates one temporary lease");
            byte[] primary = File.ReadAllBytes(store.StorePath);
            byte[] backup = File.ReadAllBytes(store.BackupPath);
            foreach (string lockedPath in new[] { store.BackupPath, store.StorePath })
            {
                PartyBlockStoreResult result;
                using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    result = store.PromoteAddresses(new[] { ip });
                Assert(!result.Success, "a locked ownership file reports mutation failure");
                Assert(File.ReadAllBytes(store.StorePath).SequenceEqual(primary)
                    && File.ReadAllBytes(store.BackupPath).SequenceEqual(backup),
                    "a failed ownership save preserves both authoritative and recovery bytes");
                Assert(!store.LoadSnapshot().AddressLeases.Single().PreserveAfterBundles,
                    "a reported failed promotion does not silently become active on reload");
                // Reset the exact fixture so every failure case remains independent.
                File.WriteAllBytes(store.StorePath, primary);
                File.WriteAllBytes(store.BackupPath, backup);
            }
            byte[] damagedPrimary = Encoding.UTF8.GetBytes("{ damaged primary");
            File.WriteAllBytes(store.StorePath, damagedPrimary);
            PartyBlockStoreResult recoveryMutation;
            using (var locked = new FileStream(store.StorePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                recoveryMutation = store.PromoteAddresses(new[] { ip });
            Assert(!recoveryMutation.Success
                && File.ReadAllBytes(store.StorePath).SequenceEqual(damagedPrimary)
                && File.ReadAllBytes(store.BackupPath).SequenceEqual(backup)
                && !store.LoadSnapshot().AddressLeases.Single().PreserveAfterBundles,
                "failed mutation from a backup preserves the only valid old ownership copy");
            Assert(store.PromoteAddresses(new[] { ip }).Success
                && store.LoadSnapshot().AddressLeases.Single().PreserveAfterBundles,
                "the same ownership change succeeds after the transient file lock is released");
            Assert(Directory.GetFiles(root).All(path => path == store.StorePath || path == store.BackupPath),
                "successful and rolled-back saves clean staged files without deleting the durable copies");
        }

        private static void TestFailedRollbackRequiresRecovery(string root)
        {
            var initialStore = new PartyBlockBundleStore(root);
            const string personalIp = "8.8.8.8";
            PartyBlockStoreResult initial = initialStore.BeginApply("Protected baseline", "party-leader",
                new[] { personalIp }, States(Blocked(personalIp)));
            Assert(initial.Success, "rollback-failure fixture preserves an existing personal block");
            byte[] validBackup = File.ReadAllBytes(initialStore.BackupPath);
            byte[] corruptPrimary = Encoding.UTF8.GetBytes("{ corrupt primary");
            File.WriteAllBytes(initialStore.StorePath, corruptPrimary);
            FileStream primaryLock = null;
            FileStream backupLock = null;
            var store = new PartyBlockBundleStore(root, delegate
            {
                // This exact boundary lets real Windows sharing locks reject
                // both the final primary commit and the backup rollback.
                primaryLock = new FileStream(initialStore.StorePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                backupLock = new FileStream(initialStore.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            });
            PartyBlockStoreResult result;
            try
            {
                result = store.BeginApply("Must not commit", "party-member",
                    new[] { "9.9.9.9" }, States(Unblocked("9.9.9.9")));
            }
            finally
            {
                if (primaryLock != null) primaryLock.Dispose();
                if (backupLock != null) backupLock.Dispose();
            }
            string[] recoveryFiles = Directory.GetFiles(root, "party-block-bundles.json.bak.previous.*");
            Assert(!result.Success && recoveryFiles.Length == 1
                && File.ReadAllBytes(recoveryFiles[0]).SequenceEqual(validBackup),
                "failed backup rollback retains the exact prior ownership bytes for recovery");
            Assert(File.ReadAllBytes(store.StorePath).SequenceEqual(corruptPrimary),
                "failed primary commit does not overwrite the original damaged evidence");
            var reopened = new PartyBlockBundleStore(root);
            PartyBlockBundleSnapshot snapshot = reopened.LoadSnapshot();
            Assert(!snapshot.Success && snapshot.ErrorMessage.Contains("unresolved recovery files"),
                "reopening explicitly reports unresolved recovery instead of trusting the uncommitted backup");
            Assert(!reopened.BeginRelease(initial.BundleId, States(Blocked(personalIp))).Success
                && !reopened.PromoteAddresses(new[] { personalIp }).Success,
                "unresolved recovery refuses release and other ownership mutation");
            Assert(Directory.GetFiles(root, "party-block-bundles.json.bak.previous.*").Length == 1
                && File.ReadAllBytes(recoveryFiles[0]).SequenceEqual(validBackup),
                "reads and rejected mutations never clean up the only valid prior ownership copy");
        }

        private static Dictionary<string, FirewallQueryResult> States(
            params KeyValuePair<string, FirewallQueryResult>[] items)
        {
            return items.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        private static KeyValuePair<string, FirewallQueryResult> Blocked(string ipAddress)
        {
            return new KeyValuePair<string, FirewallQueryResult>(
                ipAddress,
                new FirewallQueryResult { Success = true, IsBlocked = true });
        }

        private static KeyValuePair<string, FirewallQueryResult> Unblocked(string ipAddress)
        {
            return new KeyValuePair<string, FirewallQueryResult>(
                ipAddress,
                new FirewallQueryResult { Success = true });
        }

        private static FirewallBatchChangeResult BatchSuccess(params string[] ipAddresses)
        {
            return new FirewallBatchChangeResult
            {
                Success = true,
                Items = ipAddresses.Select(ip => new FirewallBatchItemResult
                {
                    IpAddress = ip,
                    Success = true
                }).ToList()
            };
        }

        private static FirewallBatchChangeResult BatchPartial(string successIp, string failedIp)
        {
            return new FirewallBatchChangeResult
            {
                Items = new List<FirewallBatchItemResult>
                {
                    new FirewallBatchItemResult { IpAddress = successIp, Success = true },
                    new FirewallBatchItemResult
                    {
                        IpAddress = failedIp,
                        ErrorMessage = "simulated intermediate failure"
                    }
                }
            };
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
                        "TarkovServerGuard-PartyBlockBundleTests-",
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
