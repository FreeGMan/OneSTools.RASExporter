# PORTING_GUIDE.md — Портирование на C#

> Итоговое резюме Фазы 6. Все факты — из `docs/TRANSPORT_SPEC.md`, `docs/CODEC_SPEC.md`,
> `docs/API_REFERENCE.md`, `docs/GAP_ANALYSIS.md`.

## Архитектура C# порта

```text
C# RAS Client (namespace OneSTools.Ras.Client)
├── Transport/                    ← TRANSPORT_SPEC.md
│   ├── RasConnection             TCP, фрейминг, handshake, keep-alive, диспетчеризация ответов
│   ├── RasEndpoint               последовательные вызовы API на endpointId
│   └── RasCompression            ограниченная распаковка deflate (бит 0 в msgFormat)
├── Wire/                         ← CODEC_SPEC.md (17 примитивов, аналог codec256)
│   ├── RasWriter / RasReader     big-endian + varint-family кодеки
├── Protocol/                     ← protos (rasapis/ras/protocol/v1) + CODEC_SPEC
│   └── SwpProtocolConstants, RasParametersSerializer, RasThrowableSerializer
├── Messages/                     ← v8platform/protos/messages/v1 + GAP_ANALYSIS (48 COVERED)
│   └── XxxRequest / XxxResponse  ручные Serialize/Deserialize для реализованных методов
├── Models/                       ← v8platform-protos/serializeapis + API_REFERENCE
│   └── ClusterInfo, InfobaseInfo, SessionInfo и другие модели реализованных сообщений
├── RasAdminClient                27 методов API из 84 в Java-интерфейсе
└── Exceptions/                   ← исключения SWP/RAS
```

## Ключевой факт о wire-формате

**НЕ стандартный protobuf.** Позиционная сериализация: поля кодируются подряд по опции
`(ras.encoding.field).order`, каждая с явным кодеком (`byte`, `short`, `int32`, `size`, `string`, ...).
`protoc` генерирует tag-value формат — **не подходит** для обмена с RAS; можно использовать только
для генерации data-классов. В текущем клиенте поля сериализуются вручную в
`Messages/` и `Models/` на основе `RasWriter`/`RasReader`.

## Собственные кодеки C# (из CODEC_SPEC.md, 17 примитивов)

| Примитив | Реализация | Ключевые детали |
|----------|-----------|-----------------|
| `byte` | 1 байт | RAW |
| `short` (`int16`) | 2 байта big-endian | `BinaryPrimitives.WriteInt16BigEndian` |
| `int32` | 4 байта big-endian | **НЕ varint** (подтверждено Java+Go) |
| `long` (`int64`) | 8 байт big-endian | |
| `size` | LEB128-varint | 7 бит/байт, бит 0x80 = продолжение, little-endian группы (как unsigned protobuf varint); <127 → 1 байт |
| `nullable_size` | varint с доп. битами | 1-й байт: 6 бит значения, бит 0x40 = продолжение, **0x80 = null** |
| `bytes` | size-prefixed | `[size][data]` |
| `string` | size-prefixed UTF-8 | `[size][utf8]` |
| `uuid` | 16 байт | порядок байт как в .NET `Guid` (см. CODEC_SPEC: требуется трансформация) |
| `float`/`double` | IEEE-754 big-endian | `BinaryPrimitives.ReadSingleBigEndian` |
| `time` | 8 байт | R8-время от 0001-01-01, AgeDelta смещение |
| `bool` | 1 байт 0/1 | |
| `endpoint_id` | = `nullable_size` int | на проводе то же |
| `type`/`string[]`/`typed value` | составные | каждая из 3.4/CodecUtils |

⚠ **Расхождение Java vs Go (для null):** Java (реальная 1C) пишет null как `0x80` в `nullable_size`;
Go-кодек пишет `0x00` и не эмитит `0x80`. **Для совместимости с RAS используй null = 0x80 (Java-семантика).**

## Что писать вручную (реализация с нуля)

- [x] `RasWriter`/`RasReader`: 17 примитивов по CODEC_SPEC.md, тесты hex-векторов и лимитов парсера.
- [x] `RasConnection`: TCP, `Negotiate` (`1C 53 57 50 01 00 01 00`), `Connect(connect.timeout=2000)`,
  чтение `[type][size][data]`, корреляция future по типу, keep-alive (type 16, пустое тело) по IdleState.
- [x] `RasEndpoint`: open → `[endpointId: nullable-size][format: short][payload]`;
  payload-слой `EndpointFormat` = `1 byte kind [0x00 void | 0x01 message | 0xFF exception] + payload/throwable`.
- [ ] SASL/START_TLS не реализованы (принятый риск изолированного контура, см. SECURITY.md).
- [x] Сериализация сообщений для 27 доступных методов клиента — порядок полей из `.proto` (order + encoder).
- [ ] Остальные методы и сообщения: наличие proto-контракта (48 COVERED / 36 GAP) не равно реализации в C#.

## Что можно взять готовым

- [x] Форматы реализованных сообщений — по [v8platform/protos](https://github.com/v8platform/protos)/ (`.proto` + `gen/*_ras.pb.go`).
- [x] Модели реализованных сообщений — по `serializeapis/*.proto` + API_REFERENCE.md.
- [x] Список методов (84) и их wire-типы — API_REFERENCE.md.

## Риски / открытые вопросы (из TRANSPORT_SPEC §9.2)

- [x] `size`/`int32` спецификация — RESOLVED: size=varint, int32=BE 4 байта.
- [x] Магия и версии — RESOLVED: `0x1C535750`, protocol/codec `256`.
- [x] Сжатие EndpointMessage — RESOLVED: бит 0 в `msgFormat`, `endpoint.encoding=deflate`.
- [ ] Версии платформы 8.3.x/8.4 в RAS: незначительные изменения в контрактах сообщений (проверять
      на целевой версии; кодеки v1.0 стабильны).
- [ ] SASL DIGEST-MD5/CRAM-MD5 точный байтовый обмен — нужен живой RAS-стенд для верификации.
- [ ] `time`-кодек и `uuid` порядок байт под .NET — сверить на реальном сервере до релиза.

## Рекомендуемый порядок реализации

1. **Адapter::`RasWriter`/`RasReader`** + 17 примитивов (CODEC_SPEC) → unit-тесты по hex-примерам.
2. **`RasConnection`** + Negotiate/Connect + фрейминг → интеграция с mock-RAS (или серверный эндпоинт на .NET для теста).
3. **`RasEndpoint`** + open/close/keep-alive.
4. **Сериализация `Packet`/`ConnectMessage`** → реальный RAS, `getClusters()` на минимальном client.
5. Расширение текущих 27 методов клиентского API на остальные методы с proto-контрактом,
   затем 36 GAP по доступным спецификациям и проверке на живом стенде.
6. SAST/audit: тесты от брёвен реального RAS (Wireshark/`tcpdump`) против C#-кодеков.

## Источники

- Протокол: `docs/TRANSPORT_SPEC.md`
- Кодеки: `docs/CODEC_SPEC.md`
- API: `docs/API_REFERENCE.md`, `docs/GAP_ANALYSIS.md`
- Контракты: [v8platform/protos](https://github.com/v8platform/protos)/
- Кодеки-примитивы (Go): [v8platform/encoder](https://github.com/v8platform/encoder)
- Официальная Java-библиотека 1С (транспорт): `com._1c.v8.{swp,ibis,core}.*`
