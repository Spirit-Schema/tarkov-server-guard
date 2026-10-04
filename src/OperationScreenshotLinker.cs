// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TarkovServerReporter
{
    /// <summary>
    /// Links original local screenshots only when their capture timestamp belongs to
    /// exactly one observed raid interval. File creation/write time is never capture
    /// evidence: a copied or edited image must not silently attach to another raid.
    /// </summary>
    public sealed class OperationScreenshotLinker
    {
        private static readonly Regex NvidiaTimestamp = new Regex(
            @"(?<!\d)(?<year>20\d{2})\.(?<month>\d{2})\.(?<day>\d{2})\s*-\s*(?<hour>\d{2})\.(?<minute>\d{2})\.(?<second>\d{2})(?:\.(?<fraction>\d{2,3}))?(?!\d)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex StandardTimestamp = new Regex(
            @"(?<!\d)(?<year>20\d{2})[-_](?<month>\d{2})[-_](?<day>\d{2})[ T_\[](?<hour>\d{2})[-_:](?<minute>\d{2})[-_:](?<second>\d{2})(?!\d)",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex CompactCaptureTimestamp = new Regex(
            @"^(?<year>20\d{2})(?<month>\d{2})(?<day>\d{2})(?<hour>\d{2})(?<minute>\d{2})(?<second>\d{2})(?:_\d+)?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static IList<string> DiscoverDefaultFolders()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            AddExisting(result, Path.Combine(documents, "Escape from Tarkov", "Screenshots"));
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(user, "Videos"));
            roots.Add(Path.Combine(user, "Pictures"));
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrWhiteSpace(oneDrive))
            {
                roots.Add(Path.Combine(oneDrive, "Videos"));
                roots.Add(Path.Combine(oneDrive, "Pictures"));
            }
            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { root, Path.Combine(root, "NVIDIA"), Path.Combine(root, "NVIDIA App"),
                    Path.Combine(root, "Captures"), Path.Combine(root, "Desktop") };
                try
                {
                    if (Directory.Exists(root))
                        foreach (string parent in Directory.EnumerateDirectories(root, "NVIDIA*", SearchOption.TopDirectoryOnly).Take(20))
                            parents.Add(parent);
                }
                catch { }
                foreach (string parent in parents)
                {
                    AddExisting(result, Path.Combine(parent, "Escape From Tarkov"));
                    AddExisting(result, Path.Combine(parent, "EscapeFromTarkov"));
                    AddExisting(result, Path.Combine(parent, "Escape from Tarkov", "Screenshots"));
                }
            }
            return result.ToList();
        }

        public IList<string> FindForRaid(ServerSession target, IEnumerable<ServerSession> knownRaids,
            IEnumerable<string> screenshotFolders)
        {
            DateTime startUtc, endUtc;
            if (!OperationNoteStore.TryGetCompletedInterval(target, out startUtc, out endUtc)) return new List<string>();
            string targetKey = RaidNoteStore.CreateStableKey(target);
            List<ServerSession> sessions = (knownRaids ?? new[] { target })
                .Where(item => item != null && item.Game == TarkovGame.Eft)
                .GroupBy(RaidNoteStore.CreateStableKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.LastUpdated).First()).ToList();
            var result = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in screenshotFolders ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string[] paths;
                try
                {
                    // OneDrive capture folders/files use cloud reparse points too.
                    // Reading the explicit folder is safe; never recurse into children.
                    if (!Directory.Exists(folder)) continue;
                    // Bound work to the explicitly known capture folder; no drive scans.
                    paths = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                        .Where(IsSupportedImage).Take(20000).ToArray();
                }
                catch { continue; }
                foreach (string path in paths)
                {
                    try
                    {
                        if (!visited.Add(Path.GetFullPath(path))
                            || !RaidNoteStore.IsSafeScreenshotAttachmentPath(path)) continue;
                        DateTime captureUtc;
                        if (!TryGetCaptureUtc(path, out captureUtc)) continue;
                        // One second covers filenames/EXIF that omit subsecond precision.
                        if (captureUtc <= startUtc.AddSeconds(1) || captureUtc >= endUtc.AddSeconds(-1)) continue;
                        var matching = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (ServerSession session in sessions)
                        {
                            if (!session.OperationStartedAt.HasValue) continue;
                            DateTime begin = OperationNoteStore.ToUtc(session.OperationStartedAt.Value);
                            DateTime end = session.OperationEndedAt.HasValue
                                ? OperationNoteStore.ToUtc(session.OperationEndedAt.Value) : DateTime.UtcNow;
                            if (captureUtc >= begin.AddSeconds(-1) && captureUtc <= end.AddSeconds(1))
                                matching.Add(RaidNoteStore.CreateStableKey(session));
                        }
                        if (matching.Count != 1 || !matching.Contains(targetKey)) continue;
                        result.Add(Path.GetFullPath(path));
                        if (result.Count >= RaidNoteStore.MaximumScreenshotPathCount) return result;
                    }
                    catch { /* An unavailable/invalid image never blocks a note save. */ }
                }
            }
            return result;
        }

        public static bool TryGetCaptureUtc(string path, out DateTime captureUtc)
        {
            captureUtc = default(DateTime);
            try
            {
                if (!File.Exists(path) || !IsSupportedImage(path)) return false;
                DateTime filenameUtc;
                bool hasFilename = TryParseCaptureFileName(path, out filenameUtc);
                DateTime exifUtc;
                bool hasExif = TryReadExifUtc(path, out exifUtc);
                if (hasFilename && hasExif && Math.Abs((filenameUtc - exifUtc).TotalSeconds) > 2) return false;
                if (!hasFilename && !hasExif) return false;
                captureUtc = hasExif ? exifUtc : filenameUtc;
                return captureUtc <= DateTime.UtcNow.AddMinutes(1);
            }
            catch { return false; }
        }

        internal static bool TryParseCaptureFileName(string path, out DateTime captureUtc)
        {
            captureUtc = default(DateTime);
            string name = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            string folder = Path.GetDirectoryName(path) ?? string.Empty;
            // Compact capture names also occur in Steam's numeric app-id folders.
            // FindForRaid only enumerates detected EFT folders or user-added sources.
            Match match = CompactCaptureTimestamp.Match(name);
            if (!match.Success)
            {
                if (!IsTarkovName(name) && !IsTarkovName(folder)) return false;
                match = NvidiaTimestamp.Match(name);
                if (!match.Success) match = StandardTimestamp.Match(name);
            }
            if (!match.Success) return false;
            try
            {
                DateTime local = new DateTime(Parse(match, "year"), Parse(match, "month"), Parse(match, "day"),
                    Parse(match, "hour"), Parse(match, "minute"), Parse(match, "second"), DateTimeKind.Unspecified);
                string fraction = match.Groups["fraction"].Value;
                if (!string.IsNullOrEmpty(fraction)) local = local.AddMilliseconds(int.Parse(fraction.PadRight(3, '0'), CultureInfo.InvariantCulture));
                return TryLocalToUtc(local, out captureUtc);
            }
            catch { return false; }
        }

        private static bool TryReadExifUtc(string path, out DateTime captureUtc)
        {
            captureUtc = default(DateTime);
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                if (new FileInfo(path).Length > 50L * 1024 * 1024) return false;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (Image image = Image.FromStream(stream, false, false))
                {
                    if (!image.PropertyIdList.Contains(0x9003)) return false;
                    string text = Encoding.ASCII.GetString(image.GetPropertyItem(0x9003).Value).TrimEnd('\0');
                    DateTime local;
                    if (!DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out local)) return false;
                    if (image.PropertyIdList.Contains(0x9011))
                    {
                        string offset = Encoding.ASCII.GetString(image.GetPropertyItem(0x9011).Value).TrimEnd('\0');
                        DateTimeOffset precise;
                        if (!DateTimeOffset.TryParseExact(text + " " + offset, "yyyy:MM:dd HH:mm:ss zzz",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out precise)) return false;
                        captureUtc = precise.UtcDateTime;
                        return true;
                    }
                    return TryLocalToUtc(local, out captureUtc);
                }
            }
            catch { return false; }
        }

        private static bool TryLocalToUtc(DateTime local, out DateTime utc)
        {
            utc = default(DateTime);
            if (TimeZoneInfo.Local.IsAmbiguousTime(local) || TimeZoneInfo.Local.IsInvalidTime(local)) return false;
            utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);
            return true;
        }

        private static int Parse(Match match, string name) { return int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture); }
        private static bool IsTarkovName(string text) { return text.IndexOf("tarkov", StringComparison.OrdinalIgnoreCase) >= 0; }
        private static bool IsSupportedImage(string path)
        {
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jxr", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
        }
        private static void AddExisting(HashSet<string> paths, string path)
        {
            try { if (Directory.Exists(path)) paths.Add(Path.GetFullPath(path)); } catch { }
        }
    }
}
