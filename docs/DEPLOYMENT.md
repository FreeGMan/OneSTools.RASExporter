# DEPLOYMENT.md — развёртывание на целевом сервере

Руководство для служебных сред. Разработка и тесты — в [README.md](../README.md);
модель угроз и принятые риски — [SECURITY.md](SECURITY.md).

## 1. Что разворачивается

| Компонент | Вид | Назначение |
|---|---|---|
| `OneSTools.Ras.Exporter` | self-contained single-file (`bin/`) | служба сбора метрик RAS → ClickHouse |
| `OneSTools.Ras.Client`, `OneSTools.Ras.Exporter.Core` | `.nupkg` в `artifacts/.../nuget/` | использование как библиотека из своих проектов (локальный фид) |

Экспортёр — консольный worker-процесс: HTTP-портов не слушает, входящих правил файрвола
не требует. Нужен **исходящий** доступ:

| Куда | Порт | Протокол |
|---|---|---|
| RAS (`ragent`) каждого сервера 1С | 1545 | SWP, **без TLS** |
| ClickHouse (HTTP) | 8123 | HTTP, **без TLS** |

Принятое заказчиком допущение ([SECURITY.md §1](SECURITY.md)): работа только в изолированном
контуре, учётные данные и метрики идут открытым текстом. Выход за контур — IPSec/mTLS-прокси
на сети либо доработка START_TLS/SASL в клиенте.

Минимальные привилегии (чек-лист перед ИБ-проверкой — [SECURITY.md §5](SECURITY.md)):

- учётная запись RAS — только просмотр (просмотровые `authenticateCluster` без прав
  администрирования достаточно: `GetSessions/GetWorkingProcesses/GetLocks/…`);
- роль ClickHouse — `CREATE`, `INSERT` на `ras_cluster_*`; `SELECT` мониторингу не нужен;
- файловые права на конфигурацию с секретами — только сервис-аккаунт (§3).

### Комплект 0.1.0 (выпущен с ошибкой раскладки)

В архивах 0.1.0 шаблон конфигурации лежал в корне релиза, а приложение читает `appsettings.json`
из каталога бинарника — развернутые по той инструкции службы падали на валидации
(`RasExporter:Servers пуст: …`) либо работали только на чистой env-конфигурации. Лечение без
пересборки: перенести конфигурацию в `bin/` (Windows — `bin\appsettings.json`), проверить
строку `Конфигурация: … appsettings.json найден …` и рестартануть службу. Исправлено в 0.1.1.

## 2. Сборка комплекта

```bash
bash tools/build-release.sh --version 0.1.1 --tag
# артефакты: artifacts/v0.1.1/{archives,nuget,RELEASE_NOTES.md,SHA256SUMS.txt}
```

Гейты внутри скрипта: `restore --locked-mode`, `build` (0 warnings), тесты, аудит
зависимостей, pack, publish по RID. Шагу publish нужна сеть (runtime-pack'ы целевого RID
с nuget.org). Оффлайн-хост: собирайте на машине с доступом в интернет, комплект переносите.

Проверка перенесённого архива на целевом сервере:

```bash
sha256sum -c SHA256SUMS.txt          # в каталоге релиза (Linux)
Get-FileHash .\archives\*.zip -Algorithm SHA256
```

## 3. Конфигурация (общая для обеих ОС)

Конфигурация читается из **каталога бинарника** (`bin/`): `appsettings.json` ищется по пути
`AppContext.BaseDirectory`, а не в каталоге запуска и не в корне релиза.

Приоритет источников: `bin/appsettings.json` < `bin/appsettings.local.json` < user-secrets
(только Development) < переменные окружения < аргументы командной строки.

В релизном архиве `appsettings.json` **отсутствует** (стендовая конфигурация репозитория
в бой не попадает) — скопируйте шаблон из того же `bin/` и замените `REPLACE_*`:

```bash
cp bin/appsettings.production.example.json bin/appsettings.json
```

