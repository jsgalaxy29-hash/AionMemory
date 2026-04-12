using System.IO;
using System.Security.Cryptography;
using Aion.Domain;
using Microsoft.Data.Sqlite;

namespace Aion.Infrastructure;

/// <summary>
/// Helper that centralizes a ready-to-use SQLCipher connection for local development and tests.
/// The key is generated per process for development and tests when no secure configuration
/// is provided, avoiding any hardcoded encryption placeholder in source control.
/// </summary>
public static class SqliteCipherDevelopmentDefaults
{
    private static readonly Lazy<string> DevelopmentKey = new(GenerateNewKey);

    /// <summary>
    /// Ensures the provided <see cref="AionDatabaseOptions"/> instance is populated with
    /// a SQLCipher-ready connection string and encryption key for local development/tests.
    /// </summary>
    public static void ApplyDefaults(AionDatabaseOptions options, string databaseFileName = "aion_dev.db", string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            options.ConnectionString = BuildConnectionString(databaseFileName, directory);
        }

        if (string.IsNullOrWhiteSpace(options.EncryptionKey))
        {
            options.EncryptionKey = GetOrCreateDevelopmentKey();
        }
    }

    /// <summary>
    /// Returns a fully initialized <see cref="AionDatabaseOptions"/> configured for
    /// development/test environments.
    /// </summary>
    public static AionDatabaseOptions CreateDefaults(string databaseFileName = "aion_dev.db", string? directory = null)
    {
        var options = new AionDatabaseOptions();
        ApplyDefaults(options, databaseFileName, directory);
        return options;
    }

    /// <summary>
    /// Builds a SQLCipher-enabled connection string targeting the provided file name.
    /// The database is created under the provided directory (or the current working directory)
    /// with sane defaults (private cache, read/write/create mode, password populated).
    /// </summary>
    public static string BuildConnectionString(string databaseFileName = "aion_dev.db", string? directory = null)
    {
        var targetDirectory = directory ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(targetDirectory);

        var databasePath = Path.Combine(targetDirectory, databaseFileName);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        };

        // Foreign keys are enforced by default when using SQLCipher >= 4; keep explicit for clarity.
        builder.ForeignKeys = true;

        return builder.ToString();
    }

    public static string GetOrCreateDevelopmentKey() => DevelopmentKey.Value;

    public static string GenerateNewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
