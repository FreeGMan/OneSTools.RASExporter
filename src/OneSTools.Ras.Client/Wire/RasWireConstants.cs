namespace OneSTools.Ras.Client.Wire;

/// <summary>
/// Константы и маски бинарного кодека RAS wire-format (версия codec 256).
/// Источник: docs/CODEC_SPEC.md, раздел 1.
/// </summary>
public static class RasWireConstants
{
    /// <summary>Маркер null в кодеках nullable_size / typed value (0x80).</summary>
    public const byte NullByte = 0x80;

    /// <summary>Кодирование логического true (1 байт).</summary>
    public const byte TrueByte = 1;

    /// <summary>Кодирование логического false (1 байт).</summary>
    public const byte FalseByte = 0;

    /// <summary>Максимальный сдвиг для size-varint (7 бит на байт).</summary>
    public const int MaxShift = 7;

    /// <summary>Сдвиг полезных бит в первом байте nullable_size (6 бит).</summary>
    public const int NullShift = 6;

    /// <summary>Бит «продолжение» в size-varint (0x80).</summary>
    public const byte NextMask = 0x80;

    /// <summary>Бит «продолжение» в nullable_size (0x40).</summary>
    public const byte NullNextMask = 0x40;

    /// <summary>Маска полезных бит группы size-varint (0x7F).</summary>
    public const byte LsbMask = 0x7F;

    /// <summary>Маска полезных бит первого байта nullable_size (0x3F).</summary>
    public const byte NullLsbMask = 0x3F;

    /// <summary>Размеры фиксированных примитивов в байтах.</summary>
    public const int SizeOfShort = 2;
    public const int SizeOfInt = 4;
    public const int SizeOfLong = 8;
    public const int SizeOfUuid = 16;
    public const int SizeOfTime = 8;

    /// <summary>Число 100-наносекундных тиков между 0001-01-01 (база .NET) и
    /// 1970-01-01T00:00:00Z — используется в time-кодеке (R8-время от 0001-01-01).
    /// </summary>
    public const long AgeDelta = 621355968000000;

    // Защитные лимиты парсинга (docs/SECURITY.md): реакция на повреждённый/враждебный поток.

    /// <summary>Максимум байт в varint size (int32: 5 групп по 7 бит + контроль переполнения).</summary>
    public const int MaxSizeVarIntBytes = 5;

    /// <summary>Максимум байт в varint nullable_size (6+7k бит).</summary>
    public const int MaxNullableSizeVarIntBytes = 5;

    /// <summary>Потолок длины bytes/string из wire (64 MiB): защита от OOM на битом length.</summary>
    public const int MaxBytesLength = 64 * 1024 * 1024;

    /// <summary>Потолок числа элементов в size-prefix списках.</summary>
    public const int MaxListSize = 1_000_000;

    /// <summary>Максимальный SWP-кадр (256 MiB).</summary>
    public const int MaxFrameSize = 256 * 1024 * 1024;

    /// <summary>Максимальная глубина цепочки cause в сериализованных throwable.</summary>
    public const int MaxThrowableDepth = 32;
}