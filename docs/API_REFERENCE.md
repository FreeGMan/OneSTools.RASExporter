# API_REFERENCE.md — API IAgentAdminConnection и модели данных

> Заполнено в Фазах 1 и 2. Сигнатуры из Java-декомпиляции, wire-маппинг из proto-контрактов.

## 1. Точка входа

### 1.1 Фабрика коннекторов

**Интерфейс:** `IAgentAdminConnectorFactory`

```java
// Создать коннектор с таймаутом подключения
IAgentAdminConnector createConnector(long connectTimeout);

// Создать коннектор с пользовательским таймером, пулом потоков и таймаутом
IAgentAdminConnector createConnector(Timer timer, Executor executor, long connectTimeout);
```

**Реализация:** `AgentAdminConnectorFactory` — создаёт экземпляры `AgentAdminConnector`.

### 1.2 Коннектор

**Интерфейс:** `IAgentAdminConnector`

```java
// Установить соединение с агентом администрирования
IAgentAdminConnection connect(String host, int port);

// Закрыть конкретное соединение
void close(IAgentAdminConnection connection);

// Завершить все соединения и освободить ресурсы
void shutdown();
```

**Реализация:** `AgentAdminConnector`
- `connect()` устанавливает `IV8Connection`, создаёт `AdministrationServiceManager` с кодировкой `"deflate"`, получает прокси `IAgentAdminConnection`.
- `close()` вызывает `stop()` на внутреннем `IV8Connection`.
- `shutdown()` закрывает все соединения и отменяет внутренний `Timer`.

---

## 2. IAgentAdminConnection — полный список методов

### 2.1 Авторизация и управление администраторами

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 1 | `void authenticateAgent(String user, String password)` | Авторизация администратора на сервере 1С (агенте) | `AUTHENTICATE_AGENT_REQUEST` |
| 2 | `void authenticate(UUID clusterId, String user, String password)` | Авторизация администратора на кластере | `AUTHENTICATE_REQUEST` |
| 3 | `void addAuthentication(UUID clusterId, String user, String password)` | Добавление аутентификации пользователя ИБ | `ADD_AUTHENTICATION_REQUEST` |
| 4 | `List<IRegUserInfo> getAgentAdmins()` | Получение списка администраторов агента | `GET_AGENT_ADMINS_REQUEST` |
| 5 | `List<IRegUserInfo> getClusterAdmins(UUID clusterId)` | Получение списка администраторов кластера | `GET_CLUSTER_ADMINS_REQUEST` |
| 6 | `void regAgentAdmin(IRegUserInfo adminInfo)` | Регистрация нового администратора агента | `REG_AGENT_ADMIN_REQUEST` |
| 7 | `void regClusterAdmin(UUID clusterId, IRegUserInfo adminInfo)` | Регистрация нового администратора кластера | `REG_CLUSTER_ADMIN_REQUEST` |
| 8 | `void unregAgentAdmin(String adminName)` | Удаление администратора агента | `UNREG_AGENT_ADMIN_REQUEST` |
| 9 | `void unregClusterAdmin(UUID clusterId, String adminName)` | Удаление администратора кластера | `UNREG_CLUSTER_ADMIN_REQUEST` |
| 10 | `String getAgentVersion()` | Получение версии сервера 1С (агента) | `GET_AGENT_VERSION_REQUEST` |

### 2.2 Кластеры

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 11 | `List<IClusterInfo> getClusters()` | Получение списка кластеров | `GET_CLUSTERS_REQUEST` |
| 12 | `IClusterInfo getClusterInfo(UUID clusterId)` | Получение информации о кластере | `GET_CLUSTER_INFO_REQUEST` |
| 13 | `UUID regCluster(IClusterInfo clusterInfo)` | Регистрация нового кластера | `REG_CLUSTER_REQUEST` |
| 14 | `void unregCluster(UUID clusterId)` | Удаление регистрации кластера | `UNREG_CLUSTER_REQUEST` |

### 2.3 Менеджеры кластера

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 15 | `List<IClusterManagerInfo> getClusterManagers(UUID clusterId)` | Получение списка менеджеров кластера | `GET_CLUSTER_MANAGERS_REQUEST` |
| 16 | `IClusterManagerInfo getClusterManagerInfo(UUID clusterId, UUID managerId)` | Получение информации о менеджере кластера | `GET_CLUSTER_MANAGER_INFO_REQUEST` |

### 2.4 Сервисы кластера

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 17 | `List<IClusterServiceInfo> getClusterServices(UUID clusterId)` | Получение списка сервисов кластера | `GET_CLUSTER_SERVICES_REQUEST` |

### 2.5 Рабочие серверы

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 18 | `List<IWorkingServerInfo> getWorkingServers(UUID clusterId)` | Получение списка рабочих серверов | `GET_WORKING_SERVERS_REQUEST` |
| 19 | `IWorkingServerInfo getWorkingServerInfo(UUID clusterId, UUID serverId)` | Получение информации о рабочем сервере | `GET_WORKING_SERVER_INFO_REQUEST` |
| 20 | `UUID regWorkingServer(UUID clusterId, IWorkingServerInfo serverInfo)` | Регистрация нового рабочего сервера | `REG_WORKING_SERVER_REQUEST` |
| 21 | `void unregWorkingServer(UUID clusterId, UUID serverId)` | Удаление рабочего сервера | `UNREG_WORKING_SERVER_REQUEST` |

### 2.6 Рабочие процессы

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 22 | `List<IWorkingProcessInfo> getWorkingProcesses(UUID clusterId)` | Получение списка рабочих процессов кластера | `GET_WORKING_PROCESSES_REQUEST` |
| 23 | `IWorkingProcessInfo getWorkingProcessInfo(UUID clusterId, UUID processId)` | Получение информации о рабочем процессе | `GET_WORKING_PROCESS_INFO_REQUEST` |
| 24 | `List<IWorkingProcessInfo> getServerWorkingProcesses(UUID clusterId, UUID serverId)` | Получение списка рабочих процессов сервера | `GET_SERVER_WORKING_PROCESSES_REQUEST` |

