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

        var workspaceContext = new TestWorkspaceContext();
        await using var context = new AionDbContext(options, workspaceContext);
        await context.Database.MigrateAsync();

        var table = await CreateTableAsync(context);
        var userId = Guid.NewGuid();
        context.Roles.Add(Role.Assign(userId, RoleKind.Admin));
        await context.SaveChangesAsync();

        var engine = CreateAuthorizedEngine(context, userId, workspaceContext);

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

        var workspaceContext = new TestWorkspaceContext();
        await using var context = new AionDbContext(options, workspaceContext);
        await context.Database.MigrateAsync();

        var table = await CreateTableAsync(context);
        var userId = Guid.NewGuid();
        context.Permissions.Add(Permission.Grant(userId, PermissionAction.Write, PermissionScope.ForTable(table.Id)));
        await context.SaveChangesAsync();

        var engine = CreateAuthorizedEngine(context, userId, workspaceContext);

        var record = await engine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Hello" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.DeleteAsync(table.Id, record.Id));
    }

    [Fact]
    public async Task Field_permission_allows_write_for_specific_field()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        var workspaceContext = new TestWorkspaceContext();
        await using var context = new AionDbContext(options, workspaceContext);
        await context.Database.MigrateAsync();

        var table = await CreateTableAsync(context);
        var userId = Guid.NewGuid();
        context.Permissions.Add(Permission.Grant(userId, PermissionAction.Write, PermissionScope.ForField(table.Id, "Title")));
        await context.SaveChangesAsync();

        var auditService = new SecurityAuditService(context, new StubCurrentUserService(userId), workspaceContext);
        var authorizationService = new AuthorizationService(context, NullLogger<AuthorizationService>.Instance, auditService);

        var result = await authorizationService.AuthorizeAsync(userId, PermissionAction.Write, PermissionScope.ForField(table.Id, "Title"));

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task Granting_record_permission_allows_access()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        var workspaceContext = new TestWorkspaceContext();
        await using var context = new AionDbContext(options, workspaceContext);
        await context.Database.MigrateAsync();

        var table = await CreateTableAsync(context);
        var grantorId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();

        var dataEngine = new AionDataEngine(
            context,
            NullLogger<AionDataEngine>.Instance,
            new NullSearchService(),
            new OperationScopeFactory(),
            new NullAutomationRuleEngine(),
            new CurrentUserService());

        var record = await dataEngine.InsertAsync(table.Id, new Dictionary<string, object?> { ["Title"] = "Shared" });

        var grantAuditService = new SecurityAuditService(context, new StubCurrentUserService(grantorId), workspaceContext);
        var grantService = new AccessGrantService(context, new StubCurrentUserService(grantorId), grantAuditService);

        var authAuditService = new SecurityAuditService(context, new StubCurrentUserService(targetUserId), workspaceContext);
        var authorizationService = new AuthorizationService(context, NullLogger<AuthorizationService>.Instance, authAuditService);

        var preGrantResult = await authorizationService.AuthorizeAsync(targetUserId, PermissionAction.Read, PermissionScope.ForRecord(table.Id, record.Id));
        Assert.False(preGrantResult.IsAllowed);

        await grantService.GrantRecordAsync(targetUserId, PermissionAction.Read, table.Id, record.Id);

        var postGrantResult = await authorizationService.AuthorizeAsync(targetUserId, PermissionAction.Read, PermissionScope.ForRecord(table.Id, record.Id));
        Assert.True(postGrantResult.IsAllowed);
    }

    private static AuthorizedDataEngine CreateAuthorizedEngine(AionDbContext context, Guid userId, IWorkspaceContext workspaceContext)
    {
        var auditService = new SecurityAuditService(context, new StubCurrentUserService(userId), workspaceContext);
        var auth = new AuthorizationService(context, NullLogger<AuthorizationService>.Instance, auditService);
        var inner = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine(), new CurrentUserService());
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

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine(), new CurrentUserService());
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
