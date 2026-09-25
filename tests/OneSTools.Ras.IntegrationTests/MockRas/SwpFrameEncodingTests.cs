using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Wire;
using OneSTools.Ras.MockServer.Framing;

namespace OneSTools.Ras.IntegrationTests.MockRas;

public sealed class SwpFrameEncodingTests
{
    [Fact]
    public void Size_Leb128_Encodes_300_As_AC_02()
    {
        var writer = new SwpFrameWriter();
        writer.WriteSize(300);

        byte[] encoded = writer.GetWrittenBytes().ToArray();

        Assert.Equal(new byte[] { 0xAC, 0x02 }, encoded);
    }

    [Fact]
    public void Size_Less_Than_128_Takes_Single_Byte()
    {
        var writer = new SwpFrameWriter();
        writer.WriteSize(127);

        byte[] encoded = writer.GetWrittenBytes().ToArray();

        Assert.Equal(new byte[] { 0x7F }, encoded);
    }

    [Fact]
    public void String_Is_SizePrefixed_Utf8()
    {
        var writer = new SwpFrameWriter();
        writer.WriteString("abc");

        byte[] encoded = writer.GetWrittenBytes().ToArray();

        Assert.Equal(new byte[] { 0x03, (byte)'a', (byte)'b', (byte)'c' }, encoded);
    }

    [Fact]
    public void NullableSize_Null_Is_Single_0x80()
    {
        var writer = new SwpFrameWriter();
        writer.WriteNullableSize(null);

        byte[] encoded = writer.GetWrittenBytes().ToArray();

        Assert.Equal(new byte[] { 0x80 }, encoded);
    }

    [Fact]
    public async Task Frame_RoundTrips_Through_MemoryStream()
    {
        var payload = new byte[] { 1, 2, 3 };
        var writer = new SwpFrameWriter();
        writer.WriteByte((byte)SwpMessageType.EndpointMessage);
        writer.WriteSize(payload.Length);
        byte[] header = writer.GetWrittenBytes().ToArray();

        using var stream = new MemoryStream();
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
        stream.Position = 0;

        var reader = new SwpFrameReader(stream);
        SwpFrame frame = await reader.ReadFrameAsync(CancellationToken.None);

        Assert.Equal((byte)SwpMessageType.EndpointMessage, frame.Type);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public async Task Oversized_Frame_Is_Rejected_Before_Allocating_Body()
    {
        var writer = new SwpFrameWriter();
        writer.WriteByte((byte)SwpMessageType.EndpointMessage);
        writer.WriteSize(RasWireConstants.MaxFrameSize + 1);
        using var stream = new MemoryStream(writer.GetWrittenBytes().ToArray());

        await Assert.ThrowsAsync<RasProtocolException>(() => new SwpFrameReader(stream).ReadFrameAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Frame_Size_Varint_Cannot_Wrap_Around()
    {
        using var stream = new MemoryStream([0x0E, 0x80, 0x80, 0x80, 0x80, 0x10]);

        await Assert.ThrowsAsync<RasProtocolException>(() => new SwpFrameReader(stream).ReadFrameAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Nullable_Size_Uses_Seven_Bit_Continuation_Bytes()
    {
        var writer = new SwpFrameWriter();
        writer.WriteNullableSize(16_384);
        using var stream = new MemoryStream(writer.GetWrittenBytes().ToArray());

        Assert.Equal(16_384, await new SwpFrameReader(stream).ReadNullableSizeAsync(CancellationToken.None));
    }
}