### 2.7 Инфобазы

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 25 | `List<IInfoBaseInfoShort> getInfoBasesShort(UUID clusterId)` | Получение краткого списка инфобаз | `GET_INFOBASES_SHORT_REQUEST` |
| 26 | `List<IInfoBaseInfo> getInfoBases(UUID clusterId)` | Получение полного списка инфобаз | `GET_INFOBASES_REQUEST` |
| 27 | `IInfoBaseInfoShort getInfoBaseShortInfo(UUID clusterId, UUID infoBaseId)` | Получение краткой информации об инфобазе | `GET_INFOBASE_SHORT_INFO_REQUEST` |
| 28 | `IInfoBaseInfo getInfoBaseInfo(UUID clusterId, UUID infoBaseId)` | Получение полной информации об инфобазе | `GET_INFOBASE_INFO_REQUEST` |
| 29 | `UUID createInfoBase(UUID clusterId, IInfoBaseInfo info, int mode)` | Создание инфобазы на кластере | `CREATE_INFOBASE_REQUEST` |
| 30 | `void updateInfoBaseShort(UUID clusterId, IInfoBaseInfoShort info)` | Обновление краткой информации об инфобазе | `UPDATE_INFOBASE_SHORT_REQUEST` |
| 31 | `void updateInfoBase(UUID clusterId, IInfoBaseInfo info)` | Обновление полной информации об инфобазе | `UPDATE_INFOBASE_REQUEST` |
| 32 | `void dropInfoBase(UUID clusterId, UUID infoBaseId, int mode)` | Удаление инфобазы с кластера | `DROP_INFOBASE_REQUEST` |

### 2.8 Соединения

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 33 | `List<IInfoBaseConnectionShort> getConnectionsShort(UUID clusterId)` | Получение краткого списка соединений кластера | `GET_CONNECTIONS_SHORT_REQUEST` |
| 34 | `List<IInfoBaseConnectionShort> getInfoBaseConnectionsShort(UUID clusterId, UUID infoBaseId)` | Получение краткого списка соединений инфобазы | `GET_INFOBASE_CONNECTIONS_SHORT_REQUEST` |
| 35 | `IInfoBaseConnectionShort getConnectionInfoShort(UUID clusterId, UUID connectionId)` | Получение краткой информации о соединении | `GET_CONNECTION_INFO_SHORT_REQUEST` |
| 36 | `List<IInfoBaseConnectionInfo> getInfoBaseConnections(UUID clusterId, UUID infoBaseId, UUID processId)` | Получение полного списка соединений инфобазы | `GET_INFOBASE_CONNECTIONS_REQUEST` |
| 37 | `void disconnect(UUID clusterId, UUID processId, UUID connectionId)` | Отключение соединения | `DISCONNECT_REQUEST` |

### 2.9 Сессии

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 38 | `List<ISessionInfo> getSessions(UUID clusterId)` | Получение списка сессий кластера | `GET_SESSIONS_REQUEST` |
| 39 | `List<ISessionInfo> getInfoBaseSessions(UUID clusterId, UUID infoBaseId)` | Получение списка сессий инфобазы | `GET_INFOBASE_SESSIONS_REQUEST` |
| 40 | `ISessionInfo getSessionInfo(UUID clusterId, UUID sessionId)` | Получение информации о сессии | `GET_SESSION_INFO_REQUEST` |
| 41 | `void terminateSession(UUID clusterId, UUID sessionId)` | Отключение сессии | `TERMINATE_SESSION_REQUEST` |
| 42 | `void terminateSession(UUID clusterId, UUID sessionId, String message)` | Отключение сессии с сообщением пользователю | `TERMINATE_SESSION_REQUEST` |

### 2.10 Блокировки

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 43 | `List<IObjectLockInfo> getLocks(UUID clusterId)` | Получение списка блокировок кластера | `GET_LOCKS_REQUEST` |
| 44 | `List<IObjectLockInfo> getInfoBaseLocks(UUID clusterId, UUID infoBaseId)` | Получение списка блокировок инфобазы | `GET_INFOBASE_LOCKS_REQUEST` |
| 45 | `List<IObjectLockInfo> getConnectionLocks(UUID clusterId, UUID connectionId)` | Получение списка блокировок соединения | `GET_CONNECTION_LOCKS_REQUEST` |
| 46 | `List<IObjectLockInfo> getSessionLocks(UUID clusterId, UUID infoBaseId, UUID sessionId)` | Получение списка блокировок сессии | `GET_SESSION_LOCKS_REQUEST` |

### 2.11 Правила распределения

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 47 | `void applyAssignmentRules(UUID clusterId, int sessionId)` | Применение правил распределения к сессии | `APPLY_ASSIGNMENT_RULES_REQUEST` |
| 48 | `List<IAssignmentRuleInfo> getAssignmentRules(UUID clusterId, UUID serverId)` | Получение списка правил распределения сервера | `GET_ASSIGNMENT_RULES_REQUEST` |
| 49 | `UUID regAssignmentRule(UUID clusterId, UUID serverId, IAssignmentRuleInfo rule, int pos)` | Регистрация нового правила распределения | `REG_ASSIGNMENT_RULE_REQUEST` |
| 50 | `void unregAssignmentRule(UUID clusterId, UUID serverId, UUID ruleId)` | Удаление правила распределения | `UNREG_ASSIGNMENT_RULE_REQUEST` |
| 51 | `IAssignmentRuleInfo getAssignmentRuleInfo(UUID clusterId, UUID serverId, UUID ruleId)` | Получение информации о правиле распределения | `GET_ASSIGNMENT_RULE_INFO_REQUEST` |

