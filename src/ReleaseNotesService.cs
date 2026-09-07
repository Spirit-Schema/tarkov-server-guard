// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    internal sealed class ReleaseNotesEntry
    {
        internal string VersionText { get; set; }
        internal string NotesText { get; set; }
    }

    internal static class ReleaseNotesCatalog
    {
        internal const int MaximumNotesCharacters = 64 * 1024;

        private sealed class BundledRelease
        {
            internal string Version;
            internal string Notes;
            internal string EnglishNotes;
        }

        private static readonly BundledRelease[] BundledReleases =
        {
            new BundledRelease
            {
                Version = "0.8.6",
                EnglishNotes =
                    "0.8.6 hotfix\n- Added a step-by-step usage guide to Add Party IPs.\n\nWhat's new since 0.8.3\n\n"
                    + "- TSG remembers your window size and adjusted column widths for your next session.\n"
                    + "- Choose Korean or English in Settings.\n"
                    + "- Search Saved Notes by text, tags, map, player name, or report reason, and filter by note type.\n"
                    + "- Add server IPs shared by your party as named lists, then remove a list's blocks together. Your personal blocks and blocks still used by other party lists stay in place.\n"
                    + "- Made the interface easier to use.\n"
                    + "- Improved log loading speed and reduced memory use when reading large logs.\n"
                    + "- Made note saving, backups, and restores more reliable.\n\n"
                    + "Party blocks apply only to this PC. Each party member needs to apply the same IPs on their own PC.",
                Notes =
                    "0.8.6 핫픽스\n- 파티 IP 추가 창에 단계별 사용방법 안내를 추가했습니다.\n\n0.8.3 이후 달라진 점\n\n"
                    + "- 창 크기와 직접 조절한 열 너비를 기억해 다음 실행에서도 이어서 사용합니다.\n"
                    + "- 설정에서 한국어·영어를 선택할 수 있습니다.\n"
                    + "- 메모 보관함에 검색과 종류 필터를 추가했습니다. 본문·태그·맵·닉네임·신고 사유로 메모를 찾을 수 있습니다.\n"
                    + "- 파티원이 공유한 서버 IP를 목록별로 추가하고 한 번에 해제할 수 있습니다. 기존 개인 차단과 다른 파티 목록의 차단은 유지됩니다.\n"
                    + "- UI 조작 편의성을 개선했습니다.\n"
                    + "- 로그 조회 속도를 높이고, 큰 로그를 읽을 때의 메모리 사용량을 줄였습니다.\n"
                    + "- 메모 저장·백업·복원의 안정성을 높였습니다.\n\n"
                    + "파티 차단은 이 PC에만 적용됩니다. 각 파티원이 같은 IP를 직접 적용해야 합니다."
            },
            new BundledRelease
            {
                Version = "0.8.5",
                EnglishNotes =
                    "What's new since 0.8.3\n\n"
                    + "- TSG remembers your window size and adjusted column widths for your next session.\n"
                    + "- Choose Korean or English in Settings.\n"
                    + "- Search Saved Notes by text, tags, map, player name, or report reason, and filter by note type.\n"
                    + "- Add server IPs shared by your party as named lists, then remove a list's blocks together. Your personal blocks and blocks still used by other party lists stay in place.\n"
                    + "- Made the interface easier to use.\n"
                    + "- Improved log loading speed and reduced memory use when reading large logs.\n"
                    + "- Made note saving, backups, and restores more reliable.\n\n"
                    + "Party blocks apply only to this PC. Each party member needs to apply the same IPs on their own PC.",
                Notes =
                    "0.8.3 이후 달라진 점\n\n"
                    + "- 창 크기와 직접 조절한 열 너비를 기억해 다음 실행에서도 이어서 사용합니다.\n"
                    + "- 설정에서 한국어·영어를 선택할 수 있습니다.\n"
                    + "- 메모 보관함에 검색과 종류 필터를 추가했습니다. 본문·태그·맵·닉네임·신고 사유로 메모를 찾을 수 있습니다.\n"
                    + "- 파티원이 공유한 서버 IP를 목록별로 추가하고 한 번에 해제할 수 있습니다. 기존 개인 차단과 다른 파티 목록의 차단은 유지됩니다.\n"
                    + "- UI 조작 편의성을 개선했습니다.\n"
                    + "- 로그 조회 속도를 높이고, 큰 로그를 읽을 때의 메모리 사용량을 줄였습니다.\n"
                    + "- 메모 저장·백업·복원의 안정성을 높였습니다.\n\n"
                    + "파티 차단은 이 PC에만 적용됩니다. 각 파티원이 같은 IP를 직접 적용해야 합니다."
            },
            new BundledRelease
            {
                Version = "0.8.3",
                EnglishNotes =
                    "- EFT connection history now shows your PMC or Scav role and whether you played solo or in a group of 2–5, when confirmed by the log.\n"
                    + "- PvP season numbers are shown only when explicitly recorded in the log. Missing details are left out.\n"
                    + "- The map and game type column is wider to accommodate raid details. Newly saved notes keep the same information.\n"
                    + "- After blocking a server, TSG checks up to 100 recent raids and shows how often that IP appeared and how many of those raids showed high latency, packet loss, or timeouts.\n"
                    + "- Blocks apply to this PC only. Each party member must block the same IPs to keep the whole party off those servers.\n"
                    + "- Step 2 of the usage guide now explains that your gear is kept after you confirm leaving.",
                Notes =
                    "- EFT 접속 기록의 맵·게임유형 뒤에 로그로 확인된 PMC·스캐브와 솔로·2인~5인 정보를 함께 표시합니다.\n"
                    + "- 로그에 명시된 시즌 번호가 있으면 PvP시즌1·PvP시즌2처럼 표시하고, 확인되지 않은 정보는 추측하지 않고 생략합니다.\n"
                    + "- 길어진 레이드 정보를 읽기 쉽도록 맵·게임유형 열을 조금 넓혔으며, 새로 저장하는 메모에도 같은 표시를 보존합니다.\n"
                    + "- 서버 차단 완료 뒤 최근 레이드 최대 100개에서 해당 IP의 사용 횟수와 높은 지연·패킷 손실·시간초과 징후가 관찰된 횟수를 안내합니다.\n"
                    + "- 차단 리스트는 이 PC에만 적용되므로 파티원 모두의 접속을 막으려면 각 파티원이 같은 서버를 차단해야 합니다.\n"
                    + "- 사용방법 2번의 나가기 확인 뒤에 (장비는 보존됩니다) 안내를 같은 줄에 추가했습니다."
            },
            new BundledRelease
            {
                Version = "0.8.2",
                EnglishNotes =
                    "- Fixed vertical text alignment in the Browse, Auto-detect, and Apply buttons.\n"
                    + "- Saved Notes can now back up raid and report notes in a single file and restore missing notes without overwriting existing ones.\n"
                    + "- Backups include valid local screenshot paths, not the original image files.\n"
                    + "- Saved Notes now shows the game type alongside the map. The default window is wider to avoid horizontal scrolling when first opened.",
                Notes =
                    "- TSG 경로의 직접선택·자동 찾기·적용 버튼 문구가 아래쪽으로 치우쳐 보이지 않도록 세로 위치를 중단에 맞췄습니다.\n"
                    + "- 메모보관함에서 일반 레이드 메모와 유저신고 메모를 하나의 파일로 백업하고 없는 메모만 안전하게 복원할 수 있습니다.\n"
                    + "- 스크린샷 원본 파일은 제외하고 검증된 로컬 이미지 연결 경로만 백업·복원합니다.\n"
                    + "- 메모보관함의 맵 열에 게임유형을 함께 표시하고, 처음 열 때 가로 스크롤이 생기지 않도록 기본 창 폭을 넓혔습니다."
            },
            new BundledRelease
            {
                Version = "0.8.1",
                EnglishNotes =
                    "- The No Log tooltip now explains that a game bug can also prevent the required log from being written.\n"
                    + "- Improved the two-line status message after blocking a server so it fits smaller windows and different display scales.\n"
                    + "- Fixed the first row's checkbox not updating immediately when selecting all saved notes.\n"
                    + "- In Blocked Servers, use the selection header or Ctrl+A to select all and see the selection count.\n"
                    + "- Improved accessible descriptions for selection columns and bulk actions. Screen readers can now read the selection count.\n"
                    + "- Added ascending, descending, and default sorting with orange direction indicators to Saved Notes, Blocked Servers, and the main Block/Unblock columns. Main-window headers explain sorting, and the two auxiliary windows have select-all checkboxes.\n"
                    + "- Block-list backup filenames now include the save time. The confirmation message shows the filename without its full path.",
                Notes =
                    "- ‘로그 없음’ 도움말에 게임 버그로 필요한 로그가 기록되지 않는 경우를 함께 안내합니다.\n"
                    + "- 서버 차단 완료 뒤 메인 화면의 두 줄 상태 안내가 작은 창과 화면 배율에서도 잘리지 않도록 개선했습니다.\n"
                    + "- 메모보관함에서 전체 선택한 첫 행의 체크 표시가 즉시 갱신되도록 수정했습니다.\n"
                    + "- 서버차단현황에서 선택 헤더와 Ctrl+A로 전체 선택하고 선택 개수를 확인할 수 있습니다.\n"
                    + "- 선택 열과 일괄 작업 버튼의 접근성 설명을 정리하고 현재 선택 개수를 화면 읽기 프로그램에도 제공합니다.\n"
                    + "- 메모보관함·서버차단현황의 정보 헤더와 메인 화면 차단·해제 열에 3단계 정렬과 주황색 방향 표시를 추가하고, 메인 정렬 헤더에 동작 안내를 제공하며 두 보조 창의 선택 헤더를 전체 선택 상태 체크박스로 개선했습니다.\n"
                    + "- 차단 목록 백업 파일명에 저장 시간을 포함하고 완료 안내에는 파일명만 표시합니다."
            }
        };

        internal static ReleaseNotesEntry FindBundled(string versionText)
        {
            string normalized = NormalizeVersion(versionText);
            foreach (BundledRelease release in BundledReleases)
            {
                if (!string.Equals(release.Version, normalized, StringComparison.Ordinal))
                    continue;
                return new ReleaseNotesEntry
                {
                    VersionText = release.Version,
                    NotesText = NormalizeNotesText(release.Notes)
                };
            }
            return null;
        }

        internal static string GetDisplayNotes(ReleaseNotesEntry entry)
        {
            if (entry == null) return NormalizeNotesText(null);
            if (AppText.CurrentLanguage == AppText.EnglishLanguage)
            {
                foreach (BundledRelease release in BundledReleases)
                {
                    if (NormalizeVersion(entry.VersionText) == release.Version
                        && NormalizeNotesText(entry.NotesText) == NormalizeNotesText(release.Notes))
                        return NormalizeNotesText(release.EnglishNotes);
                }
            }
            return NormalizeNotesText(entry.NotesText);
        }

        internal static string NormalizeNotesText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return AppText.Get("PatchNotes.Empty");

            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            var builder = new StringBuilder(Math.Min(normalized.Length, MaximumNotesCharacters));
            foreach (char character in normalized)
            {
                if (builder.Length >= MaximumNotesCharacters) break;
                if (character == '\n' || character == '\t' || !char.IsControl(character))
                    builder.Append(character);
            }

            string result = builder.ToString().Trim();
            if (normalized.Length > MaximumNotesCharacters)
                result += "\n\n" + AppText.Get("PatchNotes.Truncated");
            return string.IsNullOrWhiteSpace(result)
                ? AppText.Get("PatchNotes.Empty")
                : result.Replace("\n", "\r\n");
        }

        internal static string NormalizeVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string version = value.Trim();
            if (version.Length > 0 && (version[0] == 'v' || version[0] == 'V'))
                version = version.Substring(1);
            Version parsed;
            if (!Version.TryParse(version, out parsed)
                || parsed.Major < 0
                || parsed.Minor < 0
                || parsed.Build < 0
                || parsed.Revision >= 0)
                return string.Empty;
            return string.Format("{0}.{1}.{2}", parsed.Major, parsed.Minor, parsed.Build);
        }
    }

    internal sealed class UpdateCompletionMarker
    {
        public int SchemaVersion { get; set; }
        public string Version { get; set; }
        public string Evidence { get; set; }
    }

    internal static class UpdateCompletionNotice
    {
        internal const int SchemaVersion = 1;
        internal const string EvidenceValue = "velopack-after-update";
        internal const string PendingFileName = "update-completion-pending.json";
        internal const string ConsumedFileName = "update-completion-consumed.json";
        private const long MaximumMarkerBytes = 4096;

        internal static bool HasPendingNotice()
        {
            try
            {
                return File.Exists(Path.Combine(GetDefaultStorageRoot(), PendingFileName));
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryRecordCurrentCompletedUpdate()
        {
            return TryRecordCompletedUpdate(
                GetDefaultStorageRoot(),
                GetExecutingSemanticVersion());
        }

        internal static bool TryClaimCurrentCompletedUpdate(
            bool demoMode,
            bool installedApplication,
            out ReleaseNotesEntry entry)
        {
            return TryClaimCompletedUpdateEntry(
                GetDefaultStorageRoot(),
                GetExecutingSemanticVersion(),
                demoMode,
                installedApplication,
                out entry);
        }

        internal static bool TryClaimCompletedUpdateEntry(
            string storageRoot,
            string currentVersionText,
            bool demoMode,
            bool installedApplication,
            out ReleaseNotesEntry entry)
        {
            entry = null;
            if (!ShouldConsume(demoMode, installedApplication))
                return false;

            // Do not consume the one-time receipt if this build has no notes
            // to show. The build tests also require the actual app's version.
            ReleaseNotesEntry bundled = ReleaseNotesCatalog.FindBundled(currentVersionText);
            if (bundled == null) return false;
            string claimedVersion;
            if (!TryClaimCompletedUpdate(storageRoot, currentVersionText, out claimedVersion))
                return false;
            entry = bundled;
            return true;
        }

        internal static bool ShouldConsume(
            bool demoMode,
            bool installedApplication)
        {
            return !demoMode && installedApplication;
        }

        internal static bool TryRecordCompletedUpdate(string storageRoot, string versionText)
        {
            string version = ReleaseNotesCatalog.NormalizeVersion(versionText);
            if (string.IsNullOrEmpty(version) || string.IsNullOrWhiteSpace(storageRoot))
                return false;
            try
            {
                string root = Path.GetFullPath(storageRoot);
                Directory.CreateDirectory(root);
                string consumedPath = Path.Combine(root, ConsumedFileName);
                UpdateCompletionMarker consumed;
                if (TryReadMarker(consumedPath, out consumed)
                    && string.Equals(consumed.Version, version, StringComparison.Ordinal))
                {
                    TryDelete(Path.Combine(root, PendingFileName));
                    return true;
                }

                return TryWriteMarkerAtomically(
                    Path.Combine(root, PendingFileName),
                    CreateMarker(version));
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryClaimCompletedUpdate(
            string storageRoot,
            string currentVersionText,
            out string claimedVersion)
        {
            claimedVersion = null;
            string currentVersion = ReleaseNotesCatalog.NormalizeVersion(currentVersionText);
            if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrWhiteSpace(storageRoot))
                return false;

            string claimPath = null;
            try
            {
                string root = Path.GetFullPath(storageRoot);
                string pendingPath = Path.Combine(root, PendingFileName);
                if (!File.Exists(pendingPath)) return false;

                claimPath = Path.Combine(
                    root,
                    "update-completion-claim."
                        + System.Diagnostics.Process.GetCurrentProcess().Id
                        + "."
                        + Guid.NewGuid().ToString("N")
                        + ".json");
                try
                {
                    // File.Move is the cross-process claim. Only the process that
                    // removes the canonical pending path may consider displaying it.
                    File.Move(pendingPath, claimPath);
                }
                catch (IOException)
                {
                    return false;
                }

                UpdateCompletionMarker marker;
                if (!TryReadMarker(claimPath, out marker)
                    || !string.Equals(marker.Version, currentVersion, StringComparison.Ordinal))
                    return false;

                // Persist the receipt before returning the notice. If this fails,
                // the claim is deliberately discarded instead of risking a popup
                // on every launch. This is an at-most-once notification.
                if (!TryWriteMarkerAtomically(
                    Path.Combine(root, ConsumedFileName),
                    CreateMarker(currentVersion)))
                    return false;

                claimedVersion = currentVersion;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                TryDelete(claimPath);
            }
        }

        private static UpdateCompletionMarker CreateMarker(string version)
        {
            return new UpdateCompletionMarker
            {
                SchemaVersion = SchemaVersion,
                Version = version,
                Evidence = EvidenceValue
            };
        }

        private static string GetDefaultStorageRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard");
        }

        private static string GetExecutingSemanticVersion()
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? string.Empty
                : string.Format(
                    "{0}.{1}.{2}",
                    Math.Max(0, version.Major),
                    Math.Max(0, version.Minor),
                    Math.Max(0, version.Build));
        }

        private static bool TryReadMarker(string path, out UpdateCompletionMarker marker)
        {
            marker = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumMarkerBytes)
                    return false;
                string json = File.ReadAllText(path, new UTF8Encoding(false, true));
                var serializer = new JavaScriptSerializer
                {
                    MaxJsonLength = (int)MaximumMarkerBytes,
                    RecursionLimit = 8
                };
                marker = serializer.Deserialize<UpdateCompletionMarker>(json);
                string normalized = marker == null
                    ? string.Empty
                    : ReleaseNotesCatalog.NormalizeVersion(marker.Version);
                return marker != null
                    && marker.SchemaVersion == SchemaVersion
                    && string.Equals(marker.Version, normalized, StringComparison.Ordinal)
                    && string.Equals(marker.Evidence, EvidenceValue, StringComparison.Ordinal);
            }
            catch
            {
                marker = null;
                return false;
            }
        }

        private static bool TryWriteMarkerAtomically(
            string destinationPath,
            UpdateCompletionMarker marker)
        {
            string temporaryPath = null;
            try
            {
                string directory = Path.GetDirectoryName(destinationPath);
                Directory.CreateDirectory(directory);
                temporaryPath = destinationPath + ".tmp." + Guid.NewGuid().ToString("N");
                string json = new JavaScriptSerializer().Serialize(marker);
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                if (bytes.Length <= 0 || bytes.Length > MaximumMarkerBytes) return false;
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }

                if (File.Exists(destinationPath))
                    File.Replace(temporaryPath, destinationPath, null, true);
                else
                    File.Move(temporaryPath, destinationPath);
                temporaryPath = null;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
