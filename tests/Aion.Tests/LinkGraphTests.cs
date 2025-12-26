using Aion.Domain;
using Aion.Infrastructure.Services;
using Aion.Tests.Fixtures;
using Xunit;

namespace Aion.Tests;

public sealed class LinkGraphTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public LinkGraphTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_links_and_traverse_graph()
    {
        var source = Guid.NewGuid();
        var middle = Guid.NewGuid();
        var target = Guid.NewGuid();

        await using var context = _fixture.CreateContext();
        var service = new LinkGraphService(context, new StubCurrentUserService(Guid.NewGuid()));

        await service.CreateLinkAsync(new LinkCreateRequest(source, middle, "manual", "liés manuellement"));
        await service.CreateLinkAsync(new LinkCreateRequest(middle, target, "manual", "enchaînement"));

        var neighbors = await service.GetNeighborsAsync(source, "manual");
        Assert.Single(neighbors);

        var slice = await service.TraverseAsync(source, 2, "manual");
        Assert.Contains(source, slice.Nodes);
        Assert.Contains(middle, slice.Nodes);
        Assert.Contains(target, slice.Nodes);
        Assert.Equal(2, slice.Links.Count);
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        private readonly Guid _userId;

        public StubCurrentUserService(Guid userId)
        {
            _userId = userId;
        }

        public Guid GetCurrentUserId() => _userId;
    }
}
