namespace Infrastructure.Data.CosmosDb
{
    /// <summary>
    /// Configuration settings for the Cosmos DB repository.
    /// Bind this class to your configuration section (e.g. <c>appsettings.json</c>).
    /// </summary>
    public class Settings
    {
        /// <summary>The Cosmos DB account endpoint URI.</summary>
        public string Endpoint { get; set; }

        /// <summary>The Cosmos DB account primary or secondary key.</summary>
        public string Key { get; set; }

        /// <summary>The database id. Created automatically if it does not exist.</summary>
        public string DatabaseId { get; set; }

        /// <summary>The default container (collection) id. Created automatically if it does not exist.</summary>
        public string CollectionId { get; set; }

        /// <summary>
        /// The partition key path (e.g. <c>/id</c> or <c>/tenantId</c>).
        /// Defaults to <c>/_partitionKey</c> if not set.
        /// </summary>
        public string PartitionKey { get; set; }
    }
}
