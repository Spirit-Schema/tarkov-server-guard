// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Tarkov Server Guard")]
[assembly: AssemblyDescription("Escape from Tarkov server history, diagnostics, notes, and selective firewall guard")]
[assembly: AssemblyCompany("Spirit-Schema")]
[assembly: AssemblyProduct("Tarkov Server Guard")]
[assembly: AssemblyCopyright("Copyright © 2026 Spirit-Schema. All rights reserved.")]
[assembly: AssemblyVersion("0.8.0.0")]
[assembly: AssemblyFileVersion("0.8.0.0")]

namespace TarkovServerReporter
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Velopack may handle install/update hooks and exit from this call.
            // In an unpackaged developer build the adapter is a safe no-op.
            GitHubUpdateService.TryRunVelopackStartupHooks();

            bool shouldBlock;
            string firewallIpAddress;
            if (FirewallRuleManager.TryParseHelperCommand(args, out shouldBlock, out firewallIpAddress))
            {
                Environment.ExitCode = FirewallRuleManager.ExecuteHelperCommand(shouldBlock, firewallIpAddress);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string previewPath = GetArgumentValue(args, "--preview");
            bool demoMode = HasArgument(args, "--demo") || !string.IsNullOrWhiteSpace(previewPath);
            var form = new MainForm(demoMode);
            form.Shown += delegate
            {
                if (demoMode || !UpdateCompletionNotice.HasPendingNotice())
                    return;
                form.BeginInvoke(new Action(delegate
                {
                    ReleaseNotesEntry entry;
                    if (UpdateCompletionNotice.TryClaimCurrentCompletedUpdate(
                        demoMode,
                        GitHubUpdateService.IsInstalledApplication(),
                        out entry))
                        PatchNotesForm.ShowCompleted(form, entry);
                }));
            };

            if (!string.IsNullOrWhiteSpace(previewPath))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-2400, -1600);
                form.ShowInTaskbar = false;
                form.Shown += delegate
                {
                    form.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            Application.DoEvents();
                            form.SavePreview(Path.GetFullPath(previewPath));
                        }
                        catch
                        {
                            Environment.ExitCode = 1;
                        }
                        finally
                        {
                            form.Close();
                        }
                    }));
                };
            }

            try
            {
                Application.Run(form);
            }
            finally
            {
                NetworkServices.DisposeGeoService();
            }
        }

        private static bool HasArgument(string[] args, string expected)
        {
            if (args == null) return false;
            foreach (string argument in args)
            {
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string GetArgumentValue(string[] args, string expected)
        {
            if (args == null) return null;
            for (int index = 0; index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], expected, StringComparison.OrdinalIgnoreCase))
                    return args[index + 1];
            }
            return null;
        }
    }
}
