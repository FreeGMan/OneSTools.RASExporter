using System;
using System.IO;
using System.IO.Compression;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Wire;

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

    public static byte[] Inflate(ReadOnlySpan<byte> source, int maxOutputBytes = RasWireConstants.MaxBytesLength)
    {
        if (source.IsEmpty) return Array.Empty<byte>();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputBytes);

        using var input = new MemoryStream(source.ToArray());
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = deflate.Read(buffer)) != 0)
        {
            if (read > maxOutputBytes - output.Length)
                throw new RasProtocolException($"Deflate payload exceeds {maxOutputBytes} bytes");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
