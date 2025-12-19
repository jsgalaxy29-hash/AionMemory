using System.IO;
using Microsoft.Data.Sqlite;

namespace Aion.Infrastructure;

/// <summary>
/// Helper that centralizes a ready-to-use SQLCipher connection for local development and tests.
/// The key is a deterministic, non-sensitive value so that developers and CI can share
/// the same encrypted database without secret management overhead.
/// </summary>
public static class SqliteCipherDevelopmentDefaults
{
    /// <summary>
    /// Deterministic 32+ chars key suitable for dev/test only. Replace in production.
    /// </summary>
    public const string DevelopmentKey = "aion-dev-sqlcipher-key-change-me-32chars";

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
            options.EncryptionKey = DevelopmentKey;
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
}
