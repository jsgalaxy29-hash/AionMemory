using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class AionVisionService : IAionVisionService
{
    private static readonly string[] OcrTextKeys =
    [
        "ocrText",
        "text",
        "content",
        "summary"
    ];

    private readonly AionDbContext _db;
    private readonly IVisionService _visionProvider;
    private readonly ISearchService _search;
    private readonly RecordSearchIndexService _recordSearchIndex;
    private readonly ILogger<AionVisionService> _logger;

    public AionVisionService(
        AionDbContext db,
        IVisionService visionProvider,
        ISearchService search,
        RecordSearchIndexService recordSearchIndex,
        ILogger<AionVisionService> logger)
    {
        _db = db;
        _visionProvider = visionProvider;
        _search = search;
        _recordSearchIndex = recordSearchIndex;
        _logger = logger;
    }

    public async Task<S_VisionAnalysis> AnalyzeAsync(VisionAnalysisRequest request, CancellationToken cancellationToken = default)
    {
        var analysis = await _visionProvider.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
        analysis.FileId = request.FileId;
        analysis.AnalysisType = request.AnalysisType;

        var file = await _db.Files.FirstOrDefaultAsync(f => f.Id == request.FileId, cancellationToken).ConfigureAwait(false);
        var ocrText = request.AnalysisType == VisionAnalysisType.Ocr ? ExtractOcrText(analysis.ResultJson) : null;

        if (request.AnalysisType == VisionAnalysisType.Ocr && file is not null)
        {
            if (string.IsNullOrWhiteSpace(ocrText))
            {
                ocrText = file.FileName;
            }

            file.OcrText = ocrText;
            _db.Files.Update(file);
        }
        else if (file is null)
        {
            _logger.LogWarning("Vision analysis {AnalysisType} requested for missing file {FileId}", request.AnalysisType, request.FileId);
        }

        await _db.VisionAnalyses.AddAsync(analysis, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (file is not null)
        {
            await _search.IndexFileAsync(file, cancellationToken).ConfigureAwait(false);
        }

        if (request.AnalysisType == VisionAnalysisType.Ocr && file is not null)
        {
            await UpdateLinkedTargetsAsync(file.Id, cancellationToken).ConfigureAwait(false);
        }

        return analysis;
    }

    private async Task UpdateLinkedTargetsAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var targetIds = await _db.FileLinks.AsNoTracking()
            .Where(link => link.FileId == fileId)
            .Select(link => link.TargetId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (targetIds.Count == 0)
        {
            return;
        }

        var records = await _db.Records
            .Where(record => targetIds.Contains(record.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in records)
        {
            await _recordSearchIndex.UpdateRecordAsync(record, cancellationToken).ConfigureAwait(false);
        }

        var notes = await _db.Notes
            .Where(note => targetIds.Contains(note.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var note in notes)
        {
            await UpsertNoteSearchAsync(note, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpsertNoteSearchAsync(S_Note note, CancellationToken cancellationToken)
    {
        await EnsureNoteSearchIndexExistsAsync(cancellationToken).ConfigureAwait(false);
        var content = await BuildNoteSearchContentAsync(note, cancellationToken).ConfigureAwait(false);

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM NoteSearch WHERE NoteId = {note.Id};",
            cancellationToken)
            .ConfigureAwait(false);

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO NoteSearch(NoteId, Content) VALUES ({note.Id}, {content});",
            cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> BuildNoteSearchContentAsync(S_Note note, CancellationToken cancellationToken)
    {
        var tokens = new List<string> { note.Title, note.Content };
        if (note.Tags.Count > 0)
        {
            tokens.AddRange(note.Tags);
        }

        var linkedText = await _db.FileLinks.AsNoTracking()
            .Where(link => link.TargetId == note.Id)
            .Join(_db.Files.AsNoTracking(), link => link.FileId, file => file.Id, (_, file) => file.OcrText)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var text in linkedText)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                tokens.Add(text);
            }
        }

        return string.Join(" ", tokens.Where(token => !string.IsNullOrWhiteSpace(token))).Trim();
    }

    private async Task EnsureNoteSearchIndexExistsAsync(CancellationToken cancellationToken)
    {
        if (await TableExistsAsync("NoteSearch", cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(SearchIndexSql.NoteSearch, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
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

    private string? ExtractOcrText(string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(resultJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var key in OcrTextKeys)
            {
                if (document.RootElement.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Unable to parse vision JSON for OCR extraction");
        }

        return null;
    }
}
