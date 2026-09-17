using System;
using System.Buffers;
using System.IO;
using System.Text;
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

    public byte ReadByte()
    {
        if (!_reader.TryRead(out byte value))
            throw new EndOfStreamException();
        return value;
    }

    public short ReadShort()
    {
        if (!_reader.TryReadBigEndian(out short value))
            throw new EndOfStreamException();
        return value;
    }

    public int ReadInt32()
    {
        if (!_reader.TryReadBigEndian(out int value))
            throw new EndOfStreamException();
        return value;
    }

    public long ReadLong()
    {
        if (!_reader.TryReadBigEndian(out long value))
            throw new EndOfStreamException();
        return value;
    }

    public int ReadSize()
    {
        int cur = ReadByte();
        int size = cur & RasWireConstants.LsbMask; // 0x7F
        
        for (int shift = 7; (cur & RasWireConstants.NextMask) != 0; shift += 7)
        {
            cur = ReadByte();
            size += (cur & RasWireConstants.LsbMask) << shift;
        }

        return size;
    }

    public int? ReadNullableSize()
    {
        int cur = ReadByte();
        if ((cur & RasWireConstants.NextMask) != 0) // 0x80
        {
            if ((cur & RasWireConstants.LsbMask) != 0)
                throw new InvalidDataException("null expected");
            return null;
        }

        int size = cur & RasWireConstants.NullLsbMask; // 0x3F
        if ((cur & RasWireConstants.NullNextMask) != 0) // 0x40
        {
            int shift = 6;
            cur = ReadByte();
            size += (cur & RasWireConstants.LsbMask) << shift;

            for (shift += 7; (cur & RasWireConstants.NextMask) != 0; shift += 7)
            {
                cur = ReadByte();
                size += (cur & RasWireConstants.LsbMask) << shift;
            }
        }

        return size;
    }

    public byte[] ReadBytes(int length)
    {
        if (length == 0) return Array.Empty<byte>();
        
        var bytes = new byte[length];
        if (!_reader.TryCopyTo(bytes))
            throw new EndOfStreamException();
        _reader.Advance(length);
        return bytes;
    }

    public byte[] ReadByteArray()
    {
        int length = ReadSize();
        return ReadBytes(length);
    }

    public string? ReadString()
    {
        int? length = ReadNullableSize();
        if (length is null) return null;
        if (length.Value == 0) return string.Empty;

        if (_reader.UnreadSpan.Length >= length.Value)
        {
            string result = Encoding.UTF8.GetString(_reader.UnreadSpan.Slice(0, length.Value));
            _reader.Advance(length.Value);
            return result;
        }

        var bytes = ReadBytes(length.Value);
        return Encoding.UTF8.GetString(bytes);
    }

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

    public float ReadFloat()
    {
        return BitConverter.Int32BitsToSingle(ReadInt32());
    }

    public double ReadDouble()
    {
        return BitConverter.Int64BitsToDouble(ReadLong());
    }

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

    public int? ReadEndpointId()
    {
        return ReadNullableSize();
    }

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

    public DateTime ReadTime()
    {
        long ticks = ReadLong();
        long unixMs = (ticks - RasWireConstants.AgeDelta) / 10;
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime;
    }

    public char ReadChar()
    {
        return (char)ReadShort();
    }
}