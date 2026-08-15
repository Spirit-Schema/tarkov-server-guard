// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TarkovServerReporter;

internal static class DbIpLiteGeoTests
{
    private static int _failures;

    private static int Main()
    {
        Run("synthetic MMDB local lookup", TestLocalLookup);
        Run("candidate URL and public-address boundary", TestBoundaries);
        Run("consent gate performs no download", TestConsentGate);
        Run("gzip update is validated and committed", TestSuccessfulUpdate);
        Run("only a missing release falls back to prior month", TestReleaseFallbackBoundary);
        Run("failed first install is throttled without a large retry loop", TestFailedInstallThrottle);
        Run("future attempt timestamp does not suppress an update", TestFutureAttemptDoesNotThrottle);
        Run("recent failed update with an installed database keeps the 24-hour throttle",
            TestInstalledDatabaseFailureThrottle);
        Run("cancelled update restores retry eligibility", TestCanceledUpdateRestoresState);
        Run("transport timeout remains a throttled failure", TestTransportTimeoutIsThrottled);
        Run("owned crash residue is cleaned safely", TestStartupCleanup);
        Run("dispose cancels and joins an active update", TestDisposeUpdateRace);
        Run("invalid update preserves existing database", TestInvalidUpdatePreservesDatabase);
        Run("atomic replace retains the old primary as backup", TestAtomicBackupReplacement);
        Run("commit decision and replace share the lookup lock", TestCommitLookupSerialization);
        Run("corrupt primary falls back to backup", TestBackupFallback);
        Run("lazy corrupt primary retries the backup", TestLazyCorruptPrimaryFallback);
        Run("current-month lazy corrupt primary is repaired immediately",
            TestCurrentMonthLazyCorruptPrimaryRepair);
        Run("failed current-month primary repair keeps the 24-hour throttle",
            TestFailedCurrentMonthPrimaryRepairThrottle);

        Console.WriteLine(_failures == 0
            ? "All DB-IP Lite tests passed."
            : _failures + " DB-IP Lite test(s) failed.");
        return _failures == 0 ? 0 : 1;
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
            Console.WriteLine("FAIL: " + name + "\n" + exception);
        }
    }

    private static void TestLocalLookup()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(root, "test.mmdb");
            File.WriteAllBytes(path, BuildDatabase(IPAddress.Parse("8.8.8.8")));
            using (var reader = new DbIpLiteMmdbReader(path))
            {
                IDictionary<string, object> record = reader.Lookup(IPAddress.Parse("8.8.8.8"));
                Assert(record != null, "Target record was not found.");
                Assert(reader.Lookup(IPAddress.Parse("8.8.4.4")) == null,
                    "Unmapped IP unexpectedly matched.");
                Assert(reader.DatabaseType == "DBIP-City-Lite-Test", "Unexpected database type.");
            }

            string ipv6Path = Path.Combine(root, "ipv6-early.mmdb");
            File.WriteAllBytes(ipv6Path, BuildIpv6EarlyDataDatabase());
            using (var reader = new DbIpLiteMmdbReader(ipv6Path))
                Assert(reader.Lookup(IPAddress.Parse("8.8.8.8")) != null,
                    "IPv4-in-IPv6 early data pointer was not resolved.");

            string metadataPointerPath = Path.Combine(root, "metadata-pointer.mmdb");
            File.WriteAllBytes(metadataPointerPath, BuildMetadataPointerDatabase());
            using (var reader = new DbIpLiteMmdbReader(metadataPointerPath))
                Assert(reader.DatabaseType == "DBIP-City-Lite-Test",
                    "Metadata-relative pointer base is incorrect.");

            string store = Path.Combine(root, "store");
            Directory.CreateDirectory(store);
            File.Copy(path, Path.Combine(store, "dbip-city-lite.mmdb"));
            using (var service = new DbIpLiteGeoService(store, new CountingDownloadClient(null)))
            {
                GeoInfo geo = service.Lookup("8.8.8.8");
                Assert(geo.Success, "Local GeoInfo lookup failed: " + geo.ErrorMessage);
                Assert(geo.CountryCode == "KR", "Country code mismatch.");
                Assert(geo.Country == "대한민국", "Korean country name was not preferred.");
                Assert(geo.Region == "서울특별시", "Region mismatch.");
                Assert(geo.City == "서울", "City mismatch.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestBoundaries()
    {
        Uri uri = DbIpLiteGeoService.BuildDownloadUri("2026-08");
        Assert(uri.AbsoluteUri == "https://download.db-ip.com/free/dbip-city-lite-2026-08.mmdb.gz",
            "Download URL mismatch.");
        AssertThrows<ArgumentException>(delegate { DbIpLiteGeoService.BuildDownloadUri("2026/08"); });
        IList<string> months = DbIpLiteGeoService.GetCandidateReleaseMonths(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert(string.Join(",", months) == "2026-01,2025-12,2025-11", "Month rollover mismatch.");

        Assert(DbIpLiteGeoService.IsPublicAddress(IPAddress.Parse("8.8.8.8")), "Public IPv4 rejected.");
        Assert(!DbIpLiteGeoService.IsPublicAddress(IPAddress.Parse("127.0.0.1")), "Loopback accepted.");
        Assert(!DbIpLiteGeoService.IsPublicAddress(IPAddress.Parse("10.0.0.1")), "Private IPv4 accepted.");
        Assert(!DbIpLiteGeoService.IsPublicAddress(IPAddress.Parse("100.64.0.1")), "CGNAT accepted.");
        Assert(!DbIpLiteGeoService.IsPublicAddress(IPAddress.Parse("203.0.113.1")), "Documentation IP accepted.");
        Assert(!DbIpLiteGeoService.IsPublicAddress(IPAddress.Parse("::1")), "IPv6 loopback accepted.");
    }

    private static void TestConsentGate()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var download = new CountingDownloadClient(null);
            using (var service = new DbIpLiteGeoService(root, download))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(false, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.ConsentRequired, "Consent was not required.");
                Assert(download.CallCount == 0, "Downloader was called without consent.");
                Assert(!Directory.Exists(Path.Combine(root, "DbIpLite")), "Unexpected directory created.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestSuccessfulUpdate()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            byte[] gzip = Gzip(BuildDatabase(IPAddress.Parse("8.8.8.8")));
            var download = new CountingDownloadClient(gzip);
            using (var service = new DbIpLiteGeoService(root, download))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated, "Update failed: " + result.ErrorMessage);
                Assert(download.CallCount == 1, "Unexpected candidate download count.");
                Assert(File.Exists(service.DatabasePath), "Database was not committed.");
                Assert(service.Lookup("8.8.8.8").Success, "Committed database cannot be queried.");
                string state = File.ReadAllText(Path.Combine(root, "update-state.json"));
                Assert(state.IndexOf("sha256", StringComparison.OrdinalIgnoreCase) >= 0,
                    "SHA-256 state was not recorded.");
                Assert(!Directory.GetFiles(root).Any(delegate(string item)
                {
                    return Path.GetFileName(item).StartsWith("download-", StringComparison.Ordinal)
                        || Path.GetFileName(item).StartsWith("database-", StringComparison.Ordinal);
                }), "Temporary download files remain.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestInvalidUpdatePreservesDatabase()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(root);
            string databasePath = Path.Combine(root, "dbip-city-lite.mmdb");
            byte[] valid = BuildDatabase(IPAddress.Parse("8.8.8.8"));
            File.WriteAllBytes(databasePath, valid);
            byte[] original = File.ReadAllBytes(databasePath);
            var download = new CountingDownloadClient(Gzip(Enumerable.Repeat((byte)0x41, 4096).ToArray()));
            using (var service = new DbIpLiteGeoService(root, download))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Failed, "Invalid update was accepted.");
                Assert(download.CallCount == 1,
                    "An installed usable DB should not trigger older-month re-downloads.");
                Assert(File.ReadAllBytes(databasePath).SequenceEqual(original),
                    "Existing database changed after failed validation.");
                Assert(service.Lookup("8.8.8.8").Success, "Existing database no longer works.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestReleaseFallbackBoundary()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            byte[] validGzip = Gzip(BuildDatabase(IPAddress.Parse("8.8.8.8")));
            var fallback = new SequenceDownloadClient(true, validGzip);
            using (var service = new DbIpLiteGeoService(root, fallback))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated, "Prior-month fallback failed.");
                Assert(fallback.CallCount == 2, "404 fallback did not try exactly one prior month.");
            }

            string invalidRoot = Path.Combine(root, "invalid");
            var invalid = new CountingDownloadClient(Gzip(Enumerable.Repeat((byte)0x41, 4096).ToArray()));
            using (var service = new DbIpLiteGeoService(invalidRoot, invalid))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Failed, "Invalid MMDB was accepted.");
                Assert(invalid.CallCount == 1, "Invalid content caused multiple large downloads.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestBackupFallback()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb.bak"),
                BuildDatabase(IPAddress.Parse("8.8.8.8")));
            var download = new CountingDownloadClient(Gzip(BuildDatabase(IPAddress.Parse("8.8.8.8"))));
            using (var service = new DbIpLiteGeoService(root, download))
            {
                Assert(service.Lookup("8.8.8.8").Success, "Backup database was not used.");
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(true, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated, "Replacement update failed.");
            }
            using (var backup = new DbIpLiteMmdbReader(Path.Combine(root, "dbip-city-lite.mmdb.bak")))
                Assert(backup.Lookup(IPAddress.Parse("8.8.8.8")) != null,
                    "Good backup was overwritten by a corrupt primary.");
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestAtomicBackupReplacement()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb"),
                BuildDatabase(IPAddress.Parse("8.8.8.8")));
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb.bak"),
                BuildDatabase(IPAddress.Parse("9.9.9.9")));
            var download = new CountingDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("1.1.1.1"))));
            using (var service = new DbIpLiteGeoService(root, download))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated, "Atomic update failed.");
            }

            using (var primary = new DbIpLiteMmdbReader(Path.Combine(root, "dbip-city-lite.mmdb")))
                Assert(primary.Lookup(IPAddress.Parse("1.1.1.1")) != null,
                    "New primary was not installed.");
            using (var backup = new DbIpLiteMmdbReader(Path.Combine(root, "dbip-city-lite.mmdb.bak")))
            {
                Assert(backup.Lookup(IPAddress.Parse("8.8.8.8")) != null,
                    "Old primary was not retained as backup.");
                Assert(backup.Lookup(IPAddress.Parse("9.9.9.9")) == null,
                    "Previous backup was not replaced atomically.");
            }
            Assert(!Directory.GetFiles(root).Any(delegate(string path)
            {
                return Path.GetFileName(path).StartsWith(
                    "dbip-city-lite.mmdb.bak.tmp.", StringComparison.Ordinal);
            }), "Full-size backup temporary copy remains.");
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestCommitLookupSerialization()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string primaryPath = Path.Combine(root, "dbip-city-lite.mmdb");
            File.WriteAllBytes(primaryPath, BuildDatabase(IPAddress.Parse("8.8.8.8")));
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb.bak"),
                BuildDatabase(IPAddress.Parse("9.9.9.9")));
            var download = new PausingPayloadDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("1.1.1.1"))));
            using (var service = new DbIpLiteGeoService(root, download))
            {
                FieldInfo syncField = typeof(DbIpLiteGeoService).GetField(
                    "_sync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(syncField != null, "Service lookup lock was not found.");
                object sync = syncField.GetValue(service);
                Task<DbIpLiteUpdateResult> update = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None);
                Assert(download.Started.Wait(5000), "Update did not reach the paused downloader.");

                Monitor.Enter(sync);
                try
                {
                    download.Release.Set();
                    Assert(download.FinishedWrite.Wait(5000), "Paused payload was not written.");

                    bool staged = false;
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < deadline)
                    {
                        string candidate = Directory.GetFiles(root, "database-*.mmdb")
                            .FirstOrDefault(delegate(string path) { return new FileInfo(path).Length > 1024; });
                        if (candidate != null) { staged = true; break; }
                        using (var primary = new DbIpLiteMmdbReader(primaryPath))
                            Assert(primary.Lookup(IPAddress.Parse("1.1.1.1")) == null,
                                "Primary was replaced outside the lookup lock.");
                        Thread.Sleep(10);
                    }
                    Assert(staged, "Validated update did not reach the staged commit boundary.");
                    Thread.Sleep(250);
                    Assert(Directory.GetFiles(root, "database-*.mmdb").Length == 1,
                        "Staged database was consumed while the lookup lock was held.");
                    using (var primary = new DbIpLiteMmdbReader(primaryPath))
                        Assert(primary.Lookup(IPAddress.Parse("8.8.8.8")) != null,
                            "Old primary changed while the lookup lock was held.");
                }
                finally { Monitor.Exit(sync); }

                DbIpLiteUpdateResult result = update.GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated,
                    "Serialized update did not finish after releasing the lookup lock.");
                using (var primary = new DbIpLiteMmdbReader(primaryPath))
                    Assert(primary.Lookup(IPAddress.Parse("1.1.1.1")) != null,
                        "New primary was not committed after releasing the lookup lock.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestFailedInstallThrottle()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var invalid = new CountingDownloadClient(Gzip(Enumerable.Repeat((byte)0x41, 4096).ToArray()));
            using (var service = new DbIpLiteGeoService(root, invalid))
                Assert(service.UpdateInBackgroundIfDueAsync(true, CancellationToken.None)
                    .GetAwaiter().GetResult().Status == DbIpLiteUpdateStatus.Failed,
                    "Initial invalid install did not fail.");
            Assert(invalid.CallCount == 1, "Initial invalid install downloaded unexpectedly often.");

            var retry = new CountingDownloadClient(Gzip(BuildDatabase(IPAddress.Parse("8.8.8.8"))));
            using (var service = new DbIpLiteGeoService(root, retry))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Failed,
                    "Failed install was not throttled.");
                Assert(retry.CallCount == 0, "First-install throttle still performed a large download.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestFutureAttemptDoesNotThrottle()
    {
        string root = CreateTemporaryDirectory();
        DateTime nowUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb"),
                BuildDatabase(IPAddress.Parse("8.8.8.8")));
            WriteUpdateState(
                root, "2026-07", nowUtc.AddDays(30), nowUtc.AddMonths(-1));
            var download = new CountingDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("1.1.1.1"))));
            using (var service = new DbIpLiteGeoService(
                root, download, delegate { return nowUtc; }))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated,
                    "Future advisory timestamp incorrectly throttled the update.");
                Assert(download.CallCount == 1,
                    "Clock correction did not permit exactly one update attempt.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestInstalledDatabaseFailureThrottle()
    {
        string root = CreateTemporaryDirectory();
        DateTime nowUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb"),
                BuildDatabase(IPAddress.Parse("8.8.8.8")));
            WriteUpdateState(
                root, "2026-07", nowUtc.AddHours(-1), nowUtc.AddMonths(-1));
            var download = new CountingDownloadClient(null);
            using (var service = new DbIpLiteGeoService(
                root, download, delegate { return nowUtc; }))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.NotDue,
                    "Installed database failure did not retain its 24-hour throttle.");
                Assert(download.CallCount == 0,
                    "Throttled installed database performed a download.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestCanceledUpdateRestoresState()
    {
        string root = CreateTemporaryDirectory();
        DateTime nowUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            WriteUpdateState(root, "2026-06", nowUtc.AddHours(-2), null);
            var download = new BlockingDownloadClient();
            using (var cancellation = new CancellationTokenSource())
            using (var service = new DbIpLiteGeoService(
                root, download, delegate { return nowUtc; }))
            {
                Task<DbIpLiteUpdateResult> update = service.UpdateInBackgroundIfDueAsync(
                    true, cancellation.Token);
                Assert(download.Started.Wait(5000),
                    "Canceled update did not reach the downloader.");
                cancellation.Cancel();
                AssertThrows<OperationCanceledException>(delegate
                {
                    update.GetAwaiter().GetResult();
                });
            }

            var retry = new CountingDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("8.8.8.8"))));
            using (var service = new DbIpLiteGeoService(
                root, retry, delegate { return nowUtc; }))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated,
                    "Cancellation left a failed-attempt throttle behind.");
                Assert(retry.CallCount == 1,
                    "Canceled update was not immediately retryable.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestTransportTimeoutIsThrottled()
    {
        string root = CreateTemporaryDirectory();
        DateTime nowUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            using (var service = new DbIpLiteGeoService(
                root, new TimeoutDownloadClient(), delegate { return nowUtc; }))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Failed,
                    "Transport timeout was treated as a user cancellation.");
            }

            var retry = new CountingDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("8.8.8.8"))));
            using (var service = new DbIpLiteGeoService(
                root, retry, delegate { return nowUtc; }))
            {
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Failed,
                    "Transport timeout did not retain the missing-database throttle.");
                Assert(retry.CallCount == 0,
                    "Transport timeout allowed an immediate large retry.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestStartupCleanup()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            string guid = Guid.NewGuid().ToString("N");
            string download = Path.Combine(root, "download-" + guid + ".mmdb.gz");
            string database = Path.Combine(root, "database-" + guid + ".mmdb");
            string state = Path.Combine(root, "update-state.json.tmp." + guid);
            string backup = Path.Combine(root, "dbip-city-lite.mmdb.bak.tmp." + guid);
            string unrelated = Path.Combine(root, "download-not-a-guid.mmdb.gz");
            foreach (string path in new[] { download, database, state, backup, unrelated })
                File.WriteAllText(path, "residue");

            string lockPath = Path.Combine(root, "update.lock");
            using (var activeLock = new FileStream(
                lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var service = new DbIpLiteGeoService(root, new CountingDownloadClient(null)))
                Assert(File.Exists(download), "Startup cleanup interfered with an active updater.");

            using (var service = new DbIpLiteGeoService(root, new CountingDownloadClient(null))) { }
            Assert(!File.Exists(download) && !File.Exists(database)
                && !File.Exists(state) && !File.Exists(backup), "Owned crash residue remains.");
            Assert(File.Exists(unrelated), "Cleanup deleted an unrelated similarly named file.");
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestLazyCorruptPrimaryFallback()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            byte[] corrupt = BuildDatabase(IPAddress.Parse("8.8.8.8"));
            corrupt[208] = 0x27;
            corrupt[209] = 0xFF;
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb"), corrupt);
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb.bak"),
                BuildDatabase(IPAddress.Parse("8.8.8.8")));
            var download = new CountingDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("1.1.1.1"))));
            using (var service = new DbIpLiteGeoService(root, download))
            {
                GeoInfo first = service.Lookup("8.8.8.8");
                Assert(first.Success, "Lookup did not retry the backup after lazy corruption.");
                GeoInfo second = service.Lookup("8.8.8.8");
                Assert(second.Success, "Rejected primary was selected again.");
                DbIpLiteUpdateResult update = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(update.Status == DbIpLiteUpdateStatus.Updated,
                    "Update after lazy corruption failed.");
            }
            using (var backup = new DbIpLiteMmdbReader(Path.Combine(root, "dbip-city-lite.mmdb.bak")))
                Assert(backup.Lookup(IPAddress.Parse("8.8.8.8")) != null,
                    "Rejected lazy-corrupt primary overwrote the good rollback backup.");
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestCurrentMonthLazyCorruptPrimaryRepair()
    {
        string root = CreateTemporaryDirectory();
        DateTime nowUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            byte[] corrupt = BuildDatabase(IPAddress.Parse("8.8.8.8"));
            corrupt[208] = 0x27;
            corrupt[209] = 0xFF;
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb"), corrupt);
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb.bak"),
                BuildDatabase(IPAddress.Parse("8.8.8.8")));
            WriteUpdateState(
                root, "2026-08", nowUtc.AddMinutes(-5), nowUtc.AddMinutes(-5));

            var download = new CountingDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("1.1.1.1"))));
            using (var service = new DbIpLiteGeoService(
                root, download, delegate { return nowUtc; }))
            {
                Assert(service.Lookup("8.8.8.8").Success,
                    "Lazy corrupt primary did not activate the backup.");
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.Updated,
                    "Current-month state hid a rejected primary from repair.");
                Assert(download.CallCount == 1,
                    "Rejected primary was not repaired with exactly one download.");
                Assert(service.Lookup("1.1.1.1").Success,
                    "Repaired primary was not activated.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestFailedCurrentMonthPrimaryRepairThrottle()
    {
        string root = CreateTemporaryDirectory();
        DateTime nowUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc);
        try
        {
            byte[] corrupt = BuildDatabase(IPAddress.Parse("8.8.8.8"));
            corrupt[208] = 0x27;
            corrupt[209] = 0xFF;
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb"), corrupt);
            File.WriteAllBytes(Path.Combine(root, "dbip-city-lite.mmdb.bak"),
                BuildDatabase(IPAddress.Parse("8.8.8.8")));
            WriteUpdateState(
                root, "2026-08", nowUtc.AddMinutes(-5), nowUtc.AddMinutes(-5));

            var invalid = new CountingDownloadClient(
                Gzip(Enumerable.Repeat((byte)0x41, 4096).ToArray()));
            using (var service = new DbIpLiteGeoService(
                root, invalid, delegate { return nowUtc; }))
            {
                Assert(service.Lookup("8.8.8.8").Success,
                    "Lazy corrupt primary did not activate the backup before failed repair.");
                Assert(service.UpdateInBackgroundIfDueAsync(true, CancellationToken.None)
                    .GetAwaiter().GetResult().Status == DbIpLiteUpdateStatus.Failed,
                    "Invalid repair payload unexpectedly succeeded.");
                Assert(invalid.CallCount == 1, "Initial repair did not make exactly one attempt.");
            }

            var retry = new CountingDownloadClient(
                Gzip(BuildDatabase(IPAddress.Parse("1.1.1.1"))));
            using (var service = new DbIpLiteGeoService(
                root, retry, delegate { return nowUtc.AddHours(1); }))
            {
                Assert(service.Lookup("8.8.8.8").Success,
                    "Backup was unavailable after a failed repair.");
                DbIpLiteUpdateResult result = service.UpdateInBackgroundIfDueAsync(
                    true, CancellationToken.None).GetAwaiter().GetResult();
                Assert(result.Status == DbIpLiteUpdateStatus.NotDue,
                    "Failed primary repair did not retain the 24-hour throttle.");
                Assert(retry.CallCount == 0,
                    "Failed primary repair triggered another large download too soon.");
            }
        }
        finally { DeleteDirectory(root); }
    }

    private static void TestDisposeUpdateRace()
    {
        string root = CreateTemporaryDirectory();
        try
        {
            var download = new BlockingDownloadClient();
            var service = new DbIpLiteGeoService(root, download);
            Task<DbIpLiteUpdateResult> update = service.UpdateInBackgroundIfDueAsync(
                true, CancellationToken.None);
            Assert(download.Started.Wait(5000), "Update did not reach the downloader.");
            Task dispose = Task.Run((Action)service.Dispose);
            try { update.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            Assert(dispose.Wait(5000), "Dispose did not join the canceled update.");
            Assert(download.Disposed, "Download client was not disposed after the update stopped.");
        }
        finally { DeleteDirectory(root); }
    }

    private static byte[] BuildDatabase(IPAddress targetAddress)
    {
        byte[] address = targetAddress.GetAddressBytes();
        if (address.Length != 4) throw new ArgumentException("Synthetic builder only supports IPv4.");
        const int nodeCount = 32;
        var output = new MemoryStream();
        for (int bitIndex = 0; bitIndex < nodeCount; bitIndex++)
        {
            int currentBit = (address[bitIndex / 8] >> (7 - bitIndex % 8)) & 1;
            int matchingPointer = bitIndex == nodeCount - 1 ? nodeCount + 16 : bitIndex + 1;
            int left = currentBit == 0 ? matchingPointer : nodeCount;
            int right = currentBit == 1 ? matchingPointer : nodeCount;
            Write24(output, left);
            Write24(output, right);
        }
        output.Write(new byte[16], 0, 16);

        var record = new Dictionary<string, object>
        {
            { "country", new Dictionary<string, object>
                {
                    { "iso_code", "KR" },
                    { "names", new Dictionary<string, object>
                        {
                            { "ko", "대한민국" },
                            { "en", "South Korea" }
                        }
                    }
                }
            },
            { "subdivisions", new List<object>
                {
                    new Dictionary<string, object>
                    {
                        { "names", new Dictionary<string, object>
                            {
                                { "ko", "서울특별시" },
                                { "en", "Seoul" }
                            }
                        }
                    }
                }
            },
            { "city", new Dictionary<string, object>
                {
                    { "names", new Dictionary<string, object>
                        {
                            { "ko", "서울" },
                            { "en", "Seoul" }
                        }
                    }
                }
            }
        };
        WriteValue(output, record);
        while (output.Length < 2048) output.WriteByte(0);
        byte[] marker = new byte[]
        {
            0xAB, 0xCD, 0xEF,
            (byte)'M', (byte)'a', (byte)'x', (byte)'M', (byte)'i', (byte)'n', (byte)'d', (byte)'.',
            (byte)'c', (byte)'o', (byte)'m'
        };
        output.Write(marker, 0, marker.Length);
        WriteValue(output, new Dictionary<string, object>
        {
            { "binary_format_major_version", 2L },
            { "binary_format_minor_version", 0L },
            { "build_epoch", 1785542400L },
            { "database_type", "DBIP-City-Lite-Test" },
            { "ip_version", 4L },
            { "node_count", 32L },
            { "record_size", 24L }
        });
        return output.ToArray();
    }

    private static byte[] BuildIpv6EarlyDataDatabase()
    {
        var output = new MemoryStream();
        Write24(output, 17); // node_count(1) + 16-byte separator offset
        Write24(output, 1);  // empty record
        output.Write(new byte[16], 0, 16);
        WriteValue(output, new Dictionary<string, object>
        {
            { "country", new Dictionary<string, object>
                {
                    { "iso_code", "KR" },
                    { "names", new Dictionary<string, object> { { "en", "South Korea" } } }
                }
            }
        });
        while (output.Length < 2048) output.WriteByte(0);
        byte[] marker = new byte[]
        {
            0xAB, 0xCD, 0xEF,
            (byte)'M', (byte)'a', (byte)'x', (byte)'M', (byte)'i', (byte)'n', (byte)'d', (byte)'.',
            (byte)'c', (byte)'o', (byte)'m'
        };
        output.Write(marker, 0, marker.Length);
        WriteValue(output, new Dictionary<string, object>
        {
            { "binary_format_major_version", 2L },
            { "database_type", "DBIP-City-Lite-Test" },
            { "ip_version", 6L },
            { "node_count", 1L },
            { "record_size", 24L }
        });
        return output.ToArray();
    }

    private static byte[] BuildMetadataPointerDatabase()
    {
        byte[] normal = BuildDatabase(IPAddress.Parse("8.8.8.8"));
        const int metadataStart = 2048 + 14;
        var output = new MemoryStream();
        output.Write(normal, 0, metadataStart);

        var metadata = new MemoryStream();
        WriteControl(metadata, 7, 7);
        WriteValue(metadata, "binary_format_major_version"); WriteValue(metadata, 2L);
        WriteValue(metadata, "binary_format_minor_version"); WriteValue(metadata, 0L);
        WriteValue(metadata, "build_epoch"); WriteValue(metadata, 1785542400L);
        WriteValue(metadata, "database_type");
        metadata.WriteByte(0x20); // type pointer, one-byte pointer payload
        long pointerByteOffset = metadata.Position;
        metadata.WriteByte(0);
        WriteValue(metadata, "ip_version"); WriteValue(metadata, 4L);
        WriteValue(metadata, "node_count"); WriteValue(metadata, 32L);
        WriteValue(metadata, "record_size"); WriteValue(metadata, 24L);
        long stringOffset = metadata.Length;
        if (stringOffset > 255) throw new InvalidOperationException("Synthetic metadata pointer is too large.");
        metadata.Position = pointerByteOffset;
        metadata.WriteByte((byte)stringOffset);
        metadata.Position = metadata.Length;
        WriteValue(metadata, "DBIP-City-Lite-Test");
        byte[] metadataBytes = metadata.ToArray();
        output.Write(metadataBytes, 0, metadataBytes.Length);
        return output.ToArray();
    }

    private static void WriteValue(Stream stream, object value)
    {
        string text = value as string;
        if (text != null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            WriteControl(stream, 2, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            return;
        }

        IDictionary<string, object> map = value as IDictionary<string, object>;
        if (map != null)
        {
            WriteControl(stream, 7, map.Count);
            foreach (KeyValuePair<string, object> item in map)
            {
                WriteValue(stream, item.Key);
                WriteValue(stream, item.Value);
            }
            return;
        }

        IList<object> list = value as IList<object>;
        if (list != null)
        {
            WriteControl(stream, 11, list.Count);
            foreach (object item in list) WriteValue(stream, item);
            return;
        }

        if (value is long)
        {
            long number = (long)value;
            int size = number <= byte.MaxValue ? 1 : (number <= ushort.MaxValue ? 2 : 4);
            WriteControl(stream, 6, size);
            for (int shift = (size - 1) * 8; shift >= 0; shift -= 8)
                stream.WriteByte((byte)(number >> shift));
            return;
        }
        throw new InvalidOperationException("Unsupported synthetic MMDB value.");
    }

    private static void WriteControl(Stream stream, int type, int size)
    {
        if (size >= 29) throw new InvalidOperationException("Synthetic value is too large.");
        if (type <= 7)
            stream.WriteByte((byte)((type << 5) | size));
        else
        {
            stream.WriteByte((byte)size);
            stream.WriteByte((byte)(type - 7));
        }
    }

    private static void Write24(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static byte[] Gzip(byte[] bytes)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
            gzip.Write(bytes, 0, bytes.Length);
        return output.ToArray();
    }

    private static void WriteUpdateState(
        string root,
        string releaseMonth,
        DateTime? lastAttemptUtc,
        DateTime? lastSuccessUtc)
    {
        string json = string.Format(CultureInfo.InvariantCulture,
            "{{\"Version\":1,\"ReleaseMonth\":\"{0}\","
                + "\"LastAttemptUtc\":{1},\"LastSuccessUtc\":{2},"
                + "\"BuildUtc\":null,\"Sha256\":null,\"DatabaseType\":null}}",
            releaseMonth,
            FormatJsonDate(lastAttemptUtc),
            FormatJsonDate(lastSuccessUtc));
        File.WriteAllText(
            Path.Combine(root, "update-state.json"), json, new UTF8Encoding(false));
    }

    private static string FormatJsonDate(DateTime? value)
    {
        return value.HasValue
            ? "\"" + value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + "\""
            : "null";
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "TarkovServerGuard-DbIpTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private sealed class CountingDownloadClient : IDbIpLiteDownloadClient
    {
        private readonly byte[] _payload;
        public int CallCount { get; private set; }

        public CountingDownloadClient(byte[] payload)
        {
            _payload = payload;
        }

        public Task DownloadAsync(Uri uri, Stream destination, long maximumBytes, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_payload == null) throw new InvalidOperationException("Network access was not expected.");
            if (_payload.LongLength > maximumBytes) throw new InvalidDataException("Test payload too large.");
            destination.Write(_payload, 0, _payload.Length);
            return Task.FromResult(0);
        }
    }

    private sealed class TimeoutDownloadClient : IDbIpLiteDownloadClient
    {
        public Task DownloadAsync(
            Uri uri,
            Stream destination,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<int>();
            completion.SetCanceled();
            return completion.Task;
        }
    }

    private sealed class SequenceDownloadClient : IDbIpLiteDownloadClient
    {
        private readonly bool _firstNotFound;
        private readonly byte[] _payload;
        public int CallCount { get; private set; }

        public SequenceDownloadClient(bool firstNotFound, byte[] payload)
        {
            _firstNotFound = firstNotFound;
            _payload = payload;
        }

        public Task DownloadAsync(Uri uri, Stream destination, long maximumBytes, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_firstNotFound && CallCount == 1)
                throw new DbIpLiteDownloadNotFoundException("synthetic 404");
            destination.Write(_payload, 0, _payload.Length);
            return Task.FromResult(0);
        }
    }

    private sealed class BlockingDownloadClient : IDbIpLiteDownloadClient, IDisposable
    {
        private readonly TaskCompletionSource<int> _completion = new TaskCompletionSource<int>();
        public ManualResetEventSlim Started { get; private set; }
        public bool Disposed { get; private set; }

        public BlockingDownloadClient()
        {
            Started = new ManualResetEventSlim(false);
        }

        public Task DownloadAsync(Uri uri, Stream destination, long maximumBytes, CancellationToken cancellationToken)
        {
            Started.Set();
            cancellationToken.Register(delegate { _completion.TrySetCanceled(); });
            return _completion.Task;
        }

        public void Dispose()
        {
            Disposed = true;
            Started.Dispose();
        }
    }

    private sealed class PausingPayloadDownloadClient : IDbIpLiteDownloadClient, IDisposable
    {
        private readonly byte[] _payload;
        public ManualResetEventSlim Started { get; private set; }
        public ManualResetEventSlim Release { get; private set; }
        public ManualResetEventSlim FinishedWrite { get; private set; }

        public PausingPayloadDownloadClient(byte[] payload)
        {
            _payload = payload;
            Started = new ManualResetEventSlim(false);
            Release = new ManualResetEventSlim(false);
            FinishedWrite = new ManualResetEventSlim(false);
        }

        public Task DownloadAsync(Uri uri, Stream destination, long maximumBytes, CancellationToken cancellationToken)
        {
            return Task.Run(delegate
            {
                Started.Set();
                Release.Wait(cancellationToken);
                destination.Write(_payload, 0, _payload.Length);
                FinishedWrite.Set();
            }, cancellationToken);
        }

        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
            FinishedWrite.Dispose();
        }
    }
}
