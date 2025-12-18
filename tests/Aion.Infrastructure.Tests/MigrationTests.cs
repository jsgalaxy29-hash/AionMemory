using System.Data.Common;
using Aion.Domain;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class MigrationTests
{
    [Fact]
    public async Task Migrations_apply_and_automation_log_is_inserted()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var module = new S_Module { Name = "Test" };
        var entity = new S_EntityType { ModuleId = module.Id, Name = "Item", PluralName = "Items" };
        var rule = new S_AutomationRule
        {
            ModuleId = module.Id,
            Name = "On create",
            Trigger = AutomationTriggerType.OnCreate,
            TriggerFilter = "record.created",
            Actions = new List<AutomationAction>
            {
                new()
                {
                    ActionType = AutomationActionType.SendNotification,
                    ParametersJson = "{}"
                }
            }
        };

        await context.Modules.AddAsync(module);
        await context.EntityTypes.AddAsync(entity);
        await context.AutomationRules.AddAsync(rule);
        await context.SaveChangesAsync();

        var orchestrator = new AutomationOrchestrator(context, NullLogger<AutomationOrchestrator>.Instance);
        var executions = (await orchestrator.TriggerAsync("record.created", new { id = Guid.NewGuid() })).ToList();

        Assert.Single(executions);
        var execution = executions[0];
        Assert.Equal(AutomationExecutionStatus.Succeeded, execution.Status);
        Assert.Equal(rule.Id, execution.RuleId);

        var persisted = await context.AutomationExecutions.FindAsync(execution.Id);
        Assert.NotNull(persisted);
    }

    [Fact]
    public async Task Enum_columns_use_text_storage()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new AionDbContext(options);
        await context.Database.MigrateAsync();

        var module = new S_Module { Name = "Schema check" };
        var rule = new S_AutomationRule
        {
            ModuleId = module.Id,
            Name = "Trigger",
            Trigger = AutomationTriggerType.OnCreate,
            TriggerFilter = "check"
        };

        await context.Modules.AddAsync(module);
        await context.AutomationRules.AddAsync(rule);
        await context.SaveChangesAsync();

        Assert.Equal("TEXT", await GetColumnTypeAsync(connection, "AutomationRules", "Trigger"));
        Assert.Equal("TEXT", await GetColumnTypeAsync(connection, "AutomationActions", "ActionType"));
        Assert.Equal("TEXT", await GetColumnTypeAsync(connection, "Fields", "DataType"));
    }

    private static async Task<string> GetColumnTypeAsync(DbConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return reader.GetString(2);
            }
        }

        return string.Empty;
    }
}
