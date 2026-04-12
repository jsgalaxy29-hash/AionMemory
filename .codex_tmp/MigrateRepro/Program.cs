using Aion.Domain;
using Aion.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

var path = Path.Combine(Path.GetTempPath(), $"aion-schema-{Guid.NewGuid():N}.db");
var options = new DbContextOptionsBuilder<AionDbContext>()
    .UseSqlite($"DataSource={path}")
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
    .Options;

await using var context = new AionDbContext(options, new ScratchWorkspaceContext());
var migrator = context.GetService<IMigrator>();
await migrator.MigrateAsync("20251225000000_DashboardLayouts");

await using var connection = new SqliteConnection($"DataSource={path}");
await connection.OpenAsync();

await DumpTableAsync(connection, "Permissions");
await DumpTableAsync(connection, "Tables");
await DumpTableAsync(connection, "TableFields");
await DumpTableAsync(connection, "EntityTypes");
await DumpTableAsync(connection, "SecurityAuditLogs");
await DumpTableAsync(connection, "AiCallLogs");
await DumpTableAsync(connection, "SemanticSearchEntries");
await DumpTableAsync(connection, "SemanticSearch");

static async Task DumpTableAsync(SqliteConnection connection, string table)
{
    Console.WriteLine($"Table {table}:");
    var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({table});";
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        Console.WriteLine($" - {reader.GetString(1)} | {reader.GetString(2)} | notnull={reader.GetInt32(3)}");
    }
}

internal sealed class ScratchWorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
}

