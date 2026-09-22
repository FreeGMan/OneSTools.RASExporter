# TRANSPORT_SPEC.md — Транспортный протокол SWP (Server Wire Protocol)

> Не protobuf — кастомный бинарный формат. TCP, порт RAS по умолчанию: **1545**.
>
> Каждый факт снабжён источником: `J` = декомпилированный Java-класс (`reference/decompiled/java-src/**`),
> `P` = proto (`reference/v8platform-protos/rasapis/ras/protocol/v1/*.proto`), `D` = дебаг/вывод.
> Помечено «не определено», если точных данных из декомпилята получить не удалось.

---

## 1. Обзор и стек

Уровни (от железа к приложению):

1. TCP (Netty 3.2.x, JBoss) — `com._1c.v8.swp.internal.netty/**`
2. SWP / Service Wire Protocol — `com._1c.v8.swp/**` (framing: тип + размер + тело, aka WebSocket-подобный фрейминг)
3. ibis / SWP endpoint-слой — `com._1c.v8.ibis.internal.swp/**` (`EndpointFormat`, `EndpointMessage`)
4. RAS Admin API — `com._1c.v8.ibis.admin/**` (клиентский API поверх endpoint'ов)

Протокол называется **SWP** (`ProtocolConstants.NAME = "swp"`). Обёртка сообщений/параметры в
коде читаются с префиксами:

- `nipple.swp.protocol.<param>` — пары протокола (`ProtocolConstants.PROTOCOL_PARAMETERS_PREFIX`, `ProtocolUtils.getProtocolParameterName`)
- `nipple.swp.connection.<param>` — параметры соединения/подключения (`CONNECTION_PARAMETERS_PREFIX`)
- `nipple.swp.endpoint.*` — параметры endpoint'а (`EndpointMessage` / `WireFormatUtils`)

> Важно: в исходниках префиксы выглядят как `nipple.swp.*`, а не `swp.<...>`. Некоторые параметры на
> клиентской стороне регистрируются как `swap.<...>` — при реализации сверяться с исходным кодом,
> а не с названиями в этой строке.

## 2. Магическое число и версия

| Поле | Значение | Источник |
|---|---|---|
| magic | `475223888` (`0x1C535750`), int32 | `ProtocolConstants.MAGIC`, `NegotiateMessage`, `ProtocolConstants.java` |
| имя протокола | `"swp"` | `ProtocolConstants.NAME` |
| версия протокола V1_0 | `256` (`0x0100`) = short | `ProtocolConstants.VERSION_1_0` |
| версия codec | `256` (`0x0100`) | `BinaryCodecFactory.getDefaultVersion()`, `WireFormatV1_0.getVersion()` |
| константа codec | `Constants.VERSION_1_0 = 256` | `internal/codec/Constants.java` |

> Проверено: `475223888 (dec) == 0x1C535750 (hex)`, файл `ProtocolConstants.java`, строка `public static final int MAGIC = 475223888;`.
> Раньше в мыслях/заметках фигурировал другой hex (0x1C5A6E40) — **это ошибка**, см. ниже.

## 3. Фрейминг пакетов

Каждое сообщение на уровне SWP:

```
[type: byte][size: varint(LEB128)] [body: size байт]
```

- Отдельные байтовые поля — **big-endian** (пакет, int16/int32/int64 — BE). `BigEndianDecoder/Encoder`.
- `size` кодируется как **7-битный varint** (LEB128), каждый байт: младшие 7 бит + бит продолжения `0x80` (см. ниже декодирование).
- Назначение `size` не является частью тела; тело `size` байт следует сразу после varint-размера.

### 3.1 Тип сообщения (byte)

Номер типа — `byte` (см. `MessageType.java` / `MessageCodes.java` / proto `types.proto` `MessageType`):

| raw | Java `MessageType` / `MessageCodes` | proto `MessageType` | Направление |
|---|---|---|---|
| 0 | NEGOTIATE | — | client → server |
| 1 | CONNECT | — | client → server |
| 2 | CONNECT_ACK | — | server → client |
| 3 | START_TLS | — | client → server |
| 4 | DISCONNECT | — | обе стороны |
| 5 | SASL_NEGOTIATE | — | server → client |
| 6 | SASL_AUTH | — | client → server |
| 7 | SASL_CHALLENGE | — | server → client |
| 8 | SASL_SUCCESS | — | server → client |
| 9 | SASL_FAILURE | — | server → client |
| 10 | SASL_ABORT | — | client → server |
| 11 | ENDPOINT_OPEN | — | client → server |
| 12 | ENDPOINT_OPEN_ACK | — | server → client |
| 13 | ENDPOINT_CLOSE | — | client → server |
| 14 | ENDPOINT_MESSAGE | — | обе стороны |
| 15 | ENDPOINT_FAILURE | — | server → client |
| 16 | KEEP_ALIVE | — | обе стороны |

> В декомпилированном Java `MessageType` (enum) существуют NEGOTIATE..ENDPOINT_FAILURE(15) и отдельный
> KEEP_ALIVE(16), в proto `types.proto` значения полностью совпадают (0..16) — см. `ServiceWireType` в java-src
> (`TypeCodes.BOOLEAN=1 ... ENDPOINT_ID=13`) — они совпадают с `ParamType` в `param.proto` и пронумерованы 1..13 (см. раздел про параметры).

### 3.2 Varint (кодирование size)

По `BigEndianDecoder.decodeSize` / `CodecUtils.encodeSize`:

```
encodeSize(val):  val > 127 → пишется байт (0x80 | (val & 0x7F)), затем остальные 7-битные группы
                  каждая группа: 0x80|группа если ещё есть группы, иначе группа & 0x7F
                  (последний байт без бита 0x80)
decodeSize:  cur = readByte & 0xFF; size = cur & 0x7F;
             пока (cur & 0x80) != 0: cur = readByte & 0xFF; size += (cur & 0x7F) << shift; shift += 7
```

- NULL-размер (+ nullable size): `0x80` — это NULL (см. `encodeNullableSize`: первый байт `0x80` означает null,
  `0x80 | 0` пустой; реализация: если null → пишется байт `0x80`; иначе первый байт `(val & 0x3F) | (0x40, если есть ещё байты)`
  — распознаётся в `decodeNullableSize`).

### 3.3 Типы значений (TypeCodes — Java ↔ ParamType — proto)

Служебные типы (байтовый код) — `ServiceWireType` / `TypeCodes.java` (совпадают 1:1 с `param.proto` `ParamType`):

| code | Java `TypeCodes` / `ServiceWireType` | proto `ParamType` |
|---|---|---|
| 1 | BOOLEAN | PARAM_BOOLEAN |
| 2 | BYTE | PARAM_BYTE |
| 3 | SHORT | PARAM_SHORT |
| 4 | INT | PARAM_INT |
| 5 | LONG | PARAM_LONG |
| 6 | FLOAT | PARAM_FLOAT |
| 7 | DOUBLE | PARAM_DOUBLE |
| 8 | SIZE | PARAM_SIZE |
| 9 | NULLABLE_SIZE | PARAM_NULLABLE_SIZE |
| 10 | STRING | PARAM_STRING |
| 11 | UUID | PARAM_UUID |
| 12 | TYPE | PARAM_TYPE |
| 13 | ENDPOINT_ID | PARAM_ENDPOINT_ID |

Способ кодирования значения каждого типа (по `BinaryCodecV1_0`, big-endian):

- BOOLEAN: 1 байт (0/1), иначе exception
- BYTE: 1 байт
- SHORT: 2 байта BE
- INT: 4 байта BE
- LONG: 8 байт BE
- FLOAT: encodeInt(floatToIntBits) — 4 байта
- DOUBLE: encodeLong(doubleToLongBits) — 8 байт
- SIZE: varint (см. 3.2)
- NULLABLE_SIZE: nullable varint (0x80 = null)
- STRING: nullable-size varint + UTF-8 байты (`encodeString`: null → encodeNull; иначе encodeNullableSize(len) + байты, char ≈ UTF-8)
- UUID: два long (mostSignificantBits, leastSignificantBits) — 16 байт
- TYPE: 1 байт (`ServiceWireType.raw`)
- ENDPOINT_ID: nullable-size varint (целый `EndpointId.toInteger()` / `decodeNullableSize`)

### 3.4 Параметры (карта параметров: ключ → типизированное значение)

Из `WireFormatUtils.formatParameters/parseParameters`:

```
[параметры-map nullable-size][ (ключ: string, значение: typed-value) × N ]
typed-value: [type: byte (ServiceWireType.raw или 0x80 для null)] [данные по типу]  // см. 3.3
```

Где `typed value` — это именно `encodeType` (1 байт-код типа) + кодированное по 3.3 тело значения.

### 3.5 Ошибки/исключения (Throwable)

Из `WireFormatUtils.formatThrowable` (используется в DISCONNECT, SASL_FAILURE, ENDPOINT_FAILURE):

```
class: nullable-string
message: nullable-string
элементы стека: size (varint) × { class, method, file, line(int) }
cause (рекурсивно, nullable) → если null, конец цепочки
```

Классы исключений разрешаются через `IThrowableResolver` (набор типовых `ServiceWire*Throwable` + произвольные).

## 4. Установка соединения

Порядок (клиент `V8Connection` / `ClientProtocolHandler`):

1. **Negotiate** (client → server): сырое сообщение **без** type/size-обёртки.
   Тело = `NegotiateMessageFormat` (8 байт):
   ```
   [magic: int32 = 475223888 (0x1C535750)]
   [protocolVersion: short = 256]
   [codecVersion: short = 256]
   ```
   > Здесь magic проверяется как `int32`, версии — short. Получение: `decodeInt32(magic)`, ожидание равенства magic.
2. **Connect** (client → server): `ConnectMessage` (type 1), тело = `parameters` (см. 3.4).
   Параметры: `connect.timeout`, `endpoint.timeout`, `endpoint.encoding`, а также SASL-настройки
   (`sasl.methods`). Ключи именованы с префиксом `nipple.swp.connection.*`.
3. **ConnectAck** (server → client): `ConnectAckMessage` (type 2), тело = `parameters`.
   Параметар `keep_alive.timeout` (в param: `keep_alive.timeout`, у `ProtocolConstants.KEEP_ALIVE_TIMEOUT`) задаёт
   таймаут idle-keepalive: `IdleStateHandler(timer, 0, 0, keepAliveTimeout)` (из `ClientProtocolHandler.messageReceived`).
4. **(опц.) StartTLS** — `StartTlsMessage` (type 3) — см. §6.
5. **SASL-аутентификация** — см. §5.
6. **Открытие endpoint** — см. §7.

Handshake на стороне клиента (из `ClientProtocolHandler.handshake`):
- `negotiate(ctx)` — пишет `NegotiateMessage(wireFormat.getVersion(), wireFormat.getCodecVersion())`
- после него `ConnectMessage(parameters)` → ждём `ConnectAckMessage`; в ack читаем `keep_alive.timeout`
  и добавляем `IdleStateHandler` перед protocol-хендлером.
- При получении `DisconnectMessage` от сервера — `connectFuture`/`startTlsFuture` получают failure с
  `ServiceWireConnectionException("disconnected by peer")`.

## 5. SASL-аутентификация

Сообщения SASL (типы 5–10) — проброс байт SASL через сокет. Поток на клиенте (`ClientSaslHandler`):

1. `SaslNegotiateMessage` (5, механизмы: string[]) — **server → client** (список поддерживаемых SASL-механизмов).
   > В декомпиляте `ServerSaslHandler.negotiateRequest` отвечает на `SASL_NEGOTIATE` списком механизмов.
2. `SaslAuthMessage` (6, mechanism:string + initialResponse:byte[]) — **client → server**.
3. `SaslChallengeMessage` (7, challenge:byte[]) — **server → client** (если не complete).
4. `SaslAuthMessage` (6) — повторный ответ на challenge — **client → server**.
5. `SaslSuccessMessage` (8) — **server → client** при завершении.
6. `SaslFailureMessage` (9, throwable) — **server → client** при ошибке (и `SASL_ABORT` (10) из abort).

Механизмы (Java `SaslProperties` / `core/security/SaslConstants`): `PLAIN`, `DIGEST-MD5`, `CRAM-MD5`, `GSSAPI`.
Свойства подключаются через пул SASL (`com._1c.v8.core.security.SaslProperties`), параметры могут включать
`SASL_USERNAME`, `SASL_PASSWORD`.

> Точные payload-и форматов каждый message — см. `Sasl*MessageFormat` в `internal/messages/**`, тела:
> - negotiate: string[] (механизмы)
> - auth: string mechanism + string nullable initial-response (bytes)
> - challenge: nullable bytes
> - success: пусто
> - failure: throwable (3.5)

## 6. TLS / START_TLS и сжатие

### 6.1 START_TLS (type 3)

- `StartTlsMessage` (3) — запрос START_TLS, тело **пустое** (формат пишет только тип).
- Сервер по решению `secure.required` сам может потребовать TLS. Клиент: `ClientProtocolHandler.startTls`
  добавляет `SslHandler` в начало pipeline и шлёт `StartTlsMessage`; после успешного handshake
  выставляется `startTlsFuture`.
- `ProtocolConstants.SECURE_REQUIRED = "secure.required"`, `SASL_METHODS = "sasl.methods"`.

### 6.2 Сжатие (deflate/zlib)

- Compress/decompress поддерживается на уровне endpoint-сообщений (`EndpointMessage`, см. §7), формат-флаг
  конкретизируется в endpoint-ах (бит в `format`/`msgFormat` при ENDPOINT_MESSAGE).
- Бит 0 в `msgFormat` (`msgFormat |= 1`) означает «тело сжато» (`ClientEndpointHandler.sendMessage`).
  Сжатие — zlib/deflate: `CompressionSupportFactory` (`compression/...`, `ICompressionSupport`), методы
  `deflateBuffer`/`inflateBuffer`.
- Параметры сжатия endpoint'а передаются в `parameters` (`EndpointOpen`), ключ `endpoint.encoding` (см.
  `ProtocolConstants.ENDPOINT_ENCODING`, в RAS обычно `"deflate"`).
