using System;
using System.IO;
using System.IO.Compression;

namespace OneSTools.Ras.Client.Transport;

/// <summary>
/// Реализация сжатия/распаковки сырого Deflate (RFC 1951),
/// соответствующая Java-реализации ZlibChannelBufferDeflater/Inflater (nowrap = true).
/// </summary>
public static class RasCompression
{
    public static byte[] Deflate(ReadOnlySpan<byte> source, CompressionLevel level = CompressionLevel.Optimal)
    {
        if (source.IsEmpty) return Array.Empty<byte>();

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
        {
            deflate.Write(source);
        }
        return output.ToArray();
    }

    public static byte[] Inflate(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty) return Array.Empty<byte>();

        // В .NET ReadOnlySpan<byte> можно обернуть в ReadOnlyMemoryStream / MemoryStream
        using var input = new MemoryStream(source.ToArray());
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}
