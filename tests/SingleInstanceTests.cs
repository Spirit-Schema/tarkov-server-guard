using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    internal static class SingleInstanceTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 1)
            {
                if (args[0] == "compete")
                {
                    using (var gate = EventWaitHandle.OpenExisting(args[1] + ".Gate"))
                        if (!gate.WaitOne(5000)) return 22;
                    using (var candidate = new SingleInstanceGuard(args[1]))
                    {
                        if (!candidate.IsPrimary) return 21;
                        using (var finish = EventWaitHandle.OpenExisting(args[1] + ".Finish"))
                            return finish.WaitOne(10000) ? 20 : 23;
                    }
                }
                using (var guard = new SingleInstanceGuard(args[1]))
                {
                    if (args[0] == "reopen") return guard.IsPrimary ? 0 : 8;
                    if (args[0] == "hold")
                    {
                        if (!guard.IsPrimary) return 9;
                        using (var ready = EventWaitHandle.OpenExisting(args[1] + ".Ready")) ready.Set();
                        Thread.Sleep(30000);
                    }
                    else if (guard.IsPrimary) return 8;
                    else guard.RequestActivation();
                }
                return 0;
            }
            try
            {
                string scope = @"Local\TSG.Test." + Guid.NewGuid().ToString("N");
                using (var primary = new SingleInstanceGuard(scope))
                {
                    Assert(primary.IsPrimary, "first process owns the UI");
                    // Signal before attaching the form: startup races must retain activation.
                    using (Process probe = Child("probe", scope))
                        Assert(probe.WaitForExit(5000) && probe.ExitCode == 0, "second process exits without becoming primary");
                    using (var form = new Form())
                    {
                        form.ShowInTaskbar = false;
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new System.Drawing.Point(-2400, -1600);
                        form.Show();
                        form.WindowState = FormWindowState.Minimized;
                        primary.Attach(form);
                        PumpUntil(delegate { return form.WindowState != FormWindowState.Minimized; });
                        Assert(form.WindowState == FormWindowState.Normal, "queued second launch restores the minimized window");
                        form.Hide();
                        using (Process probe = Child("probe", scope))
                            Assert(probe.WaitForExit(5000) && probe.ExitCode == 0, "repeated launch is rejected");
                        PumpUntil(delegate { return form.Visible; });
                        Assert(form.Visible, "hidden existing window is shown");
                        form.Close();
                        using (Process restart = Child("reopen", scope))
                            Assert(restart.WaitForExit(5000) && restart.ExitCode == 0,
                                "window close releases ownership before outer process cleanup for restart");
                    }
                    using (var independent = new SingleInstanceGuard(scope + ".OtherUser"))
                        Assert(independent.IsPrimary, "independent scope is not blocked");
                }
                using (var next = new SingleInstanceGuard(scope))
                    Assert(next.IsPrimary, "normal exit releases the guard for restart/update");

                using (var gate = new EventWaitHandle(false, EventResetMode.ManualReset, scope + ".Gate"))
                using (var finish = new EventWaitHandle(false, EventResetMode.ManualReset, scope + ".Finish"))
                using (Process a = Child("compete", scope))
                using (Process b = Child("compete", scope))
                {
                    try
                    {
                        gate.Set();
                        PumpUntil(delegate { return a.HasExited || b.HasExited; });
                        Assert(a.HasExited != b.HasExited, "simultaneous launch admits exactly one process");
                        Process loser = a.HasExited ? a : b;
                        Process winner = a.HasExited ? b : a;
                        Assert(loser.ExitCode == 21, "simultaneous loser observes occupied guard");
                        finish.Set();
                        Assert(winner.WaitForExit(5000) && winner.ExitCode == 20, "simultaneous owner exits normally");
                    }
                    finally
                    {
                        finish.Set();
                        if (!a.HasExited) { a.Kill(); a.WaitForExit(5000); }
                        if (!b.HasExited) { b.Kill(); b.WaitForExit(5000); }
                    }
                }

                using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, scope + ".Ready"))
                using (Process crashed = Child("hold", scope))
                {
                    try
                    {
                        Assert(ready.WaitOne(5000), "crash fixture acquired guard");
                        // Keep the named mutex alive to exercise abandoned ownership.
                        using (var waiting = new SingleInstanceGuard(scope))
                        {
                            Assert(!waiting.IsPrimary, "live owner is exclusive");
                            crashed.Kill();
                            Assert(crashed.WaitForExit(5000), "isolated fixture terminated");
                            using (var recovered = new SingleInstanceGuard(scope))
                                Assert(recovered.IsPrimary, "abandoned mutex allows restart after crash");
                        }
                    }
                    finally { if (!crashed.HasExited) { crashed.Kill(); crashed.WaitForExit(5000); } }
                }
                string program = File.ReadAllText(Path.Combine(args[0], "src", "Program.cs"));
                Assert(program.IndexOf("TryRunVelopackStartupHooks", StringComparison.Ordinal)
                    < program.IndexOf("new SingleInstanceGuard", StringComparison.Ordinal)
                    && program.IndexOf("ExecuteHelperCommand", StringComparison.Ordinal)
                    < program.IndexOf("new SingleInstanceGuard", StringComparison.Ordinal),
                    "update hooks and elevated firewall helper run before the UI guard");
                Assert(program.Contains("demoMode ? null : new SingleInstanceGuard"), "demo/preview does not activate the installed app");
                Console.WriteLine("Single-instance tests passed.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        private static Process Child(string mode, string scope)
        {
            return Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, mode + " " + scope)
            { UseShellExecute = false, CreateNoWindow = true });
        }

        private static void PumpUntil(Func<bool> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!condition() && watch.ElapsedMilliseconds < 5000)
            { Application.DoEvents(); Thread.Sleep(10); }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Console.WriteLine("PASS: " + message);
        }
    }
}
