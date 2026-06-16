# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [10.2.1] - 2026-06-16

### Fixed
- `CreateCollectionIfNotExistsAsync` no longer throws `ArgumentException` when the container already exists with a partition key path different from the one in settings (or from the `/_partitionKey` fallback). The method now probes container existence via a metadata query before attempting creation, so any pre-existing container is accepted as-is regardless of its partition key path. Creation (only when the container is genuinely absent) still uses the atomic, race-safe `CreateContainerIfNotExistsAsync`.
- Partition key path resolution no longer throws `ArgumentException` at startup when a path segment cannot be matched to a C# property on the entity type. Unresolvable paths now fall back silently to `PartitionKey.None`, restoring cross-partition behavior for containers whose configured path does not map directly to the C# model (e.g. containers created with a path that predates a model rename). Paths that do resolve — including via `[JsonPropertyName]` fallback introduced in v10.2.0 — continue to enable precise per-partition operations.

## [10.2.0] - 2026-06-15

### Added
- `GetByID(string id, string partitionKeyValue)` — point-read with explicit partition key value. Use whenever the container has a configured partition key.
- `FindByID(string id, string partitionKeyValue)` — existence check with explicit partition key value.
- `DeleteBy(string id, string partitionKeyValue)` — delete by id with explicit partition key value.
- `ReadItemInternalAsync(string id, PartitionKey partitionKey)` protected virtual overload — primary override point for partition-key-aware reads (e.g. caching). Called by `GetByID(string, string)`.
- `DeleteItemInternalAsync(string id, PartitionKey partitionKey)` protected virtual overload — primary override point for partition-key-aware deletes. Called by `DeleteBy(string, string)`. Override alongside `DeleteItemInternalAsync(TEntity entity, string id)` to apply custom logic (audit, soft-delete) to all delete paths.

### Changed
- `GetByID(dynamic id)`, `FindByID(dynamic id)` and `DeleteBy(dynamic id)` now throw `InvalidOperationException` when a partition key is configured. Use the new two-argument overloads instead.
- Partition key resolution for non-string properties now routes to the correct `PartitionKey` constructor — `bool` uses `PartitionKey(bool)`, numeric types (`int`, `long`, `float`, `double`, `decimal`) use `PartitionKey(double)`. Previously all types were coerced to string via `Convert.ToString`, causing a type mismatch in Cosmos DB for non-string partition keys.
- `GetByID(string, string)`, `FindByID(string, string)` and `DeleteBy(string, string)` throw `ArgumentNullException` immediately when `partitionKeyValue` is null.

### Fixed
- `CreateItemInternalAsync` now returns the locally-held id string directly instead of re-reading it from the Cosmos response via reflection — eliminates one redundant `PropertyInfo.GetValue` call per `Add` operation.

### Breaking changes
- `GetByID(dynamic id)`, `FindByID(dynamic id)` and `DeleteBy(dynamic id)` now throw `InvalidOperationException` for any container with a configured partition key (any non-empty `Settings.PartitionKey`). Switch to `GetByID(id, partitionKeyValue)`, `FindByID(id, partitionKeyValue)` and `DeleteBy(id, partitionKeyValue)`.
- Subclasses that override `ReadItemInternalAsync(string id)` or `DeleteItemInternalAsync(string id)` for custom behavior (caching, audit) should also override the new `(string id, PartitionKey partitionKey)` overloads to apply that behavior to the new two-argument call paths.

## [10.1.0] - 2026-06-12

### Added
- Entity-based partition key resolution — `Add`, `Update` and `DeleteBy(TEntity)` now resolve the partition key value from the entity at runtime via reflection on the configured `Settings.PartitionKey` path. Both slash notation (e.g. `/tenantId`, `/address/city`) and dot notation (e.g. `/address.city`) are supported. Property lookup is case-insensitive. This enables per-environment partition key strategies via `appsettings.json` with no code changes (e.g. no partition key in dev, `/tenantId` in production).
- `DeleteItemInternalAsync(TEntity entity, string id)` protected virtual overload — called by `DeleteBy(TEntity)`. Override alongside `DeleteItemInternalAsync(string id)` if you have custom deletion logic (audit, soft-delete, caching) that must apply to both delete paths.
- Protected constructor `Repository(string partitionKeyPath)` for unit testing — validates the partition key path against the entity type without establishing a Cosmos DB connection.
- `BuildPartitionKeyProperties` validates the configured partition key path against `TEntity` at construction time, throwing `ArgumentException` for invalid paths so misconfiguration is caught at startup rather than at runtime.

### Changed
- Partition key resolution for entity operations is now cached at construction time (`PropertyInfo[]`), matching the existing pattern for `IdProperty`. Reflection cost is paid once per instance rather than on every write operation.
- `GetByID` and `DeleteBy(dynamic id)` continue to use the document id as the partition key value — the only viable fallback when the entity is not available.
- `ReplaceItemAsync` now uses `ConfigureAwait(false)` consistently with all other async Cosmos DB calls.