| Ключ (env-форма) | Смысл |
|---|---|
| `ClickHouse__ConnectionString` | строка подключения ЦЕЛИКОМ (env переопределяет её целиком, а не дописывает) |
| `RasExporter__Servers__<i>__Name` / `__Host` | имя для журнала/health-ключей и адрес RAS — **оба обязательны** |
| `RasExporter__Servers__<i>__Port` | порт RAS (по умолчанию 1545) |
| `RasExporter__Servers__<i>__AgentAuth__User` / `__Password` | администратор агента (пустые = анонимный вход, если авторизация не включена) |
| `RasExporter__Servers__<i>__Clusters__0__ClusterName` | конкретный кластер; **не задавайте пустое значение** — переменная создаст элемент списка и валидация упадёт; отсутствие `Clusters` = автообнаружение |
| `RasExporter__Servers__<i>__Clusters__0__User` / `__Password` | администратор кластера |
| `RasExporter__Servers__<i>__PollIntervals__SessionsSeconds` и т.п. | частоты опроса групп метрик (1..86400 c) |
| `ClickHouse__DatabaseTemplate` | имя БД на кластер; для уникальности добавляйте `{ClusterId}` |
| `Logging__LogLevel__OneSTools.Ras` | `Information` в бою; `Debug` включает wire-трассировку кадров — только для расследований |
| `Logging__Console__FormatterOptions__onestools__TimestampFormat` | формат времени в строках журнала (по умолчанию `dd.MM.yyyy HH:mm:ss`) |
| `Logging__Console__FormatterOptions__onestools__UseUtcTimestamp` | `true` — UTC вместо локального времени сервера |
| `Logging__Console__FormatterName` | `simple` возвращает стандартный двухстрочный вывод консоли |

Конфигурация проверяется на старте (`ValidateOnStart`): серверы, порты, интервалы, размер
батча, строка подключения ClickHouse. При нарушениях процесс завершается с кодом 1 и
печатает в журнал/stderr (без стек-трейса): перечень нарушений, каталог, где искался
`appsettings.json` (найден/не найден), и пример env-переопределений. Первая строка запуска
всегда показывает, что именно подхвачено:

```
30.09.2026 12:59:35 [info] [OneSTools.Ras.Exporter.Startup] Конфигурация: каталог /opt/onestools/ras-exporter/current/bin/; appsettings.json найден; appsettings.local.json не найден; серверов RAS: 1
```

Права на файлы с секретами: только сервис-аккаунт (Linux `0640 root:rasexporter`,
Windows ACL на `bin\appsettings.local.json`).

## 4. Oracle Linux 9 (systemd)

Раскладка каталогов под обновление/откат:

```bash
sudo useradd --system --home-dir /opt/onestools/ras-exporter --shell /usr/sbin/nologin rasexporter
sudo mkdir -p /opt/onestools/ras-exporter/releases
sudo tar -xzf OneSTools.Ras.Exporter-0.1.1-linux-x64.tar.gz -C /tmp
sudo mv /tmp/OneSTools.Ras.Exporter-0.1.1-linux-x64 /opt/onestools/ras-exporter/releases/0.1.1
REL=/opt/onestools/ras-exporter/releases/0.1.1
sudo ln -sfn "$REL" /opt/onestools/ras-exporter/current
```

Конфиг и секреты:

```bash
sudo cp "$REL/bin/appsettings.production.example.json" "$REL/bin/appsettings.json"   # затем правка REPLACE_*
sudo mkdir -p /etc/onestools/ras-exporter
sudo cp "$REL/onestools-ras-exporter.env.example" /etc/onestools/ras-exporter/onestools-ras-exporter.env
sudo chown -R root:rasexporter /opt/onestools/ras-exporter /etc/onestools/ras-exporter
sudo chmod 0640 /etc/onestools/ras-exporter/onestools-ras-exporter.env
sudo chmod 0640 "$REL/bin/appsettings.json"
```

Служба — сначала разовый прогон от сервис-аккаунта с тем же окружением (`timeout -s INT`
даёт graceful shutdown), и только потом автозапуск:

```bash
sudo -u rasexporter timeout -s INT 30 bash -c \
  'set -a; . /etc/onestools/ras-exporter/onestools-ras-exporter.env; set +a; exec "$1"' \
  _ "$REL/bin/OneSTools.Ras.Exporter"

sudo cp "$REL/onestools-ras-exporter.service" /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now onestools-ras-exporter
```

Проверка конфигурации (первая строка прогона и то же у сервиса):

```bash
ls -l "$REL/bin/appsettings.json"                        # должен существовать, иначе серверов 0
systemctl show -p EnvironmentFiles onestools-ras-exporter # EnvironmentFile=- молча игнорирует отсутствующий файл
journalctl -u onestools-ras-exporter -n 20 | grep 'Конфигурация:'
```

Проверка работы:

```bash
systemctl status onestools-ras-exporter --no-pager
journalctl -u onestools-ras-exporter --since -10m | grep -E '\[(err|warn)\]|Health:|Exception'
clickhouse-client --query "SHOW DATABASES LIKE 'ras_cluster_%'"
clickhouse-client -d ras_cluster_maincluster --query \
  "SELECT _time, cluster_name FROM sessions ORDER BY _time DESC LIMIT 3"
```

