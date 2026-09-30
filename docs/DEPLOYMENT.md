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

## 2. Сборка комплекта

```bash
bash tools/build-release.sh --version 0.1.0 --tag
# артефакты: artifacts/v0.1.0/{archives,nuget,RELEASE_NOTES.md,SHA256SUMS.txt}
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

Приоритет источников: `appsettings.json` < `appsettings.local.json` < user-secrets
(только Development) < переменные окружения < аргументы командной строки.

В релизном архиве `appsettings.json` **отсутствует** (стендовая конфигурация репозитория
в бой не попадает) — скопируйте шаблон и замените `REPLACE_*`:

```bash
cp appsettings.production.example.json appsettings.json
```

| Ключ (env-форма) | Смысл |
|---|---|
| `ClickHouse__ConnectionString` | строка подключения ЦЕЛИКОМ (env переопределяет её целиком, а не дописывает) |
| `RasExporter__Servers__<i>__Host` / `__Port` | адрес RAS сервера |
| `RasExporter__Servers__<i>__AgentAuth__User` / `__Password` | администратор агента (пустые = анонимный вход, если авторизация не включена) |
| `RasExporter__Servers__<i>__Clusters__0__ClusterName` | конкретный кластер; `Clusters: []` = автообнаружение всех кластеров сервера |
| `RasExporter__Servers__<i>__Clusters__0__User` / `__Password` | администратор кластера |
| `RasExporter__Servers__<i>__PollIntervals__SessionsSeconds` и т.п. | частоты опроса групп метрик (1..86400 c) |
| `ClickHouse__DatabaseTemplate` | имя БД на кластер; для уникальности добавляйте `{ClusterId}` |
| `Logging__LogLevel__OneSTools.Ras` | `Information` в бою; `Debug` включает wire-трассировку кадров — только для расследований |

Конфигурация проверяется на старте (`ValidateOnStart`): серверы, порты, интервалы, размер
батча, имя БД. Ошибка конфигурации = падение при запуске, текст ошибки — в журнале.

Права на файлы с секретами: только сервис-аккаунт (Linux `0640 root:rasexporter`,
Windows ACL на `appsettings.local.json`).

## 4. Oracle Linux 9 (systemd)

Раскладка каталогов под обновление/откат:

```bash
sudo useradd --system --home-dir /opt/onestools/ras-exporter --shell /usr/sbin/nologin rasexporter
sudo mkdir -p /opt/onestools/ras-exporter/releases
sudo tar -xzf OneSTools.Ras.Exporter-0.1.0-linux-x64.tar.gz -C /tmp
sudo mv /tmp/OneSTools.Ras.Exporter-0.1.0-linux-x64 /opt/onestools/ras-exporter/releases/0.1.0
REL=/opt/onestools/ras-exporter/releases/0.1.0
sudo ln -sfn "$REL" /opt/onestools/ras-exporter/current
```

Конфиг и секреты:

```bash
sudo cp "$REL/appsettings.production.example.json" "$REL/appsettings.json"   # затем правка REPLACE_*
sudo mkdir -p /etc/onestools/ras-exporter
sudo cp "$REL/onestools-ras-exporter.env.example" /etc/onestools/ras-exporter/onestools-ras-exporter.env
sudo chown -R root:rasexporter /opt/onestools/ras-exporter /etc/onestools/ras-exporter
sudo chmod 0640 /etc/onestools/ras-exporter/onestools-ras-exporter.env
sudo chmod 0640 "$REL/appsettings.json"
```

Служба:

```bash
sudo cp "$REL/onestools-ras-exporter.service" /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now onestools-ras-exporter
```

Перед включением автозапуска — разовый прогон от сервис-аккаунта с тем же окружением
(`timeout -s INT` даёт graceful shutdown, как в dev-смоке):

```bash
sudo -u rasexporter timeout -s INT 30 bash -c \
  'set -a; . /etc/onestools/ras-exporter/onestools-ras-exporter.env; set +a; exec "$1"' \
  _ "$REL/bin/OneSTools.Ras.Exporter"
