# !!!НАВАЙБКОЖЕНО!!!

# OneSTools.Ras

Независимый высокопроизводительный **C#-клиент сервера администрирования 1С (RAS)** по
бинарному протоколу SWP — без Java SDK и без обращения к утилите `rac` — и служба периодического
сбора метрик (сессии, процессы, соединения, блокировки, сводка кластера) с пакетной записью
в **ClickHouse**.

Стек: .NET 8 / C# 12, Nullable + ImplicitUsings, `TreatWarningsAsErrors`.

## Статус

| | |
|---|---|
| Протокол | подтверждён живым сервером **1С 8.3.25.1520** (стенд в изолированном контуре) |
| API | портировано 62 метода из 84 (`docs/GAP_ANALYSIS.md`), negotiation версий сервиса `3.0…14.0` |
| Тесты | 129: кодеки/сообщения (unit), mock-интеграция, тесты живого стенда, нагрузочный харнесс |
| Надёжность | паттерн «соединение на кластер», ретраи/backoff, пауза при падении ClickHouse — см. замеры [docs/SECURITY.md](docs/SECURITY.md), [docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md) |
| Ограничения | без TLS/SASL (принято для изолированного контура); GAP-методы не портированы; формат R8 — только POSIX-время |

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
| [docs/GAP_ANALYSIS.md](docs/GAP_ANALYSIS.md) | соответствие методов ↔ wire-сообщений (62 COVERED / 22 GAP) |
| [docs/PORTING_GUIDE.md](docs/PORTING_GUIDE.md) | план C#-порта и ключевые wire-факты |
| [docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md) | ревизия архитектуры vs OneSTools.EventLog, решения R1–R8 |
| [docs/SECURITY.md](docs/SECURITY.md) | модель угроз, лимиты парсера, обращение с секретами, чек-лист перед аудитом ИБ |
| [AGENT.md](AGENT.md) | регламент разработки (для AI-агентов) |

## Быстрый старт

Окружение — devcontainer (`.devcontainer/`): сервисы `clickhouse` (8123/9000) и `mock-ras` (1545);
реальный тестовый стенд RAS подключается как внешний хост `1c-test-server` (или через
`docker-compose.override.yml`, см. пример).

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
await ras.AuthenticateClusterAsync(clusterId, "", "");     // обязательно даже без паролей — см. docs/TRANSPORT_SPEC.md §7.2

foreach (var cluster in await ras.GetClustersAsync())
    Console.WriteLine($"{cluster.Name} @ {cluster.Host}:{cluster.Port}");

var sessions  = await ras.GetSessionsAsync(cluster.Uuid);
var processes = await ras.GetWorkingProcessesAsync(cluster.Uuid);
var locks     = await ras.GetLocksAsync(cluster.Uuid);
```

### Конфигурация экспортёра

`src/OneSTools.Ras.Exporter/appsettings.json` (**dev-only значения**, боевые креды — только
через env/`appsettings.local.json`; приоритет: base < local < user-secrets(Development) < env < args).

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
по зонду каждые 30 с. Подробности — [docs/ARCHITECTURE_REVIEW.md](docs/ARCHITECTURE_REVIEW.md).

### Тесты живого стенда

```bash
export RAC_TEST_RAS_HOST=1c-test-server RAC_TEST_RAS_PORT=1545   # значения по умолчанию
dotnet test tests/OneSTools.Ras.IntegrationTests
```

Без доступа к стенду соответствующие тесты скипуются с причиной.

### Нагрузка и хаос

```bash
dotnet run --project src/OneSTools.Ras.LoadSim -- --mode all   # soak/ramp/churn/negotiation
dotnet run --project src/OneSTools.Ras.LoadSim -- --mode chaos --duration 60
```

Отчёты (p50/p95/max, ошибки) — JSON в каталоге запуска.

### Безопасность

- Модель угроз и принятые риски (включая «без TLS в изолированном контуре») — [docs/SECURITY.md](docs/SECURITY.md).
- Проверка зависимостей/конфига: `bash tools/security-check.sh` (NuGetAudit + lock-файлы + грепок секретов).

## Структура репозитория

```
src/                  # проекты решения (см. таблицу выше)
tests/                # unit + integration
docs/                 # спецификации и обзоры (см. таблицу документации)
tools/                # security-check.sh, инструментарий реверс-инжиниринга
reference/            # декомпилят Java SDK и proto-контракты RAS (read-only, источник спецификаций)
ras-minimal/          # Dockerfile минимального образа реального RAS для dev-стенда
1c_ibis_admin_src/    # оригинальные Java-исходники Ibis admin (read-only)
.devcontainer/        # Dockerfile + compose dev-окружения
```

## Лицензия

Не определена (внутренний порт; протокольные спецификации выведены из
`reference/` материалов 1С).
