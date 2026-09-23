# AGENT.md — Инструкции для основного агента разработки

> Пользователю: общий обзор, запуск и конфигурация — в **README.md**. Здесь — регламент разработки.

> Ты — основной агент разработки проекта **OneSTools.Ras**: высокопроизводительный инструмент
> сбора метрик с сервера RAS («1С:Предприятие») по протоколу SWP и экспорта в ClickHouse.
> Стек — C# / .NET 8. Серверное окружение — devcontainer (см. `.devcontainer/`).

---

## 1. Цель проекта

Создать независимую, высокопроизводительную C#-библиотеку клиента RAS (без Java SDK и без
обращения к утилите `rac`), поверх которой реализована служба периодического сбора метрик
(сессии, процессы, соединения, блокировки, сводка кластера) с пакетным экспортом в ClickHouse.

Архитектурный ориентир — [OneSTools.EventLog](https://github.com/akpaevj/OneSTools.EventLog):
конвейерная обработка (TPL Dataflow), пакетная запись в ClickHouse, Generic Host + `appsettings.json`.

Референсная архитектура C#-порта: `docs/PORTING_GUIDE.md`.

---

## 2. Источник истины (SI)

| Документ | Назначение |
|---|---|
| `docs/TRANSPORT_SPEC.md` | Транспорт SWP: фрейминг, handshake, типы сообщений, SASL, TLS, endpoint'ы |
| `docs/CODEC_SPEC.md` | 17 бинарных кодеков wire-формата: алгоритмы, hex-примеры, C#-сигнатуры |
| `docs/API_REFERENCE.md` | 84 метода `IAgentAdminConnection`, 25 моделей данных |
| `docs/GAP_ANALYSIS.md` | Соответствие методов ↔ сообщений (62 COVERED / 22 GAP) |
| `docs/PORTING_GUIDE.md` | План реализации C#-библиотеки, список рисков |

**Жёсткие правила wire-формата (НЕ нарушать):**

- Формат **НЕ protobuf**. Поля кодируются позиционно, по `(ras.encoding.field).order` из `reference/v8platform-protos/`.
- `int32` — фиксированные **4 байта big-endian** (не varint).
- `int16`/`int64`/`float`/`double` — big-endian.
- `size` — **LEB128 varint** (7 бит/байт, бит 0x80 = продолжение).
- `nullable_size` — первый байт: 6 бит значения, бит 0x40 = продолжение, **0x80 = null**.
- **null в `nullable_size`/типизированных значениях пишется как `0x80`** (семантика Java-реализации 1С, а не Go-кодека).
- `string` — `[nullable-size][UTF-8]`; `uuid` — 16 байт (порядок как в .NET `Guid` — требует трансформации, см. CODEC_SPEC).
- `time` — R8-время от `0001-01-01` в единицах по 100 мкс, учитывать `AgeDelta`.
- Каждый факт проверять по двум источникам; при расхождении — вопрос субагенту реверс-инжиниринга.

---

## 3. Субагент реверс-инжиниринга (обязателен при неопределённости)

При спорных/неизвестных деталях протокола SWP, кодировок, layout сообщений или версий платформы
— **вызвать специализированного субагента** через инструмент `task`:

```
task(subagent_type: "rac-reverser", prompt: "…вопрос по docs/TRANSPORT_SPEC / кодировке…")
```

Описание и источники субагента: `.opencode/agents/rac-reverser.md`.
Субагент работает с декомпилятом Java-SDK (`reference/decompiled/java-src/`, 394 файла),
proto-контрактами (`reference/v8platform-protos/`) и Go-кодеком (`reference/v8platform-encoder/`).

**НЕ декомпилируй и не модифицируй JAR/исходники заново** — они только для чтения.

---

## 4. Окружение разработки (devcontainer)

Конфигурация: `.devcontainer/` (Dockerfile, docker-compose.yml, devcontainer.json).

| Сервис | Назначение | Адрес из контейнера | Адрес с хоста |
|---|---|---|---|
| `dotnet-dev` | Контейнер разработки (.NET 8 SDK + opencode CLI) | — | — |
| `clickhouse` | Хранилище для dev/интеграционных тестов | `clickhouse:8123`, `clickhouse:9000` | `localhost:8123`, `localhost:9000` |
| `mock-ras` | Эмулятор RAS (SWP handshake, protocol-fidelity) | `mock-ras:1545` | `localhost:1545` |
| `1c-test-server` | Реальный стенд: сервер 1С + ragent + RAS 8.3.25 (внешний контейнер той же docker-сети; базовый образ — `ras-minimal/`) | `1c-test-server:1545` (`ragent` 1540) | по пробросу стенда |

- VSCode: расширения C# Dev Kit, dotnet-test-explorer, Docker.
- Команды (внутри `dotnet-dev`):
  - `dotnet build OneSTools.Ras.sln`
  - `dotnet test OneSTools.Ras.sln`
  - `dotnet run --project src/OneSTools.Ras.MockServer -- <port> [maxVersion]` — mock на `:port`
  - `dotnet run --project src/OneSTools.Ras.Exporter` — служба экспорта (appsettings у бинарника; local < env < args)
  - `dotnet run --project src/OneSTools.Ras.LoadSim -- --mode all|chaos|…` — нагрузка/хаос, JSON-отчёты
  - `bash tools/security-check.sh` — аудит зависимостей/конфига (перед ИБ-проверкой)
  - Тесты живого стенда: `RAC_TEST_RAS_HOST`/`RAC_TEST_RAS_PORT` (по умолчанию `1c-test-server:1545`)
- Подмена mock-ras реальным стендом: скопировать `.devcontainer/docker-compose.override.example.yml`
  в `docker-compose.override.yml`, указать адрес стенда, сменить `Host` в `appsettings.json`.

---

## 5. Структура решения

```
OneSTools.Ras.sln
├── src/
│   ├── OneSTools.Ras.Client/          клиент SWP (библиотека)
│   │   ├── Transport/                 TCP, фрейминг, handshake, keep-alive, deflate
│   │   ├── Wire/                      RasReader/RasWriter — 17 кодеков (zero-alloc)
│   │   ├── Protocol/                  системные сообщения SWP, типы, константы
│   │   ├── Messages/                  API-сообщения (запросы/ответы)
│   │   └── Models/                    модели данных (ClusterInfo, SessionInfo, ...)
│   ├── OneSTools.Ras.Exporter.Core/   ядро сбора/экспорта
│   │   ├── Collectors/                периодический опрос по типам метрик
│   │   ├── Pipeline/                  TPL Dataflow / Channels батчинг
│   │   ├── Storage/                   абстракция IMetricsStorage
│   │   └── ClickHouse/                ClickHouseStorage (DDL, Bulk Insert)
│   ├── OneSTools.Ras.Exporter/        Worker Service/CLI (appsettings.json)
│   └── OneSTools.Ras.MockServer/      эмулятор RAS для тестов
└── tests/
    ├── OneSTools.Ras.Client.Tests/    юнит-тесты (кодеки по hex, сообщения)
    └── OneSTools.Ras.IntegrationTests/ сквозные: Client → Mock-RAS → ClickHouse
```

---

## 6. Стандарты кодирования C# (.NET 8, C# 12)

1. **Nullable Reference Types, ImplicitUsings включены** (задано в `Directory.Build.props`).
2. **Zero-allocation в горячем пути**: `ReadOnlySpan<byte>`, `Memory<byte>`, `ArrayPool<byte>`,
   `BinaryPrimitives`, `PipeReader/PipeWriter`. Не аллоцировать строки/массивы на каждый пакет.
3. **Строго асинхронный I/O**: `Task`/`ValueTask`, широкое использование `CancellationToken`.
4. **File-scoped namespaces; предпочтительно `record struct` для моделей; primary constructors.**
5. **Комментарии к коду не добавлять без запроса**; XML-doc разрешены для публичных API и
   обязательны на кодеках с указанием источника (`docs/…`).
6. Высокая производительность: избегать LINQ в hot-path, минимум аллокаций на итерацию опроса.
7. `TreatWarningsAsErrors` — код должен собираться без предупреждений.

---

## 7. Структура данных в ClickHouse (согласовывается при разработке)

Принципы (уточняются на этапе реализации хранилища, до закрепления схемы):

- **Отдельная база на кластер**: имя по шаблону `ras_cluster_{ClusterName}` (`ClickHouseSettings.DatabaseTemplate`).
- **Своя таблица на сущность**: `sessions`, `processes`, `connections`, `locks`, `cluster_summary`.
- **Движок**: `ReplacingMergeTree`/`MergeTree`, партиционирование `PARTITION BY toYYYYMM(_time)`,
  `ORDER BY (_time, …)`; кодеки сжатия колонок — `ZSTD`, `DoubleDelta` (для времени).
- **Версионирование состава полей**: разные версии платформы 1С (8.3.x/8.4) дают разный набор
  полей. Реализуется версионированными адаптерами моделей и миграциями схемы; схема должна
  допускать добавление колонок (`ALTER TABLE … ADD COLUMN`) без потери данных.
- Согласование итоговой схемы — с пользователем перед началом наполнения таблиц.
- `AutoCreateDatabases`/`AutoCreateTables` создают БД/таблицы при старте, если включены.

---

## 8. Конфигурация (appsettings.json)

- **Автообнаружение серверов НЕ используется.** Список серверов, частоты опроса и учётные данные
  — явно в `appsettings.json` (`RasExporter:Servers`). Переопределение через env-переменные:
  `RasExporter__Servers__0__Host`, `ClickHouse__ConnectionString` и т.п.
- `PollIntervals` — частота по каждому типу метрик (сек.).
- `Clusters` — перечень кластеров с учётными данными уровня кластера.
- Классы конфигурации: `src/OneSTools.Ras.Exporter/Configuration/`.

---

## 9. Статус реализации

Этапы 1–6 (кодеки, транспорт, endpoint-слой, 62 API-сообщения, Exporter.Core, Worker Service +
HealthChecks) — **реализованы и проверены живым RAS 8.3.25.1520** (см. README.md,
docs/SECURITY.md §3, замеры LoadSim). Остаток:

1. 22 GAP-метода — через субагента `rac-reverser` по декомпиляту (GAP_ANALYSIS).
2. SASL/TLS — при выходе за пределы изолированного контура (docs/TRANSPORT_SPEC.md §5–6).
3. Стенд с реальными инфобазами/нагрузкой — верификация версионированных полей SessionInfo и т.п.

---

## 10. Проверка работы (definition of done на каждом этапе)

- Сборка: `dotnet build OneSTools.Ras.sln` — **0 Warnings 0 Errors**.
- Тесты: `dotnet test OneSTools.Ras.sln` — **все зелёные**.
- Юнит-тесты кодеков обязательны (hex-векторы из CODEC_SPEC).
- Интеграционные тесты — через сервисы `mock-ras` и `clickhouse` из docker-compose.
- Для спорных мест протокола — вызов субагента `rac-reverser` с фиксацией результата
  (обновить `docs/` при найденных расхождениях).

## 11. Запреты

- НЕ изменять `reference/`, `1c_ibis_admin_src/`, `tools/` и готовые `docs/` без веской причины
  (правки протокольных спецификаций — только по фактическим данным и с отметкой источника).
- НЕ декомпилировать netty и JAR заново.
- НЕ использовать стандартный protobuf-сериализатор для обмена с RAS (только собственные кодеки).
- НЕ оставлять закомментированный код и мертвый код в `src/`.