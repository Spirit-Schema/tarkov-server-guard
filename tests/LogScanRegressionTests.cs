// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace TarkovServerReporter.Tests
{
    // Reflection keeps these regressions tied to the deliverable executable.
    internal static class LogScanRegressionTests
    {
        private static Assembly _application;
        private static Type _raidScanner;
        private static Type _legacyScanner;
        private static int _assertions;
        private static int _failures;
        private static readonly DateTime LogTime = new DateTime(2026, 1, 15, 10, 0, 0);

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            bool benchmark = args.Length >= 1 && args[0] == "--benchmark";
            if ((!benchmark && args.Length != 1) || (benchmark && args.Length != 3))
            {
                Console.Error.WriteLine("Usage: LogScanRegressionTests <app.exe> OR --benchmark <app.exe> <report.txt>");
                return 2;
            }
            string testRoot = Path.Combine(Path.GetTempPath(), "TSG-LogScanRegression-" + Guid.NewGuid().ToString("N"));
            try
            {
                _application = Assembly.LoadFrom(Path.GetFullPath(args[benchmark ? 1 : 0]));
                _raidScanner = _application.GetType("TarkovServerReporter.RaidLogScanner", true);
                _legacyScanner = _application.GetType("TarkovServerReporter.LogScanner", true);
                Directory.CreateDirectory(testRoot);
                if (benchmark) return Benchmark(testRoot, args[2]);
                Run("older file metadata invalidates both caches", delegate { TestOlderFileChange(testRoot); });
                Run("renamed file invalidates cached source path", delegate { TestRenamedFile(testRoot); });
                Run("one unreadable folder preserves other history", delegate { TestUnreadableFolder(testRoot); });
                Run("legacy partial read is retried on the next scan", delegate { TestLegacyPartialRead(testRoot); });
                Run("streaming snapshots bound growth and release handles", delegate { TestStreamingSnapshot(testRoot); });
                Run("bounded caches preserve all history and recent warm entries", delegate { TestCacheBudget(testRoot); });
                Run("oversized folder remains complete without cache retention", delegate { TestLargeFolder(testRoot); });
                Console.WriteLine("LogScanRegressionTests: " + (_failures == 0 ? "PASS" : "FAIL")
                    + " (" + _assertions + " assertions; " + _failures + " failed cases)");
                return _failures == 0 ? 0 : 1;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            finally
            {
                try
                {
                    string resolvedRoot = Path.GetFullPath(testRoot);
                    string temporaryRoot = Path.GetFullPath(Path.GetTempPath())
                        .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (resolvedRoot.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase)
                        && Path.GetFileName(resolvedRoot).StartsWith("TSG-LogScanRegression-", StringComparison.Ordinal))
                        Directory.Delete(resolvedRoot, true);
                }
                catch { }
            }
        }

        private static void Run(string name, Action action)
        {
            try { ClearCaches(); action(); Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { _failures++; Console.Error.WriteLine("FAIL: " + name + "\n" + exception); }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            _assertions++;
        }

        private static string SessionLine(string ip, int index)
        {
            return LogTime.AddSeconds(index).ToString("yyyy.MM.dd HH:mm:ss", CultureInfo.InvariantCulture)
                + "|1.1.0.1.46699|Debug|application|NetworkGameCreate profileStatus: 'RaidMode: Online, Ip: "
                + ip + ", Port: 17000, Location: Woods, Sid: JP-TK02G005_case-" + index
                + ", GameMode: deathmatch, shortId: CASE" + index + "'\r\n";
        }

        private static string CreateFolder(string root, string name, int index)
        {
            string folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "application.log");
            File.WriteAllText(path, SessionLine("203.0.113.10", index), Encoding.UTF8);
            File.SetLastWriteTimeUtc(path, LogTime.AddMinutes(index));
            Directory.SetCreationTimeUtc(folder, LogTime.AddMinutes(index));
            Directory.SetLastWriteTimeUtc(folder, LogTime.AddMinutes(index));
            return folder;
        }

        private static object ScanResult(string root)
        {
            Type pathsType = _application.GetType("TarkovServerReporter.TarkovLogPaths", true);
            Type queryType = _application.GetType("TarkovServerReporter.RaidLogScanQuery", true);
            object paths = Activator.CreateInstance(pathsType);
            pathsType.GetProperty("EftPath").SetValue(paths, root, null);
            object query = Activator.CreateInstance(queryType);
            queryType.GetProperty("MaximumRecords").SetValue(query, int.MaxValue, null);
            return _raidScanner.GetMethod("Scan", new[] { pathsType, queryType }).Invoke(null, new[] { paths, query });
        }

        private static IList Sessions(object result) { return (IList)Property(result, "Sessions"); }
        private static object Property(object instance, string name) { return instance.GetType().GetProperty(name).GetValue(instance, null); }
        private static IList ScanLegacy(string root) { return (IList)_legacyScanner.GetMethod("Scan").Invoke(null, new object[] { root, int.MaxValue }); }
        private static IDictionary Cache(Type scanner, string name) { return (IDictionary)scanner.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null); }
        private static void ClearCaches() { Cache(_raidScanner, "DirectoryCache").Clear(); Cache(_legacyScanner, "SessionCache").Clear(); }

        private static void TestOlderFileChange(string testRoot)
        {
            string root = Path.Combine(testRoot, "metadata");
            string folder = CreateFolder(root, "raid", 0);
            string app = Path.Combine(folder, "application.log");
            string later = Path.Combine(folder, "application-later.log");
            File.WriteAllText(later, "unrelated diagnostic line\r\n", Encoding.UTF8);
            File.SetLastWriteTimeUtc(later, LogTime.AddDays(2));
            Assert((string)Property(Sessions(ScanResult(root))[0], "IpAddress") == "203.0.113.10", "Initial raid IP is wrong.");
            Assert((string)Property(ScanLegacy(root)[0], "IpAddress") == "203.0.113.10", "Initial legacy IP is wrong.");
            long previousLength = new FileInfo(app).Length;
            File.WriteAllText(app, SessionLine("203.0.113.11", 0), Encoding.UTF8);
            File.SetLastWriteTimeUtc(app, LogTime.AddHours(1));
            Assert(new FileInfo(app).Length == previousLength, "Fixture must preserve combined byte count.");
            Assert((string)Property(Sessions(ScanResult(root))[0], "IpAddress") == "203.0.113.11", "Raid cache hid an older file change beneath the newest timestamp.");
            Assert((string)Property(ScanLegacy(root)[0], "IpAddress") == "203.0.113.11", "Legacy cache hid an older file change beneath the newest timestamp.");
        }

        private static void TestRenamedFile(string testRoot)
        {
            string root = Path.Combine(testRoot, "rename");
            string folder = CreateFolder(root, "raid", 0);
            string original = Path.Combine(folder, "application.log");
            ScanResult(root);
            ScanLegacy(root);
            string renamed = Path.Combine(folder, "application-rotated.log");
            File.Move(original, renamed);
            Assert((string)Property(Sessions(ScanResult(root))[0], "LogFilePath") == renamed, "Raid cache returned a source path that no longer exists.");
            Assert((string)Property(ScanLegacy(root)[0], "LogFilePath") == renamed, "Legacy cache returned a source path that no longer exists.");
        }

        private static void TestUnreadableFolder(string testRoot)
        {
            string root = Path.Combine(testRoot, "sharing");
            CreateFolder(root, "older", 0);
            CreateFolder(root, "middle", 1);
            string newest = CreateFolder(root, "newest", 2);
            using (new FileStream(Path.Combine(newest, "application.log"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                object result = ScanResult(root);
                Assert(Sessions(result).Count == 2, "A locked newest folder hid other readable history.");
                Assert(!(bool)Property(result, "ScanCompletedWithoutErrors"), "An unreadable folder was reported as a successful complete scan.");
                Assert(!(bool)Property(result, "TotalMatchingSessionsIsExact"), "Incomplete history was marked as an exact total.");
            }
            object recovered = ScanResult(root);
            Assert(Sessions(recovered).Count == 3 && (bool)Property(recovered, "ScanCompletedWithoutErrors"), "A readable folder did not recover on the next scan.");
        }

        private static void TestLegacyPartialRead(string testRoot)
        {
            string root = Path.Combine(testRoot, "legacy-sharing");
            string folder = CreateFolder(root, "raid", 0);
            string locked = Path.Combine(folder, "application-later.log");
            File.WriteAllText(locked, SessionLine("203.0.113.11", 1), Encoding.UTF8);
            File.SetLastWriteTimeUtc(locked, LogTime.AddMinutes(1));
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert((string)Property(ScanLegacy(root)[0], "IpAddress") == "203.0.113.10", "Readable rotated log should remain available during a partial scan.");
            Assert((string)Property(ScanLegacy(root)[0], "IpAddress") == "203.0.113.11", "A partial result was cached after the sharing violation ended.");
        }

        private static void TestStreamingSnapshot(string testRoot)
        {
            Type access = _application.GetType("TarkovServerReporter.LogFileAccess", false);
            Assert(access != null, "The scanner still materializes complete logs for every pass.");
            string path = Path.Combine(testRoot, "snapshot.log");
            string[] initial = Enumerable.Repeat(new string('x', 200), 10000).ToArray();
            File.WriteAllLines(path, initial, Encoding.UTF8);
            IEnumerable lines = (IEnumerable)access.GetMethod("ReadLines").Invoke(null, new object[] { path });
            IEnumerator iterator = lines.GetEnumerator();
            int count = 0;
            try
            {
                Assert(iterator.MoveNext(), "Snapshot fixture is empty.");
                count++;
                File.AppendAllText(path, "appended after this pass started\r\n", Encoding.UTF8);
                while (iterator.MoveNext()) count++;
                Assert(count == initial.Length, "An in-progress file append extended the scan beyond its opening snapshot.");
            }
            finally { ((IDisposable)iterator).Dispose(); }
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            iterator = ((IEnumerable)access.GetMethod("ReadLines").Invoke(null, new object[] { path })).GetEnumerator();
            Assert(iterator.MoveNext(), "Next scan did not start.");
            ((IDisposable)iterator).Dispose();
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Assert(true, "Abandoned enumerator released its file handle.");
            iterator = ((IEnumerable)access.GetMethod("ReadLines").Invoke(null, new object[] { path })).GetEnumerator();
            bool truncationRejected = false;
            try
            {
                Assert(iterator.MoveNext(), "Truncation fixture did not start.");
                using (new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
                try { while (iterator.MoveNext()) { } }
                catch (IOException) { truncationRejected = true; }
            }
            finally { ((IDisposable)iterator).Dispose(); }
            Assert(truncationRejected, "A truncated file silently became a successful complete snapshot.");
        }

        private static void TestCacheBudget(string testRoot)
        {
            string root = Path.Combine(testRoot, "cache-budget");
            const int count = 280;
            for (int index = 0; index < count; index++) CreateFolder(root, "raid-" + index.ToString("D3"), index);
            Assert(Sessions(ScanResult(root)).Count == count, "Cache budget truncated the complete history result.");
            Assert(ScanLegacy(root).Count == count, "Legacy cache budget truncated the complete history result.");
            Assert(Cache(_raidScanner, "DirectoryCache").Count <= 256, "Raid directory cache exceeded its retention budget.");
            Assert(Cache(_legacyScanner, "SessionCache").Count <= 256, "Legacy directory cache exceeded its retention budget.");
            string newest = Path.Combine(root, "raid-279", "application.log");
            using (new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                object warm = ScanResult(root);
                Assert(Sessions(warm).Count == count && (bool)Property(warm, "ScanCompletedWithoutErrors"), "A full-history pass evicted the recent warm raid result.");
                Assert(ScanLegacy(root).Count == count, "A full-history pass evicted the recent warm legacy result.");
            }
        }

        private static void TestLargeFolder(string testRoot)
        {
            string root = Path.Combine(testRoot, "large-folder");
            string folder = CreateFolder(root, "raid", 0);
            string file = Path.Combine(folder, "application.log");
            using (var writer = new StreamWriter(file, false, Encoding.UTF8))
                for (int index = 0; index < 2050; index++) writer.Write(SessionLine("203.0.113.10", index));
            object result = ScanResult(root);
            Assert(Sessions(result).Count == 2050, "Large-folder cache policy dropped raid records.");
            Assert(Cache(_raidScanner, "DirectoryCache").Count == 0, "An oversized per-folder result was retained indefinitely.");
        }

        private static int Benchmark(string root, string reportPath)
        {
            // Identical generated data and scan calls can be run against both
            // versions. Timings are evidence, not brittle pass/fail thresholds.
            string warmup = Path.Combine(root, "warmup");
            CreateFolder(warmup, "raid", 0);
            ScanResult(warmup);
            const int noiseLines = 80000;
            string payload = "2026.01.15 09:59:50|1.1.0.1.46699|Debug|application|Diagnostic message without session, matching, participant, report, or network evidence. " + new string('x', 96);
            var output = new StringBuilder();
            output.AppendLine("Assembly=" + _application.Location);
            output.AppendLine("Version=" + _application.GetName().Version);
            output.AppendLine("NoiseLinesPerColdScan=" + noiseLines);
            for (int trial = 0; trial < 3; trial++)
            {
                string logs = Path.Combine(root, "benchmark-" + trial);
                string folder = CreateFolder(logs, "raid", 0);
                string file = Path.Combine(folder, "application.log");
                using (var writer = new StreamWriter(file, true, Encoding.UTF8))
                    for (int index = 0; index < noiseLines; index++) writer.WriteLine(payload);
                ClearCaches();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                long baseline = GC.GetTotalMemory(false);
                long peak = baseline;
                int stop = 0;
                Thread sampler = new Thread(delegate()
                {
                    while (Interlocked.CompareExchange(ref stop, 0, 0) == 0)
                    {
                        long current = GC.GetTotalMemory(false);
                        if (current > peak) peak = current;
                        Thread.Sleep(2);
                    }
                });
                sampler.IsBackground = true;
                sampler.Start();
                int gen0 = GC.CollectionCount(0);
                var watch = Stopwatch.StartNew();
                object cold = ScanResult(logs);
                watch.Stop();
                Interlocked.Exchange(ref stop, 1);
                sampler.Join();
                Assert(Sessions(cold).Count == 1, "Benchmark workload changed raid results.");
                output.AppendLine("Trial" + trial + ".Bytes=" + new FileInfo(file).Length);
                output.AppendLine("Trial" + trial + ".ColdMs=" + watch.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
                output.AppendLine("Trial" + trial + ".PeakManagedDeltaBytes=" + (peak - baseline));
                output.AppendLine("Trial" + trial + ".Gen0Collections=" + (GC.CollectionCount(0) - gen0));
                watch.Restart();
                for (int iteration = 0; iteration < 25; iteration++)
                    Assert(Sessions(ScanResult(logs)).Count == 1, "Warm benchmark workload changed raid results.");
                watch.Stop();
                output.AppendLine("Trial" + trial + ".WarmMeanMs=" + (watch.Elapsed.TotalMilliseconds / 25).ToString("F3", CultureInfo.InvariantCulture));
            }
            File.WriteAllText(Path.GetFullPath(reportPath), output.ToString(), Encoding.UTF8);
            Console.Write(output.ToString());
            return 0;
        }
    }
}
