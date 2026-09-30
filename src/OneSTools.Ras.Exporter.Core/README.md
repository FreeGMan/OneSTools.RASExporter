# OneSTools.Ras.Exporter.Core

Ядро периодического сбора метрик RAS («1С:Предприятие») и пакетной записи в ClickHouse:

- `Collectors/` — опрос сессий, процессов, соединений, блокировок и сводки кластера
  (`ClusterCollectorIntervals` задаёт частоту каждой группы);
- `Pipeline/` — `MetricsBatchingPipeline`: ограниченные очереди, батчинг, ретраи с бэкоффом,
  `StorageHealthGate` (пауза опросов при недоступном хранилище, зонд каждые 30 c);
- `ClickHouse/` — `ClickHouseMetricsStorage`: DDL схем, `_bulk_insert_` через `ClickHouse.Client`,
  slugify имён баз (`ClickHouseDatabaseNamer`).

Зависит от `OneSTools.Ras.Client` (то же независимое нереализованное официально описание SWP).
Готовая служба — проект `OneSTools.Ras.Exporter` в репозитории.

**Ограничения**: экспорт best effort (без устойчивой очереди — после исчерпания повторов
порции отбрасываются со счётчиком потерь), без TLS/SASL, формат R8 — только POSIX-время.

MIT © 2026 Makurin Egor. «1С:Предприятие» — товарный знак фирмы «1С».