### Fixed
- Partition key value for `Add`, `Update` and `DeleteBy(TEntity)` was always the document id, causing `400 Bad Request` or silent cross-partition operations when `Settings.PartitionKey` pointed to a field other than `id`.
- `ToString()` on the resolved partition key value now uses `CultureInfo.InvariantCulture`, preventing culture-sensitive serialization differences for non-string partition key types (e.g. `int`, `DateTime`).
- Null partition key value at runtime now throws `InvalidOperationException` with a descriptive message instead of silently sending `PartitionKey.None` to Cosmos DB.

### Breaking changes
- `DeleteBy(TEntity entity)` now dispatches to `DeleteItemInternalAsync(TEntity entity, string id)` instead of `DeleteItemInternalAsync(string id)`. Subclasses that override only the string-id overload for custom deletion logic will not have that override invoked when `DeleteBy(TEntity)` is called. Override the entity overload as well (or in addition) to restore the behavior.

## [10.0.0] - 2026-06-11

### Added
- `CosmosStjSerializer` — `CosmosLinqSerializer` baseado em `System.Text.Json` com camelCase; suporta `[JsonPropertyName]` em entidades e traduz queries LINQ corretamente
- `CosmosServiceExtensions.AddCosmosClient()` — registra `CosmosClient` singleton com `CosmosStjSerializer` (recomendado para projetos novos)
- `CosmosServiceExtensions.AddCosmosClientWithNewtonsoft()` — registra `CosmosClient` singleton com o serializador Newtonsoft.Json built-in do SDK (para projetos existentes)
- Projeto `Infrastructure.Data.CosmosDb.IntegrationTests` com 9 testes de integração contra o Azure Cosmos DB Emulator (auto-skip quando emulador não está disponível)
- `docker-compose.yml` com imagem Linux do emulador para ambiente Docker
- `CosmosClient` injection via constructor — recommended for production (shared connection pool)
- `GetAll()` full implementation returning all documents in the container
- `DeleteBy(TEntity entity)` full implementation resolving the document id via reflection
- Project `Infrastructure.Data.CosmosDb.Tests` with 8 unit tests covering all CRUD operations
- XML documentation on all public and protected members
- `[JsonPropertyName("id")]` support — entities whose id property uses a custom C# name with a JSON attribute are now resolved correctly
- `AzureCosmosDisableNewtonsoftJsonCheck` property — removes unnecessary Newtonsoft.Json dependency

### Changed
- **Breaking:** Construtores que criavam `CosmosClient` internamente foram removidos — `CosmosClient` singleton deve ser injetado via DI; use `AddCosmosClient()` ou `AddCosmosClientWithNewtonsoft()`
- **Breaking:** Migrated from deprecated `Microsoft.Azure.DocumentDB.Core` to `Microsoft.Azure.Cosmos` v3.61.0
- **Breaking:** Target framework updated from `net8.0` to `net10.0`
- `Settings.TimeOut` removed — no longer used; connection timeout is managed by the Cosmos SDK
- Constructor initialization changed from `.Wait()` to `.ConfigureAwait(false).GetAwaiter().GetResult()` with `ConfigureAwait(false)` propagated through all inner awaits — eliminates deadlock risk in synchronization-context environments
- `IdProperty` cached as `static readonly` — reflection lookup performed once per generic type instead of on every `Add` and `DeleteBy(entity)` call
- `GetItemLinqQueryable` called without deprecated `allowSynchronousQueryExecution: true` parameter
- `CreateItemInternalAsync` now throws `InvalidOperationException` when the entity does not expose a resolvable id property, instead of silently returning the full entity object
- `DeleteBy(TEntity entity)` now throws `InvalidOperationException` when the resolved id value is null, instead of passing null to the Cosmos SDK

### Fixed
- `CreateItemInternalAsync` gera GUID se o id da entidade for nulo e passa o partition key explicitamente — corrige `BadRequest 400` e `ArgumentException` ao criar itens
- `DeleteBy(dynamic id)` and `Update` were missing `PartitionKey` in `RequestOptions` — operations on partitioned containers would fail at runtime
- Read, update and delete operations now use `PartitionKey.None` when no partition key is configured (non-partitioned containers), instead of `new PartitionKey(id)` which caused 404 errors
- Package metadata corrected: `Version`, `AssemblyVersion`, `FileVersion`, `Description`, `PackageReleaseNotes`, `RepositoryType`, `NeutralLanguage`, `PackageTags`

## [8.0.0] - 2024-01-01

### Changed
- Target framework updated to `net8.0`
- Dependencies updated to .NET 8 compatible versions

## [6.0.0] - 2022-01-01

### Changed
- Target framework updated to `net6.0`

## [5.0.0] - 2021-01-01

### Changed
- Target framework updated to `net5.0`
