using Infrastructure.Data.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace Infrastructure.Data.CosmosDb
{
    /// <summary>
    /// Abstract base repository for Azure Cosmos DB, implementing <see cref="IRepository{TEntity}"/>.
    /// Handles database and container creation on startup, and delegates all data access
    /// to overridable protected methods for easy customization and unit testing.
    /// </summary>
    /// <typeparam name="TEntity">
    /// The entity type. Must be a class and expose a string property that resolves to the Cosmos DB document id,
    /// in one of these forms (checked in order):
    /// <list type="number">
    ///   <item>A property named <c>Id</c></item>
    ///   <item>A property named <c>id</c></item>
    ///   <item>Any property decorated with <c>[JsonPropertyName("id")]</c></item>
    /// </list>
    /// </typeparam>
    public abstract class Repository<TEntity> : IRepository<TEntity> where TEntity : class
    {
        private readonly string DatabaseId;
        private readonly string CollectionId;
        private readonly string partitionKey;

        private readonly CosmosClient client;
        private readonly Container container;

        private static readonly System.Reflection.PropertyInfo IdProperty =
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
        /// Initializes the repository using settings from <see cref="Settings"/>.
        /// Creates the database and container if they do not exist.
        /// </summary>
        public Repository(IOptions<Settings> options) : this(
            options,
            options.Value.CollectionId,
            options.Value.PartitionKey)
        { }

        /// <summary>
        /// Initializes the repository using settings from <see cref="Settings"/>,
        /// overriding the partition key defined in configuration.
        /// </summary>
        public Repository(IOptions<Settings> options, string partitionKey) : this(
            options,
            options.Value.CollectionId,
            partitionKey)
        { }

        /// <summary>
        /// Initializes the repository using a shared <see cref="CosmosClient"/> instance
        /// and settings from <see cref="Settings"/>. Preferred for production use.
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
        public Repository(CosmosClient cosmosClient, IOptions<Settings> options, string collectionId, string partitionKey)
        {
            DatabaseId = options.Value.DatabaseId;
            CollectionId = collectionId;
            this.partitionKey = partitionKey;
            client = cosmosClient;
            InitializeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            container = client.GetContainer(DatabaseId, CollectionId);
        }

        /// <summary>
        /// Initializes the repository creating an internal <see cref="CosmosClient"/> from
        /// the endpoint and key in <see cref="Settings"/>, overriding both the collection id
        /// and partition key defined in configuration.
        /// </summary>
        public Repository(IOptions<Settings> options, string collectionId, string partitionKey)
        {
            DatabaseId = options.Value.DatabaseId;
            CollectionId = collectionId;
            this.partitionKey = partitionKey;
            client = new CosmosClient(options.Value.Endpoint, options.Value.Key);
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
        /// Assumes the partition key value equals the document id.
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
                var response = await container.ReadItemAsync<TEntity>(id, new PartitionKey(id));
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
        /// Thrown when <typeparamref name="TEntity"/> does not expose an <c>Id</c> or <c>id</c> property.
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
            var response = await container.CreateItemAsync(item).ConfigureAwait(false);
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
            await container.ReplaceItemAsync(item, id, new PartitionKey(id));
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
        /// or when the property value is null.
        /// </exception>
        public virtual async Task DeleteBy(TEntity entity)
        {
            if (IdProperty == null)
                throw new InvalidOperationException($"Type {typeof(TEntity).Name} must expose an Id or id property.");
            var id = IdProperty.GetValue(entity)?.ToString();
            if (id == null)
                throw new InvalidOperationException($"The Id property of {typeof(TEntity).Name} cannot be null.");
            await DeleteItemInternalAsync(id);
        }

        /// <summary>
        /// Core delete operation. Override to customize deletion behavior.
        /// </summary>
        protected virtual async Task DeleteItemInternalAsync(string id)
        {
            await container.DeleteItemAsync<TEntity>(id, new PartitionKey(id));
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
