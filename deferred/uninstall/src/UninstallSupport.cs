// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace TarkovServerReporter
{
    internal enum ApplicationInstallKind
    {
        Developer,
        Portable,
        Installed
    }

    internal sealed class UninstallLaunchPlan
    {
        internal ApplicationInstallKind InstallKind { get; set; }
        internal string UpdaterPath { get; set; }
        internal string Arguments { get; set; }
        internal bool DeleteUserData { get; set; }
        internal string UnavailableReason { get; set; }

        internal bool CanStart
        {
            get
            {
                return InstallKind == ApplicationInstallKind.Installed
                    && !string.IsNullOrWhiteSpace(UpdaterPath)
                    && File.Exists(UpdaterPath);
            }
        }
    }

    internal interface IUninstallProcessLauncher
    {
        bool Start(UninstallLaunchPlan plan);
    }

    internal sealed class SystemUninstallProcessLauncher : IUninstallProcessLauncher
    {
        public bool Start(UninstallLaunchPlan plan)
        {
            if (plan == null || !plan.CanStart) return false;
            var startInfo = new ProcessStartInfo
            {
                FileName = plan.UpdaterPath,
                Arguments = plan.Arguments,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(plan.UpdaterPath),
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (plan.DeleteUserData)
                startInfo.EnvironmentVariables[UninstallSupport.DeleteDataEnvironmentName] =
                    UninstallSupport.DeleteDataEnvironmentValue;
            using (Process process = Process.Start(startInfo))
                return process != null;
        }
    }

    internal static class UninstallSupport
    {
        internal const string ExpectedPackageId = "SpiritSchema.TarkovServerGuard";
        internal const string DeleteDataEnvironmentName = "TSG_UNINSTALL_DELETE_USER_DATA";
        internal const string DeleteDataEnvironmentValue = "explicit-user-confirmation-v1";
        internal const string UninstallArguments = "uninstall --silent";
        internal const string StartMenuShortcutName = "Tarkov Server Guard 제거.lnk";

        internal static UninstallLaunchPlan CreateLaunchPlan(
            string executablePath,
            bool deleteUserData)
        {
            string updaterPath;
            ApplicationInstallKind kind = DetectInstallKind(executablePath, out updaterPath);
            return new UninstallLaunchPlan
            {
                InstallKind = kind,
                UpdaterPath = updaterPath,
                Arguments = UninstallArguments,
                DeleteUserData = deleteUserData,
                UnavailableReason = kind == ApplicationInstallKind.Portable
                    ? "Portable판은 Windows 설치 목록에 등록되지 않는 무설치 배포입니다."
                    : (kind == ApplicationInstallKind.Developer
                        ? "개발·테스트 빌드에서는 실제 설치 제거를 시작하지 않습니다."
                        : null)
            };
        }

        internal static UninstallLaunchPlan CreateCurrentLaunchPlan(bool deleteUserData)
        {
            return CreateLaunchPlan(GetEntryExecutablePath(), deleteUserData);
        }

        internal static bool TryStart(
            UninstallLaunchPlan plan,
            IUninstallProcessLauncher launcher,
            out string errorMessage)
        {
            errorMessage = null;
            if (plan == null || !plan.CanStart)
            {
                errorMessage = plan == null || string.IsNullOrWhiteSpace(plan.UnavailableReason)
                    ? "Velopack 설치판의 제거 경로를 확인할 수 없습니다."
                    : plan.UnavailableReason;
                return false;
            }
            if (launcher == null)
            {
                errorMessage = "설치 제거 실행기를 준비하지 못했습니다.";
                return false;
            }

            try
            {
                if (!launcher.Start(plan))
                {
                    errorMessage = "Windows 설치 제거 프로그램을 시작하지 못했습니다.";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = "Windows 설치 제거 프로그램을 시작하지 못했습니다: "
                    + exception.Message;
                return false;
            }
        }

        internal static ApplicationInstallKind DetectInstallKind(
            string executablePath,
            out string updaterPath)
        {
            updaterPath = null;
            string root;
            string current;
            if (!TryResolveVelopackLayout(executablePath, out root, out current))
                return ApplicationInstallKind.Developer;

            string portableMarker = Path.Combine(root, ".portable");
            if (File.Exists(portableMarker)) return ApplicationInstallKind.Portable;

            string updater = Path.Combine(root, "Update.exe");
            string manifest = Path.Combine(current, "sq.version");
            if (!File.Exists(updater) || !File.Exists(manifest))
                return ApplicationInstallKind.Developer;
            if (!IsExpectedVelopackManifest(manifest, Path.GetFileName(executablePath)))
                return ApplicationInstallKind.Developer;

            updaterPath = updater;
            return ApplicationInstallKind.Installed;
        }

        internal static bool IsExpectedVelopackManifest(
            string manifestPath,
            string executableFileName)
        {
            if (string.IsNullOrWhiteSpace(manifestPath)
                || string.IsNullOrWhiteSpace(executableFileName)) return false;
            try
            {
                var info = new FileInfo(Path.GetFullPath(manifestPath));
                if (!info.Exists || info.Length <= 0 || info.Length > 256 * 1024)
                    return false;

                string id = null;
                string version = null;
                string mainExe = null;
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    MaxCharactersInDocument = 256 * 1024
                };
                using (FileStream stream = new FileStream(
                    info.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (XmlReader reader = XmlReader.Create(stream, settings))
                {
                    var document = new XmlDocument { XmlResolver = null };
                    document.Load(reader);
                    foreach (XmlNode node in document.GetElementsByTagName("*"))
                    {
                        string localName = node.LocalName;
                        if (!string.Equals(localName, "id", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(localName, "version", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(localName, "mainExe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string value = (node.InnerText ?? string.Empty).Trim();
                        if (string.Equals(localName, "id", StringComparison.OrdinalIgnoreCase))
                        {
                            if (id != null) return false;
                            id = value;
                        }
                        else if (string.Equals(localName, "version", StringComparison.OrdinalIgnoreCase))
                        {
                            if (version != null) return false;
                            version = value;
                        }
                        else
                        {
                            if (mainExe != null) return false;
                            mainExe = value;
                        }
                    }
                }

                return string.Equals(id, ExpectedPackageId, StringComparison.Ordinal)
                    && Regex.IsMatch(
                        version ?? string.Empty,
                        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
                        RegexOptions.CultureInvariant)
                    && string.Equals(
                        mainExe,
                        executableFileName,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        executableFileName,
                        "TarkovServerGuard.exe",
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldDeleteUserDataFromEnvironment(string value)
        {
            return string.Equals(
                value,
                DeleteDataEnvironmentValue,
                StringComparison.Ordinal);
        }

        internal static string GetOwnedStartMenuShortcutPath(string programsDirectory)
        {
            if (string.IsNullOrWhiteSpace(programsDirectory)) return null;
            try
            {
                return Path.Combine(Path.GetFullPath(programsDirectory), StartMenuShortcutName);
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsSafeOwnedStartMenuShortcut(
            string programsDirectory,
            string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(programsDirectory)
                || string.IsNullOrWhiteSpace(candidatePath)) return false;
            try
            {
                string programs = Path.GetFullPath(programsDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(candidatePath);
                return string.Equals(
                    Path.GetDirectoryName(candidate),
                    programs,
                    StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        Path.GetFileName(candidate),
                        StartMenuShortcutName,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static IList<string> GetOwnedUserDataDirectories(string localApplicationData)
        {
            var paths = new List<string>();
            if (string.IsNullOrWhiteSpace(localApplicationData)) return paths;
            string root;
            try { root = Path.GetFullPath(localApplicationData); }
            catch { return paths; }
            paths.Add(Path.Combine(root, "TarkovServerGuard"));
            paths.Add(Path.Combine(root, "TarkovServerReporter"));
            return paths;
        }

        internal static bool IsSafeOwnedUserDataDirectory(
            string localApplicationData,
            string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(localApplicationData)
                || string.IsNullOrWhiteSpace(candidatePath)) return false;
            try
            {
                string root = Path.GetFullPath(localApplicationData)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(candidatePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string parent = Path.GetDirectoryName(candidate);
                string leaf = Path.GetFileName(candidate);
                return string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(leaf, "TarkovServerGuard", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(leaf, "TarkovServerReporter", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        // Called only from Velopack's Windows fast uninstall callback. It must
        // show no UI and return quickly. The app-owned Start Menu entry is
        // always removed; user data still requires the explicit environment token.
        internal static void ApplyBeforeUninstallHook()
        {
            TryDeleteStartMenuUninstallShortcut();
            ApplyExplicitUserDataDeletionFromHook();
        }

        internal static void ApplyExplicitUserDataDeletionFromHook()
        {
            string confirmation = Environment.GetEnvironmentVariable(DeleteDataEnvironmentName);
            if (!ShouldDeleteUserDataFromEnvironment(confirmation)) return;

            string localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            foreach (string directory in GetOwnedUserDataDirectories(localApplicationData))
            {
                TryDeleteOwnedUserDataDirectory(localApplicationData, directory);
            }
        }

        internal static bool TryDeleteOwnedUserDataDirectory(
            string localApplicationData,
            string directory)
        {
            if (!IsSafeOwnedUserDataDirectory(localApplicationData, directory)) return false;
            try
            {
                if (!Directory.Exists(directory)) return true;
                string ownedRoot = Path.GetFullPath(directory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                FileAttributes attributes = File.GetAttributes(ownedRoot);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                DeleteDirectoryWithoutFollowingReparsePoints(ownedRoot, ownedRoot, 0);
                return !Directory.Exists(ownedRoot);
            }
            catch
            {
                // A partial cleanup failure must not corrupt or block Velopack's
                // own uninstall of app files, shortcuts, and registration.
                return false;
            }
        }

        private static void DeleteDirectoryWithoutFollowingReparsePoints(
            string ownedRoot,
            string directory,
            int depth)
        {
            if (depth > 64 || !IsPathInsideOrEqual(ownedRoot, directory))
                throw new IOException("앱 소유 데이터 삭제 범위를 벗어났습니다.");

            FileAttributes directoryAttributes = File.GetAttributes(directory);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                Directory.Delete(directory, false);
                return;
            }

            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                if (!IsPathInsideOrEqual(ownedRoot, entry))
                    throw new IOException("앱 소유 데이터 삭제 범위를 벗어났습니다.");
                FileAttributes attributes = File.GetAttributes(entry);
                bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                bool isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                if (isDirectory)
                {
                    if (isReparsePoint)
                        Directory.Delete(entry, false);
                    else
                        DeleteDirectoryWithoutFollowingReparsePoints(
                            ownedRoot,
                            entry,
                            depth + 1);
                }
                else
                {
                    if (!isReparsePoint)
                        File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                    File.Delete(entry);
                }
            }
            Directory.Delete(directory, false);
        }

        private static bool IsPathInsideOrEqual(string root, string candidate)
        {
            try
            {
                string normalizedRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedCandidate = Path.GetFullPath(candidate)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(
                    normalizedRoot,
                    normalizedCandidate,
                    StringComparison.OrdinalIgnoreCase)
                    || normalizedCandidate.StartsWith(
                        normalizedRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // Velopack already registers Update.exe as the Windows Installed Apps
        // uninstaller. This extra shortcut opens our option UI; it is not a
        // second uninstall implementation. Our before-uninstall hook owns its
        // exact cleanup because it was not created by Velopack itself.
        internal static void TryCreateStartMenuUninstallShortcut()
        {
            try
            {
                string executablePath = GetEntryExecutablePath();
                string updaterPath;
                if (DetectInstallKind(executablePath, out updaterPath)
                    != ApplicationInstallKind.Installed) return;

                string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (string.IsNullOrWhiteSpace(programs)) return;
                Directory.CreateDirectory(programs);
                string shortcutPath = GetOwnedStartMenuShortcutPath(programs);
                if (!IsSafeOwnedStartMenuShortcut(programs, shortcutPath)) return;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = null;
                object shortcut = null;
                try
                {
                    shell = Activator.CreateInstance(shellType);
                    shortcut = shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[] { shortcutPath });
                    Type shortcutType = shortcut.GetType();
                    shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty,
                        null, shortcut, new object[] { executablePath });
                    shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty,
                        null, shortcut, new object[] { "--uninstall" });
                    shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                        null, shortcut, new object[] { Path.GetDirectoryName(executablePath) });
                    shortcutType.InvokeMember("Description", BindingFlags.SetProperty,
                        null, shortcut, new object[] { "Tarkov Server Guard 제거 옵션" });
                    shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod,
                        null, shortcut, null);
                }
                finally
                {
                    ReleaseComObject(shortcut);
                    ReleaseComObject(shell);
                }
            }
            catch
            {
                // The Installed Apps entry created by Velopack remains the
                // supported fallback if a Start Menu shortcut cannot be made.
            }
        }

        private static void TryDeleteStartMenuUninstallShortcut()
        {
            TryDeleteOwnedStartMenuShortcut(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs));
        }

        internal static bool TryDeleteOwnedStartMenuShortcut(string programs)
        {
            try
            {
                string shortcutPath = GetOwnedStartMenuShortcutPath(programs);
                if (!IsSafeOwnedStartMenuShortcut(programs, shortcutPath)) return false;
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                return !File.Exists(shortcutPath);
            }
            catch
            {
                // A stale shortcut is less harmful than blocking the app's
                // registered Velopack uninstall operation.
                return false;
            }
        }

        private static bool TryResolveVelopackLayout(
            string executablePath,
            out string root,
            out string current)
        {
            root = null;
            current = null;
            if (string.IsNullOrWhiteSpace(executablePath)) return false;
            try
            {
                string executable = Path.GetFullPath(executablePath);
                string directory = Path.GetDirectoryName(executable);
                if (string.IsNullOrWhiteSpace(directory)) return false;

                if (string.Equals(
                    Path.GetFileName(directory),
                    "current",
                    StringComparison.OrdinalIgnoreCase))
                {
                    current = directory;
                    root = Path.GetDirectoryName(directory);
                }
                else
                {
                    root = directory;
                    current = Path.Combine(root, "current");
                }
                return !string.IsNullOrWhiteSpace(root) && Directory.Exists(current);
            }
            catch
            {
                root = null;
                current = null;
                return false;
            }
        }

        private static string GetEntryExecutablePath()
        {
            try
            {
                Assembly entry = Assembly.GetEntryAssembly();
                return entry == null ? null : entry.Location;
            }
            catch
            {
                return null;
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(value))
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
            }
            catch { }
        }
    }
}
