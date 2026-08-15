// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeDarkTheme.RefreshForSystemTheme(this, true);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            NativeDarkTheme.ApplyWindowChrome(this, true);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            NativeDarkTheme.ApplyWindowChrome(this, false);
            base.OnDeactivate(e);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (NativeDarkTheme.IsSystemThemeChangeMessage(message.Msg))
                NativeDarkTheme.RefreshForSystemTheme(this, ContainsFocus);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _applicationIcon != null)
                _applicationIcon.Dispose();
        }
    }

    /// <summary>
    /// Asks Windows to render native scrollbars and top-level window chrome in dark colors.
    /// The application keeps using its existing custom colors for every other control.
    /// Unsupported Windows versions and high-contrast mode retain the system default.
    /// </summary>
    internal static class NativeDarkTheme
    {
        private const string DarkExplorerTheme = "DarkMode_Explorer";
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeLegacy = 19;
        private const int DwmBorderColor = 34;
        private const int DwmCaptionColor = 35;
        private const int DwmTextColor = 36;
        private const int DwmColorDefault = -1;
        private const int SettingChangeMessage = 0x001A;
        private const int ThemeChangedMessage = 0x031A;

        private static readonly Color ActiveCaption = Color.FromArgb(15, 18, 22);
        private static readonly Color InactiveCaption = Color.FromArgb(31, 38, 46);
        private static readonly Color ActiveBorder = Color.FromArgb(54, 63, 74);
        private static readonly Color InactiveBorder = Color.FromArgb(70, 79, 90);
        private static readonly Color ActiveText = Color.FromArgb(244, 246, 248);
        private static readonly Color InactiveText = Color.FromArgb(157, 168, 181);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(
            IntPtr windowHandle,
            string subApplicationName,
            string subIdentifierList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr windowHandle,
            int attribute,
            ref int value,
            int valueSize);

        internal static void ObserveControlTree(Control root)
        {
            if (root == null || IsHighContrastEnabled()) return;

            ObserveControl(root);
            foreach (Control child in root.Controls)
                ObserveControlTree(child);
        }

        internal static bool IsSystemThemeChangeMessage(int message)
        {
            return message == SettingChangeMessage || message == ThemeChangedMessage;
        }

        internal static void RefreshForSystemTheme(Form form, bool active)
        {
            if (form == null || !form.IsHandleCreated) return;
            if (IsHighContrastEnabled())
            {
                ResetControlTree(form);
                ResetWindowChrome(form);
                return;
            }

            ObserveControlTree(form);
            ApplyWindowChrome(form, active);
        }

        internal static void ApplyWindowChrome(Form form, bool active)
        {
            if (form == null || !form.IsHandleCreated || IsHighContrastEnabled()) return;
            try
            {
                int enabled = 1;
                int result = DwmSetWindowAttribute(
                    form.Handle,
                    DwmUseImmersiveDarkMode,
                    ref enabled,
                    sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(
                        form.Handle,
                        DwmUseImmersiveDarkModeLegacy,
                        ref enabled,
                        sizeof(int));
                }

                int caption = ToColorReference(active ? ActiveCaption : InactiveCaption);
                int border = ToColorReference(active ? ActiveBorder : InactiveBorder);
                int text = ToColorReference(active ? ActiveText : InactiveText);
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmCaptionColor,
                    ref caption,
                    sizeof(int));
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmBorderColor,
                    ref border,
                    sizeof(int));
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmTextColor,
                    ref text,
                    sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (ExternalException) { }
        }

        private static void ObserveControl(Control control)
        {
            control.ControlAdded -= ControlAdded;
            control.ControlAdded += ControlAdded;

            if (!CanShowNativeScrollbars(control)) return;
            control.HandleCreated -= ScrollControlHandleCreated;
            control.HandleCreated += ScrollControlHandleCreated;
            TryApplyTo(control);
        }

        private static void ResetControlTree(Control root)
        {
            if (root == null) return;
            if (CanShowNativeScrollbars(root) && root.IsHandleCreated)
            {
                try { SetWindowTheme(root.Handle, null, null); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                catch (ExternalException) { }
            }
            foreach (Control child in root.Controls)
                ResetControlTree(child);
        }

        private static void ResetWindowChrome(Form form)
        {
            try
            {
                int disabled = 0;
                int result = DwmSetWindowAttribute(
                    form.Handle,
                    DwmUseImmersiveDarkMode,
                    ref disabled,
                    sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(
                        form.Handle,
                        DwmUseImmersiveDarkModeLegacy,
                        ref disabled,
                        sizeof(int));
                }

                int systemDefault = DwmColorDefault;
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmCaptionColor,
                    ref systemDefault,
                    sizeof(int));
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmBorderColor,
                    ref systemDefault,
                    sizeof(int));
                DwmSetWindowAttribute(
                    form.Handle,
                    DwmTextColor,
                    ref systemDefault,
                    sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (ExternalException) { }
        }

        private static void ControlAdded(object sender, ControlEventArgs e)
        {
            ObserveControlTree(e.Control);
        }

        private static void ScrollControlHandleCreated(object sender, EventArgs e)
        {
            TryApplyTo(sender as Control);
        }

        private static bool CanShowNativeScrollbars(Control control)
        {
            if (control is DataGridView || control is ScrollBar || control is RichTextBox
                || control is ListBox || control is ListView || control is TreeView)
                return true;

            var textBox = control as TextBox;
            if (textBox != null && textBox.Multiline) return true;

            var scrollable = control as ScrollableControl;
            return scrollable != null && scrollable.AutoScroll;
        }

        private static bool IsHighContrastEnabled()
        {
            try { return SystemInformation.HighContrast; }
            catch { return true; }
        }

        private static int ToColorReference(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        private static void TryApplyTo(Control control)
        {
            if (control == null || !control.IsHandleCreated || IsHighContrastEnabled()) return;
            try
            {
                // SetWindowTheme is supported on older Windows too. Unknown theme names
                // simply fail, so no version-specific or undocumented ordinal API is used.
                SetWindowTheme(control.Handle, DarkExplorerTheme, null);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (ExternalException) { }
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
