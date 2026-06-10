# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [10.0.0] - 2026-06-10

### Added
- CosmosClient injection via constructor — recommended for production (shared connection pool)
- `GetAll()` full implementation returning all documents in the container
- `DeleteBy(TEntity entity)` full implementation resolving the document id via reflection
- Project `Infrastructure.Data.CosmosDb.Tests` with 8 unit tests covering all CRUD operations
- XML documentation on all public and protected members
- `[JsonPropertyName("id")]` support — entities whose id property uses a custom C# name with a JSON attribute are now resolved correctly
- `AzureCosmosDisableNewtonsoftJsonCheck` property — removes unnecessary Newtonsoft.Json dependency

### Changed
- **Breaking:** Migrated from deprecated `Microsoft.Azure.DocumentDB.Core` to `Microsoft.Azure.Cosmos` v3.61.0
- **Breaking:** Target framework updated from `net8.0` to `net10.0`
- `Settings.TimeOut` removed — no longer used; connection timeout is managed by the Cosmos SDK
- Constructor initialization changed from `.Wait()` to `.ConfigureAwait(false).GetAwaiter().GetResult()` with `ConfigureAwait(false)` propagated through all inner awaits — eliminates deadlock risk in synchronization-context environments
- `IdProperty` cached as `static readonly` — reflection lookup performed once per generic type instead of on every `Add` and `DeleteBy(entity)` call
- `GetItemLinqQueryable` called without deprecated `allowSynchronousQueryExecution: true` parameter
- `CreateItemInternalAsync` now throws `InvalidOperationException` when the entity does not expose a resolvable id property, instead of silently returning the full entity object
- `DeleteBy(TEntity entity)` now throws `InvalidOperationException` when the resolved id value is null, instead of passing null to the Cosmos SDK

### Fixed
- `DeleteBy(dynamic id)` and `Update` were missing `PartitionKey` in `RequestOptions` — operations on partitioned containers would fail at runtime
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
