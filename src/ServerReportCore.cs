// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace TarkovServerReporter
{
    public enum TarkovGame
    {
        Unknown,
        Eft,
        Arena
    }

    public enum TarkovProgressionMode
    {
        Unknown,
        Pvp,
        Pve,
        PvpSeason
    }

    public enum TarkovHostingMode
    {
        Unknown,
        Server,
        Local
    }

    public enum TarkovRaidPurpose
    {
        Unknown,
        Progression,
        Practice
    }

    public enum RaidOperationState
    {
        Unknown,
        InProgress,
        Completed
    }

    public sealed class ServerSession
    {
        public TarkovGame Game { get; set; }
        public DateTime SessionStarted { get; set; }
        public DateTime LastUpdated { get; set; }
        public string SessionFolderName { get; set; }
        public string SessionKey { get; set; }
        public string LogFilePath { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string MapName { get; set; }
        public string GameMode { get; set; }
        public TarkovProgressionMode ProgressionMode { get; set; }
        public TarkovHostingMode HostingMode { get; set; }
        public TarkovRaidPurpose RaidPurpose { get; set; }
        public string ServerId { get; set; }
        public string ShortId { get; set; }
        public string DataCenterCode { get; set; }
        public string ClientVersion { get; set; }
        public double? MatchmakingSeconds { get; set; }
        public double? ActualRttMs { get; set; }
        public double? NetworkLoss { get; set; }
        public long NetworkSent { get; set; }
        public long NetworkReceived { get; set; }
        public int ConnectionAttempts { get; set; }
        public HashSet<string> ConnectionAttemptKeys { get; set; }
        public int UserReportCount { get; set; }
        public HashSet<string> UserReportKeys { get; set; }
        public bool ConnectedOnce { get; set; }
        public bool CurrentAttemptConnected { get; set; }
        public bool HasDisconnectRecord { get; set; }
        public bool TimedOut { get; set; }
        public int? DisconnectReason { get; set; }
        public DateTime? ConnectionEndedAt { get; set; }
        public DateTime? IpDetectedAt { get; set; }
        public DateTime? OperationStartedAt { get; set; }
        public DateTime? OperationEndedAt { get; set; }
        public RaidOperationState OperationState { get; set; }

        public bool HasServerIp
        {
            get { return !string.IsNullOrWhiteSpace(IpAddress); }
        }

        public DateTime DisplayDetectedAt
        {
            get { return IpDetectedAt ?? SessionStarted; }
        }

        public int ReconnectCount
        {
            get { return Math.Max(0, ConnectionAttempts - 1); }
        }

        public TimeSpan? OperationDuration
        {
            get
            {
                if (OperationState != RaidOperationState.Completed
                    || !OperationStartedAt.HasValue
                    || !OperationEndedAt.HasValue
                    || OperationEndedAt.Value <= OperationStartedAt.Value)
                    return null;
                return OperationEndedAt.Value - OperationStartedAt.Value;
            }
        }

        public string GameDisplayName
        {
            get
            {
                if (Game == TarkovGame.Arena) return "Arena";
                if (Game == TarkovGame.Eft) return "EFT";
                return "-";
            }
        }

        public string MapModeDisplay
        {
            get
            {
                string map = string.IsNullOrWhiteSpace(MapName) ? "-" : MapName;
                return string.IsNullOrWhiteSpace(GameMode) ? map : map + " · " + GameMode;
            }
        }

        public string ProgressionModeText
        {
            get
            {
                if (ProgressionMode == TarkovProgressionMode.Pvp) return "PvP";
                if (ProgressionMode == TarkovProgressionMode.PvpSeason) return "PvP시즌";
                if (ProgressionMode == TarkovProgressionMode.Pve)
                {
                    if (HostingMode == TarkovHostingMode.Server) return "PvE(서버)";
                    if (HostingMode == TarkovHostingMode.Local) return "PvE(로컬)";
                    return "PvE";
                }
                return "미확인";
            }
        }

        public string HostingModeText
        {
            get
            {
                if (HostingMode == TarkovHostingMode.Server) return "서버";
                if (HostingMode == TarkovHostingMode.Local) return "로컬";
                return "미확인";
            }
        }

        public string RaidTypeText
        {
            get
            {
                if (ProgressionMode == TarkovProgressionMode.Unknown) return "미확인";
                string purpose = RaidPurpose == TarkovRaidPurpose.Practice
                    ? " · 연습"
                    : string.Empty;
                return ProgressionModeText + purpose;
            }
        }

        public string ConnectionStateText
        {
            get
            {
                if (HostingMode == TarkovHostingMode.Local) return "해당 없음";
                string result;
                bool currentAttemptConnected = CurrentAttemptConnected
                    || (ConnectionAttemptKeys == null && ConnectedOnce);
                bool hasSuccessfulConnection = ConnectedOnce || CurrentAttemptConnected;
                if (TimedOut)
                    result = hasSuccessfulConnection
                        ? "비정상종료 · 시간초과"
                        : "접속실패 · 시간초과";
                else if (!currentAttemptConnected && HasDisconnectRecord)
                    result = "접속실패";
                else if (ConnectionAttempts <= 0)
                    result = "접속기록 없음";
                else if (!currentAttemptConnected)
                    result = "종료미확인";
                else if (HasDisconnectRecord && DisconnectReason.HasValue)
                    result = DisconnectReason.Value == 0 ? "정상종료" : "비정상종료";
                else if (HasDisconnectRecord)
                    result = "종료미확인";
                else
                    result = "종료미확인";

                return result;
            }
        }

        public string ConnectionResultText
        {
            get
            {
                return ReconnectCount > 0
                    ? ConnectionStateText + " · 재접속 " + ReconnectCount + "회"
                    : ConnectionStateText;
            }
        }
    }

    public enum PingQuality
    {
        Unknown,
        Good,
        Elevated,
        High
    }

    public sealed class PingResult
    {
        public int Sent { get; set; }
        public int Received { get; set; }
        public long MinimumMs { get; set; }
        public long AverageMs { get; set; }
        public long MaximumMs { get; set; }
        public string ErrorMessage { get; set; }

        public bool IsAvailable
        {
            get { return Received > 0; }
        }

        public int LossPercent
        {
            get
            {
                if (Sent <= 0) return 100;
                return (int)Math.Round(((Sent - Received) * 100.0) / Sent);
            }
        }

        public PingQuality Quality
        {
            get
            {
                if (!IsAvailable) return PingQuality.Unknown;
                if (AverageMs >= 150) return PingQuality.High;
                if (AverageMs >= 100) return PingQuality.Elevated;
                return PingQuality.Good;
            }
        }

        public string ToDisplayText()
        {
            if (!IsAvailable)
            {
                return string.IsNullOrWhiteSpace(ErrorMessage)
                    ? "응답 없음"
                    : ErrorMessage;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "평균 {0}ms  ·  최소 {1} / 최대 {2}ms",
                AverageMs,
                MinimumMs,
                MaximumMs);
        }
    }

    public sealed class GeoInfo
    {
        public bool Success { get; set; }
        public string CountryCode { get; set; }
        public string Country { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string ErrorMessage { get; set; }

        public string ToDisplayText()
        {
            if (!Success)
                return string.IsNullOrWhiteSpace(ErrorMessage) ? "확인 안 됨" : ErrorMessage;

            string place = !string.IsNullOrWhiteSpace(City)
                ? City
                : (!string.IsNullOrWhiteSpace(Region) ? Region : Country);
            string country = !string.IsNullOrWhiteSpace(CountryCode) ? CountryCode : Country;

            if (string.IsNullOrWhiteSpace(place)) return string.IsNullOrWhiteSpace(country) ? "확인 안 됨" : country;
            if (string.IsNullOrWhiteSpace(country) || string.Equals(place, country, StringComparison.OrdinalIgnoreCase))
                return place;
            return place + ", " + country;
        }
    }

    public static class DataCenterRegionClassifier
    {
        public const string UnknownCode = "??";

        private static readonly IDictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "KR", "한국" },
                { "JP", "일본" },
                { "CN", "중국" },
                { "SG", "싱가포르" },
                { "MY", "말레이시아" },
                { "HK", "홍콩" },
                { "TW", "대만" },
                { "US", "미국" },
                { "CA", "캐나다" },
                { "BR", "브라질" },
                { "CL", "칠레" },
                { "CO", "콜롬비아" },
                { "AU", "호주" },
                { "AE", "아랍에미리트" },
                { "RU", "러시아" },
                { "TR", "튀르키예" },
                { "GB", "영국" },
                { "DE", "독일" },
                { "FR", "프랑스" },
                { "NL", "네덜란드" },
                { "PL", "폴란드" },
                { "FI", "핀란드" },
                { "ZA", "남아프리카" }
            };

        public static string GetRegionCode(string dataCenterCode)
        {
            if (string.IsNullOrWhiteSpace(dataCenterCode)) return UnknownCode;
            string value = dataCenterCode.Trim().ToUpperInvariant();
            if (value.Length < 4 || value[2] != '-' || !IsAsciiLetter(value[0])
                || !IsAsciiLetter(value[1]))
                return UnknownCode;
            return NormalizeRegionCode(value.Substring(0, 2));
        }

        public static string GetDisplayName(string regionCode)
        {
            string normalized = NormalizeRegionCode(regionCode);
            if (normalized == UnknownCode) return "PVE로컬/기타";
            string displayName;
            return DisplayNames.TryGetValue(normalized, out displayName)
                ? displayName
                : normalized;
        }

        public static string GetDisplayLabel(string regionCode)
        {
            string normalized = NormalizeRegionCode(regionCode);
            if (normalized == UnknownCode) return "PVE로컬/기타";
            string name = GetDisplayName(normalized);
            return string.Equals(name, normalized, StringComparison.Ordinal)
                ? normalized
                : name + " (" + normalized + ")";
        }

        public static int GetSortOrder(string regionCode)
        {
            switch (NormalizeRegionCode(regionCode))
            {
                case "KR": return 0;
                case "JP": return 1;
                case "CN": return 2;
                case "SG": return 3;
                case "HK": return 4;
                case "TW": return 5;
                case UnknownCode: return int.MaxValue;
                default: return 100;
            }
        }

        private static string NormalizeRegionCode(string regionCode)
        {
            if (string.IsNullOrWhiteSpace(regionCode)) return UnknownCode;
            string value = regionCode.Trim().ToUpperInvariant();
            if (value.Length != 2 || !IsAsciiLetter(value[0]) || !IsAsciiLetter(value[1]))
                return UnknownCode;

            // Live data has used SN-SINxx for Singapore as well as the usual SG-SINxx.
            return string.Equals(value, "SN", StringComparison.Ordinal) ? "SG" : value;
        }

        private static bool IsAsciiLetter(char value)
        {
            return value >= 'A' && value <= 'Z';
        }
    }

    public static class PingBatchPlanner
    {
        public static IList<string> GetUniqueServerIps(IEnumerable<ServerSession> sessions)
        {
            var result = new List<string>();
            if (sessions == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServerSession session in sessions)
            {
                if (session == null || !session.HasServerIp) continue;
                string ipAddress = session.IpAddress.Trim();
                if (seen.Add(ipAddress)) result.Add(ipAddress);
            }
            return result;
        }
    }

    public sealed class SettingsStore
    {
        private readonly string _settingsPath;
        private readonly string _legacySettingsPath;

        public SettingsStore()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard",
                "settings.txt");
            _legacySettingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerReporter",
                "settings.txt");
        }

        public string LoadLogPath()
        {
            try
            {
                string sourcePath = File.Exists(_settingsPath)
                    ? _settingsPath
                    : (File.Exists(_legacySettingsPath) ? _legacySettingsPath : null);
                if (sourcePath == null) return null;
                string value = File.ReadAllText(sourcePath, Encoding.UTF8).Trim();
                return Directory.Exists(value) ? value : null;
            }
            catch
            {
                return null;
            }
        }

        public void SaveLogPath(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(_settingsPath, path ?? string.Empty, Encoding.UTF8);
            }
            catch
            {
                // The app still works without persisted settings.
            }
        }
    }

    public static class LogPathFinder
    {
        public static string Find(string savedPath)
        {
            string normalized = NormalizeSelectedFolder(savedPath);
            if (!string.IsNullOrWhiteSpace(normalized)) return normalized;

            var candidates = new List<string>();
            string registryPath = FindFromRegistry();
            AddCandidate(candidates, registryPath);

            string localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Battlestate Games",
                "EFT",
                "Logs");
            AddCandidate(candidates, Directory.Exists(localAppData) ? localAppData : null);

            string processPath = FindFromRunningProcess();
            AddCandidate(candidates, processPath);
            AddCandidate(candidates, FindFromCommonLocations());

            string withLogs = candidates.FirstOrDefault(ContainsApplicationLogs);
            return withLogs ?? candidates.FirstOrDefault();
        }

        private static void AddCandidate(ICollection<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase)) candidates.Add(path);
        }

        public static string NormalizeSelectedFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                string fullPath = Path.GetFullPath(path.Trim().Trim('"'));
                if (!Directory.Exists(fullPath)) return null;

                if (string.Equals(new DirectoryInfo(fullPath).Name, "Logs", StringComparison.OrdinalIgnoreCase))
                    return fullPath;

                string childLogs = Path.Combine(fullPath, "Logs");
                if (Directory.Exists(childLogs)) return childLogs;

                if (ContainsApplicationLogs(fullPath)) return fullPath;
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static bool ContainsApplicationLogs(string directory)
        {
            try
            {
                if (Directory.GetFiles(directory, "*application*.log", SearchOption.TopDirectoryOnly).Length > 0)
                    return true;

                foreach (string child in Directory.GetDirectories(directory).Take(5))
                {
                    if (Directory.GetFiles(child, "*application*.log", SearchOption.TopDirectoryOnly).Length > 0)
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static string FindFromRegistry()
        {
            string[] subKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov"
            };

            RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };
            foreach (RegistryView view in views)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    {
                        foreach (string subKeyName in subKeys)
                        {
                            using (RegistryKey key = baseKey.OpenSubKey(subKeyName))
                            {
                                string installLocation = key == null ? null : Convert.ToString(key.GetValue("InstallLocation"));
                                string logs = NormalizeSelectedFolder(installLocation);
                                if (!string.IsNullOrWhiteSpace(logs)) return logs;
                            }
                        }
                    }
                }
                catch
                {
                    // Try the next registry view or fallback strategy.
                }
            }

            return null;
        }

        private static string FindFromRunningProcess()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("EscapeFromTarkov");
                foreach (Process process in processes)
                {
                    try
                    {
                        string executable = process.MainModule == null ? null : process.MainModule.FileName;
                        string installDirectory = string.IsNullOrWhiteSpace(executable)
                            ? null
                            : Path.GetDirectoryName(executable);
                        string logs = NormalizeSelectedFolder(installDirectory);
                        if (!string.IsNullOrWhiteSpace(logs)) return logs;
                    }
                    catch
                    {
                        // Access to MainModule can be denied; continue with other candidates.
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

        private static string FindFromCommonLocations()
        {
            string[] relativeCandidates =
            {
                @"Battlestate Games\EFT",
                @"Battlestate Games\Escape From Tarkov",
                @"Games\Escape From Tarkov",
                @"Escape From Tarkov",
                @"EFT"
            };

            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                    foreach (string relative in relativeCandidates)
                    {
                        string logs = NormalizeSelectedFolder(Path.Combine(drive.RootDirectory.FullName, relative));
                        if (!string.IsNullOrWhiteSpace(logs)) return logs;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
    }

    public static class LogScanner
    {
        private sealed class LogCacheEntry
        {
            public long Length { get; set; }
            public DateTime LastWriteTimeUtc { get; set; }
            public int FileCount { get; set; }
            public ServerSession Session { get; set; }
        }

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, LogCacheEntry> SessionCache =
            new Dictionary<string, LogCacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex IpRegex = new Regex(
            @"\bIp:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})(?::\d+)?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MapRegex = new Regex(
            @"scene preset path:maps/(?<map>[^.\s]+)\.bundle",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex LogTimestampRegex = new Regex(
            @"(?<date>\d{4}[-.]\d{2}[-.]\d{2})[ T](?<time>\d{2}:\d{2}:\d{2})",
            RegexOptions.Compiled);

        private static readonly Dictionary<string, string> MapNames =
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

        public static IList<ServerSession> Scan(string logsBasePath, int maximumSessions)
        {
            var result = new List<ServerSession>();
            if (string.IsNullOrWhiteSpace(logsBasePath) || !Directory.Exists(logsBasePath))
                return result;

            int max = maximumSessions <= 0 ? 30 : maximumSessions;

            try
            {
                var sessionDirectories = new List<DirectoryInfo>();
                var root = new DirectoryInfo(logsBasePath);
                if (root.GetFiles("*application*.log", SearchOption.TopDirectoryOnly).Length > 0)
                    sessionDirectories.Add(root);

                sessionDirectories.AddRange(root.GetDirectories());
                sessionDirectories = sessionDirectories
                    .OrderByDescending(GetDirectorySortTime)
                    .Take(max)
                    .ToList();

                foreach (DirectoryInfo directory in sessionDirectories)
                {
                    ServerSession session = ReadSession(directory);
                    if (session != null) result.Add(session);
                }
            }
            catch
            {
                return result;
            }

            return result
                .OrderByDescending(s => s.SessionStarted)
                .ThenByDescending(s => s.LastUpdated)
                .ToList();
        }

        private static DateTime GetDirectorySortTime(DirectoryInfo directory)
        {
            try
            {
                return directory.CreationTime > directory.LastWriteTime
                    ? directory.CreationTime
                    : directory.LastWriteTime;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private static ServerSession ReadSession(DirectoryInfo directory)
        {
            FileInfo[] logFiles;
            try
            {
                logFiles = directory
                    .GetFiles("*application*.log", SearchOption.TopDirectoryOnly)
                    .OrderBy(file => file.LastWriteTimeUtc)
                    .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return null;
            }

            if (logFiles.Length == 0) return null;

            FileInfo latestLogFile = logFiles[logFiles.Length - 1];
            long combinedLength = 0;
            DateTime latestWriteUtc = DateTime.MinValue;
            foreach (FileInfo file in logFiles)
            {
                combinedLength += file.Length;
                if (file.LastWriteTimeUtc > latestWriteUtc) latestWriteUtc = file.LastWriteTimeUtc;
            }

            try
            {
                lock (CacheLock)
                {
                    LogCacheEntry cached;
                    if (SessionCache.TryGetValue(directory.FullName, out cached)
                        && cached.Length == combinedLength
                        && cached.LastWriteTimeUtc == latestWriteUtc
                        && cached.FileCount == logFiles.Length)
                    {
                        return CloneSession(cached.Session);
                    }
                }
            }
            catch
            {
                // If metadata cannot be read, fall through to a normal scan.
            }

            string matchedIp = null;
            string matchedMap = null;
            DateTime? matchedIpDetectedAt = null;

            foreach (FileInfo logFile in logFiles)
            {
                try
                {
                    using (var stream = new FileStream(
                        logFile.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            Match ipMatch = IpRegex.Match(line);
                            if (ipMatch.Success && IsValidPublicOrRoutableIpv4(ipMatch.Groups["ip"].Value))
                            {
                                matchedIp = ipMatch.Groups["ip"].Value;
                                DateTime detectedAt;
                                matchedIpDetectedAt = TryParseLogTimestamp(line, out detectedAt)
                                    ? detectedAt
                                    : (DateTime?)null;
                            }

                            Match mapMatch = MapRegex.Match(line);
                            if (mapMatch.Success)
                            {
                                string rawMap = mapMatch.Groups["map"].Value;
                                string display;
                                matchedMap = MapNames.TryGetValue(rawMap, out display) ? display : rawMap;
                            }
                        }
                    }
                }
                catch
                {
                    // Continue with the remaining rotated logs if one file is temporarily unavailable.
                }
            }

            DateTime started;
            try { started = directory.CreationTime; }
            catch { started = latestLogFile.CreationTime; }

            var scannedSession = new ServerSession
            {
                SessionStarted = started,
                LastUpdated = latestLogFile.LastWriteTime,
                SessionFolderName = directory.Name,
                LogFilePath = latestLogFile.FullName,
                IpAddress = matchedIp,
                MapName = matchedMap,
                IpDetectedAt = matchedIpDetectedAt
            };

            try
            {
                lock (CacheLock)
                {
                    SessionCache[directory.FullName] = new LogCacheEntry
                    {
                        Length = combinedLength,
                        LastWriteTimeUtc = latestWriteUtc,
                        FileCount = logFiles.Length,
                        Session = CloneSession(scannedSession)
                    };
                }
            }
            catch
            {
                // Caching is an optimization only.
            }

            return scannedSession;
        }

        private static ServerSession CloneSession(ServerSession session)
        {
            if (session == null) return null;
            return new ServerSession
            {
                SessionStarted = session.SessionStarted,
                LastUpdated = session.LastUpdated,
                SessionFolderName = session.SessionFolderName,
                LogFilePath = session.LogFilePath,
                IpAddress = session.IpAddress,
                MapName = session.MapName,
                OperationStartedAt = session.OperationStartedAt,
                OperationEndedAt = session.OperationEndedAt,
                OperationState = session.OperationState,
                IpDetectedAt = session.IpDetectedAt
            };
        }

        private static bool TryParseLogTimestamp(string line, out DateTime timestamp)
        {
            timestamp = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(line)) return false;

            Match match = LogTimestampRegex.Match(line);
            if (!match.Success) return false;

            string normalized = match.Groups["date"].Value.Replace('.', '-')
                + " " + match.Groups["time"].Value;
            return DateTime.TryParseExact(
                normalized,
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out timestamp);
        }

        private static bool IsValidPublicOrRoutableIpv4(string value)
        {
            IPAddress parsed;
            if (!IPAddress.TryParse(value, out parsed)) return false;
            return parsed.AddressFamily == AddressFamily.InterNetwork;
        }
    }

    public static class NetworkServices
    {
        internal const string ProductUserAgent = "TarkovServerGuard/0.7.4";
        private static readonly DbIpLiteGeoService GeoService = CreateGeoService();

        private static DbIpLiteGeoService CreateGeoService()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            return new DbIpLiteGeoService();
        }

        public static bool HasUsableGeoDatabase
        {
            get { return GeoService.HasUsableDatabase; }
        }

        public static Task<DbIpLiteUpdateResult> UpdateGeoDatabaseIfDueAsync(
            bool userAcceptedLicenseAndNetworkDownload,
            CancellationToken cancellationToken)
        {
            return GeoService.UpdateInBackgroundIfDueAsync(
                userAcceptedLicenseAndNetworkDownload,
                cancellationToken);
        }

        public static void DisposeGeoService()
        {
            GeoService.Dispose();
        }

        public static Task<PingResult> MeasureAdaptivePingAsync(
            string ipAddress,
            int initialAttempts,
            int additionalAttemptsOnLoss,
            int timeoutMs,
            int intervalMs)
        {
            return MeasureAdaptivePingAsync(
                ipAddress,
                initialAttempts,
                additionalAttemptsOnLoss,
                timeoutMs,
                intervalMs,
                CancellationToken.None);
        }

        public static Task<PingResult> MeasureAdaptivePingAsync(
            string ipAddress,
            int initialAttempts,
            int additionalAttemptsOnLoss,
            int timeoutMs,
            int intervalMs,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var result = new PingResult();
                var samples = new List<long>();
                int firstBatch = Math.Max(1, initialAttempts);
                int extraBatch = Math.Max(0, additionalAttemptsOnLoss);

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var ping = new Ping())
                    {
                        byte[] buffer = new byte[32];
                        var options = new PingOptions(64, true);
                        SendPingBatch(
                            ping, ipAddress, firstBatch, timeoutMs, intervalMs,
                            buffer, options, result, samples, cancellationToken);

                        if (samples.Count < firstBatch && extraBatch > 0)
                        {
                            WaitForPingInterval(intervalMs, cancellationToken);
                            SendPingBatch(
                                ping, ipAddress, extraBatch, timeoutMs, intervalMs,
                                buffer, options, result, samples, cancellationToken);
                        }
                    }

                    result.Received = samples.Count;
                    if (samples.Count > 0)
                    {
                        result.MinimumMs = samples.Min();
                        result.MaximumMs = samples.Max();
                        result.AverageMs = (long)Math.Round(samples.Average());
                    }
                    else
                    {
                        result.ErrorMessage = "응답 없음";
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = "핑 측정 실패: " + ex.Message;
                }

                return result;
            }, cancellationToken);
        }

        private static void SendPingBatch(
            Ping ping,
            string ipAddress,
            int attempts,
            int timeoutMs,
            int intervalMs,
            byte[] buffer,
            PingOptions options,
            PingResult result,
            IList<long> samples,
            CancellationToken cancellationToken)
        {
            for (int index = 0; index < attempts; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Sent++;
                try
                {
                    PingReply reply = ping.Send(ipAddress, Math.Max(250, timeoutMs), buffer, options);
                    if (reply != null && reply.Status == IPStatus.Success)
                        samples.Add(reply.RoundtripTime);
                }
                catch
                {
                    // ICMP may be blocked or deprioritized. Keep the attempt for internal reliability checks.
                }

                if (index < attempts - 1)
                    WaitForPingInterval(intervalMs, cancellationToken);
            }
        }

        private static void WaitForPingInterval(
            int intervalMs,
            CancellationToken cancellationToken)
        {
            if (intervalMs <= 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            if (cancellationToken.WaitHandle.WaitOne(intervalMs))
                cancellationToken.ThrowIfCancellationRequested();
        }

        public static Task<GeoInfo> LookupGeoAsync(string ipAddress)
        {
            return GeoService.LookupAsync(ipAddress);
        }
    }
}
