using System.Text.Json;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aion.Infrastructure.Tests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Save_and_restore_layout()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        var workspaceContext = new TestWorkspaceContext();
        await using var context = new AionDbContext(options, workspaceContext);
        await context.Database.MigrateAsync();

        var service = new DashboardService(context);
        var layout = new DashboardLayout
        {
            DashboardKey = "global",
            LayoutJson = JsonSerializer.Serialize(new DashboardLayoutDefinition
            {
                WidgetOrder = new[] { Guid.NewGuid(), Guid.NewGuid() }
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };

        var saved = await service.SaveLayoutAsync(layout);
        var restored = await service.GetLayoutAsync("global");

        Assert.NotNull(restored);
        Assert.Equal(saved.Id, restored!.Id);
        Assert.Equal(saved.LayoutJson, restored.LayoutJson);

        saved.LayoutJson = JsonSerializer.Serialize(new DashboardLayoutDefinition
        {
            WidgetOrder = new[] { Guid.NewGuid() }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        await service.SaveLayoutAsync(saved);
        var updated = await service.GetLayoutAsync("global");

        Assert.Equal(saved.LayoutJson, updated!.LayoutJson);
    }

    private sealed class TestWorkspaceContext : IWorkspaceContext
    {
        public Guid WorkspaceId { get; } = Guid.NewGuid();
    }
}
