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

    public void WriteByte(byte value)
    {
        var span = _writer.GetSpan(1);
        span[0] = value;
        _writer.Advance(1);
    }

    public void WriteShort(short value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfShort);
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        _writer.Advance(RasWireConstants.SizeOfShort);
    }

    public void WriteInt32(int value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfInt);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        _writer.Advance(RasWireConstants.SizeOfInt);
    }

    public void WriteLong(long value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfLong);
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        _writer.Advance(RasWireConstants.SizeOfLong);
    }

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

    public void WriteNullableSize(int? value)
    {
        if (value is null)
        {
            WriteByte(RasWireConstants.NullByte); // 0x80
            return;
        }

        uint remaining = (uint)value.Value;
        uint msb = remaining >> RasWireConstants.NullShift; // >> 6
        uint b1 = (msb != 0 ? (uint)RasWireConstants.NullNextMask : 0u) | (remaining & RasWireConstants.NullLsbMask);
        WriteByte((byte)b1);

        for (remaining = msb; remaining > 0; remaining = msb)
        {
            msb >>= RasWireConstants.MaxShift; // >> 7
            uint b = (msb != 0 ? (uint)RasWireConstants.NextMask : 0u) | (remaining & RasWireConstants.LsbMask);
            WriteByte((byte)b);
        }
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return;
        var span = _writer.GetSpan(value.Length);
        value.CopyTo(span);
        _writer.Advance(value.Length);
    }

    public void WriteByteArray(ReadOnlySpan<byte> value)
    {
        WriteSize(value.Length);
        WriteBytes(value);
    }

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

    public void WriteUuid(Guid value)
    {
        var span = _writer.GetSpan(RasWireConstants.SizeOfUuid);
        value.TryWriteBytes(span, bigEndian: true, out _);
        _writer.Advance(RasWireConstants.SizeOfUuid);
    }

    public void WriteFloat(float value)
    {
        WriteInt32(BitConverter.SingleToInt32Bits(value));
    }

    public void WriteDouble(double value)
    {
        WriteLong(BitConverter.DoubleToInt64Bits(value));
    }

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

    public void WriteEndpointId(int? endpointId)
    {
        WriteNullableSize(endpointId);
    }

    public void WriteBool(bool value)
    {
        WriteByte(value ? RasWireConstants.TrueByte : RasWireConstants.FalseByte);
    }

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