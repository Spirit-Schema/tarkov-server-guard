// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace TarkovServerReporter
{
    /// <summary>
    /// Provides the application text catalog without changing the process culture.
    /// Protocol parsing and persisted data must continue to use invariant formats.
    /// </summary>
    public static partial class AppText
    {
        public const string KoreanLanguage = "ko-KR";
        public const string EnglishLanguage = "en";
        public const string DefaultLanguage = KoreanLanguage;

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, string> Korean =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> EnglishByKoreanLiteral =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> AmbiguousKoreanLiterals =
            new HashSet<string>(StringComparer.Ordinal);

        private static string _currentLanguage = DefaultLanguage;

        static AppText()
        {
            AddCatalogEntries();
            ValidateCatalogs();
            BuildLiteralTranslationIndex();
        }

        public static string CurrentLanguage
        {
            get
            {
                lock (SyncRoot)
                {
                    return _currentLanguage;
                }
            }
        }

        /// <summary>
        /// Selects a supported language. Unsupported values are rejected and do not
        /// change the current selection.
        /// </summary>
        public static bool SetLanguage(string language)
        {
            string normalized;
            if (!TryNormalizeLanguage(language, out normalized)) return false;
            lock (SyncRoot)
            {
                _currentLanguage = normalized;
            }
            return true;
        }

        public static bool IsSupportedLanguage(string language)
        {
            string normalized;
            return TryNormalizeLanguage(language, out normalized);
        }

        public static string[] GetSupportedLanguages()
        {
            return new[] { KoreanLanguage, EnglishLanguage };
        }

        public static string[] GetKeys()
        {
            string[] keys = new string[Korean.Count];
            Korean.Keys.CopyTo(keys, 0);
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }

        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "[[missing-key]]";

            string language;
            lock (SyncRoot)
            {
                language = _currentLanguage;
            }

            string value;
            Dictionary<string, string> selected = GetCatalog(language);
            if (selected.TryGetValue(key, out value)) return value;
            if (Korean.TryGetValue(key, out value)) return value;
            return "[[" + key + "]]";
        }

        public static string Format(string key, params object[] args)
        {
            string language;
            lock (SyncRoot)
            {
                language = _currentLanguage;
            }

            string format;
            Dictionary<string, string> selected = GetCatalog(language);
            if (!selected.TryGetValue(key, out format)
                && !Korean.TryGetValue(key, out format))
                format = "[[" + (key ?? "missing-key") + "]]";

            return string.Format(
                GetFormattingCulture(language),
                format,
                args ?? new object[0]);
        }

        /// <summary>
        /// Translates an exact Korean catalog literal into the selected language.
        /// This compatibility API is intended for existing static WinForms text.
        /// User content, paths, and dynamic values must not be passed to it.
        /// If one Korean literal has different meanings in the catalog, the input
        /// is returned unchanged instead of guessing which translation was meant.
        /// </summary>
        public static string TranslateLiteral(string literal)
        {
            if (literal == null) return null;

            string language;
            lock (SyncRoot)
            {
                language = _currentLanguage;
            }
            if (!string.Equals(language, EnglishLanguage, StringComparison.Ordinal))
                return literal;
            if (AmbiguousKoreanLiterals.Contains(literal)) return literal;

            string translated;
            return EnglishByKoreanLiteral.TryGetValue(literal, out translated)
                ? translated
                : literal;
        }

        /// <summary>
        /// Localizes only known raid-classification tokens. The input is never
        /// modified or persisted, and unknown text is returned byte-for-byte so
        /// map names, game modes, IPs, and user content cannot be translated by
        /// accident.
        /// </summary>
        public static string LocalizeDomainDisplay(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string[] parts = value.Split(
                new[] { " · " },
                StringSplitOptions.None);
            for (int index = 0; index < parts.Length; index++)
                parts[index] = LocalizeDomainToken(parts[index]);
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Normalizes historical PvP-season display tokens without modifying the
        /// stored source value or translating any surrounding user-provided text.
        /// </summary>
        public static string NormalizePvpSeasonDisplay(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            string[] parts = value.Split(
                new[] { " · " },
                StringSplitOptions.None);
            for (int index = 0; index < parts.Length; index++)
            {
                string normalized;
                if (TryNormalizePvpSeasonToken(parts[index].Trim(), out normalized))
                    parts[index] = normalized;
            }
            return string.Join(" · ", parts);
        }

        public static string GetRegionDisplayName(string regionCode)
        {
            string normalized = NormalizeRegionCode(regionCode);
            switch (normalized)
            {
                case "KR": return Get("Region.Name.KR");
                case "JP": return Get("Region.Name.JP");
                case "CN": return Get("Region.Name.CN");
                case "SG": return Get("Region.Name.SG");
                case "MY": return Get("Region.Name.MY");
                case "HK": return Get("Region.Name.HK");
                case "TW": return Get("Region.Name.TW");
                case "US": return Get("Region.Name.US");
                case "CA": return Get("Region.Name.CA");
                case "BR": return Get("Region.Name.BR");
                case "CL": return Get("Region.Name.CL");
                case "CO": return Get("Region.Name.CO");
                case "AU": return Get("Region.Name.AU");
                case "AE": return Get("Region.Name.AE");
                case "RU": return Get("Region.Name.RU");
                case "TR": return Get("Region.Name.TR");
                case "GB": return Get("Region.Name.GB");
                case "DE": return Get("Region.Name.DE");
                case "FR": return Get("Region.Name.FR");
                case "NL": return Get("Region.Name.NL");
                case "PL": return Get("Region.Name.PL");
                case "FI": return Get("Region.Name.FI");
                case "ZA": return Get("Region.Name.ZA");
                case "??": return Get("Region.Name.LocalOrOther");
                default: return normalized;
            }
        }

        public static string GetRegionDisplayLabel(string regionCode)
        {
            string normalized = NormalizeRegionCode(regionCode);
            if (normalized == "??") return Get("Region.Name.LocalOrOther");
            string name = GetRegionDisplayName(normalized);
            return string.Equals(name, normalized, StringComparison.Ordinal)
                ? normalized
                : name + " (" + normalized + ")";
        }

        internal static bool TryNormalizeLanguage(string language, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(language)) return false;
            string candidate = language.Trim();
            if (string.Equals(candidate, KoreanLanguage, StringComparison.OrdinalIgnoreCase))
            {
                normalized = KoreanLanguage;
                return true;
            }
            if (string.Equals(candidate, EnglishLanguage, StringComparison.OrdinalIgnoreCase))
            {
                normalized = EnglishLanguage;
                return true;
            }
            return false;
        }

        private static Dictionary<string, string> GetCatalog(string language)
        {
            return string.Equals(language, EnglishLanguage, StringComparison.Ordinal)
                ? English
                : Korean;
        }

        private static string LocalizeDomainToken(string token)
        {
            if (token == null) return null;
            string value = token.Trim();
            string normalizedSeason;
            if (TryNormalizePvpSeasonToken(value, out normalizedSeason))
                return normalizedSeason;

            if (string.Equals(value, "PvE(서버)", StringComparison.Ordinal))
                return Get("Domain.Display.PveServer");
            if (string.Equals(value, "PvE(로컬)", StringComparison.Ordinal))
                return Get("Domain.Display.PveLocal");
            if (string.Equals(value, "스캐브", StringComparison.Ordinal))
                return Get("Domain.Character.Scav");
            if (string.Equals(value, "단독", StringComparison.Ordinal)
                || string.Equals(value, "솔로", StringComparison.Ordinal))
                return Get("Domain.Participation.Solo");
            if (string.Equals(value, "파티", StringComparison.Ordinal))
                return Get("Domain.Participation.Party");
            if (string.Equals(value, "연습", StringComparison.Ordinal))
                return Get("Domain.RaidPurpose.Practice");
            if (string.Equals(value, "서버", StringComparison.Ordinal))
                return Get("Domain.Hosting.Server");
            if (string.Equals(value, "로컬", StringComparison.Ordinal))
                return Get("Domain.Hosting.Local");
            if (string.Equals(value, "미확인", StringComparison.Ordinal))
                return Get("Common.Status.Unknown");

            string partyValue = value.EndsWith(" 파티", StringComparison.Ordinal)
                ? value.Substring(0, value.Length - " 파티".Length)
                : value;
            string partySize;
            if (TryReadAsciiNumberSuffix(partyValue, string.Empty, "인", out partySize))
            {
                return string.Equals(CurrentLanguage, EnglishLanguage, StringComparison.Ordinal)
                    ? Format("Domain.Participation.PartySize", partySize)
                    : partySize + "인";
            }
            return token;
        }

        private static bool TryNormalizePvpSeasonToken(
            string value,
            out string normalized)
        {
            normalized = null;
            string seasonNumber;
            if (TryReadAsciiNumberSuffix(value, "PvP시즌", out seasonNumber)
                || TryReadAsciiNumberSuffix(value, "PvPs", out seasonNumber)
                || TryReadAsciiNumberSuffix(value, "PvP/S", out seasonNumber))
            {
                int parsedSeason;
                normalized = int.TryParse(
                        seasonNumber,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out parsedSeason)
                    && parsedSeason > 0
                    && parsedSeason <= 999
                    ? "PvP/S" + parsedSeason.ToString(CultureInfo.InvariantCulture)
                    : "PvP/S?";
                return true;
            }
            if (string.Equals(value, "PvPs?", StringComparison.Ordinal)
                || string.Equals(value, "PvP/S?", StringComparison.Ordinal)
                || string.Equals(value, "시즌 PvP", StringComparison.Ordinal)
                || string.Equals(value, "Seasonal PvP", StringComparison.Ordinal))
            {
                normalized = "PvP/S?";
                return true;
            }
            return false;
        }

        private static bool TryReadAsciiNumberSuffix(
            string value,
            string prefix,
            out string number)
        {
            return TryReadAsciiNumberSuffix(value, prefix, string.Empty, out number);
        }

        private static bool TryReadAsciiNumberSuffix(
            string value,
            string prefix,
            string suffix,
            out string number)
        {
            number = null;
            if (value == null
                || !value.StartsWith(prefix, StringComparison.Ordinal)
                || !value.EndsWith(suffix, StringComparison.Ordinal)
                || value.Length < prefix.Length + suffix.Length)
                return false;
            string candidate = value.Substring(
                prefix.Length,
                value.Length - prefix.Length - suffix.Length);
            foreach (char character in candidate)
            {
                if (character < '0' || character > '9') return false;
            }
            number = candidate;
            return true;
        }

        private static string NormalizeRegionCode(string regionCode)
        {
            if (string.IsNullOrWhiteSpace(regionCode)) return "??";
            string normalized = regionCode.Trim().ToUpperInvariant();
            if (string.Equals(normalized, "SN", StringComparison.Ordinal)) return "SG";
            if (normalized == "??") return normalized;
            if (normalized.Length != 2
                || normalized[0] < 'A' || normalized[0] > 'Z'
                || normalized[1] < 'A' || normalized[1] > 'Z')
                return "??";
            return normalized;
        }

        private static CultureInfo GetFormattingCulture(string language)
        {
            return string.Equals(language, EnglishLanguage, StringComparison.Ordinal)
                ? CultureInfo.GetCultureInfo("en-US")
                : CultureInfo.GetCultureInfo(KoreanLanguage);
        }

        private static void Add(string key, string korean, string english)
        {
            Korean.Add(key, korean);
            English.Add(key, english);
        }

        private static void ValidateCatalogs()
        {
            if (Korean.Count != English.Count)
                throw new InvalidOperationException("Localization catalog key counts do not match.");

            foreach (KeyValuePair<string, string> entry in Korean)
            {
                string english;
                if (string.IsNullOrWhiteSpace(entry.Key)
                    || string.IsNullOrWhiteSpace(entry.Value)
                    || !English.TryGetValue(entry.Key, out english)
                    || string.IsNullOrWhiteSpace(english))
                    throw new InvalidOperationException(
                        "Localization catalog entry is incomplete: " + entry.Key);
            }
        }

        private static void BuildLiteralTranslationIndex()
        {
            foreach (KeyValuePair<string, string> entry in Korean)
            {
                string english = English[entry.Key];
                string existing;
                if (!EnglishByKoreanLiteral.TryGetValue(entry.Value, out existing))
                {
                    EnglishByKoreanLiteral.Add(entry.Value, english);
                    continue;
                }
                if (!string.Equals(existing, english, StringComparison.Ordinal))
                    AmbiguousKoreanLiterals.Add(entry.Value);
            }
        }

        private static void AddCatalogEntries()
        {
            // Shared controls and values.
            Add("Common.Button.Apply", "적용", "Apply");
            Add("Common.Button.AutoDetect", "자동 찾기", "Auto-detect");
            Add("Common.Button.Back", "뒤로", "Back");
            Add("Common.Button.Block", "차단", "Block");
            Add("Common.Button.Browse", "직접 선택", "Browse");
            Add("Common.Button.Cancel", "취소", "Cancel");
            Add("Common.Button.Clear", "지우기", "Clear");
            Add("Common.Button.Close", "닫기", "Close");
            Add("Common.Button.Confirm", "확인", "Confirm");
            Add("Common.Button.Copy", "복사", "Copy");
            Add("Common.Button.CopyIp", "IP 복사", "Copy IP");
            Add("Common.Button.Delete", "삭제", "Delete");
            Add("Common.Button.Edit", "수정", "Edit");
            Add("Common.Button.Export", "내보내기", "Export");
            Add("Common.Button.Import", "불러오기", "Import");
            Add("Common.Button.Open", "열기", "Open");
            Add("Common.Button.Refresh", "새로 고침", "Refresh");
            Add("Common.Button.Restore", "복원", "Restore");
            Add("Common.Button.Save", "저장", "Save");
            Add("Common.Button.Scan", "조회", "Scan");
            Add("Common.Button.SelectAll", "전체 선택", "Select All");
            Add("Common.Button.Stop", "중지", "Stop");
            Add("Common.Button.Unblock", "차단 해제", "Unblock");
            Add("Common.Button.UnblockAll", "전체 차단 해제", "Unblock All");
            Add("Common.Button.UnblockSelected", "선택한 서버 차단 해제", "Unblock Selected");
            Add("Common.Label.Game", "게임", "Game");
            Add("Common.Label.Map", "맵", "Map");
            Add("Common.Label.Note", "메모", "Note");
            Add("Common.Label.Status", "상태", "Status");
            Add("Common.Label.Time", "시각", "Time");
            Add("Common.Label.Type", "유형", "Type");
            Add("Common.Status.Loading", "불러오는 중…", "Loading…");
            Add("Common.Status.NotApplicable", "해당 없음", "Not Applicable");
            Add("Common.Status.NotVerified", "확인 안 됨", "Not Verified");
            Add("Common.Status.Ready", "준비됨", "Ready");
            Add("Common.Status.Saved", "저장됨", "Saved");
            Add("Common.Status.Saving", "저장 중…", "Saving…");
            Add("Common.Status.Unknown", "미확인", "Unknown");
            Add("Common.Value.Empty", "-", "-");
            Add("Common.Count.Selected", "{0}개 선택", "{0} selected");
            Add("Common.Count.Items.One", "{0}개", "{0} item");
            Add("Common.Count.Items.Other", "{0}개", "{0} items");
            Add("Common.Error.Title", "오류", "Error");
            Add("Common.Error.Unexpected", "예상하지 못한 오류가 발생했습니다.", "An unexpected error occurred.");
            Add("Common.Error.TryAgain", "잠시 후 다시 시도해 주세요.", "Please try again in a moment.");

            // Language preferences.
            Add("Settings.Language.Label", "언어", "Language");
            Add("Settings.Language.Korean", "한국어", "한국어");
            Add("Settings.Language.English", "English", "English");
            Add("Settings.Language.RestartRequired", "언어 변경은 프로그램을 다시 시작하면 적용됩니다.", "The language change will apply after you restart the app.");
            Add("Settings.Language.SaveFailed", "언어 설정을 저장하지 못했습니다.", "The language setting could not be saved.");

            // Main window and log paths.
            Add("Main.Window.Title", "Tarkov Server Guard", "Tarkov Server Guard");
            Add("Main.Header.Subtitle", "타르코프 서버 접속 기록과 선택적 차단 관리", "Review connections, diagnose issues, and block selected servers");
            Add("Main.Button.UsageGuide", "사용방법", "Usage Guide");
            Add("Compatibility.Main.UsageGuideSpaced", "사용 방법", "Usage Guide");
            Add("Main.Button.CheckForUpdates", "업데이트 확인", "Check for Updates");
            Add("Main.Button.License", "라이선스 및 저작권", "License & Copyright");
            Add("Main.Section.LogPaths", "게임 로그 경로", "Game Log Paths");
            Add("Main.Label.EftLogPath", "EFT 로그", "EFT Logs");
            Add("Main.Label.ArenaLogPath", "Arena 로그", "Arena Logs");
            Add("Main.LogPath.NotFound", "로그 경로를 찾지 못했습니다.", "No log folder found.");
            Add("Main.LogPath.Applied", "로그 경로를 적용했습니다.", "Log folders updated.");
            Add("Main.LogPath.Invalid", "올바른 게임 로그 폴더를 선택해 주세요.", "Select a valid game log folder.");
            Add("Main.Section.CurrentServer", "현재 서버", "Current Server");
            Add("Main.Label.DataCenterRegion", "데이터센터 / 지역", "Data Center / Region");
            Add("Main.Label.CurrentPing", "현재 핑", "Current Ping");
            Add("Main.Label.InRaidRtt", "실게임 RTT", "In-Raid RTT");
            Add("Main.Label.InRaidPacketLoss", "실게임 패킷 손실", "In-Raid Packet Loss");
            Add("Main.Status.Scanning", "서버 정보를 조회하는 중…", "Scanning server information…");
            Add("Main.Status.ScanComplete", "서버 정보 조회를 마쳤습니다.", "Server scan complete.");
            Add("Main.Status.ScanCancelled", "서버 정보 조회를 취소했습니다.", "Server scan canceled.");
            Add("Main.Status.NoCurrentServer", "현재 연결된 서버가 없습니다.", "No active server connection.");
            Add("Main.Section.ConnectionHistory", "접속 기록", "Connection History");
            Add("Main.Section.ConnectionControls", "접속 제어", "Connection Controls");
            Add("Main.Filter.Latest100", "최근 100개", "Latest 100");
            Add("Main.Filter.Today", "오늘", "Today");
            Add("Main.Filter.Last7Days", "최근 7일", "Last 7 Days");
            Add("Main.Filter.Last30Days", "최근 30일", "Last 30 Days");
            Add("Main.Filter.Custom", "직접 선택", "Custom");
            Add("Main.Column.ServerIp", "서버 IP", "Server IP");
            Add("Main.Column.ConnectionResult", "서버 연결 결과", "Connection Result");
            Add("Main.Column.Firewall", "차단", "Block");
            Add("Main.Connection.LogUnavailable", "로그 없음", "No Log");
            Add("Main.Connection.NoRecord", "접속 기록 없음", "No Connect Event");
            Add("Main.Connection.NormalEnd", "정상 종료", "Disconnected");
            Add("Main.Connection.AbnormalEnd", "비정상 종료", "Connection Lost");
            Add("Main.Connection.Failed", "접속 실패", "Connection Failed");
            Add("Main.Connection.Timeout", "시간 초과", "Timed Out");
            Add("Main.Connection.ReconnectCount.One", "재접속 {0}회", "{0} reconnect");
            Add("Main.Connection.ReconnectCount.Other", "재접속 {0}회", "{0} reconnects");
            Add("Main.Result.Count", "조건에 맞는 접속 기록 {0}개", "Matching connections: {0}");
            Add("Main.Privacy.LocalOnly", "TSG는 게임 로그와 메모를 이 PC에서만 처리합니다.", "TSG processes game logs and notes only on this PC.");
            Add("Main.Sort.HeaderHelp", "클릭할 때마다 오름차순, 내림차순, 기본 순서로 정렬합니다.", "Click to cycle through ascending, descending, and default order.");
            Add("Main.Action.HeaderAccessibleName", "서버 차단 및 해제", "Server Block and Unblock Actions");
            Add("Main.Action.HeaderAccessibleDescription", "가로 스크롤과 관계없이 선택한 서버를 차단하거나 해제합니다.", "Block or unblock the selected server without changing the horizontal scroll position.");
            Add("Main.Header.UsageTooltip", "서버 차단 후 게임 화면에서 진행하는 방법을 확인합니다.", "View what to do in the game after blocking a server.");
            Add("Main.Header.SubtitleFull", "EFT·Arena 접속 기록을 확인하고, 필요한 서버만 선택적으로 차단하거나 해제합니다.", "Review EFT and Arena connections and choose which servers to block.");
            Add("Main.A11y.ProgramVersion", "프로그램 버전", "Application Version");
            Add("Main.A11y.CurrentVersion", "현재 프로그램 버전 {0}", "Current application version: {0}");
            Add("Main.Update.Button", "업데이트확인", "Check for Updates");
            Add("Main.Update.ButtonChecking", "확인 중…", "Checking…");
            Add("Main.Update.Tooltip", "새 버전이 있는지 지금 확인합니다.", "Check now for a new version.");
            Add("Main.Update.AccessibleDescription", "GitHub Releases에서 새 버전을 수동으로 확인합니다.", "Manually check GitHub Releases for a new version.");
            Add("Main.Update.AlreadyRunning", "업데이트 확인이 이미 진행 중입니다.", "An update check is already in progress.");
            Add("Main.Update.Checking", "새 버전을 확인하는 중…", "Checking for a new version…");
            Add("Main.Update.Available", "새 버전 v{0}을 찾았습니다.", "Version {0} is available.");
            Add("Main.Update.UpToDate", "현재 v{0}이 최신 버전입니다.", "Version {0} is up to date.");
            Add("Main.Update.Failed", "업데이트를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요.", "Could not check for updates. Please try again later.");
            Add("Main.Update.Cancelled", "업데이트 확인을 취소했습니다.", "Update check canceled.");
            Add("Main.Language.SwitchToEnglishTooltip", "English로 전환하고 TSG를 다시 시작합니다.", "Switch to English and restart TSG.");
            Add("Main.Language.SwitchToKoreanTooltip", "한국어로 전환하고 TSG를 다시 시작합니다.", "Switch to Korean and restart TSG.");
            Add("Main.Language.ChangePrompt", "{0}(으)로 변경하고 TSG를 다시 시작할까요?", "Switch to {0} and restart TSG?");
            Add("Main.License.Button", "라이선스", "License");
            Add("Main.License.Tooltip", "라이선스 전문과 서드파티 고지를 확인합니다.", "View the full license and third-party notices.");
            Add("Main.License.AccessibleDescription", "라이선스 및 저작권 안내를 엽니다.", "Open license and copyright information.");
            Add("Main.Path.Browse", "직접선택", "Browse");
            Add("Main.Path.AutoDetect", "자동 찾기", "Auto-detect");
            Add("Main.Path.Help", "시작할 때 공홈과 Steam 설치 경로를 자동으로 찾습니다. 찾지 못한 경우에만 직접 선택하세요.", "TSG checks the official launcher and Steam locations at startup. Browse only if automatic detection fails.");
            Add("Main.Path.AutoDetectTooltip", "공홈·Steam 설치 경로를 다시 자동 탐지합니다.", "Search the official launcher and Steam locations again.");
            Add("Main.Path.ApplyTooltip", "직접 선택한 경로를 적용합니다. 경로 변경이 없으면 현재 로그를 다시 읽습니다.", "Apply the selected paths. If they have not changed, refresh the current logs.");
            Add("Main.LauncherSelection.Loading", "게임런처 선택 서버   EFT: 확인 중…   |   Arena: 확인 중…", "Server Selection   EFT: Checking…   |   Arena: Checking…");
            Add("Main.LauncherSelection.Format", "게임런처 선택 서버   EFT: {0}   |   Arena: {1}", "Server Selection   EFT: {0}   |   Arena: {1}");
            Add("Main.LauncherSelection.Help", "EFT는 런처 로그에서 마지막으로 확인한 서버 적용값이며, 괄호는 기록 시각입니다.\n기록을 찾지 못해도 런처에는 서버가 선택되어 있을 수 있습니다.\n런처의 서버 선택 창에서 적용한 뒤 TSG가 갱신되는지 확인하세요.\nArena는 인게임 서버 선택값입니다.", "EFT shows the last server selection found in the launcher logs, with the time it was recorded.\nA missing record does not mean no servers are selected.\nApply your selection in the launcher and check that TSG updates.\nArena shows the in-game server selection.");
            Add("Main.LauncherSelection.CheckLauncher", "런처에서 확인", "Check Launcher");
            Add("Main.LauncherSelection.None", "선택 기록 없음", "No Selection Logged");
            Add("Main.Current.Title", "선택한 접속 서버", "Selected Connection");
            Add("Main.Current.Finding", "서버를 찾는 중…", "Finding server…");
            Add("Main.Current.GameMapType", "게임 · 맵/유형", "Game / Raid");
            Add("Main.Current.ConnectionTime", "접속 시각", "Connected At");
            Add("Main.Current.BeforeScan", "조회 전", "Not Scanned");
            Add("Main.Current.DataCenterRegion", "데이터센터/지역", "Data Center / Region");
            Add("Main.Current.InRaidPacketLoss", "실게임 패킷손실", "In-Raid Packet Loss");
            Add("Main.Current.MapTypeAccessibleName", "선택한 접속의 게임·맵·유형", "Game, map, and type for the selected connection");
            Add("Main.Current.RttAccessibleName", "선택한 접속의 실게임 RTT", "In-raid RTT for the selected connection");
            Add("Main.Current.PacketLossAccessibleName", "선택한 접속의 실게임 패킷손실", "In-raid packet loss for the selected connection");
            Add("Main.Scan.Tooltip", "최신 로그를 다시 읽은 뒤 현재 목록의 고유 IP에 대해 방화벽 상태·핑·지역을 조회합니다.", "Refresh the latest logs, then scan firewall status, ping, and location for each unique IP in the list.");
            Add("Main.Privacy.FullNotice", "사용자의 게임 로그·계정정보·SID·로컬경로는 전송하지 않습니다.\r\n차단·해제 시에만 Windows 관리자권한을 요청합니다.\r\n게임서버 IP 지역은 외부 API 대신 PC의 DB-IP Lite 데이터로 조회합니다.\r\n조회 시 새 월간 지역 DB가 있으면 자동으로 업데이트합니다. (약 60~70MB 교체)\r\n새 버전 확인을 위해 GitHub Releases에 접속합니다.\r\nDB-IP.com . CC BY 4.0", "Your game logs, account information, SID, and local paths are never sent.\r\nWindows administrator permission is requested only when blocking or unblocking.\r\nServer IP locations are resolved with the local DB-IP Lite database, not an external lookup API.\r\nA newer monthly location database is downloaded during a scan when available (about 60–70 MB).\r\nTSG connects to GitHub Releases to check for updates.\r\nDB-IP.com · CC BY 4.0");
            Add("Main.Period.Latest100", "최근100개", "Latest 100");
            Add("Main.Period.SevenDays", "7일", "7d");
            Add("Main.Period.ThirtyDays", "30일", "30d");
            Add("Main.Period.Custom", "직접선택", "Custom");
            Add("Main.Period.Latest100Tooltip", "EFT·Arena 전체에서 가장 최근 접속 기록 100개를 표시합니다.", "Show the 100 most recent EFT and Arena connections.");
            Add("Main.Period.TodayTooltip", "오늘 00:00부터 현재까지의 접속 기록을 다시 읽습니다.", "Refresh connections from 00:00 today through now.");
            Add("Main.Period.SevenDaysTooltip", "오늘을 포함한 최근 7일의 접속 기록을 다시 읽습니다.", "Refresh connections from the last 7 days, including today.");
            Add("Main.Period.ThirtyDaysTooltip", "오늘을 포함한 최근 30일의 접속 기록을 다시 읽습니다.", "Refresh connections from the last 30 days, including today.");
            Add("Main.Period.CustomTooltip", "시작일과 종료일을 직접 선택합니다. 종료일 전체가 포함됩니다.", "Choose a start and end date. The entire end date is included.");
            Add("Main.NotesArchive.Button", "메모 보관함", "Saved Notes");
            Add("Main.NotesArchive.Tooltip", "게임 로그가 삭제된 뒤에도 저장한 레이드 메모를 확인합니다.", "View saved raid notes even after their game logs are deleted.");
            Add("Main.BlockedServers.Button", "서버차단현황", "Blocked Servers");
            Add("Main.BlockedServers.Tooltip", "로그와 관계없이 앱이 관리하는 차단 서버를 확인하고 해제합니다.", "View and unblock servers managed by the app, independently of the logs.");
            Add("Main.Filter.All", "전체", "All");
            Add("Main.Filter.RegionAll", "지역: 전체 ▾", "Region: All ▾");
            Add("Main.Filter.RegionAccessibleName", "데이터센터 지역 필터", "Data Center Region Filter");
            Add("Main.Filter.RegionAccessibleDescription", "최근 목록에서 표시할 데이터센터 지역을 여러 개 선택합니다.", "Select one or more data center regions to show in the recent list.");
            Add("Main.Column.ConnectionTime", "접속 시각", "Connection Time");
            Add("Main.Column.PlayerReports", "신고기록", "Player Reports");
            Add("Main.Column.Note", "메모", "Note");
            Add("Main.Column.MapGameType", "맵 · 게임유형", "Map · Game Type");
            Add("Main.Column.DataCenterRegion", "데이터센터 / 지역", "Data Center / Region");
            Add("Main.Column.InRaidRtt", "실게임\r\nRTT", "In-Raid\r\nRTT");
            Add("Main.Column.InRaidPacketLoss", "실게임\r\n패킷손실", "In-Raid\r\nPacket Loss");
            Add("Main.Column.Unblock", "해제", "Unblock");
            Add("Main.Column.ConnectionResultHelp", "마지막 서버 연결 구간의 결과와 재접속 횟수입니다. 셀에 마우스를 올리면 상태별 의미를 확인할 수 있습니다.", "Shows the last server connection attempt and reconnect count. Point to a cell for details about its status.");
            Add("Main.Status.Preparing", "준비 중…", "Preparing…");
            Add("Main.Status.AccessibleName", "작업 상태 안내", "Task Status");
            Add("Main.Advanced.OperationTime", "작전시간 / 작전종료", "Duration / End");
            Add("Main.Advanced.AssignmentEntryTime", "서버배정 / 입장시간", "Match / Load Time");
            Add("Main.Advanced.GameVersion", "게임버전", "Game Version");
            Add("Main.Advanced.PortDataCenter", "포트 / 데이터센터", "Port / Data Center");
            Add("Main.Advanced.OperationTimeHelp", "로그 기준 작전시간과 종료 시각입니다. 실제 사망·탈출 시점과 차이가 있을 수 있으며 생환을 뜻하지 않습니다.", "Raid duration and end time from the log. The end event may differ from the actual death or extraction time and does not imply survival.");
            Add("Main.Advanced.AssignmentEntryHelp", "서버 배정과 서버 배정 후 레이드 입장까지 걸린 시간을 로그를 기준으로 표시합니다.", "Time spent matchmaking, then loading into the raid after a server was assigned. Both values come from the game log.");
            Add("Main.Advanced.ElapsedSuffix", " 걸림", " elapsed");
            Add("Main.Paths.FindingInitial", "공홈·Steam의 EFT·Arena 로그 폴더를 찾는 중…", "Looking for EFT and Arena log folders from the official launcher and Steam…");
            Add("Main.Paths.InitialCheckFailed", "초기 로그 경로를 확인하지 못했습니다: {0}", "Couldn't check the log folders: {0}");
            Add("Main.Paths.AutoDetectFailed", "로그 폴더를 자동으로 찾지 못했습니다. EFT 또는 Arena의 ‘직접 선택’을 이용해 주세요.", "No log folder was detected automatically. Use Browse for EFT or Arena.");
            Add("Main.Paths.Rediscovering", "공홈·Steam 설치 경로를 다시 찾는 중…", "Searching the official launcher and Steam locations again…");
            Add("Main.Paths.RediscoverFailed", "로그 경로를 다시 찾지 못했습니다: {0}", "Couldn't search for log folders: {0}");
            Add("Main.Paths.InstallNotFound", "공홈·Steam 설치 경로를 찾지 못했습니다. 설치 폴더를 직접 선택해 주세요.", "No BSG Launcher or Steam installation found. Use Browse to select the game folder.");
            Add("Main.Paths.SavedSource", "저장된 경로", "Saved path");
            Add("Main.Paths.Detected", "{0} 로그 경로를 확인했습니다.", "Found the {0} log path.");
            Add("Main.Paths.SelectAtLeastOne", "EFT 또는 Arena의 Logs 폴더를 하나 이상 선택해 주세요.", "Select at least one EFT or Arena Logs folder.");
            Add("Main.Paths.DialogTitle", "로그 폴더 확인", "Select Log Folder");
            Add("Main.Paths.SelectPrompt", "{0}의 Logs 폴더 또는 게임 설치 폴더를 선택하세요.", "Select the {0} Logs folder or game installation folder.");
            Add("Main.Paths.NoLogsInSelection", "선택한 폴더에서 Logs 폴더를 찾지 못했습니다.", "No Logs folder was found in the selected location.");
            Add("Main.Paths.SelectionPending", "폴더를 선택했습니다. ‘적용’을 누르면 로그를 다시 읽습니다.", "Folder selected. Click Apply to load its logs.");
            Add("Main.Paths.SelectionUnchanged", "현재 적용된 경로와 같습니다.", "This folder is already selected.");
            Add("Main.Logs.Reading", "{0} EFT·Arena 로그를 읽는 중…", "Reading EFT and Arena logs for {0}…");
            Add("Main.Logs.NoServerIp", "선택한 로그에서 접속 서버 IP 기록을 찾지 못했습니다.", "No server connections found in the selected logs.");
            Add("Main.Logs.NoConnectionsForPeriod", "{0} 범위에서 표시할 접속 기록을 찾지 못했습니다.", "No connections found for {0}.");
            Add("Main.Logs.ReadFailed", "로그를 읽는 중 오류가 발생했습니다: {0}", "Couldn't read the logs: {0}");
            Add("Main.Period.RangeLabel", "{0:yyyy-MM-dd}부터 {1:yyyy-MM-dd}까지의 접속 기록입니다. 종료일 전체가 포함됩니다.", "Connections from {0:yyyy-MM-dd} through {1:yyyy-MM-dd}, including the entire end date.");
            Add("Main.Period.DialogTitle", "기간 직접 선택", "Custom Date Range");
            Add("Main.Period.DialogPrompt", "조회할 기간을 선택하세요", "Choose a date range to scan");
            Add("Main.Period.StartDate", "시작일", "Start Date");
            Add("Main.Period.EndDate", "종료일", "End Date");
            Add("Main.Period.DialogHelp", "종료일의 23:59:59까지 포함하며,\r\n결과가 많으면 최근 100개만 표시합니다.", "Includes the entire end date.\r\nShows up to 100 of the most recent connections.");
            Add("Main.Period.InvalidRange", "시작일은 종료일보다 늦을 수 없습니다.", "The start date cannot be later than the end date.");
            Add("Main.Period.ValidationTitle", "기간 확인", "Check Date Range");
            Add("Main.Period.TodayLabel", "오늘", "Today");
            Add("Main.Period.Last7DaysLabel", "최근 7일", "Last 7 Days");
            Add("Main.Period.Last30DaysLabel", "최근 30일", "Last 30 Days");
            Add("Main.Period.CustomLabel", "직접 선택", "Custom Range");
            Add("Main.Period.Latest100Label", "최근 100개", "Latest 100");
            Add("Main.Summary.RegionFiltered", "지역 필터 {0}개 표시 / 대상 {1}개", "Region filter: {0} shown / {1} available");
            Add("Main.Summary.MatchingRecentCap", " · 조건에 맞는 {0}개 중 최근 100개 기준", " · latest 100 of {0} matches");
            Add("Main.Summary.RecentConfirmedAtLeast", "최근 100개 표시 · 확인된 기록 {0}개 이상", "Latest 100 · at least {0} records found");
            Add("Main.Summary.ConfirmedCount", "확인된 기록 {0}개 표시", "Records found: {0}");
            Add("Main.Summary.VisibleOfMatches", "{0}개 표시 · 조건에 맞는 {1}개 중 최근 100개", "Showing {0} · latest 100 of {1} matches");
            Add("Main.Summary.VisibleCount", "{0}개 표시", "Showing {0}");
            Add("Main.Summary.PartialMayOmit", " · 일부 로그를 읽지 못해 결과가 누락될 수 있음", " · some results may be missing because not all logs could be read");
            Add("Main.Summary.PartialMayOmitLatest", " · 일부 로그를 읽지 못해 최신 기록이 누락될 수 있음", " · recent results may be missing because not all logs could be read");
            Add("Main.Summary.Status", "{0} · {1} 접속 기록 · {2}{3}{4}", "{0} · {1} connections · {2}{3}{4}");
            Add("Main.Summary.ScanPending", " · 조회 대기", " · scan pending");
            Add("Main.Summary.NoNewLogs", " · 새 로그 없음", " · no new logs");
            Add("Main.Summary.NewLogs", " · 새 로그 {0}개 반영", " · new logs: {0}");
            Add("Main.Summary.OldestExcluded", " · 가장 오래된 {0}개 제외", " · oldest records omitted: {0}");
            Add("Main.Summary.RegionFilterRatio", " · 지역 필터 {0}/{1}개", " · region filter {0}/{1}");
            Add("Main.Region.MenuAccessibleName", "데이터센터 지역 선택", "Data Center Region Selection");
            Add("Main.Region.All", "전체 지역", "All Regions");
            Add("Main.Region.None", "표시할 지역 기록 없음", "No Regions Available");
            Add("Main.Region.ItemCount", "{0}   {1}개", "{0}   {1}");
            Add("Main.Region.ButtonOne", "지역: {0} ▾", "Region: {0} ▾");
            Add("Main.Region.ButtonCount", "지역: {0}개 ▾", "Regions: {0} ▾");
            Add("Main.Region.LocalOrOtherCompact", "PVE/기타", "Local / Other");
            Add("Main.Region.AllTooltip", "최근 목록의 모든 데이터센터 지역을 표시합니다.", "Show every data center region in the recent list.");
            Add("Main.Region.SelectedTooltip", "선택 지역: {0}", "Selected regions: {0}");
            Add("Main.Logs.NoFilteredConnections", "현재 필터에 표시할 접속 기록이 없습니다.", "No connections match the current filters.");
            Add("Main.Row.PlayerReportCount", "유저신고x{0}", "{0}");
            Add("Main.Row.PlayerReportTooltip", "클릭하여 신고한 유저의 닉네임과 신고 사유를 메모합니다.", "Add a note about the player you reported in-game.");
            Add("Main.Current.LocalRun", "로컬 실행", "Local Raid");
            Add("Main.Current.NoMatchIp", "매칭 IP 없음", "No Server IP");
            Add("Main.Current.Measuring", "측정 중…", "Measuring…");
            Add("Main.Current.Scanning", "조회 중…", "Scanning…");
            Add("Main.Current.Blocked", "차단 중", "Blocked");
            Add("Main.Current.NoResponse", "응답 없음", "No Response");
            Add("Main.Current.PingDetails", "평균 {0}ms · 최소 {1} / 최대 {2}ms", "Average: {0} ms · Min: {1} ms · Max: {2} ms");
            Add("Main.Geo.PublicIpRequired", "공인 서버 IP가 아닙니다.", "Not a public server IP.");
            Add("Main.Geo.DatabaseMissing", "로컬 지역 DB가 없습니다.", "Location database unavailable.");
            Add("Main.Geo.NotFound", "지역 정보가 없습니다.", "Location not found.");
            Add("Main.Geo.DatabaseUnreadable", "지역 DB를 읽을 수 없습니다.", "The location database could not be read.");
            Add("Main.Current.NotFound", "서버를 찾지 못했습니다", "No Server Found");
            Add("Main.Detail.InProgress", "진행 중", "In Progress");
            Add("Main.Duration.MinutesSeconds", "{0}분 {1}초", "{0}m {1}s");
            Add("Main.Duration.Seconds", "{0}초", "{0}s");
            Add("Main.Query.RefreshingLogs", "최신 레이드 로그를 확인하는 중…", "Checking the latest raid logs…");
            Add("Main.Query.NoServerIp", "최신 로그에서 조회할 서버 IP를 찾지 못했습니다.", "No server IP was found in the latest logs.");
            Add("Main.Query.PreparingGeoDb", "지역 DB 최초 준비 중… 약 60~70MB, 네트워크에 따라 잠시 걸릴 수 있습니다.", "Downloading the location database (about 60–70 MB). This is needed for the first scan.");
            Add("Main.Query.CheckingFirewall", "Windows 방화벽 상태를 확인하는 중…", "Checking Windows Firewall status…");
            Add("Main.Query.Progress", "조회 중… {0}/{1}", "Scanning… {0}/{1}");
            Add("Main.Query.Complete", "고유 IP {0}개 조회 완료 · 핑 응답 {1}개 · 차단 중 {2}개 · 지역 확인 {3}개", "Scanned: {0} IPs · Responded: {1} · Blocked: {2} · Located: {3}");
            Add("Main.Query.GeoDbFailedSuffix", " · 지역 DB 준비 실패", " · location database unavailable");
            Add("Main.Query.Cancelled", "서버 조회를 취소했습니다.", "Server scan canceled.");
            Add("Main.Query.Failed", "조회 중 오류가 발생했습니다: {0}", "Server scan failed: {0}");
            Add("Main.Query.Cancelling", "서버 조회를 취소하는 중…", "Canceling server scan…");
            Add("Main.Query.ButtonCancelling", "취소 중…", "Canceling…");
            Add("Main.Query.ButtonCancel", "취소", "Cancel");
            Add("Main.Query.WaitingForStop", "진행 중인 조회가 끝나기를 기다리고 있습니다.", "Waiting for the current scan to stop.");
            Add("Main.Query.CancelTooltip", "진행 중인 서버 조회와 지역 DB 최초 준비를 취소합니다.", "Cancel the current server scan and initial location database setup.");
            Add("Main.Metric.MissingLogHelp", "레이드 진행 중 또는 게임의 버그 · 비정상 종료로 필요한 로그가 기록되지 않았을 수 있습니다.", "The required log may be missing because the raid is still in progress, the game ended abnormally, or the game did not write it.");
            Add("Main.Metric.LocalRaidHelp", "로컬 PvE 레이드는 게임 서버 통계가 적용되지 않습니다.", "Game server statistics do not apply to local PvE raids.");
            Add("Main.Connection.State.Normal", "정상종료", "Disconnected");
            Add("Main.Connection.State.Abnormal", "비정상종료", "Connection Lost");
            Add("Main.Connection.State.Failed", "접속실패", "Connection Failed");
            Add("Main.Connection.State.NoRecord", "접속기록 없음", "No Connect Event");
            Add("Main.Connection.State.LogUnavailable", "로그없음", "No Log");
            Add("Main.Connection.State.NotApplicable", "해당 없음", "N/A");
            Add("Main.Connection.State.AbnormalTimeout", "비정상종료 · 시간초과", "Lost · Timeout");
            Add("Main.Connection.State.FailedTimeout", "접속실패 · 시간초과", "Failed · Timeout");
            Add("Main.Connection.ResultReconnect.One", "{0} · 재접속 {1}회", "{0} · {1} reconnect");
            Add("Main.Connection.ResultReconnect.Other", "{0} · 재접속 {1}회", "{0} · {1} reconnects");
            Add("Main.Connection.Help.Normal", "서버 연결이 정상적으로 종료된 기록입니다. 탈출·사망 같은 레이드 결과는 구분하지 않습니다.", "The log shows a normal disconnect. This does not indicate whether you extracted or died.");
            Add("Main.Connection.Help.AbnormalTimeout", "서버 연결 뒤 시간 초과로 비정상 종료된 기록입니다.", "The connection timed out after the server connection was established.");
            Add("Main.Connection.Help.FailedTimeout", "서버 연결이 완료되기 전에 시간 초과되어 접속에 실패한 기록입니다.", "The connection timed out before the server connection was established.");
            Add("Main.Connection.Help.Failed", "서버가 배정됐지만 마지막 연결 시도가 성공하기 전에 끝난 기록입니다.", "A server was assigned, but the last connection attempt ended before it succeeded.");
            Add("Main.Connection.Help.Abnormal", "서버 연결 뒤 정상 종료가 아닌 명시적인 종료 사유가 기록됐습니다.", "The log shows an abnormal disconnect after the connection was established.");
            Add("Main.Connection.Help.NoRecord", "서버 IP는 배정됐지만 대응하는 Connect 로그를 찾지 못했습니다.", "The game assigned a server IP, but the log has no matching Connect event.");
            Add("Main.Connection.Help.NotApplicable", "로컬 레이드는 연결할 게임 서버가 없어 서버연결 결과가 적용되지 않습니다.", "Server connection status does not apply to a local raid because there is no game server connection.");
            Add("Main.Connection.Help.Unknown", "종료 기록이 없거나 사유를 확정할 수 없습니다. 진행 중·강제 종료·로그 누락일 수 있습니다.", "No conclusive end event was logged. The raid may still be in progress, the game may have closed unexpectedly, or the logs may be incomplete.");
            Add("Main.Preview.NoFirewall", "미리보기 모드에서는 실제 방화벽을 변경하지 않습니다.", "Preview mode does not change Windows Firewall.");
            Add("Main.Preview.NoNotes", "미리보기 모드에서는 실제 메모를 열거나 변경하지 않습니다.", "Preview mode does not open or change your saved notes.");
            Add("Main.Preview.NoSettings", "미리보기 모드에서는 실제 설정을 변경하지 않습니다.", "Preview mode does not change your saved settings.");
            Add("Main.Preview.NoBlockedServers", "미리보기 모드에서는 실제 서버차단현황을 열지 않습니다.", "Preview mode does not open the live blocked-server list.");
            Add("Main.Action.NoServerIp", "이 기록에는 접속 제어할 서버 IP가 없습니다.", "This record has no server IP available for connection controls.");
            Add("Main.Action.Scanning", "서버 상태를 조회하고 있습니다.", "Server status is being scanned.");
            Add("Main.Action.FirewallBusy", "차단·해제 작업이 진행 중입니다.", "A block or unblock operation is in progress.");
            Add("Main.Action.LogsBusy", "로그를 읽고 있습니다. 잠시 후 다시 시도해 주세요.", "Logs are being read. Please try again in a moment.");
            Add("Main.Action.ScanFirst", "먼저 조회를 실행해 주세요.", "Run a scan first.");
            Add("Main.Action.FirewallUnknown", "방화벽 상태를 확인하지 못했습니다. 다시 조회해 주세요.", "Firewall status is unavailable. Run the scan again.");
            Add("Main.Action.AlreadyBlocked", "이미 차단 중인 서버입니다.", "This server is already blocked.");
            Add("Main.Action.NotBlocked", "현재 차단되지 않은 서버입니다.", "This server is not currently blocked.");
            Add("Main.Action.Unavailable", "현재 이 작업을 실행할 수 없습니다.", "This action is not available right now.");
            Add("Main.Action.DialogTitle", "접속 제어", "Connection Controls");
            Add("Main.Firewall.ChangingBlock", "{0} 서버 차단 중… 관리자 권한 요청을 확인해 주세요.", "Blocking {0}… Approve the Windows administrator prompt to continue.");
            Add("Main.Firewall.ChangingUnblock", "{0} 서버 해제 중… 관리자 권한 요청을 확인해 주세요.", "Unblocking {0}… Approve the Windows administrator prompt to continue.");
            Add("Main.Firewall.StateMismatch", "요청한 상태와 실제 방화벽 상태가 다릅니다. 현재 상태를 다시 확인해 주세요.", "The firewall doesn't show the requested change. Scan again to check its status.");
            Add("Main.Firewall.MetadataFailed", "방화벽 변경은 완료했지만 부가 정보를 저장하지 못했습니다.", "The firewall was updated, but the server details couldn't be saved.");
            Add("Main.Firewall.Failed", "방화벽 작업을 완료하지 못했습니다.", "The firewall operation could not be completed.");
            Add("Main.Firewall.Unblocked", "{0} 서버 차단을 해제했습니다.", "Unblocked server {0}.");
            Add("Main.Firewall.Blocked", "{0} 서버 차단을 적용했습니다.\r\n", "Blocked server {0}.\r\n");
            Add("Main.Firewall.Persistence", "차단 리스트는 Windows 방화벽에 저장되므로 해당 앱 종료·PC 재부팅 후에도 유지되며, 앱에서 해제할 때까지 계속 적용됩니다.", "Blocks stay active after TSG closes or your PC restarts, until you unblock the servers.");
            Add("Main.Firewall.QualityEvidence", "최근 레이드 {0}개 중 이 IP가 사용된 {1}개를 확인했고, 그중 {2}개에서 높은 지연·패킷 손실·시간초과 징후가 확인되었습니다.", "In the latest {0} raids, this IP was used in {1}; {2} showed high latency, packet loss, or timeouts.");
            Add("Main.Note.OpenTooltip", "저장된 메모를 엽니다.", "Open the saved note.");
            Add("Main.Note.AddTooltip", "이 레이드에 메모를 추가합니다.", "Add a note to this raid.");
            Add("Main.BlockedServers.Refreshing", "변경된 차단 상태를 목록에 반영하는 중…", "Refreshing the changed block status…");
            Add("Main.BlockedServers.Refreshed", "서버차단현황의 변경 사항을 반영했습니다.", "Blocked-server changes have been applied.");
            Add("Main.BlockedServers.RefreshFailed", "서버차단현황은 변경됐지만 목록을 새로 고치지 못했습니다: {0}", "The blocked-server list changed, but this view could not be refreshed: {0}");
            Add("Main.Clipboard.Copied", "서버 IP를 클립보드에 복사했습니다.", "Server IP copied to the clipboard.");
            Add("Main.Clipboard.Failed", "클립보드 복사 실패: {0}", "Could not copy to the clipboard: {0}");
            Add("Main.Preview.PathSuffix", "미리보기", "Preview");
            Add("Main.Preview.LauncherSelection", "게임런처 선택 서버   EFT: Singapore, Japan (08-14 00:30)   |   Arena: Korea, Japan (08-13 22:10)", "Server Selection   EFT: Singapore, Japan (08-14 00:30)   |   Arena: Korea, Japan (08-13 22:10)");
            Add("Main.Preview.Status", "미리보기용 샘플입니다. 실제 실행에서는 EFT·Arena 로그에서 최근 100개 기록을 읽습니다.", "Preview sample. In normal use, TSG reads the latest 100 connections from EFT and Arena logs.");

            // Stable domain presentation terms.
            Add("Domain.Progression.Pvp", "PvP", "PvP");
            Add("Domain.Progression.PvpSeason", "PvP/S{0}", "PvP/S{0}");
            Add("Domain.Progression.Pve", "PvE", "PvE");
            Add("Domain.Display.PveServer", "PvE(서버)", "PvE (Server)");
            Add("Domain.Display.PveLocal", "PvE(로컬)", "PvE (Local)");
            Add("Domain.Hosting.Server", "서버", "Server-hosted");
            Add("Domain.Hosting.Local", "로컬", "Local");
            Add("Domain.RaidPurpose.Practice", "연습", "Practice");
            Add("Domain.Character.Pmc", "PMC", "PMC");
            Add("Domain.Character.Scav", "스캐브", "Scav");
            Add("Domain.Participation.Solo", "단독", "Solo");
            Add("Domain.Participation.Party", "파티", "Party");
            Add("Domain.Participation.PartySize", "{0}인", "Party of {0}");

            Add("Region.Name.KR", "한국", "Korea");
            Add("Region.Name.JP", "일본", "Japan");
            Add("Region.Name.CN", "중국", "China");
            Add("Region.Name.SG", "싱가포르", "Singapore");
            Add("Region.Name.MY", "말레이시아", "Malaysia");
            Add("Region.Name.HK", "홍콩", "Hong Kong");
            Add("Region.Name.TW", "대만", "Taiwan");
            Add("Region.Name.US", "미국", "United States");
            Add("Region.Name.CA", "캐나다", "Canada");
            Add("Region.Name.BR", "브라질", "Brazil");
            Add("Region.Name.CL", "칠레", "Chile");
            Add("Region.Name.CO", "콜롬비아", "Colombia");
            Add("Region.Name.AU", "호주", "Australia");
            Add("Region.Name.AE", "아랍에미리트", "United Arab Emirates");
            Add("Region.Name.RU", "러시아", "Russia");
            Add("Region.Name.TR", "튀르키예", "Türkiye");
            Add("Region.Name.GB", "영국", "United Kingdom");
            Add("Region.Name.DE", "독일", "Germany");
            Add("Region.Name.FR", "프랑스", "France");
            Add("Region.Name.NL", "네덜란드", "Netherlands");
            Add("Region.Name.PL", "폴란드", "Poland");
            Add("Region.Name.FI", "핀란드", "Finland");
            Add("Region.Name.ZA", "남아프리카", "South Africa");
            Add("Region.Name.LocalOrOther", "PVE로컬/기타", "Local PvE / Other");


            // Post-raid report flow.
            Add("RaidReport.Navigation.Label", "레이드 결과보고", "Raid Report");
            Add("RaidReport.Window.Title", "레이드 결과보고", "Raid Report");
            Add("RaidReport.Card.PendingTitle", "결과보고 대기", "Report Pending");
            Add("RaidReport.Card.PendingDescription", "방금 레이드의 계획 결과를 확인해 주세요.", "Review the plan results from your latest raid.");
            Add("RaidReport.Button.Submit", "결과보고 제출", "Submit Report");
            Add("RaidReport.Button.Complete", "계획 완료", "Complete Plan");
            Add("RaidReport.Button.Continue", "일부 완료 · 이어가기", "Partially Complete · Continue");
            Add("RaidReport.Button.Retry", "그대로 재시도", "Retry Unchanged");
            Add("RaidReport.Button.ClosePlan", "계획 종료", "Close Plan");
            Add("RaidReport.Status.Submitted", "결과보고를 제출했습니다.", "Report submitted.");
            Add("RaidReport.Warning.Incomplete", "미완료 항목이 {0}개 있습니다. 계획을 완료할까요?", "{0} items are incomplete. Complete the plan anyway?");
            Add("RaidReport.CarryOver.Title", "다음 레이드로 이어갈 내용", "Carry Over to the Next Raid");
            Add("RaidReport.CarryOver.ResetNotice", "준비물과 동선의 체크 상태는 초기화됩니다.", "Equipment and route checkboxes will be reset.");
            Add("RaidReport.CarryOver.SetNext", "다음 작전계획으로 제출", "Submit as Next Plan");

            // Unified notes and memo archive.
            Add("Notes.Navigation.Label", "메모 보관함", "Saved Notes");
            Add("Notes.Navigation.Tooltip", "게임 로그가 삭제된 뒤에도 저장한 레이드 메모와 유저신고 메모를 확인합니다.", "View saved raid notes and report notes, even after game logs are deleted.");
            Add("Notes.Window.Title", "메모 보관함", "Saved Notes");
            Add("Notes.OpenFailed", "메모를 열지 못했습니다: {0}", "Could not open notes: {0}");
            Add("Notes.Tab.All", "모든 메모", "All Notes");
            Add("Notes.Tab.RaidRecords", "레이드 기록", "Raid Records");
            Add("Notes.Tab.FreeNotes", "자유메모", "Free Notes");
            Add("Notes.Tab.Archived", "보관됨", "Archived");
            Add("Notes.Type.RaidNote", "레이드 메모", "Raid Note");
            Add("Notes.Type.PlayerReport", "유저신고 메모", "Report Note");
            Add("Notes.Search.Placeholder", "제목, 본문, 맵, 서버 IP 검색", "Search titles, text, maps, or server IPs");
            Add("Notes.Label.Tags", "태그", "Tags");
            Add("Notes.Label.PlayerName", "유저네임", "Player Name");
            Add("Notes.Label.ReportReason", "신고 사유", "Report Reason");
            Add("Notes.Button.AttachScreenshot", "스크린샷 첨부", "Attach Screenshot");
            Add("Notes.Button.OpenStorageFolder", "보관 폴더 열기", "Open Storage Folder");
            Add("Notes.Status.Empty", "표시할 메모가 없습니다.", "There are no notes to display.");
            Add("Notes.Status.Count", "메모 {0}개", "{0} notes");
            Add("Notes.Confirm.Delete", "이 메모를 삭제할까요?", "Delete this note?");
            Add("Notes.Warning.LocalOnly", "메모와 첨부 경로는 이 PC에만 저장됩니다.", "Notes and attachment paths are stored only on this PC.");

            // Blocked servers, including temporary party entries.
            Add("BlockedServers.Navigation.Label", "서버차단현황", "Blocked Servers");
            Add("BlockedServers.Window.Title", "서버차단현황", "Blocked Servers");
            Add("BlockedServers.Section.Local", "내 차단 목록", "My Block List");
            Add("BlockedServers.Section.Party", "파티용 차단 목록", "Party Block List");
            Add("BlockedServers.Party.Description", "파티 플레이용으로 받은 서버를 따로 관리합니다. 파티가 끝나면 한 번에 해제할 수 있습니다.", "Keep IPs shared by your party in separate lists so you can remove their blocks together later.");
            Add("BlockedServers.Party.Button.AddIp", "파티용 IP 추가", "Add Party IPs");
            Add("BlockedServers.Party.Button.EndSession", "파티 차단 모두 해제", "Remove Party Blocks");
            Add("BlockedServers.Party.Confirm.EndSession", "파티용 차단 {0}개를 모두 해제할까요?", "Unblock all {0} party entries?");
            Add("BlockedServers.Column.Selected", "선택", "Selected");
            Add("BlockedServers.Column.Ip", "IP 주소", "IP Address");
            Add("BlockedServers.Column.DataCenter", "데이터센터", "Data Center");
            Add("BlockedServers.Column.Region", "지역", "Region");
            Add("BlockedServers.Column.BlockedAt", "차단 시각", "Blocked At");
            Add("BlockedServers.Column.CurrentPing", "현재 핑", "Current Ping");
            Add("BlockedServers.Column.Note", "메모", "Note");
            Add("BlockedServers.Status.Empty", "차단된 서버가 없습니다.", "No servers are blocked.");
            Add("BlockedServers.Status.Count", "차단된 서버 {0}개", "Blocked servers: {0}");
            Add("BlockedServers.Status.Blocked", "서버를 차단했습니다.", "Server blocked.");
            Add("BlockedServers.Status.Unblocked", "서버 차단을 해제했습니다.", "Server unblocked.");
            Add("BlockedServers.Warning.LocalFirewall", "차단 목록은 이 PC의 Windows 방화벽에만 적용됩니다.", "The block list applies only to Windows Firewall on this PC.");
            Add("BlockedServers.Ping.Title", "현재 핑 측정", "Current Ping Scan");
            Add("BlockedServers.Ping.BlockedUnavailable", "차단 중인 서버에는 현재 핑을 직접 측정할 수 없습니다.", "Current ping cannot be measured directly while a server is blocked.");
            Add("BlockedServers.Ping.NoReply", "응답 없음", "No Response");
            Add("BlockedServers.Ping.Scanning", "현재 핑을 측정하는 중…", "Scanning current ping…");
            Add("BlockedServers.Ping.Complete", "현재 핑 측정을 마쳤습니다.", "Current ping scan complete.");
            Add("BlockedServers.Confirm.UnblockSelected", "선택한 서버 {0}개의 차단을 해제할까요?", "Unblock the {0} selected servers?");

            // Backup, update, and accessibility text used by the core UI.
            Add("Backup.Status.ExportComplete", "백업을 저장했습니다: {0}", "Backup saved: {0}");
            Add("Backup.Status.RestoreComplete", "{0}개 항목을 복원했습니다.", "Restored {0} items.");
            Add("Backup.Error.InvalidFile", "지원하지 않거나 손상된 백업 파일입니다.", "The backup file is unsupported or damaged.");
            Add("Update.Status.Checking", "업데이트를 확인하는 중…", "Checking for updates…");
            Add("Update.Status.Current", "최신 버전을 사용하고 있습니다.", "You are using the latest version.");
            Add("Update.Status.Available", "새 버전 {0}을 사용할 수 있습니다.", "Version {0} is available.");
            Add("Update.Button.Install", "업데이트 설치", "Install Update");
            Add("Update.Button.Later", "나중에", "Later");
            Add("A11y.Main.Scan", "게임 로그를 다시 읽고 현재 서버 정보를 조회합니다.", "Reads the game logs again and scans the current server.");
            Add("A11y.Main.ConnectionHistory", "게임 서버 접속 기록 목록", "Game server connection history");
            Add("A11y.BlockedServers.Selection", "차단된 서버 선택 상태", "Blocked server selection state");
            Add("A11y.BlockedServers.Party", "파티용으로 임시 추가한 차단 서버 목록", "Servers temporarily blocked for the party session");
            AddV084CatalogEntries();
        }
    }
}
