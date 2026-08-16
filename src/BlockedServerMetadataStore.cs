// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    public sealed class BlockedServerMetadata
    {
        public string IpAddress { get; set; }
        public string DataCenter { get; set; }
        public string Location { get; set; }
        public string Note { get; set; }
        public DateTime? BlockedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public string DataCenterLocationText
        {
            get
            {
                bool hasDataCenter = !string.IsNullOrWhiteSpace(DataCenter);
                bool hasLocation = !string.IsNullOrWhiteSpace(Location);
                if (hasDataCenter && hasLocation) return DataCenter + " · " + Location;
                if (hasDataCenter) return DataCenter;
                if (hasLocation) return Location;
                return "-";
            }
        }

        public string BlockedAtText
        {
            get
            {
                return BlockedAtUtc.HasValue
                    ? BlockedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
                    : "확인 안 됨";
            }
        }
    }

    public static class BlockedServerMetadataStore
    {
        private enum MetadataFileLoadState
        {
            Missing,
            Valid,
            Invalid
        }

        private sealed class MetadataDocument
        {
            public int Version { get; set; }
            public List<MetadataItem> Items { get; set; }
        }

        private sealed class MetadataItem
        {
            public string Ip { get; set; }
            public string DataCenter { get; set; }
            public string Location { get; set; }
            public string Note { get; set; }
            public string BlockedAtUtc { get; set; }
            public string UpdatedAtUtc { get; set; }
        }

        private const int CurrentVersion = 1;
        public const int MaximumNoteLength = 300;
        private const int MaximumRecordCount = 4096;
        private const int MaximumFileBytes = 2 * 1024 * 1024;
        private static readonly object SyncRoot = new object();

        private static readonly string StoreDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TarkovServerGuard");
        private static readonly string StorePath = Path.Combine(
            StoreDirectory,
            "blocked-server-metadata.json");
        private static readonly string BackupPath = StorePath + ".bak";

        public static IDictionary<string, BlockedServerMetadata> LoadAll()
        {
            lock (SyncRoot)
            {
                Dictionary<string, BlockedServerMetadata> records;
                if (LoadFile(StorePath, out records) == MetadataFileLoadState.Valid)
                    return CloneRecords(records);
                if (LoadFile(BackupPath, out records) == MetadataFileLoadState.Valid)
                    return CloneRecords(records);
                return new Dictionary<string, BlockedServerMetadata>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static bool UpdateLocation(string ipAddress, string dataCenter, string location)
        {
            return Upsert(ipAddress, dataCenter, location, null);
        }

        public static bool MarkBlocked(string ipAddress, string dataCenter, string location)
        {
            return Upsert(ipAddress, dataCenter, location, DateTime.UtcNow);
        }

        public static bool UpdateNote(string ipAddress, string note)
        {
            if (!FirewallRuleManager.IsValidIpv4(ipAddress)) return false;
            string safeNote = NormalizeNote(note);

            lock (SyncRoot)
            {
                try
                {
                    Dictionary<string, BlockedServerMetadata> records;
                    if (!TryLoadForMutation(out records)) return false;
                    BlockedServerMetadata metadata;
                    if (!records.TryGetValue(ipAddress, out metadata))
                    {
                        if (safeNote == null) return true;
                        metadata = new BlockedServerMetadata { IpAddress = ipAddress };
                        records[ipAddress] = metadata;
                    }

                    metadata.Note = safeNote;
                    metadata.UpdatedAtUtc = DateTime.UtcNow;
                    return SaveCore(records);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool Upsert(
            string ipAddress,
            string dataCenter,
            string location,
            DateTime? blockedAtUtc)
        {
            if (!FirewallRuleManager.IsValidIpv4(ipAddress)) return false;
            string safeDataCenter = NormalizeText(dataCenter, 80);
            string safeLocation = NormalizeText(location, 180);

            lock (SyncRoot)
            {
                try
                {
                    Dictionary<string, BlockedServerMetadata> records;
                    if (!TryLoadForMutation(out records)) return false;
                    BlockedServerMetadata metadata;
                    if (!records.TryGetValue(ipAddress, out metadata))
                    {
                        metadata = new BlockedServerMetadata { IpAddress = ipAddress };
                        records[ipAddress] = metadata;
                    }

                    if (!string.IsNullOrWhiteSpace(safeDataCenter))
                        metadata.DataCenter = safeDataCenter;
                    if (!string.IsNullOrWhiteSpace(safeLocation))
                        metadata.Location = safeLocation;
                    if (blockedAtUtc.HasValue)
                        metadata.BlockedAtUtc = NormalizeUtc(blockedAtUtc.Value);
                    metadata.UpdatedAtUtc = DateTime.UtcNow;
                    return SaveCore(records);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool MergeMissingFromBackup(IEnumerable<BlockedServerBackupEntry> entries)
        {
            if (entries == null) return true;
            var candidates = entries.ToList();
            if (candidates.Count > MaximumRecordCount) return false;
            foreach (BlockedServerBackupEntry entry in candidates)
            {
                if (entry == null || !FirewallRuleManager.IsPublicIpv4(entry.IpAddress))
                    return false;
            }

            lock (SyncRoot)
            {
                try
                {
                    Dictionary<string, BlockedServerMetadata> records;
                    if (!TryLoadForMutation(out records)) return false;
                    bool changed = false;
                    foreach (BlockedServerBackupEntry entry in candidates)
                    {
                        string dataCenter = NormalizeText(entry.DataCenter, 80);
                        string location = NormalizeText(entry.Location, 180);
                        string note = NormalizeNote(entry.Note);
                        DateTime? blockedAtUtc = entry.BlockedAtUtc.HasValue
                            ? (DateTime?)NormalizeUtc(entry.BlockedAtUtc.Value)
                            : null;
                        if (dataCenter == null && location == null && note == null
                            && !blockedAtUtc.HasValue)
                            continue;

                        BlockedServerMetadata metadata;
                        if (!records.TryGetValue(entry.IpAddress, out metadata))
                        {
                            metadata = new BlockedServerMetadata { IpAddress = entry.IpAddress };
                            records[entry.IpAddress] = metadata;
                        }

                        bool itemChanged = false;
                        if (string.IsNullOrWhiteSpace(metadata.DataCenter) && dataCenter != null)
                        {
                            metadata.DataCenter = dataCenter;
                            itemChanged = true;
                        }
                        if (string.IsNullOrWhiteSpace(metadata.Location) && location != null)
                        {
                            metadata.Location = location;
                            itemChanged = true;
                        }
                        if (string.IsNullOrWhiteSpace(metadata.Note) && note != null)
                        {
                            metadata.Note = note;
                            itemChanged = true;
                        }
                        if (!metadata.BlockedAtUtc.HasValue && blockedAtUtc.HasValue)
                        {
                            metadata.BlockedAtUtc = blockedAtUtc;
                            itemChanged = true;
                        }
                        if (!itemChanged) continue;

                        metadata.UpdatedAtUtc = DateTime.UtcNow;
                        changed = true;
                    }
                    return !changed || SaveCore(records);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool Remove(IEnumerable<string> ipAddresses)
        {
            if (ipAddresses == null) return true;
            string[] addresses = ipAddresses
                .Where(FirewallRuleManager.IsValidIpv4)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (addresses.Length == 0) return true;

            lock (SyncRoot)
            {
                try
                {
                    Dictionary<string, BlockedServerMetadata> records;
                    if (!TryLoadForMutation(out records)) return false;
                    bool changed = false;
                    foreach (string ipAddress in addresses)
                        changed |= records.Remove(ipAddress);
                    return !changed || SaveCore(records);
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool TryLoadForMutation(
            out Dictionary<string, BlockedServerMetadata> records)
        {
            MetadataFileLoadState primaryState = LoadFile(StorePath, out records);
            if (primaryState == MetadataFileLoadState.Valid) return true;

            Dictionary<string, BlockedServerMetadata> backupRecords;
            MetadataFileLoadState backupState = LoadFile(BackupPath, out backupRecords);
            if (backupState == MetadataFileLoadState.Valid)
            {
                records = backupRecords;
                return true;
            }

            if (primaryState == MetadataFileLoadState.Missing
                && backupState == MetadataFileLoadState.Missing)
            {
                records = new Dictionary<string, BlockedServerMetadata>(
                    StringComparer.OrdinalIgnoreCase);
                return true;
            }

            // Existing metadata that cannot be validated must never be replaced by
            // a newly-created partial document. The caller can report failure while
            // leaving both recovery artifacts byte-for-byte untouched.
            records = null;
            return false;
        }

        private static MetadataFileLoadState LoadFile(
            string path,
            out Dictionary<string, BlockedServerMetadata> records)
        {
            records = null;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                    return Directory.Exists(path)
                        ? MetadataFileLoadState.Invalid
                        : MetadataFileLoadState.Missing;
                if (info.Length < 2 || info.Length > MaximumFileBytes)
                    return MetadataFileLoadState.Invalid;

                byte[] bytes;
                using (var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                {
                    if (stream.Length < 2 || stream.Length > MaximumFileBytes)
                        return MetadataFileLoadState.Invalid;
                    bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) return MetadataFileLoadState.Invalid;
                        offset += read;
                    }
                    if (stream.ReadByte() >= 0) return MetadataFileLoadState.Invalid;
                }
                string json = new UTF8Encoding(false, true).GetString(bytes);
                if (json.Length > 0 && json[0] == '\ufeff') json = json.Substring(1);

                var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };
                MetadataDocument document = serializer.Deserialize<MetadataDocument>(json);
                if (document == null || document.Version != CurrentVersion || document.Items == null
                    || document.Items.Count > MaximumRecordCount)
                    return MetadataFileLoadState.Invalid;

                var parsed = new Dictionary<string, BlockedServerMetadata>(StringComparer.OrdinalIgnoreCase);
                foreach (MetadataItem item in document.Items)
                {
                    if (item == null
                        || !FirewallRuleManager.IsValidIpv4(item.Ip)
                        || parsed.ContainsKey(item.Ip))
                        return MetadataFileLoadState.Invalid;
                    DateTime updatedAt;
                    if (!TryParseUtc(item.UpdatedAtUtc, out updatedAt))
                        return MetadataFileLoadState.Invalid;
                    DateTime? nullableBlockedAt = null;
                    if (item.BlockedAtUtc != null)
                    {
                        DateTime blockedAt;
                        if (!TryParseUtc(item.BlockedAtUtc, out blockedAt))
                            return MetadataFileLoadState.Invalid;
                        nullableBlockedAt = blockedAt;
                    }
                    parsed[item.Ip] = new BlockedServerMetadata
                    {
                        IpAddress = item.Ip,
                        DataCenter = NormalizeText(item.DataCenter, 80),
                        Location = NormalizeText(item.Location, 180),
                        Note = NormalizeNote(item.Note),
                        BlockedAtUtc = nullableBlockedAt,
                        UpdatedAtUtc = updatedAt
                    };
                }
                records = parsed;
                return MetadataFileLoadState.Valid;
            }
            catch
            {
                return MetadataFileLoadState.Invalid;
            }
        }

        private static bool SaveCore(IDictionary<string, BlockedServerMetadata> records)
        {
            if (records == null || records.Count > MaximumRecordCount) return false;
            if (records.Values.Any(item => item == null
                || !FirewallRuleManager.IsValidIpv4(item.IpAddress))) return false;
            if (!Directory.Exists(StoreDirectory)) Directory.CreateDirectory(StoreDirectory);

            var document = new MetadataDocument
            {
                Version = CurrentVersion,
                Items = records.Values
                    .OrderBy(item => item.IpAddress, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new MetadataItem
                    {
                        Ip = item.IpAddress,
                        DataCenter = NormalizeText(item.DataCenter, 80),
                        Location = NormalizeText(item.Location, 180),
                        Note = NormalizeNote(item.Note),
                        BlockedAtUtc = FormatUtc(item.BlockedAtUtc),
                        UpdatedAtUtc = FormatUtc(item.UpdatedAtUtc == default(DateTime)
                            ? DateTime.UtcNow
                            : item.UpdatedAtUtc)
                    })
                    .ToList()
            };

            var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumFileBytes };
            string json = serializer.Serialize(document);
            if (Encoding.UTF8.GetByteCount(json) > MaximumFileBytes) return false;

            string temporaryPath = StorePath + ".tmp." + Guid.NewGuid().ToString("N");
            string backupTemporaryPath = BackupPath + ".tmp." + Guid.NewGuid().ToString("N");
            string replacedPath = StorePath + ".previous." + Guid.NewGuid().ToString("N");
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                WriteNewFile(temporaryPath, bytes);

                if (File.Exists(StorePath))
                {
                    // Keep the durable backup untouched until the new primary is committed.
                    File.Replace(temporaryPath, StorePath, replacedPath, true);
                }
                else
                {
                    File.Move(temporaryPath, StorePath);
                }

                WriteNewFile(backupTemporaryPath, bytes);
                if (File.Exists(BackupPath))
                    File.Replace(backupTemporaryPath, BackupPath, null, true);
                else
                    File.Move(backupTemporaryPath, BackupPath);
                return true;
            }
            finally
            {
                DeleteTemporaryFile(temporaryPath);
                DeleteTemporaryFile(backupTemporaryPath);
                DeleteTemporaryFile(replacedPath);
            }
        }

        private static void WriteNewFile(string path, byte[] bytes)
        {
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }

        private static void DeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Stale temporary files are never considered metadata.
            }
        }

        private static IDictionary<string, BlockedServerMetadata> CloneRecords(
            IDictionary<string, BlockedServerMetadata> source)
        {
            return source.ToDictionary(
                pair => pair.Key,
                pair => new BlockedServerMetadata
                {
                    IpAddress = pair.Value.IpAddress,
                    DataCenter = pair.Value.DataCenter,
                    Location = pair.Value.Location,
                    Note = pair.Value.Note,
                    BlockedAtUtc = pair.Value.BlockedAtUtc,
                    UpdatedAtUtc = pair.Value.UpdatedAtUtc
                },
                StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizeText(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
            foreach (char character in value.Trim())
            {
                if (builder.Length >= maximumLength) break;
                if (!char.IsControl(character)) builder.Append(character);
            }
            string normalized = builder.ToString().Trim();
            if (normalized == "-" || string.Equals(normalized, "확인 안 됨", StringComparison.Ordinal))
                return null;
            return normalized.Length == 0 ? null : normalized;
        }

        private static string NormalizeNote(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var builder = new StringBuilder(Math.Min(value.Length, MaximumNoteLength));
            string trimmed = value.Trim();
            for (int index = 0; index < trimmed.Length; index++)
            {
                char character = trimmed[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= trimmed.Length || !char.IsLowSurrogate(trimmed[index + 1]))
                        continue;
                    if (builder.Length + 2 > MaximumNoteLength) break;
                    builder.Append(character);
                    builder.Append(trimmed[++index]);
                    continue;
                }
                if (char.IsLowSurrogate(character)) continue;
                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (character == '\r' || character == '\n' || character == '\t'
                    || category == UnicodeCategory.LineSeparator
                    || category == UnicodeCategory.ParagraphSeparator)
                {
                    if (builder.Length > 0
                        && builder[builder.Length - 1] != ' '
                        && builder.Length < MaximumNoteLength)
                        builder.Append(' ');
                    continue;
                }
                if (char.IsControl(character))
                    continue;
                if (builder.Length >= MaximumNoteLength) break;
                builder.Append(character);
            }
            string normalized = builder.ToString().Trim();
            return normalized.Length == 0 ? null : normalized;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc) return value;
            if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        private static string FormatUtc(DateTime? value)
        {
            return value.HasValue
                ? NormalizeUtc(value.Value).ToString("o", CultureInfo.InvariantCulture)
                : null;
        }

        private static bool TryParseUtc(string value, out DateTime parsed)
        {
            return DateTime.TryParseExact(
                value,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed);
        }
    }
}
