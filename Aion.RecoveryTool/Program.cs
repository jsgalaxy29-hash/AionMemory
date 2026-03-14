using System.Globalization;
using Aion.Domain;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Aion Recovery Tool");
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/Aion.RecoveryTool -- check --connection <connectionString> --key <encryptionKey>");
    Console.WriteLine("  dotnet run --project src/Aion.RecoveryTool -- export --connection <connectionString> --key <encryptionKey> --output <path>");
    Console.WriteLine("  dotnet run --project src/Aion.RecoveryTool -- rebuild-search --connection <connectionString> --key <encryptionKey>");
    return 1;
}

var command = args[0].ToLowerInvariant();
var connectionString = GetOption(args, "--connection") ?? string.Empty;
var encryptionKey = GetOption(args, "--key") ?? Environment.GetEnvironmentVariable("AION_DB_KEY") ?? string.Empty;
var outputPath = GetOption(args, "--output") ?? string.Empty;

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing required --connection.");
    return 2;
}

if (string.IsNullOrWhiteSpace(encryptionKey))
{
    Console.Error.WriteLine("Missing required --key or AION_DB_KEY.");
    return 2;
}

try
{
    return command switch
    {
        "check" => await RunCheckAsync(connectionString, encryptionKey),
        "export" => await RunExportAsync(connectionString, encryptionKey, outputPath),
        "rebuild-search" => await RunRebuildSearchAsync(connectionString, encryptionKey),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Recovery tool failed: {ex.Message}"));
    return 3;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command '{command}'. Use 'check' or 'export'.");
    return 1;
}

static string? GetOption(string[] args, string name)
{
    var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    if (index < 0 || index >= args.Length - 1)
    {
        return null;
    }

    return args[index + 1];
}

static async Task<int> RunCheckAsync(string connectionString, string encryptionKey)
{
    await using var source = CreateSourceConnection(connectionString);
    await source.OpenAsync();
    ApplyEncryptionPragmas(source, encryptionKey);

    var report = await DatabaseIntegrityVerifier.VerifyAsync(source).ConfigureAwait(false);
    if (report.IsValid)
    {
        Console.WriteLine("Integrity check: OK");
        return 0;
    }

    Console.Error.WriteLine($"Integrity check failed: {string.Join(" | ", report.Issues)}");
    return 4;
}

static async Task<int> RunExportAsync(string connectionString, string encryptionKey, string outputPath)
{
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        Console.Error.WriteLine("Missing required --output for export.");
        return 2;
    }

    var destinationPath = Path.GetFullPath(outputPath);
    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");

    await using var source = CreateSourceConnection(connectionString);
    await using var destination = CreateDestinationConnection(destinationPath);

    await source.OpenAsync();
    await destination.OpenAsync();
    ApplyEncryptionPragmas(source, encryptionKey);
    ApplyEncryptionPragmas(destination, encryptionKey);

    source.BackupDatabase(destination);

    Console.WriteLine($"Export completed: {destinationPath}");
    return 0;
}

static async Task<int> RunRebuildSearchAsync(string connectionString, string encryptionKey)
{
    var options = Options.Create(new AionDatabaseOptions
    {
        ConnectionString = connectionString,
        EncryptionKey = encryptionKey
    });

    var builder = new DbContextOptionsBuilder<AionDbContext>();
    SqliteConnectionFactory.ConfigureBuilder(builder, options);

    await using var context = new AionDbContext(builder.Options, new RecoveryWorkspaceContext());
    await context.Database.MigrateAsync();

    var indexService = new RecordSearchIndexService(context, NullLogger<RecordSearchIndexService>.Instance);
    await indexService.RebuildAsync();

    Console.WriteLine("Record search index rebuild completed.");
    return 0;
}

static SqliteConnection CreateSourceConnection(string connectionString)
{
    var builder = new SqliteConnectionStringBuilder(connectionString)
    {
        Mode = SqliteOpenMode.ReadOnly,
        Cache = SqliteCacheMode.Private
    };
    builder.Remove("Password");
    builder.Remove("Pwd");

    return new SqliteConnection(builder.ToString());
}

sealed class RecoveryWorkspaceContext : IWorkspaceContext
{
    public Guid WorkspaceId { get; } = TenancyDefaults.DefaultWorkspaceId;
}

static SqliteConnection CreateDestinationConnection(string destinationPath)
{
    var builder = new SqliteConnectionStringBuilder
    {
        DataSource = destinationPath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Private
    };

    return new SqliteConnection(builder.ToString());
}

static void ApplyEncryptionPragmas(SqliteConnection connection, string encryptionKey)
{
    if (string.IsNullOrWhiteSpace(encryptionKey))
    {
        throw new InvalidOperationException("A non-empty SQLCipher key is required to open the SQLite connection.");
    }

    using (var pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA key = $key;";
        var parameter = pragma.CreateParameter();
        parameter.ParameterName = "$key";
        parameter.Value = encryptionKey;
        pragma.Parameters.Add(parameter);
        pragma.ExecuteNonQuery();
    }

    using var secureMemory = connection.CreateCommand();
    secureMemory.CommandText = "PRAGMA cipher_memory_security = ON;";
    secureMemory.ExecuteNonQuery();
}
