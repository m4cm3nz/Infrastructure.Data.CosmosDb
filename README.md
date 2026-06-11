# Infrastructure.Data.CosmosDb

A .NET 10.0 generic repository base class for Azure Cosmos DB, built on the [Microsoft.Azure.Cosmos](https://www.nuget.org/packages/Microsoft.Azure.Cosmos) SDK v3.

## Installation

```bash
dotnet add package Infrastructure.Data.CosmosDb
```

## Configuration

Add the connection settings to your `appsettings.json`:

```json
{
  "CosmosDb": {
    "Endpoint": "https://<your-account>.documents.azure.com:443/",
    "Key": "<your-primary-key>",
    "DatabaseId": "MyDatabase",
    "CollectionId": "MyCollection",
    "PartitionKey": "/id"
  }
}
```

Register the settings in your application startup:

```csharp
services.Configure<Settings>(configuration.GetSection("CosmosDb"));
```

## Usage

### 1. Create your entity

The entity must expose a string property that maps to the Cosmos DB document `id`. The repository resolves it in this order:

1. A property named `Id`
2. A property named `id`
3. Any property decorated with `[JsonPropertyName("id")]`

The `CosmosClient` serializer must map the chosen property to the JSON field `id`. Using a camelCase naming policy covers options 1 and 2 automatically; `[JsonPropertyName("id")]` covers option 3 regardless of naming policy.

```csharp
// Option 1 — property named Id (most common)
public class Order
{
    public string Id { get; set; }
    public string CustomerId { get; set; }
    public decimal Total { get; set; }
}

// Option 2 — property named id
public class Order
{
    public string id { get; set; }
}

// Option 3 — custom property name with JSON attribute
// With AddCosmosClient() (STJ):
public class Order
{
    [JsonPropertyName("id")]
    public string DocumentId { get; set; }
}

// With AddCosmosClientWithNewtonsoft():
public class Order
{
    [Newtonsoft.Json.JsonProperty("id")]
    public string DocumentId { get; set; }
}
```

### 2. Create your repository

Inherit from `Repository<TEntity>` and inject `CosmosClient` and `IOptions<Settings>`:

```csharp
public class OrderRepository : Repository<Order>
{
    public OrderRepository(CosmosClient client, IOptions<Settings> options)
        : base(client, options) { }
}
```

To override the collection or partition key defined in configuration:

```csharp
public class ProductRepository : Repository<Product>
{
    public ProductRepository(CosmosClient client, IOptions<Settings> options)
        : base(client, options, collectionId: "Products", partitionKey: "/category") { }
}
```

For containers without a partition key, pass an empty string:

```csharp
public class LogRepository : Repository<LogEntry>
{
    public LogRepository(CosmosClient client, IOptions<Settings> options)
        : base(client, options, collectionId: "Logs", partitionKey: "") { }
}
```

### 3. Register and use

Register `CosmosClient` as a singleton using one of the provided extension methods:

```csharp
// Program.cs
services.Configure<Settings>(configuration.GetSection("CosmosDb"));

// Option A — System.Text.Json with camelCase (recommended for new projects)
// Supports [JsonPropertyName] attributes
services.AddCosmosClient();

// Option B — Newtonsoft.Json with camelCase (for existing projects or preference)
// Supports [Newtonsoft.Json.JsonProperty] attributes
services.AddCosmosClientWithNewtonsoft();

// Both accept an optional delegate for additional configuration
services.AddCosmosClient(opt => opt.ConnectionMode = ConnectionMode.Gateway);

services.AddScoped<OrderRepository>();
```

```csharp
// Usage
public class OrderService
{
    private readonly OrderRepository _repository;

    public OrderService(OrderRepository repository) => _repository = repository;

    public Task<Order> GetOrder(string id) => _repository.GetByID(id);

    public Task<IEnumerable<Order>> GetByCustomer(string customerId)
        => _repository.GetAll(o => o.CustomerId == customerId);

    public async Task<string> CreateOrder(Order order)
        => (await _repository.Add(order))?.ToString();

    public Task UpdateOrder(Order order) => _repository.Update(order, order.Id);

    public Task DeleteOrder(string id) => _repository.DeleteBy(id);
}
```

## Overriding behavior

All data access operations are delegated to `protected virtual` methods, making it easy to customize or test without a real Cosmos DB connection:

| Public method | Protected override |
|---|---|
| `GetByID` | `ReadItemInternalAsync` |
| `GetAll()` | `QueryAllItemsInternalAsync` |
| `GetAll(predicate)` | `QueryItemsInternalAsync` |
| `Add` | `CreateItemInternalAsync` |
| `Update` | `ReplaceItemInternalAsync` |
| `DeleteBy(id)` | `DeleteItemInternalAsync` |

```csharp
public class CachedOrderRepository : Repository<Order>
{
    private readonly IMemoryCache _cache;

    public CachedOrderRepository(CosmosClient client, IOptions<Settings> options, IMemoryCache cache)
        : base(client, options) => _cache = cache;

    protected override async Task<Order> ReadItemInternalAsync(string id)
    {
        return await _cache.GetOrCreateAsync(id, _ => base.ReadItemInternalAsync(id));
    }
}
```

### Testing without Cosmos DB

Use the parameterless protected constructor and override the internal methods with an in-memory store:

```csharp
class TestOrderRepository : Repository<Order>
{
    private readonly Dictionary<string, Order> _store = new();

    public TestOrderRepository() : base() { }

    protected override Task<Order> ReadItemInternalAsync(string id)
    {
        _store.TryGetValue(id, out var item);
        return Task.FromResult(item);
    }

    protected override Task<dynamic> CreateItemInternalAsync(Order item)
    {
        if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString();
        _store[item.Id] = item;
        return Task.FromResult<dynamic>(item.Id);
    }

    protected override Task DeleteItemInternalAsync(string id)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }
}
```

## Running integration tests

Integration tests connect to a real Cosmos DB Emulator and are skipped automatically if it is not available — they never block the build.

### Option 1 — Windows native emulator

```bash
winget install Microsoft.Azure.CosmosEmulator
# start via Start Menu or:
& "C:\Program Files\Azure Cosmos DB Emulator\CosmosDB.Emulator.exe"
```

### Option 2 — Docker

```bash
docker compose up -d
```

### Running the tests

```bash
# unit tests (always available, no emulator needed)
dotnet test Infrastructure.Data.CosmosDb.Tests

# integration tests (skipped if emulator is not running)
dotnet test Infrastructure.Data.CosmosDb.IntegrationTests
```

---

## Notes

- The database and container are created automatically on first use if they do not exist.
- Default container throughput is **1000 RU/s**. Override `CreateCollectionIfNotExistsAsync` to customize.
- `Add` generates a GUID id if the entity's id property is null or empty.
- This implementation assumes the **partition key value equals the document id**. Override the protected methods if your partition strategy differs.
- `Add` returns `dynamic` (interface contract) — call `.ToString()` to get the id as a string.
- Entities must expose a string property that resolves to the Cosmos DB document `id` — either named `Id`, `id`, or decorated with `[JsonPropertyName("id")]`. If none is found, `Add` and `DeleteBy(entity)` throw `InvalidOperationException`.