Замечания по Linux:

- Бинарник самодостаточен: .NET Runtime ставить не нужно, `libicu` не нужен
  (сборка с `InvariantGlobalization`), достаточно glibc ≥ 2.17 (в OL9 — 2.34).
- SELinux enforcing: запуск сервиса из `/opt` обычно проходит (`unconfined_service_t`);
  при отказе смотрите `ausearch -m avc -ts recent` и размечайте
  `semanage fcontext -a -t bin_t '/opt/onestools/ras-exporter/releases/.*/bin/.*' && restorecon -R`.
- `MemoryDenyWriteExecute` в юните не выставлять — JIT .NET требует исполняемую память.
- Остановка — SIGINT с `TimeoutStopSec=120`: конвейер дописывает накопленные батчи
  (финальный drain до 2×5 c на тип метрик при недоступном ClickHouse).
- `StartLimitIntervalSec=60`/`StartLimitBurst=3` в юните: при битой конфигурации служба
  уходит в `failed` вместо бесконечного спам-рестарта; почините конфиг и `systemctl reset-failed`.
- Логи — в journald; `LimitNOFILE=65536`.

## 5. Windows (Scheduled Task)

Распакуйте архив, например в `C:\onestools\ras-exporter` (структура: `bin\` с exe и шаблоном
конфига, `run-as-task.cmd`, скрипты, `DEPLOYMENT.md`, `LICENSE`).

```powershell
# от администратора, PowerShell 5.1+
Copy-Item .\bin\appsettings.production.example.json .\bin\appsettings.json   # затем правка REPLACE_*
.\install-scheduled-task.ps1 -InstallDir 'C:\onestools\ras-exporter' -Start
```

Секреты — двумя способами: `bin\appsettings.local.json` рядом с бинарником с ACL только на
сервис-аккаунт, либо переменные уровня машины:

```powershell
[Environment]::SetEnvironmentVariable('ClickHouse__ConnectionString', 'Host=ch;Port=8123;Username=ras_exporter;Password=...', 'Machine')
```

Machine-scope переменные подхватываются заново запущенным процессом; после изменения
перезапустите задачу (`Stop`/`Start-ScheduledTask`), а если не применились — службу
Task Scheduler.

Проверка:

```powershell
Get-ScheduledTask -TaskName 'OneSTools.Ras.Exporter' | Get-ScheduledTaskInfo
Get-Content .\logs\exporter.log -Tail 40 | Select-String '\[err\]|\[warn\]|Health:'
```

Замечания по Windows:

- Журнал — файл `logs\exporter.log` (консольный вывод через обёртку `run-as-task.cmd`);
  ротация не встроена — её настраивают отдельно (второй Scheduled Task с чисткой/переносом
  файлов, агент сбора логов). Следите за объёмом: `OneSTools.Ras=Debug` даёт много строк.
- Остановка/снятие задачи завершает процесс принудительно — накопленные батчи теряются
  (экспорт best effort). Для штатной остановки предпочтительнее `Stop-ScheduledTask`
  после остановки опроса RAS на стороне источника, либо планируйте окно.
- Снятие: `.\uninstall-scheduled-task.ps1 [-RemoveLogs]` (бинарники и конфиг остаются).
- Настоящая служба Windows (`sc.exe create`) не используется: экспортёр не реализует
  протокол сервисного контроллера, при этом Scheduled Task с `-RestartCount`/
  `-StartWhenAvailable` даёт эквивалентный автозапуск без изменений в коде.

## 6. Наблюдение

Формат строки журнала (консольный форматтер `onestools`, одинаков для journald и файла на
Windows): `дата время [уровень] [категория] текст`, уровни — `trc|dbg|info|warn|err|crit`,
время — локальное на сервере (переключается, см. §3). Категория — сервер/кластер/компонент,
по ней и фильтруют (`[Cluster.MainCluster]`, `[RasServer.ras-prod-1]`). Исключения —
следующими строками с отступом:

```
30.09.2026 12:51:22 [err] [Cluster.Mock-Cluster-01] Ошибка инициализации базы данных ClickHouse
          System.Net.Http.HttpRequestException: Name or service not known (ch:8123)
           ---> System.Net.Sockets.SocketException (0xFFFDFFFF): Name or service not known
30.09.2026 12:51:31 [warn] [OneSTools.Ras.Exporter.Services.Health.LoggingHealthCheckPublisher] Health: Degraded | ras-cluster-sessions=Degraded (...)
```

Здоровье публикуется в журнал только при **переходах** состояния (шум подавлен),
первый проход — через 10 c после старта, далее каждые 30 c:

```
[info] [OneSTools.Ras.Exporter.Services.Health.LoggingHealthCheckPublisher] Health: Healthy
[warn] [...] Health: Degraded | ras-cluster-sessions=Degraded (ClickHouse недоступен — опрос RAS приостановлен, ожидание восстановления.)
[warn] [...] Health: Degraded | ras-cluster-sessions=Degraded (Потеряно элементов метрик: 12345.)
[err]  [...] Health: Unhealthy | ras-cluster-sessions=Unhealthy (Все сессии нездоровы: ...)
```

Что считать сигналом:

| Признак | Значение | Действие |
|---|---|---|
| `ClickHouse недоступен` | гейт приостановил опросы RAS, зонд каждые 30 c | чинить хранилище; метрики за простой **не** восстанавливаются |
| `Потеряно элементов метрик: N` | после исчерпания повторов порции отброшены | увеличить `BatchSize`/частоту ретраев, поднять ёмкость CH |
| `N/M сессий нездоровы` | SWP-соединение кластера потеряно (идёт переподключение) | проверить доступность RAS/сетевой сегмент |
| `нет свежего опроса` | коллектор не отдаёт данные дольше 2×интервала | временно включить `Debug`-логирование, посмотреть исключения |
| отсутствие строк `Health:` вообще | процесс не запущен/падает на старте | `systemctl status` / `Get-ScheduledTaskInfo`, первые строки журнала (валидация конфига) |
| `Конфигурация некорректна` + `Серверов RAS: 0` | `appsettings.json` не в каталоге бинарника или не заданы env | см. §3: файл нужен в `bin/`; `systemctl show -p EnvironmentFiles` |
| `RasExporter:Servers пуст: добавьте сервер…` | пустой список серверов (валидация) | задать `Name`+`Host` в `bin/appsettings.json` или env |
| `требуется ClusterId или ClusterName` | в env задано **пустое** `…Clusters__0__ClusterName=` | убрать переменную — включится автообнаружение |

HTTP-эндпоинта `/health` нет — мониторинг строится на журнале (grep/парсер логов,
`systemd` `ExecStartPost`-проверка, агент сбора логов).

## 7. Обновление и откат

Linux:

```bash
sudo tar -xzf OneSTools.Ras.Exporter-0.1.1-linux-x64.tar.gz -C /tmp
sudo mv /tmp/OneSTools.Ras.Exporter-0.1.1-linux-x64 /opt/onestools/ras-exporter/releases/0.1.1
sudo cp /opt/onestools/ras-exporter/releases/0.1.0/bin/appsettings.json /opt/onestools/ras-exporter/releases/0.1.1/bin/
sudo ln -sfn /opt/onestools/ras-exporter/releases/0.1.1 /opt/onestools/ras-exporter/current
sudo systemctl restart onestools-ras-exporter
# откат: ln -sfn .../releases/0.1.0 current && systemctl restart
```

Windows: `Stop-ScheduledTask`, замена `bin\` вместе с `bin\appsettings*.json` (шаблоны/скрипты
обновляются вместе с архивом), `Start-ScheduledTask`. ClickHouse-схема совместима между
версиями 0.x (DDL идемпотентен, `CREATE TABLE IF NOT EXISTS`); откат старым бинарником
безопасен.

## 8. Библиотека из локального фида

```bash
dotnet nuget add source /path/to/artifacts/v0.1.1/nuget --name onestools-local
dotnet add package OneSTools.Ras.Client
```

Публикация на nuget.org не выполнялась; пакеты не подписаны.

## 9. Ограничения, влияющие на эксплуатацию

- Только изолированный контур: без TLS/SASL (см. §1).
- 27 из 84 методов Java API; версии сервиса `3.0…14.0` (8.3.25.x). Поведение 15.0+ (8.4)
  не верифицировано.
- Поля `SessionInfo` разных версий платформы проверялись на 8.3.25.1520; при росте версии
  возможны расхождения в состав/порядке полей.
- Время в формате R8 — POSIX-время; метки `_time` — UTC.
- Метрики содержат имена пользователей 1С и адреса хостов клиентов (условно ПДн) —
  учитывайте при передаче журнала и при настройке доступа к ClickHouse.
- Проверка ИБ: чек-лист [SECURITY.md §5](SECURITY.md) перед вводом в эксплуатацию.
