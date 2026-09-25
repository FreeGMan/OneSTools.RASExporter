using System;
using System.Buffers;
using System.IO;
using System.Text;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Protocol;

namespace OneSTools.Ras.Client.Wire;

/// <summary>
/// Чтение примитивов RAS wire-format из ReadOnlySequence.
/// Реализует 17 кодеков из docs/CODEC_SPEC.md.
/// </summary>
public ref struct RasReader
{
    private SequenceReader<byte> _reader;

    public RasReader(ReadOnlySequence<byte> sequence)
    {
        _reader = new SequenceReader<byte>(sequence);
    }

    /// <summary>RAW: 1 байт (CODEC_SPEC §3.1).</summary>
    public byte ReadByte()
    {
        if (!_reader.TryRead(out byte value))
            throw new EndOfStreamException();
        return value;
    }

    /// <summary>int16, 2 байта big-endian (CODEC_SPEC §3.1).</summary>
    public short ReadShort()
    {
        if (!_reader.TryReadBigEndian(out short value))
            throw new EndOfStreamException();
        return value;
    }

    /// <summary>int32: фиксированные 4 байта big-endian, НЕ varint (CODEC_SPEC §3.1).</summary>
    public int ReadInt32()
    {
        if (!_reader.TryReadBigEndian(out int value))
            throw new EndOfStreamException();
        return value;
    }

    /// <summary>int64: фиксированные 8 байт big-endian (CODEC_SPEC §3.1).</summary>
    public long ReadLong()
    {
        if (!_reader.TryReadBigEndian(out long value))
            throw new EndOfStreamException();
        return value;
    }

    /// <summary>size: unsigned LEB128 varint, 7 бит/байт, 0x80 = продолжение (CODEC_SPEC §3.2).</summary>
    public int ReadSize()
    {
        int cur = ReadByte();
        int size = cur & RasWireConstants.LsbMask;

        for (int i = 1, shift = 7; (cur & RasWireConstants.NextMask) != 0; i++, shift += 7)
        {
            if (i > RasWireConstants.MaxSizeVarIntBytes - 1)
                throw new RasProtocolException("size varint too long");
            cur = ReadByte();
            if (i == RasWireConstants.MaxSizeVarIntBytes - 1 && (cur & 0xF8) != 0)
                throw new RasProtocolException("size varint overflow");
            int part = (cur & RasWireConstants.LsbMask) << shift;
            if (part < 0 || size > int.MaxValue - part)
                throw new RasProtocolException("size varint overflow");
            size += part;
        }

        return size;
    }

    /// <summary>nullable_size: первый байт — 6 бит значения, 0x40 = продолжение, 0x80 = null (CODEC_SPEC §3.2).</summary>
    public int? ReadNullableSize()
    {
        int cur = ReadByte();
        if ((cur & RasWireConstants.NextMask) != 0)
        {
            if ((cur & RasWireConstants.LsbMask) != 0)
                throw new InvalidDataException("null expected");
            return null;
        }

        int size = cur & RasWireConstants.NullLsbMask;
        if ((cur & RasWireConstants.NullNextMask) != 0)
        {
            int shift = 6;
            for (int i = 1; ; i++, shift += 7)
            {
                if (i > RasWireConstants.MaxNullableSizeVarIntBytes - 1)
                    throw new RasProtocolException("nullable_size varint too long");
                cur = ReadByte();
                if (i == RasWireConstants.MaxNullableSizeVarIntBytes - 1 && (cur & 0xF0) != 0)
                    throw new RasProtocolException("nullable_size varint overflow");
                int part = (cur & RasWireConstants.LsbMask) << shift;
                if (part < 0 || size > int.MaxValue - part)
                    throw new RasProtocolException("nullable_size varint overflow");
                size += part;
                if ((cur & RasWireConstants.NextMask) == 0)
                    break;
            }
        }

        return size;
    }

    /// <summary>Сырые байты фиксированной длины с проверками лимитов (SECURITY.md T1).</summary>
    public byte[] ReadBytes(int length)
    {
        if (length == 0) return Array.Empty<byte>();
        if (length < 0 || length > RasWireConstants.MaxBytesLength)
            throw new RasProtocolException($"bytes length out of bounds: {length}");
        if (length > Remaining)
            throw new EndOfStreamException("bytes length exceeds remaining data");

        var bytes = new byte[length];
        if (!_reader.TryCopyTo(bytes))
            throw new EndOfStreamException();
        _reader.Advance(length);
        return bytes;
    }

    /// <summary>bytes: [size][данные] (CODEC_SPEC §3.3).</summary>
    public byte[] ReadByteArray()
    {
        int length = ReadSize();
        return ReadBytes(length);
    }

    /// <summary>string: [nullable-size UTF-8 длина][байты]; null ⇒ 0x80 (Java-семантика, CODEC_SPEC §3.3).</summary>
    public string? ReadString()
    {
        int? length = ReadNullableSize();
        if (length is null) return null;
        if (length.Value == 0) return string.Empty;
        if (length.Value > RasWireConstants.MaxBytesLength)
            throw new RasProtocolException($"string length out of bounds: {length.Value}");

        if (_reader.UnreadSpan.Length >= length.Value)
        {
            string result = Encoding.UTF8.GetString(_reader.UnreadSpan.Slice(0, length.Value));
            _reader.Advance(length.Value);
            return result;
        }

        var bytes = ReadBytes(length.Value);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>uuid: 16 байт, big-endian порядок равный .NET Guid (CODEC_SPEC §3.3, трансформация проверена стендом).</summary>
    public Guid ReadUuid()
    {
        if (_reader.UnreadSpan.Length >= RasWireConstants.SizeOfUuid)
        {
            var guid = new Guid(_reader.UnreadSpan.Slice(0, RasWireConstants.SizeOfUuid), bigEndian: true);
            _reader.Advance(RasWireConstants.SizeOfUuid);
            return guid;
        }
        
        var bytes = ReadBytes(RasWireConstants.SizeOfUuid);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>float: IEEE-754, 4 байта big-endian (CODEC_SPEC §3.1).</summary>
    public float ReadFloat()
    {
        return BitConverter.Int32BitsToSingle(ReadInt32());
    }

    /// <summary>double: IEEE-754, 8 байт big-endian (CODEC_SPEC §3.1).</summary>
    public double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadLong());
    }

    /// <summary>type: 1 байт кода ServiceWireType; 0x80 = null (CODEC_SPEC §3.4).</summary>
    public ServiceWireType? ReadType()
    {
        int cur = ReadByte();
        if ((cur & RasWireConstants.NextMask) != 0)
        {
            if ((cur & RasWireConstants.LsbMask) != 0)
                throw new InvalidDataException("type expected");
            return null;
        }

        if (!Enum.IsDefined(typeof(ServiceWireType), (byte)cur))
            throw new InvalidDataException($"Unknown type code: {cur}");
            
        return (ServiceWireType)cur;
    }

    /// <summary>endpoint_id: тот же кодлинг, что nullable_size (CODEC_SPEC §3.2).</summary>
    public int? ReadEndpointId()
    {
        return ReadNullableSize();
    }

    /// <summary>bool: 1 байт 0/1 (CODEC_SPEC §3.1).</summary>
    public bool ReadBool()
    {
        byte val = ReadByte();
        return val switch
        {
            RasWireConstants.TrueByte => true,
            RasWireConstants.FalseByte => false,
            _ => throw new InvalidDataException("boolean expected")
        };
    }

    /// <summary>time: int64 тиков от 0001-01-01 (R8-время, смещение AgeDelta), отдаётся в UTC (CODEC_SPEC §3.3).</summary>
    public DateTime ReadTime()
    {
        long ticks = ReadLong();
        long unixMs = (ticks - RasWireConstants.AgeDelta) / 10;
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
    }

    public int Consumed => (int)_reader.Consumed;
    public int Remaining => (int)_reader.Remaining;

    /// <summary>Длина size-prefix списка с проверками: потолок MaxListSize и согласованность
    /// с остатком потока (каждый элемент занимает минимум 1 байт).</summary>
    public int ReadListCount()
    {
        int count = ReadSize();
        if (count > RasWireConstants.MaxListSize)
            throw new RasProtocolException($"list size out of bounds: {count}");
        if (count > Remaining)
            throw new EndOfStreamException("list size exceeds remaining data");
        return count;
    }

    /// <summary>Прочитать остаток потока как байты.</summary>
    public byte[] ReadRemainingBytes()
    {
        int remaining = Remaining;
        if (remaining == 0) return Array.Empty<byte>();
        return ReadBytes(remaining);
    }

    /// <summary>char: 2 байта big-endian кодовая точка.</summary>
    public char ReadChar()
    {
        return (char)ReadShort();
    }
}