### 2.12 Безопасность (профили безопасности)

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 52 | `List<ISecurityProfile> getSecurityProfiles(UUID clusterId)` | Получение списка профилей безопасности | `GET_SECURITY_PROFILES_REQUEST` |
| 53 | `void createSecurityProfile(UUID clusterId, ISecurityProfile profile)` | Создание профиля безопасности | `CREATE_SECURITY_PROFILE_REQUEST` |
| 54 | `void dropSecurityProfile(UUID clusterId, String spName)` | Удаление профиля безопасности | `DROP_SECURITY_PROFILE_REQUEST` |
| 55 | `List<ISecurityProfileVirtualDirectory> getSecurityProfileVirtualDirectories(UUID clusterId, String spName)` | Получение виртуальных каталогов профиля | `GET_VIRTUAL_DIRECTORIES_REQUEST` |
| 56 | `void createSecurityProfileVirtualDirectory(UUID clusterId, ISecurityProfileVirtualDirectory dir)` | Создание виртуального каталога | `CREATE_VIRTUAL_DIRECTORY_REQUEST` |
| 57 | `void dropSecurityProfileVirtualDirectory(UUID clusterId, String spName, String alias)` | Удаление виртуального каталога | `DROP_VIRTUAL_DIRECTORY_REQUEST` |
| 58 | `List<ISecurityProfileCOMClass> getSecurityProfileComClasses(UUID clusterId, String spName)` | Получение COM-классов профиля | `GET_COM_CLASSES_REQUEST` |
| 59 | `void createSecurityProfileComClass(UUID clusterId, ISecurityProfileCOMClass comClass)` | Создание COM-класса | `CREATE_COM_CLASS_REQUEST` |
| 60 | `void dropSecurityProfileComClass(UUID clusterId, String spName, String name)` | Удаление COM-класса | `DROP_COM_CLASS_REQUEST` |
| 61 | `List<ISecurityProfileAddIn> getSecurityProfileAddIns(UUID clusterId, String spName)` | Получение списка AddIn профиля | `GET_ALLOWED_ADDINS_REQUEST` |
| 62 | `void createSecurityProfileAddIn(UUID clusterId, ISecurityProfileAddIn addIn)` | Создание AddIn | `CREATE_ALLOWED_ADDIN_REQUEST` |
| 63 | `void dropSecurityProfileAddIn(UUID clusterId, String spName, String name)` | Удаление AddIn | `DROP_ALLOWED_ADDIN_REQUEST` |
| 64 | `List<ISecurityProfileExternalModule> getSecurityProfileUnsafeExternalModules(UUID clusterId, String spName)` | Получение небезопасных внешних модулей | `GET_EXTERNAL_MODULES_REQUEST` |
| 65 | `void createSecurityProfileUnsafeExternalModule(UUID clusterId, ISecurityProfileExternalModule module)` | Создание небезопасного внешнего модуля | `CREATE_EXTERNAL_MODULE_REQUEST` |
| 66 | `void dropSecurityProfileUnsafeExternalModule(UUID clusterId, String spName, String name)` | Удаление небезопасного внешнего модуля | `DROP_EXTERNAL_MODULE_REQUEST` |
| 67 | `List<ISecurityProfileApplication> getSecurityProfileApplications(UUID clusterId, String spName)` | Получение разрешённых приложений | `GET_ALLOWED_APPLICATIONS_REQUEST` |
| 68 | `void createSecurityProfileApplication(UUID clusterId, ISecurityProfileApplication app)` | Создание разрешённого приложения | `CREATE_ALLOWED_APPLICATION_REQUEST` |
| 69 | `void dropSecurityProfileApplication(UUID clusterId, String spName, String name)` | Удаление разрешённого приложения | `DROP_ALLOWED_APPLICATION_REQUEST` |
| 70 | `List<ISecurityProfileInternetResource> getSecurityProfileInternetResources(UUID clusterId, String spName)` | Получение интернет-ресурсов профиля | `GET_INTERNET_RESOURCES_REQUEST` |
| 71 | `void createSecurityProfileInternetResource(UUID clusterId, ISecurityProfileInternetResource res)` | Создание интернет-ресурса | `CREATE_INTERNET_RESOURCE_REQUEST` |
| 72 | `void dropSecurityProfileInternetResource(UUID clusterId, String spName, String name)` | Удаление интернет-ресурса | `DROP_INTERNET_RESOURCE_REQUEST` |

### 2.13 Управление вызовами

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 73 | `void interruptCurrentServerCall(UUID clusterId, UUID sessionId, String message)` | Прерывание текущего серверного вызова | `INTERRUPT_SESSION_CURRENT_SERVER_CALL_REQUEST` |

### 2.14 Потребление ресурсов (счётчики)

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 74 | `List<IResourceConsumptionCounter> getResourceConsumptionCounters(UUID clusterId)` | Получение списка счётчиков потребления ресурсов | `GET_RESOURCE_COUNTERS_REQUEST` |
| 75 | `IResourceConsumptionCounter getResourceConsumptionCounterInfo(UUID clusterId, String name)` | Получение информации о счётчике | `GET_RESOURCE_COUNTER_INFO_REQUEST` |
| 76 | `void regResourceConsumptionCounter(UUID clusterId, IResourceConsumptionCounter counter)` | Регистрация нового счётчика | `REG_RESOURCE_COUNTER_REQUEST` |
| 77 | `void unregResourceConsumptionCounter(UUID clusterId, String name)` | Удаление счётчика | `UNREG_RESOURCE_COUNTER_REQUEST` |

