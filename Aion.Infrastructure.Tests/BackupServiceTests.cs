using System;
using System.IO;
using System.Threading.Tasks;
using Aion.Infrastructure;
using Aion.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aion.Infrastructure.Tests;

public class BackupServiceTests
{
    [Fact]
    public async Task Backup_and_restore_roundtrip_database()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var databasePath = Path.Combine(tempRoot, "aion.db");
        var backupFolder = Path.Combine(tempRoot, "backups");
        Directory.CreateDirectory(backupFolder);

        var dbOptions = Options.Create(new AionDatabaseOptions
        {
            ConnectionString = $"Data Source={databasePath}",
            EncryptionKey = new string('k', 32)
        });
        var backupOptions = Options.Create(new BackupOptions
        {
            BackupFolder = backupFolder,
            RequireIntegrityCheck = true
        });

        await using (var connection = new SqliteConnection(dbOptions.Value.ConnectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS sample(id INTEGER PRIMARY KEY, value TEXT);" +
                                  "INSERT INTO sample(value) VALUES('before');";
            await command.ExecuteNonQueryAsync();
        }

        var backupService = new BackupService(dbOptions, backupOptions, NullLogger<BackupService>.Instance);
        var manifest = await backupService.CreateBackupAsync(cancellationToken: default);

        await using (var connection = new SqliteConnection(dbOptions.Value.ConnectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO sample(value) VALUES('after');";
            await command.ExecuteNonQueryAsync();
        }

        var restoreService = new RestoreService(backupOptions, dbOptions, NullLogger<RestoreService>.Instance);
        await restoreService.RestoreLatestAsync(cancellationToken: default);

        await using var verifyConnection = new SqliteConnection(dbOptions.Value.ConnectionString);
        await verifyConnection.OpenAsync();
        var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT COUNT(*) FROM sample";
        var count = (long)(await verifyCommand.ExecuteScalarAsync());

        Assert.False(manifest.IsEncrypted);
        Assert.Equal(1, count);
    }
}
