# OneSTools.Ras.Client

Независимый C#-клиент сервера администрирования 1С (RAS) по бинарному протоколу SWP:
транспорт, 17 wire-кодеков, протокольные/API-сообщения и модели. Без Java SDK (`com._1c.v8.*`)
и без обращения к утилите `rac`.

**Реализация НЕ официальная**: не разработана, не одобрена и не аффилирована с фирмой «1С».
Совместимость обеспечена независимым поведенческим анализом (см. `docs/` в репозитории проекта).
Протокол **без TLS/SASL** — эксплуатация только в изолированном контуре.

## Установка (локальный фид)

```bash
dotnet nuget add source /path/to/nuget-feed --name onestools-local
dotnet add package OneSTools.Ras.Client
```

## Пример

```csharp
using OneSTools.Ras.Client;

await using var ras = new RasAdminClient("ras-host", 1545);
await ras.ConnectAsync();
var clusters = await ras.GetClustersAsync();
await ras.AuthenticateClusterAsync(clusters[0].Uuid, "", "");   // нужен даже без пароля
var sessions = await ras.GetSessionsAsync(clusters[0].Uuid);
```

## Область

27 методов в `RasAdminClient` из 84 методов Java API; negotiation версий сервиса `3.0…14.0`
(подтверждено стендом 8.3.25.1520). Часть методов не портирована — см. `docs/GAP_ANALYSIS.md`.

MIT © 2026 Makurin Egor. «1С:Предприятие» — товарный знак фирмы «1С».
