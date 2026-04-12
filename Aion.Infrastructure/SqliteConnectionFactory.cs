using Aion.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure;

public static class SqliteConnectionFactory
{
    public static void ConfigureBuilder(DbContextOptionsBuilder optionsBuilder, IOptions<AionDatabaseOptions> databaseOptions)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentNullException.ThrowIfNull(databaseOptions);

        var options = databaseOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("ConnectionString is required for SQLite configuration.");
        }

        var builder = new SqliteConnectionStringBuilder(options.ConnectionString);
        builder.Remove("Password");
        builder.Remove("Pwd");
        if (builder.Mode is not SqliteOpenMode.ReadWriteCreate)
        {
            builder.Mode = SqliteOpenMode.ReadWriteCreate;
        }

        if (builder.Cache is not SqliteCacheMode.Private)
        {
            builder.Cache = SqliteCacheMode.Private;
        }

        if (builder.Pooling)
        {
            builder.Pooling = false;
        }

        if (builder.ForeignKeys == null || (bool)!builder.ForeignKeys)
        {
            builder.ForeignKeys = true;
        }

        optionsBuilder.AddInterceptors(new SqliteEncryptionInterceptor(options.EncryptionKey));
        optionsBuilder.UseSqlite(builder.ToString());
    }
}
