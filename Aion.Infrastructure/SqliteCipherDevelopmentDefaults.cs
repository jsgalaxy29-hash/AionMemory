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
            Cache = SqliteCacheMode.Private,
            Password = DevelopmentKey
        };

        // Foreign keys are enforced by default when using SQLCipher >= 4; keep explicit for clarity.
        builder.ForeignKeys = true;

        return builder.ToString();
    }
}