### 2.15 Потребление ресурсов (лимиты)

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 78 | `List<IResourceConsumptionLimit> getResourceConsumptionLimits(UUID clusterId)` | Получение списка лимитов потребления ресурсов | `GET_RESOURCE_LIMITS_REQUEST` |
| 79 | `IResourceConsumptionLimit getResourceConsumptionLimitInfo(UUID clusterId, String name)` | Получение информации о лимите | `GET_RESOURCE_LIMIT_INFO_REQUEST` |
| 80 | `void regResourceConsumptionLimit(UUID clusterId, IResourceConsumptionLimit limit)` | Регистрация нового лимита | `REG_RESOURCE_LIMIT_REQUEST` |
| 81 | `void unregResourceConsumptionLimit(UUID clusterId, String name)` | Удаление лимита | `UNREG_RESOURCE_LIMIT_REQUEST` |

### 2.16 Потребление ресурсов (значения счётчиков)

| # | Сигнатура Java | Описание | Wire-тип запроса |
|---|---------------|----------|-----------------|
| 82 | `List<IResourceConsumptionCounterValue> getResourceConsumptionCounterValues(UUID clusterId, String counterName, String objectName)` | Получение значений счётчика | `GET_COUNTER_VALUES_REQUEST` |
| 83 | `void clearResourceConsumptionCounterAccumulatedValues(UUID clusterId, String counterName, String objectName)` | Очистка накопленных значений | `CLEAR_COUNTER_VALUE_REQUEST` |
| 84 | `List<IResourceConsumptionCounterValue> getResourceConsumptionCounterAccumulatedValues(UUID clusterId, String counterName, String objectName)` | Получение накопленных значений | `GET_COUNTER_ACCUMULATED_VALUES_REQUEST` |

**Итого методов в IAgentAdminConnection: 84**

---

## 3. Модели данных

### 3.1 IClusterInfo / ClusterInfo

Описание модели кластера серверов 1С.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.ClusterInfo`) | Proto-тип |
|-------------|----------|---------------------------------------|-----------|
| `clusterId` | `UUID` | `uuid` | `string` (uuid) |
| `expirationTimeout` | `int` | `expiration_timeout` | `int32` |
| `hostName` | `String` | `host` | `string` |
| `lifeTimeLimit` | `int` | `lifetime_limit` | `int32` |
| `mainPort` | `int` | `port` | `int32` (short) |
| `maxMemorySize` | `int` | `max_memory_size` | `int32` |
| `maxMemoryTimeLimit` | `int` | `max_memory_time_limit` | `int32` |
| `name` | `String` | `name` | `string` |
| `securityLevel` | `int` | `security_level` | `int32` |
| `sessionFaultToleranceLevel` | `int` | `session_fault_tolerance_level` | `int32` |
| `loadBalancingMode` | `int` | `load_balancing_mode` | `int32` |
| `clusterRecyclingErrorsCountThreshold` | `int` | `errors_count_threshold` | `int32` |
| `clusterRecyclingKillProblemProcesses` | `boolean` | `kill_problem_processes` | `bool` |
| `clusterRecyclingKillByMemoryWithDump` | `boolean` | `kill_by_memory_with_dump` | `bool` (v≥9) |

### 3.2 IInfoBaseInfo / InfoBaseInfo

Полная информация об информационной базе.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.InfobaseInfo`) | Proto-тип |
|-------------|----------|----------------------------------------|-----------|
| `infoBaseId` | `UUID` | `uuid` | `string` (uuid) |
| `dateOffset` | `int` | `date_offset` | `int32` |
| `dbms` | `String` | `dbms` | `string` |
| `dbName` | `String` | `db_name` | `string` |
| `dbPassword` | `String` | `db_pwd` | `string` |
| `dbServerName` | `String` | `db_server` | `string` |
| `dbUser` | `String` | `db_user` | `string` |
| `deniedFrom` | `Date` | `denied_from` | `Timestamp` |
| `deniedMessage` | `String` | `denied_message` | `string` |
| `deniedParameter` | `String` | `denied_parameter` | `string` |
| `deniedTo` | `Date` | `denied_to` | `Timestamp` |
| `descr` | `String` | `descr` | `string` |
| `locale` | `String` | `locale` | `string` |
| `name` | `String` | `name` | `string` |
| `permissionCode` | `String` | `permission_code` | `string` |
| `scheduledJobsDenied` | `boolean` | `scheduled_jobs_deny` | `bool` |
| `securityLevel` | `int` (read-only) | `security_level` | `int32` |
| `sessionsDenied` | `boolean` | `sessions_deny` | `bool` |
| `licenseDistributionAllowed` | `int` | `license_distribution` | `int32` |
| `externalSessionManagerConnectionString` | `String` | `external_session_manager_connection_string` | `string` |
| `externalSessionManagerRequired` | `boolean` | `external_session_manager_required` | `bool` |
| `securityProfileName` | `String` | `SecurityProfileName` | `string` |
| `safeModeSecurityProfileName` | `String` | `SafeModeSecurityProfileName` | `string` |
| `reserveWorkingProcesses` | `boolean` | `ReserveWorkingProcesses` | `bool` (v≥9) |

### 3.3 IInfoBaseInfoShort / InfoBaseInfoShort

