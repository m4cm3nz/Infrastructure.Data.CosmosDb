using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Infrastructure.Data.CosmosDb.IntegrationTests
{
    public class CosmosEmulatorFixture : IAsyncLifetime
    {
        public const string Endpoint = "https://localhost:8081";
        public const string Key = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        public const string DatabaseId = "IntegrationTests";

        public const string SkipReason =
            "Cosmos DB Emulator não está disponível. " +
            "Windows: 'winget install Microsoft.Azure.CosmosEmulator' | Docker: 'docker compose up -d'";

        public CosmosClient Client { get; private set; }
        public bool IsAvailable { get; private set; }

        public string LastError { get; private set; }

        public async Task InitializeAsync()
        {
            try
            {
                Client = CreateClient();
                await Client.ReadAccountAsync();
                IsAvailable = true;
                try { await Client.GetDatabase(DatabaseId).DeleteAsync(); } catch { }
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                LastError = $"{ex.GetType().Name}: {ex.Message}";
                var inner = ex.InnerException;
                while (inner != null)
                {
                    LastError += $" → {inner.GetType().Name}: {inner.Message}";
                    inner = inner.InnerException;
                }
            }
        }

        public async Task DisposeAsync()
        {
            if (IsAvailable && Client != null)
            {
                try { await Client.GetDatabase(DatabaseId).DeleteAsync(); } catch { }
                Client.Dispose();
            }
        }

        public static CosmosClient CreateClient() =>
            new CosmosClient(Endpoint, Key, new CosmosClientOptions
            {
                ConnectionMode = ConnectionMode.Gateway,
                Serializer = new CosmosStjSerializer()
            });

        public IOptions<Settings> CreateOptions(string collectionId, string partitionKey = "/id") =>
            Options.Create(new Settings
            {
                Endpoint = Endpoint,
                Key = Key,
                DatabaseId = DatabaseId,
                CollectionId = collectionId,
                PartitionKey = partitionKey
            });
    }
}
