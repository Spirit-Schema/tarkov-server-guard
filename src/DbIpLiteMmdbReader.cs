// SPDX-License-Identifier: MPL-2.0
// Copyright 2026 Spirit-Schema

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace TarkovServerReporter
{
    /// <summary>
    /// Minimal, bounds-checked MaxMind DB v2 reader for DB-IP City Lite.
    /// It intentionally implements only local file access; networking and updates live elsewhere.
    /// </summary>
    public sealed class DbIpLiteMmdbReader : IDisposable
    {
        private static readonly byte[] MetadataMarker = new byte[]
        {
            0xAB, 0xCD, 0xEF,
            (byte)'M', (byte)'a', (byte)'x', (byte)'M', (byte)'i', (byte)'n', (byte)'d', (byte)'.',
            (byte)'c', (byte)'o', (byte)'m'
        };
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private const int MaximumMetadataSearchBytes = 128 * 1024;
        private const int MaximumDecodeDepth = 40;
        private const int MaximumContainerItems = 100000;
        private const int MaximumDecodedItems = 200000;
        private const long MaximumDecodedScalarBytes = 32L * 1024 * 1024;
        private const int DataSeparatorLength = 16;

        private readonly object _sync = new object();
        private readonly FileStream _stream;
        private readonly long _length;
        private readonly long _searchTreeSize;
        private readonly long _dataSectionStart;
        private readonly long _metadataMarkerOffset;
        private readonly int _nodeByteSize;
        private readonly long _ipv4StartNode;
        private bool _disposed;

        public DbIpLiteMmdbReader(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("MMDB path is empty.", "path");

            _stream = new FileStream(
                Path.GetFullPath(path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.RandomAccess);
            try
            {
                _length = _stream.Length;
                if (_length <= MetadataMarker.Length)
                    throw new InvalidDataException("MMDB file is too small.");

                long metadataOffset = FindMetadataOffset();
                _metadataMarkerOffset = metadataOffset - MetadataMarker.Length;
                var metadataDecoder = new Decoder(this, metadataOffset, _length);
                DecodedValue metadataValue = metadataDecoder.Decode(metadataOffset, 0);
                IDictionary<string, object> metadata = metadataValue.Value as IDictionary<string, object>;
                if (metadata == null)
                    throw new InvalidDataException("MMDB metadata is not a map.");

                BinaryFormatMajorVersion = GetRequiredInt(metadata, "binary_format_major_version");
                BinaryFormatMinorVersion = GetOptionalInt(metadata, "binary_format_minor_version", 0);
                NodeCount = GetRequiredLong(metadata, "node_count");
                RecordSize = GetRequiredInt(metadata, "record_size");
                IpVersion = GetRequiredInt(metadata, "ip_version");
                DatabaseType = GetRequiredString(metadata, "database_type");
                long buildEpoch = GetOptionalLong(metadata, "build_epoch", 0);
                BuildUtc = buildEpoch > 0 && buildEpoch <= 253402300799L
                    ? DateTimeOffset.FromUnixTimeSeconds(buildEpoch).UtcDateTime
                    : default(DateTime);

                if (BinaryFormatMajorVersion != 2)
                    throw new InvalidDataException("Unsupported MMDB binary format version.");
                if (NodeCount <= 0 || NodeCount > int.MaxValue)
                    throw new InvalidDataException("Invalid MMDB node count.");
                if (RecordSize != 24 && RecordSize != 28 && RecordSize != 32)
                    throw new InvalidDataException("Unsupported MMDB record size.");
                if (IpVersion != 4 && IpVersion != 6)
                    throw new InvalidDataException("Invalid MMDB IP version.");

                _nodeByteSize = RecordSize * 2 / 8;
                _searchTreeSize = checked(NodeCount * _nodeByteSize);
                _dataSectionStart = checked(_searchTreeSize + DataSeparatorLength);
                if (_dataSectionStart >= metadataOffset || _dataSectionStart >= _length)
                    throw new InvalidDataException("MMDB search tree or data section is out of bounds.");

                ValidateDataSeparator();
                _ipv4StartNode = IpVersion == 6 ? FindIpv4StartNode() : 0;
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        public int BinaryFormatMajorVersion { get; private set; }
        public int BinaryFormatMinorVersion { get; private set; }
        public long NodeCount { get; private set; }
        public int RecordSize { get; private set; }
        public int IpVersion { get; private set; }
        public string DatabaseType { get; private set; }
        public DateTime BuildUtc { get; private set; }

        public IDictionary<string, object> Lookup(IPAddress address)
        {
            if (address == null) throw new ArgumentNullException("address");
            lock (_sync)
            {
                ThrowIfDisposed();
                byte[] bytes = address.GetAddressBytes();
                long node;
                if (bytes.Length == 4)
                {
                    node = IpVersion == 6 ? _ipv4StartNode : 0;
                }
                else if (bytes.Length == 16 && IpVersion == 6)
                {
                    node = 0;
                }
                else
                {
                    return null;
                }

                if (node == NodeCount) return null;
                if (node > NodeCount) return DecodeDataRecord(node);
                foreach (byte item in bytes)
                {
                    for (int bit = 7; bit >= 0; bit--)
                    {
                        node = ReadNode(node, ((item >> bit) & 1) != 0 ? 1 : 0);
                        if (node == NodeCount) return null;
                        if (node > NodeCount) return DecodeDataRecord(node);
                    }
                }
                return null;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                _stream.Dispose();
            }
        }

        internal byte ReadByteAt(long offset)
        {
            if (offset < 0 || offset >= _length)
                throw new InvalidDataException("MMDB read is out of bounds.");
            _stream.Position = offset;
            int value = _stream.ReadByte();
            if (value < 0) throw new EndOfStreamException();
            return (byte)value;
        }

        internal byte[] ReadBytesAt(long offset, int count)
        {
            if (count < 0 || offset < 0 || offset > _length - count)
                throw new InvalidDataException("MMDB read is out of bounds.");
            byte[] buffer = new byte[count];
            _stream.Position = offset;
            int total = 0;
            while (total < count)
            {
                int read = _stream.Read(buffer, total, count - total);
                if (read <= 0) throw new EndOfStreamException();
                total += read;
            }
            return buffer;
        }

        private long FindMetadataOffset()
        {
            int count = (int)Math.Min(_length, MaximumMetadataSearchBytes);
            long start = _length - count;
            byte[] tail = ReadBytesAt(start, count);
            for (int index = tail.Length - MetadataMarker.Length; index >= 0; index--)
            {
                bool match = true;
                for (int markerIndex = 0; markerIndex < MetadataMarker.Length; markerIndex++)
                {
                    if (tail[index + markerIndex] == MetadataMarker[markerIndex]) continue;
                    match = false;
                    break;
                }
                if (match) return start + index + MetadataMarker.Length;
            }
            throw new InvalidDataException("MMDB metadata marker was not found.");
        }

        private void ValidateDataSeparator()
        {
            byte[] separator = ReadBytesAt(_searchTreeSize, DataSeparatorLength);
            for (int index = 0; index < separator.Length; index++)
            {
                if (separator[index] != 0)
                    throw new InvalidDataException("MMDB data separator is invalid.");
            }
        }

        private long FindIpv4StartNode()
        {
            long node = 0;
            for (int bit = 0; bit < 96 && node < NodeCount; bit++)
                node = ReadNode(node, 0);
            return node;
        }

        private IDictionary<string, object> DecodeDataRecord(long node)
        {
            long dataOffset = checked(node - NodeCount + _searchTreeSize);
            if (dataOffset < _dataSectionStart || dataOffset >= _length)
                throw new InvalidDataException("MMDB data pointer is out of bounds.");
            if (dataOffset >= _metadataMarkerOffset)
                throw new InvalidDataException("MMDB data pointer enters the metadata section.");
            var decoder = new Decoder(this, _dataSectionStart, _metadataMarkerOffset);
            DecodedValue decoded = decoder.Decode(dataOffset, 0);
            return decoded.Value as IDictionary<string, object>;
        }

        private long ReadNode(long node, int branch)
        {
            if (node < 0 || node >= NodeCount)
                throw new InvalidDataException("MMDB node is out of bounds.");
            byte[] buffer = ReadBytesAt(checked(node * _nodeByteSize), _nodeByteSize);
            if (RecordSize == 24)
            {
                int offset = branch == 0 ? 0 : 3;
                return ((long)buffer[offset] << 16)
                    | ((long)buffer[offset + 1] << 8)
                    | buffer[offset + 2];
            }
            if (RecordSize == 28)
            {
                if (branch == 0)
                    return ((long)(buffer[3] & 0xF0) << 20)
                        | ((long)buffer[0] << 16)
                        | ((long)buffer[1] << 8)
                        | buffer[2];
                return ((long)(buffer[3] & 0x0F) << 24)
                    | ((long)buffer[4] << 16)
                    | ((long)buffer[5] << 8)
                    | buffer[6];
            }
            int start = branch == 0 ? 0 : 4;
            return ((long)buffer[start] << 24)
                | ((long)buffer[start + 1] << 16)
                | ((long)buffer[start + 2] << 8)
                | buffer[start + 3];
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException("DbIpLiteMmdbReader");
        }

        private static int GetRequiredInt(IDictionary<string, object> map, string key)
        {
            return checked((int)GetRequiredLong(map, key));
        }

        private static int GetOptionalInt(IDictionary<string, object> map, string key, int fallback)
        {
            object value;
            return map.TryGetValue(key, out value) ? checked((int)ConvertToLong(value, key)) : fallback;
        }

        private static long GetRequiredLong(IDictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value))
                throw new InvalidDataException("MMDB metadata is missing " + key + ".");
            return ConvertToLong(value, key);
        }

        private static long GetOptionalLong(IDictionary<string, object> map, string key, long fallback)
        {
            object value;
            return map.TryGetValue(key, out value) ? ConvertToLong(value, key) : fallback;
        }

        private static long ConvertToLong(object value, string key)
        {
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch (Exception exception)
            {
                throw new InvalidDataException("Invalid MMDB numeric metadata: " + key + ".", exception);
            }
        }

        private static string GetRequiredString(IDictionary<string, object> map, string key)
        {
            object value;
            string text = map.TryGetValue(key, out value) ? value as string : null;
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("MMDB metadata is missing " + key + ".");
            return text;
        }

        private sealed class DecodedValue
        {
            public object Value { get; set; }
            public long NextOffset { get; set; }
        }

        private sealed class Decoder
        {
            private readonly DbIpLiteMmdbReader _reader;
            private readonly long _pointerBase;
            private readonly long _maximumOffsetExclusive;
            private int _remainingItems = MaximumDecodedItems;
            private long _remainingScalarBytes = MaximumDecodedScalarBytes;

            public Decoder(DbIpLiteMmdbReader reader, long pointerBase, long maximumOffsetExclusive)
            {
                _reader = reader;
                _pointerBase = pointerBase;
                _maximumOffsetExclusive = maximumOffsetExclusive;
            }

            public DecodedValue Decode(long offset, int depth)
            {
                if (depth > MaximumDecodeDepth)
                    throw new InvalidDataException("MMDB nesting is too deep.");
                if (--_remainingItems < 0)
                    throw new InvalidDataException("MMDB decoded item budget was exceeded.");

                long cursor = offset;
                byte control = ReadByte(cursor++);
                int type = control >> 5;
                if (type == 0)
                {
                    type = ReadByte(cursor++) + 7;
                }

                if (type == 1)
                    return DecodePointer(control, cursor, depth);

                int size = DecodeSize(control & 0x1F, ref cursor);
                switch (type)
                {
                    case 2:
                        ConsumeScalarBytes(size);
                        return Scalar(StrictUtf8.GetString(ReadBytes(cursor, size)), cursor + size);
                    case 3:
                        if (size != 8) throw new InvalidDataException("Invalid MMDB double size.");
                        return Scalar(ReadDouble(cursor), cursor + size);
                    case 4:
                        ConsumeScalarBytes(size);
                        return Scalar(ReadBytes(cursor, size), cursor + size);
                    case 5:
                        return Scalar(ReadUnsigned64(cursor, size, 2), cursor + size);
                    case 6:
                        return Scalar(ReadUnsigned64(cursor, size, 4), cursor + size);
                    case 9:
                        return Scalar(ReadUnsigned64(cursor, size, 8), cursor + size);
                    case 10:
                        return Scalar(ReadUnsigned128(cursor, size), cursor + size);
                    case 7:
                        return DecodeMap(cursor, size, depth);
                    case 8:
                        return Scalar(ReadSigned(cursor, size), cursor + size);
                    case 11:
                        return DecodeArray(cursor, size, depth);
                    case 14:
                        if (size != 0 && size != 1)
                            throw new InvalidDataException("Invalid MMDB boolean value.");
                        return Scalar(size != 0, cursor);
                    case 15:
                        if (size != 4) throw new InvalidDataException("Invalid MMDB float size.");
                        return Scalar(ReadFloat(cursor), cursor + size);
                    default:
                        throw new InvalidDataException("Unsupported MMDB data type " + type + ".");
                }
            }

            private DecodedValue DecodePointer(byte control, long cursor, int depth)
            {
                int pointerSize = ((control >> 3) & 0x03) + 1;
                long pointer;
                if (pointerSize == 4)
                {
                    pointer = checked((long)ReadUnsigned64(cursor, 4, 4));
                }
                else
                {
                    pointer = control & 0x07;
                    pointer = (pointer << (pointerSize * 8))
                        | checked((long)ReadUnsigned64(cursor, pointerSize, pointerSize));
                    if (pointerSize == 2) pointer += 2048;
                    else if (pointerSize == 3) pointer += 526336;
                }
                long returnOffset = cursor + pointerSize;
                long target = checked(_pointerBase + pointer);
                DecodedValue referenced = Decode(target, depth + 1);
                return new DecodedValue { Value = referenced.Value, NextOffset = returnOffset };
            }

            private DecodedValue DecodeMap(long cursor, int size, int depth)
            {
                if (size < 0 || size > MaximumContainerItems)
                    throw new InvalidDataException("MMDB map is too large.");
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                for (int index = 0; index < size; index++)
                {
                    DecodedValue keyValue = Decode(cursor, depth + 1);
                    string key = keyValue.Value as string;
                    if (key == null) throw new InvalidDataException("MMDB map key is not text.");
                    DecodedValue value = Decode(keyValue.NextOffset, depth + 1);
                    map[key] = value.Value;
                    cursor = value.NextOffset;
                }
                return Scalar(map, cursor);
            }

            private DecodedValue DecodeArray(long cursor, int size, int depth)
            {
                if (size < 0 || size > MaximumContainerItems)
                    throw new InvalidDataException("MMDB array is too large.");
                var values = new List<object>(size);
                for (int index = 0; index < size; index++)
                {
                    DecodedValue value = Decode(cursor, depth + 1);
                    values.Add(value.Value);
                    cursor = value.NextOffset;
                }
                return Scalar(values, cursor);
            }

            private int DecodeSize(int size, ref long cursor)
            {
                if (size < 29) return size;
                if (size == 29) return 29 + ReadByte(cursor++);
                if (size == 30)
                {
                    int value = (ReadByte(cursor) << 8) | ReadByte(cursor + 1);
                    cursor += 2;
                    return 285 + value;
                }
                int extended = (ReadByte(cursor) << 16)
                    | (ReadByte(cursor + 1) << 8)
                    | ReadByte(cursor + 2);
                cursor += 3;
                return checked(65821 + extended);
            }

            private ulong ReadUnsigned64(long offset, int size, int maximumSize)
            {
                if (size < 0 || size > maximumSize)
                    throw new InvalidDataException("Unsupported MMDB integer size.");
                ulong value = 0;
                for (int index = 0; index < size; index++)
                    value = (value << 8) | ReadByte(offset + index);
                return value;
            }

            private object ReadUnsigned128(long offset, int size)
            {
                if (size < 0 || size > 16)
                    throw new InvalidDataException("Unsupported MMDB uint128 size.");
                if (size <= 8) return ReadUnsigned64(offset, size, 8);
                ulong high = ReadUnsigned64(offset, size - 8, 8);
                ulong low = ReadUnsigned64(offset + size - 8, 8, 8);
                return new UInt128Value(high, low);
            }

            private long ReadSigned(long offset, int size)
            {
                if (size == 0) return 0;
                if (size > 4) throw new InvalidDataException("Unsupported MMDB signed integer size.");
                long value = checked((long)ReadUnsigned64(offset, size, 4));
                if (size < 4) return value;
                long signBit = 1L << (size * 8 - 1);
                return (value & signBit) == 0 ? value : value - (1L << (size * 8));
            }

            private double ReadDouble(long offset)
            {
                byte[] bytes = ReadBytes(offset, 8);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                return BitConverter.ToDouble(bytes, 0);
            }

            private float ReadFloat(long offset)
            {
                byte[] bytes = ReadBytes(offset, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                return BitConverter.ToSingle(bytes, 0);
            }

            private static DecodedValue Scalar(object value, long nextOffset)
            {
                return new DecodedValue { Value = value, NextOffset = nextOffset };
            }

            private byte ReadByte(long offset)
            {
                if (offset < 0 || offset >= _maximumOffsetExclusive)
                    throw new InvalidDataException("MMDB value crosses its section boundary.");
                return _reader.ReadByteAt(offset);
            }

            private byte[] ReadBytes(long offset, int count)
            {
                if (count < 0 || offset < 0 || offset > _maximumOffsetExclusive - count)
                    throw new InvalidDataException("MMDB value crosses its section boundary.");
                return _reader.ReadBytesAt(offset, count);
            }

            private void ConsumeScalarBytes(int count)
            {
                _remainingScalarBytes -= count;
                if (_remainingScalarBytes < 0)
                    throw new InvalidDataException("MMDB decoded byte budget was exceeded.");
            }
        }

        private sealed class UInt128Value
        {
            public UInt128Value(ulong high, ulong low)
            {
                High = high;
                Low = low;
            }

            public ulong High { get; private set; }
            public ulong Low { get; private set; }
        }
    }
}
