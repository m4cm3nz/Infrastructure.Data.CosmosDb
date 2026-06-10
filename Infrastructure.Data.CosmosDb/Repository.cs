using Infrastructure.Data.Abstractions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Infrastructure.Data.CosmosDb
{
    public abstract class Repository<TEntity> : IRepository<TEntity> where TEntity : class
    {
        private readonly string Endpoint;
        private readonly string Key;
        private readonly string DatabaseId;
        private readonly string CollectionId;
        private readonly string partitionKey;
        private readonly TimeSpan TimeOut;

        private readonly CosmosClient client;
        private readonly Container container;

        protected Repository() { }

        public Repository(IOptions<Settings> options) : this(
            options,
            options.Value.CollectionId,
            options.Value.PartitionKey)
        { }

        public Repository(IOptions<Settings> options, string partitionKey) : this(
            options,
            options.Value.CollectionId,
            partitionKey)
        { }

        public Repository(CosmosClient cosmosClient, IOptions<Settings> options) : this(
            cosmosClient,
            options,
            options.Value.CollectionId,
            options.Value.PartitionKey)
        { }

        public Repository(CosmosClient cosmosClient, IOptions<Settings> options, string collectionId, string partitionKey)
        {
            Endpoint = options.Value.Endpoint;
            Key = options.Value.Key;
            DatabaseId = options.Value.DatabaseId;
            CollectionId = collectionId;
            this.partitionKey = partitionKey;
            TimeOut = TimeSpan.FromMilliseconds(
                double.TryParse(options.Value.TimeOut,
                out double timeOut) ? timeOut : 3);

            client = cosmosClient;

            CreateDatabaseIfNotExistsAsync().Wait(TimeOut);
            CreateCollectionIfNotExistsAsync().Wait(TimeOut);

            container = client.GetContainer(DatabaseId, CollectionId);
        }

        public Repository(IOptions<Settings> options, string collectionId, string partitionKey)
        {
            Endpoint = options.Value.Endpoint;
            Key = options.Value.Key;
            DatabaseId = options.Value.DatabaseId;
            CollectionId = collectionId;
            this.partitionKey = partitionKey;
            TimeOut = TimeSpan.FromMilliseconds(
                double.TryParse(options.Value.TimeOut,
                out double timeOut) ? timeOut : 3);

            client = new CosmosClient(Endpoint, Key);

            CreateDatabaseIfNotExistsAsync().Wait(TimeOut);
            CreateCollectionIfNotExistsAsync().Wait(TimeOut);

            container = client.GetContainer(DatabaseId, CollectionId);
        }

        protected virtual async Task CreateDatabaseIfNotExistsAsync()
        {
            await client.CreateDatabaseIfNotExistsAsync(DatabaseId);
        }

        protected virtual async Task CreateCollectionIfNotExistsAsync()
        {
            var database = client.GetDatabase(DatabaseId);
            var pk = string.IsNullOrEmpty(partitionKey) ? "/_partitionKey" : partitionKey;
            var containerProperties = new ContainerProperties(CollectionId, pk);
            await database.CreateContainerIfNotExistsAsync(containerProperties, throughput: 1000);
        }

        public virtual async Task<TEntity> GetByID(dynamic id)
        {
            return await ReadItemInternalAsync(id.ToString());
        }

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

        public virtual async Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> predicate)
        {
            return await QueryItemsInternalAsync(predicate);
        }

        protected virtual async Task<IEnumerable<TEntity>> QueryItemsInternalAsync(Expression<Func<TEntity, bool>> predicate)
        {
            var queryable = container.GetItemLinqQueryable<TEntity>(true).Where(predicate);
            var iterator = queryable.ToFeedIterator();

            var results = new List<TEntity>();
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                results.AddRange(response.Resource);
            }

            return results;
        }

        public virtual async Task<dynamic> Add(TEntity item)
        {
            return await CreateItemInternalAsync(item);
        }

        protected virtual async Task<dynamic> CreateItemInternalAsync(TEntity item)
        {
            var response = await container.CreateItemAsync(item);
            var resource = response.Resource;
            var idProp = resource.GetType().GetProperty("id") ?? resource.GetType().GetProperty("Id");
            return idProp != null ? idProp.GetValue(resource) : (dynamic)resource;
        }

        public virtual async Task Update(TEntity item, dynamic id)
        {
            await ReplaceItemInternalAsync(item, id.ToString());
        }

        protected virtual async Task ReplaceItemInternalAsync(TEntity item, string id)
        {
            await container.ReplaceItemAsync(item, id, new PartitionKey(id));
        }

        public virtual async Task DeleteBy(dynamic id)
        {
            await DeleteItemInternalAsync(id.ToString());
        }

        protected virtual async Task DeleteItemInternalAsync(string id)
        {
            await container.DeleteItemAsync<TEntity>(id, new PartitionKey(id));
        }

        public virtual async Task<bool> FindByID(dynamic identity)
        {
            return (await GetByID(identity)) != null;
        }

        public virtual Task<IEnumerable<TEntity>> GetAll()
        {
            throw new NotImplementedException();
        }

        public virtual Task DeleteBy(TEntity entity)
        {
            throw new NotImplementedException();
        }
    }
}
