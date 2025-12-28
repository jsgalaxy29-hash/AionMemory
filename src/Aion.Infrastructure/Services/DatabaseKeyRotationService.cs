using System;
using System.Collections.Generic;
using System.IO;
using Aion.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class DatabaseKeyRotationService : IKeyRotationService
{
    private const int MinimumKeyLength = 32;
    private readonly AionDatabaseOptions _databaseOptions;
    private readonly IBackupService _backupService;
    private readonly IRestoreService _restoreService;
    private readonly ILogger<DatabaseKeyRotationService> _logger;
    private readonly ISecurityAuditService _securityAuditService;

    public DatabaseKeyRotationService(
        IOptions<AionDatabaseOptions> databaseOptions,
        IBackupService backupService,
        IRestoreService restoreService,
        ILogger<DatabaseKeyRotationService> logger,
        ISecurityAuditService securityAuditService)
    {
        _databaseOptions = databaseOptions.Value;
        _backupService = backupService;
        _restoreService = restoreService;
        _logger = logger;
        _securityAuditService = securityAuditService;
    }

    public async Task<KeyRotationResult> RotateAsync(string newKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newKey))
        {
            throw new ArgumentException("A non-empty SQLCipher key is required.", nameof(newKey));
        }

        if (newKey.Length < MinimumKeyLength)
        {
            throw new ArgumentException($"The SQLCipher key must contain at least {MinimumKeyLength} characters.", nameof(newKey));
        }

        if (string.IsNullOrWhiteSpace(_databaseOptions.EncryptionKey))
        {
            throw new InvalidOperationException("The current SQLCipher key is missing.");
        }

        if (string.Equals(newKey, _databaseOptions.EncryptionKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The new SQLCipher key must differ from the current key.");
        }

        var backup = await _backupService.CreateBackupAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var connectionString = BuildConnectionString(_databaseOptions.ConnectionString);
        var databasePath = ResolveDatabasePath(_databaseOptions.ConnectionString);

        try
        {
            await RekeyAsync(connectionString, _databaseOptions.EncryptionKey, newKey, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(connectionString, newKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLCipher key rotation failed; restoring the latest backup.");
            try
            {
                await _restoreService.RestoreLatestAsync(databasePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception restoreException)
            {
                _logger.LogCritical(restoreException, "Database restore failed after key rotation failure.");
            }

            throw new InvalidOperationException(
                "La rotation de la clé SQLCipher a échoué. La base a été restaurée depuis la sauvegarde la plus récente.",
                ex);
        }

        await _securityAuditService.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.Encryption,
            "database.key_rotated",
            metadata: new Dictionary<string, object?>
            {
                ["backupFile"] = backup.FileName,
                ["databasePath"] = databasePath
            }), cancellationToken).ConfigureAwait(false);

        return new KeyRotationResult(backup, DateTimeOffset.UtcNow, databasePath);
    }

    private static async Task RekeyAsync(string connectionString, string currentKey, string newKey, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ApplyKeyAsync(connection, currentKey, cancellationToken).ConfigureAwait(false);
        await VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);

        await using (var rekey = connection.CreateCommand())
        {
            rekey.CommandText = "PRAGMA rekey = $key;";
            var parameter = rekey.CreateParameter();
            parameter.ParameterName = "$key";
            parameter.Value = newKey;
            rekey.Parameters.Add(parameter);
            await rekey.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyAsync(string connectionString, string newKey, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ApplyKeyAsync(connection, newKey, cancellationToken).ConfigureAwait(false);
        await VerifyIntegrityAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyKeyAsync(SqliteConnection connection, string key, CancellationToken cancellationToken)
    {
        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA key = $key;";
            var parameter = pragma.CreateParameter();
            parameter.ParameterName = "$key";
            parameter.Value = key;
            pragma.Parameters.Add(parameter);
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var secureMemory = connection.CreateCommand();
        secureMemory.CommandText = "PRAGMA cipher_memory_security = ON;";
        await secureMemory.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyIntegrityAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not string text || !string.Equals(text, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("SQLite integrity check failed after SQLCipher key operation.");
        }
    }

    private static string BuildConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
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

        if (builder.ForeignKeys == null || (bool)!builder.ForeignKeys)
        {
            builder.ForeignKeys = true;
        }

        return builder.ToString();
    }

    private static string ResolveDatabasePath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("The SQLite data source must be configured.");
        }

        return Path.GetFullPath(builder.DataSource);
    }
}
