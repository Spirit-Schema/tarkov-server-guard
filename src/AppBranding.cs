// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    /// <summary>
    /// Gives every top-level window the icon embedded in the running executable.
    /// The icon is cloned per form so dialog lifetimes never share a native handle.
    /// </summary>
    public class BrandedForm : Form
    {
        private readonly Icon _applicationIcon;

        public BrandedForm()
        {
            _applicationIcon = AppBranding.TryCreateApplicationIcon();
            if (_applicationIcon != null)
                Icon = _applicationIcon;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _applicationIcon != null)
                _applicationIcon.Dispose();
        }
    }

    internal static class AppBranding
    {
        internal static Icon TryCreateApplicationIcon()
        {
            try
            {
                string executablePath = Application.ExecutablePath;
                if (string.IsNullOrWhiteSpace(executablePath)) return null;

                using (Icon executableIcon = Icon.ExtractAssociatedIcon(executablePath))
                {
                    return executableIcon == null ? null : (Icon)executableIcon.Clone();
                }
            }
            catch
            {
                // Branding must never prevent the application or a test host from opening.
                return null;
            }
        }
    }
}
