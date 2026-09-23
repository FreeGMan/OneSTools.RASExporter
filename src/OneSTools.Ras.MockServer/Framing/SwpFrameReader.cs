using System.Buffers.Binary;
using System.Text;
using OneSTools.Ras.Client.Protocol;

namespace OneSTools.Ras.MockServer.Framing;

/// <summary>
/// Чтение байтов wire-формата RAS/SWP из потока: фреймы [type][size][body],
/// LEB128, nullable_size и строки.
/// Источник: docs/TRANSPORT_SPEC.md разделы 3.2–3.4.
/// </summary>
public sealed class SwpFrameReader
{
    private readonly Stream _stream;

    public SwpFrameReader(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>Читает N байт целиком (бросает EndOfStreamException при недоумении потока).</summary>
    public async Task<byte[]> ReadBytesAsync(int count, CancellationToken cancellationToken)
    {
        var result = new byte[count];
        int readTotal = 0;
        while (readTotal < count)
        {
            int read = await _stream.ReadAsync(result.AsMemory(readTotal, count - readTotal), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            readTotal += read;
        }

        return result;
    }

    /// <summary>Читает LEB128 size-varint: 7 бит на байт, бит 0x80 — продолжение.</summary>
    public async Task<int> ReadVarIntAsync(CancellationToken cancellationToken)
    {
        int value = 0;
        int shift = 0;
        while (true)
        {
            var raw = await _stream.ReadByteAsync(cancellationToken);
            value |= (raw & 0x7F) << shift;
            if ((raw & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
            if (shift > 28)
            {
                throw new InvalidDataException("LEB128 varint слишком длинный");
            }
        }
    }

    /// <summary>Читает nullable_size: 0x80 = null, иначе 6 бит + 0x40-продолжение.</summary>
    public async Task<int?> ReadNullableSizeAsync(CancellationToken cancellationToken)
    {
        var first = await _stream.ReadByteAsync(cancellationToken);
        if (first == 0x80)
        {
            return null;
        }

        int value = first & 0x3F;
        int shift = 6;
        if ((first & 0x40) != 0)
        {
            while (true)
            {
                var next = await _stream.ReadByteAsync(cancellationToken);
                value |= (next & 0x3F) << shift;
                if ((next & 0x40) == 0)
                {
                    break;
                }

                shift += 6;
                if (shift > 30)
                {
                    throw new InvalidDataException("nullable varint слишком длинный");
                }
            }
        }

        return value;
    }

    /// <summary>Читает string: nullable-size + UTF-8.</summary>
    public async Task<string?> ReadStringAsync(CancellationToken cancellationToken)
    {
        int? length = await ReadNullableSizeAsync(cancellationToken);
        if (length is null)
        {
            return null;
        }

        byte[] bytes = await ReadBytesAsync(length.Value, cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Читает фрейм: [type: byte][size: LEB128][body]. Negotiate не входит сюда.</summary>
    public async Task<SwpFrame> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte type = await _stream.ReadByteAsync(cancellationToken);
        int size = await ReadVarIntAsync(cancellationToken);
        byte[] payload = size == 0
            ? Array.Empty<byte>()
            : await ReadBytesAsync(size, cancellationToken);
        return new SwpFrame(type, payload);
    }

    public async Task<short> ReadShortAsync(CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(2, cancellationToken);
        return BinaryPrimitives.ReadInt16BigEndian(bytes);
    }

    public async Task<int> ReadIntAsync(CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(4, cancellationToken);
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    /// <summary>
    /// Пропускает типизированное значение: [type: byte ServiceWireType][тело по типу].
    /// </summary>
    public async Task SkipTypedValueAsync(CancellationToken cancellationToken)
    {
        var type = await _stream.ReadByteAsync(cancellationToken);
        switch (type)
        {
            case (byte)ServiceWireType.Boolean:
            case (byte)ServiceWireType.Byte:
            case (byte)ServiceWireType.Type:
                await ReadBytesAsync(1, cancellationToken);
                break;
            case (byte)ServiceWireType.Short:
                await ReadBytesAsync(2, cancellationToken);
                break;
            case (byte)ServiceWireType.Int:
            case (byte)ServiceWireType.Float:
                await ReadBytesAsync(4, cancellationToken);
                break;
            case (byte)ServiceWireType.Long:
            case (byte)ServiceWireType.Double:
            case (byte)ServiceWireType.Uuid:
                await ReadBytesAsync(type == (byte)ServiceWireType.Uuid ? 16 : 8, cancellationToken);
                break;
            case (byte)ServiceWireType.Size:
                await ReadVarIntAsync(cancellationToken);
                break;
            case (byte)ServiceWireType.NullableSize:
            case (byte)ServiceWireType.EndpointId:
                await ReadNullableSizeAsync(cancellationToken);
                break;
            case (byte)ServiceWireType.String:
                int? length = await ReadNullableSizeAsync(cancellationToken);
                if (length is { } len)
                {
                    await ReadBytesAsync(len, cancellationToken);
                }

                break;
            case 0x80:
                // Терминатор null для параметров-значений
                break;
            default:
                throw new InvalidDataException($"Неизвестный тип параметра: {type}");
        }
    }
}

/// <summary>Внутренние расширения потока для mock-сервера.</summary>
internal static class StreamExtensions
{
    public static async Task<byte> ReadByteAsync(this Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        int read = await stream.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            throw new EndOfStreamException();
        }

        return buffer[0];
    }
}