- ZlibChannelBufferDeflater/Inflater — `com._1c.v8.swp.compression.zlib.*` (по `ICompressionSupport`).

> Точная схема сжатия применяется к body ep-message, а не к svc-сообщениям верхнего уровня (см. §7).

## 7. Эндпоинты (EndpointMessage)

Пример работы endpoint-механизма (клиент RAS):

- `EndpointOpenMessage` (11): `serviceId: string name + string version`, `parameters: map(3.4)`
  (в т.ч. `endpoint.encoding`). Открытие ожидает `EndpointOpenAckMessage`.
- `EndpointOpenAckMessage` (12): `serviceId (~name+version)`, `endpointId: nullable-page-id (int, та же
  кодировка, что и у `EndpointId`)`, `parameters: map`. С `endpointId`, сопоставление с ожидаемым
  (client `waitingEndpoints`, по `EndpointId`).
- `EndpointFailureMessage` (15): `serviceId`, `endpointId (nullable)`, throwable (3.5). → endpoint'
  будущий результат становится failure.
- `EndpointMessage` (14): тело = `endpointId (nullable-size)` + `format: short` + данные endpoint-слоя;
  в конце `EndpointFormat` накладывает уровень: `[0=VOID | 1=MESSAGE | -1(0xFF)=EXCEPTION][payload]`.
  В `ClientEndpointHandler.parseMessage` полезная нагрузка endpoint-а приходит как
  `EndpointMessage(endpointId, format, body-char)`: формат сохраняется как `short`, а `0xFF` означает
  exception (что разрешается через `IThrowableResolver`).
  - `EndpointFormat.formatMessage`: byte `VOID_MESSAGE=0` + пусто; `MESSAGE=1` + payload;
    `EXCEPTION=-1` (0xFF) + throwable.
  - При сжатии: `(msgFormat & 1) != 0` → `inflateBuffer`/`deflateBuffer` вокруг body endpoint-а.
