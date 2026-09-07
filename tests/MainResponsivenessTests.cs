// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class MainResponsivenessTests
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static Assembly _app;
        private static int _assertions;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length != 1 || !File.Exists(args[0]))
                    throw new ArgumentException("Usage: MainResponsivenessTests <product.exe>");
                _app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
                TypeOf("AppText").GetMethod("SetLanguage").Invoke(null, new object[] { "en" });
                StaUiTestHarness.Run(delegate
                {
                    TestBlockCompletesBeforeEvidence();
                    TestNewerStatusWins();
                    TestUnblockWinsOverOldEvidence();
                    TestRejectedNativeChange(false);
                    TestRejectedNativeChange(true);
                    TestEvidenceFailureDoesNotUndoBlock();
                    TestLauncherRefreshOrdering();
                });
                Console.WriteLine("MainResponsivenessTests: PASS (" + _assertions + " assertions)");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("MainResponsivenessTests: FAIL");
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void TestLauncherRefreshOrdering()
        {
            using (var form = (Form)Activator.CreateInstance(TypeOf("MainForm"), new object[] { true }))
            {
                DateTime earlier = new DateTime(2026, 9, 6, 17, 1, 30);
                object korea = Activator.CreateInstance(TypeOf("LauncherSelectionInfo"));
                Set(korea, "EftSelection", "Korea"); Set(korea, "EftUpdatedAt", earlier);
                object china = Activator.CreateInstance(TypeOf("LauncherSelectionInfo"));
                Set(china, "EftSelection", "China"); Set(china, "EftUpdatedAt", earlier.AddMinutes(1));
                var label = (Label)Field(form, "_launcherSelectionLabel");
                foreach (object selection in new[] { china, korea })
                {
                    object source = CreateSource("LauncherSelectionInfo");
                    Task refresh = (Task)Invoke(form, "RefreshLauncherSelectionFromAsync", Get(source, "Task"));
                    Complete(source, selection); Wait(refresh, "launcher selection refresh");
                }
                Assert(label.Text.Contains("Korea") && !label.Text.Contains("China"),
                    "Failed Apply rollback restores the previous selection despite its older timestamp.");
                object older = CreateSource("LauncherSelectionInfo"), newer = CreateSource("LauncherSelectionInfo");
                Task oldRead = (Task)Invoke(form, "RefreshLauncherSelectionFromAsync", Get(older, "Task"));
                Task newRead = (Task)Invoke(form, "RefreshLauncherSelectionFromAsync", Get(newer, "Task"));
                Complete(newer, china); Wait(newRead, "newer launcher read");
                Complete(older, korea); Wait(oldRead, "late older launcher read");
                Assert(label.Text.Contains("China") && !label.Text.Contains("Korea"),
                    "A late older scan cannot overwrite a newer scan.");
                object empty = CreateSource("LauncherSelectionInfo");
                Task emptyRead = (Task)Invoke(form, "RefreshLauncherSelectionFromAsync", Get(empty, "Task"));
                Complete(empty, Activator.CreateInstance(TypeOf("LauncherSelectionInfo")));
                Wait(emptyRead, "temporarily missing launcher log");
                Assert(label.Text.Contains("China"), "Transient missing logs retain the last parsed selection.");
                object rejected = Activator.CreateInstance(TypeOf("LauncherSelectionInfo"));
                Set(rejected, "EftSelectionInvalidated", true);
                object rejectedSource = CreateSource("LauncherSelectionInfo");
                Task rejectedRead = (Task)Invoke(form, "RefreshLauncherSelectionFromAsync", Get(rejectedSource, "Task"));
                Complete(rejectedSource, rejected); Wait(rejectedRead, "rejected first Apply");
                Assert(!label.Text.Contains("China") && label.Text.Contains("Check Launcher"),
                    "A rejected first Apply clears its tentative display even without a prior valid selection.");
            }
        }

        private static void TestBlockCompletesBeforeEvidence()
        {
            using (var fixture = new Fixture())
            {
                Task action = fixture.StartChange(true);
                Assert(!action.IsCompleted && fixture.Busy && fixture.AddressBusy,
                    "The pending native block keeps its busy state until the firewall result arrives.");
                Assert(fixture.NativeCalls == 1 && fixture.EvidenceCalls == 0 && fixture.Saved == 0,
                    "Neither metadata nor evidence runs before the native block succeeds.");
                Task duplicate = fixture.StartChange(true);
                Wait(duplicate, "ignored duplicate action");
                Assert(fixture.NativeCalls == 1, "A second action cannot start while the native block is pending.");

                fixture.CompleteNative(true, true, false);
                Wait(action, "block action completion independent of optional evidence");
                Assert(!fixture.Busy && !fixture.AddressBusy,
                    "The core action releases global and per-address busy state without waiting for evidence.");
                Task evidence = fixture.PendingEvidence;
                Assert(fixture.EvidenceCalls == 1 && evidence != null && !evidence.IsCompleted,
                    "Evidence continues separately after the core action has completed.");
                Assert(fixture.Saved == 1 && fixture.Removed == 0 && fixture.IsBlocked,
                    "The successful block updates state and saves metadata exactly once.");
                string immediate = fixture.Status;
                Assert(immediate.Contains("Blocked server " + Fixture.Ip),
                    "Block success is visible while optional evidence is still pending.");
                fixture.CompleteEvidence(100, 4, 2);
                Wait(evidence, "evidence continuation");
                Assert(fixture.Status.Contains("latest 100 raids") && fixture.Status.Contains("used in 4")
                    && fixture.Status.Contains("2 showed") && fixture.Status.Contains("Blocked server " + Fixture.Ip),
                    "Current evidence adds its observed counts to the matching block success status.");
                Assert(!fixture.Busy && fixture.Saved == 1,
                    "Evidence completion does not re-enter busy state or resave metadata.");
            }
        }

        private static void TestNewerStatusWins()
        {
            using (var fixture = new Fixture())
            {
                Task action = fixture.StartChange(true);
                fixture.CompleteNative(true, true, false);
                Wait(action, "block before a newer status");
                Task evidence = fixture.PendingEvidence;
                long before = fixture.StatusRevision;
                fixture.SetStatus("Independent newer operation completed.");
                Assert(fixture.StatusRevision > before,
                    "A newer user-visible status advances the status revision.");
                fixture.CompleteEvidence(90, 3, 1);
                Wait(evidence, "stale evidence following newer status");
                Assert(fixture.Status == "Independent newer operation completed.",
                    "An older evidence continuation never overwrites a newer operation status.");
                Assert(fixture.IsBlocked, "Ignoring stale evidence does not change the confirmed firewall state.");
            }
        }

        private static void TestUnblockWinsOverOldEvidence()
        {
            using (var fixture = new Fixture())
            {
                Task block = fixture.StartChange(true);
                fixture.CompleteNative(true, true, false);
                Wait(block, "block before unblock");
                Task evidence = fixture.PendingEvidence;
                fixture.NewNativeRequest();
                Task unblock = fixture.StartChange(false);
                Assert(fixture.NativeCalls == 2 && fixture.Busy,
                    "The user can unblock while the previous block's evidence is still pending.");
                fixture.CompleteNative(true, false, false);
                Wait(unblock, "unblock action");
                string unblockStatus = fixture.Status;
                Assert(!fixture.IsBlocked && fixture.Removed == 1 && fixture.Saved == 1
                    && fixture.EvidenceCalls == 1 && unblockStatus.Contains("Unblocked server " + Fixture.Ip),
                    "Unblock updates state and metadata without starting block evidence again.");
                fixture.CompleteEvidence(100, 5, 5);
                Wait(evidence, "old block evidence after unblock");
                Assert(fixture.Status == unblockStatus && !fixture.IsBlocked && !fixture.Busy && !fixture.AddressBusy,
                    "Old block evidence cannot replace the later unblock outcome or reblock the address.");
            }
        }

        private static void TestRejectedNativeChange(bool cancelled)
        {
            using (var fixture = new Fixture())
            {
                Task action = fixture.StartChange(true);
                fixture.CompleteNative(false, false, cancelled);
                Wait(action, cancelled ? "cancelled native block" : "failed native block");
                Assert(!fixture.Busy && !fixture.AddressBusy && !fixture.IsBlocked,
                    "A failed or cancelled native block releases busy state without inventing a blocked state.");
                Assert(fixture.Saved == 0 && fixture.Removed == 0 && fixture.EvidenceCalls == 0,
                    "Failed or cancelled native operations do not save metadata or request evidence.");
                Assert(fixture.Queries == (cancelled ? 0 : 1),
                    "Only a non-cancelled native failure uses the injected read-only state recheck.");
            }
        }

        private static void TestEvidenceFailureDoesNotUndoBlock()
        {
            using (var fixture = new Fixture())
            {
                Task action = fixture.StartChange(true);
                fixture.CompleteNative(true, true, false);
                Wait(action, "successful native block before evidence failure");
                Task evidence = fixture.PendingEvidence;
                string blockedStatus = fixture.Status;
                fixture.FailEvidence();
                Wait(evidence, "handled optional evidence failure");
                Assert(action.Status == TaskStatus.RanToCompletion && fixture.IsBlocked
                    && fixture.Saved == 1 && fixture.Removed == 0,
                    "Failure in optional evidence never reverses or fails the successful block.");
                Assert(fixture.Status == blockedStatus && !fixture.Busy && !fixture.AddressBusy,
                    "Evidence failure preserves the immediate success message and responsive controls.");
            }
        }

        private sealed class Fixture : IDisposable
        {
            internal const string Ip = "8.8.8.8";
            private readonly Form _form;
            private readonly IDictionary _states;
            private object _nativeSource;
            private readonly object _evidenceSource;
            internal int NativeCalls;
            internal int EvidenceCalls;
            internal int Queries;
            internal int Saved;
            internal int Removed;

            internal Fixture()
            {
                _form = (Form)Activator.CreateInstance(TypeOf("MainForm"), new object[] { true });
                _nativeSource = CreateSource("FirewallChangeResult");
                _evidenceSource = CreateSource("RaidQualityEvidenceSummary");
                // Every external effect used by the action is replaced before
                // showing the preview or invoking an action. Nothing reaches
                // the system firewall, log paths, or the user's metadata store.
                Install("_changeFirewallState", "ChangeNative");
                Install("_queryFirewallState", "QueryState");
                Install("_saveBlockedMetadata", "SaveMetadata");
                Install("_removeBlockedMetadata", "RemoveMetadata");
                Install("_loadRaidQualityEvidence", "LoadEvidence");
                _form.StartPosition = FormStartPosition.Manual;
                _form.Location = new Point(-25000, -25000);
                _form.ShowInTaskbar = false;
                _form.Show();
                Application.DoEvents();
                _states = (IDictionary)Field(_form, "_firewallStates");
                _states.Clear();
                _states.Add(Ip, State(false));
            }

            internal bool Busy { get { return (bool)Field(_form, "_isFirewallChanging"); } }
            internal bool AddressBusy
            {
                get
                {
                    object set = Field(_form, "_firewallBusyIpAddresses");
                    return (bool)set.GetType().GetMethod("Contains").Invoke(set, new object[] { Ip });
                }
            }
            internal bool IsBlocked { get { return (bool)Get(_states[Ip], "IsBlocked"); } }
            internal string Status { get { return ((Label)Field(_form, "_statusLabel")).Text; } }
            internal long StatusRevision { get { return (long)Field(_form, "_statusRevision"); } }
            internal Task PendingEvidence { get { return (Task)Field(_form, "_pendingQualityEvidence"); } }

            internal Task StartChange(bool block)
            {
                return (Task)Invoke(_form, "ChangeFirewallStateAsync", Ip, block);
            }
            internal void NewNativeRequest() { _nativeSource = CreateSource("FirewallChangeResult"); }
            internal void CompleteNative(bool success, bool blocked, bool cancelled)
            {
                object result = Activator.CreateInstance(TypeOf("FirewallChangeResult"));
                Set(result, "Success", success);
                Set(result, "IsBlocked", blocked);
                Set(result, "Cancelled", cancelled);
                Set(result, "ErrorMessage", success ? null : "Synthetic native failure");
                Complete(_nativeSource, result);
            }
            internal void CompleteEvidence(int window, int matching, int problems)
            {
                object summary = Activator.CreateInstance(TypeOf("RaidQualityEvidenceSummary"), Instance,
                    null, new object[] { window, matching, problems }, null);
                Complete(_evidenceSource, summary);
            }
            internal void FailEvidence()
            {
                _evidenceSource.GetType().GetMethod("SetException", new[] { typeof(Exception) })
                    .Invoke(_evidenceSource, new object[] { new IOException("Synthetic optional log read failure") });
            }
            internal void SetStatus(string message) { Invoke(_form, "SetStatus", message, Color.CornflowerBlue); }

            public object ChangeNative(string ip, bool block)
            {
                Assert(ip == Ip, "Native change receives only the synthetic fixture address.");
                NativeCalls++;
                return Get(_nativeSource, "Task");
            }
            public object QueryState(string ip)
            {
                Assert(ip == Ip, "State recheck receives only the synthetic fixture address.");
                Queries++;
                return State(false);
            }
            public void SaveMetadata(string ip)
            {
                Assert(ip == Ip, "Metadata save receives only the synthetic fixture address.");
                Saved++;
            }
            public void RemoveMetadata(string ip)
            {
                Assert(ip == Ip, "Metadata removal receives only the synthetic fixture address.");
                Removed++;
            }
            public object LoadEvidence(string ip)
            {
                Assert(ip == Ip, "Evidence receives only the synthetic fixture address.");
                EvidenceCalls++;
                return Get(_evidenceSource, "Task");
            }
            private void Install(string fieldName, string methodName)
            {
                FieldInfo field = _form.GetType().GetField(fieldName, Instance);
                Assert(field != null, "An injectable effect boundary exists: " + fieldName);
                MethodInfo target = GetType().GetMethod(methodName);
                MethodInfo signature = field.FieldType.GetMethod("Invoke");
                ParameterExpression[] parameters = signature.GetParameters()
                    .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name)).ToArray();
                Expression call = Expression.Call(Expression.Constant(this), target, parameters);
                if (signature.ReturnType != typeof(void)) call = Expression.Convert(call, signature.ReturnType);
                field.SetValue(_form, Expression.Lambda(field.FieldType, call, parameters).Compile());
            }
            public void Dispose() { _form.Dispose(); }
        }

        private static object State(bool blocked)
        {
            object state = Activator.CreateInstance(TypeOf("FirewallQueryResult"));
            Set(state, "Success", true);
            Set(state, "IsBlocked", blocked);
            return state;
        }
        private static Type TypeOf(string name) { return _app.GetType("TarkovServerReporter." + name, true); }
        private static object CreateSource(string resultType)
        {
            return Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(TypeOf(resultType)));
        }
        private static void Complete(object source, object result)
        {
            source.GetType().GetMethod("SetResult").Invoke(source, new[] { result });
        }
        private static object Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, Instance);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, name);
            return field.GetValue(target);
        }
        private static object Get(object target, string name) { return target.GetType().GetProperty(name).GetValue(target, null); }
        private static void Set(object target, string name, object value) { target.GetType().GetProperty(name).SetValue(target, value, null); }
        private static object Invoke(object target, string name, params object[] args)
        {
            return target.GetType().GetMethod(name, Instance).Invoke(target, args);
        }
        private static void Wait(Task task, string operation)
        {
            Assert(task != null, "A task was provided for " + operation + ".");
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            Assert(task.IsCompleted, "Timed out waiting for " + operation + ".");
            task.GetAwaiter().GetResult();
            Application.DoEvents();
        }
        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
