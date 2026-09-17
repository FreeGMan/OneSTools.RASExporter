namespace OneSTools.Ras.MockServer.Framing;

/// <summary>
/// Фрейм уровня SWP: [type: byte][size: LEB128 varint][body].
/// Источник: docs/TRANSPORT_SPEC.md, раздел 3.
/// </summary>
public readonly record struct SwpFrame(byte Type, byte[] Payload)
{
    public static SwpFrame Empty(byte type) => new(type, Array.Empty<byte>());
}