- `EndpointCloseMessage` (13): `endpointId: nullable-size(int)` — клиент закрывает endpoint.

> Endpoint-сообщения идут между `CONNECT/SASL` и закрытием соединения; KEEP_ALIVE (16) — пустое тело
> (см. §8).

### 7.1 Correl–ие запросов

- Открытие endpoint'а коррелируется по `endpointId` (см. `ClientEndpointHandler.PendingEndpoint /
  waitingEndpoints`, `pendingEndpoint` отдаётся по `endpointId` при `ENDPOINT_OPEN_ACK`).
- Отправка endpoint-сообщений — по объекту `endpoint` (reference), идентификатор запроса = endpointId.
- `IServiceWireFuture` (connect / sasl / open) — keyed в `ClientProtocolHandler` (connectFuture,
  startTlsFuture), на 1 соединение 1 handshake.

### 7.2 Имя и версии сервиса администрирования (подтверждено на живом RAS 8.3.25.1520, 2026-09-22)

- Wire-имя сервиса: **`v8.service.Admin.Cluster`** (Java SDK: `AgentAdminConnectionProxy.SERVICE_NAME`,
  `reference/decompiled/java-src/com/_1c/v8/ibis/internal/admin/AgentAdminConnectionProxy.java:174`).
  FQN `com.1c.v8.ibis.admin.IAgentAdminConnection` — имя Java-интерфейса, сервер его **не** знает
  (ответ `v8.service.UnsupportedService`).
- Версии: сервер 8.3.25.1520 принимает `3.0 … 14.0` (Java SDK перебирает `3.0…10.0` — список устарел).
- Отказ по версии содержит поддерживаемый список в тексте throwable:
  `Service is not supported [name='…', version='…', supported=14.0,13.0,…,3.0]` — клиентский
  negotiation ограничен этим множеством (аналог `V8Connection.detectSupportedVersions`).
- Outer throwable отказа открытия: `swp.Endpoint` («Mount point error» / «Ошибка точки подключения» —
  локализуется сервером), cause — `v8.service.UnsupportedService`.
- Формат EndpointFailure на проводе: `[string name][string version][nullable endpointId][throwable]`
  (клиент сопоставляет отказ по name+version, endpointId при отказе открытия не используется).
- Вызовы просмотра кластера (`getSessions/getWorkingProcesses/getWorkingServers/getClusterManagers/
  getLocks/…`) требуют предварительного `authenticateCluster` **даже для кластера без аутентификации**
  (пустые учётки допустимы); иначе `v8.service.Admin.Cluster#Rights`. `getClusters/getClusterInfo/
  getInfoBases` работают без аутентификации. Источник: проба стендом `1c-test-server` 2026-09-22.

## 8. Keep-alive

- `KeepAliveMessage` (16) — пустое тело.
- Клиент отправляет при `IdleState.ALL_IDLE` (после `IdleStateHandler` с таймером из `keep_alive.timeout`
  ConnectAck). `ClientProtocolHandler.handleUpstream` на `IdleStateEvent(ALL_IDLE)` пишет `KeepAliveMessage`.
- Сервер: `KeepAliveMessage` не требует ответа (просто принимается).

## 9. Не определено / требует проверки

### 9.1 Подтверждено по декомпиляту (бывшие «неопределённые» пункты)

- **Порядок полей `EndpointMessage` подтверждён** — `MessageEncoderHandler.formatEndpointMessage`:
  `[endpointId: encodeEndpointId → nullable-size (int)][format: encodeShort → int16 BE][payload]`,
  затем общий фрейм `[type=14][size][body]`.
- **Бит сжатия в `msgFormat` подтверждён** — `ClientEndpointHandler`:
  - запись: `msgFormat` стартует с `0`; если endpoint-компрессия настроена → `deflateBuffer(body)` и
    `msgFormat |= 1` (бит 0 = deflate).
  - чтение: `if ((msg.getFormat() & 1) != 0) → inflateBuffer(body)`.
  - компрессия выбирается на клиенте из параметра `endpoint.encoding` (обычно `"deflate"`) в
    `EndpointOpenAckMessage.parameters` (`openEndpointAckReceived`); если параметра нет — сжатие не применяется.
- **Магия протокола** — `0x1C535750` (475223888), байты `1C 53 57 50` = «1C» + «SWP». Версии (int16 BE):
  `protocolVersion` и `codecVersion`, обе `256` (`VERSION_1_0`). (`ProtocolConstants`, `NegotiateMessageFormat`.)

### 9.2 Осталось проверить на «живом» RAS (не декомпилируется)

- Точная wire-последовательность SASL-механизмов (`PLAIN`, `DIGEST-MD5`, `CRAM-MD5`, `GSSAPI`) —
  шаги определены в `ServerSaslHandler`/`ClientSaslHandler`, но байтовый обмен без реального RAS
  подтвердить нельзя (нужен стенд).
- Параметр `secure.required` и явный перехват `START_TLS` между CONNECT и SASL — детали зависят от
  политики конкретной версии RAS.
- Точные дефолты таймингов (keep-alive, endpoint timeouts) в разных версиях платформы.

## Источники

- `reference/decompiled/java-src/com/_1c/v8/swp/**` — ProtocolConstants, NegotiateMessage(Format), MessageType,
  MessageCodes, BinaryCodecV1_0, WireFormatV1_0, TypeCodes/ServiceWireType, WireFormatUtils,
  ConnectMessageFormat/ConnectAckMessageFormat, Sasl*Message(Format), StartTlsMessageFormat,
  EndpointOpenMessageFormat/EndpointOpenAckMessageFormat, EndpointFormat, KeepAliveMessageFormat
- `reference/decompiled/java-src/com/_1c/v8/ibis/internal/swp/**` — V8Connection, V8Transport, EndpointFormat
- `reference/decompiled/java-src/com/_1c/v8/core/**`/`db/SimpleSaslHandler` — SaslProperties, SaslConstants
- `reference/v8platform-protos/rasapis/ras/protocol/v1/protocol.proto`, `connect.proto`, `endpoint.proto`,
  `param.proto`, `types.proto` — сверка типов/сообщений
- Netty pipeline: `swp.internal.netty` (ClientProtocolHandler, ClientSaslHandler, ServerSaslHandler)
- Сжатие: `swp/compression/**` (CompressionSupportFactory, ICompressionSupport), zlib
