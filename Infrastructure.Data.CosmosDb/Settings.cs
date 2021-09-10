namespace Infrastructure.Data.CosmosDb
{
    public class Settings
    {
        public string Endpoint { get; set; }
        public string Key { get; set; }
        public string DatabaseId { get; set; }
        public string TimeOut { get; set; }
        public string CollectionId { get; set; }
        public string PartitionKey { get; set; }
    }
}
