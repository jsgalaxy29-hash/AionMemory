using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure;

public sealed class AionDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AionDbContext>
{
    public AionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AionDbContext>();
        var databasePath = Path.GetFullPath("aion_designtime.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        var options = Options.Create(new AionDatabaseOptions
        {
            ConnectionString = connectionString,
            EncryptionKey = Environment.GetEnvironmentVariable("AION_DB_KEY") ?? string.Empty
        });

        SqliteConnectionFactory.ConfigureBuilder(builder, options);
        return new AionDbContext(builder.Options);
    }
}