Краткая информация об информационной базе.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.InfobaseSummaryInfo`) | Proto-тип |
|-------------|----------|------------------------------------------------|-----------|
| `infoBaseId` | `UUID` | `uuid` | `string` (uuid) |
| `descr` | `String` | `descr` | `string` |
| `name` | `String` | `name` | `string` |

### 3.4 ISessionInfo

Информация о сессии (сеансе) работы пользователя.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.SessionInfo`) | Proto-тип |
|-------------|----------|----------------------------------------|-----------|
| `sid` | `UUID` | `uuid` | `string` (uuid) |
| `appId` | `String` | `app_id` | `string` |
| `blockedByDbms` | `int` | `blocked_by_dbms` | `int32` |
| `blockedByLs` | `int` | `blocked_by_ls` | `int32` |
| `bytesAll` | `long` | `bytes_all` | `int64` |
| `bytesLast5Min` | `long` | `bytes_last5min` | `int64` |
| `callsAll` | `int` | `calls_all` | `int32` |
| `callsLast5Min` | `long` | `calls_last5min` | `int64` |
| `connectionId` | `UUID` | `connection_id` | `string` (uuid) |
| `dbmsBytesAll` | `long` | `dbms_bytes_all` | `int64` |
| `dbmsBytesLast5Min` | `long` | `dbms_bytes_last5min` | `int64` |
| `dbProcInfo` | `String` | `db_proc_info` | `string` |
| `dbProcTook` | `int` | `db_proc_took` | `int32` |
| `dbProcTookAt` | `Date` | `db_proc_took_at` | `Timestamp` |
| `durationAll` | `int` | `duration_all` | `int32` |
| `durationAllDbms` | `int` | `duration_all_dbms` | `int32` |
| `durationCurrent` | `int` | `duration_current` | `int32` |
| `durationCurrentDbms` | `int` | `duration_current_dbms` | `int32` |
| `durationLast5Min` | `long` | `duration_last_5_min` | `int64` |
| `durationLast5MinDbms` | `long` | `duration_last_5_min_dbms` | `int64` |
| `host` | `String` | `host` | `string` |
| `infoBaseId` | `UUID` | `infobase_id` | `string` (uuid) |
| `lastActiveAt` | `Date` | `last_active_at` | `Timestamp` |
| `licenses` | `List<ILicenseInfo>` | `licenses` | `repeated LicenseInfo` |
| `locale` | `String` | `locale` | `string` |
| `workingProcessId` | `UUID` | `process_id` | `string` (uuid) |
| `sessionId` | `int` | `id` | `int32` |
| `startedAt` | `Date` | `started_at` | `Timestamp` |
| `userName` | `String` | `user_name` | `string` |
| `hibernate` | `boolean` | `hibernate` | `bool` |
| `passiveSessionHibernateTime` | `int` | `passive_session_hibernate_time` | `int32` |
| `hibernateSessionTerminationTime` | `int` | `hibernate_session_terminate_time` | `int32` |
| `memoryCurrent` | `long` | `memory_current` | `int64` (v≥4) |
| `memoryLast5Min` | `long` | `memory_last5min` | `int64` (v≥4) |
| `memoryTotal` | `long` | `memory_total` | `int64` (v≥4) |
| `readBytesCurrent` | `long` | `read_current` | `int64` (v≥4) |
| `readBytesLast5Min` | `long` | `read_last5min` | `int64` (v≥4) |
| `readBytesTotal` | `long` | `read_total` | `int64` (v≥4) |
| `writeBytesCurrent` | `long` | `write_current` | `int64` (v≥4) |
| `writeBytesLast5Min` | `long` | `write_last5min` | `int64` (v≥4) |
| `writeBytesTotal` | `long` | `write_total` | `int64` (v≥4) |
| `durationCurrentService` | `int` | `duration_current_service` | `int32` (v≥5) |
| `durationLast5MinService` | `long` | `duration_last5min_service` | `int64` (v≥5) |
| `durationAllService` | `int` | `duration_all_service` | `int32` (v≥5) |
| `currentServiceName` | `String` | `current_service_name` | `string` (v≥5) |
| `cpuTimeCurrent` | `long` | `cpu_time_current` | `int64` (v≥6) |
| `cpuTimeLast5Min` | `long` | `cpu_time_last5min` | `int64` (v≥6) |
| `cpuTimeAll` | `long` | `cpu_time_total` | `int64` (v≥6) |
| `dataSeparation` | `String` | `data_separation` | `string` (v≥7) |
| `clientIPAddress` | `String` | `client_ip_address` | `string` (v≥10) |

### 3.5 IWorkingServerInfo / WorkingServerInfo

Информация о рабочем сервере кластера.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.ServerInfo`) | Proto-тип |
|-------------|----------|---------------------------------------|-----------|
| `workingServerId` | `UUID` | `uuid` | `string` (uuid) |
| `hostName` | `String` | `agent_host` | `string` |
| `mainPort` | `int` | `agent_port` | `int32` (short) |
| `name` | `String` (read-only) | `name` | `string` |
| `mainServer` | `boolean` | `main_server` | `bool` |
| `infoBasesPerWorkingProcessLimit` | `int` | `infobases_limit` | `int32` |
| `workingProcessMemoryLimit` | `long` | `memory_limit` | `int64` |
| `connectionsPerWorkingProcessLimit` | `int` | `connections_limit` | `int32` |
| `clusterMainPort` | `int` (read-only) | `cluster_port` | `int32` (short) |
| `dedicatedManagers` | `boolean` | `dedicate_managers` | `bool` |
| `portRanges` | `List<IPortRangeInfo>` | `port_ranges` | `repeated PortRange` |
| `safeWorkingProcessesMemoryLimit` | `long` | `safe_working_processes_memory_limit` | `int64` |
| `safeCallMemoryLimit` | `long` | `safe_call_memory_limit` | `int64` |
| `criticalProcessesTotalMemory` | `long` | `critical_total_memory` | `int64` (v≥8) |
| `temporaryAllowedProcessesTotalMemory` | `long` | `temporary_allowed_total_memory` | `int64` (v≥8) |
| `temporaryAllowedProcessesTotalMemoryTimeLimit` | `long` | `temporary_allowed_total_memory_time_limit` | `int64` (v≥8) |

### 3.6 IWorkingProcessInfo

Информация о рабочем процессе.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.ProcessInfo`) | Proto-тип |
|-------------|----------|----------------------------------------|-----------|
| `workingProcessId` | `UUID` | `uuid` | `string` (uuid) |
| `avgBackCallTime` | `double` | `avg_back_call_time` | `double` |
| `avgCallTime` | `double` | `avg_call_time` | `double` |
| `avgDBCallTime` | `double` | `avg_db_call_time` | `double` |
| `avgLockCallTime` | `double` | `avg_lock_call_time` | `double` |
| `avgServerCallTime` | `double` | `avg_server_call_time` | `double` |
| `avgThreads` | `double` | `avg_threads` | `double` |
| `capacity` | `int` | `capacity` | `int32` |
| `connections` | `int` | `connections` | `int32` |
| `hostName` | `String` | `host` | `string` |
| `enable` | `boolean` | `enable` | `bool` |
| `license` | `List<ILicenseInfo>` | `licenses` | `repeated LicenseInfo` |
| `mainPort` | `int` | `port` | `int32` (short) |
| `memoryExcessTime` | `long` | `memory_excess_time` | `int32` |
| `memorySize` | `int` | `memory_size` | `int32` |
| `pid` | `String` | `pid` | `string` |
| `running` | `int` | `running` | `bool` (int) |
| `selectionSize` | `int` | `selection_size` | `int32` |
| `startedAt` | `Date` | `started_at` | `Timestamp` |
| `use` | `int` | `use` | `bool` (int) |
| `availablePerfomance` | `int` | `available_perfomance` | `int32` |
| `reserve` | `boolean` | `reverse` | `bool` (v≥9) |

