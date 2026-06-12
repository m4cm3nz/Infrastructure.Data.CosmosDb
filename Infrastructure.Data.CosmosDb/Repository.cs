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

        // Used by GetByID and DeleteBy(id) — only viable when PK path == id path.
        private PartitionKey ResolvePartitionKey(string id) =>
            partitionKeyProperties == null ? PartitionKey.None : new PartitionKey(id);

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
            return new PartitionKey(Convert.ToString(current, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Resolves and caches the <see cref="PropertyInfo"/> chain for a partition key path.
        /// Supports both slash notation (<c>/Header/VersionCode</c>) and dot notation (<c>/Header.VersionCode</c>).
        /// Property lookup is case-insensitive. Returns <c>null</c> when <paramref name="path"/> is empty.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when any segment of <paramref name="path"/> does not match a public property on the
        /// expected type, allowing misconfigured paths to be caught at startup.
        /// </exception>
        private static PropertyInfo[] BuildPartitionKeyProperties(Type entityType, string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var segments = path.TrimStart('/').Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var props = new PropertyInfo[segments.Length];
            var currentType = entityType;
            for (int i = 0; i < segments.Length; i++)
            {
                var prop = currentType.GetProperty(segments[i],
                    BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                    throw new ArgumentException(
                        $"Property '{segments[i]}' not found on type '{currentType.Name}' " +
                        $"for partition key path '{path}'.",
                        nameof(path));
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
        /// Protected constructor for unit testing with a partition key path. Validates the path
        /// against <typeparamref name="TEntity"/> at construction time but does not establish
        /// a Cosmos DB connection. Override the protected internal methods to provide a test-friendly data store.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="partitionKeyPath"/> contains a segment that does not match
        /// any public property on <typeparamref name="TEntity"/> or its nested types.
        /// </exception>
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
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="partitionKey"/> contains a segment that does not match
        /// any public property on <typeparamref name="TEntity"/> or its nested types.
        /// </exception>
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
            var pk = string.IsNullOrEmpty(partitionKey) ? "/_partitionKey" : partitionKey;
            await database.CreateContainerIfNotExistsAsync(new ContainerProperties(CollectionId, pk), throughput: 1000).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the entity with the given id, or <c>null</c> if not found.
        /// Assumes the partition key value equals the document id — only reliable when
        /// <c>Settings.PartitionKey</c> points to the id field. For other partition key strategies,
        /// use <see cref="GetAll(Expression{Func{TEntity, bool}})"/> to query by the partition key field.
        /// </summary>
        public virtual async Task<TEntity> GetByID(dynamic id)
        {
            return await ReadItemInternalAsync(id.ToString());
        }

        /// <summary>
        /// Core read operation. Override to customize read behavior (e.g. caching).
        /// </summary>
        protected virtual async Task<TEntity> ReadItemInternalAsync(string id)
        {
            try
            {
                var response = await container.ReadItemAsync<TEntity>(id, ResolvePartitionKey(id));
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

            var response = await container.CreateItemAsync(item, ResolvePartitionKey(item)).ConfigureAwait(false);
            return IdProperty.GetValue(response.Resource)?.ToString();
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
        /// </summary>
        public virtual async Task DeleteBy(dynamic id)
        {
            await DeleteItemInternalAsync(id.ToString());
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
        /// Core delete operation by id. Called by <see cref="DeleteBy(dynamic)"/>.
        /// Override to customize deletion behavior for id-based deletes.
        /// </summary>
        protected virtual async Task DeleteItemInternalAsync(string id)
        {
            await container.DeleteItemAsync<TEntity>(id, ResolvePartitionKey(id)).ConfigureAwait(false);
        }

        /// <summary>
        /// Core delete operation using the entity for partition key resolution. Called by <see cref="DeleteBy(TEntity)"/>.
        /// Override to customize deletion behavior for entity-based deletes. If you previously overrode
        /// <see cref="DeleteItemInternalAsync(string)"/> for custom deletion logic (audit, soft-delete, etc.),
        /// override this method as well so that <see cref="DeleteBy(TEntity)"/> also goes through your custom logic.
        /// </summary>
        protected virtual async Task DeleteItemInternalAsync(TEntity entity, string id)
        {
            await container.DeleteItemAsync<TEntity>(id, ResolvePartitionKey(entity)).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns <c>true</c> if an entity with the given id exists.
        /// </summary>
        public virtual async Task<bool> FindByID(dynamic identity)
        {
            return (await GetByID(identity)) != null;
        }
    }
}
