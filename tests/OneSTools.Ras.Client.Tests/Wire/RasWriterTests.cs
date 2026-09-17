using System;
using System.Buffers;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Wire;

public sealed class RasWriterTests
{
    private byte[] GetBytes(Action<RasWriter> action)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        action(writer);
        return buffer.WrittenSpan.ToArray();
    }

    [Fact]
    public void WriteByte_Value()
    {
        Assert.Equal(new byte[] { 0x7F }, GetBytes(w => w.WriteByte(0x7F)));
        Assert.Equal(new byte[] { 0x80 }, GetBytes(w => w.WriteByte(0x80))); // Java semantics
    }

    [Fact]
    public void WriteShort_Value()
    {
        Assert.Equal(new byte[] { 0x12, 0x34 }, GetBytes(w => w.WriteShort(0x1234)));
        Assert.Equal(new byte[] { 0x01, 0x00 }, GetBytes(w => w.WriteShort(256)));
    }

    [Fact]
    public void WriteInt32_Value()
    {
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, GetBytes(w => w.WriteInt32(0x12345678)));
        Assert.Equal(new byte[] { 0x1C, 0x53, 0x57, 0x50 }, GetBytes(w => w.WriteInt32(475223888)));
    }

    [Fact]
    public void WriteLong_Value()
    {
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, GetBytes(w => w.WriteLong(0x0102030405060708)));
    }

    [Fact]
    public void WriteSize_Value()
    {
        Assert.Equal(new byte[] { 0x00 }, GetBytes(w => w.WriteSize(0)));
        Assert.Equal(new byte[] { 0x7F }, GetBytes(w => w.WriteSize(127)));
        Assert.Equal(new byte[] { 0xAC, 0x02 }, GetBytes(w => w.WriteSize(300)));
        Assert.Equal(new byte[] { 0x80, 0x80, 0x01 }, GetBytes(w => w.WriteSize(16384)));
    }

    [Fact]
    public void WriteNullableSize_Value()
    {
        Assert.Equal(new byte[] { 0x80 }, GetBytes(w => w.WriteNullableSize(null)));
        Assert.Equal(new byte[] { 0x00 }, GetBytes(w => w.WriteNullableSize(0)));
        Assert.Equal(new byte[] { 0x6C, 0x04 }, GetBytes(w => w.WriteNullableSize(300)));
    }

    [Fact]
    public void WriteString_Value()
    {
        Assert.Equal(new byte[] { 0x05, 0x68, 0x65, 0x6C, 0x6C, 0x6F }, GetBytes(w => w.WriteString("hello")));
        Assert.Equal(new byte[] { 0x00 }, GetBytes(w => w.WriteString("")));
        Assert.Equal(new byte[] { 0x80 }, GetBytes(w => w.WriteString(null)));
    }

    [Fact]
    public void WriteUuid_Value()
    {
        var guid = Guid.Parse("123e4567-e89b-12d3-a456-426614174000");
        Assert.Equal(
            new byte[] { 0x12, 0x3E, 0x45, 0x67, 0xE8, 0x9B, 0x12, 0xD3, 0xA4, 0x56, 0x42, 0x66, 0x14, 0x17, 0x40, 0x00 },
            GetBytes(w => w.WriteUuid(guid))
        );
    }

    [Fact]
    public void WriteFloat_Value()
    {
        Assert.Equal(new byte[] { 0x3F, 0x80, 0x00, 0x00 }, GetBytes(w => w.WriteFloat(1.0f)));
        Assert.Equal(new byte[] { 0xC0, 0x20, 0x00, 0x00 }, GetBytes(w => w.WriteFloat(-2.5f)));
    }

    [Fact]
    public void WriteDouble_Value()
    {
        Assert.Equal(new byte[] { 0x3F, 0xF0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, GetBytes(w => w.WriteDouble(1.0)));
    }

    [Fact]
    public void WriteBool_Value()
    {
        Assert.Equal(new byte[] { 0x01 }, GetBytes(w => w.WriteBool(true)));
        Assert.Equal(new byte[] { 0x00 }, GetBytes(w => w.WriteBool(false)));
    }

    [Fact]
    public void WriteType_Value()
    {
        Assert.Equal(new byte[] { 0x04 }, GetBytes(w => w.WriteType(ServiceWireType.Int)));
        Assert.Equal(new byte[] { 0x80 }, GetBytes(w => w.WriteType(null)));
    }

    [Fact]
    public void WriteTime_Value()
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new byte[] { 0x00, 0x02, 0x35, 0x1E, 0xB5, 0xC0, 0x70, 0x00 }, GetBytes(w => w.WriteTime(epoch)));

        var date2021 = new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new byte[] { 0x00, 0x02, 0x43, 0xC2, 0x06, 0x30, 0xD0, 0x00 }, GetBytes(w => w.WriteTime(date2021)));
    }
}