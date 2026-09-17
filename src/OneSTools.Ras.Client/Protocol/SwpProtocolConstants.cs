namespace OneSTools.Ras.Client.Protocol;

/// <summary>
/// Константы протокола SWP: магическое число, версии, фрейминг.
/// Источник: docs/TRANSPORT_SPEC.md, разделы 2–3.
/// </summary>
public static class SwpProtocolConstants
{
    /// <summary>Магическое число протокола (0x1C535750), int32 big-endian.</summary>
    public const int Magic = 475223888;

    /// <summary>Версия протокола V1_0 (0x0100), short big-endian.</summary>
    public const short ProtocolVersion = 256;

    /// <summary>Версия кодека V1_0 (0x0100).</summary>
    public const short CodecVersion = 256;

    /// <summary>Имя протокола (используется в параметрах соединения).</summary>
    public const string Name = "swp";
}

/// <summary>
/// Типы сообщений уровня SWP (raw = номер байта в фрейме [type][size][body]).
/// Источник: docs/TRANSPORT_SPEC.md, раздел 3.1.
/// </summary>
public enum SwpMessageType : byte
{
    Negotiate = 0,
    Connect = 1,
    ConnectAck = 2,
    StartTls = 3,
    Disconnect = 4,
    SaslNegotiate = 5,
    SaslAuth = 6,
    SaslChallenge = 7,
    SaslSuccess = 8,
    SaslFailure = 9,
    SaslAbort = 10,
    EndpointOpen = 11,
    EndpointOpenAck = 12,
    EndpointClose = 13,
    EndpointMessage = 14,
    EndpointFailure = 15,
    KeepAlive = 16,
}

/// <summary>
/// Коды типов значений (ServiceWireType / TypeCodes), совпадают с protobuf ParamType.
/// Источник: docs/TRANSPORT_SPEC.md, раздел 3.3.
/// </summary>
public enum ServiceWireType : byte
{
    Boolean = 1,
    Byte = 2,
    Short = 3,
    Int = 4,
    Long = 5,
    Float = 6,
    Double = 7,
    Size = 8,
    NullableSize = 9,
    String = 10,
    Uuid = 11,
    Type = 12,
    EndpointId = 13,
}