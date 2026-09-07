// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace TarkovServerReporter.Tests
{
    // UI suites must run on an STA thread inside a real Application.Run pump.
    // Posting teardown explicitly avoids relying on a second Idle event, which
    // need not fire when Windows has no more messages to dispatch.
    internal static class StaUiTestHarness
    {
        public static void Run(Action tests)
        {
            if (tests == null) throw new ArgumentNullException("tests");
            if (System.Threading.Thread.CurrentThread.GetApartmentState()
                != System.Threading.ApartmentState.STA)
                throw new InvalidOperationException("UI tests require an STA entry point.");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

            Exception failure = null;
            bool testsRan = false;
            bool teardownRan = false;
            using (var dispatcher = new Control())
            using (var context = new ApplicationContext())
            {
                IntPtr handle = dispatcher.Handle;
                dispatcher.BeginInvoke(new Action(delegate
                {
                    testsRan = true;
                    try { tests(); }
                    catch (Exception exception) { failure = exception; }
                    finally
                    {
                        // Use Dispose so a failing assertion with dirty editor
                        // contents cannot leave an unattended save dialog open.
                        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
                        {
                            try { form.Dispose(); }
                            catch (Exception exception)
                            {
                                failure = Combine(failure, exception);
                            }
                        }
                        // Let callbacks queued by disposal run before exiting.
                        dispatcher.BeginInvoke(new Action(delegate
                        {
                            teardownRan = true;
                            context.ExitThread();
                        }));
                    }
                }));
                try { Application.Run(context); }
                catch (Exception exception) { failure = Combine(failure, exception); }
            }

            if (!testsRan || !teardownRan)
                failure = Combine(failure, new InvalidOperationException(
                    "The UI test message pump did not complete test execution and teardown."));
            if (Application.OpenForms.Count != 0)
                failure = Combine(failure, new InvalidOperationException(
                    "The UI suite leaked " + Application.OpenForms.Count + " open form(s)."));
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static Exception Combine(Exception first, Exception next)
        {
            return first == null ? next : new AggregateException(first, next);
        }
    }
}
