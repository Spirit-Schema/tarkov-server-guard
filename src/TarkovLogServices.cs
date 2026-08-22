// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace TarkovServerReporter
{
    public sealed class TarkovLogPaths
    {
        public string EftPath { get; set; }
        public string ArenaPath { get; set; }

        public string GetPath(TarkovGame game)
        {
            return game == TarkovGame.Arena ? ArenaPath : EftPath;
        }

        public void SetPath(TarkovGame game, string path)
        {
            if (game == TarkovGame.Arena)
                ArenaPath = path;
            else
                EftPath = path;
        }
    }

    public sealed class RaidLogScanQuery
    {
        public RaidLogScanQuery()
        {
            MaximumRecords = 100;
        }

        public int MaximumRecords { get; set; }
        public DateTime? StartInclusive { get; set; }
        public DateTime? EndExclusive { get; set; }
        public TarkovGame? GameFilter { get; set; }
    }

    public sealed class RaidLogScanResult
    {
        public RaidLogScanResult()
        {
            Sessions = new List<ServerSession>();
            ScanCompletedWithoutErrors = true;
        }

        public IList<ServerSession> Sessions { get; set; }
        public int EftFoldersScanned { get; set; }
        public int ArenaFoldersScanned { get; set; }
        public int TotalMatchingSessions { get; set; }
        public bool TotalMatchingSessionsIsExact { get; set; }
        public bool ScanCompletedWithoutErrors { get; set; }

        public bool HasMoreSessions
        {
            get
            {
                int returned = Sessions == null ? 0 : Sessions.Count;
                return !TotalMatchingSessionsIsExact || TotalMatchingSessions > returned;
            }
        }
    }

    public sealed class LauncherSelectionInfo
    {
        public string EftSelection { get; set; }
        public string ArenaSelection { get; set; }
        public DateTime? EftUpdatedAt { get; set; }
        public DateTime? ArenaUpdatedAt { get; set; }

        public string GetDisplay(TarkovGame game)
        {
            string value = game == TarkovGame.Arena ? ArenaSelection : EftSelection;
            return string.IsNullOrWhiteSpace(value) ? "선택 기록 없음" : value;
        }
    }

    public sealed class TarkovLogSettingsStore
    {
        private readonly string _settingsPath;

        public TarkovLogSettingsStore()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard",
                "log-paths.txt");
        }

        public TarkovLogPaths Load()
        {
            var paths = new TarkovLogPaths();
            try
            {
                if (File.Exists(_settingsPath))
                {
                    foreach (string line in File.ReadAllLines(_settingsPath, Encoding.UTF8))
                    {
                        int separator = line.IndexOf('=');
                        if (separator <= 0) continue;
                        string key = line.Substring(0, separator).Trim();
                        string value = line.Substring(separator + 1).Trim();
                        if (!Directory.Exists(value)) continue;
                        if (string.Equals(key, "eft", StringComparison.OrdinalIgnoreCase))
                            paths.EftPath = value;
                        else if (string.Equals(key, "arena", StringComparison.OrdinalIgnoreCase))
                            paths.ArenaPath = value;
                    }
                }

                if (string.IsNullOrWhiteSpace(paths.EftPath))
                    paths.EftPath = new SettingsStore().LoadLogPath();
            }
            catch
            {
                // Automatic discovery still works if settings cannot be read.
            }
            return paths;
        }

        public void Save(TarkovLogPaths paths)
        {
            try
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string[] lines =
                {
                    "eft=" + (paths == null ? string.Empty : paths.EftPath ?? string.Empty),
                    "arena=" + (paths == null ? string.Empty : paths.ArenaPath ?? string.Empty)
                };
                File.WriteAllLines(_settingsPath, lines, Encoding.UTF8);
            }
            catch
            {
                // The app remains usable without persisted paths.
            }
        }
    }

    public static class TarkovLogPathFinder
    {
        private const string EftSteamAppId = "3932890";
        private static readonly Regex FullyQualifiedLocalPathRegex = new Regex(
            @"^[A-Za-z]:[\\/]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private sealed class PathCandidate
        {
            public string Path { get; set; }
            public int Priority { get; set; }
            public DateTime LatestLogWriteUtc { get; set; }
        }

        public static TarkovLogPaths Find(TarkovLogPaths savedPaths)
        {
            var result = new TarkovLogPaths();
            result.EftPath = FindForGame(
                TarkovGame.Eft,
                savedPaths == null ? null : savedPaths.EftPath);
            result.ArenaPath = FindForGame(
                TarkovGame.Arena,
                savedPaths == null ? null : savedPaths.ArenaPath);
            return result;
        }

        public static string FindForGame(TarkovGame game, string savedPath)
        {
            var candidates = new List<PathCandidate>();

            AddCandidate(candidates, FindFromProcess(
                game == TarkovGame.Arena ? "EscapeFromTarkovArena" : "EscapeFromTarkov"), 500);

            foreach (string path in FindFromUninstallRegistry(game))
                AddCandidate(candidates, path, 450);

            foreach (string steamRoot in FindSteamRoots())
            {
                foreach (string path in FindSteamPathsForGame(game, steamRoot))
                    AddCandidate(candidates, path, 450);
            }

            // A manually selected path remains a candidate, but no longer prevents a newer
            // official or Steam installation from repairing a stale saved selection.
            AddCandidate(candidates, savedPath, 350);

            if (game == TarkovGame.Eft)
            {
                AddCandidate(
                    candidates,
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Battlestate Games",
                        "EFT",
                        "Logs"),
                    300);
            }

            foreach (string path in FindFromCommonLocations(game))
                AddCandidate(candidates, path, 100);

            PathCandidate selected = candidates
                .OrderByDescending(item => item.LatestLogWriteUtc)
                .ThenByDescending(item => item.Priority)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return selected == null ? null : selected.Path;
        }

        internal static string FindSteamForGameFromRoot(TarkovGame game, string steamRoot)
        {
            var candidates = new List<PathCandidate>();
            foreach (string path in FindSteamPathsForGame(game, steamRoot))
                AddCandidate(candidates, path, 450);
            PathCandidate selected = candidates
                .OrderByDescending(item => item.LatestLogWriteUtc)
                .ThenByDescending(item => item.Priority)
                .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            return selected == null ? null : selected.Path;
        }

        private static void AddCandidate(ICollection<PathCandidate> candidates, string path, int priority)
        {
            string normalized = LogPathFinder.NormalizeSelectedFolder(path);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            PathCandidate existing = candidates.FirstOrDefault(
                item => string.Equals(item.Path, normalized, StringComparison.OrdinalIgnoreCase));
            DateTime latest = GetLatestLogWriteUtc(normalized);
            if (existing != null)
            {
                existing.Priority = Math.Max(existing.Priority, priority);
                if (latest > existing.LatestLogWriteUtc) existing.LatestLogWriteUtc = latest;
                return;
            }

            candidates.Add(new PathCandidate
            {
                Path = normalized,
                Priority = priority,
                LatestLogWriteUtc = latest
            });
        }

        private static DateTime GetLatestLogWriteUtc(string logsPath)
        {
            DateTime latest = DateTime.MinValue;
            try
            {
                foreach (string file in Directory.EnumerateFiles(logsPath, "*.log", SearchOption.TopDirectoryOnly).Take(64))
                    UpdateLatestWriteTime(file, ref latest);
            }
            catch
            {
                // Continue with session directories when a top-level live log rotates.
            }

            string[] directories;
            try
            {
                directories = Directory.EnumerateDirectories(logsPath)
                    .OrderByDescending(GetDirectoryWriteTimeSafe)
                    .Take(32)
                    .ToArray();
            }
            catch
            {
                directories = new string[0];
            }

            foreach (string directory in directories)
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly).Take(64))
                        UpdateLatestWriteTime(file, ref latest);
                }
                catch
                {
                    // One rotating or inaccessible session must not hide all other sessions.
                }
            }
            return latest;
        }

        private static void UpdateLatestWriteTime(string file, ref DateTime latest)
        {
            try
            {
                DateTime written = File.GetLastWriteTimeUtc(file);
                if (written > latest) latest = written;
            }
            catch
            {
                // The file may be rotated between enumeration and metadata access.
            }
        }

        private static DateTime GetDirectoryWriteTimeSafe(string directory)
        {
            try
            {
                return Directory.GetLastWriteTimeUtc(directory);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static IList<string> FindFromUninstallRegistry(TarkovGame game)
        {
            string baseName = game == TarkovGame.Arena ? "EscapeFromTarkovArena" : "EscapeFromTarkov";
            string[] subKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + baseName + "_live",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + baseName
            };
            var results = new List<string>();

            RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
            RegistryHive[] hives = { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive hive in hives)
            {
                foreach (RegistryView view in views)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                        {
                            foreach (string subKey in subKeys)
                            {
                                using (RegistryKey key = baseKey.OpenSubKey(subKey))
                                {
                                    string location = key == null
                                        ? null
                                        : Convert.ToString(key.GetValue("InstallLocation"));
                                    string logs = LogPathFinder.NormalizeSelectedFolder(location);
                                    AddUniquePath(results, logs);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Continue with the next hive or registry view.
                    }
                }
            }
            return results;
        }

        private static string FindFromProcess(string processName)
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        string executable = process.MainModule == null ? null : process.MainModule.FileName;
                        string logs = LogPathFinder.NormalizeSelectedFolder(
                            string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable));
                        if (!string.IsNullOrWhiteSpace(logs)) return logs;
                    }
                    catch
                    {
                        // Access to MainModule can be denied.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private static IList<string> FindSteamRoots()
        {
            var roots = new List<string>();
            AddSteamRootFromRegistry(roots, RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam", "SteamPath");
            AddSteamRootFromRegistry(roots, RegistryHive.CurrentUser, @"SOFTWARE\Valve\Steam", "InstallPath");
            AddSteamRootFromRegistry(roots, RegistryHive.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                AddUniqueDirectory(roots, Path.Combine(programFilesX86, "Steam"));
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
                AddUniqueDirectory(roots, Path.Combine(programFiles, "Steam"));
            return roots;
        }

        private static void AddSteamRootFromRegistry(
            ICollection<string> roots,
            RegistryHive hive,
            string subKey,
            string valueName)
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view))
                    using (RegistryKey key = baseKey.OpenSubKey(subKey))
                    {
                        string value = key == null ? null : Convert.ToString(key.GetValue(valueName));
                        AddUniqueDirectory(roots, value);
                    }
                }
                catch
                {
                    // Steam is optional. A missing or inaccessible registry key is expected.
                }
            }
        }

        private static IList<string> FindSteamPathsForGame(TarkovGame game, string steamRoot)
        {
            var results = new List<string>();
            foreach (string library in ReadSteamLibraries(steamRoot))
            {
                string steamApps = Path.Combine(library, "steamapps");
                string common = Path.Combine(steamApps, "common");
                if (!Directory.Exists(common)) continue;

                if (game == TarkovGame.Eft)
                {
                    string manifest = Path.Combine(steamApps, "appmanifest_" + EftSteamAppId + ".acf");
                    string installDirectory = ReadVdfValue(manifest, "installdir", 1024 * 1024);
                    string installRoot = ResolveChildDirectory(common, installDirectory);
                    foreach (string logs in FindLogsUnderInstallRoot(game, installRoot, true))
                        AddUniquePath(results, logs);
                }

                // Arena does not currently have a confirmed standalone Steam AppID. Inspect only
                // top-level Tarkov folders from confirmed Steam libraries and require Arena evidence.
                IEnumerable<string> tarkovFolders;
                try
                {
                    tarkovFolders = Directory.EnumerateDirectories(common)
                        .Where(path => Path.GetFileName(path).IndexOf("Tarkov", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Take(32)
                        .ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (string installRoot in tarkovFolders)
                {
                    foreach (string logs in FindLogsUnderInstallRoot(game, installRoot, false))
                        AddUniquePath(results, logs);
                }
            }
            return results;
        }

        private static IList<string> ReadSteamLibraries(string steamRoot)
        {
            var libraries = new List<string>();
            string root = NormalizeExistingRoot(steamRoot);
            if (root == null) return libraries;
            AddUniqueDirectory(libraries, root);

            string vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            string content = ReadLimitedText(vdfPath, 4 * 1024 * 1024);
            if (string.IsNullOrWhiteSpace(content)) return libraries;

            MatchCollection matches = Regex.Matches(
                content,
                "\"path\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (Match match in matches)
                AddUniqueDirectory(libraries, UnescapeVdfValue(match.Groups["value"].Value));
            return libraries;
        }

        private static string ReadVdfValue(string path, string key, long maximumBytes)
        {
            string content = ReadLimitedText(path, maximumBytes);
            if (string.IsNullOrWhiteSpace(content)) return null;
            Match match = Regex.Match(
                content,
                "\"" + Regex.Escape(key) + "\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? UnescapeVdfValue(match.Groups["value"].Value) : null;
        }

        private static string ReadLimitedText(string path, long maximumBytes)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length < 0 || info.Length > maximumBytes) return null;
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
            catch
            {
                return null;
            }
        }

        private static string UnescapeVdfValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 2048) return null;
            var builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current == '\\' && index + 1 < value.Length
                    && (value[index + 1] == '\\' || value[index + 1] == '"'))
                {
                    builder.Append(value[index + 1]);
                    index++;
                }
                else
                {
                    builder.Append(current);
                }
            }
            return builder.ToString();
        }

        private static string ResolveChildDirectory(string parent, string child)
        {
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(child)
                || child.Length > 512 || Path.IsPathRooted(child) || child.IndexOf('\0') >= 0)
                return null;
            try
            {
                string parentFull = Path.GetFullPath(parent)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string childFull = Path.GetFullPath(Path.Combine(parentFull, child));
                string prefix = parentFull + Path.DirectorySeparatorChar;
                if (!childFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || !Directory.Exists(childFull))
                    return null;
                return childFull;
            }
            catch
            {
                return null;
            }
        }

        private static IList<string> FindLogsUnderInstallRoot(
            TarkovGame game,
            string installRoot,
            bool trustedRoot)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot)) return results;

            if (trustedRoot || LooksLikeGameRoot(game, installRoot))
                AddUniquePath(results, LogPathFinder.NormalizeSelectedFolder(installRoot));

            try
            {
                foreach (string child in Directory.EnumerateDirectories(installRoot)
                    .Where(path => LooksLikeGameRoot(game, path))
                    .Take(24))
                {
                    AddUniquePath(results, LogPathFinder.NormalizeSelectedFolder(child));
                }
            }
            catch
            {
                // Do not recurse through a Steam library. Direct candidates are sufficient.
            }
            return results;
        }

        private static bool LooksLikeGameRoot(TarkovGame game, string directory)
        {
            try
            {
                if (game == TarkovGame.Arena)
                {
                    return Directory.EnumerateFiles(
                        directory,
                        "EscapeFromTarkovArena*.exe",
                        SearchOption.TopDirectoryOnly).Any();
                }
                return File.Exists(Path.Combine(directory, "EscapeFromTarkov.exe"));
            }
            catch
            {
                return false;
            }
        }

        private static IList<string> FindFromCommonLocations(TarkovGame game)
        {
            string[] relativeCandidates = game == TarkovGame.Arena
                ? new[]
                {
                    @"Battlestate Games\Escape from Tarkov Arena",
                    @"Battlestate Games\EFT Arena",
                    @"Games\Escape from Tarkov Arena",
                    @"Escape from Tarkov Arena"
                }
                : new[]
                {
                    @"Battlestate Games\EFT",
                    @"Battlestate Games\Escape From Tarkov",
                    @"Games\Escape From Tarkov",
                    @"Escape From Tarkov",
                    @"EFT"
                };
            var results = new List<string>();
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                    foreach (string relative in relativeCandidates)
                    {
                        string installRoot = Path.Combine(drive.RootDirectory.FullName, relative);
                        if (!LooksLikeGameRoot(game, installRoot)) continue;
                        string logs = LogPathFinder.NormalizeSelectedFolder(installRoot);
                        AddUniquePath(results, logs);
                    }
                }
            }
            catch
            {
                // Fixed-drive fallbacks are optional.
            }
            return results;
        }

        private static string NormalizeExistingRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.IndexOf('\0') >= 0)
                return null;
            try
            {
                string trimmed = path.Trim().Trim('"');
                if (!IsFullyQualifiedLocalPath(trimmed))
                    return null;
                string full = Path.GetFullPath(trimmed);
                return Path.IsPathRooted(full) && Directory.Exists(full) ? full : null;
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsFullyQualifiedLocalPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && FullyQualifiedLocalPathRegex.IsMatch(path);
        }

        private static void AddUniqueDirectory(ICollection<string> paths, string path)
        {
            string normalized = NormalizeExistingRoot(path);
            if (normalized == null) return;
            if (!paths.Contains(normalized, StringComparer.OrdinalIgnoreCase)) paths.Add(normalized);
        }

        private static void AddUniquePath(ICollection<string> paths, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
        }
    }

    internal sealed class PvpSeasonIdentity
    {
        public int Number { get; set; }
        public string Key { get; set; }
        public string InternalName { get; set; }
        public PvpSeasonEvidence Evidence { get; set; }
    }

    internal static class PvpSeasonCatalog
    {
        private sealed class KnownSeason
        {
            public int Number { get; set; }
            public string Key { get; set; }
            public string InternalName { get; set; }
            public int VersionMajor { get; set; }
            public int VersionMinor { get; set; }
            public int VersionPatch { get; set; }
        }

        // Names and aliases remain metadata here. The UI intentionally renders only
        // the stable number (for example, "PvP시즌1") so a renamed season does not
        // require changes in the presentation layer.
        private static readonly KnownSeason[] KnownSeasons =
        {
            new KnownSeason
            {
                Number = 1,
                Key = "kord-breach",
                InternalName = "KORD BREACH",
                VersionMajor = 1,
                VersionMinor = 1,
                VersionPatch = 0
            }
        };

        internal static PvpSeasonIdentity Resolve(int? explicitNumber, string clientVersion)
        {
            if (IsValidNumber(explicitNumber))
            {
                KnownSeason known = KnownSeasons.FirstOrDefault(
                    item => item.Number == explicitNumber.Value);
                return CreateIdentity(
                    explicitNumber.Value,
                    known,
                    PvpSeasonEvidence.ExplicitLogValue);
            }

            int[] version;
            if (!TryParseNumericVersion(clientVersion, out version)) return null;
            KnownSeason mapped = KnownSeasons.FirstOrDefault(item =>
                version[0] == item.VersionMajor
                && version[1] == item.VersionMinor
                && version[2] == item.VersionPatch);
            return mapped == null
                ? null
                : CreateIdentity(
                    mapped.Number,
                    mapped,
                    PvpSeasonEvidence.VerifiedVersionMapping);
        }

        internal static bool IsValidNumber(int? number)
        {
            return number.HasValue && number.Value > 0 && number.Value <= 999;
        }

        private static PvpSeasonIdentity CreateIdentity(
            int number,
            KnownSeason known,
            PvpSeasonEvidence evidence)
        {
            return new PvpSeasonIdentity
            {
                Number = number,
                Key = known == null ? null : known.Key,
                InternalName = known == null ? null : known.InternalName,
                Evidence = evidence
            };
        }

        private static bool TryParseNumericVersion(string value, out int[] version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Trim().Split('.');
            if (parts.Length < 3 || parts.Length > 8) return false;
            var parsed = new int[parts.Length];
            for (int index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(
                    parts[index],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out parsed[index])
                    || parsed[index] < 0)
                    return false;
            }
            version = parsed;
            return true;
        }
    }

    public static class RaidLogScanner
    {
        private sealed class CachedDirectory
        {
            public long Length { get; set; }
            public DateTime LastWriteUtc { get; set; }
            public int FileCount { get; set; }
            public IList<ServerSession> Sessions { get; set; }
        }

        private sealed class MatchTiming
        {
            public DateTime Timestamp { get; set; }
            public double Seconds { get; set; }
            public bool IsExplicitZero { get; set; }
            public string LogFilePath { get; set; }
            public string ClientVersion { get; set; }
        }

        private sealed class SessionModeEvent
        {
            public DateTime Timestamp { get; set; }
            public TarkovProgressionMode Mode { get; set; }
            public int? PvpSeasonNumber { get; set; }
            public string PvpSeasonKey { get; set; }
            public string PvpSeasonName { get; set; }
            public PvpSeasonEvidence PvpSeasonEvidence { get; set; }
        }

        private sealed class MapPresetEvent
        {
            public DateTime Timestamp { get; set; }
            public string MapName { get; set; }
        }

        private sealed class TimedLogEvent
        {
            public DateTime Timestamp { get; set; }
        }

        private enum ParticipantEventKind
        {
            Mode,
            PrepareProfile,
            EmptyGroup,
            NonEmptyGroup,
            MatchJoin,
            GroupStartRoute,
            GroupInviteAccept,
            GroupReady,
            GroupNotReady,
            GroupMemberRemoved,
            GroupRemoved,
            GroupStart,
            Assignment,
            MatchingCancelled,
            GameStarted
        }

        private sealed class ParticipantLogEvent
        {
            public DateTime Timestamp { get; set; }
            public int Order { get; set; }
            public ParticipantEventKind Kind { get; set; }
            public TarkovProgressionMode Mode { get; set; }
            public string ProfileId { get; set; }
            public string MemberId { get; set; }
            public bool? Ready { get; set; }
            public TarkovCharacterType VisualCharacter { get; set; }
        }

        private sealed class GroupMemberState
        {
            public bool? Ready { get; set; }
            public TarkovCharacterType VisualCharacter { get; set; }
        }

        private sealed class ParticipantGeneration
        {
            public DateTime StartedAt { get; set; }
            public TarkovProgressionMode Mode { get; set; }
            public string BaseProfileId { get; set; }
            public bool HasApplicationMatch { get; set; }
            public bool HasEmptyGroup { get; set; }
            public bool HasMatchJoin { get; set; }
            public bool HasPartyRoute { get; set; }
            public bool HasPartyStart { get; set; }
            public int? PartySize { get; set; }
            public bool VisualPmc { get; set; }
            public bool VisualScav { get; set; }
        }

        private sealed class ParticipantAssignment
        {
            public DateTime Timestamp { get; set; }
            public TarkovProgressionMode Mode { get; set; }
            public TarkovCharacterType ProfileCharacter { get; set; }
            public bool HasEmptyGroup { get; set; }
            public bool HasMatchJoin { get; set; }
            public bool HasPartyRoute { get; set; }
            public bool HasPartyStart { get; set; }
            public int? PartySize { get; set; }
            public bool VisualPmc { get; set; }
            public bool VisualScav { get; set; }
        }

        private sealed class GameStartedEvent
        {
            public DateTime Timestamp { get; set; }
            public double? ReportedSeconds { get; set; }
            public double? RealSeconds { get; set; }
        }

        private sealed class UserReportRequest
        {
            public DateTime? RequestedAt { get; set; }
            public DateTime? SuccessfulResponseAt { get; set; }
            public string RequestId { get; set; }
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, CachedDirectory> DirectoryCache =
            new Dictionary<string, CachedDirectory>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex TimestampRegex = new Regex(
            @"(?<date>\d{4}[-.]\d{2}[-.]\d{2})[ T](?<time>\d{2}:\d{2}:\d{2})(?<fraction>\.\d{1,7})?",
            RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new Regex(
            @"\|(?<version>\d+(?:\.\d+){2,})\|",
            RegexOptions.Compiled);
        private static readonly Regex MatchingRegex = new Regex(
            @"\bMatchingCompleted:(?<reported>-?\d+(?:\.\d+)?)(?:\s+real:(?<real>-?\d+(?:\.\d+)?))?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SessionModeRegex = new Regex(
            @"\bSession mode:\s*(?:(?<mode>Regular|Pve)\b|(?<mode>PvpSeason)(?:\s*(?:Season\s*)?(?:[:=#]\s*)?(?<season>\d{1,3}))?(?![A-Za-z0-9_]))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameCreatedRegex = new Regex(
            @"\bGameCreated:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameStartedRegex = new Regex(
            @"\bGameStarted:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameStartedReportedSecondsRegex = new Regex(
            @"\bGameStarted:\s*(?<seconds>[-+]?\d+(?:\.\d+)?)(?=$|\s)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameStartedRealSecondsRegex = new Regex(
            @"\bGameStarted:.*?\breal:\s*(?<seconds>[-+]?\d+(?:\.\d+)?)(?=$|\s)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EftPostRaidProfileRegex = new Regex(
            @"\bPrepareSelectedProfileLocally\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ArenaMatchEndRegex = new Regex(
            @"\bGameplayState:\s*MatchEnd\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ArenaMatchingStartedRegex = new Regex(
            @"MatchingProgressState:\s*MatchingStarted",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EftServerRegex = new Regex(
            @"RaidMode:\s*(?<raid>[^,]+),\s*Ip:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s*,\s*Port:\s*(?<port>\d+)\s*,\s*Location:\s*(?<map>[^,]+),\s*Sid:\s*(?<sid>[^,]+),\s*GameMode:\s*(?<mode>[^,]+),\s*shortId:\s*(?<short>[^,'\s]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ArenaServerRegex = new Regex(
            @"Server found\.\s*ip:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+port:\s*(?<port>\d+)\s+sid:\s*(?<sid>.+?)\s+shortId:\s*(?<short>\S+)\s+map:\s*(?<map>\S+)\s+mode:\s*(?<mode>\S+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LegacyIpRegex = new Regex(
            @"\bIp:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})(?::(?<port>\d+))?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LegacyMapRegex = new Regex(
            @"scene preset path:maps/(?<map>[^.\s]+)\.bundle",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EndpointRegex = new Regex(
            @"address:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3}):(?<port>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex StatisticsRegex = new Regex(
            @"Statistics \(address:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3}):(?<port>\d+),\s*rtt:\s*(?<rtt>[-+0-9.Ee]+),\s*lose:\s*(?<lose>[-+0-9.Ee]+),\s*sent:\s*(?<sent>\d+),\s*received:\s*(?<received>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DisconnectedReasonRegex = new Regex(
            @"Enter to the 'Disconnected' state \(address:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3}):(?<port>\d+),\s*reason:\s*(?<reason>-?\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DataCenterRegex = new Regex(
            @"^(?<dc>[A-Z]{2}-[A-Z0-9]+?)G\d+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UserReportRouteRegex = new Regex(
            @"/client/report/send(?:\?)?(?=$|[\s,]|\.(?:\s|$))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex BackendRequestIdRegex = new Regex(
            @"\bid\s+\[(?<id>[^\]\r\n]{1,64})\]\s*:",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PrepareProfileIdRegex = new Regex(
            @"\bPrepareSelectedProfileLocally\b.*?\bProfileId:(?<id>[0-9A-Fa-f]{24})(?![0-9A-Fa-f])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AssignmentProfileIdRegex = new Regex(
            @"\bTRACE-NetworkGameCreate\b.*?\bProfileid:\s*(?<id>[0-9A-Fa-f]{24})(?![0-9A-Fa-f])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MatchingGroupIdRegex = new Regex(
            @"\bMatching with group id:(?<id>[^\r\n]*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex PushNotificationMarkerRegex = new Regex(
            @"\bGot notification\s*\|\s*(?<type>GroupMatch[A-Za-z0-9_]+)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const long MaximumPushNotificationFileBytes = 4L * 1024 * 1024;
        private const int MaximumPushNotificationEventChars = 256 * 1024;
        private const int MaximumPushNotificationEventLines = 4096;
        private const int MaximumPushNotificationLineChars = 16 * 1024;
        private const int MaximumOpaqueIdentifierChars = 128;

        private static readonly Dictionary<string, string> EftMapNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "woods_preset", "Woods" },
                { "customs_preset", "Customs" },
                { "bigmap", "Customs" },
                { "shoreline_preset", "Shoreline" },
                { "shopping_mall", "Interchange" },
                { "rezerv_base_preset", "Reserve" },
                { "rezervbase", "Reserve" },
                { "lighthouse_preset", "Lighthouse" },
                { "city_preset", "Streets of Tarkov" },
                { "tarkovstreets", "Streets of Tarkov" },
                { "factory_day_preset", "Factory" },
                { "factory_night_preset", "Factory" },
                { "factory4_day", "Factory" },
                { "factory4_night", "Factory" },
                { "sandbox_preset", "Ground Zero" },
                { "sandbox_high_preset", "Ground Zero" },
                { "sandbox", "Ground Zero" },
                { "sandbox_high", "Ground Zero" },
                { "laboratory_preset", "The Lab" },
                { "laboratory", "The Lab" },
                { "labyrinth_preset", "Labyrinth" }
            };

        public static RaidLogScanResult Scan(TarkovLogPaths paths, int maximumRecords)
        {
            int max = maximumRecords <= 0 ? 100 : maximumRecords;
            var result = new RaidLogScanResult();
            int eftFolders;
            int arenaFolders;
            bool allEftFoldersScanned;
            bool allArenaFoldersScanned;
            bool eftReadSucceeded;
            bool arenaReadSucceeded;
            IList<ServerSession> eft = ScanGameCore(
                paths == null ? null : paths.EftPath,
                TarkovGame.Eft,
                max,
                out eftFolders,
                out allEftFoldersScanned,
                out eftReadSucceeded);
            IList<ServerSession> arena = ScanGameCore(
                paths == null ? null : paths.ArenaPath,
                TarkovGame.Arena,
                max,
                out arenaFolders,
                out allArenaFoldersScanned,
                out arenaReadSucceeded);
            result.EftFoldersScanned = eftFolders;
            result.ArenaFoldersScanned = arenaFolders;
            IList<ServerSession> matching = MergeDuplicateSessions(eft.Concat(arena))
                .Where(session => session != null
                    && (session.HasServerIp || session.HostingMode == TarkovHostingMode.Local))
                .OrderByDescending(session => session.DisplayDetectedAt)
                .ThenByDescending(session => session.LastUpdated)
                .ToList();
            result.TotalMatchingSessions = matching.Count;
            result.ScanCompletedWithoutErrors = eftReadSucceeded && arenaReadSucceeded;
            result.TotalMatchingSessionsIsExact = allEftFoldersScanned
                && allArenaFoldersScanned
                && result.ScanCompletedWithoutErrors;
            result.Sessions = matching.Take(max).ToList();
            return result;
        }

        public static RaidLogScanResult Scan(TarkovLogPaths paths, RaidLogScanQuery query)
        {
            RaidLogScanQuery effectiveQuery = query ?? new RaidLogScanQuery();
            if (effectiveQuery.StartInclusive.HasValue
                && effectiveQuery.EndExclusive.HasValue
                && effectiveQuery.StartInclusive.Value >= effectiveQuery.EndExclusive.Value)
            {
                throw new ArgumentException(
                    "StartInclusive must be earlier than EndExclusive.",
                    "query");
            }
            if (effectiveQuery.GameFilter.HasValue
                && effectiveQuery.GameFilter.Value != TarkovGame.Eft
                && effectiveQuery.GameFilter.Value != TarkovGame.Arena)
            {
                throw new ArgumentOutOfRangeException(
                    "query",
                    "GameFilter must be EFT, Arena, or null.");
            }

            int max = effectiveQuery.MaximumRecords <= 0
                ? 100
                : effectiveQuery.MaximumRecords;
            bool scanEft = !effectiveQuery.GameFilter.HasValue
                || effectiveQuery.GameFilter.Value == TarkovGame.Eft;
            bool scanArena = !effectiveQuery.GameFilter.HasValue
                || effectiveQuery.GameFilter.Value == TarkovGame.Arena;
            int eftFolders = 0;
            int arenaFolders = 0;
            bool allEftFoldersScanned = true;
            bool allArenaFoldersScanned = true;
            bool eftReadSucceeded = true;
            bool arenaReadSucceeded = true;
            IList<ServerSession> eft = scanEft
                ? ScanGameCore(
                    paths == null ? null : paths.EftPath,
                    TarkovGame.Eft,
                    int.MaxValue,
                    out eftFolders,
                    out allEftFoldersScanned,
                    out eftReadSucceeded)
                : new List<ServerSession>();
            IList<ServerSession> arena = scanArena
                ? ScanGameCore(
                    paths == null ? null : paths.ArenaPath,
                    TarkovGame.Arena,
                    int.MaxValue,
                    out arenaFolders,
                    out allArenaFoldersScanned,
                    out arenaReadSucceeded)
                : new List<ServerSession>();

            IEnumerable<ServerSession> matching = MergeDuplicateSessions(eft.Concat(arena))
                .Where(session => session != null
                    && (session.HasServerIp || session.HostingMode == TarkovHostingMode.Local));
            if (effectiveQuery.GameFilter.HasValue)
            {
                TarkovGame game = effectiveQuery.GameFilter.Value;
                matching = matching.Where(session => session.Game == game);
            }
            if (effectiveQuery.StartInclusive.HasValue)
            {
                DateTime startInclusive = effectiveQuery.StartInclusive.Value;
                matching = matching.Where(session => session.DisplayDetectedAt >= startInclusive);
            }
            if (effectiveQuery.EndExclusive.HasValue)
            {
                DateTime endExclusive = effectiveQuery.EndExclusive.Value;
                matching = matching.Where(session => session.DisplayDetectedAt < endExclusive);
            }

            IList<ServerSession> ordered = matching
                .OrderByDescending(session => session.DisplayDetectedAt)
                .ThenByDescending(session => session.LastUpdated)
                .ToList();
            return new RaidLogScanResult
            {
                Sessions = ordered.Take(max).ToList(),
                EftFoldersScanned = eftFolders,
                ArenaFoldersScanned = arenaFolders,
                TotalMatchingSessions = ordered.Count,
                TotalMatchingSessionsIsExact = allEftFoldersScanned
                    && allArenaFoldersScanned
                    && eftReadSucceeded
                    && arenaReadSucceeded,
                ScanCompletedWithoutErrors = eftReadSucceeded && arenaReadSucceeded
            };
        }

        public static IList<ServerSession> ScanGame(
            string logsPath,
            TarkovGame game,
            int maximumFolders,
            out int foldersScanned)
        {
            bool allFoldersScanned;
            bool readSucceeded;
            return ScanGameCore(
                logsPath,
                game,
                maximumFolders,
                out foldersScanned,
                out allFoldersScanned,
                out readSucceeded);
        }

        private static IList<ServerSession> ScanGameCore(
            string logsPath,
            TarkovGame game,
            int maximumFolders,
            out int foldersScanned,
            out bool allFoldersScanned,
            out bool readSucceeded)
        {
            foldersScanned = 0;
            allFoldersScanned = true;
            readSucceeded = true;
            var sessions = new List<ServerSession>();
            if (string.IsNullOrWhiteSpace(logsPath)) return sessions;
            if (!Directory.Exists(logsPath))
            {
                allFoldersScanned = false;
                readSucceeded = false;
                return sessions;
            }

            int max = maximumFolders <= 0 ? 100 : maximumFolders;
            try
            {
                var root = new DirectoryInfo(logsPath);
                var directories = new List<DirectoryInfo>();
                if (GetRelevantFiles(root, game).Length > 0) directories.Add(root);
                directories.AddRange(root.GetDirectories());
                directories = directories
                    .OrderByDescending(directory =>
                        directory.CreationTime > directory.LastWriteTime
                            ? directory.CreationTime
                            : directory.LastWriteTime)
                    .ToList();
                allFoldersScanned = directories.Count <= max;
                directories = directories.Take(max).ToList();

                foreach (DirectoryInfo directory in directories)
                {
                    foldersScanned++;
                    sessions.AddRange(ReadDirectory(directory, game));
                }
            }
            catch
            {
                allFoldersScanned = false;
                readSucceeded = false;
                return sessions;
            }

            return sessions
                .OrderByDescending(session => session.DisplayDetectedAt)
                .ThenByDescending(session => session.LastUpdated)
                .ToList();
        }

        private static IList<ServerSession> ReadDirectory(DirectoryInfo directory, TarkovGame game)
        {
            FileInfo[] relevantFiles = GetRelevantFiles(directory, game);
            if (relevantFiles.Length == 0) return new List<ServerSession>();

            long combinedLength = 0;
            DateTime latestWriteUtc = DateTime.MinValue;
            foreach (FileInfo file in relevantFiles)
            {
                combinedLength += file.Length;
                if (file.LastWriteTimeUtc > latestWriteUtc) latestWriteUtc = file.LastWriteTimeUtc;
            }

            string cacheKey = game + "|" + directory.FullName;
            lock (CacheLock)
            {
                CachedDirectory cached;
                if (DirectoryCache.TryGetValue(cacheKey, out cached)
                    && cached.Length == combinedLength
                    && cached.LastWriteUtc == latestWriteUtc
                    && cached.FileCount == relevantFiles.Length)
                {
                    return cached.Sessions.Select(CloneSession).ToList();
                }
            }

            IList<ServerSession> scanned = ParseDirectory(directory, relevantFiles, game);
            // An in-progress value depends on wall-clock freshness even when the file length and
            // write timestamp do not change. Reparse that single live folder so it naturally
            // becomes Unknown after the conservative activity window instead of being cached
            // as "in progress" forever.
            if (!scanned.Any(session => session.OperationState == RaidOperationState.InProgress))
            {
                lock (CacheLock)
                {
                    DirectoryCache[cacheKey] = new CachedDirectory
                    {
                        Length = combinedLength,
                        LastWriteUtc = latestWriteUtc,
                        FileCount = relevantFiles.Length,
                        Sessions = scanned.Select(CloneSession).ToList()
                    };
                }
            }
            else
            {
                lock (CacheLock) DirectoryCache.Remove(cacheKey);
            }
            return scanned;
        }

        private static IList<ServerSession> ParseDirectory(
            DirectoryInfo directory,
            FileInfo[] relevantFiles,
            TarkovGame game)
        {
            var timings = new List<MatchTiming>();
            var sessionModes = new List<SessionModeEvent>();
            var mapPresets = new List<MapPresetEvent>();
            var gameCreatedEvents = new List<TimedLogEvent>();
            var gameStartedEvents = new List<GameStartedEvent>();
            var eftPostRaidEvents = new List<TimedLogEvent>();
            var arenaMatchEndEvents = new List<TimedLogEvent>();
            FileInfo[] applicationFiles = relevantFiles
                .Where(file => file.Name.IndexOf("application", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (FileInfo file in applicationFiles)
            {
                foreach (string line in ReadLines(file.FullName))
                {
                    DateTime timestamp;
                    if (!TryParseTimestamp(line, out timestamp)) continue;

                    Match modeMatch = SessionModeRegex.Match(line);
                    if (game == TarkovGame.Eft && modeMatch.Success)
                    {
                        sessionModes.Add(CreateSessionModeEvent(
                            timestamp,
                            modeMatch,
                            GetVersion(line)));
                    }

                    Match match = MatchingRegex.Match(line);
                    double seconds;
                    double reportedSeconds = 0;
                    double realSeconds = 0;
                    bool reportedParsed = match.Success && double.TryParse(
                        match.Groups["reported"].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out reportedSeconds);
                    bool realParsed = match.Success
                        && match.Groups["real"].Success
                        && double.TryParse(
                            match.Groups["real"].Value,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out realSeconds);
                    string secondsText = match.Groups["real"].Success
                        ? match.Groups["real"].Value
                        : match.Groups["reported"].Value;
                    if (match.Success && double.TryParse(
                        secondsText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out seconds))
                    {
                        timings.Add(new MatchTiming
                        {
                            Timestamp = timestamp,
                            Seconds = seconds,
                            IsExplicitZero = reportedParsed
                                && realParsed
                                && Math.Abs(reportedSeconds) < 0.0001
                                && Math.Abs(realSeconds) < 0.0001,
                            LogFilePath = file.FullName,
                            ClientVersion = GetVersion(line)
                        });
                    }

                    if (GameStartedRegex.IsMatch(line))
                    {
                        gameStartedEvents.Add(new GameStartedEvent
                        {
                            Timestamp = timestamp,
                            ReportedSeconds = ParseOptionalSeconds(
                                GameStartedReportedSecondsRegex,
                                line),
                            RealSeconds = ParseOptionalSeconds(
                                GameStartedRealSecondsRegex,
                                line)
                        });
                    }

                    if (game == TarkovGame.Eft)
                    {
                        Match mapMatch = LegacyMapRegex.Match(line);
                        if (mapMatch.Success)
                        {
                            mapPresets.Add(new MapPresetEvent
                            {
                                Timestamp = timestamp,
                                MapName = NormalizeMap(TarkovGame.Eft, mapMatch.Groups["map"].Value)
                            });
                        }
                        if (GameCreatedRegex.IsMatch(line))
                            gameCreatedEvents.Add(new TimedLogEvent { Timestamp = timestamp });
                        if (EftPostRaidProfileRegex.IsMatch(line))
                            eftPostRaidEvents.Add(new TimedLogEvent { Timestamp = timestamp });
                    }
                }
            }

            sessionModes.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            mapPresets.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            gameCreatedEvents.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            gameStartedEvents.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));
            eftPostRaidEvents.Sort((left, right) => left.Timestamp.CompareTo(right.Timestamp));

            var sessionsByKey = new Dictionary<string, ServerSession>(StringComparer.OrdinalIgnoreCase);
            string legacyIp = null;
            int legacyPort = 0;
            string legacyMap = null;
            DateTime? legacyTimestamp = null;
            string legacyVersion = null;
            string legacyFile = null;

            FileInfo[] serverFiles = game == TarkovGame.Arena
                ? relevantFiles.Where(file => file.Name.IndexOf("lifecycle", StringComparison.OrdinalIgnoreCase) >= 0).ToArray()
                : applicationFiles;
            DateTime? arenaMatchingStarted = null;
            foreach (FileInfo file in serverFiles
                .OrderBy(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string line in ReadLines(file.FullName))
                {
                    DateTime lineTimestamp;
                    if (game == TarkovGame.Arena
                        && ArenaMatchEndRegex.IsMatch(line)
                        && TryParseTimestamp(line, out lineTimestamp))
                    {
                        arenaMatchEndEvents.Add(new TimedLogEvent { Timestamp = lineTimestamp });
                    }
                    if (game == TarkovGame.Arena
                        && ArenaMatchingStartedRegex.IsMatch(line)
                        && TryParseTimestamp(line, out lineTimestamp))
                    {
                        arenaMatchingStarted = lineTimestamp;
                    }

                    Match richMatch = game == TarkovGame.Arena
                        ? ArenaServerRegex.Match(line)
                        : EftServerRegex.Match(line);
                    DateTime timestamp;
                    if (richMatch.Success
                        && TryParseTimestamp(line, out timestamp)
                        && IsValidIpv4(richMatch.Groups["ip"].Value))
                    {
                        ServerSession session = CreateRichSession(
                            directory,
                            file,
                            game,
                            richMatch,
                            line,
                            timestamp,
                            timings);
                        if (game == TarkovGame.Eft)
                            ApplyEftRaidClassification(session, sessionModes, TarkovHostingMode.Server);
                        if (game == TarkovGame.Arena && arenaMatchingStarted.HasValue)
                        {
                            double elapsed = (timestamp - arenaMatchingStarted.Value).TotalSeconds;
                            if (elapsed >= 0 && elapsed <= 3600) session.MatchmakingSeconds = elapsed;
                            arenaMatchingStarted = null;
                        }
                        ServerSession existing;
                        if (sessionsByKey.TryGetValue(session.SessionKey, out existing))
                        {
                            if (timestamp > existing.LastUpdated) existing.LastUpdated = timestamp;
                        }
                        else
                        {
                            sessionsByKey[session.SessionKey] = session;
                        }
                    }

                    if (game != TarkovGame.Eft) continue;
                    Match ipMatch = LegacyIpRegex.Match(line);
                    if (ipMatch.Success && IsValidIpv4(ipMatch.Groups["ip"].Value))
                    {
                        legacyIp = ipMatch.Groups["ip"].Value;
                        int.TryParse(ipMatch.Groups["port"].Value, out legacyPort);
                        DateTime parsed;
                        legacyTimestamp = TryParseTimestamp(line, out parsed) ? parsed : (DateTime?)null;
                        legacyVersion = GetVersion(line);
                        legacyFile = file.FullName;
                    }
                    Match mapMatch = LegacyMapRegex.Match(line);
                    if (mapMatch.Success) legacyMap = NormalizeMap(TarkovGame.Eft, mapMatch.Groups["map"].Value);
                }
            }

            var sessions = sessionsByKey.Values.OrderBy(item => item.DisplayDetectedAt).ToList();
            if (sessions.Count == 0 && !string.IsNullOrWhiteSpace(legacyIp))
            {
                DateTime started;
                try { started = directory.CreationTime; }
                catch { started = relevantFiles[0].CreationTime; }
                sessions.Add(new ServerSession
                {
                    Game = game,
                    SessionStarted = started,
                    LastUpdated = legacyTimestamp ?? relevantFiles.Max(file => file.LastWriteTime),
                    SessionFolderName = directory.Name,
                    SessionKey = game + "|" + directory.FullName,
                    LogFilePath = legacyFile,
                    IpAddress = legacyIp,
                    Port = legacyPort,
                    MapName = legacyMap,
                    ClientVersion = legacyVersion,
                    HostingMode = TarkovHostingMode.Server,
                    IpDetectedAt = legacyTimestamp
                });
                ApplyEftRaidClassification(
                    sessions[0],
                    sessionModes,
                    TarkovHostingMode.Server);
            }

            if (game == TarkovGame.Eft)
            {
                sessions.AddRange(BuildConfirmedLocalSessions(
                    directory,
                    timings,
                    sessionModes,
                    mapPresets,
                    gameCreatedEvents,
                    gameStartedEvents,
                    sessions,
                    relevantFiles.Where(file => file.Name.IndexOf(
                        "network-connection",
                        StringComparison.OrdinalIgnoreCase) >= 0)));
                ApplyRaidParticipantEvents(sessions, relevantFiles);
            }

            ApplyNetworkEvents(
                sessions,
                relevantFiles.Where(file => file.Name.IndexOf("network-connection", StringComparison.OrdinalIgnoreCase) >= 0));
            ApplyOperationTimes(
                sessions,
                game,
                gameStartedEvents,
                game == TarkovGame.Arena ? arenaMatchEndEvents : eftPostRaidEvents,
                relevantFiles);
            if (game == TarkovGame.Eft)
            {
                ApplyUserReportEvents(
                    sessions,
                    relevantFiles.Where(file => file.Name.IndexOf("backend", StringComparison.OrdinalIgnoreCase) >= 0));
            }
            return sessions.OrderByDescending(item => item.DisplayDetectedAt).ToList();
        }

        private static void ApplyOperationTimes(
            IList<ServerSession> sessions,
            TarkovGame game,
            IEnumerable<GameStartedEvent> gameStartedEvents,
            IEnumerable<TimedLogEvent> terminalEvents,
            IEnumerable<FileInfo> relevantFiles)
        {
            if (sessions == null || sessions.Count == 0) return;

            List<GameStartedEvent> starts = gameStartedEvents
                .OrderBy(item => item.Timestamp)
                .ToList();
            List<DateTime> terminals = terminalEvents
                .Select(item => item.Timestamp)
                .OrderBy(timestamp => timestamp)
                .ToList();
            List<ServerSession> ordered = sessions
                .Where(session => session != null)
                .OrderBy(session => session.DisplayDetectedAt)
                .ToList();
            DateTime now = DateTime.Now;

            for (int index = 0; index < ordered.Count; index++)
            {
                ServerSession session = ordered[index];
                session.OperationStartedAt = null;
                session.OperationEndedAt = null;
                session.OperationState = RaidOperationState.Unknown;
                session.RaidEntryMeasuredSeconds = null;

                DateTime assignment = session.DisplayDetectedAt;
                DateTime? nextAssignment = index + 1 < ordered.Count
                    ? (DateTime?)ordered[index + 1].DisplayDetectedAt
                    : null;
                GameStartedEvent startedEvent = starts
                    .Where(item => item.Timestamp >= assignment
                        && (item.Timestamp - assignment).TotalSeconds
                            <= ServerSession.MaximumRaidEntrySeconds
                        && (!nextAssignment.HasValue || item.Timestamp < nextAssignment.Value))
                    .FirstOrDefault();
                if (startedEvent == null) continue;

                DateTime started = startedEvent.Timestamp;
                session.OperationStartedAt = started;
                session.RaidEntryMeasuredSeconds = GetPreferredGameStartedSeconds(startedEvent);
                DateTime? nextGameStart = starts
                    .Where(item => item.Timestamp > started)
                    .Select(item => (DateTime?)item.Timestamp)
                    .FirstOrDefault();
                DateTime? endBoundary = MinTimestamp(nextAssignment, nextGameStart);

                DateTime? explicitTerminal = terminals
                    .Where(timestamp => timestamp > started
                        && (!endBoundary.HasValue || timestamp < endBoundary.Value))
                    .Select(timestamp => (DateTime?)timestamp)
                    .FirstOrDefault();

                // EFT's normal reason 0 disconnect is closer to the actual raid end than the
                // subsequent profile refresh. ApplyNetworkEvents retains only the final attempt,
                // so a reason 0 followed by a reconnect cannot prematurely end the operation.
                DateTime? networkTerminal = null;
                if (game == TarkovGame.Eft
                    && session.DisconnectReason == 0
                    && session.ConnectionEndedAt.HasValue
                    && session.ConnectionEndedAt.Value > started
                    && (!endBoundary.HasValue || session.ConnectionEndedAt.Value < endBoundary.Value))
                {
                    networkTerminal = session.ConnectionEndedAt;
                }

                DateTime? ended = MinTimestamp(networkTerminal, explicitTerminal);
                if (ended.HasValue)
                {
                    TimeSpan duration = ended.Value - started;
                    if (duration > TimeSpan.Zero && duration <= TimeSpan.FromHours(6))
                    {
                        session.OperationEndedAt = ended;
                        session.OperationState = RaidOperationState.Completed;
                    }
                    continue;
                }

                bool hasOutOfOrderTerminal = terminals.Any(timestamp =>
                    timestamp >= assignment
                    && timestamp <= started
                    && (!endBoundary.HasValue || timestamp < endBoundary.Value));
                bool isLastOperation = !nextAssignment.HasValue && !nextGameStart.HasValue;
                bool hasRecentOperationActivity = HasRecentLogActivity(
                    relevantFiles.Where(file => session.HasServerIp
                        ? file.Name.IndexOf("network-connection", StringComparison.OrdinalIgnoreCase) >= 0
                        : file.Name.IndexOf("application", StringComparison.OrdinalIgnoreCase) >= 0),
                    now);
                bool hasActiveConnectionEvidence = !session.HasServerIp
                    || (session.ConnectedOnce
                        && session.CurrentAttemptConnected
                        && !session.HasDisconnectRecord
                        && !session.TimedOut);
                TimeSpan elapsed = now - started;
                if (!hasOutOfOrderTerminal
                    && isLastOperation
                    && hasRecentOperationActivity
                    && hasActiveConnectionEvidence
                    && elapsed >= TimeSpan.Zero
                    && elapsed <= TimeSpan.FromHours(6))
                {
                    session.OperationState = RaidOperationState.InProgress;
                }
            }
        }

        private static DateTime? MinTimestamp(DateTime? first, DateTime? second)
        {
            if (!first.HasValue) return second;
            if (!second.HasValue) return first;
            return first.Value <= second.Value ? first : second;
        }

        private static double? ParseOptionalSeconds(Regex regex, string line)
        {
            Match match = regex.Match(line ?? string.Empty);
            double seconds;
            if (!match.Success
                || !double.TryParse(
                    match.Groups["seconds"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out seconds))
            {
                return null;
            }
            return seconds;
        }

        private static double? GetPreferredGameStartedSeconds(GameStartedEvent startedEvent)
        {
            if (startedEvent == null) return null;
            if (startedEvent.RealSeconds.HasValue
                && ServerSession.IsValidRaidEntrySeconds(startedEvent.RealSeconds.Value))
            {
                return startedEvent.RealSeconds;
            }
            if (startedEvent.ReportedSeconds.HasValue
                && ServerSession.IsValidRaidEntrySeconds(startedEvent.ReportedSeconds.Value))
            {
                return startedEvent.ReportedSeconds;
            }
            return null;
        }

        private static bool HasRecentLogActivity(IEnumerable<FileInfo> relevantFiles, DateTime now)
        {
            DateTime latestWriteUtc = DateTime.MinValue;
            foreach (FileInfo file in relevantFiles ?? Enumerable.Empty<FileInfo>())
            {
                try
                {
                    file.Refresh();
                    if (file.LastWriteTimeUtc > latestWriteUtc) latestWriteUtc = file.LastWriteTimeUtc;
                }
                catch
                {
                    // A rotating file cannot by itself prove that a historical raid is active.
                }
            }
            if (latestWriteUtc == DateTime.MinValue) return false;
            TimeSpan age = now.ToUniversalTime() - latestWriteUtc;
            return age >= TimeSpan.FromMinutes(-2) && age <= TimeSpan.FromMinutes(10);
        }

        private static ServerSession CreateRichSession(
            DirectoryInfo directory,
            FileInfo file,
            TarkovGame game,
            Match match,
            string sourceLine,
            DateTime timestamp,
            IList<MatchTiming> timings)
        {
            int port;
            int.TryParse(match.Groups["port"].Value, out port);
            string serverId = match.Groups["sid"].Value.Trim().Trim('\'', '"');
            string shortId = match.Groups["short"].Value.Trim().Trim('\'', '"');
            string stableId = !string.IsNullOrWhiteSpace(serverId)
                ? serverId
                : (!string.IsNullOrWhiteSpace(shortId) ? shortId : timestamp.Ticks.ToString(CultureInfo.InvariantCulture));
            MatchTiming timing = FindNearestTiming(timings, timestamp);
            return new ServerSession
            {
                Game = game,
                SessionStarted = timestamp,
                LastUpdated = timestamp,
                SessionFolderName = directory.Name,
                SessionKey = BuildSessionKey(game, serverId, shortId, timestamp, stableId),
                LogFilePath = file.FullName,
                IpAddress = match.Groups["ip"].Value,
                Port = port,
                MapName = NormalizeMap(game, match.Groups["map"].Value),
                GameMode = game == TarkovGame.Eft
                    ? match.Groups["raid"].Value.Trim().Trim('\'', '"')
                    : match.Groups["mode"].Value.Trim().Trim('\'', '"'),
                ServerId = serverId,
                ShortId = shortId,
                DataCenterCode = ExtractDataCenter(serverId),
                ClientVersion = GetVersion(sourceLine),
                MatchmakingSeconds = timing == null ? (double?)null : timing.Seconds,
                IpDetectedAt = timestamp
            };
        }

        private static TarkovProgressionMode ParseProgressionMode(string value)
        {
            if (string.Equals(value, "Regular", StringComparison.OrdinalIgnoreCase))
                return TarkovProgressionMode.Pvp;
            if (string.Equals(value, "Pve", StringComparison.OrdinalIgnoreCase))
                return TarkovProgressionMode.Pve;
            if (string.Equals(value, "PvpSeason", StringComparison.OrdinalIgnoreCase))
                return TarkovProgressionMode.PvpSeason;
            return TarkovProgressionMode.Unknown;
        }

        private static SessionModeEvent CreateSessionModeEvent(
            DateTime timestamp,
            Match modeMatch,
            string clientVersion)
        {
            TarkovProgressionMode mode = ParseProgressionMode(
                modeMatch.Groups["mode"].Value);
            var result = new SessionModeEvent
            {
                Timestamp = timestamp,
                Mode = mode
            };
            if (mode != TarkovProgressionMode.PvpSeason) return result;

            int parsedNumber;
            int? explicitNumber = modeMatch.Groups["season"].Success
                && int.TryParse(
                    modeMatch.Groups["season"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out parsedNumber)
                && PvpSeasonCatalog.IsValidNumber(parsedNumber)
                    ? parsedNumber
                    : (int?)null;
            PvpSeasonIdentity identity = PvpSeasonCatalog.Resolve(
                explicitNumber,
                clientVersion);
            if (identity == null) return result;
            result.PvpSeasonNumber = identity.Number;
            result.PvpSeasonKey = identity.Key;
            result.PvpSeasonName = identity.InternalName;
            result.PvpSeasonEvidence = identity.Evidence;
            return result;
        }

        private static SessionModeEvent FindSessionModeEvent(
            IEnumerable<SessionModeEvent> sessionModes,
            DateTime timestamp)
        {
            return sessionModes
                .Where(item => item.Timestamp <= timestamp)
                .OrderByDescending(item => item.Timestamp)
                .FirstOrDefault();
        }

        private static TarkovProgressionMode FindProgressionMode(
            IEnumerable<SessionModeEvent> sessionModes,
            DateTime timestamp)
        {
            SessionModeEvent mode = FindSessionModeEvent(sessionModes, timestamp);
            return mode == null ? TarkovProgressionMode.Unknown : mode.Mode;
        }

        private static void ApplyEftRaidClassification(
            ServerSession session,
            IEnumerable<SessionModeEvent> sessionModes,
            TarkovHostingMode hostingMode)
        {
            if (session == null) return;
            SessionModeEvent mode = FindSessionModeEvent(
                sessionModes,
                session.DisplayDetectedAt);
            session.ProgressionMode = mode == null
                ? TarkovProgressionMode.Unknown
                : mode.Mode;
            ApplyPvpSeasonIdentity(session, mode);
            session.HostingMode = hostingMode;
            session.RaidPurpose = session.ProgressionMode == TarkovProgressionMode.Unknown
                ? TarkovRaidPurpose.Unknown
                : TarkovRaidPurpose.Progression;
        }

        private static void ApplyPvpSeasonIdentity(
            ServerSession session,
            SessionModeEvent mode)
        {
            session.PvpSeasonNumber = null;
            session.PvpSeasonKey = null;
            session.PvpSeasonName = null;
            session.PvpSeasonEvidence = PvpSeasonEvidence.None;
            if (mode == null || mode.Mode != TarkovProgressionMode.PvpSeason) return;

            PvpSeasonIdentity identity = mode.PvpSeasonNumber.HasValue
                ? new PvpSeasonIdentity
                {
                    Number = mode.PvpSeasonNumber.Value,
                    Key = mode.PvpSeasonKey,
                    InternalName = mode.PvpSeasonName,
                    Evidence = mode.PvpSeasonEvidence
                }
                : PvpSeasonCatalog.Resolve(null, session.ClientVersion);
            if (identity == null) return;
            session.PvpSeasonNumber = identity.Number;
            session.PvpSeasonKey = identity.Key;
            session.PvpSeasonName = identity.InternalName;
            session.PvpSeasonEvidence = identity.Evidence;
        }

        private static IList<ServerSession> BuildConfirmedLocalSessions(
            DirectoryInfo directory,
            IEnumerable<MatchTiming> timings,
            IEnumerable<SessionModeEvent> sessionModes,
            IEnumerable<MapPresetEvent> mapPresets,
            IEnumerable<TimedLogEvent> gameCreatedEvents,
            IEnumerable<GameStartedEvent> gameStartedEvents,
            IEnumerable<ServerSession> serverSessions,
            IEnumerable<FileInfo> networkFiles)
        {
            var sessions = new List<ServerSession>();
            var usedGameCreated = new HashSet<long>();
            var usedGameStarted = new HashSet<long>();
            FileInfo[] orderedNetworkFiles = networkFiles
                .OrderBy(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (MatchTiming timing in timings
                .Where(item => item.IsExplicitZero)
                .OrderBy(item => item.Timestamp))
            {
                if (FindProgressionMode(sessionModes, timing.Timestamp) != TarkovProgressionMode.Pve)
                    continue;

                TimedLogEvent created = gameCreatedEvents
                    .Where(item => !usedGameCreated.Contains(item.Timestamp.Ticks)
                        && item.Timestamp >= timing.Timestamp
                        && item.Timestamp <= timing.Timestamp.AddMinutes(2))
                    .OrderBy(item => item.Timestamp)
                    .FirstOrDefault();
                if (created == null) continue;

                GameStartedEvent started = gameStartedEvents
                    .Where(item => !usedGameStarted.Contains(item.Timestamp.Ticks)
                        && item.Timestamp >= created.Timestamp
                        && item.Timestamp <= created.Timestamp.AddMinutes(3))
                    .OrderBy(item => item.Timestamp)
                    .FirstOrDefault();
                if (started == null) continue;
                if (FindProgressionMode(sessionModes, started.Timestamp) != TarkovProgressionMode.Pve)
                    continue;

                MapPresetEvent map = mapPresets
                    .Where(item => item.Timestamp >= timing.Timestamp.AddSeconds(-3)
                        && item.Timestamp <= created.Timestamp)
                    .OrderByDescending(item => item.Timestamp)
                    .FirstOrDefault();
                if (map == null || string.IsNullOrWhiteSpace(map.MapName)) continue;

                DateTime evidenceStart = timing.Timestamp.AddSeconds(-3);
                DateTime evidenceEnd = started.Timestamp.AddSeconds(5);
                bool hasServerAssignment = serverSessions.Any(session =>
                    session.HasServerIp
                    && session.DisplayDetectedAt >= evidenceStart
                    && session.DisplayDetectedAt <= evidenceEnd);
                if (hasServerAssignment
                    || HasNetworkConnectionInWindow(orderedNetworkFiles, evidenceStart, evidenceEnd))
                    continue;

                usedGameCreated.Add(created.Timestamp.Ticks);
                usedGameStarted.Add(started.Timestamp.Ticks);
                sessions.Add(new ServerSession
                {
                    Game = TarkovGame.Eft,
                    SessionStarted = timing.Timestamp,
                    LastUpdated = started.Timestamp,
                    SessionFolderName = directory.Name,
                    SessionKey = TarkovGame.Eft + "|local|"
                        + timing.Timestamp.Ticks.ToString(CultureInfo.InvariantCulture)
                        + "|" + map.MapName,
                    LogFilePath = timing.LogFilePath,
                    MapName = map.MapName,
                    ProgressionMode = TarkovProgressionMode.Pve,
                    HostingMode = TarkovHostingMode.Local,
                    RaidPurpose = TarkovRaidPurpose.Progression,
                    ClientVersion = timing.ClientVersion,
                    MatchmakingSeconds = timing.Seconds
                });
            }
            return sessions;
        }

        private static bool HasNetworkConnectionInWindow(
            IEnumerable<FileInfo> networkFiles,
            DateTime start,
            DateTime end)
        {
            foreach (FileInfo file in networkFiles)
            {
                foreach (string line in ReadLines(file.FullName))
                {
                    if (!Regex.IsMatch(line, @"(?:^|\|)Connect \(address:", RegexOptions.IgnoreCase)
                        && line.IndexOf(
                            "Enter to the 'Connected' state",
                            StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    DateTime timestamp;
                    if (TryParseTimestamp(line, out timestamp)
                        && timestamp >= start
                        && timestamp <= end)
                        return true;
                }
            }
            return false;
        }

        private static void ApplyRaidParticipantEvents(
            IList<ServerSession> sessions,
            IEnumerable<FileInfo> relevantFiles)
        {
            if (sessions == null || sessions.Count == 0) return;

            IList<ParticipantLogEvent> events = ReadParticipantLogEvents(relevantFiles);
            IList<ParticipantAssignment> assignments = InferParticipantAssignments(events);
            foreach (ParticipantAssignment assignment in assignments)
            {
                ServerSession session = sessions
                    .Where(item => item != null
                        && item.Game == TarkovGame.Eft
                        && Math.Abs((item.DisplayDetectedAt - assignment.Timestamp).TotalSeconds) <= 1)
                    .OrderBy(item => Math.Abs((item.DisplayDetectedAt - assignment.Timestamp).TotalMilliseconds))
                    .FirstOrDefault();
                if (session == null) continue;
                if (assignment.Mode != TarkovProgressionMode.Unknown
                    && session.ProgressionMode != TarkovProgressionMode.Unknown
                    && assignment.Mode != session.ProgressionMode)
                    continue;

                if (assignment.ProfileCharacter == TarkovCharacterType.Pmc
                    || assignment.ProfileCharacter == TarkovCharacterType.Scav)
                {
                    RaidClassificationModel.AddCharacterEvidence(
                        session,
                        assignment.ProfileCharacter,
                        RaidCharacterEvidence.ProfileIdRelation);
                }
                if (assignment.VisualPmc)
                {
                    RaidClassificationModel.AddCharacterEvidence(
                        session,
                        TarkovCharacterType.Pmc,
                        RaidCharacterEvidence.PlayerVisualSide);
                }
                if (assignment.VisualScav)
                {
                    RaidClassificationModel.AddCharacterEvidence(
                        session,
                        TarkovCharacterType.Scav,
                        RaidCharacterEvidence.PlayerVisualSide);
                }

                if (assignment.HasEmptyGroup)
                {
                    RaidClassificationModel.AddParticipationEvidence(
                        session,
                        RaidParticipationEvidence.EmptyGroupId);
                }
                if (assignment.HasMatchJoin)
                {
                    RaidClassificationModel.AddParticipationEvidence(
                        session,
                        RaidParticipationEvidence.MatchJoin);
                }
                if (assignment.HasPartyRoute)
                {
                    RaidClassificationModel.AddParticipationEvidence(
                        session,
                        RaidParticipationEvidence.GroupStartRoute);
                }
                if (assignment.HasPartyStart)
                {
                    RaidClassificationModel.AddParticipationEvidence(
                        session,
                        RaidParticipationEvidence.GroupStartEvent);
                }
                if (assignment.PartySize.HasValue)
                    RaidClassificationModel.AddPartySizeEvidence(session, assignment.PartySize);
            }
        }

        private static IList<ParticipantLogEvent> ReadParticipantLogEvents(
            IEnumerable<FileInfo> relevantFiles)
        {
            var events = new List<ParticipantLogEvent>();
            int order = 0;
            FileInfo[] files = (relevantFiles ?? Enumerable.Empty<FileInfo>()).ToArray();

            foreach (FileInfo file in files
                .Where(item => item.Name.IndexOf("application", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string line in ReadLines(file.FullName))
                {
                    DateTime timestamp;
                    if (!TryParseTimestamp(line, out timestamp)) continue;

                    Match modeMatch = SessionModeRegex.Match(line);
                    if (modeMatch.Success)
                    {
                        events.Add(new ParticipantLogEvent
                        {
                            Timestamp = timestamp,
                            Order = order++,
                            Kind = ParticipantEventKind.Mode,
                            Mode = ParseProgressionMode(modeMatch.Groups["mode"].Value)
                        });
                    }

                    Match prepare = PrepareProfileIdRegex.Match(line);
                    if (prepare.Success)
                    {
                        events.Add(new ParticipantLogEvent
                        {
                            Timestamp = timestamp,
                            Order = order++,
                            Kind = ParticipantEventKind.PrepareProfile,
                            ProfileId = prepare.Groups["id"].Value.ToLowerInvariant()
                        });
                    }

                    Match group = MatchingGroupIdRegex.Match(line);
                    if (group.Success)
                    {
                        string value = group.Groups["id"].Value.Trim().Trim('\'', '"');
                        events.Add(new ParticipantLogEvent
                        {
                            Timestamp = timestamp,
                            Order = order++,
                            Kind = string.IsNullOrWhiteSpace(value)
                                ? ParticipantEventKind.EmptyGroup
                                : ParticipantEventKind.NonEmptyGroup
                        });
                    }

                    Match assignment = AssignmentProfileIdRegex.Match(line);
                    if (assignment.Success)
                    {
                        events.Add(new ParticipantLogEvent
                        {
                            Timestamp = timestamp,
                            Order = order++,
                            Kind = ParticipantEventKind.Assignment,
                            ProfileId = assignment.Groups["id"].Value.ToLowerInvariant()
                        });
                    }

                    if (line.IndexOf("Network game matching cancelled.", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("Network game matching aborted.", StringComparison.OrdinalIgnoreCase) >= 0
                        || line.IndexOf("Local game matching cancelled.", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        events.Add(new ParticipantLogEvent
                        {
                            Timestamp = timestamp,
                            Order = order++,
                            Kind = ParticipantEventKind.MatchingCancelled
                        });
                    }
                    if (GameStartedRegex.IsMatch(line))
                    {
                        events.Add(new ParticipantLogEvent
                        {
                            Timestamp = timestamp,
                            Order = order++,
                            Kind = ParticipantEventKind.GameStarted
                        });
                    }
                }
            }

            foreach (FileInfo file in files
                .Where(item => item.Name.IndexOf("backend", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string line in ReadLines(file.FullName))
                {
                    if (line.IndexOf("---> Request HTTPS", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    ParticipantEventKind kind;
                    if (line.IndexOf("/client/match/group/start_game", StringComparison.OrdinalIgnoreCase) >= 0)
                        kind = ParticipantEventKind.GroupStartRoute;
                    else if (line.IndexOf("/client/match/join", StringComparison.OrdinalIgnoreCase) >= 0)
                        kind = ParticipantEventKind.MatchJoin;
                    else
                        continue;
                    DateTime timestamp;
                    if (!TryParseTimestamp(line, out timestamp)) continue;
                    events.Add(new ParticipantLogEvent
                    {
                        Timestamp = timestamp,
                        Order = order++,
                        Kind = kind
                    });
                }
            }

            foreach (FileInfo file in files
                .Where(item => item.Name.IndexOf("push-notifications", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                ReadPushParticipantEvents(file, events, ref order);
            }

            return events
                .OrderBy(item => item.Timestamp)
                .ThenBy(item => GetParticipantEventPriority(item.Kind))
                .ThenBy(item => item.Order)
                .ToList();
        }

        private static int GetParticipantEventPriority(ParticipantEventKind kind)
        {
            if (kind == ParticipantEventKind.Mode) return 0;
            if (kind == ParticipantEventKind.PrepareProfile) return 1;
            if (kind == ParticipantEventKind.GroupInviteAccept
                || kind == ParticipantEventKind.GroupReady
                || kind == ParticipantEventKind.GroupNotReady
                || kind == ParticipantEventKind.GroupMemberRemoved
                || kind == ParticipantEventKind.GroupRemoved)
                return 2;
            if (kind == ParticipantEventKind.EmptyGroup
                || kind == ParticipantEventKind.NonEmptyGroup)
                return 3;
            if (kind == ParticipantEventKind.MatchJoin
                || kind == ParticipantEventKind.GroupStartRoute
                || kind == ParticipantEventKind.GroupStart)
                return 4;
            if (kind == ParticipantEventKind.Assignment) return 8;
            if (kind == ParticipantEventKind.MatchingCancelled) return 9;
            if (kind == ParticipantEventKind.GameStarted) return 10;
            return 5;
        }

        private static IList<ParticipantAssignment> InferParticipantAssignments(
            IEnumerable<ParticipantLogEvent> source)
        {
            var results = new List<ParticipantAssignment>();
            var members = new Dictionary<string, GroupMemberState>(StringComparer.Ordinal);
            ParticipantGeneration generation = null;
            TarkovProgressionMode currentMode = TarkovProgressionMode.Unknown;
            string pendingProfileId = null;
            DateTime pendingProfileAt = DateTime.MinValue;
            TarkovProgressionMode pendingProfileMode = TarkovProgressionMode.Unknown;

            foreach (ParticipantLogEvent item in source ?? Enumerable.Empty<ParticipantLogEvent>())
            {
                if (item.Kind == ParticipantEventKind.Mode)
                {
                    currentMode = item.Mode;
                    generation = null;
                    members.Clear();
                    pendingProfileId = null;
                    pendingProfileAt = DateTime.MinValue;
                    pendingProfileMode = TarkovProgressionMode.Unknown;
                    continue;
                }
                if (item.Kind == ParticipantEventKind.PrepareProfile)
                {
                    pendingProfileId = item.ProfileId;
                    pendingProfileAt = item.Timestamp;
                    pendingProfileMode = currentMode;
                    if (generation != null && generation.Mode == currentMode)
                        generation.BaseProfileId = pendingProfileId;
                    continue;
                }
                if (item.Kind == ParticipantEventKind.MatchingCancelled
                    || item.Kind == ParticipantEventKind.GroupRemoved
                    || item.Kind == ParticipantEventKind.GameStarted)
                {
                    generation = null;
                    members.Clear();
                    pendingProfileId = null;
                    pendingProfileAt = DateTime.MinValue;
                    pendingProfileMode = TarkovProgressionMode.Unknown;
                    continue;
                }

                if (item.Kind == ParticipantEventKind.GroupInviteAccept
                    || item.Kind == ParticipantEventKind.GroupReady
                    || item.Kind == ParticipantEventKind.GroupNotReady)
                {
                    if (string.IsNullOrWhiteSpace(item.MemberId)) continue;
                    GroupMemberState member;
                    if (!members.TryGetValue(item.MemberId, out member))
                    {
                        member = new GroupMemberState();
                        members[item.MemberId] = member;
                    }
                    if (item.Ready.HasValue) member.Ready = item.Ready;
                    if (item.VisualCharacter == TarkovCharacterType.Pmc
                        || item.VisualCharacter == TarkovCharacterType.Scav)
                        member.VisualCharacter = item.VisualCharacter;
                    continue;
                }
                if (item.Kind == ParticipantEventKind.GroupMemberRemoved)
                {
                    if (!string.IsNullOrWhiteSpace(item.MemberId)) members.Remove(item.MemberId);
                    continue;
                }

                if (item.Kind == ParticipantEventKind.EmptyGroup
                    || item.Kind == ParticipantEventKind.NonEmptyGroup)
                {
                    bool replace = generation == null
                        || generation.HasApplicationMatch
                        || item.Timestamp - generation.StartedAt > TimeSpan.FromMinutes(2);
                    if (replace)
                    {
                        generation = CreateParticipantGeneration(
                            item.Timestamp,
                            currentMode,
                            pendingProfileId,
                            pendingProfileAt,
                            pendingProfileMode);
                    }
                    generation.HasApplicationMatch = true;
                    if (item.Kind == ParticipantEventKind.EmptyGroup)
                        generation.HasEmptyGroup = true;
                    else
                        generation.HasPartyStart = true;
                    continue;
                }

                if (item.Kind == ParticipantEventKind.MatchJoin
                    || item.Kind == ParticipantEventKind.GroupStartRoute
                    || item.Kind == ParticipantEventKind.GroupStart)
                {
                    if (generation == null
                        || item.Timestamp - generation.StartedAt > TimeSpan.FromMinutes(2))
                    {
                        generation = CreateParticipantGeneration(
                            item.Timestamp,
                            currentMode,
                            pendingProfileId,
                            pendingProfileAt,
                            pendingProfileMode);
                    }
                    if (item.Kind == ParticipantEventKind.MatchJoin)
                        generation.HasMatchJoin = true;
                    else if (item.Kind == ParticipantEventKind.GroupStartRoute)
                    {
                        generation.HasPartyRoute = true;
                    }
                    else
                    {
                        generation.HasPartyStart = true;
                        CaptureFinalGroupState(generation, members);
                        members.Clear();
                    }
                    continue;
                }

                if (item.Kind != ParticipantEventKind.Assignment) continue;
                if (generation == null)
                {
                    generation = CreateParticipantGeneration(
                        item.Timestamp,
                        currentMode,
                        pendingProfileId,
                        pendingProfileAt,
                        pendingProfileMode);
                }

                results.Add(new ParticipantAssignment
                {
                    Timestamp = item.Timestamp,
                    Mode = generation.Mode,
                    ProfileCharacter = CompareProfileIds(
                        generation.BaseProfileId,
                        item.ProfileId),
                    HasEmptyGroup = generation.HasEmptyGroup,
                    HasMatchJoin = generation.HasMatchJoin,
                    HasPartyRoute = generation.HasPartyRoute,
                    HasPartyStart = generation.HasPartyStart,
                    PartySize = generation.PartySize,
                    VisualPmc = generation.VisualPmc,
                    VisualScav = generation.VisualScav
                });
                generation = null;
                members.Clear();
                pendingProfileId = null;
                pendingProfileAt = DateTime.MinValue;
                pendingProfileMode = TarkovProgressionMode.Unknown;
            }
            return results;
        }

        private static ParticipantGeneration CreateParticipantGeneration(
            DateTime timestamp,
            TarkovProgressionMode mode,
            string pendingProfileId,
            DateTime pendingProfileAt,
            TarkovProgressionMode pendingProfileMode)
        {
            bool profileIsCurrent = !string.IsNullOrWhiteSpace(pendingProfileId)
                && pendingProfileAt <= timestamp
                && timestamp - pendingProfileAt <= TimeSpan.FromMinutes(30)
                && pendingProfileMode == mode;
            return new ParticipantGeneration
            {
                StartedAt = timestamp,
                Mode = mode,
                BaseProfileId = profileIsCurrent ? pendingProfileId : null
            };
        }

        private static void CaptureFinalGroupState(
            ParticipantGeneration generation,
            IDictionary<string, GroupMemberState> members)
        {
            if (generation == null || members == null) return;
            generation.PartySize = null;
            generation.VisualPmc = false;
            generation.VisualScav = false;
            if (members.Count < 1 || members.Count > 4) return;
            if (members.Values.Any(item => !item.Ready.HasValue || !item.Ready.Value)) return;

            generation.PartySize = members.Count + 1;
            bool everyVisualKnown = members.Values.All(item =>
                item.VisualCharacter == TarkovCharacterType.Pmc
                || item.VisualCharacter == TarkovCharacterType.Scav);
            if (!everyVisualKnown) return;
            generation.VisualPmc = members.Values.Any(item =>
                item.VisualCharacter == TarkovCharacterType.Pmc);
            generation.VisualScav = members.Values.Any(item =>
                item.VisualCharacter == TarkovCharacterType.Scav);
        }

        internal static TarkovCharacterType CompareProfileIds(
            string baseProfileId,
            string raidProfileId)
        {
            if (!IsHexObjectId(baseProfileId) || !IsHexObjectId(raidProfileId))
                return TarkovCharacterType.Unknown;
            if (string.Equals(baseProfileId, raidProfileId, StringComparison.OrdinalIgnoreCase))
                return TarkovCharacterType.Pmc;

            char[] incremented = baseProfileId.ToLowerInvariant().ToCharArray();
            int carry = 1;
            for (int index = incremented.Length - 1; index >= 0 && carry != 0; index--)
            {
                int value = HexValue(incremented[index]) + carry;
                incremented[index] = HexDigit(value & 15);
                carry = value >> 4;
            }
            if (carry != 0) return TarkovCharacterType.Unknown;
            return string.Equals(
                new string(incremented),
                raidProfileId,
                StringComparison.OrdinalIgnoreCase)
                    ? TarkovCharacterType.Scav
                    : TarkovCharacterType.Unknown;
        }

        private static bool IsHexObjectId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 24) return false;
            return value.All(character => HexValue(character) >= 0);
        }

        private static int HexValue(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }

        private static char HexDigit(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }

        private static void ReadPushParticipantEvents(
            FileInfo file,
            ICollection<ParticipantLogEvent> events,
            ref int order)
        {
            if (file == null || events == null) return;
            try
            {
                file.Refresh();
                if (file.Length < 0 || file.Length > MaximumPushNotificationFileBytes) return;
            }
            catch
            {
                return;
            }

            var seenEventIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using (var stream = new FileStream(
                    file.FullName,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length > MaximumPushNotificationLineChars) continue;
                        Match marker = PushNotificationMarkerRegex.Match(line);
                        DateTime timestamp;
                        if (!marker.Success || !TryParseTimestamp(line, out timestamp)) continue;
                        string json;
                        if (!TryReadPushJsonObject(reader, out json)) continue;
                        ParticipantLogEvent parsed;
                        string eventId;
                        if (!TryParsePushParticipantEvent(
                            marker.Groups["type"].Value,
                            timestamp,
                            json,
                            out parsed,
                            out eventId))
                            continue;
                        if (!seenEventIds.Add(eventId)) continue;
                        parsed.Order = order++;
                        events.Add(parsed);
                    }
                }
            }
            catch
            {
                // A rotating or malformed push log cannot invalidate the normal raid scan.
            }
        }

        private static bool TryReadPushJsonObject(StreamReader reader, out string json)
        {
            json = null;
            if (reader == null) return false;
            var builder = new StringBuilder();
            int depth = 0;
            int lines = 0;
            bool started = false;
            bool inString = false;
            bool escaped = false;
            bool rejected = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lines++;
                if (lines > MaximumPushNotificationEventLines
                    || line.Length > MaximumPushNotificationLineChars)
                    rejected = true;
                if (!started && string.IsNullOrWhiteSpace(line) && lines <= 3) continue;
                if (!started && line.Trim() != "{") return false;

                if (!rejected)
                {
                    if (builder.Length + line.Length + 1 > MaximumPushNotificationEventChars)
                        rejected = true;
                    else
                        builder.AppendLine(line);
                }

                foreach (char character in line)
                {
                    if (inString)
                    {
                        if (escaped)
                            escaped = false;
                        else if (character == '\\')
                            escaped = true;
                        else if (character == '"')
                            inString = false;
                        continue;
                    }
                    if (character == '"')
                    {
                        inString = true;
                    }
                    else if (character == '{')
                    {
                        depth++;
                        started = true;
                    }
                    else if (character == '}')
                    {
                        depth--;
                        if (depth < 0) return false;
                    }
                }
                if (started && depth == 0)
                {
                    if (rejected || inString) return false;
                    json = builder.ToString();
                    return true;
                }
            }
            return false;
        }

        private static bool TryParsePushParticipantEvent(
            string markerType,
            DateTime timestamp,
            string json,
            out ParticipantLogEvent result,
            out string eventId)
        {
            result = null;
            eventId = null;
            if (string.IsNullOrWhiteSpace(markerType)
                || string.IsNullOrWhiteSpace(json)
                || json.Length > MaximumPushNotificationEventChars)
                return false;

            ParticipantEventKind mappedKind;
            if (!TryMapPushParticipantType(markerType, out mappedKind))
                return false;

            IDictionary<string, object> root;
            try
            {
                var serializer = new JavaScriptSerializer
                {
                    MaxJsonLength = MaximumPushNotificationEventChars,
                    RecursionLimit = 64
                };
                root = serializer.DeserializeObject(json) as IDictionary<string, object>;
            }
            catch
            {
                return false;
            }
                if (root == null) return false;
            string payloadType = GetJsonString(root, "type");
            if (!string.Equals(markerType, payloadType, StringComparison.OrdinalIgnoreCase))
                return false;
            eventId = GetOpaqueJsonId(root, "eventId");
            if (string.IsNullOrWhiteSpace(eventId)) return false;

            var parsed = new ParticipantLogEvent { Timestamp = timestamp };
            if (mappedKind == ParticipantEventKind.GroupInviteAccept)
            {
                bool? isReady;
                bool parsedIsReady;
                if (!TryParseOptionalBoolean(root, "isReady", out isReady, out parsedIsReady))
                    return false;

                parsed.Kind = mappedKind;
                parsed.MemberId = GetOpaqueJsonId(root, "aid");
                parsed.Ready = isReady;
                if (string.IsNullOrWhiteSpace(parsed.MemberId)) return false;
            }
            else if (mappedKind == ParticipantEventKind.GroupReady)
            {
                IDictionary<string, object> profile = GetJsonObject(root, "extendedProfile");
                IDictionary<string, object> visual = GetJsonObject(profile, "PlayerVisualRepresentation");
                IDictionary<string, object> info = GetJsonObject(visual, "Info");
                parsed.Kind = mappedKind;
                parsed.MemberId = GetOpaqueJsonId(profile, "aid");
                parsed.Ready = true;
                parsed.VisualCharacter = ParseVisualSide(GetJsonString(info, "Side"));
                if (string.IsNullOrWhiteSpace(parsed.MemberId)) return false;
            }
            else if (mappedKind == ParticipantEventKind.GroupNotReady)
            {
                parsed.Kind = mappedKind;
                parsed.MemberId = GetOpaqueJsonId(root, "aid");
                parsed.Ready = false;
                if (string.IsNullOrWhiteSpace(parsed.MemberId)) return false;
            }
            else if (mappedKind == ParticipantEventKind.GroupMemberRemoved)
            {
                parsed.Kind = mappedKind;
                parsed.MemberId = GetOpaqueJsonId(root, "aid");
                if (string.IsNullOrWhiteSpace(parsed.MemberId)) return false;
            }
            else if (mappedKind == ParticipantEventKind.GroupRemoved)
            {
                parsed.Kind = mappedKind;
            }
            else if (mappedKind == ParticipantEventKind.GroupStart)
            {
                parsed.Kind = mappedKind;
            }
            else
            {
                return false;
            }
            result = parsed;
            return true;
        }

        private static bool TryMapPushParticipantType(
            string markerType,
            out ParticipantEventKind kind)
        {
            kind = ParticipantEventKind.Mode;
            if (string.IsNullOrWhiteSpace(markerType)) return false;
            if (string.Equals(markerType, "GroupMatchInviteAccept", StringComparison.OrdinalIgnoreCase))
            {
                kind = ParticipantEventKind.GroupInviteAccept;
                return true;
            }
            if (string.Equals(markerType, "GroupMatchRaidReady", StringComparison.OrdinalIgnoreCase))
            {
                kind = ParticipantEventKind.GroupReady;
                return true;
            }
            if (string.Equals(markerType, "GroupMatchRaidNotReady", StringComparison.OrdinalIgnoreCase))
            {
                kind = ParticipantEventKind.GroupNotReady;
                return true;
            }
            if (string.Equals(markerType, "GroupMatchUserLeave", StringComparison.OrdinalIgnoreCase)
                || string.Equals(markerType, "GroupMatchInviteDecline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(markerType, "GroupMatchInviteExpired", StringComparison.OrdinalIgnoreCase))
            {
                kind = ParticipantEventKind.GroupMemberRemoved;
                return true;
            }
            if (string.Equals(markerType, "GroupMatchWasRemoved", StringComparison.OrdinalIgnoreCase))
            {
                kind = ParticipantEventKind.GroupRemoved;
                return true;
            }
            if (string.Equals(markerType, "GroupMatchStartGame", StringComparison.OrdinalIgnoreCase))
            {
                kind = ParticipantEventKind.GroupStart;
                return true;
            }
            return false;
        }

        private static bool TryParseOptionalBoolean(
            IDictionary<string, object> source,
            string name,
            out bool? value,
            out bool parsed)
        {
            value = null;
            parsed = false;
            if (source == null || string.IsNullOrWhiteSpace(name)) return true;
            object raw;
            if (!source.TryGetValue(name, out raw))
                return true;
            parsed = true;
            if (!(raw is bool)) return false;
            value = (bool)raw;
            return true;
        }

        private static IDictionary<string, object> GetJsonObject(
            IDictionary<string, object> source,
            string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name)) return null;
            object value;
            return source.TryGetValue(name, out value)
                ? value as IDictionary<string, object>
                : null;
        }

        private static string GetJsonString(IDictionary<string, object> source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name)) return null;
            object value;
            if (!source.TryGetValue(name, out value) || value == null) return null;
            return value as string;
        }

        private static string GetOpaqueJsonId(IDictionary<string, object> source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name)) return null;
            object value;
            if (!source.TryGetValue(name, out value) || value == null) return null;
            string result = value as string;
            if (result == null || result.Length > MaximumOpaqueIdentifierChars) return null;
            if (HasInvalidOpaqueIdCharacter(result)) return null;
            return result;
        }

        private static bool? GetJsonBoolean(IDictionary<string, object> source, string name)
        {
            if (source == null || string.IsNullOrWhiteSpace(name)) return null;
            object value;
            if (!source.TryGetValue(name, out value) || !(value is bool)) return null;
            return (bool)value;
        }

        private static TarkovCharacterType ParseVisualSide(string side)
        {
            if (string.Equals(side, "Savage", StringComparison.OrdinalIgnoreCase))
                return TarkovCharacterType.Scav;
            if (string.Equals(side, "Usec", StringComparison.OrdinalIgnoreCase)
                || string.Equals(side, "Bear", StringComparison.OrdinalIgnoreCase))
                return TarkovCharacterType.Pmc;
            return TarkovCharacterType.Unknown;
        }

        private static bool HasInvalidOpaqueIdCharacter(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsWhiteSpace(character) || char.IsControl(character)) return true;
            }
            return false;
        }

        private static void ApplyUserReportEvents(
            IList<ServerSession> sessions,
            IEnumerable<FileInfo> backendFiles)
        {
            if (sessions == null || sessions.Count == 0) return;
            var requests = new Dictionary<string, UserReportRequest>(StringComparer.OrdinalIgnoreCase);

            foreach (FileInfo file in backendFiles
                .OrderBy(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string line in ReadLines(file.FullName))
                {
                    if (!UserReportRouteRegex.IsMatch(line)) continue;
                    Match idMatch = BackendRequestIdRegex.Match(line);
                    DateTime timestamp;
                    if (!idMatch.Success || !TryParseTimestamp(line, out timestamp)) continue;

                    string requestId = idMatch.Groups["id"].Value.Trim();
                    UserReportRequest request;
                    if (!requests.TryGetValue(requestId, out request))
                    {
                        request = new UserReportRequest { RequestId = requestId };
                        requests[requestId] = request;
                    }

                    if (line.IndexOf("---> Request HTTPS", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!request.RequestedAt.HasValue || timestamp < request.RequestedAt.Value)
                            request.RequestedAt = timestamp;
                    }
                    else if (line.IndexOf("<--- Response HTTPS", StringComparison.OrdinalIgnoreCase) >= 0
                        && IsSuccessfulUserReportResponse(line))
                    {
                        if (!request.SuccessfulResponseAt.HasValue
                            || timestamp < request.SuccessfulResponseAt.Value)
                            request.SuccessfulResponseAt = timestamp;
                    }
                }
            }

            foreach (UserReportRequest request in requests.Values)
            {
                if (!request.RequestedAt.HasValue || !request.SuccessfulResponseAt.HasValue
                    || request.SuccessfulResponseAt.Value < request.RequestedAt.Value)
                    continue;

                DateTime reportedAt = request.RequestedAt.Value;
                ServerSession target = sessions
                    .Where(session => session.DisplayDetectedAt <= reportedAt
                        && reportedAt - session.DisplayDetectedAt <= TimeSpan.FromHours(6))
                    .OrderByDescending(session => session.DisplayDetectedAt)
                    .FirstOrDefault();
                if (target == null) continue;
                if (target.UserReportKeys == null)
                    target.UserReportKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string eventKey = reportedAt.Ticks.ToString(CultureInfo.InvariantCulture)
                    + "|" + request.SuccessfulResponseAt.Value.Ticks.ToString(CultureInfo.InvariantCulture);
                target.UserReportKeys.Add(eventKey);
                target.UserReportCount = target.UserReportKeys.Count;
            }
        }

        private static bool IsSuccessfulUserReportResponse(string line)
        {
            return line.IndexOf("|Info|backend|", StringComparison.OrdinalIgnoreCase) >= 0
                && line.IndexOf("|Error|", StringComparison.OrdinalIgnoreCase) < 0
                && line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) < 0
                && line.IndexOf("BackendServerSideException", StringComparison.OrdinalIgnoreCase) < 0
                && !Regex.IsMatch(line, @"\berror:\s*\d+", RegexOptions.IgnoreCase);
        }

        private static MatchTiming FindNearestTiming(IList<MatchTiming> timings, DateTime timestamp)
        {
            MatchTiming nearest = null;
            double nearestSeconds = double.MaxValue;
            foreach (MatchTiming timing in timings)
            {
                double signedDifference = (timestamp - timing.Timestamp).TotalSeconds;
                double difference = Math.Abs(signedDifference);
                if (signedDifference >= -3 && signedDifference <= 180 && difference < nearestSeconds)
                {
                    nearest = timing;
                    nearestSeconds = difference;
                }
            }
            return nearest;
        }

        private static void ApplyNetworkEvents(
            IList<ServerSession> sessions,
            IEnumerable<FileInfo> networkFiles)
        {
            foreach (FileInfo file in networkFiles
                .OrderBy(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                foreach (string line in ReadLines(file.FullName))
                {
                    DateTime timestamp;
                    if (!TryParseTimestamp(line, out timestamp)) continue;

                    Match stats = StatisticsRegex.Match(line);
                    Match endpoint = stats.Success ? stats : EndpointRegex.Match(line);
                    if (!endpoint.Success) continue;
                    int port;
                    if (!int.TryParse(endpoint.Groups["port"].Value, out port)) continue;
                    bool isConnect = Regex.IsMatch(line, @"(?:^|\|)Connect \(address:", RegexOptions.IgnoreCase);
                    ServerSession session = FindSessionForEvent(
                        sessions,
                        endpoint.Groups["ip"].Value,
                        port,
                        timestamp,
                        isConnect);
                    if (session == null) continue;
                    if (timestamp > session.LastUpdated) session.LastUpdated = timestamp;

                    if (isConnect)
                    {
                        if (session.ConnectionAttemptKeys == null)
                            session.ConnectionAttemptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        string eventKey = timestamp.Ticks.ToString(CultureInfo.InvariantCulture)
                            + "|" + endpoint.Groups["ip"].Value + ":" + port;
                        if (session.ConnectionAttemptKeys.Add(eventKey))
                        {
                            if (session.ConnectionAttempts > 0)
                            {
                                session.TimedOut = false;
                                session.HasDisconnectRecord = false;
                                session.DisconnectReason = null;
                                session.ConnectionEndedAt = null;
                            }
                            session.CurrentAttemptConnected = false;
                            session.ConnectionAttempts = session.ConnectionAttemptKeys.Count;
                        }
                    }
                    if (line.IndexOf("Enter to the 'Connected' state", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        session.ConnectedOnce = true;
                        session.CurrentAttemptConnected = true;
                    }
                    if (Regex.IsMatch(line, @"(?:^|\|)Disconnect \(address:", RegexOptions.IgnoreCase))
                    {
                        session.HasDisconnectRecord = true;
                        session.ConnectionEndedAt = timestamp;
                    }
                    if (line.IndexOf("Receive disconnect (address:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        session.HasDisconnectRecord = true;
                        session.ConnectionEndedAt = timestamp;
                    }
                    if (line.IndexOf("Timeout:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        session.TimedOut = true;
                        session.ConnectionEndedAt = timestamp;
                    }

                    Match reason = DisconnectedReasonRegex.Match(line);
                    int reasonValue;
                    if (reason.Success && int.TryParse(reason.Groups["reason"].Value, out reasonValue))
                    {
                        session.HasDisconnectRecord = true;
                        session.DisconnectReason = reasonValue;
                        if (reasonValue == 2) session.TimedOut = true;
                        session.ConnectionEndedAt = timestamp;
                    }

                    if (stats.Success)
                    {
                        double rtt;
                        double loss;
                        long sent;
                        long received;
                        long.TryParse(stats.Groups["received"].Value, out received);
                        if (double.TryParse(stats.Groups["rtt"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out rtt)
                            && rtt > 0
                            && received > 0)
                            session.ActualRttMs = rtt;
                        if (double.TryParse(stats.Groups["lose"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out loss))
                            session.NetworkLoss = Math.Max(0, loss);
                        if (long.TryParse(stats.Groups["sent"].Value, out sent)) session.NetworkSent = sent;
                        session.NetworkReceived = received;
                    }
                }
            }
        }

        private static ServerSession FindSessionForEvent(
            IEnumerable<ServerSession> sessions,
            string ipAddress,
            int port,
            DateTime timestamp,
            bool allowShortFutureWindow)
        {
            DateTime latestAllowed = allowShortFutureWindow ? timestamp.AddSeconds(1) : timestamp;
            return sessions
                .Where(session => string.Equals(session.IpAddress, ipAddress, StringComparison.OrdinalIgnoreCase)
                    && (session.Port <= 0 || session.Port == port)
                    && session.DisplayDetectedAt <= latestAllowed)
                .OrderByDescending(session => session.DisplayDetectedAt)
                .FirstOrDefault();
        }

        private static FileInfo[] GetRelevantFiles(DirectoryInfo directory, TarkovGame game)
        {
            Exception lastException = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return directory.GetFiles("*.log", SearchOption.TopDirectoryOnly)
                        .Where(file => file.Name.IndexOf("application", StringComparison.OrdinalIgnoreCase) >= 0
                            || file.Name.IndexOf("network-connection", StringComparison.OrdinalIgnoreCase) >= 0
                            || (game == TarkovGame.Eft
                                && file.Name.IndexOf("backend", StringComparison.OrdinalIgnoreCase) >= 0)
                            || (game == TarkovGame.Eft
                                && file.Name.IndexOf("push-notifications", StringComparison.OrdinalIgnoreCase) >= 0)
                            || (game == TarkovGame.Arena
                                && file.Name.IndexOf("lifecycle", StringComparison.OrdinalIgnoreCase) >= 0))
                        .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }
            throw new IOException("로그 파일 목록을 읽지 못했습니다.", lastException);
        }

        private static IEnumerable<string> ReadLines(string path)
        {
            Exception lastException = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var lines = new List<string>();
                try
                {
                    using (var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) lines.Add(line);
                    }
                    return lines;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }
            }
            throw new IOException("로그 파일을 읽지 못했습니다.", lastException);
        }

        private static string GetVersion(string line)
        {
            Match match = VersionRegex.Match(line ?? string.Empty);
            return match.Success ? match.Groups["version"].Value : null;
        }

        private static bool TryParseTimestamp(string line, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;
            Match match = TimestampRegex.Match(line ?? string.Empty);
            if (!match.Success) return false;
            string normalized = match.Groups["date"].Value.Replace('.', '-')
                + " " + match.Groups["time"].Value
                + match.Groups["fraction"].Value;
            return DateTime.TryParseExact(
                normalized,
                match.Groups["fraction"].Success
                    ? "yyyy-MM-dd HH:mm:ss.FFFFFFF"
                    : "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out timestamp);
        }

        private static bool IsValidIpv4(string value)
        {
            IPAddress address;
            return IPAddress.TryParse(value, out address)
                && address.AddressFamily == AddressFamily.InterNetwork;
        }

        private static string NormalizeMap(TarkovGame game, string rawValue)
        {
            string raw = (rawValue ?? string.Empty).Trim().Trim('\'', '"');
            if (game == TarkovGame.Eft)
            {
                string mapped;
                return EftMapNames.TryGetValue(raw, out mapped) ? mapped : raw.Replace('_', ' ');
            }
            if (raw.StartsWith("Arena_", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(6);
            return raw.Replace('_', ' ');
        }

        private static string ExtractDataCenter(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId)) return null;
            string prefix = serverId.Split('_')[0];
            Match match = DataCenterRegex.Match(prefix);
            return match.Success ? match.Groups["dc"].Value.ToUpperInvariant() : prefix;
        }

        private static string BuildSessionKey(
            TarkovGame game,
            string serverId,
            string shortId,
            DateTime timestamp,
            string fallback)
        {
            if (!string.IsNullOrWhiteSpace(serverId)) return game + "|sid|" + serverId.Trim();
            if (!string.IsNullOrWhiteSpace(shortId))
                return game + "|short|" + timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "|" + shortId.Trim();
            return game + "|fallback|" + timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "|" + fallback;
        }

        private static IEnumerable<ServerSession> MergeDuplicateSessions(IEnumerable<ServerSession> source)
        {
            var merged = new Dictionary<string, ServerSession>(StringComparer.OrdinalIgnoreCase);
            foreach (ServerSession candidate in source.Where(item => item != null))
            {
                string key = string.IsNullOrWhiteSpace(candidate.SessionKey)
                    ? candidate.Game + "|" + candidate.LogFilePath + "|" + candidate.DisplayDetectedAt.Ticks
                    : candidate.SessionKey;
                ServerSession existing;
                if (!merged.TryGetValue(key, out existing))
                {
                    merged[key] = CloneSession(candidate);
                    continue;
                }

                bool candidateIsStrictlyLater = candidate.LastUpdated > existing.LastUpdated;
                bool candidateIsLater = candidate.LastUpdated >= existing.LastUpdated;
                if (candidate.DisplayDetectedAt < existing.DisplayDetectedAt)
                {
                    existing.SessionStarted = candidate.SessionStarted;
                    existing.IpDetectedAt = candidate.IpDetectedAt;
                }
                if (candidate.LastUpdated > existing.LastUpdated) existing.LastUpdated = candidate.LastUpdated;
                if (existing.ConnectionAttemptKeys != null || candidate.ConnectionAttemptKeys != null)
                {
                    if (existing.ConnectionAttemptKeys == null)
                        existing.ConnectionAttemptKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (candidate.ConnectionAttemptKeys != null)
                        existing.ConnectionAttemptKeys.UnionWith(candidate.ConnectionAttemptKeys);
                    existing.ConnectionAttempts = existing.ConnectionAttemptKeys.Count;
                }
                else
                {
                    existing.ConnectionAttempts = Math.Max(existing.ConnectionAttempts, candidate.ConnectionAttempts);
                }
                existing.ConnectedOnce = existing.ConnectedOnce || candidate.ConnectedOnce;
                if (existing.UserReportKeys != null || candidate.UserReportKeys != null)
                {
                    if (existing.UserReportKeys == null)
                        existing.UserReportKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (candidate.UserReportKeys != null)
                        existing.UserReportKeys.UnionWith(candidate.UserReportKeys);
                    existing.UserReportCount = existing.UserReportKeys.Count;
                }
                else
                {
                    existing.UserReportCount = Math.Max(
                        existing.UserReportCount,
                        candidate.UserReportCount);
                }
                if (existing.ProgressionMode == TarkovProgressionMode.Unknown
                    && candidate.ProgressionMode != TarkovProgressionMode.Unknown)
                    existing.ProgressionMode = candidate.ProgressionMode;
                if (existing.ProgressionMode == TarkovProgressionMode.PvpSeason
                    && candidate.ProgressionMode == TarkovProgressionMode.PvpSeason
                    && PvpSeasonCatalog.IsValidNumber(candidate.PvpSeasonNumber)
                    && (!PvpSeasonCatalog.IsValidNumber(existing.PvpSeasonNumber)
                        || candidate.PvpSeasonEvidence > existing.PvpSeasonEvidence))
                {
                    existing.PvpSeasonNumber = candidate.PvpSeasonNumber;
                    existing.PvpSeasonKey = candidate.PvpSeasonKey;
                    existing.PvpSeasonName = candidate.PvpSeasonName;
                    existing.PvpSeasonEvidence = candidate.PvpSeasonEvidence;
                }
                if (existing.HostingMode == TarkovHostingMode.Unknown
                    && candidate.HostingMode != TarkovHostingMode.Unknown)
                    existing.HostingMode = candidate.HostingMode;
                if (existing.RaidPurpose == TarkovRaidPurpose.Unknown
                    && candidate.RaidPurpose != TarkovRaidPurpose.Unknown)
                    existing.RaidPurpose = candidate.RaidPurpose;
                RaidClassificationModel.MergeInto(existing, candidate);
                if (!existing.MatchmakingSeconds.HasValue && candidate.MatchmakingSeconds.HasValue)
                    existing.MatchmakingSeconds = candidate.MatchmakingSeconds;
                int candidateOperationPriority = GetOperationStatePriority(candidate.OperationState);
                int existingOperationPriority = GetOperationStatePriority(existing.OperationState);
                bool candidateHasOperation = candidate.OperationStartedAt.HasValue;
                bool sameOperationStart = candidateHasOperation
                    && existing.OperationStartedAt.HasValue
                    && candidate.OperationStartedAt.Value == existing.OperationStartedAt.Value;
                bool replaceOperation = candidateHasOperation
                    && (candidateOperationPriority > existingOperationPriority
                        || !existing.OperationStartedAt.HasValue
                        || (candidateOperationPriority == existingOperationPriority
                            && candidateIsStrictlyLater));
                if (replaceOperation)
                {
                    double? measuredSeconds = candidate.RaidEntryMeasuredSeconds;
                    // Two copies of the same GameStarted event may differ only because one
                    // log was truncated. Reuse its valid measurement only when the event
                    // timestamp proves that the provenance is identical.
                    if (!measuredSeconds.HasValue && sameOperationStart)
                        measuredSeconds = existing.RaidEntryMeasuredSeconds;
                    existing.OperationStartedAt = candidate.OperationStartedAt;
                    existing.OperationEndedAt = candidate.OperationEndedAt;
                    existing.OperationState = candidate.OperationState;
                    existing.RaidEntryMeasuredSeconds = measuredSeconds;
                }
                else if (sameOperationStart
                    && !existing.RaidEntryMeasuredSeconds.HasValue
                    && candidate.RaidEntryMeasuredSeconds.HasValue)
                {
                    existing.RaidEntryMeasuredSeconds = candidate.RaidEntryMeasuredSeconds;
                }
                if (candidate.ActualRttMs.HasValue && (!existing.ActualRttMs.HasValue || candidateIsLater))
                    existing.ActualRttMs = candidate.ActualRttMs;
                if (candidateIsLater)
                {
                    existing.HasDisconnectRecord = candidate.HasDisconnectRecord;
                    existing.CurrentAttemptConnected = candidate.CurrentAttemptConnected;
                    existing.TimedOut = candidate.TimedOut;
                    existing.DisconnectReason = candidate.DisconnectReason;
                    existing.ConnectionEndedAt = candidate.ConnectionEndedAt;
                    existing.NetworkLoss = candidate.NetworkLoss;
                    existing.NetworkSent = candidate.NetworkSent;
                    existing.NetworkReceived = candidate.NetworkReceived;
                    existing.LogFilePath = candidate.LogFilePath;
                }
            }
            return merged.Values;
        }

        private static int GetOperationStatePriority(RaidOperationState state)
        {
            if (state == RaidOperationState.Completed) return 2;
            if (state == RaidOperationState.InProgress) return 1;
            return 0;
        }

        private static ServerSession CloneSession(ServerSession source)
        {
            return new ServerSession
            {
                Game = source.Game,
                SessionStarted = source.SessionStarted,
                LastUpdated = source.LastUpdated,
                SessionFolderName = source.SessionFolderName,
                SessionKey = source.SessionKey,
                LogFilePath = source.LogFilePath,
                IpAddress = source.IpAddress,
                Port = source.Port,
                MapName = source.MapName,
                GameMode = source.GameMode,
                ProgressionMode = source.ProgressionMode,
                PvpSeasonNumber = source.PvpSeasonNumber,
                PvpSeasonKey = source.PvpSeasonKey,
                PvpSeasonName = source.PvpSeasonName,
                PvpSeasonEvidence = source.PvpSeasonEvidence,
                HostingMode = source.HostingMode,
                RaidPurpose = source.RaidPurpose,
                CharacterType = source.CharacterType,
                ParticipationType = source.ParticipationType,
                PartySize = source.PartySize,
                CharacterEvidence = source.CharacterEvidence,
                ParticipationEvidence = source.ParticipationEvidence,
                PartySizeEvidence = source.PartySizeEvidence,
                ServerId = source.ServerId,
                ShortId = source.ShortId,
                DataCenterCode = source.DataCenterCode,
                ClientVersion = source.ClientVersion,
                MatchmakingSeconds = source.MatchmakingSeconds,
                ActualRttMs = source.ActualRttMs,
                NetworkLoss = source.NetworkLoss,
                NetworkSent = source.NetworkSent,
                NetworkReceived = source.NetworkReceived,
                ConnectionAttempts = source.ConnectionAttempts,
                ConnectionAttemptKeys = source.ConnectionAttemptKeys == null
                    ? null
                    : new HashSet<string>(source.ConnectionAttemptKeys, StringComparer.OrdinalIgnoreCase),
                UserReportCount = source.UserReportCount,
                UserReportKeys = source.UserReportKeys == null
                    ? null
                    : new HashSet<string>(source.UserReportKeys, StringComparer.OrdinalIgnoreCase),
                ConnectedOnce = source.ConnectedOnce,
                CurrentAttemptConnected = source.CurrentAttemptConnected,
                HasDisconnectRecord = source.HasDisconnectRecord,
                TimedOut = source.TimedOut,
                DisconnectReason = source.DisconnectReason,
                ConnectionEndedAt = source.ConnectionEndedAt,
                RaidEntryMeasuredSeconds = source.RaidEntryMeasuredSeconds,
                OperationStartedAt = source.OperationStartedAt,
                OperationEndedAt = source.OperationEndedAt,
                OperationState = source.OperationState,
                IpDetectedAt = source.IpDetectedAt
            };
        }
    }

    public static class LauncherSelectionReader
    {
        private const int MaximumLauncherPayloadLength = 8192;
        private const int MaximumSettingsPayloadLength = 262144;
        private const int MaximumArenaSettingsLength = 65536;
        private const string SettingsLoadedMarker = "Settings loaded:";
        private static readonly Regex ApplyCallRegex = new Regex(
            @"MatchingConfigurationWindow\.Apply\((?<arg>.*)\)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SelectGameRegex = new Regex(
            "MainWindow\\.SelectGame\\(\\\"(?<game>eft|arena)\\\"\\)\\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SettingsSelectedGameRegex = new Regex(
            "\\\"selectedGame\\\"\\s*:\\s*\\\"(?<game>eft|arena)\\\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SafeDataCenterNameRegex = new Regex(
            @"^[A-Za-z0-9 ._-]{1,80}$",
            RegexOptions.Compiled);
        private static readonly Regex TimestampRegex = new Regex(
            @"^(?<date>\d{4}\.\d{2}\.\d{2})\s+(?<time>\d{2}:\d{2}:\d{2})",
            RegexOptions.Compiled);

        public static LauncherSelectionInfo ReadCurrent()
        {
            string logRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Battlestate Games",
                "BsgLauncher",
                "Logs");
            string arenaRegionsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Battlestate Games",
                "Escape from Tarkov Arena",
                "Settings",
                "Regions.ini");
            return ReadFromSources(logRoot, arenaRegionsPath);
        }

        internal static LauncherSelectionInfo ReadFromDirectory(string logRoot)
        {
            return ReadFromSources(logRoot, null);
        }

        internal static LauncherSelectionInfo ReadFromSources(string logRoot, string arenaRegionsPath)
        {
            var result = new LauncherSelectionInfo();
            ReadEftSelection(logRoot, result);
            ReadArenaSelection(arenaRegionsPath, result);
            return result;
        }

        private static void ReadEftSelection(string logRoot, LauncherSelectionInfo result)
        {
            if (result == null || string.IsNullOrWhiteSpace(logRoot) || !Directory.Exists(logRoot)) return;

            string currentGame = null;
            string configurationGame = null;
            string pendingGame = null;
            string pendingPreviousSelection = null;
            DateTime? pendingPreviousUpdatedAt = null;
            DateTime? pendingAppliedAt = null;
            try
            {
                IEnumerable<FileInfo> files = new DirectoryInfo(logRoot)
                    .GetFiles("BSG_Launcher_*.log", SearchOption.TopDirectoryOnly)
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.LastWriteTimeUtc);
                foreach (FileInfo file in files)
                {
                    currentGame = null;
                    configurationGame = null;
                    pendingGame = null;
                    pendingPreviousSelection = null;
                    pendingPreviousUpdatedAt = null;
                    pendingAppliedAt = null;
                    foreach (string line in ReadLines(file.FullName))
                    {
                        DateTime lineTimestamp;
                        bool hasLineTimestamp = TryParseTimestamp(line, out lineTimestamp);

                        if (line.IndexOf("Starting launcher v.", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            currentGame = null;
                            configurationGame = null;
                            pendingGame = null;
                            pendingPreviousSelection = null;
                            pendingPreviousUpdatedAt = null;
                            pendingAppliedAt = null;
                            continue;
                        }

                        int settingsMarkerIndex = line.IndexOf(SettingsLoadedMarker, StringComparison.Ordinal);
                        if (settingsMarkerIndex >= 0)
                        {
                            string selectedGame;
                            currentGame = TryParseSettingsSelectedGame(line, settingsMarkerIndex, out selectedGame)
                                ? selectedGame
                                : null;
                            configurationGame = null;
                            pendingGame = null;
                            pendingPreviousSelection = null;
                            pendingPreviousUpdatedAt = null;
                            pendingAppliedAt = null;
                            continue;
                        }

                        if (pendingAppliedAt.HasValue && hasLineTimestamp)
                        {
                            double elapsed = (lineTimestamp - pendingAppliedAt.Value).TotalSeconds;
                            if (elapsed >= 0 && elapsed <= 2
                                && line.IndexOf("ErrorWindow initialized", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (string.Equals(pendingGame, "eft", StringComparison.OrdinalIgnoreCase))
                                {
                                    result.EftSelection = pendingPreviousSelection;
                                    result.EftUpdatedAt = pendingPreviousUpdatedAt;
                                }
                                pendingGame = null;
                                pendingAppliedAt = null;
                            }
                            else if (elapsed > 2)
                            {
                                pendingGame = null;
                                pendingAppliedAt = null;
                            }
                        }

                        Match changed = SelectGameRegex.Match(line);
                        if (changed.Success)
                        {
                            currentGame = changed.Groups["game"].Value.ToLowerInvariant();
                            configurationGame = null;
                        }

                        if (line.IndexOf("MainWindow.ShowMatchingConfig()", StringComparison.OrdinalIgnoreCase) >= 0)
                            configurationGame = string.Equals(currentGame, "eft", StringComparison.OrdinalIgnoreCase)
                                ? "eft"
                                : null;

                        if (line.IndexOf("MatchingConfigurationWindow.Apply", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            Match applyCall = ApplyCallRegex.Match(line);
                            string payload = applyCall.Success
                                ? DecodeCallArgument(applyCall.Groups["arg"].Value)
                                : line;
                            if (payload.Length > MaximumLauncherPayloadLength) continue;
                            List<string> names;
                            if (TryParseDataCenters(payload, out names))
                            {
                                string display = names.Count == 0 ? "자동 선택" : string.Join(", ", names.ToArray());
                                DateTime timestamp;
                                TryParseTimestamp(line, out timestamp);
                                if (string.Equals(configurationGame, "eft", StringComparison.OrdinalIgnoreCase))
                                {
                                    pendingGame = "eft";
                                    pendingPreviousSelection = result.EftSelection;
                                    pendingPreviousUpdatedAt = result.EftUpdatedAt;
                                    pendingAppliedAt = timestamp == DateTime.MinValue ? (DateTime?)null : timestamp;
                                    result.EftSelection = display;
                                    result.EftUpdatedAt = timestamp == DateTime.MinValue ? (DateTime?)null : timestamp;
                                }
                            }
                        }

                        if (line.IndexOf("MatchingConfigurationWindow closed", StringComparison.OrdinalIgnoreCase) >= 0)
                            configurationGame = null;
                    }
                }
            }
            catch
            {
                // Launcher logs can rotate or disappear while being inspected.
            }
        }

        private static bool TryParseSettingsSelectedGame(string line, int markerIndex, out string selectedGame)
        {
            selectedGame = null;
            try
            {
                int payloadStart = markerIndex + SettingsLoadedMarker.Length;
                if (markerIndex < 0 || payloadStart > line.Length) return false;
                string payload = line.Substring(payloadStart).Trim();
                if (payload.Length < 2
                    || payload.Length > MaximumSettingsPayloadLength
                    || payload[0] != '{'
                    || payload[payload.Length - 1] != '}')
                    return false;

                MatchCollection matches = SettingsSelectedGameRegex.Matches(payload);
                if (matches.Count != 1) return false;
                selectedGame = matches[0].Groups["game"].Value.ToLowerInvariant();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ReadArenaSelection(string arenaRegionsPath, LauncherSelectionInfo result)
        {
            if (result == null || string.IsNullOrWhiteSpace(arenaRegionsPath) || !File.Exists(arenaRegionsPath)) return;

            try
            {
                var before = new FileInfo(arenaRegionsPath);
                if (before.Length <= 0 || before.Length > MaximumArenaSettingsLength) return;
                long originalLength = before.Length;
                DateTime originalWriteTimeUtc = before.LastWriteTimeUtc;
                string payload;
                using (var stream = new FileStream(
                    arenaRegionsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    payload = reader.ReadToEnd();
                }

                var after = new FileInfo(arenaRegionsPath);
                if (after.Length != originalLength || after.LastWriteTimeUtc != originalWriteTimeUtc) return;

                string selection;
                if (!TryParseArenaRegions(payload, out selection)) return;
                result.ArenaSelection = selection;
                result.ArenaUpdatedAt = after.LastWriteTime;
            }
            catch
            {
                // Arena can rewrite Regions.ini while it is being read; the next refresh retries it.
            }
        }

        private static bool TryParseArenaRegions(string payload, out string selection)
        {
            selection = null;
            try
            {
                string json = (payload ?? string.Empty).Trim();
                if (json.Length < 2
                    || json.Length > MaximumArenaSettingsLength
                    || json[0] != '{'
                    || json[json.Length - 1] != '}')
                    return false;

                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumArenaSettingsLength };
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null) return false;

                object rawAutoSelect;
                if (root.TryGetValue("AutoSelectRegionsEnabled", out rawAutoSelect))
                {
                    if (!(rawAutoSelect is bool)) return false;
                    if ((bool)rawAutoSelect)
                    {
                        selection = "자동 선택";
                        return true;
                    }
                }

                object rawRegionSettings;
                if (!root.TryGetValue("RegionSettings", out rawRegionSettings)) return false;
                var regionSettings = rawRegionSettings as object[];
                if (regionSettings == null || regionSettings.Length > 100) return false;

                var selectedNames = new List<string>();
                var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (object rawRegion in regionSettings)
                {
                    var region = rawRegion as Dictionary<string, object>;
                    object rawName;
                    object rawState;
                    if (region == null
                        || !region.TryGetValue("DataCenter", out rawName)
                        || !region.TryGetValue("State", out rawState))
                        return false;
                    string name = rawName as string;
                    if (name == null
                        || !SafeDataCenterNameRegex.IsMatch(name)
                        || !(rawState is bool))
                        return false;
                    if ((bool)rawState && uniqueNames.Add(name)) selectedNames.Add(name);
                }

                selection = string.Join(", ", selectedNames.ToArray());
                return true;
            }
            catch
            {
                selection = null;
                return false;
            }
        }

        private static string DecodeCallArgument(string value)
        {
            string text = (value ?? string.Empty).Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2);
            try { return Regex.Unescape(text); }
            catch { return text; }
        }

        private static bool TryParseDataCenters(string payload, out List<string> names)
        {
            names = new List<string>();
            try
            {
                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumLauncherPayloadLength };
                var root = serializer.DeserializeObject(payload) as Dictionary<string, object>;
                object rawCenters;
                if (root == null || !root.TryGetValue("dataCenters", out rawCenters)) return false;
                var values = rawCenters as object[];
                if (values == null || values.Length > 50) return false;
                foreach (object rawValue in values)
                {
                    string value = rawValue as string;
                    if (value == null || !SafeDataCenterNameRegex.IsMatch(value)) return false;
                    names.Add(value);
                }
                return true;
            }
            catch
            {
                names.Clear();
                return false;
            }
        }

        private static IEnumerable<string> ReadLines(string path)
        {
            var lines = new List<string>();
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null) lines.Add(line);
                }
            }
            catch
            {
                // Launcher logs can rotate while being read.
            }
            return lines;
        }

        private static bool TryParseTimestamp(string line, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;
            Match match = TimestampRegex.Match(line ?? string.Empty);
            if (!match.Success) return false;
            return DateTime.TryParseExact(
                match.Groups["date"].Value + " " + match.Groups["time"].Value,
                "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out timestamp);
        }
    }
}
