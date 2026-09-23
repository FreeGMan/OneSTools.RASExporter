using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using OneSTools.Ras.Client.Protocol;

namespace OneSTools.Ras.Client.Wire;

/// <summary>
/// Запись примитивов RAS wire-format в IBufferWriter.
/// Реализует 17 кодеков из docs/CODEC_SPEC.md.
/// </summary>
public readonly struct RasWriter
{
    private readonly IBufferWriter<byte> _writer;

    public RasWriter(IBufferWriter<byte> writer)
    {
        _writer = writer;
    }

    /// <summary>RAW: 1 байт.</summary>
    public void WriteByte(byte value)
    {
        var span = _writer.GetSpan(1);
        span[0] = value;
        _writer.Advance(1);
    }

    /// <summary>int16 big-endian.</summary>
    public void WriteShort(short value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfShort);
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        _writer.Advance(RasWireConstants.SizeOfShort);
    }

    /// <summary>int32: фиксированные 4 байта big-endian, НЕ varint.</summary>
    public void WriteInt32(int value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfInt);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        _writer.Advance(RasWireConstants.SizeOfInt);
    }

    /// <summary>int64 big-endian.</summary>
    public void WriteLong(long value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfLong);
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        _writer.Advance(RasWireConstants.SizeOfLong);
    }

    /// <summary>size: unsigned LEB128 varint (7 бит/байт).</summary>
    public void WriteSize(int value)
    {
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            WriteByte((byte)(0x80 | (remaining & 0x7F)));
            remaining >>= 7;
        }
        WriteByte((byte)remaining);
    }

    /// <summary>nullable_size: 6+7k бит; null = ровно 0x80 (Java-семантика).</summary>
    public void WriteNullableSize(int? value)
    {
        if (value is null)
        {
            WriteByte(RasWireConstants.NullByte);
            return;
        }

        uint remaining = (uint)value.Value;
        uint msb = remaining >> RasWireConstants.NullShift;
        uint b1 = (msb != 0 ? (uint)RasWireConstants.NullNextMask : 0u) | (remaining & RasWireConstants.NullLsbMask);
        WriteByte((byte)b1);

        for (remaining = msb; remaining > 0; remaining = msb)
        {
            msb >>= RasWireConstants.MaxShift;
            uint b = (msb != 0 ? (uint)RasWireConstants.NextMask : 0u) | (remaining & RasWireConstants.LsbMask);
            WriteByte((byte)b);
        }
    }

    /// <summary>Сырые байты без заголовка.</summary>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return;
        var span = _writer.GetSpan(value.Length);
        value.CopyTo(span);
        _writer.Advance(value.Length);
    }

    /// <summary>bytes: [size][данные].</summary>
    public void WriteByteArray(ReadOnlySpan<byte> value)
    {
        WriteSize(value.Length);
        WriteBytes(value);
    }

    /// <summary>string: [nullable-size UTF-8 длина][байты]; null ⇒ 0x80.</summary>
    public void WriteString(string? value)
    {
        if (value is null)
        {
            WriteByte(RasWireConstants.NullByte);
            return;
        }
        
        int length = Encoding.UTF8.GetByteCount(value);
        WriteNullableSize(length);
        
        var span = _writer.GetSpan(length);
        Encoding.UTF8.GetBytes(value, span);
        _writer.Advance(length);
    }

    /// <summary>uuid: 16 байт big-endian.</summary>
    public void WriteUuid(Guid value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfUuid);
        value.TryWriteBytes(span, bigEndian: true, out _);
        _writer.Advance(RasWireConstants.SizeOfUuid);
    }

    /// <summary>float IEEE-754 big-endian.</summary>
    public void WriteFloat(float value)
    {
        WriteInt32(BitConverter.SingleToInt32Bits(value));
    }

    /// <summary>double IEEE-754 big-endian.</summary>
    public void WriteDouble(double value)
    {
        WriteLong(BitConverter.DoubleToInt64Bits(value));
    }

    /// <summary>type: байт кода; null ⇒ 0x80.</summary>
    public void WriteType(ServiceWireType? value)
    {
        if (value is null)
        {
            WriteByte(RasWireConstants.NullByte);
        }
        else
        {
            WriteByte((byte)value.Value);
        }
    }

    /// <summary>endpoint_id: кодирование nullable_size.</summary>
    public void WriteEndpointId(int? endpointId)
    {
        WriteNullableSize(endpointId);
    }

    /// <summary>bool: 1 байт 0/1.</summary>
    public void WriteBool(bool value)
    {
        WriteByte(value ? RasWireConstants.TrueByte : RasWireConstants.FalseByte);
    }

    /// <summary>time: R8-тики от 0001-01-01 (AgeDelta), вход — UTC/local пересчитывается в Unix-миллисекунды.</summary>
    public void WriteTime(DateTime value)
    {
        long unixMs = ((DateTimeOffset)value).ToUnixTimeMilliseconds();
        long ticks = (unixMs * 10) + RasWireConstants.AgeDelta;
        WriteLong(ticks);
    }

    public void WriteChar(char value)
    {
        WriteShort((short)value);
    }
}