```

Проверка:

```bash
systemctl status onestools-ras-exporter --no-pager
journalctl -u onestools-ras-exporter --since -10m | grep -E 'Health:|error|Exception'
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
- Логи — в journald; `LimitNOFILE=65536`.

## 5. Windows (Scheduled Task)

Распакуйте архив, например в `C:\onestools\ras-exporter` (структура: `bin\`,
`run-as-task.cmd`, скрипты, шаблоны конфига).

```powershell
# от администратора, PowerShell 5.1+
Copy-Item .\appsettings.production.example.json .\appsettings.json   # затем правка REPLACE_*
.\install-scheduled-task.ps1 -InstallDir 'C:\onestools\ras-exporter' -Start
```

Секреты — двумя способами: `appsettings.local.json` рядом с бинарником с ACL только на
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
Get-Content .\logs\exporter.log -Tail 40 | Select-String 'Health:|error'
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

Здоровье публикуется в журнал только при **переходах** состояния (шум подавлен),
первый проход — через 10 c после старта, далее каждые 30 c:

```
Health: Healthy
Health: Degraded | ras-cluster-sessions=Degraded (ClickHouse недоступен — опрос RAS приостановлен, ожидание восстановления.)
Health: Degraded | ras-cluster-sessions=Degraded (Потеряно элементов метрик: 12345.)
Health: Unhealthy | ras-cluster-sessions=Unhealthy (Все сессии нездоровы: ...)
```

Что считать сигналом:

| Признак | Значение | Действие |
|---|---|---|
| `ClickHouse недоступен` | гейт приостановил опросы RAS, зонд каждые 30 c | чинить хранилище; метрики за простой **не** восстанавливаются |
| `Потеряно элементов метрик: N` | после исчерпания повторов порции отброшены | увеличить `BatchSize`/частоту ретраев, поднять ёмкость CH |
| `N/M сессий нездоровы` | SWP-соединение кластера потеряно (идёт переподключение) | проверить доступность RAS/сетевой сегмент |
| `нет свежего опроса` | коллектор не отдаёт данные дольше 2×интервала | временно включить `Debug`-логирование, посмотреть исключения |
| отсутствие строк `Health:` вообще | процесс не запущен/падает на старте | `systemctl status` / `Get-ScheduledTaskInfo`, первые строки журнала (валидация конфига) |

HTTP-эндпоинта `/health` нет — мониторинг строится на журнале (grep/парсер логов,
`systemd` `ExecStartPost`-проверка, агент сбора логов).

## 7. Обновление и откат

Linux:

```bash
sudo tar -xzf OneSTools.Ras.Exporter-0.1.1-linux-x64.tar.gz -C /tmp
sudo mv /tmp/OneSTools.Ras.Exporter-0.1.1-linux-x64 /opt/onestools/ras-exporter/releases/0.1.1
sudo cp /opt/onestools/ras-exporter/releases/0.1.0/appsettings.json /opt/onestools/ras-exporter/releases/0.1.1/
sudo ln -sfn /opt/onestools/ras-exporter/releases/0.1.1 /opt/onestools/ras-exporter/current
sudo systemctl restart onestools-ras-exporter
# откат: ln -sfn .../releases/0.1.0 current && systemctl restart
```

Windows: `Stop-ScheduledTask`, замена `bin\` и `appsettings*.json` (шаблоны/скрипты
обновляются вместе с архивом), `Start-ScheduledTask`. ClickHouse-схема совместима между
версиями 0.x (DDL идемпотентен, `CREATE TABLE IF NOT EXISTS`); откат старым бинарником
безопасен.

## 8. Библиотека из локального фида

```bash
dotnet nuget add source /path/to/artifacts/v0.1.0/nuget --name onestools-local
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
