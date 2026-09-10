// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    // Owned and disposed by the UI thread. Names do not include version or path:
    // installed and portable copies share one UI per Windows user/session.
    internal sealed class SingleInstanceGuard : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly EventWaitHandle _activate;
        private RegisteredWaitHandle _registration;
        private bool _ownsMutex;
        private volatile bool _disposed;

        internal static string ApplicationScope
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return @"Local\SpiritSchema.TarkovServerGuard.UI." + identity.User.Value;
            }
        }

        internal SingleInstanceGuard(string scope)
        {
            // Create the event first so an early second launch cannot lose its signal
            // while the owner is still constructing the main window.
            _activate = new EventWaitHandle(false, EventResetMode.AutoReset, scope + ".Activate");
            _mutex = new Mutex(false, scope + ".Mutex");
            try { _ownsMutex = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
        }

        internal bool IsPrimary { get { return _ownsMutex; } }

        internal void RequestActivation() { _activate.Set(); }

        internal void Attach(Form form)
        {
            if (!_ownsMutex) throw new InvalidOperationException("Only the primary instance can attach a window.");
            if (_registration != null) throw new InvalidOperationException("A window is already attached.");
            // Application.Restart can launch the next process before Main's final
            // cleanup returns. Release once the UI is closed so it is not rejected.
            form.FormClosed += delegate { Dispose(); };
            // Called after Shown, when BeginInvoke has a real window handle.
            _registration = ThreadPool.RegisterWaitForSingleObject(_activate, delegate(object state, bool timedOut)
            {
                if (_disposed) return;
                try
                {
                    form.BeginInvoke(new Action(delegate
                    {
                        if (_disposed || form.IsDisposed || form.Disposing) return;
                        RestoreWindow(form);
                    }));
                }
                catch (InvalidOperationException) { /* Window is shutting down. */ }
            }, null, Timeout.Infinite, false);
        }

        internal static void RestoreWindow(Form form)
        {
            if (!form.Visible) form.Show();
            if (form.WindowState == FormWindowState.Minimized)
                form.WindowState = FormWindowState.Normal;
            form.Activate();
            // Preserve modal workflows instead of activating a disabled owner alone.
            foreach (Form owned in form.OwnedForms)
                if (owned.Visible && !owned.IsDisposed) owned.Activate();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_registration != null) _registration.Unregister(null);
            if (_ownsMutex) { _mutex.ReleaseMutex(); _ownsMutex = false; }
            _mutex.Dispose();
            _activate.Dispose();
        }
    }
}
