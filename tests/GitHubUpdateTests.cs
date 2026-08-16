// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TarkovServerReporter;

internal static class GitHubUpdateTests
{
    private static int _failures;

    private static int Main()
    {
        Run("semantic versions are strict and ordered", TestSemanticVersions);
        Run("production repository is fixed and token-free", TestFixedRepository);
        Run("GitHub checks add TLS 1.2 without replacing other protocols", TestTls12Boundary);
        Run("automatic update checks use a six-hour cadence", TestSixHourCheckCadence);
        Run("a successful no-update check records the six-hour cadence", TestNoUpdateCadence);
        Run("future clock state cannot suppress checks", TestFutureClockRecovery);
        Run("only newer stable releases are accepted", TestStableUpgradeBoundary);
        Run("later defers the same release for 24 hours", TestDeferral);
        Run("network failures preserve state and remain retryable", TestFailureBoundary);
        Run("cancelled checks preserve state and remain retryable", TestCancellationBoundary);
        Run("missing Velopack performs no persistent check", TestUnavailableEngine);
        Run("manual checks bypass cadence and same-version deferral", TestManualCheckBypassesSuppression);
        Run("manual checks report current and failure outcomes", TestManualCheckOutcomes);
        Run("manual and automatic checks share one single-flight gate", TestManualCheckSingleFlight);
        Run("update state is atomic and recovers from backup", TestStateBackupRecovery);
        Run("oversized update state is ignored safely", TestOversizedStateBoundary);
        Run("Velopack 1.2 runtime binding is compatible when present", TestVelopackRuntimeBinding);

        Console.WriteLine(_failures == 0
            ? "All GitHub update tests passed."
            : _failures + " GitHub update test(s) failed.");
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

    private static void TestSemanticVersions()
    {
        SemanticVersion current;
        SemanticVersion newer;
        SemanticVersion prerelease;
        Assert(SemanticVersion.TryParse("v0.7.1", out current), "Current version was rejected.");
        Assert(SemanticVersion.TryParse("0.7.2+build.15", out newer), "Build metadata was rejected.");
        Assert(SemanticVersion.TryParse("0.7.2-rc.1", out prerelease), "Prerelease was rejected.");
        Assert(newer.CompareTo(current) > 0, "Newer stable version was not greater.");
        Assert(prerelease.CompareTo(newer) < 0, "Prerelease precedence is incorrect.");
        Assert(!SemanticVersion.TryParse("0.7.1.0", out current), "Four-part version was accepted.");
        Assert(!SemanticVersion.TryParse("0.07.1", out current), "Leading zero was accepted.");
        Assert(!SemanticVersion.TryParse("0.7.2-rc.01", out current), "Invalid prerelease was accepted.");
    }

    private static void TestFixedRepository()
    {
        Assert(GitHubUpdateService.RepositoryUrl
            == "https://github.com/Spirit-Schema/tarkov-server-guard",
            "Unexpected production update repository.");
        AssertThrows<InvalidOperationException>(delegate
        {
            VelopackReflectionUpdateEngine.CreateGitHub("https://github.com/example/other");
        });
    }

    private static void TestSixHourCheckCadence()
    {
        var clock = new FakeClock(new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));
        var engine = new FakeEngine("0.7.2");
        var store = new MemoryStateStore();
        var service = new GitHubUpdateService("0.7.1", engine, store, clock);

        Assert(Get(service) != null, "First update check did not return the candidate.");
        Assert(engine.CheckCount == 1, "First update check count mismatch.");
        clock.UtcNowValue = clock.UtcNowValue.AddHours(5).AddMinutes(59);
        Assert(Get(service) == null, "A second check ran before six hours.");
        Assert(engine.CheckCount == 1, "Network boundary was entered before six hours.");
        clock.UtcNowValue = clock.UtcNowValue.AddMinutes(1);
        Assert(Get(service) != null, "The six-hour check did not run.");
        Assert(engine.CheckCount == 2, "Six-hour update check count mismatch.");
    }

