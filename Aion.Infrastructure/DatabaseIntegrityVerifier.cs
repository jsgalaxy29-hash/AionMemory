using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Aion.Infrastructure;

public sealed record DatabaseIntegrityReport(bool IsValid, IReadOnlyList<string> Issues);

public static class DatabaseIntegrityVerifier
{
    public static async Task<DatabaseIntegrityReport> VerifyAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var issues = new List<string>();
        try
        {
            var integrityResults = await RunPragmaAsync(connection, "PRAGMA integrity_check;", cancellationToken).ConfigureAwait(false);
            if (integrityResults.Count == 0)
            {
                issues.Add("Integrity check returned no results.");
            }
            else if (integrityResults.Count != 1 || !string.Equals(integrityResults[0], "ok", StringComparison.OrdinalIgnoreCase))
            {
                issues.AddRange(integrityResults);
            }
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("Database integrity check failed to execute; the database may be corrupted.", ex);
        }

        var foreignKeyResults = await RunPragmaAsync(connection, "PRAGMA foreign_key_check;", cancellationToken).ConfigureAwait(false);
        if (foreignKeyResults.Count > 0)
        {
            issues.Add($"Foreign key check failed: {string.Join(" | ", foreignKeyResults)}");
        }

        return new DatabaseIntegrityReport(issues.Count == 0, issues);
    }

    private static async Task<List<string>> RunPragmaAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.FieldCount == 1)
            {
                results.Add(reader.GetValue(0)?.ToString() ?? string.Empty);
                continue;
            }

            var segments = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                segments[i] = reader.GetValue(i)?.ToString() ?? string.Empty;
            }

            results.Add(string.Join(":", segments));
        }

        return results;
    }
}
