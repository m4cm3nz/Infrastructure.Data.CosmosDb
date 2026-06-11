using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

namespace Infrastructure.Data.CosmosDb.IntegrationTests
{
    // --- Entities ---

    public class OrderEntity
    {
        public string Id { get; set; }
        public string CustomerName { get; set; }
        public decimal Total { get; set; }
    }

    public class PackageEntity
    {
        [JsonPropertyName("id")]
        public string DocumentId { get; set; }
        public string Content { get; set; }
    }

    // --- Repositories ---

    public class OrderRepository : Repository<OrderEntity>
    {
        public OrderRepository(CosmosClient client, IOptions<Settings> options)
            : base(client, options) { }
    }

    public class PackageRepository : Repository<PackageEntity>
    {
        public PackageRepository(CosmosClient client, IOptions<Settings> options)
            : base(client, options, "Packages", "") { }
    }

    // --- Tests ---

    public class RepositoryIntegrationTests(CosmosEmulatorFixture fixture)
        : IClassFixture<CosmosEmulatorFixture>
    {
        private OrderRepository CreateOrderRepo() =>
            new(fixture.Client, fixture.CreateOptions("Orders", "/id"));

        private PackageRepository CreatePackageRepo() =>
            new(fixture.Client, fixture.CreateOptions("Packages", ""));

        [SkippableFact]
        public async Task Add_And_GetById_Should_Work()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Cliente Teste", Total = 99.99m };

            var id = await repo.Add(entity);
            Assert.NotNull(id);

            var fetched = await repo.GetByID(id);
            Assert.NotNull(fetched);
            Assert.Equal("Cliente Teste", fetched.CustomerName);
        }

        [SkippableFact]
        public async Task Update_Should_Replace_Entity()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Original", Total = 10m };
            var id = await repo.Add(entity);

            entity.CustomerName = "Atualizado";
            await repo.Update(entity, id);

            var fetched = await repo.GetByID(id);
            Assert.Equal("Atualizado", fetched.CustomerName);
        }

        [SkippableFact]
        public async Task DeleteBy_Id_Should_Remove()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Para Deletar", Total = 1m };
            var id = await repo.Add(entity);

            await repo.DeleteBy(id);

            var fetched = await repo.GetByID(id);
            Assert.Null(fetched);
        }

        [SkippableFact]
        public async Task DeleteBy_Entity_Should_Remove()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Para Deletar", Total = 1m };
            await repo.Add(entity);

            await repo.DeleteBy(entity);

            var fetched = await repo.GetByID(entity.Id);
            Assert.Null(fetched);
        }

        [SkippableFact]
        public async Task FindByID_Should_Return_True_When_Exists()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Existe", Total = 5m };
            var id = await repo.Add(entity);

            Assert.True(await repo.FindByID(id));
            Assert.False(await repo.FindByID("nao-existe"));
        }

        [SkippableFact]
        public async Task GetAll_Should_Return_All_Items()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            await repo.Add(new OrderEntity { CustomerName = "A", Total = 1m });
            await repo.Add(new OrderEntity { CustomerName = "B", Total = 2m });

            var results = await repo.GetAll();
            Assert.True(results.Count() >= 2);
        }

        [SkippableFact]
        public async Task GetAll_With_Predicate_Should_Filter()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            await repo.Add(new OrderEntity { CustomerName = "Filtrar", Total = 50m });
            await repo.Add(new OrderEntity { CustomerName = "Ignorar", Total = 50m });

            var results = await repo.GetAll(x => x.CustomerName == "Filtrar");
            Assert.Single(results);
        }

        [SkippableFact]
        public async Task Entity_With_JsonPropertyName_Should_Work()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreatePackageRepo();
            var entity = new PackageEntity { Content = "conteudo teste" };

            var id = await repo.Add(entity);
            Assert.NotNull(id);

            var fetched = await repo.GetByID(id);
            Assert.NotNull(fetched);
            Assert.Equal("conteudo teste", fetched.Content);
        }

        [SkippableFact]
        public async Task Entity_With_JsonPropertyName_DeleteBy_Entity_Should_Work()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreatePackageRepo();
            var entity = new PackageEntity { Content = "para deletar" };
            await repo.Add(entity);

            await repo.DeleteBy(entity);

            var fetched = await repo.GetByID(entity.DocumentId);
            Assert.Null(fetched);
        }
    }
}
