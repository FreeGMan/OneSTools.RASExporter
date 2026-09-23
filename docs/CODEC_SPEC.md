# CODEC_SPEC.md — Кодеки RAS wire format (для C#)

> Фаза 4. Каждый кодек: алгоритм, hex-пример, C#-сигнатура.
> Источники: [v8platform/encoder](https://github.com/v8platform/encoder) (`ras/codec256/{encode,decode}.go`, `encode_func.go`, `decode_func.go`, `utils.go`, `tag.go`, `options.go`, `marshal.go`); Java `com._1c.v8.swp.codec.*` и `com._1c.v8.swp.internal.codec.*` (официальная библиотека 1С); сгенерённые `*_ras.pb.go`.

## 0. Сводка ответов на контрольные вопросы

- **`int32` — это НЕ varint**. Классический фиксированный **4 байта big-endian**, unsigned-биты (`binary.BigEndian.PutUint32`, Java `BigEndianEncoder.encodeInt32`).
- **`size` — это varint (LEB128)**: 7 бит на байт, старший бит (0x80) = «продолжение», группы идут от младшей к старшей (как protobuf unsigned varint). Отличается от `int32`. Памяти на поле `<127` — 1 байт.
- **`nullable_size`** — вариация `size` для nullable-длины: в первом байте только 6 бит данных, бит 0x40 = «продолжение», 0x80 в первом байте = **null**.
- **`float`/`double`** — IEEE-754, битовое представление (`Float.floatToIntBits` / `Double.doubleToLongBits`), упаковано как `int32`/`long` big-endian (4/8 байт).
- **`uuid`** — 16 сырых байт в RFC 4122 network byte order (time_low, time_mid, time_hi_and_version BE, затем node BE).
- **`endpoint_id`** — это просто обёртка над `int` (`EndpointId.id`), на проводе = **`nullable_size`**.
- **Null-маркер. ⚠ РАСХОЖДЕНИЕ оригиналов:**
  - Java-(эталон 1C): `encodeNull()` пишет **0x80** (`NULL_BYTE = -128`). `decodeNullableSize`/`decodeType`: байт с битом 0x80 и нулевой полезной нагрузкой = `null`; байт с 0x80 и ненулевой нагрузкой → ошибка «null expected».
  - Go `writeNull` пишет **0x00** и никогда не эмитит 0x80 (`FormatByte`/`FormatType` переотображают значение 0x80 → записывают 0x00). 0x00 декодируется Go как размер 0 (пустая строка), т.е. Go не различает «пусто» и «null».
  - **Рекомендация для C#-порта, говорящего с реальным сервером 1C: null = 0x80 (Java).** Протокол версии 256 определён реализацией 1C (`BinaryCodecV1_0.getVersion() = 256`, `codec256.Version() = 256`).

## 1. Константы и маски (идентичны в Go `utils.go` и Java `BinaryCodecV1_0`)

| Go | Java | Значение | C# |
|----|------|----------|----|
| `NULL_BYTE` | `NULL_BYTE` | `0x80` (128 / -128) | `const byte NullByte = 0x80;` |
| `TRUE_BYTE` | `TRUE_BYTE` | `1` | `const byte TrueByte = 1;` |
| `FALSE_BYTE` | `FALSE_BYTE` | `0` | `const byte FalseByte = 0;` |
| `MAX_SHIFT` | `MAX_SHIFT` | `7` | `const int MaxShift = 7;` |
| `NULL_SHIFT` | `NULL_SHIFT` | `6` | `const int NullShift = 6;` |
| `NEXT_MASK` | `NEXT_MASK` | `-128` (=0x80 как int) | `const int NextMask = 0x80;` |
| `NULL_NEXT_MASK` | `NULL_NEXT_MASK` | `64` (0x40) | `const int NullNextMask = 0x40;` |
| `LSB_MASK` | `LSB_MASK` | `127` (0x7F) | `const int LsbMask = 0x7F;` |
| `NULL_LSB_MASK` | `NULL_LSB_MASK` | `63` (0x3F) | `const int NullLsbMask = 0x3F;` |
| `SIZEOF_SHORT/INT/LONG` | `SIZEOF_*` | 2 / 4 / 8 | — |
| `AgeDelta` | — (Go; то же поле в Java-формате дат) | `621355968000000` | `const long AgeDelta = 621355968000000;` |

`AgeDelta` = число 100-мкс-тиков между 0001-01-01 (база .NET) и 1970-01-01T00:00:00Z (= 0x0002351EB5C07000).

## 2. Таблица кодеков (заполненная)

| # | Имя (из .proto encoder) | Описание | C#-сигнатура |
|---|---------------------------|----------|--------------|
| 1 | `byte` | 1 байт; Go переотображает 0x80→0x00 (null) | `public static void WriteByte(BinaryWriter w, byte value)` |
| 2 | `short` | 2 байта big-endian | `public static void WriteShort(BinaryWriter w, short value)` |
| 3 | `int32` | 4 байта big-endian (не varint!) | `public static void WriteInt32(BinaryWriter w, int value)` |
| 4 | `long` | 8 байт big-endian | `public static void WriteLong(BinaryWriter w, long value)` |
| 5 | `size` | varint LEB128 (7 бит/байт, бит 0x80 = продолжение) | `public static void WriteSize(BinaryWriter w, int value)` |
| 6 | `bytes` | сырые байты БЕЗ префикса (длина из отдельного поля `size`) | `public static void WriteBytes(BinaryWriter w, byte[] value)` |
| 7 | `string` | `nullable_size(len)` + UTF-8; пустая = 0x00, null = 0x80 (Java) | `public static void WriteString(BinaryWriter w, string value)` |
| 8 | `uuid` | 16 байт, RFC network order | `public static void WriteUuid(BinaryWriter w, Guid value)` |
| 9 | `float`/`double` | IEEE-754, BE | `public static void WriteFloat(BinaryWriter w, float value)` / `public static void WriteDouble(BinaryWriter w, double value)` |
| 10 | `nullable_size` | 6-бит LSB, бит 0x40 = продолжение, 0x80 = null | `public static void WriteNullableSize(BinaryWriter w, int? value)` |
| 11 | `type` | 1 байт (1..13), null = 0x80 | `public static void WriteType(BinaryWriter w, ServiceWireType? value)` |
| 12 | `endpoint_id` | то же, что `nullable_size` (int-обёртка) | `public static void WriteEndpointId(BinaryWriter w, int? endpointId)` |
| + | `bool` | 1 байт: 0 / 1 | `public static void WriteBool(BinaryWriter w, bool value)` |
| + | `time` | `long` = `ms*10 + AgeDelta`, BE | `public static void WriteTime(BinaryWriter w, DateTime value)` |
| + | `byte array` | Java composite: `size` + байты | `public static void WriteByteArray(BinaryWriter w, byte[] value)` |
| + | `char` | = `short` (2 байта BE) | `public static void WriteChar(BinaryWriter w, char value)` |

Методы чтения (по соглашению из шаблона): `public static byte ReadByte(BinaryReader r)`, `public static short ReadShort(BinaryReader r)`, `public static int ReadInt32(BinaryReader r)`, `public static long ReadLong(BinaryReader r)`, `public static int ReadSize(BinaryReader r)`, `public static int? ReadNullableSize(BinaryReader r)`, `public static byte[] ReadBytes(BinaryReader r, int length)`, `public static string ReadString(BinaryReader r)`, `public static Guid ReadUuid(BinaryReader r)`, `public static float ReadFloat(BinaryReader r)`, `public static double ReadDouble(BinaryReader r)`, `public static bool ReadBool(BinaryReader r)`, `public static ServiceWireType? ReadType(BinaryReader r)`, `public static int? ReadEndpointId(BinaryReader r)`, `public static DateTime ReadTime(BinaryReader r)`, `public static byte[] ReadByteArray(BinaryReader r)`, `public static char ReadChar(BinaryReader r)`.

⚠ Внимание: `BinaryReader`/`BinaryWriter` .NET по умолчанию **little-endian**. Все числовые кодеки пишутся вручную байт за байтом big-endian (аналог Go `binary.BigEndian` / Java `BigEndianEncoder`).

---

## 3. Детали кодеков

### 3.1 byte

- **Go:** `FormatByte` / `ParseByte`; **Java:** `encodeByte` / `decodeByte`, `decodeUnsignedByte`.
- **Алгоритм:** 1 байт.
  ```go
  encode: if (val == NULL_BYTE /*0x80*/) → writeNull()   // Go пишет 0x00
          else → write байт val
  decode: b1 = readByte(); вернуть b1 (Go: int(b1&0xFF); Java: сырой byte)
  ```
- **Пример:** `0x7F → [7F]`; Go `0x80 → [00]`; Java `0x80 → [80]`.
- **C#:** `public static void WriteByte(BinaryWriter w, byte value)`; `public static byte ReadByte(BinaryReader r)`.
- **Особенность:** в Go значение 0x80 считается «null» и перекодируется в 0x00; Java пишет значение как есть. Для реального сервера 1C следуй Java (0x80 — обычный байт). `decodeUnsignedByte()` в Java = `(short)(readByte() & 0xFF)`.

### 3.2 short (= `char`)

- **Go:** `FormatShort` / `ParseShort` (регистрация: `char short int16 uint16` — тот же кодек, что `char` в Java); **Java:** `encodeShort` / `decodeShort`, `encodeChar` / `decodeChar`, `decodeUnsignedShort`.
- **Алгоритм:** 2 байта big-endian (`binary.BigEndian.PutUint16`, Java `BigEndianEncoder.encodeInt16`).
  ```
  buffer[0] = (val >> 8) & 0xFF;  buffer[1] = val & 0xFF;
  ```
- **Пример:** `0x1234 → [12 34]`; `256 (0x0100) → [01 00]`.
- **C#:** `public static void WriteShort(BinaryWriter w, short value)`; `public static short ReadShort(BinaryReader r)`. Для unsigned: `ReadUnsignedShort → (int)(ReadShort() & 0xFFFF)`.
- **Особенность:** значение хранится как unsigned 16 бит; Go/Java умеют раскладывать из любого типа (`int/long` → `uint16`). Поля `format`/`protocol`/`version` (типа 256) кодируются именно этим кодеком.

### 3.3 int32

- **Go:** `FormatInt` / `ParseInt` (регистрация: `int int32 uint32`); **Java:** `encodeInt` / `decodeInt`, `decodeUnsignedInt`.
- **Алгоритм:** 4 байта **big-endian**, unsigned-биты значения.
  ```
  buffer[0] = (val >> 24) & 0xFF;  buffer[1] = (val >> 16) & 0xFF;
  buffer[2] = (val >> 8) & 0xFF;   buffer[3] = val & 0xFF;
  ```
- **Пример:** `0x12345678 → [12 34 56 78]`; `300 → [00 00 01 2C]`; Magic `475223888 = 0x1C535750 → [1C 53 57 50]`.
- **C#:** `public static void WriteInt32(BinaryWriter w, int value)`; `public static int ReadInt32(BinaryReader r)`.
- **Особенность:** НЕ varint (в .proto поле помечено `varint`, но wire-реализация — фикс. 4 байта). Может кодировать `bool` (Go: 0/1). `decodeUnsignedInt` = `(long)(ReadInt32() & 0xFFFFFFFF)`.

### 3.4 long

- **Go:** `FormatLong` / `ParseLong` (регистрация: `int64 uint64 long`); **Java:** `encodeLong` / `decodeLong`.
- **Алгоритм:** 8 байт big-endian (`binary.BigEndian.PutUint64`, Java `BigEndianEncoder.encodeInt64`).
- **Пример:** `0x0102030405060708 → [01 02 03 04 05 06 07 08]`; `1 → [00 00 00 00 00 00 00 01]`.
- **C#:** `public static void WriteLong(BinaryWriter w, long value)`; `public static long ReadLong(BinaryReader r)`.
- **Особенность:** unsigned-биты; Go может читать в `int16/int32/...` через усечение.

### 3.5 size (сложный кодек, varint LEB128)

- **Go:** `FormatSize` / `ParseSize` (также `encodeSize`/`decodeSize` в `encode_func.go`/`decode_func.go`); **Java:** `encodeSize` / `decodeSize`.
- **Алгоритм:** переменная длина, 7 бит данных на байт, старший бит 0x80 = «есть следующий байт». Младшая группа пишется первой (little-endian базы 128 = protobuf unsigned varint).

  ```csharp
  // WriteSize — псевдокод из FormatSize/encodeSize
  void WriteSize(int value)
  {
      int msb = value >> 7;
      writeByte((byte)((msb != 0 ? 0x80 : 0x00) | (value & 0x7F)));
      for (value = msb; value > 0; value = msb)
      {
          msb >>= 7;
          writeByte((byte)((msb != 0 ? 0x80 : 0x00) | (value & 0x7F)));
      }
  }

  // ReadSize — псевдокод из ParseSize/decodeSize
  int ReadSize()
  {
      int cur = readByte() & 0xFF;
      int size = cur & 0x7F;
      for (int shift = 7; (cur & 0x80) != 0; shift += 7)
      {
          cur = readByte() & 0xFF;
          size += (cur & 0x7F) << shift;
      }
      return size;
  }
  ```
- **Примеры (посчитаны из алгоритма):**
  | значение | байты | разбор |
  |----------|-------|-------|
  | 0 | `00` | 0 |
  | 1 | `01` | 1 |
  | 127 | `7F` | 127 (1 байт, без продолжения) |
  | 128 | `80 01` | 0 + 1<<7 |
  | 300 | `AC 02` | 0x2C=44 + (2<<7)=256 → 300 |
  | 16384 | `80 80 01` | 0 + 0<<7 + (1<<14) |
  | 2097151 | `FF FF FF` | 0x7F + (0x7F<<7)=(0x7F<<14) |
  | 30000 | `B0 EA 01` | 0x30=48 + (0x6A<<7)=13568 + (1<<14)=16384 |
- **C#:** `public static void WriteSize(BinaryWriter w, int value)`; `public static int ReadSize(BinaryReader r)`.
- **Особенность:** используется для количества элементов в списках (`FormatSize(writer, len(x.Licenses))`), размера payload в пакете (`Packet.Size`), длины массива. Декодирование прекращается, когда бит 0x80 очередного байта сброшен.

### 3.6 bytes (raw) и byte array (size-prefixed)

- **Go:** `FormatBytes` / `ParseBytes` (raw, без длины); **Java:** `encodeByteArray`/`decodeByteArray` (size-prefixed).
- **Алгоритм:**
  - Go `bytes`: просто записать массив как есть. Длина берётся из **отдельного** предшествующего поля `size` (в пакете см. `packet_ras.pb.go`: поле `Data opts: encoder:"bytes" type_field:1 size_field:2`). `ParseBytes` читает ровно `len(data)` байт; если `len==0` — обои-чтение всего остатка (`io.ReadAll`).
  - Java `byte array`: `encodeSize(len)` + байты; чтение: `len = decodeSize(); readBytes(len)`.
- **Пример:** массив `[0xDE,0xAD]`:
  - Go `bytes` (длина уже была в `size`): `DE AD` — только байты.
  - Java `byte array`: `02 DE AD` (size=2 + байты).
- **C#:** raw: `public static void WriteBytes(BinaryWriter w, byte[] value)`; `public static byte[] ReadBytes(BinaryReader r, int length)`. Size-prefixed: `public static void WriteByteArray(BinaryWriter w, byte[] value)`; `public static byte[] ReadByteArray(BinaryReader r)`.
- **Особенность:** в Go-мире `bytes` — бесконечный «остаток потока» или raw после отдельного `size`; length-prefixed вариант существует только в Java `encodeByteArray`. Для message `EndpointDataMessage.Bytes` применяется raw (`io.ReadAll(reader)`).

### 3.7 string

- **Go:** `FormatString` / `ParseString` (также `encodeString`/`decodeString`); **Java:** `encodeString` / `decodeString`.
- **Алгоритм:**
  ```csharp
  void WriteString(string value)
  {
      if (value == null) writeByte(0x80);          // Java-эталон: null (Go отличия нет — см. §0)
      else {
          byte[] utf8 = Encoding.UTF8.GetBytes(value);
          WriteNullableSize(utf8.Length);          // пустая строка → WriteNullableSize(0) → [00]
          writeBytes(utf8);
      }
  }

  string ReadString()
  {
      int? len = ReadNullableSize();
      if (len == null) return null;               // только 0x80
      return Encoding.UTF8.GetString(readBytes(len.Value));
  }
  ```
  Go: пустая строка (len==0) → `writeNull()` (0x00) без payload. Эталон Java: `null → 0x80`; `"" → encodeNullableSize(0)` = 0x00 + 0 байт.
- **Пример:** `"hello" → [05 68 65 6C 6C 6F]`; `"" → [00]`; `null` (Java) → `[80]`.
- **C#:** `public static void WriteString(BinaryWriter w, string value)`; `public static string ReadString(BinaryReader r)`.
- **Особенность:** кодировка UTF-8; длина-префикс = `nullable_size` (6-бит LSB). Декодирование нельзя читать сырым `BinaryReader.ReadString()`/.

### 3.8 uuid

- **Go:** `FormatUuid` / `ParseUUID`; **Java:** `encodeUuid` / `decodeUuid`.
- **Алгоритм:** 16 байт в RFC 4122 network order. Go пишет `uuid.UUID.Bytes()` (массив из 16 байт); Java пишет `encodeLong(msb); encodeLong(lsb)` — те же 16 байт BE.
  ```csharp
  void WriteUuid(Guid value)
  {
      byte[] n = value.ToByteArray();            // GUID в .NET — смешанный порядок!
      // переставить в network order: первые 3 поля big-endian, последние 2 как есть
      writeBytes(networkOrderBytes);
  }

  Guid ReadUuid()
  {
      byte[] b = readBytes(16);
      // 4 байта лидирующего поля — BE; реконструировать Guid вручную
  }
  ```
- **Пример:** `123e4567-e89b-12d3-a456-426614174000 → [12 3E 45 67 E8 9B 12 D3 A4 56 42 66 14 17 40 00]`.
- **C#:** `public static void WriteUuid(BinaryWriter w, Guid value)`; `public static Guid ReadUuid(BinaryReader r)`.
- **Особенность:** порядок байт — как в строковом представлении UUID (первые 4 байта = time_low BE). `Guid.ToByteArray()` в .NET использует little-endian для первых 3 компонентов — **нельзя писать напрямую**, нужна ручная перестановка.

### 3.9 float / double

- **Go:** `FormatFloat`/`ParseFloat`, `FormatDouble`/`ParseDouble`; **Java:** `encodeFloat`/`decodeFloat`, `encodeDouble`/`decodeDouble`.
- **Алгоритм:** битовая переинтерпретация IEEE-754:
  - `float` → `uint32 = Float.floatToIntBits(value)/math.Float32bits` → кодируется как **int32** (4 байта BE).
  - `double` → `uint64 = Double.doubleToLongBits/math.Float64bits` → кодируется как **long** (8 байт BE).
- **Пример:** `1.0f → [3F 80 00 00]`; `-2.5f → [C0 20 00 00]`; `1.0 → [3F F0 00 00 00 00 00 00]`.
- **C#:** `public static void WriteFloat(BinaryWriter w, float value)` (через `BitConverter.SingleToInt32Bits`, big-endian); `public static double WriteDouble(BinaryWriter w, double value)` (через `BitConverter.DoubleToInt64Bits`); чтение — обратно через `Int32BitsToSingle`/`Int64BitsToDouble`.
- **Особенность:** на проводе — битовые паттерны IEEE-754 (не десятичная строка), big-endian. NaN/signed zero сохраняются.

### 3.10 nullable_size

- **Go:** `FormatNullable` / `ParseNullable` (регистрация `null-size nullable`; `encodeNullableSize`/`decodeNullableSize`); **Java:** `encodeNullableSize` / `decodeNullableSize`.
- **Алгоритм:** LEB128, но первый байт несёт **6 бит** данных, бит **0x40** = «продолжение», бит **0x80** в первом байте = null.
  ```csharp
  void WriteNullableSize(int? value)
  {
      if (value == null) { writeByte(0x80); return; }   // Java-эталон
      int val = value.Value;
      int msb = val >> 6;
      writeByte((byte)((msb != 0 ? 0x40 : 0x00) | (val & 0x7F)));
      for (val = msb; val > 0; val = msb)
      {
          msb >>= 7;
          writeByte((byte)((msb != 0 ? 0x80 : 0x00) | (val & 0x7F)));
      }
  }

  int? ReadNullableSize()
  {
      int cur = readByte() & 0xFF;
      if ((cur & 0x80) != 0)
      {
          if ((cur & 0x7F) != 0) throw /* "null expected" */;
          return null;
      }
      int size = cur & 0x3F;
      if ((cur & 0x40) != 0)
      {
          int shift = 6;
          cur = readByte() & 0xFF;
          size += (cur & 0x7F) << shift;
          for (shift += 7; (cur & 0x80) != 0; shift += 7)
          {
              cur = readByte() & 0xFF;
              size += (cur & 0x7F) << shift;
          }
      }
      return size;
  }
  ```
  Примечание (Go-вариант): в Go «null» = 0x00 (0х80 не эмитится, `ParseNullable` возвращает 0), поэтому для строгого соответствия 1C используй Java-семантику.
- **Примеры:**
  | значение | байты | разбор |
  |----------|-------|-------|
  | 0 | `00` | LSB=0, нет продолжения |
  | 1 | `01` | |
  | 63 | `3F` | 6 бит полностью |
  | 64 | `40 01` | LSB=0 + 1<<6 |
  | 127 | `7F 01` | LSB=63 + 1<<6 = 127 |
  | 300 | `6C 04` | LSB=44 + (4<<6)=256 |
  | 30000 | `70 D4 03` | 48 + (0x54<<6)=5376 + (3<<13)=24576 |
  | null (Java) | `80` | бит 0x80, нагрузка 0 |
- **C#:** `public static void WriteNullableSize(BinaryWriter w, int? value)`; `public static int? ReadNullableSize(BinaryReader r)`.
- **Особенность:** служит префиксом длины для `string` и кодеком для `endpoint_id`. Декодер бросает исключение, если встречен байт с 0x80 и ненулевой нагрузкой (и Java, и Go).

### 3.11 type (ServiceWireType)

- **Go:** `FormatType` / `ParseType` (также `encodeType`/`decodeType`); **Java:** `encodeType` / `decodeType`.
- **Алгоритм:** 1 байт — raw-код типа.
  ```csharp
  void WriteType(ServiceWireType? value)
  {
      if (value == null) writeByte(0x80);      // Java: null (Go: writeNull → 0x00)
      else writeByte((byte)value.Raw());
  }

  ServiceWireType? ReadType()
  {
      int cur = readByte() & 0xFF;
      if ((cur & 0x80) != 0)
      {
          if ((cur & 0x7F) != 0) throw /* "type expected" */;
          return null;
      }
      ServiceWireType t = ServiceWireType.FromByte((byte)cur);
      if (t == null) throw /* "type expected" */;
      return t;
  }
  ```
  Go `ParseType` (в `decode_func.go`) читает байт без null-проверки и кладёт в `*byte`; Java — строго по enum.
- **Коды типов** (`ServiceWireType.java` = `TypeCodes.java`):
  | код | тип |
  |-----|-----|
  | 1 | `BOOLEAN` |
  | 2 | `BYTE` |
  | 3 | `SHORT` |
  | 4 | `INT` |
  | 5 | `LONG` |
  | 6 | `FLOAT` |
  | 7 | `DOUBLE` |
  | 8 | `SIZE` |
  | 9 | `NULLABLE_SIZE` |
  | 10 | `STRING` |
  | 11 | `UUID` |
  | 12 | `TYPE` |
  | 13 | `ENDPOINT_ID` |
- **Пример:** `INT → [04]`; `null → [80]` (Java) / `[00]` (Go).
- **C#:** enum `ServiceWireType` со свойством `Raw`; `public static void WriteType(BinaryWriter w, ServiceWireType? value)`; `public static ServiceWireType? ReadType(BinaryReader r)`.

### 3.12 endpoint_id

- **Go:** в сгенерённых сообщениях поля `EndpointId` используют `FormatNullable`/`ParseNullable` (см. `endpoint_ras.pb.go`: `EndpointClose.EndpointId opt: encoder:"nullable"`). **Java:** `encodeEndpointId` / `decodeEndpointId`.
- **Алгоритм:** `EndpointId` — обёртка над одним `int` ("`id`"). На проводе — **nullable_size** значения `id`:
  ```java
  encodeEndpointId: encodeNullableSize(val == null ? null : val.toInteger());
  decodeEndpointId: Integer id = decodeNullableSize();
                    return id != null ? new EndpointId(id) : null;
  ```
- **Пример:** `EndpointId(0x2C=44) → [2C]`; `EndpointId(300) → [6C 04]`; `null → [80]`.
- **C#:** `public static void WriteEndpointId(BinaryWriter w, int? endpointId)` = делегирует `WriteNullableSize`; `public static int? ReadEndpointId(BinaryReader r)` делегирует `ReadNullableSize`. Класс `EndpointId` (int + `CompareTo`, `Equals`, `ToString` hex) — не обязателен.
- **Особенность:** nullable: сигнал о том, что эндпоинт закрыт/отсутствует, кодируется null.

### 3.13 bool

- **Go:** `FormatBool` / `ParseBool`; **Java:** `encodeBoolean` / `decodeBoolean`.
- **Алгоритм:** 1 байт: `false = 0x00`, `true = 0x01`. Go также принимает `int` (0/1); Java `decodeBoolean` кидает `ServiceWireCodecException("boolean expected")` при значении ≠0 и ≠1 (Go тихо даёт false).
- **Пример:** `true → [01]`; `false → [00]`.
- **C#:** `public static void WriteBool(BinaryWriter w, bool value)`; `public static bool ReadBool(BinaryReader r)` (с проверкой 0/1 по Java).

### 3.14 time

- **Go-only в ref-наборе** (Java `IServiceWireEncoder` не содержит encodeTime; поля дат в реальном протоколе идут как long). Go: `FormatTime` / `ParseTime`, `encodeTime`/`decodeTime`.
- **Алгоритм:**
  ```
  encode: ms   = unixNano / 1_000_000          // миллисекунды от 1970-01-01
          ticks = ms * 10 + AgeDelta            // AgeDelta = 0x0002351EB5C07000
          WriteLong(ticks)
  decode: ticks = ReadLong()
          ms   = (ticks - AgeDelta) / 10
          время = 1970-01-01 + ms миллисекунд
  ```
- **Пример:** `1970-01-01T00:00:00Z → [00 02 35 1E B5 C0 70 00]`; `2021-01-01T00:00:00Z → [00 02 43 C2 06 30 D0 00]`.
- **C#:** `public static void WriteTime(BinaryWriter w, DateTime value)` (использовать `DateTimeOffset.ToUnixTimeMilliseconds`, добавить `AgeDelta`, `WriteLong`); `public static DateTime ReadTime(BinaryReader r)`.
- **Особенность:** сетка 1 мс (декодирование усекает до мс); за точность до `AgeDelta` отвечает константа.

---

## 4. Составные кодеки (утилиты)

Из Java `CodecUtils.java` (протокольные композиции):

- **string array (Go: список строк):** `WriteSize(n)` + n раз `WriteString`. Чтение: `n = ReadSize()`.
- **typed value (`encodeTypedValue`/`decodeTypedValue`):** `WriteType(type)` на null → `WriteType(null)`; иначе тип + значение:
  - BOOLEAN → bool; BYTE → byte; SHORT → short; INT → int32; LONG → long;
  - STRING → string; TYPE → тип; ENDPOINT_ID → endpoint_id.
  - `detectType(Class)`: см. таблицу кодов типов §3.11.
- Примеры: `encodeTypedValue((Integer)300)` → `[04] [00 00 01 2C]` (INT + int32 BE); `null` → `[80]`.

## 5. Авто-сериализатор структур (рефлексия)

Файлы `tag.go`, `options.go`, `marshal.go` (`marshal.go` — заглушка; логика в `encoder.go`/`decode_func.go`).

- **Тег** — namespace `rac`, формат `rac:"<codec>[,<order>[,<version>]]"`:
  - `<codec>` — имя кодек-функции (`byte`, `short`, `int int32 uint32`, `string`, `size`, `nullable`, `type`, `bool`, `uuid`, `time`, …) — регистрируются в картах `encoderFunc`/`decoderFunc` (`RegisterEncoderType`/`RegisterDecoderType`).
  - `<order>` — позиция поля в потоке (поля сериализуются **позиционно в порядке возрастания order**, НЕ tag-value как в protobuf).
  - `<version>` — минимальная версия протокола: если `codecField.Version > version`, поле пропускается.
  - `-` / пустой тег → поле игнорируется.
- **Дефолтный кодек по типу Go-поля** (`encodeBasic`):
  | Go-тип | кодек |
  |--------|-------|
  | `string` | encodeString (nullable_size + UTF-8) |
  | `bool` | encodeBool |
  | `int/uint/int32/uint32` | encodeUint32 = int32 BE |
  | `int16/uint16` | encodeUint16 = short BE |
  | `int64/uint64` | encodeUint64 = long BE |
  | `int8/uint8` | encodeByte |
  | `float32` / `float64` | encodeFloat32 / encodeFloat64 |
- **Слайсы** (`encodeSlice`): `WriteSize(len)` + каждый элемент тем же кодеком.
- **Структуры** (`encodeStruct`): проход по полям, отсортированным по `order`, каждый — своим кодеком; `time.Time` обрабатывается спец-кейсом (encodeTime).
- **Для C#:** генератор/сериализатор должен эмулировать это: атрибут `[RasField(Codec, Order, Version)]`, порядок сериализации по `Order`, версионная фильтрация, префикс `size` для списков, дефолтный кодек по типу поля. Payload-пакета (`Packet`) предписан: `byte Type; size Size; bytes Data` (см. `packet_ras.pb.go`).

## 6. Примеры (сводно)

| Кодек | значение | байты |
|-------|----------|-------|
| byte | 0x7F | `7F` |
| byte | 0x80 (Go) | `00` |
| short | 0x1234 | `12 34` |
| short | 256 | `01 00` |
| int32 | 0x12345678 | `12 34 56 78` |
| int32 | 475223888 (Magic) | `1C 53 57 50` |
| long | 0x0102030405060708 | `01 02 03 04 05 06 07 08` |
| size | 300 | `AC 02` |
| size | 16384 | `80 80 01` |
| nullable_size | 300 | `6C 04` |
| nullable_size | null | `80` |
| string | "hello" | `05 68 65 6C 6C 6F` |
| string | "" | `00` |
| uuid | 123e4567-e89b-12d3-a456-426614174000 | `12 3E 45 67 E8 9B 12 D3 A4 56 42 66 14 17 40 00` |
| float | 1.0 | `3F 80 00 00` |
| float | -2.5 | `C0 20 00 00` |
| double | 1.0 | `3F F0 00 00 00 00 00 00` |
| bool | true / false | `01` / `00` |
| type | INT (4) | `04` |
| time | 1970-01-01T00:00:00Z | `00 02 35 1E B5 C0 70 00` |
| time | 2021-01-01T00:00:00Z | `00 02 43 C2 06 30 D0 00` |

**Пример полного пакета** (`Packet`, см. `packet_ras.pb.go` — поля `byte type`, `size size`, `bytes data`):
`Type=2 (CONNECT), Size=2, Data=[01 00]` → `02 02 01 00` (byte `02`, size varint `02`, данные).
`Type=2, Size=300, Data=…` → `02 AC 02 <300 байт>` (size varint от 300 = `AC 02`).

## 7. Источники

- Go: [v8platform/encoder](https://github.com/v8platform/encoder) — `ras/codec256/{encode,decode,version}.go`, `ras/{encoder,encode_func,decode_func,utils,tag,options,marshal}.go`
- Go сгенерированные: [v8platform/protos](https://github.com/v8platform/protos)/gen/ras/protocol/v1/*_ras.pb.go, [v8platform/protos](https://github.com/v8platform/protos)/gen/v8platform/serialize/v1/*_ras.pb.go
- Java: `com._1c.v8.swp.codec.{CodecUtils,BinaryCodecFactory,IServiceWireCodec(Encoder,Decoder),ServiceWireType}` (официальная библиотека 1С)
- Java: `com._1c.v8.swp.internal.codec.{BinaryCodecV1_0,BigEndianEncoder,BigEndianDecoder,TypeCodes,Constants}`
- Java: `com._1c.v8.swp.endpoints.EndpointId`, `messages.NegotiateMessage*`, `ProtocolConstants` (MAGIC = 475223888 = `0x1C535750`; исправлено в `TRANSPORT_SPEC.md` ошибочное `0x1C5A6E40`)