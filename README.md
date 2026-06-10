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
public class Order
{
    [JsonPropertyName("id")]
    public string DocumentId { get; set; }
}
```

### 2. Create your repository

Inherit from `Repository<TEntity>` and inject `IOptions<Settings>`:

```csharp
public class OrderRepository : Repository<Order>
{
    public OrderRepository(IOptions<Settings> options) : base(options) { }
}
```

### 3. Register and use

```csharp
// Startup / Program.cs
services.Configure<Settings>(configuration.GetSection("CosmosDb"));
services.AddScoped<OrderRepository>();

// Usage
public class OrderService
{
    private readonly OrderRepository _repository;

    public OrderService(OrderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Order> GetOrder(string id)
        => await _repository.GetByID(id);

    public async Task<IEnumerable<Order>> GetByCustomer(string customerId)
        => await _repository.GetAll(o => o.CustomerId == customerId);

    public async Task<string> CreateOrder(Order order)
        => (await _repository.Add(order))?.ToString(); // Add returns dynamic; cast to string

    public async Task UpdateOrder(Order order)
        => await _repository.Update(order, order.Id);

    public async Task DeleteOrder(string id)
        => await _repository.DeleteBy(id);
}
```

## Injecting a shared CosmosClient

For scenarios where you want to share a single `CosmosClient` instance across repositories (recommended for production), inject it directly:

```csharp
// Startup / Program.cs
services.AddSingleton(new CosmosClient(endpoint, key));
services.Configure<Settings>(configuration.GetSection("CosmosDb"));
services.AddScoped<OrderRepository>();

// Repository
public class OrderRepository : Repository<Order>
{
    public OrderRepository(CosmosClient client, IOptions<Settings> options)
        : base(client, options) { }
}
```

## Using multiple containers

Override the collection and partition key per repository:

```csharp
public class ProductRepository : Repository<Product>
{
    public ProductRepository(IOptions<Settings> options)
        : base(options, collectionId: "Products", partitionKey: "/category") { }
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

## Notes

- The database and container are created automatically on first use if they do not exist.
- Default container throughput is **1000 RU/s**. Override `CreateCollectionIfNotExistsAsync` to customize.
- This implementation assumes the **partition key value equals the document id**. Override the protected methods if your partition strategy differs.
- `Add` returns `dynamic` (interface contract) — call `.ToString()` to get the id as a string.
- Entities must expose a string property that resolves to the Cosmos DB document `id` — either named `Id`, `id`, or decorated with `[JsonPropertyName("id")]`. If none is found, `Add` and `DeleteBy(entity)` throw `InvalidOperationException`.
