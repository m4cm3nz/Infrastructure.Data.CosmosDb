using Infrastructure.Data.CosmosDb;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Infrastructure.Data.CosmosDb.Tests
{
    public class FakeEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    class TestRepository : Repository<FakeEntity>
    {
        private readonly Dictionary<string, FakeEntity> store = new();

        public TestRepository() : base() { }

        protected override Task<FakeEntity> ReadItemInternalAsync(string id)
        {
            store.TryGetValue(id, out var v);
            return Task.FromResult(v);
        }

        protected override Task<IEnumerable<FakeEntity>> QueryItemsInternalAsync(System.Linq.Expressions.Expression<Func<FakeEntity, bool>> predicate)
        {
            // naive evaluation for tests: compile predicate
            var func = predicate.Compile();
            var list = new List<FakeEntity>();
            foreach (var v in store.Values)
                if (func(v)) list.Add(v);
            return Task.FromResult((IEnumerable<FakeEntity>)list);
        }

        protected override Task<dynamic> CreateItemInternalAsync(FakeEntity item)
        {
            if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString();
            store[item.Id] = item;
            return Task.FromResult<object>(item.Id);
        }

        protected override Task ReplaceItemInternalAsync(FakeEntity item, string id)
        {
            store[id] = item;
            return Task.CompletedTask;
        }

        protected override Task DeleteItemInternalAsync(string id)
        {
            store.Remove(id);
            return Task.CompletedTask;
        }
    }

    public class RepositoryTests
    {
        [Fact]
        public async Task Add_And_GetById_Should_Work()
        {
            var repo = new TestRepository();
            var entity = new FakeEntity { Name = "test" };

            var id = await repo.Add(entity);
            Assert.NotNull(id);

            var fetched = await repo.GetByID(id);
            Assert.NotNull(fetched);
            Assert.Equal("test", fetched.Name);
        }

        [Fact]
        public async Task Update_Should_Replace()
        {
            var repo = new TestRepository();
            var entity = new FakeEntity { Name = "test" };
            var id = await repo.Add(entity);

            entity.Name = "updated";
            await repo.Update(entity, id);

            var fetched = await repo.GetByID(id);
            Assert.Equal("updated", fetched.Name);
        }

        [Fact]
        public async Task Delete_Should_Remove()
        {
            var repo = new TestRepository();
            var entity = new FakeEntity { Name = "test" };
            var id = await repo.Add(entity);

            await repo.DeleteBy(id);
            var fetched = await repo.GetByID(id);
            Assert.Null(fetched);
        }

        [Fact]
        public async Task Query_Should_Filter()
        {
            var repo = new TestRepository();
            await repo.Add(new FakeEntity { Name = "a" });
            await repo.Add(new FakeEntity { Name = "b" });

            var results = await repo.GetAll(x => x.Name == "a");
            Assert.Single(results);
        }
    }
}
