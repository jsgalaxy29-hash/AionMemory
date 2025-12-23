using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class AuthorizationServiceTests
{
    [Fact]
    public async Task Admin_role_allows_all_actions()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var table = await CreateTableAsync(context);
        var userId = Guid.NewGuid();
        context.Roles.Add(Role.Assign(userId, RoleKind.Admin));
        await context.SaveChangesAsync();

        var engine = CreateAuthorizedEngine(context, userId);

        var record = await engine.InsertAsync(table.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Authorized"
        });

        Assert.Equal(table.Id, record.TableId);
    }

    [Fact]
    public async Task Denies_action_without_permission()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var table = await CreateTableAsync(context);
        var userId = Guid.NewGuid();
        context.Permissions.Add(Permission.Grant(userId, PermissionAction.Write, PermissionScope.ForTable(table.Id)));
        await context.SaveChangesAsync();

        var engine = CreateAuthorizedEngine(context, userId);

        var record = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Hello" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.DeleteAsync(table.Id, record.Id));
    }

    private static AuthorizedDataEngine CreateAuthorizedEngine(AionDbContext context, Guid userId)
    {
        var auth = new AuthorizationService(context, NullLogger<AuthorizationService>.Instance);
        var inner = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine());
        var current = new StubCurrentUserService(userId);
        return new AuthorizedDataEngine(inner, auth, current, NullLogger<AuthorizedDataEngine>.Instance);
    }

    private static async Task<STable> CreateTableAsync(AionDbContext context)
    {
        var table = new STable
        {
            Name = "perms",
            DisplayName = "Permissions",
            Fields =
            {
                new SFieldDefinition { Name = "Title", Label = "Title", DataType = FieldDataType.Text, IsRequired = true }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine());
        await engine.CreateTableAsync(table);
        return table;
    }
}

file sealed class StubCurrentUserService : ICurrentUserService
{
    private readonly Guid _userId;

    public StubCurrentUserService(Guid userId)
    {
        _userId = userId;
    }

    public Guid GetCurrentUserId() => _userId;
}

file sealed class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchHit>> SearchAsync(string query, System.Threading.CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

    public Task IndexNoteAsync(S_Note note, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexRecordAsync(F_Record record, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexFileAsync(F_File file, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(string targetType, Guid targetId, System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file sealed class NullAutomationRuleEngine : IAutomationRuleEngine
{
    public Task<IReadOnlyCollection<AutomationExecution>> ExecuteAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<AutomationExecution>>(Array.Empty<AutomationExecution>());
}
