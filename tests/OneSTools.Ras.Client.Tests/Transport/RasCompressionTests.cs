using System;
using System.Text;
using OneSTools.Ras.Client.Transport;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Transport;

public sealed class RasCompressionTests
{
    [Fact]
    public void Empty_Array_Roundtrips()
    {
        var compressed = RasCompression.Deflate(ReadOnlySpan<byte>.Empty);
        Assert.Empty(compressed);

        var decompressed = RasCompression.Inflate(ReadOnlySpan<byte>.Empty);
        Assert.Empty(decompressed);
    }

    [Fact]
    public void String_Payload_Deflates_And_Inflates()
    {
        string text = "Hello world! 1C:Enterprise RAS Protocol SWP Endpoint compression test! " +
                      "Repeating some text to make sure compression ratio is meaningful. " +
                      "1234567890 1234567890 1234567890.";
        byte[] original = Encoding.UTF8.GetBytes(text);

        byte[] compressed = RasCompression.Deflate(original);
        Assert.True(compressed.Length < original.Length);

        byte[] decompressed = RasCompression.Inflate(compressed);
        Assert.Equal(original, decompressed);
        Assert.Equal(text, Encoding.UTF8.GetString(decompressed));
    }
}
