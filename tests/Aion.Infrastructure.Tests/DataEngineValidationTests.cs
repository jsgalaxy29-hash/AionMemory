using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class DataEngineValidationTests
{
    [Fact]
    public async Task InsertAsync_applies_defaults_and_validates_required_fields()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var table = new STable
        {
            Name = "tasks",
            DisplayName = "Tâches",
            Fields = new List<SFieldDefinition>
            {
                new() { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true },
                new() { Name = "Status", Label = "Statut", DataType = FieldDataType.Text, DefaultValue = "todo" }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine(), new CurrentUserService());
        await engine.CreateTableAsync(table);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.InsertAsync(table.Id, "{}"));

        var record = await engine.InsertAsync(table.Id, "{ \"Title\": \"Test\" }");

        Assert.Contains("\"Status\":\"todo\"", record.DataJson);
        Assert.Equal(table.Id, record.EntityTypeId);
    }

    [Fact]
    public async Task QueryAsync_combines_view_filters_with_equals()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var table = new STable
        {
            Name = "notes",
            DisplayName = "Notes",
            Fields =
            {
                new SFieldDefinition { Name = "Title", Label = "Titre", DataType = FieldDataType.Text, IsRequired = true },
                new SFieldDefinition { Name = "Category", Label = "Catégorie", DataType = FieldDataType.Text }
            },
            Views =
            {
                new SViewDefinition
                {
                    Name = "byCategory",
                    QueryDefinition = "{ \"Category\": \"work\" }"
                }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine(), new CurrentUserService());
        await engine.CreateTableAsync(table);

        await engine.InsertAsync(table.Id, "{ \"Title\": \"A\", \"Category\": \"work\" }");
        await engine.InsertAsync(table.Id, "{ \"Title\": \"B\", \"Category\": \"home\" }");

        var filtered = await engine.QueryAsync(table.Id, new QuerySpec { View = "byCategory" });
        Assert.Single(filtered);

        var combined = await engine.QueryAsync(table.Id, new QuerySpec
        {
            View = "byCategory",
            Filters = { new QueryFilter("Category", QueryFilterOperator.Equals, "home") }
        });
        Assert.Empty(combined);

        var byEqualsOnly = await engine.QueryAsync(table.Id, new QuerySpec
        {
            Filters = { new QueryFilter("Category", QueryFilterOperator.Equals, "home") }
        });
        Assert.Single(byEqualsOnly);
    }

    [Fact]
    public async Task NormalizePayload_enforces_types_defaults_and_lookups()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var peopleTable = new STable
        {
            Name = "people",
            DisplayName = "People",
            Fields =
            {
                new SFieldDefinition { Name = "Name", Label = "Name", DataType = FieldDataType.Text, IsRequired = true },
                new SFieldDefinition { Name = "Email", Label = "Email", DataType = FieldDataType.Text, IsRequired = true }
            }
        };

        var projectsTable = new STable
        {
            Name = "projects",
            DisplayName = "Projects",
            Fields =
            {
                new SFieldDefinition { Name = "Title", Label = "Title", DataType = FieldDataType.Text, IsRequired = true },
                new SFieldDefinition { Name = "Status", Label = "Status", DataType = FieldDataType.Text, DefaultValue = "open" },
                new SFieldDefinition { Name = "Priority", Label = "Priority", DataType = FieldDataType.Number },
                new SFieldDefinition { Name = "Budget", Label = "Budget", DataType = FieldDataType.Decimal, MinValue = 0, MaxValue = 100000 },
                new SFieldDefinition { Name = "DueDate", Label = "Due date", DataType = FieldDataType.DateTime },
                new SFieldDefinition { Name = "Owner", Label = "Owner", DataType = FieldDataType.Lookup, LookupTarget = peopleTable.Id.ToString(), LookupField = "Name", IsRequired = true }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine(), new CurrentUserService());

        await engine.CreateTableAsync(peopleTable);
        await engine.CreateTableAsync(projectsTable);

        var owner = await engine.InsertAsync(peopleTable.Id, "{ \"Name\": \"Alice\", \"Email\": \"alice@example.com\" }");

        var dueDate = DateTimeOffset.UtcNow.AddDays(7);
        var project = await engine.InsertAsync(projectsTable.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Migration",
            ["Priority"] = "2",
            ["Budget"] = "1250.50",
            ["DueDate"] = dueDate,
            ["Owner"] = owner.Id.ToString()
        });

        var payload = JsonDocument.Parse(project.DataJson).RootElement;
        Assert.Equal("open", payload.GetProperty("Status").GetString());
        Assert.Equal(2, payload.GetProperty("Priority").GetInt64());
        Assert.Equal(1250.50m, payload.GetProperty("Budget").GetDecimal());
        Assert.Equal(owner.Id, payload.GetProperty("Owner").GetGuid());
        Assert.Equal(dueDate.ToUniversalTime().ToString("O"), payload.GetProperty("DueDate").GetString());

        var resolved = await engine.GetResolvedAsync(projectsTable.Id, project.Id);
        Assert.NotNull(resolved);
        Assert.Equal("Alice", resolved!.Lookups["Owner"].Label);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.InsertAsync(projectsTable.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Invalid",
            ["Owner"] = Guid.NewGuid()
        }));
    }

    [Fact]
    public async Task InsertAsync_batches_lookup_validation_queries()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var interceptor = new LookupCommandCounter();
        var options = new DbContextOptionsBuilder<AionDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var peopleTable = new STable
        {
            Name = "people",
            DisplayName = "People",
            Fields =
            {
                new SFieldDefinition { Name = "Name", Label = "Name", DataType = FieldDataType.Text, IsRequired = true }
            }
        };

        var teamsTable = new STable
        {
            Name = "teams",
            DisplayName = "Teams",
            Fields =
            {
                new SFieldDefinition { Name = "Name", Label = "Name", DataType = FieldDataType.Text, IsRequired = true }
            }
        };

        var assignmentTable = new STable
        {
            Name = "assignments",
            DisplayName = "Assignments",
            Fields =
            {
                new SFieldDefinition { Name = "Title", Label = "Title", DataType = FieldDataType.Text, IsRequired = true },
                new SFieldDefinition { Name = "Owner", Label = "Owner", DataType = FieldDataType.Lookup, LookupTarget = peopleTable.Id.ToString() },
                new SFieldDefinition { Name = "Reviewer", Label = "Reviewer", DataType = FieldDataType.Lookup, LookupTarget = peopleTable.Id.ToString() },
                new SFieldDefinition { Name = "Team", Label = "Team", DataType = FieldDataType.Lookup, LookupTarget = teamsTable.Id.ToString() }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine(), new CurrentUserService());

        await engine.CreateTableAsync(peopleTable);
        await engine.CreateTableAsync(teamsTable);
        await engine.CreateTableAsync(assignmentTable);

        var owner = await engine.InsertAsync(peopleTable.Id, "{ \"Name\": \"Owner\" }");
        var reviewer = await engine.InsertAsync(peopleTable.Id, "{ \"Name\": \"Reviewer\" }");
        var team = await engine.InsertAsync(teamsTable.Id, "{ \"Name\": \"Team A\" }");

        interceptor.Reset();

        await engine.InsertAsync(assignmentTable.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Assignment",
            ["Owner"] = owner.Id,
            ["Reviewer"] = reviewer.Id,
            ["Team"] = team.Id
        });

        await engine.InsertAsync(assignmentTable.Id, new Dictionary<string, object?>
        {
            ["Title"] = "Assignment 2",
            ["Owner"] = owner.Id,
            ["Reviewer"] = reviewer.Id,
            ["Team"] = team.Id
        });

        Assert.Equal(2, interceptor.LookupSelectCount);
    }

    [Fact]
    public async Task QueryAsync_rejects_contains_filters()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AionDbContext>().UseSqlite(connection).Options;

        await using var context = new AionDbContext(options, new TestWorkspaceContext());
        await context.Database.MigrateAsync();

        var table = new STable
        {
            Name = "notes",
            DisplayName = "Notes",
            Fields =
            {
                new SFieldDefinition { Name = "Title", Label = "Title", DataType = FieldDataType.Text, IsRequired = true }
            }
        };

        var engine = new AionDataEngine(context, NullLogger<AionDataEngine>.Instance, new NullSearchService(), new OperationScopeFactory(), new NullAutomationRuleEngine(), new CurrentUserService());
        await engine.CreateTableAsync(table);
        await engine.InsertAsync(table.Id, "{ \"Title\": \"Hello\" }");

        var results = await engine.QueryAsync(table.Id, new QuerySpec
        {
            Filters = { new QueryFilter("Title", QueryFilterOperator.Contains, "Hel") }
        });

        Assert.Single(results);
    }
}

file sealed class NullSearchService : ISearchService
{
    public Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
        => Task.FromResult<IEnumerable<SearchHit>>(Array.Empty<SearchHit>());

    public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

file sealed class LookupCommandCounter : DbCommandInterceptor
{
    public int LookupSelectCount { get; private set; }

    public void Reset()
    {
        LookupSelectCount = 0;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        CountLookupSelect(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        CountLookupSelect(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void CountLookupSelect(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return;
        }

        if (commandText.Contains("FROM \"Records\"", StringComparison.OrdinalIgnoreCase))
        {
            LookupSelectCount += 1;
        }
    }
}

file sealed class NullAutomationRuleEngine : IAutomationRuleEngine
{
    public Task<IReadOnlyCollection<AutomationExecution>> ExecuteAsync(AutomationEvent automationEvent, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyCollection<AutomationExecution>>(Array.Empty<AutomationExecution>());
}