### 3.7 ILicenseInfo

Информация о лицензии.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.LicenseInfo`) | Proto-тип |
|-------------|----------|----------------------------------------|-----------|
| `fullName` | `String` | `full_name` | `string` |
| `fullPresentation` | `String` | `full_presentation` | `string` |
| `issuedByServer` | `boolean` | `issued_by_server` | `bool` |
| `licenseType` | `int` | `license_type` | `int32` |
| `maxUsersAll` | `int` | `max_users_all` | `int32` |
| `maxUsersCur` | `int` | `max_users_cur` | `int32` |
| `net` | `boolean` | `net` | `bool` |
| `rmngrAddress` | `String` | `rmngr_address` | `string` |
| `rmngrPid` | `String` | `rmngr_pid` | `string` |
| `rmngrPort` | `int` | `rmngr_port` | `int32` |
| `series` | `String` | `series` | `string` |
| `shortPresentation` | `String` | `short_presentation` | `string` |

### 3.8 IInfoBaseConnectionInfo

Полная информация о соединении инфобазы.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `infoBaseConnectionId` | `UUID` | Уникальный идентификатор соединения |
| `appId` | `String` | Идентификатор приложения |
| `blockedByDbms` | `int` | Заблокировано СУБД |
| `bytesAll` | `long` | Общий объём данных |
| `bytesLast5Min` | `long` | Объём данных за 5 мин |
| `callsAll` | `long` | Общее число вызовов |
| `callsLast5Min` | `long` | Число вызовов за 5 мин |
| `connectedAt` | `Date` | Время подключения |
| `connId` | `int` | Внутренний ID соединения |
| `dbConnMode` | `int` | Режим подключения к БД |
| `dbmsBytesAll` | `long` | Общий объём данных СУБД |
| `dbmsBytesLast5Min` | `long` | Объём данных СУБД за 5 мин |
| `dbProcInfo` | `String` | Информация о процессе БД |
| `dbProcTook` | `int` | Время захвата процесса БД |
| `dbProcTookAt` | `Date` | Время захвата |
| `durationAll` | `int` | Общая длительность |
| `durationAllDbms` | `int` | Общая длительность СУБД |
| `durationCurrent` | `int` | Текущая длительность |
| `durationCurrentDbms` | `int` | Текущая длительность СУБД |
| `durationLast5Min` | `long` | Длительность за 5 мин |
| `durationLast5MinDbms` | `long` | Длительность СУБД за 5 мин |
| `hostName` | `String` | Имя хоста |
| `ibConnMode` | `int` | Режим соединения ИБ |
| `threadMode` | `int` | Режим потока |
| `userName` | `String` | Имя пользователя |
| `memoryCurrent` | `long` | Текущий объём памяти |
| `memoryLast5Min` | `long` | Память за 5 мин |
| `memoryTotal` | `long` | Общий объём памяти |
| `readBytesCurrent` | `long` | Текущий объём чтения |
| `readBytesLast5Min` | `long` | Объём чтения за 5 мин |
| `readBytesTotal` | `long` | Общий объём чтения |
| `writeBytesCurrent` | `long` | Текущий объём записи |
| `writeBytesLast5Min` | `long` | Объём записи за 5 мин |
| `writeBytesTotal` | `long` | Общий объём записи |

### 3.9 IInfoBaseConnectionShort

Краткая информация о соединении.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.ConnectionInfo`) | Proto-тип |
|-------------|----------|------------------------------------------|-----------|
| `infoBaseConnectionId` | `UUID` | `uuid` | `string` (uuid) |
| `application` | `String` | `application` | `string` |
| `blockedByLs` | `int` | `blocked_by_ls` | `int32` |
| `connectedAt` | `Date` | `connected_at` | `Timestamp` |
| `connId` | `int` | `id` | `int32` |
| `host` | `String` | `host` | `string` |
| `infoBaseId` | `UUID` | `infobase_id` | `string` (uuid) |
| `workingProcessId` | `UUID` | `process_id` | `string` (uuid) |
| `sessionNumber` | `int` | `session_id` | `int32` |

### 3.10 IObjectLockInfo / LockInfo

