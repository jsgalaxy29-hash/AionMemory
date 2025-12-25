using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure;

public sealed class AionDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AionDbContext>
{
    public AionDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AionDbContext>();
        var devDefaults = SqliteCipherDevelopmentDefaults.CreateDefaults("aion_designtime.db");
        var overrideKey = Environment.GetEnvironmentVariable("AION_DB_KEY");
        if (!string.IsNullOrWhiteSpace(overrideKey))
        {
            devDefaults.EncryptionKey = overrideKey;
        }

        var options = Options.Create(devDefaults);

        SqliteConnectionFactory.ConfigureBuilder(builder, options);
        return new AionDbContext(builder.Options, new DefaultWorkspaceContext());
    }
}
