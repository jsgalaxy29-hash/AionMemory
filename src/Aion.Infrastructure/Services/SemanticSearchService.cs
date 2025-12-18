using System.Data;
using System.Text.Json;
using System.Threading;
using Aion.AI;
using Aion.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class SemanticSearchService : ISearchService
{
    private static readonly JsonSerializerOptions EmbeddingSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _keywordIndexGate = new(1, 1);
    private readonly AionDbContext _db;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly ILogger<SemanticSearchService> _logger;
    private bool _keywordIndexesReady;

    public SemanticSearchService(AionDbContext db, ILogger<SemanticSearchService> logger, IServiceProvider serviceProvider)
    {
        _db = db;
        _logger = logger;
        _embeddingProvider = serviceProvider.GetService<IEmbeddingProvider>();
    }

    public async Task<IEnumerable<SearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, SearchHit>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return results.Values;
        }

        await AddOrUpdateAsync(results, await FetchKeywordMatchesAsync(query, cancellationToken).ConfigureAwait(false));
        await AddOrUpdateAsync(results, await RunSemanticSearchAsync(query, cancellationToken).ConfigureAwait(false));

        return results
            .Values
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Title)
            .Take(20)
            .ToList();
    }

    public Task IndexNoteAsync(S_Note note, CancellationToken cancellationToken = default)
        => UpsertSemanticEntryAsync("Note", note.Id, note.Title, note.Content, cancellationToken);

    public async Task IndexRecordAsync(F_Record record, CancellationToken cancellationToken = default)
    {
        var table = await _db.Tables
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == record.TableId, cancellationToken)
            .ConfigureAwait(false);

        var title = table?.DisplayName ?? table?.Name ?? $"Enregistrement {record.Id}";
        await UpsertSemanticEntryAsync("Record", record.Id, title, record.DataJson ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task IndexFileAsync(F_File file, CancellationToken cancellationToken = default)
    {
        var content = string.Join(" ", new[] { file.FileName, file.MimeType, file.ThumbnailPath })
            .Trim();
        return UpsertSemanticEntryAsync("File", file.Id, file.FileName, content, cancellationToken);
    }

    public async Task RemoveAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        var entry = await _db.SemanticSearch
            .FirstOrDefaultAsync(e => e.TargetType == targetType && e.TargetId == targetId, cancellationToken)
            .ConfigureAwait(false);

        if (entry is null)
        {
            return;
        }

        _db.SemanticSearch.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IEnumerable<SearchHit>> FetchKeywordMatchesAsync(string query, CancellationToken cancellationToken)
    {
        var hits = new List<SearchHit>();

        var notes = await SafeQueryAsync(
            () => _db.NoteSearch
                .Where(n => n.Content.Contains(query))
                .Select(n => new { n.NoteId, n.Content })
                .ToListAsync(cancellationToken),
            "NoteSearch",
            cancellationToken).ConfigureAwait(false);

        hits.AddRange(notes.Select(n => new SearchHit("Note", n.NoteId, $"Note {n.NoteId:N}", BuildSnippet(n.Content), 0.6)));

        var records = await SafeQueryAsync(
            () => _db.RecordSearch
                .Where(r => r.Content.Contains(query))
                .Select(r => new { r.RecordId, r.TableId, r.Content })
                .ToListAsync(cancellationToken),
            "RecordSearch",
            cancellationToken).ConfigureAwait(false);

        foreach (var record in records)
        {
            hits.Add(new SearchHit("Record", record.RecordId, $"Enregistrement {record.TableId:N}", BuildSnippet(record.Content), 0.5));
        }

        var files = await SafeQueryAsync(
            () => _db.FileSearch
                .Where(f => f.Content.Contains(query))
                .Select(f => new { f.FileId, f.Content })
                .ToListAsync(cancellationToken),
            "FileSearch",
            cancellationToken).ConfigureAwait(false);

        hits.AddRange(files.Select(f => new SearchHit("File", f.FileId, $"Fichier {f.FileId:N}", BuildSnippet(f.Content), 0.4)));

        return hits;
    }

    private async Task<IEnumerable<SearchHit>> RunSemanticSearchAsync(string query, CancellationToken cancellationToken)
    {
        if (_embeddingProvider is null)
        {
            return Array.Empty<SearchHit>();
        }

        float[] embedding;
        try
        {
            embedding = (await _embeddingProvider.EmbedAsync(query, cancellationToken).ConfigureAwait(false)).Vector;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic search fallback to keyword only");
            return Array.Empty<SearchHit>();
        }

        var candidates = await _db.SemanticSearch
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hits = new List<SearchHit>();
        foreach (var entry in candidates)
        {
            var vector = ParseEmbedding(entry.EmbeddingJson);
            if (vector is null)
            {
                continue;
            }

            var score = ComputeCosineSimilarity(embedding, vector);
            hits.Add(new SearchHit(entry.TargetType, entry.TargetId, entry.Title, BuildSnippet(entry.Content), score));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .Take(10)
            .ToList();
    }

    private async Task UpsertSemanticEntryAsync(string targetType, Guid targetId, string title, string content, CancellationToken cancellationToken)
    {
        var entry = await _db.SemanticSearch
            .FirstOrDefaultAsync(e => e.TargetType == targetType && e.TargetId == targetId, cancellationToken)
            .ConfigureAwait(false);

        entry ??= new SemanticSearchEntry { TargetType = targetType, TargetId = targetId };
        entry.Title = title;
        entry.Content = content;
        entry.IndexedAt = DateTimeOffset.UtcNow;
        entry.EmbeddingJson = await TryEmbedAsync(title, content, cancellationToken).ConfigureAwait(false);

        if (_db.Entry(entry).State == EntityState.Detached)
        {
            await _db.SemanticSearch.AddAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _db.SemanticSearch.Update(entry);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TryEmbedAsync(string title, string content, CancellationToken cancellationToken)
    {
        if (_embeddingProvider is null)
        {
            return null;
        }

        try
        {
            var embedding = await _embeddingProvider
                .EmbedAsync($"{title}\n{content}", cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Serialize(embedding.Vector, EmbeddingSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to generate embedding for {Target}", title);
            return null;
        }
    }

    private async Task<List<T>> SafeQueryAsync<T>(Func<Task<List<T>>> query, string source, CancellationToken cancellationToken)
    {
        try
        {
            return await query().ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Skipping {Source} keyword search because the index is missing.", source);
            await EnsureKeywordIndexesAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await query().ConfigureAwait(false);
            }
            catch (Exception retryEx)
            {
                _logger.LogWarning(retryEx, "Unable to run {Source} keyword search even after ensuring indexes exist.", source);
                return new List<T>();
            }
        }
    }

    private async Task EnsureKeywordIndexesAsync(CancellationToken cancellationToken)
    {
        if (_keywordIndexesReady)
        {
            return;
        }

        await _keywordIndexGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_keywordIndexesReady)
            {
                return;
            }

            await _db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            await EnsureKeywordIndexExistsAsync("NoteSearch", NoteSearchSql, cancellationToken).ConfigureAwait(false);
            await EnsureKeywordIndexExistsAsync("RecordSearch", RecordSearchSql, cancellationToken).ConfigureAwait(false);
            await EnsureKeywordIndexExistsAsync("FileSearch", FileSearchSql, cancellationToken).ConfigureAwait(false);
            _keywordIndexesReady = true;
        }
        finally
        {
            _keywordIndexGate.Release();
        }
    }

    private async Task EnsureKeywordIndexExistsAsync(string tableName, string creationSql, CancellationToken cancellationToken)
    {
        if (await TableExistsAsync(tableName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(creationSql, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = tableName;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (shouldClose)
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }

        return result is string;
    }

    private const string NoteSearchSql = """
DROP TRIGGER IF EXISTS NoteSearch_ai;
DROP TRIGGER IF EXISTS NoteSearch_au;
DROP TRIGGER IF EXISTS NoteSearch_ad;
CREATE VIRTUAL TABLE IF NOT EXISTS NoteSearch USING fts5(NoteId UNINDEXED, Content);
CREATE TRIGGER NoteSearch_ai AFTER INSERT ON Notes BEGIN
    DELETE FROM NoteSearch WHERE NoteId = new.Id;
    INSERT INTO NoteSearch(NoteId, Content) VALUES (new.Id, COALESCE(new.Title,'') || ' ' || COALESCE(new.Content,''));
END;
CREATE TRIGGER NoteSearch_au AFTER UPDATE ON Notes BEGIN
    DELETE FROM NoteSearch WHERE NoteId = new.Id;
    INSERT INTO NoteSearch(NoteId, Content) VALUES (new.Id, COALESCE(new.Title,'') || ' ' || COALESCE(new.Content,''));
END;
CREATE TRIGGER NoteSearch_ad AFTER DELETE ON Notes BEGIN
    DELETE FROM NoteSearch WHERE NoteId = old.Id;
END;
INSERT INTO NoteSearch(NoteId, Content)
SELECT Id, COALESCE(Title,'') || ' ' || COALESCE(Content,'') FROM Notes;
""";

    private const string RecordSearchSql = """
DROP TRIGGER IF EXISTS RecordSearch_ai;
DROP TRIGGER IF EXISTS RecordSearch_au;
DROP TRIGGER IF EXISTS RecordSearch_ad;
CREATE VIRTUAL TABLE IF NOT EXISTS RecordSearch USING fts5(RecordId UNINDEXED, EntityTypeId UNINDEXED, Content);
CREATE TRIGGER RecordSearch_ai AFTER INSERT ON Records BEGIN
    DELETE FROM RecordSearch WHERE RecordId = new.Id;
    INSERT INTO RecordSearch(RecordId, EntityTypeId, Content) VALUES (new.Id, new.EntityTypeId, COALESCE(new.DataJson,''));
END;
CREATE TRIGGER RecordSearch_au AFTER UPDATE ON Records BEGIN
    DELETE FROM RecordSearch WHERE RecordId = new.Id;
    INSERT INTO RecordSearch(RecordId, EntityTypeId, Content) VALUES (new.Id, new.EntityTypeId, COALESCE(new.DataJson,''));
END;
CREATE TRIGGER RecordSearch_ad AFTER DELETE ON Records BEGIN
    DELETE FROM RecordSearch WHERE RecordId = old.Id;
END;
INSERT INTO RecordSearch(RecordId, EntityTypeId, Content)
SELECT Id, EntityTypeId, COALESCE(DataJson,'') FROM Records;
""";

    private const string FileSearchSql = """
DROP TRIGGER IF EXISTS FileSearch_ai;
DROP TRIGGER IF EXISTS FileSearch_au;
DROP TRIGGER IF EXISTS FileSearch_ad;
CREATE VIRTUAL TABLE IF NOT EXISTS FileSearch USING fts5(FileId UNINDEXED, Content);
CREATE TRIGGER FileSearch_ai AFTER INSERT ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = new.Id;
    INSERT INTO FileSearch(FileId, Content) VALUES (new.Id, COALESCE(new.FileName,'') || ' ' || COALESCE(new.MimeType,'') || ' ' || COALESCE(new.StoragePath,''));
END;
CREATE TRIGGER FileSearch_au AFTER UPDATE ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = new.Id;
    INSERT INTO FileSearch(FileId, Content) VALUES (new.Id, COALESCE(new.FileName,'') || ' ' || COALESCE(new.MimeType,'') || ' ' || COALESCE(new.StoragePath,''));
END;
CREATE TRIGGER FileSearch_ad AFTER DELETE ON Files BEGIN
    DELETE FROM FileSearch WHERE FileId = old.Id;
END;
INSERT INTO FileSearch(FileId, Content)
SELECT Id, COALESCE(FileName,'') || ' ' || COALESCE(MimeType,'') || ' ' || COALESCE(StoragePath,'') FROM Files;
""";

    private static float[]? ParseEmbedding(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<float[]>(serialized, EmbeddingSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static double ComputeCosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || right.Count == 0 || left.Count != right.Count)
        {
            return 0;
        }

        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;
        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static async Task AddOrUpdateAsync(IDictionary<string, SearchHit> registry, IEnumerable<SearchHit> hits)
    {
        foreach (var hit in hits)
        {
            var key = $"{hit.TargetType}:{hit.TargetId}";
            if (registry.TryGetValue(key, out var existing))
            {
                if (hit.Score > existing.Score)
                {
                    registry[key] = hit;
                }
            }
            else
            {
                registry[key] = hit;
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static string BuildSnippet(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        const int limit = 180;
        var trimmed = content.Replace("\n", " ").Replace("\r", " ").Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[..limit] + "…";
    }
}
