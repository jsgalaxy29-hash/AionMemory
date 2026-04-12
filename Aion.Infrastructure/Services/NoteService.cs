using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aion.AI;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Aion.Infrastructure.Observability;
using Aion.Infrastructure.Services.Automation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class NoteService : IAionNoteService, INoteService
{
    private readonly AionDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAudioTranscriptionProvider _transcriptionProvider;
    private readonly INoteTaggingService _taggingService;
    private readonly ISearchService _search;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<NoteService> _logger;

    public NoteService(
        AionDbContext db,
        IFileStorageService fileStorage,
        IAudioTranscriptionProvider transcriptionProvider,
        INoteTaggingService taggingService,
        ISearchService search,
        ICurrentUserService currentUserService,
        ILogger<NoteService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _transcriptionProvider = transcriptionProvider;
        _taggingService = taggingService;
        _search = search;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var audioFile = await _fileStorage.SaveAsync(fileName, audioStream, "audio/wav", cancellationToken).ConfigureAwait(false);
        audioStream.Position = 0;
        var transcription = await _transcriptionProvider.TranscribeAsync(audioStream, fileName, cancellationToken).ConfigureAwait(false);

        var tags = await _taggingService.SuggestTagsAsync(title, transcription.Text, cancellationToken).ConfigureAwait(false);
        var note = BuildNote(title, transcription.Text, NoteSourceType.Voice, links, audioFile.Id, tags);
        await PersistNoteAsync(note, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var tags = await _taggingService.SuggestTagsAsync(title, content, cancellationToken).ConfigureAwait(false);
        var note = BuildNote(title, content, NoteSourceType.Text, links, null, tags);
        await PersistNoteAsync(note, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<IEnumerable<S_Note>> GetChronologicalAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        var notes = await _db.Notes
            .Include(n => n.Links)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return notes
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToList();
    }

    private S_Note BuildNote(
        string title,
        string content,
        NoteSourceType source,
        IEnumerable<J_Note_Link>? links,
        Guid? audioFileId,
        IEnumerable<string>? tags)
    {
        var linkList = links?.ToList() ?? new List<J_Note_Link>();
        var normalizedTags = NormalizeTags(tags);
        var context = linkList.Count == 0
            ? null
            : string.Join(", ", linkList.Select(l => $"{l.TargetType}:{l.TargetId}"));

        return new S_Note
        {
            Title = title,
            Content = content,
            Source = source,
            AudioFileId = audioFileId,
            IsTranscribed = source == NoteSourceType.Voice,
            CreatedAt = DateTimeOffset.UtcNow,
            Tags = normalizedTags,
            Links = linkList,
            JournalContext = context
        };
    }

    private static List<string> NormalizeTags(IEnumerable<string>? tags)
        => tags is null
            ? new List<string>()
            : tags
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(tag => tag)
                .ToList();

    private async Task PersistNoteAsync(S_Note note, CancellationToken cancellationToken)
    {
        await _db.Notes.AddAsync(note, cancellationToken).ConfigureAwait(false);
        var history = new S_HistoryEvent
        {
            Title = "Note créée",
            Description = note.Title,
            OccurredAt = note.CreatedAt,
            Links = new List<S_Link>()
        };

        history.ModuleId = await ResolveModuleIdAsync(note.Links.Select(l => l.TargetType), cancellationToken).ConfigureAwait(false);

        history.Links.Add(new S_Link
        {
            SourceType = nameof(S_HistoryEvent),
            SourceId = history.Id,
            TargetType = note.JournalContext ?? nameof(S_Note),
            TargetId = note.Id,
            Relation = "note",
            Type = "history",
            CreatedBy = _currentUserService.GetCurrentUserId(),
            Reason = "note created"
        });

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Note {NoteId} persisted (source={Source})", note.Id, note.Source);
        await _search.IndexNoteAsync(note, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveModuleIdAsync(IEnumerable<string> targetTypes, CancellationToken cancellationToken)
    {
        var normalized = targetTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return null;
        }

        var tableIds = await _db.Tables.AsNoTracking()
            .Where(table => normalized.Contains(table.Name))
            .Select(table => table.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tableIds.Count == 0)
        {
            return null;
        }

        return await _db.EntityTypes.AsNoTracking()
            .Where(entity => tableIds.Contains(entity.Id))
            .Select(entity => (Guid?)entity.ModuleId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

