using System.Buffers.Binary;
using System.Text;
using OneSTools.Ras.Client.Protocol;

namespace OneSTools.Ras.MockServer.Framing;

/// <summary>
/// Запись байтов wire-формата RAS/SWP: big-endian числа, LEB128 size-varint,
/// nullable_size (0x80 = null), string [nullable-size][utf8] и тип-значения.
/// Источник: docs/TRANSPORT_SPEC.md разделы 3.2–3.4, docs/CODEC_SPEC.md.
/// </summary>
public sealed class SwpFrameWriter
{
    private readonly MemoryStream _stream = new();

    public ReadOnlySpan<byte> GetWrittenBytes() => _stream.ToArray();

    public void WriteByte(byte value) => _stream.WriteByte(value);

    public void WriteShort(short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        WriteBytes(buffer);
    }

    public void WriteInt(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        WriteBytes(buffer);
    }

    public void WriteLong(long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        WriteBytes(buffer);
    }

    /// <summary>size-varint (LEB128): 7 бит на байт, бит 0x80 — продолжение.</summary>
    public void WriteSize(int value)
    {
        uint remaining = (uint)value;
        while (remaining >= 0x80)
        {
            _stream.WriteByte((byte)(0x80 | (remaining & 0x7F)));
            remaining >>= 7;
        }

        _stream.WriteByte((byte)remaining);
    }

    /// <summary>nullable_size: 0x80 = null, иначе 6 бит значения + 0x40-продолжение.</summary>
    public void WriteNullableSize(int? value)
    {
        if (value is null)
        {
            _stream.WriteByte(0x80);
            return;
        }

        uint remaining = (uint)value.Value;
        uint msb = remaining >> 6;
        _stream.WriteByte((byte)((msb != 0 ? 0x40u : 0x00u) | (remaining & 0x3Fu)));

        for (remaining = msb; remaining > 0; remaining = msb)
        {
            msb >>= 7;
            _stream.WriteByte((byte)((msb != 0 ? 0x80u : 0x00u) | (remaining & 0x7Fu)));
        }
    }

    public void WriteString(string value)
    {
        if (value is null)
        {
            WriteNullableSize(null);
            return;
        }

        int length = Encoding.UTF8.GetByteCount(value);
        WriteNullableSize(length);
        WriteBytes(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>typed value: [type: byte ServiceWireType][тело по типу].</summary>
    public void WriteTypedInt(int value)
    {
        WriteByte((byte)ServiceWireType.Int);
        WriteInt(value);
    }

    public void WriteTypedShort(short value)
    {
        WriteByte((byte)ServiceWireType.Short);
        WriteShort(value);
    }

    public void WriteTypedString(string value)
    {
        WriteByte((byte)ServiceWireType.String);
        WriteString(value);
    }

    private void WriteBytes(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);
}