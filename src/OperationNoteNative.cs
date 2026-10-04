// Copyright © 2026 Spirit-Schema. All rights reserved.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TarkovServerReporter
{
    internal static class OperationNoteNative
    {
        internal const int ExtendedStyle = -20;
        internal const long Transparent = 0x20;
        internal const long NoActivate = 0x08000000;
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLong64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLong64(IntPtr window, int index, IntPtr value);
        internal static long GetStyle(IntPtr window)
        {
            return IntPtr.Size == 8 ? GetWindowLong64(window, ExtendedStyle).ToInt64() : GetWindowLong32(window, ExtendedStyle);
        }
        internal static void SetInputThrough(IntPtr window, bool locked)
        {
            if (window == IntPtr.Zero) return;
            long style = GetStyle(window);
            style = locked ? style | Transparent | NoActivate : style & ~(Transparent | NoActivate);
            if (IntPtr.Size == 8) SetWindowLong64(window, ExtendedStyle, new IntPtr(style));
            else SetWindowLong32(window, ExtendedStyle, (int)style);
        }
        internal static bool IsGameWindow(IntPtr window)
        {
            uint id;
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out id) == 0) return false;
            try
            {
                using (Process process = Process.GetProcessById((int)id))
                    return string.Equals(process.ProcessName, "EscapeFromTarkov", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(process.ProcessName, "EscapeFromTarkovArena", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(process.ProcessName, "Arena", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        internal static uint GetProcessId(IntPtr window)
        {
            uint id; return GetWindowThreadProcessId(window, out id) == 0 ? 0 : id;
        }
        internal static bool IsSameReturnTarget(IntPtr window, uint processId)
        {
            return processId != 0 && IsWindow(window) && IsWindowVisible(window) && !IsIconic(window) && GetProcessId(window) == processId;
        }
        // Never undo the user's Alt+Tab. Both the current owner and original target must still match.
        internal static bool MayReturnFocus(IntPtr foreground, IntPtr noteWindow, bool targetVerified)
        {
            return foreground == noteWindow && targetVerified;
        }
    }

    internal sealed class OperationNoteHotKeys : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
        internal event Action Toggle;
        internal event Action Edit;
        internal bool ToggleRegistered { get; private set; }
        internal bool EditRegistered { get; private set; }
        internal OperationNoteHotKeys(AppPreferences preferences)
        {
            CreateHandle(new CreateParams { Caption = "TSG operation note shortcuts", Parent = new IntPtr(-3) });
            AppHotKeyPreference toggle = preferences.OverlayToggleHotKey ?? AppPreferencesStore.CreateDefaultOverlayToggleHotKey();
            AppHotKeyPreference edit = preferences.OverlayEditHotKey ?? AppPreferencesStore.CreateDefaultOverlayEditHotKey();
            ToggleRegistered = RegisterHotKey(Handle, 881, (uint)toggle.Modifiers | 0x4000, (uint)toggle.Key);
            EditRegistered = RegisterHotKey(Handle, 882, (uint)edit.Modifiers | 0x4000, (uint)edit.Key);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
            {
                if (message.WParam.ToInt32() == 881 && Toggle != null) Toggle();
                if (message.WParam.ToInt32() == 882 && Edit != null) Edit();
            }
            base.WndProc(ref message);
        }
        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            if (ToggleRegistered) UnregisterHotKey(Handle, 881);
            if (EditRegistered) UnregisterHotKey(Handle, 882);
            DestroyHandle();
        }
    }
}