Информация о блокировке объекта.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.LockInfo`) | Proto-тип |
|-------------|----------|-------------------------------------|-----------|
| `connectionId` | `UUID` | `connection_id` | `string` (uuid) |
| `lockDescr` | `String` | `description` | `string` |
| `lockedAt` | `Date` | `locked_at` | `Timestamp` |
| `object` | `UUID` | `object_id` | `string` (uuid) |
| `sid` | `UUID` | `session_id` | `string` (uuid) |

### 3.11 IClusterManagerInfo / ManagerInfo

Информация о менеджере кластера.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.ManagerInfo`) | Proto-тип |
|-------------|----------|----------------------------------------|-----------|
| `clusterManagerId` | `UUID` | `uuid` | `string` (uuid) |
| `descr` | `String` | `descr` | `string` |
| `hostName` | `String` | `host` | `string` |
| `mainManager` | `int` | `main_manager` | `int32` |
| `mainPort` | `int` | `port` | `int32` (short) |
| `pid` | `String` | `pid` | `string` |

### 3.12 IClusterServiceInfo / ServiceInfo

Информация о сервисе кластера.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.ServiceInfo`) | Proto-тип |
|-------------|----------|----------------------------------------|-----------|
| `name` | `String` | `name` | `string` |
| `descr` | `String` | `descr` | `string` |
| `mainOnly` | `int` | `main_only` | `int32` |
| `clusterManagerIds` | `List<UUID>` | `managers` | `repeated string` (uuid) |

### 3.13 IAssignmentRuleInfo / AssignmentInfo

Правило распределения серверных вызовов.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.AssignmentInfo`) | Proto-тип |
|-------------|----------|------------------------------------------|-----------|
| `assignmentRuleId` | `UUID` | `uuid` | `string` (uuid) |
| `objectType` | `String` | `object_type` | `string` |
| `infoBaseName` | `String` | `infobase_name` | `string` |
| `ruleType` | `int` | `type` | `int32` |
| `applicationExt` | `String` | `application_ext` | `string` |
| `priority` | `int` | `priority` | `int32` |

### 3.14 IRegUserInfo / AdminInfo

Информация об администраторе сервера/кластера.

| Поле (Java) | Java-тип | Proto-поле (`ras.messages.v1.AdminInfo`) | Proto-тип |
|-------------|----------|----------------------------------------|-----------|
| `name` | `String` | `Name` | `string` |
| `descr` | `String` | `description` | `string` |
| `password` | `String` | `password` | `string` |
| `passwordAuthAllowed` | `boolean` | `password_auth_allowed` | `bool` |
| `sysAuthAllowed` | `boolean` | `sys_auth_allowed` | `bool` |
| `sysUserName` | `String` | `sys_user_name` | `string` |

### 3.15 IPortRangeInfo / PortRange

Диапазон портов для рабочих процессов.

| Поле (Java) | Java-тип | Proto-поле (`serialize.v1.PortRange`) | Proto-тип |
|-------------|----------|--------------------------------------|-----------|
| `highBound` | `int` | `high` | `int32` (short) |
| `lowBound` | `int` | `low` | `int32` (short) |

### 3.16 IResourceConsumptionCounter

Счётчик потребления ресурсов.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `name` | `String` | Имя счётчика |
| `description` | `String` | Описание |
| `collectionTime` | `long` | Время сбора данных (мс) |
| `group` | `int` | Группа |
| `filterType` | `int` | Тип фильтра |
| `filterValue` | `String` | Значение фильтра |
| `analyzeDuration` | `boolean` | Анализ длительности |
| `analyzeCpuTime` | `boolean` | Анализ времени CPU |
| `analyzeMemory` | `boolean` | Анализ памяти |
| `analyzeReadBytes` | `boolean` | Анализ объёма чтения |
| `analyzeWriteBytes` | `boolean` | Анализ объёма записи |
| `analyzeDurationDbms` | `boolean` | Анализ длительности СУБД |
| `analyzeDbmsBytes` | `boolean` | Анализ объёма данных СУБД |
| `analyzeDurationService` | `boolean` | Анализ длительности сервиса |
| `analyzeCalls` | `boolean` | Анализ количества вызовов |
| `analyzeActiveSessionCount` | `boolean` | Анализ числа активных сессий |
| `analyzeSessionCount` | `boolean` | Анализ числа сессий |

### 3.17 IResourceConsumptionLimit

Лимит потребления ресурсов.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `name` | `String` | Имя лимита |
| `description` | `String` | Описание |
| `counter` | `String` | Имя привязанного счётчика |
| `action` | `int` | Действие при превышении |
| `message` | `String` | Сообщение при превышении |
| `duration` | `long` | Лимит длительности (мс) |
| `cpuTime` | `long` | Лимит времени CPU (мс) |
| `memory` | `long` | Лимит памяти (байт) |
| `readBytes` | `long` | Лимит чтения (байт) |
| `writeBytes` | `long` | Лимит записи (байт) |
| `durationDbms` | `long` | Лимит длительности СУБД (мс) |
| `dbmsBytes` | `long` | Лимит данных СУБД (байт) |
| `durationService` | `long` | Лимит длительности сервиса (мс) |
| `calls` | `long` | Лимит числа вызовов |
| `activeSessionCount` | `long` | Лимит числа активных сессий |
| `sessionCount` | `long` | Лимит числа сессий |

### 3.18 IResourceConsumptionCounterValue

Значение счётчика потребления ресурсов.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `object` | `String` | Идентификатор объекта |
| `collectionTime` | `long` | Время сбора |
| `time` | `Date` | Время измерения |
| `duration` | `long` | Длительность |
| `cpuTime` | `long` | Время CPU |
| `memory` | `long` | Память |
| `readBytes` | `long` | Объём чтения |
| `writeBytes` | `long` | Объём записи |
| `durationDbms` | `long` | Длительность СУБД |
| `dbmsBytes` | `long` | Данные СУБД |
| `durationService` | `long` | Длительность сервиса |
| `calls` | `long` | Число вызовов |
| `activeSessionCount` | `long` | Число активных сессий |
| `sessionCount` | `long` | Число сессий |

