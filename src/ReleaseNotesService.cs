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
        }

        private static readonly BundledRelease[] BundledReleases =
        {
            new BundledRelease
            {
                Version = "0.8.2",
                Notes =
                    "- TSG 경로의 직접선택·자동 찾기·적용 버튼 문구가 아래쪽으로 치우쳐 보이지 않도록 세로 위치를 중단에 맞췄습니다.\n"
                    + "- 메모보관함에서 일반 레이드 메모와 유저신고 메모를 하나의 파일로 백업하고 없는 메모만 안전하게 복원할 수 있습니다.\n"
                    + "- 스크린샷 원본 파일은 제외하고 검증된 로컬 이미지 연결 경로만 백업·복원합니다.\n"
                    + "- 메모보관함의 맵 열에 게임유형을 함께 표시하고, 처음 열 때 가로 스크롤이 생기지 않도록 기본 창 폭을 넓혔습니다."
            },
            new BundledRelease
            {
                Version = "0.8.1",
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

        internal static string NormalizeNotesText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "이 버전에는 별도의 변경 사항 내용이 제공되지 않았습니다.";

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
                result += "\n\n[표시할 수 있는 최대 길이를 넘어 나머지 내용은 생략했습니다.]";
            return string.IsNullOrWhiteSpace(result)
                ? "이 버전에는 별도의 변경 사항 내용이 제공되지 않았습니다."
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
            entry = null;
            if (!ShouldConsume(demoMode, installedApplication))
                return false;

            string claimedVersion;
            if (!TryClaimCompletedUpdate(
                GetDefaultStorageRoot(),
                GetExecutingSemanticVersion(),
                out claimedVersion))
                return false;
            entry = ReleaseNotesCatalog.FindBundled(claimedVersion);
            return entry != null;
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
