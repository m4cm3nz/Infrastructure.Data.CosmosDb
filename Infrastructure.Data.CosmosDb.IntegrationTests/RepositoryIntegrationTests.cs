using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using System;
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

    /// <summary>Partition key is /tenantId (non-id field) to validate entity-based PK resolution.</summary>
    public class TenantEntity
    {
        public string Id { get; set; }
        public string TenantId { get; set; }
        public string Name { get; set; }
    }

    public class TenantEntityWithNullPk
    {
        public string Id { get; set; }
        public string TenantId { get; set; }  // will be left null to trigger the throw
        public string Name { get; set; }
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

    public class TenantRepository : Repository<TenantEntity>
    {
        public TenantRepository(CosmosClient client, IOptions<Settings> options)
            : base(client, options) { }
    }

    public class TenantNullPkRepository : Repository<TenantEntityWithNullPk>
    {
        public TenantNullPkRepository(CosmosClient client, IOptions<Settings> options)
            : base(client, options) { }
    }

    // --- Tests ---

    public class RepositoryIntegrationTests(CosmosEmulatorFixture fixture)
        : IClassFixture<CosmosEmulatorFixture>
    {
        private OrderRepository CreateOrderRepo() =>
            new(fixture.Client, fixture.CreateOptions("Orders", "/id"));

        private PackageRepository CreatePackageRepo() =>
            new(fixture.Client, fixture.CreateOptions("Packages", ""));

        private TenantRepository CreateTenantRepo() =>
            new(fixture.Client, fixture.CreateOptions("Tenants", "/tenantId"));

        private TenantNullPkRepository CreateTenantNullPkRepo() =>
            new(fixture.Client, fixture.CreateOptions("TenantsNullPk", "/tenantId"));

        [SkippableFact]
        public async Task Add_And_GetById_Should_Work()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Cliente Teste", Total = 99.99m };

            var id = (string)await repo.Add(entity);
            Assert.NotNull(id);

            // OrderRepository uses PK=/id, so the partition key value equals the document id.
            var fetched = await repo.GetByID(id, id);
            Assert.NotNull(fetched);
            Assert.Equal("Cliente Teste", fetched.CustomerName);
        }

        [SkippableFact]
        public async Task Update_Should_Replace_Entity()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Original", Total = 10m };
            var id = (string)await repo.Add(entity);

            entity.CustomerName = "Atualizado";
            await repo.Update(entity, id);

            var fetched = await repo.GetByID(id, id);
            Assert.Equal("Atualizado", fetched.CustomerName);
        }

        [SkippableFact]
        public async Task DeleteBy_Id_Should_Remove()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Para Deletar", Total = 1m };
            var id = (string)await repo.Add(entity);

            await repo.DeleteBy(id, id);

            var fetched = await repo.GetByID(id, id);
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

            var fetched = await repo.GetByID(entity.Id, entity.Id);
            Assert.Null(fetched);
        }

        [SkippableFact]
        public async Task FindByID_Should_Return_True_When_Exists()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateOrderRepo();
            var entity = new OrderEntity { CustomerName = "Existe", Total = 5m };
            var id = (string)await repo.Add(entity);

            Assert.True(await repo.FindByID(id, id));
            Assert.False(await repo.FindByID("nao-existe", "nao-existe"));
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

        // --- Non-id partition key tests ---

        [SkippableFact]
        public async Task NonIdPartitionKey_Add_And_GetAll_Should_Work()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateTenantRepo();
            var tenantId = $"tenant-{Guid.NewGuid():N}";
            var entity = new TenantEntity { TenantId = tenantId, Name = "Empresa A" };

            var id = await repo.Add(entity);
            Assert.NotNull(id);

            var results = await repo.GetAll(x => x.TenantId == tenantId);
            Assert.Single(results);
            Assert.Equal("Empresa A", results.First().Name);
        }

        [SkippableFact]
        public async Task NonIdPartitionKey_GetById_Should_Work()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateTenantRepo();
            var entity = new TenantEntity { TenantId = "tenant-getbyid", Name = "Point Read Test" };
            var id = (string)await repo.Add(entity);

            var fetched = await repo.GetByID(id, "tenant-getbyid");
            Assert.NotNull(fetched);
            Assert.Equal("Point Read Test", fetched.Name);
        }

        [SkippableFact]
        public async Task NonIdPartitionKey_FindById_Should_Work()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateTenantRepo();
            var entity = new TenantEntity { TenantId = "tenant-findbyid", Name = "Exists Test" };
            var id = (string)await repo.Add(entity);

            Assert.True(await repo.FindByID(id, "tenant-findbyid"));
            Assert.False(await repo.FindByID("nao-existe", "tenant-findbyid"));
        }

        [SkippableFact]
        public async Task NonIdPartitionKey_DeleteBy_Id_Should_Remove()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateTenantRepo();
            var entity = new TenantEntity { TenantId = "tenant-deleteid", Name = "Para Deletar" };
            var id = (string)await repo.Add(entity);

            await repo.DeleteBy(id, "tenant-deleteid");

            var fetched = await repo.GetByID(id, "tenant-deleteid");
            Assert.Null(fetched);
        }

        [SkippableFact]
        public async Task NonIdPartitionKey_Update_Should_Replace()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateTenantRepo();
            var entity = new TenantEntity { TenantId = "tenant-b", Name = "Original" };
            var id = await repo.Add(entity);

            entity.Name = "Atualizado";
            await repo.Update(entity, id);

            var idStr = (string)id;
            var results = await repo.GetAll(x => x.Id == idStr);
            Assert.Equal("Atualizado", results.First().Name);
        }

        [SkippableFact]
        public async Task NonIdPartitionKey_DeleteBy_Entity_Should_Remove()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateTenantRepo();
            var entity = new TenantEntity { TenantId = "tenant-c", Name = "Para Deletar" };
            await repo.Add(entity);

            await repo.DeleteBy(entity);

            var results = await repo.GetAll(x => x.Id == entity.Id);
            Assert.Empty(results);
        }

        [SkippableFact]
        public async Task NonIdPartitionKey_NullValue_Should_ThrowInvalidOperationException()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            var repo = CreateTenantNullPkRepo();
            var entity = new TenantEntityWithNullPk { TenantId = null, Name = "sem pk" };

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.Add(entity));
        }

        [SkippableFact]
        public void InvalidPartitionKeyPath_Should_ThrowArgumentException_AtConstruction()
        {
            Skip.IfNot(fixture.IsAvailable, fixture.LastError ?? CosmosEmulatorFixture.SkipReason);

            Assert.Throws<ArgumentException>(() =>
                new OrderRepository(fixture.Client, fixture.CreateOptions("Orders", "/NonExistentProperty")));
        }
    }
}
