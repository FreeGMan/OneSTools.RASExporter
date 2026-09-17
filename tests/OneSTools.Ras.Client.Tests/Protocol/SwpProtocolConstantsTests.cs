using OneSTools.Ras.Client.Protocol;

namespace OneSTools.Ras.Client.Tests.Protocol;

public sealed class SwpProtocolConstantsTests
{
    [Fact]
    public void Magic_Is_0x1C535750()
    {
        Assert.Equal(0x1C535750, SwpProtocolConstants.Magic);
        Assert.Equal(475223888, SwpProtocolConstants.Magic);
    }

    [Fact]
    public void Versions_Are_256()
    {
        Assert.Equal(256, SwpProtocolConstants.ProtocolVersion);
        Assert.Equal(256, SwpProtocolConstants.CodecVersion);
    }

    [Fact]
    public void MessageTypes_Cover_0_Through_16()
    {
        Assert.Equal(0, (byte)SwpMessageType.Negotiate);
        Assert.Equal(1, (byte)SwpMessageType.Connect);
        Assert.Equal(2, (byte)SwpMessageType.ConnectAck);
        Assert.Equal(11, (byte)SwpMessageType.EndpointOpen);
        Assert.Equal(14, (byte)SwpMessageType.EndpointMessage);
        Assert.Equal(16, (byte)SwpMessageType.KeepAlive);
        Assert.Equal(17, Enum.GetValues<SwpMessageType>().Length);
    }
}