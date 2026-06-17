using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using System;
using System.Net.Sockets;
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

        // Tempo máximo de espera pela inicialização do emulador depois que a porta já está aberta.
        // O processo aceita conexões TCP bem antes de o gateway responder ao ReadAccountAsync.
        private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan ReadinessPollInterval = TimeSpan.FromSeconds(3);

        public async Task InitializeAsync()
        {
            // Checagem rápida: se nada escuta na porta do gateway, o emulador não foi iniciado.
            // Falha imediata (skip) em vez de esperar o timeout de readiness — mantém CI sem emulador rápido.
            if (!await IsGatewayListeningAsync())
            {
                IsAvailable = false;
                LastError = $"Nenhum serviço escutando em {Endpoint} — emulador não iniciado. {SkipReason}";
                return;
            }

            try
            {
                Client = CreateClient();
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                CaptureError(ex);
                return;
            }

            // Porta aberta: o emulador pode ainda estar subindo. Faz polling do ReadAccountAsync até ficar pronto.
            var deadline = DateTime.UtcNow + ReadinessTimeout;
            while (true)
            {
                try
                {
                    await Client.ReadAccountAsync();
                    IsAvailable = true;
                    try { await Client.GetDatabase(DatabaseId).DeleteAsync(); } catch { }
                    return;
                }
                catch (Exception ex)
                {
                    CaptureError(ex);
                    if (DateTime.UtcNow >= deadline)
                    {
                        IsAvailable = false;
                        return;
                    }
                    await Task.Delay(ReadinessPollInterval);
                }
            }
        }

        private static async Task<bool> IsGatewayListeningAsync()
        {
            var uri = new Uri(Endpoint);

            System.Net.IPAddress[] addresses;
            try
            {
                // "localhost" resolve para ::1 (IPv6) e 127.0.0.1 (IPv4); o emulador escuta só em IPv4.
                // Tentamos cada endereço para não falhar só porque o IPv6 veio primeiro.
                addresses = await System.Net.Dns.GetHostAddressesAsync(uri.Host);
            }
            catch
            {
                return false;
            }

            foreach (var addr in addresses)
            {
                try
                {
                    using var tcp = new TcpClient(addr.AddressFamily);
                    var connect = tcp.ConnectAsync(addr, uri.Port);
                    if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(2))) != connect)
                        continue; // timeout neste endereço → tenta o próximo

                    await connect; // observa resultado/exceção
                    if (tcp.Connected)
                        return true;
                }
                catch
                {
                    // este endereço recusou a conexão → tenta o próximo
                }
            }

            return false;
        }

        private void CaptureError(Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            var inner = ex.InnerException;
            while (inner != null)
            {
                LastError += $" → {inner.GetType().Name}: {inner.Message}";
                inner = inner.InnerException;
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
