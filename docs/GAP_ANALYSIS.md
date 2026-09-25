# GAP_ANALYSIS.md — Маппинг API-методов ↔ proto-контрактов

> Заполнено в Фазе 2. Соответствие методов `IAgentAdminConnection` и wire-сообщений из proto-контрактов.
> COVERED описывает наличие proto-контракта, а не наличие метода в C#-клиенте.
> Публичный `RasAdminClient` сейчас предоставляет 27 методов API из перечисленных 84.

## Правило

- **COVERED**: метод покрыт контрактом в [v8platform/protos](https://github.com/v8platform/protos)/rasapis/ras/messages/v1/ или `serializeapis/` — `message_type` присутствует в `types.proto` и определён в `*_messages.proto`.
- **GAP**: `message_type` отсутствует в `types.proto`, либо нет proto-определения сообщения, либо метод не имеет wire-протокола.

---

## Маппинг

### Авторизация и управление администраторами

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 1 | `authenticateAgent(user, pwd)` | `AUTHENTICATE_AGENT_REQUEST` | — (void) | COVERED |
| 2 | `authenticate(clusterId, user, pwd)` | `AUTHENTICATE_REQUEST` | — (void) | COVERED |
| 3 | `addAuthentication(clusterId, user, pwd)` | `ADD_AUTHENTICATION_REQUEST` | — (void) | COVERED |
| 4 | `getAgentAdmins()` | `GET_AGENT_ADMINS_REQUEST` | `AdminInfo[]` | COVERED |
| 5 | `getClusterAdmins(clusterId)` | `GET_CLUSTER_ADMINS_REQUEST` | `AdminInfo[]` | COVERED |
| 6 | `regAgentAdmin(adminInfo)` | `REG_AGENT_ADMIN_REQUEST` | — (void) | COVERED |
| 7 | `regClusterAdmin(clusterId, adminInfo)` | `REG_CLUSTER_ADMIN_REQUEST` | — (void) | COVERED |
| 8 | `unregAgentAdmin(adminName)` | `UNREG_AGENT_ADMIN_REQUEST` | — (void) | COVERED |
| 9 | `unregClusterAdmin(clusterId, adminName)` | `UNREG_CLUSTER_ADMIN_REQUEST` | — (void) | COVERED |
| 10 | `getAgentVersion()` | `GET_AGENT_VERSION_REQUEST` | `string version` | COVERED |

### Кластеры

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 11 | `getClusters()` | `GET_CLUSTERS_REQUEST` | `ClusterInfo[]` | COVERED |
| 12 | `getClusterInfo(clusterId)` | `GET_CLUSTER_INFO_REQUEST` | `ClusterInfo` | COVERED |
| 13 | `regCluster(clusterInfo)` | `REG_CLUSTER_REQUEST` | `UUID (cluster_id)` | COVERED |
| 14 | `unregCluster(clusterId)` | `UNREG_CLUSTER_REQUEST` | — (void) | COVERED |

### Менеджеры кластера

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 15 | `getClusterManagers(clusterId)` | `GET_CLUSTER_MANAGERS_REQUEST` | `ManagerInfo[]` | COVERED |
| 16 | `getClusterManagerInfo(clusterId, managerId)` | `GET_CLUSTER_MANAGER_INFO_REQUEST` | `ManagerInfo` | COVERED |

### Сервисы кластера

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 17 | `getClusterServices(clusterId)` | `GET_CLUSTER_SERVICES_REQUEST` | `ServiceInfo[]` | COVERED |

### Рабочие серверы

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 18 | `getWorkingServers(clusterId)` | `GET_WORKING_SERVERS_REQUEST` | `ServerInfo[]` | COVERED |
| 19 | `getWorkingServerInfo(clusterId, serverId)` | `GET_WORKING_SERVER_INFO_REQUEST` | `ServerInfo` | COVERED |
| 20 | `regWorkingServer(clusterId, serverInfo)` | `REG_WORKING_SERVER_REQUEST` | `UUID (server_id)` | COVERED |
| 21 | `unregWorkingServer(clusterId, serverId)` | `UNREG_WORKING_SERVER_REQUEST` | — (void) | COVERED |

### Рабочие процессы

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 22 | `getWorkingProcesses(clusterId)` | `GET_WORKING_PROCESSES_REQUEST` | `ProcessInfo[]` | COVERED |
| 23 | `getWorkingProcessInfo(clusterId, processId)` | `GET_WORKING_PROCESS_INFO_REQUEST` | `ProcessInfo` | COVERED |
| 24 | `getServerWorkingProcesses(clusterId, serverId)` | `GET_SERVER_WORKING_PROCESSES_REQUEST` | `ProcessInfo[]` | COVERED |

### Инфобазы

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 25 | `getInfoBasesShort(clusterId)` | `GET_INFOBASES_SHORT_REQUEST` | `InfobaseSummaryInfo[]` | COVERED |
| 26 | `getInfoBases(clusterId)` | `GET_INFOBASES_REQUEST` | `InfobaseInfo[]` | COVERED |
| 27 | `getInfoBaseShortInfo(clusterId, infoBaseId)` | `GET_INFOBASE_SHORT_INFO_REQUEST` | `InfobaseSummaryInfo` | GAP |
| 28 | `getInfoBaseInfo(clusterId, infoBaseId)` | `GET_INFOBASE_INFO_REQUEST` | `InfobaseInfo` | COVERED |
| 29 | `createInfoBase(clusterId, info, mode)` | `CREATE_INFOBASE_REQUEST` | `UUID (infobase_id)` | COVERED |
| 30 | `updateInfoBaseShort(clusterId, info)` | `UPDATE_INFOBASE_SHORT_REQUEST` | — (void) | COVERED |
| 31 | `updateInfoBase(clusterId, info)` | `UPDATE_INFOBASE_REQUEST` | — (void) | COVERED |
| 32 | `dropInfoBase(clusterId, infoBaseId, mode)` | `DROP_INFOBASE_REQUEST` | — (void) | COVERED |

### Соединения

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 33 | `getConnectionsShort(clusterId)` | `GET_CONNECTIONS_SHORT_REQUEST` | `ConnectionInfo[]` | COVERED |
| 34 | `getInfoBaseConnectionsShort(clusterId, infoBaseId)` | `GET_INFOBASE_CONNECTIONS_SHORT_REQUEST` | `ConnectionInfo[]` | COVERED |
| 35 | `getConnectionInfoShort(clusterId, connectionId)` | `GET_CONNECTION_INFO_SHORT_REQUEST` | `ConnectionInfo` | GAP |
| 36 | `getInfoBaseConnections(clusterId, infoBaseId, processId)` | `GET_INFOBASE_CONNECTIONS_REQUEST` | `ConnectionInfo[]` | GAP |
| 37 | `disconnect(clusterId, processId, connectionId)` | `DISCONNECT_REQUEST` | — (void) | COVERED |

> Примечание: типы 54–57 (`GET_CONNECTION_INFO_SHORT_REQUEST/RESPONSE`, `GET_INFOBASE_CONNECTIONS_REQUEST/RESPONSE`) зарезервированы в `types.proto`, но proto-определения сообщений отсутствуют (отмечены как «не реализованы» в `connections_messages.proto`).

### Сессии

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 38 | `getSessions(clusterId)` | `GET_SESSIONS_REQUEST` | `SessionInfo[]` | COVERED |
| 39 | `getInfoBaseSessions(clusterId, infoBaseId)` | `GET_INFOBASE_SESSIONS_REQUEST` | `SessionInfo[]` | COVERED |
| 40 | `getSessionInfo(clusterId, sessionId)` | `GET_SESSION_INFO_REQUEST` | `SessionInfo` | COVERED |
| 41 | `terminateSession(clusterId, sessionId)` | `TERMINATE_SESSION_REQUEST` | — (void) | COVERED |
| 42 | `terminateSession(clusterId, sessionId, message)` | `TERMINATE_SESSION_REQUEST` | — (void) | COVERED |

### Блокировки

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 43 | `getLocks(clusterId)` | `GET_LOCKS_REQUEST` | `LockInfo[]` | COVERED |
| 44 | `getInfoBaseLocks(clusterId, infoBaseId)` | `GET_INFOBASE_LOCKS_REQUEST` | `LockInfo[]` | COVERED |
| 45 | `getConnectionLocks(clusterId, connectionId)` | `GET_CONNECTION_LOCKS_REQUEST` | `LockInfo[]` | COVERED |
| 46 | `getSessionLocks(clusterId, infoBaseId, sessionId)` | `GET_SESSION_LOCKS_REQUEST` | `LockInfo[]` | COVERED |

### Правила распределения

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 47 | `applyAssignmentRules(clusterId, sessionId)` | `APPLY_ASSIGNMENT_RULES_REQUEST` | — (void) | COVERED |
| 48 | `getAssignmentRules(clusterId, serverId)` | `GET_ASSIGNMENT_RULES_REQUEST` | `AssignmentInfo[]` | COVERED |
| 49 | `regAssignmentRule(clusterId, serverId, rule, pos)` | `REG_ASSIGNMENT_RULE_REQUEST` | `UUID (rule_id)` | COVERED |
| 50 | `unregAssignmentRule(clusterId, serverId, ruleId)` | `UNREG_ASSIGNMENT_RULE_REQUEST` | — (void) | COVERED |
| 51 | `getAssignmentRuleInfo(clusterId, serverId, ruleId)` | `GET_ASSIGNMENT_RULE_INFO_REQUEST` | `AssignmentInfo` | COVERED |

### Безопасность (профили безопасности)

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 52 | `getSecurityProfiles(clusterId)` | `GET_SECURITY_PROFILES_REQUEST` | `ISecurityProfile[]` | GAP |
| 53 | `createSecurityProfile(clusterId, profile)` | `CREATE_SECURITY_PROFILE_REQUEST` | — (void) | GAP |
| 54 | `dropSecurityProfile(clusterId, spName)` | `DROP_SECURITY_PROFILE_REQUEST` | — (void) | GAP |
| 55 | `getSecurityProfileVirtualDirectories(clusterId, spName)` | `GET_VIRTUAL_DIRECTORIES_REQUEST` | `ISecurityProfileVirtualDirectory[]` | GAP |
| 56 | `createSecurityProfileVirtualDirectory(clusterId, dir)` | `CREATE_VIRTUAL_DIRECTORY_REQUEST` | — (void) | GAP |
| 57 | `dropSecurityProfileVirtualDirectory(clusterId, spName, alias)` | `DROP_VIRTUAL_DIRECTORY_REQUEST` | — (void) | GAP |
| 58 | `getSecurityProfileComClasses(clusterId, spName)` | `GET_COM_CLASSES_REQUEST` | `ISecurityProfileCOMClass[]` | GAP |
| 59 | `createSecurityProfileComClass(clusterId, comClass)` | `CREATE_COM_CLASS_REQUEST` | — (void) | GAP |
| 60 | `dropSecurityProfileComClass(clusterId, spName, name)` | `DROP_COM_CLASS_REQUEST` | — (void) | GAP |
| 61 | `getSecurityProfileAddIns(clusterId, spName)` | `GET_ALLOWED_ADDINS_REQUEST` | `ISecurityProfileAddIn[]` | GAP |
| 62 | `createSecurityProfileAddIn(clusterId, addIn)` | `CREATE_ALLOWED_ADDIN_REQUEST` | — (void) | GAP |
| 63 | `dropSecurityProfileAddIn(clusterId, spName, name)` | `DROP_ALLOWED_ADDIN_REQUEST` | — (void) | GAP |
| 64 | `getSecurityProfileUnsafeExternalModules(clusterId, spName)` | `GET_EXTERNAL_MODULES_REQUEST` | `ISecurityProfileExternalModule[]` | GAP |
| 65 | `createSecurityProfileUnsafeExternalModule(clusterId, module)` | `CREATE_EXTERNAL_MODULE_REQUEST` | — (void) | GAP |
| 66 | `dropSecurityProfileUnsafeExternalModule(clusterId, spName, name)` | `DROP_EXTERNAL_MODULE_REQUEST` | — (void) | GAP |
| 67 | `getSecurityProfileApplications(clusterId, spName)` | `GET_ALLOWED_APPLICATIONS_REQUEST` | `ISecurityProfileApplication[]` | GAP |
| 68 | `createSecurityProfileApplication(clusterId, app)` | `CREATE_ALLOWED_APPLICATION_REQUEST` | — (void) | GAP |
| 69 | `dropSecurityProfileApplication(clusterId, spName, name)` | `DROP_ALLOWED_APPLICATION_REQUEST` | — (void) | GAP |
| 70 | `getSecurityProfileInternetResources(clusterId, spName)` | `GET_INTERNET_RESOURCES_REQUEST` | `ISecurityProfileInternetResource[]` | GAP |
| 71 | `createSecurityProfileInternetResource(clusterId, res)` | `CREATE_INTERNET_RESOURCE_REQUEST` | — (void) | GAP |
| 72 | `dropSecurityProfileInternetResource(clusterId, spName, name)` | `DROP_INTERNET_RESOURCE_REQUEST` | — (void) | GAP |

> Примечание: все 21 `message_type` для безопасности присутствуют в `types.proto`, однако **ни одно proto-определение сообщения** (с полями) в `rasapis/ras/messages/v1/` не реализовано. Сериализация формата безопасности не описана в proto.

### Управление вызовами

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 73 | `interruptCurrentServerCall(clusterId, sessionId, message)` | `INTERRUPT_SESSION_CURRENT_SERVER_CALL_REQUEST` | — (void) | GAP |

> Примечание: `message_type` есть в `types.proto` (№117), но proto-определение сообщения отсутствует.

### Потребление ресурсов (счётчики)

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 74 | `getResourceConsumptionCounters(clusterId)` | `GET_RESOURCE_COUNTERS_REQUEST` | `IResourceConsumptionCounter[]` | GAP |
| 75 | `getResourceConsumptionCounterInfo(clusterId, name)` | `GET_RESOURCE_COUNTER_INFO_REQUEST` | `IResourceConsumptionCounter` | GAP |
| 76 | `regResourceConsumptionCounter(clusterId, counter)` | `REG_RESOURCE_COUNTER_REQUEST` | — (void) | GAP |
| 77 | `unregResourceConsumptionCounter(clusterId, name)` | `UNREG_RESOURCE_COUNTER_REQUEST` | — (void) | GAP |

> Примечание: все `message_type` для счётчиков есть в `types.proto` (№118–123), но proto-определения сообщений отсутствуют.

### Потребление ресурсов (лимиты)

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 78 | `getResourceConsumptionLimits(clusterId)` | `GET_RESOURCE_LIMITS_REQUEST` | `IResourceConsumptionLimit[]` | GAP |
| 79 | `getResourceConsumptionLimitInfo(clusterId, name)` | `GET_RESOURCE_LIMIT_INFO_REQUEST` | `IResourceConsumptionLimit` | GAP |
| 80 | `regResourceConsumptionLimit(clusterId, limit)` | `REG_RESOURCE_LIMIT_REQUEST` | — (void) | GAP |
| 81 | `unregResourceConsumptionLimit(clusterId, name)` | `UNREG_RESOURCE_LIMIT_REQUEST` | — (void) | GAP |

> Примечание: все `message_type` для лимитов есть в `types.proto` (№124–129), но proto-определения сообщений отсутствуют.

### Потребление ресурсов (значения счётчиков)

| # | Метод Java | wire_msg_type | Модель ответа | Статус |
|---|-----------|--------------|---------------|--------|
| 82 | `getResourceConsumptionCounterValues(clusterId, counterName, objectName)` | `GET_COUNTER_VALUES_REQUEST` | `IResourceConsumptionCounterValue[]` | GAP |
| 83 | `clearResourceConsumptionCounterAccumulatedValues(clusterId, counterName, objectName)` | `CLEAR_COUNTER_VALUE_REQUEST` | — (void) | GAP |
| 84 | `getResourceConsumptionCounterAccumulatedValues(clusterId, counterName, objectName)` | `GET_COUNTER_ACCUMULATED_VALUES_REQUEST` | `IResourceConsumptionCounterValue[]` | GAP |

> Примечание: все `message_type` для значений есть в `types.proto` (№130–134), но proto-определения сообщений отсутствуют.

---

## Сводка

| Статус | Количество | Доля |
|--------|-----------|------|
| COVERED | 48 | 57.1% |
| GAP | 36 | 42.9% |
| **Итого** | **84** | **100%** |

### Разбивка по группам

| Группа | Всего | COVERED | GAP |
|--------|-------|---------|-----|
| Авторизация и администраторы | 10 | 10 | 0 |
| Кластеры | 4 | 4 | 0 |
| Менеджеры кластера | 2 | 2 | 0 |
| Сервисы кластера | 1 | 1 | 0 |
| Рабочие серверы | 4 | 4 | 0 |
| Рабочие процессы | 3 | 3 | 0 |
| Инфобазы | 8 | 7 | 1 |
| Соединения | 5 | 3 | 2 |
| Сессии | 5 | 5 | 0 |
| Блокировки | 4 | 4 | 0 |
| Правила распределения | 5 | 5 | 0 |
| Безопасность | 21 | 0 | 21 |
| Управление вызовами | 1 | 0 | 1 |
| Счётчики ресурсов | 4 | 0 | 4 |
| Лимиты ресурсов | 4 | 0 | 4 |
| Значения счётчиков | 3 | 0 | 3 |

### Анализ GAP

Основная масса GAP-ов (32 из 36) связана с отсутствием proto-определений для **профилей безопасности** (21) и **потребления ресурсов** (11). Типы сообщений (`message_type`) зарезервированы в `types.proto`, но:
- Нет файлов `security_profiles_messages.proto`, `resource_counters_messages.proto` и т.д.
- Нет моделей данных `serialize.v1` для SecurityProfile, ResourceConsumptionCounter, ResourceConsumptionLimit и подобных.

Остальные GAP-ы:
- `getConnectionInfoShort` (№35) и `getInfoBaseConnections` (№36) — типы 54–57 зарезервированы, но proto-определения отсутствуют; в комментариях к `connections_messages.proto` указано «не реализованы».
- `getInfoBaseShortInfo` (№27) — тип 46–47 есть в `types.proto`, но proto-определение отсутствует.
- `interruptCurrentServerCall` (№73) — тип 117 есть в `types.proto`, но proto-определение отсутствует.

---

## Источники

- [v8platform/protos](https://github.com/v8platform/protos)/rasapis/ras/messages/v1/types.proto — перечисление MessageType
- [v8platform/protos](https://github.com/v8platform/protos)/rasapis/ras/messages/v1/*.proto — определения сообщений
- [v8platform/protos](https://github.com/v8platform/protos)/serializeapis/v8platform/serialize/v1/*.proto — модели данных
- `com._1c.v8.ibis.admin.IAgentAdminConnection` (официальная Java-библиотека 1С) — Java API
- `com._1c.v8.ibis.admin.*` — модели данных
