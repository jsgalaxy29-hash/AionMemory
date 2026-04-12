using System.Text.Json;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure;
using Aion.Infrastructure.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

await using var connection = new SqliteConnection("DataSource=:memory:");
await connection.OpenAsync();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddFilter((category, level) => category.Contains("Database.Command") || category.Contains("Update"))
        .AddConsole();
});

var options = new DbContextOptionsBuilder<AionDbContext>()
    .UseSqlite(connection)
    .UseLoggerFactory(loggerFactory)
    .EnableSensitiveDataLogging()
    .EnableDetailedErrors()
    .Options;

await using var context = new AionDbContext(options, new TestWorkspaceContext());
await context.Database.EnsureCreatedAsync();

var validator = new ModuleValidator(context, loggerFactory.CreateLogger<ModuleValidator>());
var applier = new ModuleApplier(
    context,
    validator,
    loggerFactory.CreateLogger<ModuleApplier>(),
    new OperationScopeFactory(),
    new NullSecurityAuditService());

var initialSpec = BuildSimpleSpec();
await applier.ApplyAsync(initialSpec);

var updatedSpec = BuildSimpleSpec();
updatedSpec.Tables[0].Fields.Add(new FieldSpec
{
    Slug = "priority",
    Label = "Priorite",
    DataType = ModuleFieldDataTypes.Number,
    IsFilterable = true,
    IsSortable = true
});
updatedSpec.Tables[0].Views.Add(new ViewSpec
{
    Slug = "form",
    DisplayName = "Formulaire",
    Visualization = "form",
    IsDefault = false
});

try
{
    await applier.ApplyAsync(updatedSpec);
    Console.WriteLine("FIRST UPDATED APPLY OK");
    await applier.ApplyAsync(updatedSpec);
    Console.WriteLine("SECOND UPDATED APPLY OK");
}
catch (DbUpdateConcurrencyException ex)
{
    Console.WriteLine("CONCURRENCY");
    Console.WriteLine(ex);
    foreach (var entry in ex.Entries)
    {
        Console.WriteLine($"ENTRY: {entry.Metadata.Name} state={entry.State}");
        Console.WriteLine(entry.DebugView.LongView);
    }

    foreach (var entry in context.ChangeTracker.Entries())
    {
        Console.WriteLine($"TRACKED: {entry.Metadata.Name} state={entry.State}");
        Console.WriteLine(entry.DebugView.LongView);
    }

    throw;
}

static ModuleSpec BuildSimpleSpec()
    => new()
    {
        Slug = "aion-module",
        Tables =
        {
            new TableSpec
            {
                Slug = "tasks",
                DisplayName = "Taches",
                Description = "Module de taches",
                Fields = new List<FieldSpec>
                {
                    new() { Slug = "title", Label = "Titre", DataType = ModuleFieldDataTypes.Text, IsRequired = true, IsSearchable = true, IsListVisible = true },
                    new() { Slug = "status", Label = "Statut", DataType = ModuleFieldDataTypes.Enum, EnumValues = new List<string> { "todo", "doing", "done" }, IsFilterable = true },
                    new() { Slug = "assignee", Label = "Assigne a", DataType = ModuleFieldDataTypes.Text, IsSearchable = true },
                    new() { Slug = "dueDate", Label = "Echeance", DataType = ModuleFieldDataTypes.Date },
                    new() { Slug = "estimatedHours", Label = "Heures estimees", DataType = ModuleFieldDataTypes.Decimal, MinValue = 0 }
                },
                Views = new List<ViewSpec>
                {
                    new()
                    {
                        Slug = "list",
                        DisplayName = "Liste",
                        Filter = new Dictionary<string, string?> { ["status"] = "todo" },
                        Sort = "dueDate asc",
                        IsDefault = true
                    }
                }
            }
        }
    };

internal sealed class TestWorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
}

public sealed class NullSecurityAuditService : ISecurityAuditService
{
    public void Track(SecurityAuditEvent auditEvent)
    {
    }

    public Task LogAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