### 3.19 ISecurityProfile

Профиль безопасности.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `spName` | `String` | Имя профиля |
| `descr` | `String` | Описание |
| `fileSystemFullAccess` | `boolean` | Полный доступ к файловой системе |
| `comUseFullAccess` | `boolean` | Полный доступ к COM |
| `addInUseFullAccess` | `boolean` | Полный доступ к AddIn |
| `externalAppFullAccess` | `boolean` | Полный доступ к внешним приложениям |
| `internetUseFullAccess` | `boolean` | Полный доступ в интернет |
| `safeModeProfile` | `boolean` | Профиль безопасного режима |
| `privilegedModeInSafeModeAllowed` | `boolean` | Привилежированный режим в безопасном |
| `unsafeExternalModuleFullAccess` | `boolean` | Полный доступ к небезопасным модулям |
| `cryptographyAllowed` | `boolean` | Разрешена криптография |
| `rightExtension` | `boolean` | Расширение прав |
| `rightExtensionDefinitionRoles` | `String` | Роли расширения прав |
| `allModulesExtension` | `boolean` | Расширение всех модулей |
| `modulesAvailableForExtension` | `String` | Модули доступные для расширения |
| `modulesNotAvailableForExtension` | `String` | Модули недоступные для расширения |
| `privilegedModeRoles` | `String` | Роли привилежированного режима |
| `fullPrivilegedMode` | `boolean` | Полный привилежированный режим |

### 3.20 ISecurityProfileVirtualDirectory

Виртуальный каталог профиля безопасности.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `spName` | `String` | Имя профиля безопасности |
| `alias` | `String` | Псевдоним (alias) |
| `physicalPath` | `String` | Физический путь |
| `allowedRead` | `boolean` | Разрешено чтение |
| `allowedWrite` | `boolean` | Разрешена запись |
| `descr` | `String` | Описание |

### 3.21 ISecurityProfileCOMClass

COM-класс профиля безопасности.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `spName` | `String` | Имя профиля безопасности |
| `name` | `String` | Имя COM-класса |
| `fileName` | `String` | Имя файла |
| `objectUuid` | `String` | UUID COM-объекта |
| `computerName` | `String` | Имя компьютера |
| `descr` | `String` | Описание |

### 3.22 ISecurityProfileAddIn

AddIn-компонент профиля безопасности.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `spName` | `String` | Имя профиля безопасности |
| `name` | `String` | Имя AddIn |
| `addInHash` | `String` | Хеш AddIn |
| `descr` | `String` | Описание |

### 3.23 ISecurityProfileExternalModule

Небезопасный внешний модуль профиля безопасности.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `spName` | `String` | Имя профиля безопасности |
| `name` | `String` | Имя модуля |
| `moduleHash` | `String` | Хеш модуля |
| `descr` | `String` | Описание |

### 3.24 ISecurityProfileApplication

Разрешённое приложение профиля безопасности.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `spName` | `String` | Имя профиля безопасности |
| `name` | `String` | Имя приложения |
| `commandWild` | `String` | Маска команды |
| `descr` | `String` | Описание |

### 3.25 ISecurityProfileInternetResource

Интернет-ресурс профиля безопасности.

| Поле (Java) | Java-тип | Описание |
|-------------|----------|----------|
| `spName` | `String` | Имя профиля безопасности |
| `name` | `String` | Имя ресурса |
| `address` | `String` | Адрес |
| `port` | `int` | Порт |
| `protocol` | `String` | Протокол |
| `descr` | `String` | Описание |

---

## 4. Иерархия исключений

```
RuntimeException
├── AgentAdminException                        — базовое исключение библиотеки
│   ├── AgentAdminAuthenticationException      — ошибки аутентификации
│   ├── AgentAdminConnectionException          — ошибки установки соединения
│   └── AgentAdminNotFoundException            — запрашиваемый объект не найден
└── AgentAdminOperationException               — ошибки выполнения операций (final)
```

- **AgentAdminException** (`RuntimeException`) — общий базовый класс. Конструкторы: `()`, `(String)`, `(String, Throwable)`, `(Throwable)`.
- **AgentAdminAuthenticationException** (`final`, extends `AgentAdminException`) — выбрасывается при неверных учётных данных.
- **AgentAdminConnectionException** (extends `AgentAdminException`) — выбрасывается при невозможности установить соединение (ошибки транспорта, неподдерживаемый сервис).
- **AgentAdminNotFoundException** (extends `AgentAdminException`) — выбрасывается при обращении к несуществующему кластеру, инфобазе, сессии и т.д.
- **AgentAdminOperationException** (`final`, extends `RuntimeException`) — выбрасывается при ошибке выполнения операции на сервере (не наследует `AgentAdminException`).

---

## Источники

- Javadoc: `1c_ibis_admin_src/docs/com/_1c/v8/ibis/admin/`
- Java-интерфейсы: `reference/decompiled/java-src/com/_1c/v8/ibis/admin/`
- Java-реализации: `reference/decompiled/java-src/com/_1c/v8/ibis/admin/` (файлы без префикса `I`)
- Фабрики/коннекторы: `reference/decompiled/java-src/com/_1c/v8/ibis/admin/client/`
- Модели данных (proto): `reference/v8platform-protos/serializeapis/v8platform/serialize/v1/*.proto`
- Контракты сообщений (proto): `reference/v8platform-protos/rasapis/ras/messages/v1/*.proto`
