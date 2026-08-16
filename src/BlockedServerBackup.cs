// Copyright © 2026 Spirit-Schema. All rights reserved.
// Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace TarkovServerReporter
{
    public sealed class BlockedServerBackupEntry
    {
        public string IpAddress { get; set; }
        public string DataCenter { get; set; }
        public string Location { get; set; }
        public string Note { get; set; }
        public DateTime? BlockedAtUtc { get; set; }

        public bool HasMetadata
        {
            get
            {
                return !string.IsNullOrWhiteSpace(DataCenter)
                    || !string.IsNullOrWhiteSpace(Location)
                    || !string.IsNullOrWhiteSpace(Note)
                    || BlockedAtUtc.HasValue;
            }
        }
    }

    public sealed class BlockedServerBackupExportResult
    {
        public BlockedServerBackupExportResult()
        {
            Entries = new List<BlockedServerBackupEntry>();
            ExcludedAddresses = new List<string>();
        }

        public bool Success { get; set; }
        public byte[] Utf8Bytes { get; set; }
        public string ErrorMessage { get; set; }
        public IList<BlockedServerBackupEntry> Entries { get; set; }
        public IList<string> ExcludedAddresses { get; set; }
    }

    public sealed class BlockedServerBackupParsedItem
    {
        public int SourceIndex { get; set; }
        public string SourceIpAddress { get; set; }
        public BlockedServerBackupEntry Entry { get; set; }
        public string ExclusionReason { get; set; }

        public bool IsEligible
        {
            get { return Entry != null && string.IsNullOrWhiteSpace(ExclusionReason); }
        }
    }

    public sealed class BlockedServerBackupParseResult
    {
        public BlockedServerBackupParseResult()
        {
            Items = new List<BlockedServerBackupParsedItem>();
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public IList<BlockedServerBackupParsedItem> Items { get; set; }
    }

    public enum BlockedServerRestoreStatus
    {
        NewBlock,
        AlreadyBlocked,
        Excluded
    }

    public sealed class BlockedServerRestoreItem
    {
        public int SourceIndex { get; set; }
        public string SourceIpAddress { get; set; }
        public BlockedServerBackupEntry Entry { get; set; }
        public BlockedServerRestoreStatus Status { get; set; }
        public string Detail { get; set; }

        public string IpAddress
        {
            get
            {
                if (Entry != null && !string.IsNullOrWhiteSpace(Entry.IpAddress))
                    return Entry.IpAddress;
                return string.IsNullOrWhiteSpace(SourceIpAddress) ? "-" : SourceIpAddress;
            }
        }

        public bool HasMetadata
        {
            get { return Entry != null && Entry.HasMetadata; }
        }
    }

    public static class BlockedServerBackupService
    {
        private sealed class BackupDocument
        {
            public string Format { get; set; }
            public int Version { get; set; }
            public List<BackupItem> Items { get; set; }
        }

        private sealed class BackupItem
        {
            public string Ip { get; set; }
            public string DataCenter { get; set; }
            public string Location { get; set; }
            public string Note { get; set; }
            public string BlockedAtUtc { get; set; }
        }

        private sealed class JsonMemberUniquenessReader
        {
            private readonly string _json;
            private int _index;

            internal JsonMemberUniquenessReader(string json)
            {
                _json = json ?? string.Empty;
            }

            internal bool DuplicateMemberFound { get; private set; }

            internal bool ReadDocument()
            {
                SkipWhitespace();
                if (!ReadValue(0)) return false;
                SkipWhitespace();
                return _index == _json.Length;
            }

            private bool ReadValue(int depth)
            {
                if (depth > 16) return false;
                SkipWhitespace();
                if (_index >= _json.Length) return false;
                char token = _json[_index];
                if (token == '{') return ReadObject(depth + 1);
                if (token == '[') return ReadArray(depth + 1);
                if (token == '"')
                {
                    string ignored;
                    return TryReadString(out ignored);
                }
                if (token == 't') return TryReadLiteral("true");
                if (token == 'f') return TryReadLiteral("false");
                if (token == 'n') return TryReadLiteral("null");
                return token == '-' || IsDigit(token) ? ReadNumber() : false;
            }

            private bool ReadObject(int depth)
            {
                if (depth > 16 || !TryReadCharacter('{')) return false;
                SkipWhitespace();
                if (TryReadCharacter('}')) return true;

                var members = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    string member;
                    if (!TryReadString(out member)) return false;
                    if (!members.Add(member))
                    {
                        DuplicateMemberFound = true;
                        return false;
                    }
                    SkipWhitespace();
                    if (!TryReadCharacter(':') || !ReadValue(depth)) return false;
                    SkipWhitespace();
                    if (TryReadCharacter('}')) return true;
                    if (!TryReadCharacter(',')) return false;
                    SkipWhitespace();
                }
            }

            private bool ReadArray(int depth)
            {
                if (depth > 16 || !TryReadCharacter('[')) return false;
                SkipWhitespace();
                if (TryReadCharacter(']')) return true;
                while (true)
                {
                    if (!ReadValue(depth)) return false;
                    SkipWhitespace();
                    if (TryReadCharacter(']')) return true;
                    if (!TryReadCharacter(',')) return false;
                    SkipWhitespace();
                }
            }

            private bool TryReadString(out string value)
            {
                value = null;
                if (!TryReadCharacter('"')) return false;
                var builder = new StringBuilder();
                while (_index < _json.Length)
                {
                    char character = _json[_index++];
                    if (character == '"')
                    {
                        value = builder.ToString();
                        return true;
                    }
                    if (character < 0x20) return false;
                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (_index >= _json.Length) return false;
                    char escape = _json[_index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            int codePoint = 0;
                            for (int offset = 0; offset < 4; offset++)
                            {
                                if (_index >= _json.Length) return false;
                                int digit = HexValue(_json[_index++]);
                                if (digit < 0) return false;
                                codePoint = (codePoint << 4) | digit;
                            }
                            builder.Append((char)codePoint);
                            break;
                        default:
                            return false;
                    }
                }
                return false;
            }

            private bool ReadNumber()
            {
                if (TryReadCharacter('-') && _index >= _json.Length) return false;
                if (_index >= _json.Length) return false;
                if (_json[_index] == '0')
                {
                    _index++;
                }
                else
                {
                    if (_json[_index] < '1' || _json[_index] > '9') return false;
                    while (_index < _json.Length && IsDigit(_json[_index])) _index++;
                }

                if (_index < _json.Length && _json[_index] == '.')
                {
                    _index++;
                    if (_index >= _json.Length || !IsDigit(_json[_index])) return false;
                    while (_index < _json.Length && IsDigit(_json[_index])) _index++;
                }
                if (_index < _json.Length
                    && (_json[_index] == 'e' || _json[_index] == 'E'))
                {
                    _index++;
                    if (_index < _json.Length
                        && (_json[_index] == '+' || _json[_index] == '-')) _index++;
                    if (_index >= _json.Length || !IsDigit(_json[_index])) return false;
                    while (_index < _json.Length && IsDigit(_json[_index])) _index++;
                }
                return true;
            }

            private bool TryReadLiteral(string literal)
            {
                if (_index + literal.Length > _json.Length) return false;
                for (int offset = 0; offset < literal.Length; offset++)
                {
                    if (_json[_index + offset] != literal[offset]) return false;
                }
                _index += literal.Length;
                return true;
            }

            private bool TryReadCharacter(char expected)
            {
                if (_index >= _json.Length || _json[_index] != expected) return false;
                _index++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_index < _json.Length)
                {
                    char character = _json[_index];
                    if (character != ' ' && character != '\t'
                        && character != '\r' && character != '\n') return;
                    _index++;
                }
            }

            private static bool IsDigit(char character)
            {
                return character >= '0' && character <= '9';
            }

            private static int HexValue(char character)
            {
                if (character >= '0' && character <= '9') return character - '0';
                if (character >= 'a' && character <= 'f') return character - 'a' + 10;
                if (character >= 'A' && character <= 'F') return character - 'A' + 10;
                return -1;
            }
        }

        public const string FormatName = "TarkovServerGuard.BlockedServers";
        public const int CurrentVersion = 1;
        public const int MaximumFileBytes = 2 * 1024 * 1024;
        public const int MaximumItemCount = 1024;
        private const int MaximumDataCenterLength = 80;
        private const int MaximumLocationLength = 180;

        private static readonly HashSet<string> TopLevelFields = new HashSet<string>(
            new[] { "Format", "Version", "Items" },
            StringComparer.Ordinal);
        private static readonly HashSet<string> ItemFields = new HashSet<string>(
            new[] { "Ip", "DataCenter", "Location", "Note", "BlockedAtUtc" },
            StringComparer.Ordinal);

        public static BlockedServerBackupExportResult CreateExport(
            IEnumerable<ManagedBlockedServer> managedServers,
            IDictionary<string, BlockedServerMetadata> metadataByIp)
        {
            var result = new BlockedServerBackupExportResult();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<BlockedServerBackupEntry>();

            if (managedServers != null)
            {
                foreach (ManagedBlockedServer server in managedServers)
                {
                    string ipAddress = server == null ? null : server.IpAddress;
                    if (!FirewallRuleManager.IsPublicIpv4(ipAddress))
                    {
                        result.ExcludedAddresses.Add(
                            string.IsNullOrWhiteSpace(ipAddress) ? "(주소 없음)" : ipAddress);
                        continue;
                    }
                    if (!seen.Add(ipAddress)) continue;

                    BlockedServerMetadata metadata = null;
                    if (metadataByIp != null) metadataByIp.TryGetValue(ipAddress, out metadata);
                    entries.Add(new BlockedServerBackupEntry
                    {
                        IpAddress = ipAddress,
                        DataCenter = NormalizeOptionalText(
                            metadata == null ? null : metadata.DataCenter,
                            MaximumDataCenterLength),
                        Location = NormalizeOptionalText(
                            metadata == null ? null : metadata.Location,
                            MaximumLocationLength),
                        Note = NormalizeOptionalText(
                            metadata == null ? null : metadata.Note,
                            BlockedServerMetadataStore.MaximumNoteLength),
                        BlockedAtUtc = metadata == null || !metadata.BlockedAtUtc.HasValue
                            ? null
                            : (DateTime?)NormalizeUtc(metadata.BlockedAtUtc.Value)
                    });
                }
            }

            entries = entries.OrderBy(item => Ipv4SortKey(item.IpAddress)).ToList();
            if (entries.Count == 0)
            {
                result.ErrorMessage = result.ExcludedAddresses.Count > 0
                    ? "복원 가능한 공인 IPv4 관리 규칙이 없어 백업을 만들지 않았습니다."
                    : "현재 백업할 앱 관리 차단 규칙이 없습니다.";
                return result;
            }
            if (entries.Count > MaximumItemCount)
            {
                result.ErrorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    "차단 서버가 {0}개여서 백업 한도({1}개)를 초과했습니다.",
                    entries.Count,
                    MaximumItemCount);
                return result;
            }

            var document = new BackupDocument
            {
                Format = FormatName,
                Version = CurrentVersion,
                Items = entries.Select(item => new BackupItem
                {
                    Ip = item.IpAddress,
                    DataCenter = item.DataCenter,
                    Location = item.Location,
                    Note = item.Note,
                    BlockedAtUtc = item.BlockedAtUtc.HasValue
                        ? NormalizeUtc(item.BlockedAtUtc.Value).ToString("o", CultureInfo.InvariantCulture)
                        : null
                }).ToList()
            };

            try
            {
                var serializer = CreateSerializer();
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(serializer.Serialize(document));
                if (bytes.Length > MaximumFileBytes)
                {
                    result.ErrorMessage = "백업 파일 크기 한도를 초과했습니다.";
                    return result;
                }
                result.Entries = entries;
                result.Utf8Bytes = bytes;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "차단 목록 백업을 만들지 못했습니다: " + ex.Message;
            }
            return result;
        }

        public static BlockedServerBackupParseResult Parse(byte[] utf8Bytes)
        {
            var result = new BlockedServerBackupParseResult();
            if (utf8Bytes == null || utf8Bytes.Length < 2)
            {
                result.ErrorMessage = "비어 있거나 올바르지 않은 백업 파일입니다.";
                return result;
            }
            if (utf8Bytes.Length > MaximumFileBytes)
            {
                result.ErrorMessage = "백업 파일 크기 한도를 초과했습니다.";
                return result;
            }

            string json;
            try
            {
                json = new UTF8Encoding(false, true).GetString(utf8Bytes);
                if (json.Length > 0 && json[0] == '\ufeff') json = json.Substring(1);
            }
            catch (DecoderFallbackException)
            {
                result.ErrorMessage = "백업 파일이 올바른 UTF-8 형식이 아닙니다.";
                return result;
            }

            var uniquenessReader = new JsonMemberUniquenessReader(json);
            if (!uniquenessReader.ReadDocument())
            {
                result.ErrorMessage = uniquenessReader.DuplicateMemberFound
                    ? "백업 JSON에 중복된 필드가 포함되어 있습니다."
                    : "백업 JSON 형식을 읽을 수 없습니다.";
                return result;
            }

            IDictionary<string, object> document;
            try
            {
                document = CreateSerializer().DeserializeObject(json) as IDictionary<string, object>;
            }
            catch
            {
                result.ErrorMessage = "백업 JSON 형식을 읽을 수 없습니다.";
                return result;
            }
            if (document == null)
            {
                result.ErrorMessage = "백업 JSON의 최상위 형식이 올바르지 않습니다.";
                return result;
            }
            if (document.Keys.Any(key => !TopLevelFields.Contains(key)))
            {
                result.ErrorMessage = "지원하지 않는 최상위 필드가 포함되어 있습니다.";
                return result;
            }

            object formatValue;
            object versionValue;
            object itemsValue;
            if (!document.TryGetValue("Format", out formatValue)
                || !string.Equals(formatValue as string, FormatName, StringComparison.Ordinal)
                || !document.TryGetValue("Version", out versionValue)
                || !(versionValue is int)
                || (int)versionValue != CurrentVersion
                || !document.TryGetValue("Items", out itemsValue))
            {
                result.ErrorMessage = "지원하지 않는 차단 목록 백업 형식 또는 버전입니다.";
                return result;
            }

            object[] rawItems = itemsValue as object[];
            if (rawItems == null)
            {
                result.ErrorMessage = "백업 항목 목록 형식이 올바르지 않습니다.";
                return result;
            }
            if (rawItems.Length > MaximumItemCount)
            {
                result.ErrorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    "백업 항목 수가 한도({0}개)를 초과했습니다.",
                    MaximumItemCount);
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < rawItems.Length; index++)
                result.Items.Add(ParseItem(rawItems[index], index + 1, seen));
            result.Success = true;
            return result;
        }

        public static IList<BlockedServerRestoreItem> CreateRestorePreview(
            BlockedServerBackupParseResult parsed,
            IDictionary<string, FirewallQueryResult> currentStates)
        {
            var preview = new List<BlockedServerRestoreItem>();
            if (parsed == null || !parsed.Success || parsed.Items == null) return preview;

            foreach (BlockedServerBackupParsedItem item in parsed.Items)
            {
                var planned = new BlockedServerRestoreItem
                {
                    SourceIndex = item.SourceIndex,
                    SourceIpAddress = item.SourceIpAddress,
                    Entry = item.Entry,
                    Status = BlockedServerRestoreStatus.Excluded,
                    Detail = item.ExclusionReason
                };
                if (!item.IsEligible)
                {
                    preview.Add(planned);
                    continue;
                }

                FirewallQueryResult state = null;
                if (currentStates == null
                    || !currentStates.TryGetValue(item.Entry.IpAddress, out state)
                    || state == null
                    || !state.Success)
                {
                    planned.Detail = state == null || string.IsNullOrWhiteSpace(state.ErrorMessage)
                        ? "현재 방화벽 상태를 확인하지 못했습니다."
                        : state.ErrorMessage;
                }
                else if (state.IsBlocked)
                {
                    planned.Status = BlockedServerRestoreStatus.AlreadyBlocked;
                    planned.Detail = "이미 앱 관리 규칙으로 차단되어 있습니다.";
                }
                else
                {
                    planned.Status = BlockedServerRestoreStatus.NewBlock;
                    planned.Detail = "새 앱 관리 규칙으로 차단합니다.";
                }
                preview.Add(planned);
            }
            return preview;
        }

        private static BlockedServerBackupParsedItem ParseItem(
            object raw,
            int sourceIndex,
            ISet<string> seen)
        {
            var parsed = new BlockedServerBackupParsedItem { SourceIndex = sourceIndex };
            IDictionary<string, object> item = raw as IDictionary<string, object>;
            if (item == null)
            {
                parsed.ExclusionReason = "항목 형식이 올바르지 않습니다.";
                return parsed;
            }
            if (item.Keys.Any(key => !ItemFields.Contains(key)))
            {
                parsed.ExclusionReason = "지원하지 않는 항목 필드가 포함되어 있습니다.";
                return parsed;
            }

            object ipValue;
            if (!item.TryGetValue("Ip", out ipValue) || !(ipValue is string))
            {
                parsed.ExclusionReason = "서버 IP가 없거나 문자열이 아닙니다.";
                return parsed;
            }
            parsed.SourceIpAddress = (string)ipValue;
            if (!FirewallRuleManager.IsPublicIpv4(parsed.SourceIpAddress))
            {
                parsed.ExclusionReason = "정확한 공인 IPv4 주소가 아닙니다.";
                return parsed;
            }

            string dataCenter;
            string location;
            string note;
            DateTime? blockedAtUtc;
            string error;
            if (!TryReadOptionalText(item, "DataCenter", MaximumDataCenterLength, out dataCenter, out error)
                || !TryReadOptionalText(item, "Location", MaximumLocationLength, out location, out error)
                || !TryReadOptionalText(
                    item,
                    "Note",
                    BlockedServerMetadataStore.MaximumNoteLength,
                    out note,
                    out error)
                || !TryReadOptionalUtc(item, "BlockedAtUtc", out blockedAtUtc, out error))
            {
                parsed.ExclusionReason = error;
                return parsed;
            }

            parsed.Entry = new BlockedServerBackupEntry
            {
                IpAddress = parsed.SourceIpAddress,
                DataCenter = dataCenter,
                Location = location,
                Note = note,
                BlockedAtUtc = blockedAtUtc
            };
            if (!seen.Add(parsed.SourceIpAddress))
                parsed.ExclusionReason = "같은 파일 안에 중복된 서버 IP입니다.";
            return parsed;
        }

        private static bool TryReadOptionalText(
            IDictionary<string, object> item,
            string field,
            int maximumLength,
            out string value,
            out string error)
        {
            value = null;
            error = null;
            object raw;
            if (!item.TryGetValue(field, out raw) || raw == null) return true;
            string text = raw as string;
            if (text == null)
            {
                error = field + " 값이 문자열 또는 null이 아닙니다.";
                return false;
            }
            text = text.Trim();
            if (text.Length == 0) return true;
            if (text.Length > maximumLength || !HasValidTextCharacters(text))
            {
                error = field + " 값의 길이 또는 문자가 올바르지 않습니다.";
                return false;
            }
            value = text;
            return true;
        }

        private static bool TryReadOptionalUtc(
            IDictionary<string, object> item,
            string field,
            out DateTime? value,
            out string error)
        {
            value = null;
            error = null;
            object raw;
            if (!item.TryGetValue(field, out raw) || raw == null) return true;
            string text = raw as string;
            DateTime parsed;
            if (text == null || !DateTime.TryParseExact(
                text,
                "o",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed))
            {
                error = field + " 값이 올바른 UTC 시각이 아닙니다.";
                return false;
            }
            value = NormalizeUtc(parsed);
            return true;
        }

        private static bool HasValidTextCharacters(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (char.IsControl(character)) return false;
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        return false;
                    index++;
                }
                else if (char.IsLowSurrogate(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static string NormalizeOptionalText(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim();
            var builder = new StringBuilder(Math.Min(trimmed.Length, maximumLength));
            for (int index = 0; index < trimmed.Length && builder.Length < maximumLength; index++)
            {
                char character = trimmed[index];
                if (char.IsHighSurrogate(character))
                {
                    if (index + 1 >= trimmed.Length || !char.IsLowSurrogate(trimmed[index + 1]))
                        continue;
                    if (builder.Length + 2 > maximumLength) break;
                    builder.Append(character);
                    builder.Append(trimmed[++index]);
                }
                else if (!char.IsLowSurrogate(character) && !char.IsControl(character))
                {
                    builder.Append(character);
                }
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

        private static uint Ipv4SortKey(string ipAddress)
        {
            byte[] bytes = IPAddress.Parse(ipAddress).GetAddressBytes();
            return ((uint)bytes[0] << 24)
                | ((uint)bytes[1] << 16)
                | ((uint)bytes[2] << 8)
                | bytes[3];
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = MaximumFileBytes,
                RecursionLimit = 16
            };
        }
    }

    internal static class BlockedServerBackupFile
    {
        internal static void WriteAtomic(string path, byte[] bytes)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("백업 파일 경로가 비어 있습니다.", "path");
            if (bytes == null || bytes.Length < 2
                || bytes.Length > BlockedServerBackupService.MaximumFileBytes)
                throw new ArgumentException("백업 파일 데이터 크기가 올바르지 않습니다.", "bytes");

            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("백업 파일 폴더를 찾을 수 없습니다.");

            string temporaryPath = Path.Combine(
                directory,
                Path.GetFileName(fullPath) + ".tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                    File.Replace(temporaryPath, fullPath, null, true);
                else
                    File.Move(temporaryPath, fullPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch
                {
                    // A stale temporary file is never selected as a backup input automatically.
                }
            }
        }
    }
}
