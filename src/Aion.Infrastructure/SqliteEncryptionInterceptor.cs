using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aion.Infrastructure;

public sealed class SqliteEncryptionInterceptor : DbConnectionInterceptor
{
    private readonly string _encryptionKey;

    public SqliteEncryptionInterceptor(string encryptionKey)
    {
        _encryptionKey = encryptionKey ?? string.Empty;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyEncryptionPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ApplyEncryptionPragmas(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void ApplyEncryptionPragmas(DbConnection connection)
    {
        if (string.IsNullOrWhiteSpace(_encryptionKey))
        {
            throw new InvalidOperationException("A non-empty SQLCipher key is required to open the SQLite connection.");
        }

        if (connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        using (var pragma = sqliteConnection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA key = $key;";
            var parameter = pragma.CreateParameter();
            parameter.ParameterName = "$key";
            parameter.Value = _encryptionKey;
            pragma.Parameters.Add(parameter);
            pragma.ExecuteNonQuery();
        }

        using var secureMemory = sqliteConnection.CreateCommand();
        secureMemory.CommandText = "PRAGMA cipher_memory_security = ON;";
        secureMemory.ExecuteNonQuery();
    }
}
