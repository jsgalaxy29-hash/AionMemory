using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure;

public sealed class AionDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AionDbContext>
{
    public AionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AionDbContext>();
        var connectionString = SqliteCipherDevelopmentDefaults.BuildConnectionString("aion_designtime.db");

        var options = Options.Create(new AionDatabaseOptions
        {
            ConnectionString = connectionString,
            EncryptionKey = Environment.GetEnvironmentVariable("AION_DB_KEY")
                ?? SqliteCipherDevelopmentDefaults.DevelopmentKey
        });

        SqliteConnectionFactory.ConfigureBuilder(builder, options);
        return new AionDbContext(builder.Options);
    }
}
