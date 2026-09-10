using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class UpdateLifecycleTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        private static int Main()
        {
            try
            {
                StaUiTestHarness.Run(delegate
                {
                    AppText.SetLanguage(AppText.KoreanLanguage);
                    TestPrompt("later");
                    TestPrompt("dismiss");
                    TestPrompt("download-failed");
                    TestPrompt("apply-failed");
                    TestPrompt("cancelled");
                    TestMainLifecycle();
                });
                Console.WriteLine("Update lifecycle tests passed.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        private static void TestPrompt(string action)
        {
            var store = new Store();
            var engine = new Engine { DownloadOutcome = action };
            var service = new GitHubUpdateService("0.8.7", engine, store, new SystemUpdateClock());
            var update = new ApplicationUpdate("0.8.8", new object());
            Exception failure = null;
            bool acted = false;
            using (var driver = new System.Windows.Forms.Timer { Interval = 10 })
            {
                driver.Tick += delegate
                {
                    UpdatePromptForm prompt = Application.OpenForms.OfType<UpdatePromptForm>().FirstOrDefault();
                    if (prompt == null) return;
                    driver.Stop();
                    try
                    {
                        Assert(service.ShowUpdatePromptAsync(null, update, CancellationToken.None).IsCompleted
                            && Application.OpenForms.OfType<UpdatePromptForm>().Count() == 1,
                            "overlapping prompt request is ignored");
                        if (action == "later") Button(prompt, "_laterButton").PerformClick();
                        else if (action == "dismiss") prompt.Close();
                        else
                        {
                            Button(prompt, "_updateButton").PerformClick();
                            if (action == "cancelled")
                                Assert(prompt.DialogResult == DialogResult.Cancel && !prompt.DeferRequested,
                                    "cancellation closes without a user deferral");
                            else if (!prompt.IsDisposed && prompt.Visible)
                            {
                                Assert(Button(prompt, "_laterButton").Text == AppText.Get("Common.Button.Close"),
                                    "existing error-close label is unchanged");
                                Assert(Button(prompt, "_updateButton").Text == AppText.Get("UpdateDialog.Retry"),
                                    "existing retry label is unchanged");
                                Button(prompt, "_laterButton").PerformClick();
                            }
                        }
                        acted = true;
                    }
                    catch (Exception ex) { failure = ex; prompt.Dispose(); }
                };
                driver.Start();
                service.ShowUpdatePromptAsync(null, update, CancellationToken.None).GetAwaiter().GetResult();
            }
            if (failure != null) throw failure;
            Assert(acted, "prompt action completed: " + action);
            Assert(service.IsVersionDeferred("0.8.8") == (action == "later"),
                "only explicit Later persists a deferral: " + action);
            Assert(store.Saves == (action == "later" ? 1 : 0),
                "closing, cancellation and failures do not save deferral state");
        }

        private static void TestMainLifecycle()
        {
            var store = new Store();
            var engine = new Engine();
            var service = new GitHubUpdateService("0.8.7", engine, store, new SystemUpdateClock());
            using (var main = new MainForm(true))
            {
                main.StartPosition = FormStartPosition.Manual;
                main.Location = new System.Drawing.Point(-2400, -1600);
                main.ShowInTaskbar = false;
                main.Show();
                Application.DoEvents();
                Assert(Field(main, "_automaticUpdateTimer") == null, "demo never starts automatic network monitoring");
                // Main's Shown path has already loaded isolated demo data. Switch only
                // the monitoring gate and inject a fake service; no production I/O runs.
                typeof(MainForm).GetField("_demoMode", Instance).SetValue(main, false);
                typeof(MainForm).GetField("_updateService", Instance).SetValue(main, service);
                Invoke(main, "StartAutomaticUpdateMonitoring");
                System.Windows.Forms.Timer timer = (System.Windows.Forms.Timer)Field(main, "_automaticUpdateTimer");
                Assert(timer != null && timer.Enabled, "running application starts periodic monitoring");
                PumpUntil(delegate { return engine.Checks == 1 && Field(main, "_updateCheckCancellation") == null; });
                Tick(timer);
                Application.DoEvents();
                Assert(engine.Checks == 1, "minute timer does not issue another network request before six hours");

                // A background/minimized app queues an available update without a modal popup.
                main.WindowState = FormWindowState.Minimized;
                engine.Candidate = "0.8.8";
                SetDue(main);
                Tick(timer);
                PumpUntil(delegate { return engine.Checks == 2 && Field(main, "_updateCheckCancellation") == null; });
                Assert(Field(main, "_pendingAutomaticUpdate") != null
                    && !Application.OpenForms.OfType<UpdatePromptForm>().Any(),
                    "minimized app queues update without interrupting another application");
                service.Defer("0.8.8");
                main.WindowState = FormWindowState.Normal;
                main.Activate();
                Application.DoEvents();
                Invoke(main, "TryShowPendingAutomaticUpdate");
                Assert(!Application.OpenForms.OfType<UpdatePromptForm>().Any(),
                    "pending update rechecks a newer deferral before showing");

                engine.Candidate = null;
                engine.ThrowCheck = true;
                SetDue(main);
                Tick(timer);
                PumpUntil(delegate { return engine.Checks == 3 && Field(main, "_updateCheckCancellation") == null; });
                DateTime deadline = store.State.DeferredUntilUtc;
                Tick(timer);
                Application.DoEvents();
                Assert(engine.Checks == 3 && store.State.DeferredUntilUtc == deadline,
                    "failed automatic check neither floods retries nor changes user deferral");
                engine.ThrowCheck = false;
                Invoke(main, "CheckForUpdatesManuallyAsync");
                PumpUntil(delegate { return engine.Checks == 4 && Field(main, "_updateCheckCancellation") == null; });
                Assert(store.State.DeferredUntilUtc == deadline, "manual retry remains possible without changing deferral");
                main.Dispose();
                Assert(!timer.Enabled && Field(main, "_automaticUpdateTimer") == null,
                    "closing disposes monitoring timer");
            }
        }

        private static void SetDue(MainForm main)
        { typeof(MainForm).GetField("_nextAutomaticUpdateCheckUtc", Instance).SetValue(main, DateTime.UtcNow.AddSeconds(-1)); }
        private static object Field(object instance, string name)
        { return instance.GetType().GetField(name, Instance).GetValue(instance); }
        private static Button Button(object instance, string name) { return (Button)Field(instance, name); }
        private static void Invoke(object instance, string name)
        { instance.GetType().GetMethod(name, Instance).Invoke(instance, null); }
        private static void Tick(System.Windows.Forms.Timer timer)
        { typeof(System.Windows.Forms.Timer).GetMethod("OnTick", Instance).Invoke(timer, new object[] { EventArgs.Empty }); }
        private static void PumpUntil(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
            Assert(condition(), "asynchronous monitoring step completes without blocking the message pump");
        }
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); Console.WriteLine("PASS: " + message); }

        private sealed class Store : IUpdateCheckStateStore
        {
            internal UpdateCheckState State = new UpdateCheckState();
            internal int Saves;
            public UpdateCheckState Load() { return State.Clone(); }
            public void Save(UpdateCheckState state) { State = state.Clone(); Saves++; }
        }

        private sealed class Engine : IApplicationUpdateEngine
        {
            internal string Candidate;
            internal string DownloadOutcome;
            internal bool ThrowCheck;
            internal int Checks;
            public bool IsAvailable { get { return true; } }
            public Task<ApplicationUpdate> CheckForUpdateAsync(CancellationToken token)
            {
                Interlocked.Increment(ref Checks);
                token.ThrowIfCancellationRequested();
                if (ThrowCheck) throw new IOException("simulated offline");
                return Task.FromResult(Candidate == null ? null : new ApplicationUpdate(Candidate, new object()));
            }
            public Task DownloadAndApplyAsync(ApplicationUpdate update, Action<int> progress, CancellationToken token)
            {
                if (DownloadOutcome == "download-failed") throw new IOException("simulated download failure");
                if (DownloadOutcome == "cancelled") throw new OperationCanceledException();
                return Task.FromResult(0);
            }
        }
    }
}
