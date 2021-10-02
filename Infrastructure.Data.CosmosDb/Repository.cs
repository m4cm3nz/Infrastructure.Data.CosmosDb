using Infrastructure.Data.Abstractions;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
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

        private readonly DocumentClient client;

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

            client = new DocumentClient(new Uri(Endpoint), Key);
            client.ConnectionPolicy.RequestTimeout = TimeOut;

            CreateDatabaseIfNotExistsAsync().Wait(TimeOut);
            CreateCollectionIfNotExistsAsync().Wait(TimeOut);
        }

        private async Task CreateDatabaseIfNotExistsAsync()
        {
            try
            {
                await client.ReadDatabaseAsync(UriFactory.CreateDatabaseUri(DatabaseId));
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    await client.CreateDatabaseAsync(new Database { Id = DatabaseId });
                else
                    throw;
            }
        }

        private async Task CreateCollectionIfNotExistsAsync()
        {
            try
            {
                await client.ReadDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId));
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var documentCollection = new DocumentCollection { Id = CollectionId };

                    if (!string.IsNullOrEmpty(partitionKey))
                        documentCollection.PartitionKey.Paths.Add(partitionKey);

                    await client.CreateDocumentCollectionAsync(
                        UriFactory.CreateDatabaseUri(DatabaseId),
                        documentCollection,
                        new RequestOptions { OfferThroughput = 1000 });
                }
                else
                    throw;
            }
        }

        public virtual async Task<TEntity> GetByID(dynamic id)
        {
            try
            {
                var document = await client.ReadDocumentAsync(
                    UriFactory.CreateDocumentUri(
                        DatabaseId,
                        CollectionId,
                        id.ToString()));

                return (TEntity)(dynamic)document.Resource;
            }
            catch (DocumentClientException e)
            {
                if (e.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return default;
                else
                    throw;
            }
        }

        public virtual async Task<IEnumerable<TEntity>> GetAll(Expression<Func<TEntity, bool>> predicate)
        {
            IDocumentQuery<TEntity> query = client.CreateDocumentQuery<TEntity>(
                UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId),
                new FeedOptions { MaxItemCount = -1, EnableCrossPartitionQuery = true })
                .Where(predicate)
                .AsDocumentQuery();

            var results = new List<TEntity>();
            while (query.HasMoreResults)
                results.AddRange(await query.ExecuteNextAsync<TEntity>());

            return results;
        }

        public virtual async Task<dynamic> Add(TEntity item)
        {
            return (await client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(DatabaseId, CollectionId), item)).Resource.Id;
        }

        public virtual async Task Update(TEntity item, dynamic id)
        {
            await client.ReplaceDocumentAsync(UriFactory.CreateDocumentUri(DatabaseId, CollectionId, id.ToString()), item);
        }

        public virtual async Task DeleteBy(dynamic id)
        {
            await client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(DatabaseId, CollectionId, id.ToString()));
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
