using Infrastructure.Data.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Infrastructure.Data.CosmosDb
{
    /// <summary>
    /// Abstract base repository for Azure Cosmos DB, implementing <see cref="IRepository{TEntity}"/>.
    /// Requires an injected <see cref="CosmosClient"/> singleton. Handles database and container
    /// creation on startup, and delegates all data access to overridable protected methods
    /// for easy customization and unit testing.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type. Must be a class and expose a string property that resolves to the Cosmos DB
    /// document id, in one of these forms (checked in order):
    /// <list type="number">
    ///   <item>A property named <c>Id</c></item>
    ///   <item>A property named <c>id</c></item>
    ///   <item>Any property decorated with <c>[JsonPropertyName("id")]</c></item>
    /// </list>
    /// The <see cref="CosmosClient"/> must be configured with a serializer that maps the chosen
    /// property to the JSON field <c>id</c> (e.g. camelCase naming policy for <c>Id</c>).
    /// </typeparam>
    public abstract class Repository<TEntity> : IRepository<TEntity> where TEntity : class
    {
        private readonly string DatabaseId;
        private readonly string CollectionId;
        private readonly string partitionKey;
        private readonly PropertyInfo[] partitionKeyProperties;

        private readonly CosmosClient client;
        private readonly Container container;

        private PartitionKey ResolvePartitionKey()
        {
            if (partitionKeyProperties == null)
                return PartitionKey.None;
            throw new InvalidOperationException(
                $"Partition key path '{partitionKey}' requires the key value to be provided explicitly. " +
                $"Use GetByID(id, partitionKeyValue), FindByID(id, partitionKeyValue), or DeleteBy(id, partitionKeyValue).");
        }

        // Used by Add, Update, DeleteBy(entity) — resolves the PK value from the entity via cached reflection.
        private PartitionKey ResolvePartitionKey(TEntity entity)
        {
            if (partitionKeyProperties == null)
                return PartitionKey.None;

            object current = entity;
            foreach (var prop in partitionKeyProperties)
            {
                current = prop.GetValue(current);
                if (current == null)
                    throw new InvalidOperationException(
                        $"Partition key path '{partitionKey}' resolved to null on property '{prop.Name}'. " +
                        $"Ensure the entity is fully populated before calling this operation.");
            }
            return current switch
            {
                string s  => new PartitionKey(s),
                bool b    => new PartitionKey(b),
                double d  => new PartitionKey(d),
                float f   => new PartitionKey((double)f),
                int i     => new PartitionKey((double)i),
                long l    => new PartitionKey((double)l),
                decimal m => new PartitionKey((double)m),
                _         => new PartitionKey(Convert.ToString(current, CultureInfo.InvariantCulture))
            };
        }

        /// <summary>
        /// Resolves and caches the <see cref="PropertyInfo"/> chain for a partition key path.
        /// Supports both slash notation (<c>/Header/VersionCode</c>) and dot notation (<c>/Header.VersionCode</c>).
        /// Each segment is matched against <c>[JsonPropertyName]</c> first (the path is a JSON path), then
        /// against the C# property name case-insensitively. Returns <c>null</c> when <paramref name="path"/>
        /// is empty <em>or</em> when a segment cannot be resolved — in the latter case operations fall back
        /// to <see cref="PartitionKey.None"/> (cross-partition) rather than failing.
        /// </summary>
        private static PropertyInfo[] BuildPartitionKeyProperties(Type entityType, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var segments = path.TrimStart('/').Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var props = new PropertyInfo[segments.Length];
            var currentType = entityType;
            for (int i = 0; i < segments.Length; i++)
            {
                // The path uses JSON names, so [JsonPropertyName] takes precedence over the C# name.
                var prop = currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(p => string.Equals(
                            p.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name,
                            segments[i],
                            StringComparison.OrdinalIgnoreCase))
                    ?? currentType.GetProperty(segments[i],
                        BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                    return null;
                props[i] = prop;
                currentType = prop.PropertyType;
            }
            return props;
        }

        private static readonly PropertyInfo IdProperty =
            typeof(TEntity).GetProperty("Id") ??
            typeof(TEntity).GetProperty("id") ??
            Array.Find(
                typeof(TEntity).GetProperties(),
                p => p.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
                       ?.Name == "id");

        /// <summary>
        /// Protected constructor for testing. Skips Cosmos DB initialization.
        /// Override the protected internal methods to provide a test-friendly data store.
        /// </summary>
        protected Repository() { }

        /// <summary>
        /// Protected constructor for unit testing with a partition key path. Resolves the path
        /// against <typeparamref name="TEntity"/> at construction time but does not establish
        /// a Cosmos DB connection. A path whose segments cannot be resolved falls back to
        /// <see cref="PartitionKey.None"/> (cross-partition). Override the protected internal
        /// methods to provide a test-friendly data store.
        /// </summary>
        protected Repository(string partitionKeyPath)
        {
            partitionKey = partitionKeyPath;
            partitionKeyProperties = BuildPartitionKeyProperties(typeof(TEntity), partitionKeyPath);
        }

        /// <summary>
        /// Initializes the repository using a shared <see cref="CosmosClient"/> instance
        /// and settings from <see cref="Settings"/>.
        /// </summary>
        public Repository(CosmosClient cosmosClient, IOptions<Settings> options) : this(
            cosmosClient,
            options,
            options.Value.CollectionId,
            options.Value.PartitionKey)
        { }

        /// <summary>
        /// Initializes the repository using a shared <see cref="CosmosClient"/> instance,
        /// overriding both the collection id and partition key defined in configuration.
        /// A partition key path whose segments cannot be resolved against <typeparamref name="TEntity"/>
        /// falls back to <see cref="PartitionKey.None"/> (cross-partition).
        /// </summary>
        public Repository(CosmosClient cosmosClient, IOptions<Settings> options, string collectionId, string partitionKey)
        {
            DatabaseId = options.Value.DatabaseId;
            CollectionId = collectionId;
            this.partitionKey = partitionKey;
            partitionKeyProperties = BuildPartitionKeyProperties(typeof(TEntity), partitionKey);
            client = cosmosClient;
            InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            container = client.GetContainer(DatabaseId, CollectionId);
        }

        private async Task InitializeAsync()
        {
            await CreateDatabaseIfNotExistsAsync().ConfigureAwait(false);
            await CreateCollectionIfNotExistsAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the database if it does not exist. Override to customize database creation.
        /// </summary>
        protected virtual async Task CreateDatabaseIfNotExistsAsync()
        {
            await client.CreateDatabaseIfNotExistsAsync(DatabaseId).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates the container if it does not exist, with a default throughput of 1000 RU/s.
        /// Override to customize container properties or throughput.
        /// </summary>
        protected virtual async Task CreateCollectionIfNotExistsAsync()
        {
            var database = client.GetDatabase(DatabaseId);

            // Probe existence via a metadata query so an existing container is reused as-is,
            // regardless of its partition key. A FeedIterator may return an empty page while
            // more results remain, so every page must be drained before concluding absence.
            var iterator = database.GetContainerQueryIterator<ContainerProperties>(
                new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", CollectionId));
            while (iterator.HasMoreResults)
            {
                if ((await iterator.ReadNextAsync().ConfigureAwait(false)).Any())
                    return;
            }

            var pk = string.IsNullOrEmpty(partitionKey) ? "/_partitionKey" : partitionKey;
            await database.CreateContainerIfNotExistsAsync(
                new ContainerProperties(CollectionId, pk), throughput: 1000).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the entity with the given id, or <c>null</c> if not found.
        /// Only valid for containers without a configured partition key (<c>PartitionKey.None</c>).
        /// For any container with a partition key, use <see cref="GetByID(string, string)"/> instead.
        /// </summary>
        public virtual async Task<TEntity> GetByID(dynamic id)
        {
            return await ReadItemInternalAsync(id.ToString());
        }

        /// <summary>
        /// Returns the entity identified by <paramref name="id"/> and <paramref name="partitionKeyValue"/>,
        /// or <c>null</c> if not found. Use this overload for any container with a configured partition key.
        /// </summary>
        public virtual async Task<TEntity> GetByID(string id, string partitionKeyValue)
        {
            ArgumentNullException.ThrowIfNull(partitionKeyValue);
            return await ReadItemInternalAsync(id, new PartitionKey(partitionKeyValue));
        }

        /// <summary>
        /// Core read operation by id only. Valid when no partition key is configured; throws otherwise.
        /// </summary>
        protected virtual async Task<TEntity> ReadItemInternalAsync(string id)
            => await ReadItemInternalAsync(id, ResolvePartitionKey());

        /// <summary>
        /// Core read operation with explicit partition key. Override to customize read behavior (e.g. caching).
        /// </summary>
        protected virtual async Task<TEntity> ReadItemInternalAsync(string id, PartitionKey partitionKey)
        {
            try
            {
                var response = await container.ReadItemAsync<TEntity>(id, partitionKey);
                return response.Resource;
            }
            catch (CosmosException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return default;
                else
                    throw;
            }
        }

        /// <summary>
        /// Returns all entities in the container. Use with caution on large collections.
        /// </summary>
        public virtual async Task<IEnumerable<TEntity>> GetAll()
        {
            return await QueryAllItemsInternalAsync();
        }

        /// <summary>
        /// Core full-scan operation. Override to customize or restrict full collection reads.
        /// </summary>
        protected virtual async Task<IEnumerable<TEntity>> QueryAllItemsInternalAsync()
        {
            var iterator = container.GetItemLinqQueryable<TEntity>().ToFeedIterator();
            var results = new List<TEntity>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response.Resource);
            }
            return results;
        }

        /// <summary>
        /// Returns all entities matching the given predicate.
        /// </summary>
        public virtual async Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> predicate)
        {
            return await QueryItemsInternalAsync(predicate);
        }

        /// <summary>
        /// Core query operation. Override to customize query behavior (e.g. partition targeting).
        /// </summary>
        protected virtual async Task<IEnumerable<TEntity>> QueryItemsInternalAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var queryable = container.GetItemLinqQueryable<TEntity>().Where(predicate);
            var iterator = queryable.ToFeedIterator();
            var results = new List<TEntity>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response.Resource);
            }
            return results;
        }

        /// <summary>
        /// Inserts the entity and returns the generated id as <c>string</c>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <typeparamref name="TEntity"/> does not expose an <c>Id</c> or <c>id</c> property,
        /// or when the partition key path resolves to a null value on the entity.
        /// </exception>
        public virtual async Task<dynamic> Add(TEntity item)
        {
            return await CreateItemInternalAsync(item);
        }

        /// <summary>
        /// Core insert operation. Override to customize creation behavior.
        /// </summary>
        protected virtual async Task<dynamic> CreateItemInternalAsync(TEntity item)
        {
            if (IdProperty == null)
                throw new InvalidOperationException($"Type {typeof(TEntity).Name} must expose an Id or id property.");

            var id = IdProperty.GetValue(item)?.ToString();
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString();
                IdProperty.SetValue(item, id);
            }

            await container.CreateItemAsync(item, ResolvePartitionKey(item)).ConfigureAwait(false);
            return id;
        }

        /// <summary>
        /// Replaces the entity with the given id.
        /// </summary>
        public virtual async Task Update(TEntity item, dynamic id)
        {
            await ReplaceItemInternalAsync(item, id.ToString());
        }

        /// <summary>
        /// Core replace operation. Override to customize update behavior.
        /// </summary>
        protected virtual async Task ReplaceItemInternalAsync(TEntity item, string id)
        {
            await container.ReplaceItemAsync(item, id, ResolvePartitionKey(item)).ConfigureAwait(false);
        }

        /// <summary>
        /// Deletes the entity with the given id.
        /// Only valid for containers without a configured partition key (<c>PartitionKey.None</c>).
        /// For any container with a partition key, use <see cref="DeleteBy(string, string)"/> instead.
        /// </summary>
        public virtual async Task DeleteBy(dynamic id)
        {
            await DeleteItemInternalAsync(id.ToString());
        }

        /// <summary>
        /// Deletes the entity identified by <paramref name="id"/> and <paramref name="partitionKeyValue"/>.
        /// Use this overload for any container with a configured partition key.
        /// </summary>
        public virtual async Task DeleteBy(string id, string partitionKeyValue)
        {
            ArgumentNullException.ThrowIfNull(partitionKeyValue);
            await DeleteItemInternalAsync(id, new PartitionKey(partitionKeyValue));
        }

        /// <summary>
        /// Deletes the given entity. The entity must expose an <c>Id</c> or <c>id</c> property with a non-null value.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <typeparamref name="TEntity"/> does not expose an <c>Id</c> or <c>id</c> property,
        /// when the property value is null, or when the partition key path resolves to a null value on the entity.
        /// </exception>
        public virtual async Task DeleteBy(TEntity entity)
        {
            if (IdProperty == null)
                throw new InvalidOperationException($"Type {typeof(TEntity).Name} must expose an Id or id property.");
            var id = IdProperty.GetValue(entity)?.ToString();
            if (id == null)
                throw new InvalidOperationException($"The Id property of {typeof(TEntity).Name} cannot be null.");
            await DeleteItemInternalAsync(entity, id);
        }

        /// <summary>
        /// Core delete operation by id only. Valid when no partition key is configured; throws otherwise.
        /// </summary>
        protected virtual async Task DeleteItemInternalAsync(string id)
            => await DeleteItemInternalAsync(id, ResolvePartitionKey());

        /// <summary>
        /// Core delete operation with explicit partition key. Called by <see cref="DeleteBy(string, string)"/>.
        /// Override to customize deletion behavior for id-based deletes. If you previously overrode
        /// <see cref="DeleteItemInternalAsync(string)"/> for custom deletion logic (audit, soft-delete, etc.),
        /// override this method as well so that <see cref="DeleteBy(string, string)"/> also goes through your custom logic.
        /// </summary>
        protected virtual async Task DeleteItemInternalAsync(string id, PartitionKey partitionKey)
        {
            await container.DeleteItemAsync<TEntity>(id, partitionKey).ConfigureAwait(false);
        }

        /// <summary>
        /// Core delete operation using the entity for partition key resolution. Called by <see cref="DeleteBy(TEntity)"/>.
        /// Override to customize deletion behavior for entity-based deletes. If you previously overrode
        /// <see cref="DeleteItemInternalAsync(string, PartitionKey)"/> for custom deletion logic (audit, soft-delete, etc.),
        /// override this method as well so that <see cref="DeleteBy(TEntity)"/> also goes through your custom logic.
        /// </summary>
        protected virtual async Task DeleteItemInternalAsync(TEntity entity, string id)
        {
            await container.DeleteItemAsync<TEntity>(id, ResolvePartitionKey(entity)).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns <c>true</c> if an entity with the given id exists.
        /// Only valid for containers without a configured partition key (<c>PartitionKey.None</c>).
        /// For any container with a partition key, use <see cref="FindByID(string, string)"/> instead.
        /// </summary>
        public virtual async Task<bool> FindByID(dynamic identity)
        {
            return (await GetByID(identity)) != null;
        }

        /// <summary>
        /// Returns <c>true</c> if an entity with the given <paramref name="id"/> and
        /// <paramref name="partitionKeyValue"/> exists. Use this overload for any container
        /// with a configured partition key.
        /// </summary>
        public virtual async Task<bool> FindByID(string id, string partitionKeyValue)
        {
            return (await GetByID(id, partitionKeyValue)) != null;
        }
    }
}
