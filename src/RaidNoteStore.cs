// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    public sealed class RaidNoteRecord
    {
        public RaidNoteRecord()
        {
            ScreenshotPaths = new List<string>();
            Tags = new List<string>();
        }

        public string Key { get; set; }
        public string Game { get; set; }
        public DateTime RaidStartedUtc { get; set; }
        public string MapName { get; set; }
        public string NoteText { get; set; }
        public List<string> ScreenshotPaths { get; set; }
        public List<string> Tags { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>
    /// Stores raid notes outside the game log tree so they survive log rotation/deletion.
    /// File names are SHA-256 keys; raw server IDs and log paths are never persisted as keys.
    /// </summary>
    public sealed class RaidNoteStore
    {
        internal const string LegacyDefaultNoteTemplate = "\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n유저닉네임\r\n1.\r\n2.\r\n3.";
        private const int MaximumJsonLength = 2 * 1024 * 1024;
        private readonly object _sync = new object();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly string _notesFolder;

        public RaidNoteStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TarkovServerGuard",
                "RaidNotes"))
        {
        }

        public RaidNoteStore(string notesFolder)
        {
            if (string.IsNullOrWhiteSpace(notesFolder))
                throw new ArgumentException("메모 저장 폴더가 비어 있습니다.", "notesFolder");
            _notesFolder = Path.GetFullPath(notesFolder);
            _serializer.MaxJsonLength = MaximumJsonLength;
            _serializer.RecursionLimit = 20;
        }

        public string NotesFolder
        {
            get { return _notesFolder; }
        }

        public static string CreateStableKey(ServerSession session)
        {
            if (session == null) throw new ArgumentNullException("session");

            // SessionKey may contain a full local path, so do not persist or depend on it.
            // ServerId can be sensitive; it is used only inside this one-way digest.
            string stableIdentity;
            if (!string.IsNullOrWhiteSpace(session.SessionFolderName))
            {
                // The session folder and its parsed wall-clock timestamp remain unchanged while
                // a live raid later gains an IP, server ID, disconnect record, or reconnect.
                stableIdentity = "folder\n"
                    + NormalizeIdentityPart(session.SessionFolderName) + "\n"
                    + session.SessionStarted.Ticks.ToString(CultureInfo.InvariantCulture);
            }
            else if (session.SessionStarted != default(DateTime))
            {
                stableIdentity = "time\n"
                    + session.SessionStarted.Ticks.ToString(CultureInfo.InvariantCulture) + "\n"
                    + NormalizeIdentityPart(session.ShortId) + "\n"
                    + NormalizeIdentityPart(session.ServerId);
            }
            else
            {
                // Last-resort input may contain a local path, but only its digest is persisted.
                stableIdentity = "opaque\n" + NormalizeIdentityPart(session.SessionKey);
            }

            string canonical = "raid-note-v1\n"
                + ((int)session.Game).ToString(CultureInfo.InvariantCulture) + "\n"
                + stableIdentity;
            return ComputeSha256(canonical);
        }

        public static string CreateStableKey(string opaqueIdentity)
        {
            if (string.IsNullOrWhiteSpace(opaqueIdentity))
                throw new ArgumentException("레이드 식별자가 비어 있습니다.", "opaqueIdentity");
            return ComputeSha256("raid-note-v1\n" + opaqueIdentity.Trim());
        }

        public RaidNoteRecord Load(ServerSession session)
        {
            return Load(CreateStableKey(session));
        }

        public RaidNoteRecord Load(string key)
        {
            ValidateKey(key);
            lock (_sync)
            {
                EnsureFolder();
                string path = GetPath(key);
                RaidNoteRecord record = TryRead(path, key);
                if (record != null) return record;
                return TryRead(GetBackupPath(key), key);
            }
        }

        public IList<RaidNoteRecord> LoadAll()
        {
            lock (_sync)
            {
                EnsureFolder();
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string path in Directory.EnumerateFiles(_notesFolder, "*.json*", SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(path);
                        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                        if (name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                            name = name.Substring(0, name.Length - 4);
                        if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                        string key = name.Substring(0, name.Length - 5);
                        try
                        {
                            ValidateKey(key);
                            keys.Add(key);
                        }
                        catch (ArgumentException)
                        {
                            // Ignore files not created by this store.
                        }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    return new List<RaidNoteRecord>();
                }

                var result = new List<RaidNoteRecord>();
                foreach (string key in keys)
                {
                    RaidNoteRecord record = TryRead(GetPath(key), key)
                        ?? TryRead(GetBackupPath(key), key);
                    if (record != null) result.Add(record);
                }
                return result
                    .OrderByDescending(item => item.RaidStartedUtc)
                    .ThenByDescending(item => item.UpdatedUtc)
                    .ToList();
            }
        }

        public bool Exists(ServerSession session)
        {
            string key = CreateStableKey(session);
            lock (_sync)
            {
                return File.Exists(GetPath(key)) || File.Exists(GetBackupPath(key));
            }
        }

        public bool Exists(string key)
        {
            ValidateKey(key);
            lock (_sync)
            {
                return File.Exists(GetPath(key)) || File.Exists(GetBackupPath(key));
            }
        }

        public RaidNoteRecord CreateFor(ServerSession session)
        {
            if (session == null) throw new ArgumentNullException("session");
            DateTime now = DateTime.UtcNow;
            return new RaidNoteRecord
            {
                Key = CreateStableKey(session),
                Game = session.GameDisplayName,
                RaidStartedUtc = NormalizeDate(session.SessionStarted),
                MapName = Limit(session.MapName, 256),
                CreatedUtc = now,
                UpdatedUtc = now
            };
        }

        public void Save(ServerSession session, RaidNoteRecord record)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (record == null) throw new ArgumentNullException("record");
            string expectedKey = CreateStableKey(session);
            Save(expectedKey, record, session);
        }

        public void Save(string key, RaidNoteRecord record)
        {
            Save(key, record, null);
        }

        public void Delete(ServerSession session)
        {
            Delete(CreateStableKey(session));
        }

        public void Delete(string key)
        {
            ValidateKey(key);
            lock (_sync)
            {
                DeleteForUserRequest(GetPath(key));
                DeleteForUserRequest(GetBackupPath(key));
                DeleteIfPresent(GetTemporaryPath(key));
            }
        }

        public void OpenNotesFolder()
        {
            lock (_sync) EnsureFolder();
            Process.Start("explorer.exe", QuoteArgument(_notesFolder));
        }

        private void Save(string key, RaidNoteRecord record, ServerSession session)
        {
            ValidateKey(key);
            RaidNoteRecord normalized = NormalizeRecord(key, record, session);
            string json = _serializer.Serialize(normalized);
            if (json.Length > MaximumJsonLength)
                throw new InvalidOperationException("메모 데이터가 허용 크기를 초과했습니다.");

            lock (_sync)
            {
                EnsureFolder();
                string target = GetPath(key);
                string backup = GetBackupPath(key);
                string temporary = GetTemporaryPath(key);
                DeleteIfPresent(temporary);
                WriteDurably(temporary, json);
                try
                {
                    if (File.Exists(target))
                        File.Replace(temporary, target, backup, true);
                    else
                        File.Move(temporary, target);
                }
                finally
                {
                    DeleteIfPresent(temporary);
                }
            }

            CopyRecordValues(normalized, record);
        }

        private RaidNoteRecord NormalizeRecord(string key, RaidNoteRecord record, ServerSession session)
        {
            DateTime now = DateTime.UtcNow;
            DateTime created = record.CreatedUtc == default(DateTime) ? now : NormalizeDate(record.CreatedUtc);
            var normalized = new RaidNoteRecord
            {
                Key = key,
                Game = Limit(session == null ? record.Game : session.GameDisplayName, 32),
                RaidStartedUtc = session == null
                    ? NormalizeDate(record.RaidStartedUtc)
                    : NormalizeDate(session.SessionStarted),
                MapName = Limit(session == null ? record.MapName : session.MapName, 256),
                NoteText = Limit(NormalizeLegacyNoteText(record.NoteText), 200000),
                ScreenshotPaths = NormalizePaths(record.ScreenshotPaths, 100),
                Tags = NormalizeList(record.Tags, 100, 64, true),
                CreatedUtc = created,
                UpdatedUtc = now
            };
            return normalized;
        }

        private RaidNoteRecord TryRead(string path, string expectedKey)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumJsonLength * 2L) return null;
                string json;
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    json = reader.ReadToEnd();
                }
                RaidNoteRecord record = _serializer.Deserialize<RaidNoteRecord>(json);
                if (record == null || !string.Equals(record.Key, expectedKey, StringComparison.OrdinalIgnoreCase))
                    return null;
                record.ScreenshotPaths = record.ScreenshotPaths ?? new List<string>();
                record.Tags = record.Tags ?? new List<string>();
                record.NoteText = NormalizeLegacyNoteText(record.NoteText);
                return record;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> NormalizeList(
            IEnumerable<string> values,
            int maximumItems,
            int maximumLength,
            bool ignoreCase)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(ignoreCase
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
            if (values == null) return result;
            foreach (string value in values)
            {
                string normalized = Limit(value == null ? null : value.Trim(), maximumLength);
                if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized)) continue;
                result.Add(normalized);
                if (result.Count >= maximumItems) break;
            }
            return result;
        }

        private static List<string> NormalizePaths(IEnumerable<string> values, int maximumItems)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null) return result;
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)) continue;
                string path;
                try { path = Path.GetFullPath(value.Trim().Trim('"')); }
                catch { continue; }
                if (!seen.Add(path)) continue;
                result.Add(Limit(path, 2048));
                if (result.Count >= maximumItems) break;
            }
            return result;
        }

        private static void CopyRecordValues(RaidNoteRecord source, RaidNoteRecord destination)
        {
            destination.Key = source.Key;
            destination.Game = source.Game;
            destination.RaidStartedUtc = source.RaidStartedUtc;
            destination.MapName = source.MapName;
            destination.NoteText = source.NoteText;
            destination.ScreenshotPaths = new List<string>(source.ScreenshotPaths);
            destination.Tags = new List<string>(source.Tags);
            destination.CreatedUtc = source.CreatedUtc;
            destination.UpdatedUtc = source.UpdatedUtc;
        }

        private static string Limit(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        internal static string NormalizeLegacyNoteText(string value)
        {
            return string.Equals(value ?? string.Empty, LegacyDefaultNoteTemplate,
                StringComparison.Ordinal) ? string.Empty : (value ?? string.Empty);
        }

        private static DateTime NormalizeDate(DateTime value)
        {
            if (value == default(DateTime)) return default(DateTime);
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        }

        private static string NormalizeIdentityPart(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }

        private static string ComputeSha256(string value)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest) builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length != 64
                || key.Any(character => !((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')
                    || (character >= 'A' && character <= 'F'))))
                throw new ArgumentException("유효하지 않은 메모 키입니다.", "key");
        }

        private string GetPath(string key)
        {
            return Path.Combine(_notesFolder, key.ToLowerInvariant() + ".json");
        }

        private string GetBackupPath(string key)
        {
            return GetPath(key) + ".bak";
        }

        private string GetTemporaryPath(string key)
        {
            return GetPath(key) + ".tmp";
        }

        private void EnsureFolder()
        {
            if (!Directory.Exists(_notesFolder)) Directory.CreateDirectory(_notesFolder);
        }

        private static void WriteDurably(string path, string content)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static void DeleteIfPresent(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Cleanup must not hide the original load/save result.
            }
        }

        private static void DeleteForUserRequest(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", string.Empty) + "\"";
        }
    }
}
