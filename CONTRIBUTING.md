# Contributing

Thank you for your interest in contributing to **Infrastructure.Data.CosmosDb**.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Any editor — Visual Studio, Rider, or VS Code with the C# extension
- [Azure Cosmos DB Emulator](https://learn.microsoft.com/azure/cosmos-db/emulator) (only required to run the integration tests)

## Setting up

```bash
git clone https://github.com/m4cm3nz/Infrastructure.Data.CosmosDb.git
cd Infrastructure.Data.CosmosDb
dotnet build
dotnet test
```

The unit tests run without any external dependency. The integration tests target the
Cosmos DB Emulator and skip automatically when it is not running, so a plain
`dotnet test` always passes locally.

## How to contribute

### Reporting a bug

Open an issue using the **Bug report** template. Include a minimal reproduction snippet.

### Proposing a feature

Open an issue using the **Feature request** template **before writing any code**, so the
design can be discussed first.

### Submitting a pull request

1. Fork the repository and create a branch from `master`.
2. Make your changes following the conventions below.
3. Run `dotnet build` and `dotnet test` — both must pass with zero warnings.
4. Open a PR against `master` using the provided template.

---

## Conventions

### Testing

- Every public behavior must have at least one test.
- Unit tests (`Infrastructure.Data.CosmosDb.Tests`) must not depend on a live database —
  mock or override the protected members exposed by the repository base class.
- Integration tests (`Infrastructure.Data.CosmosDb.IntegrationTests`) run against the
  Cosmos DB Emulator and must skip cleanly when it is unavailable.
- Use xUnit. Follow the naming pattern in existing test files.

### Commit messages

Use the conventional commits format:

```
feat: add explicit partition key to GetById
fix: reuse pre-existing container regardless of partition key path
docs: update README with partition key examples
chore: bump version to 10.2.1
perf: cache partition key resolution via reflection
```

### Comments

Write no comments unless the *why* is non-obvious — a hidden constraint, an SDK edge case,
or a workaround for a specific bug. Do not describe what the code does.

---

## Branch and release model

- `master` is the stable branch. All PRs target `master`.
- Versions follow [Semantic Versioning](https://semver.org). Breaking changes increment the major version.
- A NuGet package is published automatically when a `v*.*.*` tag is pushed to `master`.
