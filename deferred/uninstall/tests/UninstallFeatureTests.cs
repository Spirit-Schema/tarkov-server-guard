// Deferred regression fixture. This file is intentionally excluded from build.ps1.
// Restore the dependencies listed in ../README.md before compiling it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TarkovServerReporter.Tests
{
    internal static class UninstallFeatureTests
    {
        private static int _failed;

        [STAThread]
        private static int Main()
        {
            Run("strict installed/portable/developer layout detection", TestInstallKinds);
            Run("foreign and malformed sq.version manifests are rejected", TestManifestBoundary);
            Run("registered updater delegation is explicit", TestLaunchDelegation);
            Run("user-data deletion token and path boundary are narrow", TestUserDataBoundary);
            Run("owned Start Menu shortcut boundary is narrow", TestShortcutBoundary);
            Run("preview UI cannot start removal", TestPreviewUi);
            Run("firewall cleanup is verified before delegation", TestFirewallCleanup);
            Run("remaining firewall rules fail closed", TestFirewallFinalAbsence);
            Console.WriteLine(_failed == 0
                ? "Deferred uninstall tests passed."
                : _failed + " deferred uninstall test(s) failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static void TestInstallKinds()
        {
            WithLayout(delegate(string root, string current, string executable)
            {
                string updater = Path.Combine(root, "Update.exe");
                File.WriteAllText(updater, string.Empty);
                WriteManifest(Path.Combine(current, "sq.version"),
                    UninstallSupport.ExpectedPackageId,
                    "0.7.5",
                    "TarkovServerGuard.exe");
                string resolved;
                Assert(UninstallSupport.DetectInstallKind(executable, out resolved)
                    == ApplicationInstallKind.Installed && resolved == updater,
                    "A valid Velopack installed layout was not recognized.");

                File.WriteAllText(Path.Combine(root, ".portable"), string.Empty);
                Assert(UninstallSupport.DetectInstallKind(executable, out resolved)
                    == ApplicationInstallKind.Portable && resolved == null,
                    "Portable layout exposed registered uninstall delegation.");
            });

            string developer = Path.Combine(Path.GetTempPath(), "TarkovServerGuard.exe");
            string ignored;
            Assert(UninstallSupport.DetectInstallKind(developer, out ignored)
                == ApplicationInstallKind.Developer,
                "A loose developer executable was treated as installed.");
        }

        private static void TestManifestBoundary()
        {
            WithTemporaryRoot(delegate(string root)
            {
                string manifest = Path.Combine(root, "sq.version");
                WriteManifest(manifest, "Foreign.Package", "0.7.5", "TarkovServerGuard.exe");
                Assert(!UninstallSupport.IsExpectedVelopackManifest(
                    manifest, "TarkovServerGuard.exe"),
                    "A foreign package manifest was accepted.");
                File.WriteAllText(manifest,
                    "<package><id>" + UninstallSupport.ExpectedPackageId
                    + "</id><id>" + UninstallSupport.ExpectedPackageId
                    + "</id><version>0.7.5</version><mainExe>TarkovServerGuard.exe</mainExe></package>");
                Assert(!UninstallSupport.IsExpectedVelopackManifest(
                    manifest, "TarkovServerGuard.exe"),
                    "Duplicate identity elements were accepted.");
                File.WriteAllText(manifest, "<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///c:/x'>]><x>&y;</x>");
                Assert(!UninstallSupport.IsExpectedVelopackManifest(
                    manifest, "TarkovServerGuard.exe"),
                    "A DTD-bearing manifest was accepted.");
            });
        }

        private static void TestLaunchDelegation()
        {
            string updater = Path.GetTempFileName();
            try
            {
                var plan = new UninstallLaunchPlan
                {
                    InstallKind = ApplicationInstallKind.Installed,
                    UpdaterPath = updater,
                    Arguments = UninstallSupport.UninstallArguments,
                    DeleteUserData = true
                };
                var launcher = new FakeLauncher(true);
                string error;
                Assert(UninstallSupport.TryStart(plan, launcher, out error),
                    "A valid immutable plan was not delegated.");
                Assert(launcher.Calls == 1
                    && launcher.LastPlan.Arguments == "uninstall --silent"
                    && launcher.LastPlan.DeleteUserData,
                    "The registered Velopack delegation changed arguments or options.");
                Assert(!UninstallSupport.TryStart(plan, new FakeLauncher(false), out error),
                    "A failed updater start was reported as successful.");
            }
            finally
            {
                File.Delete(updater);
            }
        }

        private static void TestUserDataBoundary()
        {
            WithTemporaryRoot(delegate(string localRoot)
            {
                IList<string> owned = UninstallSupport.GetOwnedUserDataDirectories(localRoot);
                Assert(owned.Count == 2
                    && owned.All(path => UninstallSupport.IsSafeOwnedUserDataDirectory(
                        localRoot, path)),
                    "The exact legacy/current app data roots were not recognized.");
                Assert(!UninstallSupport.IsSafeOwnedUserDataDirectory(localRoot, localRoot),
                    "The LocalAppData root itself became deletable.");
                Assert(!UninstallSupport.IsSafeOwnedUserDataDirectory(
                    localRoot, Path.Combine(localRoot, "OtherApp")),
                    "An adjacent app directory became deletable.");
                Assert(UninstallSupport.ShouldDeleteUserDataFromEnvironment(
                    UninstallSupport.DeleteDataEnvironmentValue),
                    "The explicit deletion token was rejected.");
                Assert(!UninstallSupport.ShouldDeleteUserDataFromEnvironment("1"),
                    "A broad truthy value enabled user-data deletion.");
            });
        }

        private static void TestShortcutBoundary()
        {
            WithTemporaryRoot(delegate(string programs)
            {
                string owned = UninstallSupport.GetOwnedStartMenuShortcutPath(programs);
                Assert(UninstallSupport.IsSafeOwnedStartMenuShortcut(programs, owned),
                    "The exact app-owned shortcut was not recognized.");
                Assert(!UninstallSupport.IsSafeOwnedStartMenuShortcut(
                    programs, Path.Combine(programs, "Tarkov Server Guard.lnk")),
                    "The main app shortcut was included in uninstall cleanup.");
                Assert(!UninstallSupport.IsSafeOwnedStartMenuShortcut(
                    programs, Path.Combine(programs, "..", UninstallSupport.StartMenuShortcutName)),
                    "A shortcut path outside the exact Programs directory was accepted.");
            });
        }

        private static void TestPreviewUi()
        {
            using (var form = new UninstallOptionsForm(
                deleteData => new UninstallLaunchPlan
                {
                    InstallKind = ApplicationInstallKind.Developer,
                    Arguments = UninstallSupport.UninstallArguments,
                    DeleteUserData = deleteData,
                    UnavailableReason = "preview"
                },
                new FakeLauncher(true),
                new FakeFirewallService(new UninstallFirewallCleanupResult { Success = true })))
            {
                Assert(!form.IsStartEnabledForTest,
                    "A developer/preview UI exposed the real uninstall action.");
            }
        }

        private static void TestFirewallCleanup()
        {
            var gateway = new FakeGateway(
                Query("51.1.1.1", "52.2.2.2"),
                new FirewallBatchChangeResult
                {
                    Success = true,
                    Items = new List<FirewallBatchItemResult>
                    {
                        new FirewallBatchItemResult { IpAddress = "51.1.1.1", Success = true },
                        new FirewallBatchItemResult { IpAddress = "52.2.2.2", Success = true }
                    }
                },
                Query());
            UninstallFirewallCleanupResult result = new SystemUninstallFirewallService(gateway)
                .RemoveAllManagedRulesAsync().GetAwaiter().GetResult();
            Assert(result.Success && result.RemovedCount == 2,
                "Verified firewall cleanup did not complete.");
            Assert(gateway.Events.SequenceEqual(new[] { "query", "remove", "query" }),
                "Cleanup did not query, remove, and re-query in order.");
        }

        private static void TestFirewallFinalAbsence()
        {
            var gateway = new FakeGateway(
                Query("51.1.1.1"),
                new FirewallBatchChangeResult
                {
                    Success = true,
                    Items = new List<FirewallBatchItemResult>
                    {
                        new FirewallBatchItemResult { IpAddress = "51.1.1.1", Success = true }
                    }
                },
                Query("51.1.1.1"));
            UninstallFirewallCleanupResult result = new SystemUninstallFirewallService(gateway)
                .RemoveAllManagedRulesAsync().GetAwaiter().GetResult();
            Assert(!result.Success && result.FailedCount > 0,
                "Cleanup reported success while an owned rule remained.");
        }

        private static ManagedBlockedServerQueryResult Query(params string[] addresses)
        {
            var result = new ManagedBlockedServerQueryResult { Success = true };
            foreach (string address in addresses)
                result.Servers.Add(new ManagedBlockedServer { IpAddress = address });
            return result;
        }

        private static void WriteManifest(
            string path,
            string id,
            string version,
            string mainExe)
        {
            File.WriteAllText(path,
                "<package><id>" + id + "</id><version>" + version
                + "</version><mainExe>" + mainExe + "</mainExe></package>");
        }

        private static void WithLayout(Action<string, string, string> action)
        {
            WithTemporaryRoot(delegate(string root)
            {
                string current = Path.Combine(root, "current");
                Directory.CreateDirectory(current);
                string executable = Path.Combine(current, "TarkovServerGuard.exe");
                File.WriteAllText(executable, string.Empty);
                action(root, current, executable);
            });
        }

        private static void WithTemporaryRoot(Action<string> action)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "tsg-deferred-uninstall-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { action(root); }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void Run(string name, Action test)
        {
            try { test(); Console.WriteLine("PASS: " + name); }
            catch (Exception exception)
            {
                _failed++;
                Console.Error.WriteLine("FAIL: " + name + " - " + exception.Message);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private sealed class FakeLauncher : IUninstallProcessLauncher
        {
            private readonly bool _result;
            internal int Calls;
            internal UninstallLaunchPlan LastPlan;
            internal FakeLauncher(bool result) { _result = result; }
            public bool Start(UninstallLaunchPlan plan)
            {
                Calls++;
                LastPlan = plan;
                return _result;
            }
        }

        private sealed class FakeFirewallService : IUninstallFirewallService
        {
            private readonly UninstallFirewallCleanupResult _result;
            internal FakeFirewallService(UninstallFirewallCleanupResult result) { _result = result; }
            public Task<UninstallFirewallCleanupResult> RemoveAllManagedRulesAsync()
            {
                return Task.FromResult(_result);
            }
        }

        private sealed class FakeGateway : IUninstallFirewallRuleGateway
        {
            private readonly Queue<ManagedBlockedServerQueryResult> _queries;
            private readonly FirewallBatchChangeResult _batch;
            internal readonly List<string> Events = new List<string>();

            internal FakeGateway(
                ManagedBlockedServerQueryResult first,
                FirewallBatchChangeResult batch,
                ManagedBlockedServerQueryResult final)
            {
                _queries = new Queue<ManagedBlockedServerQueryResult>(new[] { first, final });
                _batch = batch;
            }

            public ManagedBlockedServerQueryResult QueryAllOwnedManagedRules()
            {
                Events.Add("query");
                return _queries.Dequeue();
            }

            public Task<FirewallBatchChangeResult> RemoveManyWithElevationAsync(
                IEnumerable<string> ipAddresses)
            {
                Events.Add("remove");
                return Task.FromResult(_batch);
            }
        }
    }
}
