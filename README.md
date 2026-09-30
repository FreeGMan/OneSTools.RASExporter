# !!!НАВАЙБКОЖЕНО!!!

# OneSTools.Ras

Независимый высокопроизводительный **C#-клиент сервера администрирования 1С (RAS)** по
бинарному протоколу SWP — без Java SDK и без обращения к утилите `rac` ([MIT](LICENSE)) — и служба периодического
сбора метрик (сессии, процессы, соединения, блокировки, сводка кластера) с пакетной записью
в **ClickHouse**. Вдохновлен проектом [OneSTools.EventLog](https://github.com/akpaevj/OneSTools.EventLog).

**ВНИМАНИЕ!** Проект разработан с применением AI, использует **НЕ** оффициальную реализацию протокола взаимодействия с RAS.
Применяйте на свой страх и риск.

Стек: .NET 8 / C# 12, Nullable + ImplicitUsings, `TreatWarningsAsErrors`.

## Статус

| | |
|---|---|
| Протокол | выведен из поведения обращения официальной Java-библиотеки с живому серверу **1С 8.3.25.1520** (стенд в изолированном контуре) |
| API | 27 методов в публичном `RasAdminClient` из 84 методов Java API; `docs/GAP_ANALYSIS.md` оценивает наличие proto-контрактов, а не реализацию C#; negotiation версий сервиса `3.0…14.0` |
| Тесты | 147: кодеки/сообщения (unit), mock-интеграция, тесты живого стенда и ClickHouse; нагрузочный харнесс запускается отдельно |
| Надёжность | соединение на кластер, retry/backoff, повтор инициализации схемы после сбоя ClickHouse, ограниченные очереди и счётчик потерь — см. [docs/SECURITY.md](docs/SECURITY.md) |
| Ограничения | без TLS/SASL (изолированный контур); часть методов Java API не портирована; экспорт метрик best effort, без устойчивой очереди; формат R8 — только POSIX-время |

## Проекты

| Проект | Назначение |
|---|---|
| `src/OneSTools.Ras.Client` | библиотека-клиент: транспорт SWP (`Transport/`), 17 wire-кодеков (`Wire/`), протокольные и API-сообщения (`Protocol/`, `Messages/`), модели (`Models/`) |
| `src/OneSTools.Ras.Exporter.Core` | коллекторы метрик, конвейер батчинга (`Channels` + ретраи + гейт хранилища), хранилище ClickHouse |
| `src/OneSTools.Ras.Exporter` | worker-служба: конфигурация, диагностика здоровья, graceful shutdown |
| `src/OneSTools.Ras.MockServer` | эмулятор RAS для тестов (protocol-fidelity: имя/версии сервиса, UnsupportedService, Rights-гейт) |
| `src/OneSTools.Ras.LoadSim` | нагрузочный/хаос-харнесс (stage1/ramp/churn/negotiation/chaos, JSON-отчёты) |
| `tests/OneSTools.Ras.Client.Tests` | unit: кодеки по hex-векторам, сообщения, fuzz-регрессии парсера |
| `tests/OneSTools.Ras.IntegrationTests` | mock-стенд (in-process), живой стенд (скип при недоступности), ClickHouse e2e |

## Документация

| Документ | О чём |
|---|---|
| [docs/TRANSPORT_SPEC.md](docs/TRANSPORT_SPEC.md) | транспорт SWP: magic/negotiate, фрейминг, handshake, эндпоинты, SASL/TLS-рамки, §7.2 — wire-имя и версии сервиса (подтверждено живым RAS) |
| [docs/CODEC_SPEC.md](docs/CODEC_SPEC.md) | 17 бинарных кодеков wire-формата: алгоритмы, hex-примеры, C#-сигнатуры |
| [docs/API_REFERENCE.md](docs/API_REFERENCE.md) | 84 метода `IAgentAdminConnection`, 25 моделей данных |
| [docs/GAP_ANALYSIS.md](docs/GAP_ANALYSIS.md) | наличие proto-описаний для 84 методов Java API (48 COVERED / 36 GAP), не охват реализации клиента |
| [docs/PORTING_GUIDE.md](docs/PORTING_GUIDE.md) | план C#-порта и ключевые wire-факты |
| [docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md) | ревизия архитектуры vs OneSTools.EventLog, решения R1–R8 |
| [docs/SECURITY.md](docs/SECURITY.md) | модель угроз, лимиты парсера, обращение с секретами, чек-лист перед аудитом ИБ |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | развёртывание на целевом сервере: сборка релиза, Linux (systemd) и Windows (Scheduled Task), конфигурация, наблюдение, обновление/откат |
| [AGENT.md](AGENT.md) | регламент разработки (для AI-агентов) |

## Быстрый старт

Окружение — devcontainer (`.devcontainer/`): сервисы `clickhouse` (8123/9000) и `mock-ras` (1545);
реальный тестовый стенд RAS подключается как внешний хост `1c-test-server` (или через
`docker-compose.override.yml`, см. пример).
Перед открытием devcontainer установите `CLICKHOUSE_PASSWORD` в окружении Docker Compose
или в локальном `.env` (игнорируется Git). Опубликованные dev-порты привязаны к `127.0.0.1`;
внутри сети Compose сервисы доступны друг другу по именам.

```bash
dotnet build OneSTools.Ras.sln          # 0 warnings / 0 errors — обязательное условие
dotnet test  OneSTools.Ras.sln          # unit + mock; тесты живого стенда скипуются без доступа

dotnet run --project src/OneSTools.Ras.MockServer -- 1545     # mock-RAS на :1545
dotnet run --project src/OneSTools.Ras.Exporter               # служба экспорта (appsettings у exe)
```

### Клиент как библиотека

```csharp
using OneSTools.Ras.Client;

await using var ras = new RasAdminClient("ras-host", 1545);
await ras.ConnectAsync();                                  // TCP + negotiate + эндпоинт v14.0
var clusters = await ras.GetClustersAsync();
if (clusters.Count == 0) throw new InvalidOperationException("RAS не вернул кластеры");
var cluster = clusters[0];
Console.WriteLine($"{cluster.Name} @ {cluster.Host}:{cluster.Port}");
await ras.AuthenticateClusterAsync(cluster.Uuid, "", "");   // требуется даже без пароля

var sessions  = await ras.GetSessionsAsync(cluster.Uuid);
var processes = await ras.GetWorkingProcessesAsync(cluster.Uuid);
var locks     = await ras.GetLocksAsync(cluster.Uuid);
```

### Конфигурация экспортёра

`src/OneSTools.Ras.Exporter/appsettings.json` содержит пример конфигурации без пароля ClickHouse;
учётные данные задаются через env/`appsettings.local.json` (приоритет: base < local <
user-secrets(Development) < env < args). В devcontainer `ClickHouse__ConnectionString` собирается
из `CLICKHOUSE_PASSWORD`; вне контейнера задайте полную строку подключения через env или локальный файл.

```jsonc
{
  "ClickHouse": { "ConnectionString": "Host=...;Port=8123;...", "DatabaseTemplate": "ras_cluster_{ClusterName}" },
  "RasExporter": {
    "Servers": [{
      "Name": "1c-test-server", "Host": "...", "Port": 1545,
      "AgentAuth": { "User": "", "Password": "" },
      "PollIntervals": { "SessionsSeconds": 5, "ProcessesSeconds": 5, "ConnectionsSeconds": 10, "LocksSeconds": 5, "ClustersSeconds": 300 },
      "Clusters": [{ "ClusterId": "...", "ClusterName": "MainCluster", "User": "", "Password": "", "Enabled": true }]
    }]
  }
}
```

- `Clusters: []` — авто-обнаружение всех кластеров сервера.
- Переопределение: `RasExporter__Servers__0__Host=...`, `ClickHouse__ConnectionString=...`.
- Для уникального имени БД на каждый кластер задайте `DatabaseTemplate`, содержащий
  `{ClusterId}`: шаблон по умолчанию основан только на нормализованном имени кластера.
- При старте проверяются серверы, порты, интервалы опроса, размер батча и параметры ClickHouse.
- Пустые `User/Password` кластера — валидный анонимный вход (реальный RAS всё равно требует
  явный `authenticateCluster`).

### Архитектура потока данных

```
RAS (SWP:1545) ──1 conn/кластер──> RasAdminClient ──> 5 коллекторов (таймеры)
        пауза опросов ◄── StorageHealthGate ◄── сбой записи ◄── BatchProcessor×5 ──> ClickHouse (HTTP bulk)
```

Каждая пара (сервер, кластер) — отдельное SWP-соединение с независимым reconnect и
экспоненциальным backoff; запросы внутри соединения сериализованы (SWP-эндпоинт
последователен). При недоступном ClickHouse опросы RAS встают на паузу и возобновляются
по зонду каждые 30 с; схема создаётся повторно до старта сбора. После исчерпания повторов
неотправленные метрики отбрасываются с записью ошибки и отражением числа потерь в health-check.
Подробности — [docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md).

### Тесты живого стенда

```bash
export RAC_TEST_RAS_HOST=1c-test-server RAC_TEST_RAS_PORT=1545   # значения по умолчанию
dotnet test tests/OneSTools.Ras.IntegrationTests
```

Без доступа к стенду соответствующие тесты скипуются с причиной.
Тестам ClickHouse нужна переменная `CLICKHOUSE_PASSWORD` либо
`CLICKHOUSE_TEST_CONNECTION_STRING`; без неё эти тесты пропускаются.

### Нагрузка и хаос

```bash
dotnet run --project src/OneSTools.Ras.LoadSim -- --mode all   # soak/ramp/churn/negotiation
dotnet run --project src/OneSTools.Ras.LoadSim -- --mode chaos --duration 60
```

Отчёты (p50/p95/max, ошибки) — JSON в каталоге запуска.

### Безопасность

- Модель угроз и принятые риски (включая «без TLS в изолированном контуре») — [docs/SECURITY.md](docs/SECURITY.md).
- Проверка зависимостей/конфига: `bash tools/security-check.sh` (NuGetAudit, JSON-отчёт по прямым
  и транзитивным пакетам, locked-mode restore, проверка секретов; требуется `jq`).

## Релиз и развёртывание

```bash
bash tools/build-release.sh --version 0.1.0 --tag
# → artifacts/v0.1.0/{archives,nuget,RELEASE_NOTES.md,SHA256SUMS.txt}
```

Скрипт прогоняет гейты (`restore --locked-mode`, `build` 0 warnings, тесты, аудит
зависимостей), пакует библиотеки в `.nupkg` и собирает экспортёр в self-contained
single-file (`linux-x64`, `win-x64`) — .NET Runtime на целевом сервере не требуется.
Комплекты для развёртывания и использования библиотек — [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

Для боевого контура стендовые значения из `src/OneSTools.Ras.Exporter/appsettings.json`
в релиз не попадают: в архиве `appsettings.production.example.json` + секреты через env.

## Структура репозитория

```
src/                  # проекты решения (см. таблицу выше)
tests/                # unit + integration
docs/                 # спецификации и обзоры (см. таблицу документации)
tools/                # security-check.sh, build-release.sh
deploy/               # релизные активы: systemd unit, env-пример, Scheduled Task скрипты,
                      # appsettings.production.example.json
ras-minimal/          # Dockerfile образа RAS для dev-стенда (файлы платформы — локально, см. ras-minimal/README.md)
.devcontainer/        # Dockerfile + compose dev-окружения
```

## Источники и референсы

Спецификации `docs/` выведены из анализа поведения и публичных контрактов; **сторонние
проприетарные файлы в репозитории не хранятся**.

- [OneSTools.EventLog](https://github.com/akpaevj/OneSTools.EventLog) — основной идейный и
  архитектурный вдохновитель проекта (host-сервис, конвейер, пакетная запись в ClickHouse).
  Благодарность автору! <3
- [v8platform/protos](https://github.com/v8platform/protos) — публичные protobuf-контракты
  интерфейсов 1С (RAS protocol/messages/serialize), использованные как основа описаний форматов
  сообщений. Огромная благодарность за проделанную работу!
- [v8platform/encoder](https://github.com/v8platform/encoder) — эталонная реализация
  бинарного кодека 1С (codec256) от того же автора что и `v8platform/protos`, по ней верифицирован `docs/CODEC_SPEC.md`.
- **Официальная Java-библиотека администрирования 1С** (`com._1c.v8.*`, пакеты `ibis.admin`,
  `swp`) и утилита `rac` — входит в дистрибутив платформы «1С:Предприятие 8» и скачивается с
  официальных ресурсов фирмы «1С» (портал 1C:ИТС / [releases.1c.ru](https://releases.1c.ru)).
  Принципы работы протокола SWP и семантика API сформированы на её основе.
- **Тестовый образ RAS** (`ras-minimal/`): бинарные файлы платформы также берутся из
  дистрибутива 1С и в репозиторий не коммитятся (см. [ras-minimal/README.md](ras-minimal/README.md)).

## Товарные знаки и права

«1С:Предприятие», логотипы и обозначения 1С — товарные знаки фирмы «1С». Проект **не
разработан, не одобрен и не аффилирован** с фирмой «1С»; совместимость с протоколом
обеспечена независимой реализацией. Файлы платформы 1С (бинарники) пользователям образа
`ras-minimal` необходимо получить самостоятельно из дистрибутива — на них действует EULA 1С.

## Лицензия

[MIT](LICENSE) © 2026 Makurin Egor