    private static void TestTls12Boundary()
    {
        SecurityProtocolType original = ServicePointManager.SecurityProtocol;
        SecurityProtocolType observedAtCheck = 0;
        SecurityProtocolType observedAfterCheck = 0;
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
            var engine = new FakeEngine(null);
            var service = new GitHubUpdateService(
                "0.7.1",
                engine,
                new MemoryStateStore(),
                new FakeClock(new DateTime(2026, 8, 15, 1, 0, 0, DateTimeKind.Utc)));

            Assert(Get(service) == null, "TLS boundary check unexpectedly returned an update.");
            observedAtCheck = engine.SecurityProtocolAtLastCheck;
            observedAfterCheck = ServicePointManager.SecurityProtocol;
        }
        finally
        {
            ServicePointManager.SecurityProtocol = original;
        }

        SecurityProtocolType expected = SecurityProtocolType.Tls | SecurityProtocolType.Tls12;
        Assert(observedAtCheck == expected,
            "TLS 1.2 was not added before the update engine check or another protocol was replaced.");
        Assert(observedAfterCheck == expected,
            "TLS 1.2 update-check configuration did not preserve the existing protocol flags.");
        Assert(ServicePointManager.SecurityProtocol == original,
            "The TLS test did not restore the process-wide security protocol value.");
    }

    private static void TestFutureClockRecovery()
    {
        DateTime now = new DateTime(2026, 8, 15, 3, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var engine = new FakeEngine("0.7.2");
        var store = new MemoryStateStore
        {
            State = new UpdateCheckState { LastCheckUtc = now.AddYears(5) }
        };
        var service = new GitHubUpdateService("0.7.1", engine, store, clock);
        Assert(Get(service) != null, "A future timestamp suppressed the update check.");
        Assert(store.State.LastCheckUtc == now, "Future timestamp was not repaired.");
    }

    private static void TestNoUpdateCadence()
    {
        DateTime now = new DateTime(2026, 8, 15, 2, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var engine = new FakeEngine(null);
        var store = new MemoryStateStore
        {
            State = new UpdateCheckState
            {
                LastCheckUtc = now.AddDays(-2),
                DeferredVersion = "0.7.9",
                DeferredUntilUtc = now.AddHours(6)
            }
        };
        var service = new GitHubUpdateService("0.7.1", engine, store, clock);

        Assert(Get(service) == null, "A no-update result unexpectedly returned a candidate.");
        Assert(engine.CheckCount == 1, "The no-update check did not enter the engine once.");
        Assert(store.SaveCount == 1, "The successful no-update check was not persisted once.");
        Assert(store.State.LastCheckUtc == now, "The successful no-update timestamp was not recorded.");
        Assert(store.State.DeferredVersion == "0.7.9"
            && store.State.DeferredUntilUtc == now.AddHours(6),
            "The successful no-update check changed deferral state.");

        Assert(Get(service) == null, "A no-update result was checked again before six hours.");
        Assert(engine.CheckCount == 1, "The successful no-update check was not throttled.");
    }

    private static void TestStableUpgradeBoundary()
    {
        AssertCandidate("0.7.2", true);
        AssertCandidate("v0.7.1", false);
        AssertCandidate("0.7.0", false);
        AssertCandidate("0.8.0-beta.1", false);
        AssertCandidate("not-a-version", false);
    }

    private static void AssertCandidate(string candidate, bool expected)
    {
        DateTime now = new DateTime(2026, 8, 15, 4, 0, 0, DateTimeKind.Utc);
        var service = new GitHubUpdateService(
            "0.7.1",
            new FakeEngine(candidate),
            new MemoryStateStore(),
            new FakeClock(now));
        Assert((Get(service) != null) == expected, "Unexpected decision for " + candidate + ".");
    }

    private static void TestDeferral()
    {
        DateTime start = new DateTime(2026, 8, 15, 5, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(start);
        var engine = new FakeEngine("0.7.2");
        var store = new MemoryStateStore();
        var service = new GitHubUpdateService("0.7.1", engine, store, clock);
        Assert(Get(service) != null, "Initial candidate was missing.");
        service.Defer("0.7.2");
        Assert(store.State.DeferredVersion == "0.7.2", "Deferred version was not stored.");
        Assert(store.State.DeferredUntilUtc == start.AddHours(24), "Deferral duration is not 24 hours.");

        clock.UtcNowValue = start.AddHours(5).AddMinutes(59);
        Assert(Get(service) == null, "Deferred release was shown too soon.");
        Assert(engine.CheckCount == 1, "A deferred release caused an unnecessary check.");
        clock.UtcNowValue = start.AddHours(24);
        Assert(Get(service) != null, "Deferred release was not shown after 24 hours.");
    }

    private static void TestFailureBoundary()
    {
        DateTime start = new DateTime(2026, 8, 15, 6, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(start);
        var engine = new FakeEngine(null) { CheckException = new IOException("offline") };
        DateTime originalLastCheck = start.AddDays(-2);
        DateTime originalDeferral = start.AddHours(4);
        var store = new MemoryStateStore
        {
            State = new UpdateCheckState
            {
                LastCheckUtc = originalLastCheck,
                DeferredVersion = "0.7.9",
                DeferredUntilUtc = originalDeferral
            }
        };
        var service = new GitHubUpdateService("0.7.1", engine, store, clock);
        Assert(Get(service) == null, "Network failure escaped as an update.");
        Assert(engine.CheckCount == 1, "Network failure check count mismatch.");
        Assert(store.SaveCount == 0, "Network failure wrote update state.");
        Assert(store.State.LastCheckUtc == originalLastCheck
            && store.State.DeferredVersion == "0.7.9"
            && store.State.DeferredUntilUtc == originalDeferral,
            "Network failure changed existing update state.");

        Assert(Get(service) == null, "A repeated network failure escaped as an update.");
        Assert(engine.CheckCount == 2, "Network failure was not immediately retryable.");
        Assert(store.SaveCount == 0, "Repeated network failure wrote update state.");
    }

    private static void TestCancellationBoundary()
    {
        DateTime start = new DateTime(2026, 8, 15, 6, 30, 0, DateTimeKind.Utc);
        DateTime originalLastCheck = start.AddDays(-2);
        DateTime originalDeferral = start.AddHours(3);
        var clock = new FakeClock(start);
        var engine = new FakeEngine("0.7.2");
        var store = new MemoryStateStore
        {
            State = new UpdateCheckState
            {
                LastCheckUtc = originalLastCheck,
                DeferredVersion = "0.7.9",
                DeferredUntilUtc = originalDeferral
            }
        };
        var service = new GitHubUpdateService("0.7.1", engine, store, clock);
        using (var source = new CancellationTokenSource())
        {
            source.Cancel();
            AssertThrows<OperationCanceledException>(delegate
            {
                service.CheckForUpdateAsync(source.Token).GetAwaiter().GetResult();
            });
        }

        Assert(engine.CheckCount == 1, "Cancelled check did not enter the engine once.");
        Assert(store.SaveCount == 0, "Cancelled check wrote update state.");
        Assert(store.State.LastCheckUtc == originalLastCheck
            && store.State.DeferredVersion == "0.7.9"
            && store.State.DeferredUntilUtc == originalDeferral,
            "Cancelled check changed existing update state.");

        Assert(Get(service) != null, "A cancelled check was not immediately retryable.");
        Assert(engine.CheckCount == 2, "Retry after cancellation did not enter the engine.");
        Assert(store.State.LastCheckUtc == start,
            "Successful retry after cancellation did not record its timestamp.");
    }

    private static void TestUnavailableEngine()
    {
        var engine = new FakeEngine("0.7.2") { Available = false };
        var store = new MemoryStateStore();
        var service = new GitHubUpdateService(
            "0.7.1",
            engine,
            store,
            new FakeClock(new DateTime(2026, 8, 15, 7, 0, 0, DateTimeKind.Utc)));
        Assert(Get(service) == null, "Unavailable update engine returned an update.");
        Assert(engine.CheckCount == 0, "Unavailable update engine entered its network boundary.");
        Assert(store.SaveCount == 0, "Portable mode wrote a misleading check timestamp.");
    }

    private static void TestManualCheckBypassesSuppression()
    {
        DateTime now = new DateTime(2026, 8, 15, 7, 30, 0, DateTimeKind.Utc);
        var engine = new FakeEngine("0.7.2");
        var store = new MemoryStateStore
        {
            State = new UpdateCheckState
            {
                LastCheckUtc = now,
                DeferredVersion = "0.7.2",
                DeferredUntilUtc = now.AddHours(24)
            }
        };
        var service = new GitHubUpdateService(
            "0.7.1",
            engine,
            store,
            new FakeClock(now));

        Assert(Get(service) == null, "Automatic cadence suppression did not apply.");
        Assert(engine.CheckCount == 0, "Automatic suppressed check entered the engine.");

        ManualUpdateCheckResult result = GetManual(service);
        Assert(result.Status == ManualUpdateCheckStatus.UpdateAvailable,
            "Manual check did not report the deferred update.");
        Assert(result.Update != null && result.Update.VersionText == "0.7.2",
            "Manual update result did not carry normalized update data.");
        Assert(engine.CheckCount == 1, "Manual check did not enter the engine exactly once.");
        Assert(store.State.LastCheckUtc == now,
            "Successful manual check did not record its timestamp.");
        Assert(store.State.DeferredVersion == "0.7.2"
            && store.State.DeferredUntilUtc == now.AddHours(24),
            "Manual check changed the existing later deferral.");
    }

    private static void TestManualCheckOutcomes()
    {
        DateTime now = new DateTime(2026, 8, 15, 7, 45, 0, DateTimeKind.Utc);

        var currentStore = new MemoryStateStore();
        var current = new GitHubUpdateService(
            "0.7.1",
            new FakeEngine("0.7.1"),
            currentStore,
            new FakeClock(now));
        ManualUpdateCheckResult currentResult = GetManual(current);
        Assert(currentResult.Status == ManualUpdateCheckStatus.UpToDate
            && currentResult.Update == null,
            "Current release did not produce the up-to-date outcome.");
        Assert(currentStore.State.LastCheckUtc == now,
            "Successful current-version check did not record its timestamp.");

        var failureStore = new MemoryStateStore();
        var failed = new GitHubUpdateService(
            "0.7.1",
            new FakeEngine(null) { CheckException = new IOException("offline") },
            failureStore,
            new FakeClock(now));
        Assert(GetManual(failed).Status == ManualUpdateCheckStatus.Failed,
            "Network failure did not produce the failed outcome.");
        Assert(failureStore.SaveCount == 0,
            "Failed manual check wrote a successful-check timestamp.");

        var unavailableEngine = new FakeEngine("0.7.2") { Available = false };
        var unavailable = new GitHubUpdateService(
            "0.7.1",
            unavailableEngine,
            new MemoryStateStore(),
            new FakeClock(now));
        Assert(GetManual(unavailable).Status == ManualUpdateCheckStatus.Failed,
            "Unavailable update engine did not produce the failed outcome.");
        Assert(unavailableEngine.CheckCount == 0,
            "Unavailable update engine entered its network boundary.");

        var malformedStore = new MemoryStateStore();
        var malformed = new GitHubUpdateService(
            "0.7.1",
            new FakeEngine("invalid-version"),
            malformedStore,
            new FakeClock(now));
        Assert(GetManual(malformed).Status == ManualUpdateCheckStatus.Failed,
            "Malformed engine result did not produce the failed outcome.");
        Assert(malformedStore.SaveCount == 0,
            "Malformed engine result wrote a successful-check timestamp.");
    }

    private static void TestManualCheckSingleFlight()
    {
        DateTime now = new DateTime(2026, 8, 15, 7, 50, 0, DateTimeKind.Utc);
        var pending = new TaskCompletionSource<ApplicationUpdate>();
        var engine = new FakeEngine(null) { PendingCheck = pending.Task };
        var service = new GitHubUpdateService(
            "0.7.1",
            engine,
            new MemoryStateStore(),
            new FakeClock(now));

        Task<ManualUpdateCheckResult> first = service.CheckForUpdateManuallyAsync(
            CancellationToken.None);
        Assert(engine.CheckCount == 1, "First manual check did not acquire the gate.");

        ManualUpdateCheckResult second = GetManual(service);
        Assert(second.Status == ManualUpdateCheckStatus.AlreadyRunning,
            "Concurrent manual check did not report already-running.");
        Assert(Get(service) == null,
            "Concurrent automatic check escaped the shared single-flight gate.");
        Assert(engine.CheckCount == 1,
            "A concurrent check entered the engine more than once.");

        pending.SetResult(new ApplicationUpdate("0.7.2", new object()));
        ManualUpdateCheckResult completed = first.GetAwaiter().GetResult();
        Assert(completed.Status == ManualUpdateCheckStatus.UpdateAvailable,
            "First manual check did not complete after the gate was released.");
        Assert(GetManual(service).Status == ManualUpdateCheckStatus.UpdateAvailable,
            "Single-flight gate was not released after completion.");
        Assert(engine.CheckCount == 2,
            "A later manual check did not reacquire the released gate.");
    }

    private static void TestStateBackupRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "TarkovServerGuard-UpdateTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileUpdateCheckStateStore(root);
            DateTime first = new DateTime(2026, 8, 15, 8, 0, 0, DateTimeKind.Utc);
            DateTime second = first.AddDays(1);
            store.Save(new UpdateCheckState { LastCheckUtc = first, DeferredVersion = "0.7.2" });
            store.Save(new UpdateCheckState { LastCheckUtc = second, DeferredVersion = "0.7.3" });
            File.WriteAllText(Path.Combine(root, "update-check-state.json"), "not-json");
            UpdateCheckState recovered = store.Load();
            Assert(recovered.LastCheckUtc == first, "Backup timestamp was not recovered.");
            Assert(recovered.DeferredVersion == "0.7.2", "Backup version was not recovered.");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void TestOversizedStateBoundary()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "TarkovServerGuard-UpdateSizeTest-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(
                Path.Combine(root, "update-check-state.json"),
                new string('x', (64 * 1024) + 1));
            var store = new FileUpdateCheckStateStore(root);
            UpdateCheckState loaded = store.Load();
            Assert(loaded != null && loaded.LastCheckUtc == default(DateTime),
                "Oversized state was not rejected as an empty state.");
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch { }
        }
    }

    private static void TestVelopackRuntimeBinding()
    {
        VelopackReflectionUpdateEngine engine = VelopackReflectionUpdateEngine.CreateGitHub(
            GitHubUpdateService.RepositoryUrl);
        if (!engine.IsRuntimePresent)
        {
            Console.WriteLine("INFO: Velopack.dll is not beside this test executable; runtime probe skipped.");
            return;
        }

        MethodInfo factory = typeof(VelopackReflectionUpdateEngine).GetMethod(
            "GetOrCreateManager", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(factory != null, "Velopack manager factory is missing.");
        object manager = factory.Invoke(engine, null);
        Assert(manager != null && manager.GetType().FullName == "Velopack.UpdateManager",
            "Velopack UpdateManager binding failed.");
        PropertyInfo installed = manager.GetType().GetProperty(
            "IsInstalled", BindingFlags.Instance | BindingFlags.Public);
        Assert(installed != null, "Velopack IsInstalled boundary is missing.");
        bool installedValue = (bool)installed.GetValue(manager, null);
        Assert(engine.IsAvailable == installedValue,
            "Updater availability does not follow Velopack's installed/portable boundary.");

        PropertyInfo sourceProperty = manager.GetType().GetProperty(
            "Source", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(sourceProperty != null, "Velopack Source property is missing.");
        object source = sourceProperty.GetValue(manager, null);
        Assert(source != null && source.GetType().FullName == "Velopack.Sources.GithubSource",
            "Velopack GithubSource binding failed.");
        PropertyInfo prerelease = source.GetType().GetProperty(
            "Prerelease", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        PropertyInfo accessToken = source.GetType().GetProperty(
            "AccessToken", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(prerelease != null && !(bool)prerelease.GetValue(source, null),
            "Velopack prerelease filtering is not disabled.");
        Assert(accessToken != null && accessToken.GetValue(source, null) == null,
            "A GitHub access token was unexpectedly configured.");

        Type appType = manager.GetType().Assembly.GetType("Velopack.VelopackApp", false);
        Assert(appType != null, "VelopackApp startup dispatcher type is missing.");
        MethodInfo build = appType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
        Assert(build != null && build.GetParameters().Length == 0,
            "VelopackApp.Build startup contract is incompatible.");
        object builder = build.Invoke(null, null);
        Assert(builder != null, "VelopackApp.Build returned no startup builder.");
        MethodInfo afterUpdateHook = builder.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(item => item.Name == "OnAfterUpdateFastCallback"
                && item.GetParameters().Length == 1
                && typeof(Delegate).IsAssignableFrom(item.GetParameters()[0].ParameterType));
        Assert(afterUpdateHook != null,
            "Velopack after-update completion hook contract is incompatible.");
        MethodInfo startupRun = builder.GetType().GetMethods(
                BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(item => item.Name == "Run" && item.GetParameters().Length == 0);
        Assert(startupRun != null, "VelopackApp.Run startup contract is incompatible.");

        MethodInfo check = manager.GetType().GetMethod("CheckForUpdatesAsync", Type.EmptyTypes);
        MethodInfo download = manager.GetType().GetMethods().FirstOrDefault(delegate(MethodInfo item)
        {
            return item.Name == "DownloadUpdatesAsync" && item.GetParameters().Length == 3;
        });
        MethodInfo apply = manager.GetType().GetMethods().FirstOrDefault(delegate(MethodInfo item)
        {
            return item.Name == "ApplyUpdatesAndRestart" && item.GetParameters().Length == 2;
        });
        Assert(check != null, "Velopack CheckForUpdatesAsync signature is incompatible.");
        Assert(download != null && download.GetParameters()[0].ParameterType.FullName == "Velopack.UpdateInfo",
            "Velopack DownloadUpdatesAsync signature is incompatible.");
        Assert(apply != null && apply.GetParameters()[0].ParameterType.FullName == "Velopack.VelopackAsset",
            "Velopack ApplyUpdatesAndRestart signature is incompatible.");
    }

    private static ApplicationUpdate Get(GitHubUpdateService service)
    {
        return service.CheckForUpdateAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static ManualUpdateCheckResult GetManual(GitHubUpdateService service)
    {
        return service.CheckForUpdateManuallyAsync(CancellationToken.None).GetAwaiter().GetResult();
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

    private sealed class FakeClock : IUpdateClock
    {
        public DateTime UtcNowValue { get; set; }
        public DateTime UtcNow { get { return UtcNowValue; } }

        public FakeClock(DateTime utcNow)
        {
            UtcNowValue = utcNow;
        }
    }

    private sealed class MemoryStateStore : IUpdateCheckStateStore
    {
        public UpdateCheckState State { get; set; }
        public int SaveCount { get; private set; }

        public MemoryStateStore()
        {
            State = new UpdateCheckState();
        }

        public UpdateCheckState Load()
        {
            return State == null ? new UpdateCheckState() : State.Clone();
        }

        public void Save(UpdateCheckState state)
        {
            SaveCount++;
            State = state.Clone();
        }
    }

    private sealed class FakeEngine : IApplicationUpdateEngine
    {
        private readonly string _candidate;
        public bool Available { get; set; }
        public bool IsAvailable { get { return Available; } }
        public int CheckCount { get; private set; }
        public Exception CheckException { get; set; }
        public Task<ApplicationUpdate> PendingCheck { get; set; }
        public SecurityProtocolType SecurityProtocolAtLastCheck { get; private set; }

        public FakeEngine(string candidate)
        {
            _candidate = candidate;
            Available = true;
        }

        public Task<ApplicationUpdate> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            SecurityProtocolAtLastCheck = ServicePointManager.SecurityProtocol;
            cancellationToken.ThrowIfCancellationRequested();
            if (CheckException != null) throw CheckException;
            if (PendingCheck != null) return PendingCheck;
            return Task.FromResult(string.IsNullOrEmpty(_candidate)
                ? null
                : new ApplicationUpdate(_candidate, new object()));
        }

        public Task DownloadAndApplyAsync(
            ApplicationUpdate update,
            Action<int> progress,
            CancellationToken cancellationToken)
        {
            if (progress != null) progress(100);
            return Task.FromResult(0);
        }
    }
}
