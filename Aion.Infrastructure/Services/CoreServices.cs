using System.Security.Cryptography;
using System.Text.Json;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class MetadataService : IMetadataService
{
    private readonly AionDbContext _db;
    private readonly ILogger<MetadataService> _logger;

    public MetadataService(AionDbContext db, ILogger<MetadataService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<S_EntityType> AddEntityTypeAsync(Guid moduleId, S_EntityType entityType, CancellationToken cancellationToken = default)
    {
        entityType.ModuleId = moduleId;
        await _db.EntityTypes.AddAsync(entityType, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Entity type {Name} added to module {Module}", entityType.Name, moduleId);
        return entityType;
    }

    public async Task<S_Module> CreateModuleAsync(S_Module module, CancellationToken cancellationToken = default)
    {
        await _db.Modules.AddAsync(module, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Module {Name} created", module.Name);
        return module;
    }

    public async Task<IEnumerable<S_Module>> GetModulesAsync(CancellationToken cancellationToken = default)
        => await _db.Modules
            .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
            .Include(m => m.Reports)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Actions)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Conditions)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

public sealed class AionDataEngine : IAionDataEngine, IDataEngine
{
    private readonly AionDbContext _db;
    private readonly ILogger<AionDataEngine> _logger;

    public AionDataEngine(AionDbContext db, ILogger<AionDataEngine> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default)
    {
        await _db.Tables.AddAsync(table, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Table {Table} created with {FieldCount} fields", table.Name, table.Fields.Count);
        return table;
    }

    public async Task<STable?> GetTableAsync(Guid tableId, CancellationToken cancellationToken = default)
        => await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .FirstOrDefaultAsync(t => t.Id == tableId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IEnumerable<STable>> GetTablesAsync(CancellationToken cancellationToken = default)
        => await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<F_Record?> GetAsync(Guid entityTypeId, Guid id, CancellationToken cancellationToken = default)
        => await _db.Records.FirstOrDefaultAsync(r => r.EntityTypeId == entityTypeId && r.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<F_Record> InsertAsync(Guid entityTypeId, string dataJson, CancellationToken cancellationToken = default)
    {
        var record = new F_Record
        {
            EntityTypeId = entityTypeId,
            DataJson = dataJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _db.Records.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} inserted for entity {EntityTypeId}", record.Id, entityTypeId);
        return record;
    }

    public async Task<F_Record> UpdateAsync(Guid entityTypeId, Guid id, string dataJson, CancellationToken cancellationToken = default)
    {
        var record = await _db.Records.FirstOrDefaultAsync(r => r.EntityTypeId == entityTypeId && r.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Record {id} not found for entity {entityTypeId}");

        record.DataJson = dataJson;
        record.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Records.Update(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} updated for entity {EntityTypeId}", id, entityTypeId);
        return record;
    }

    public async Task DeleteAsync(Guid entityTypeId, Guid id, CancellationToken cancellationToken = default)
    {
        var record = await _db.Records.FirstOrDefaultAsync(r => r.EntityTypeId == entityTypeId && r.Id == id, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return;
        }

        _db.Records.Remove(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} deleted for entity {EntityTypeId}", id, entityTypeId);
    }

    public async Task<IEnumerable<F_Record>> QueryAsync(Guid entityTypeId, string? filter = null, IDictionary<string, string?>? equals = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Records.AsQueryable().Where(r => r.EntityTypeId == entityTypeId);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(r => _db.RecordSearch
                .Where(s => s.EntityTypeId == entityTypeId && EF.Functions.Match(s.Content, filter))
                .Select(s => s.RecordId)
                .Contains(r.Id));
        }

        if (equals is not null)
        {
            foreach (var clause in equals.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)))
            {
                var jsonPath = $"$.{clause.Key}";
                query = query.Where(r => EF.Functions.JsonExtract(r.DataJson, jsonPath) == clause.Value);
            }
        }

        return await query.OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class NoteService : IAionNoteService, INoteService
{
    private readonly AionDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAudioTranscriptionProvider _transcriptionProvider;
    private readonly ILogger<NoteService> _logger;

    public NoteService(AionDbContext db, IFileStorageService fileStorage, IAudioTranscriptionProvider transcriptionProvider, ILogger<NoteService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _transcriptionProvider = transcriptionProvider;
        _logger = logger;
    }

    public async Task<S_Note> CreateDictatedNoteAsync(string title, Stream audioStream, string fileName, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var audioFile = await _fileStorage.SaveAsync(fileName, audioStream, "audio/wav", cancellationToken).ConfigureAwait(false);
        audioStream.Position = 0;
        var transcription = await _transcriptionProvider.TranscribeAsync(audioStream, fileName, cancellationToken).ConfigureAwait(false);

        var note = BuildNote(title, transcription.Text, NoteSourceType.Voice, links, audioFile.Id);
        await PersistNoteAsync(note, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<S_Note> CreateTextNoteAsync(string title, string content, IEnumerable<J_Note_Link>? links = null, CancellationToken cancellationToken = default)
    {
        var note = BuildNote(title, content, NoteSourceType.Text, links, null);
        await PersistNoteAsync(note, cancellationToken).ConfigureAwait(false);
        return note;
    }

    public async Task<IEnumerable<S_Note>> GetChronologicalAsync(int take = 50, CancellationToken cancellationToken = default)
        => await _db.Notes
            .Include(n => n.Links)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private S_Note BuildNote(string title, string content, NoteSourceType source, IEnumerable<J_Note_Link>? links, Guid? audioFileId)
    {
        var linkList = links?.ToList() ?? new List<J_Note_Link>();
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
            Links = linkList,
            JournalContext = context
        };
    }

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

        history.Links.Add(new S_Link
        {
            SourceType = nameof(S_HistoryEvent),
            SourceId = history.Id,
            TargetType = note.JournalContext ?? nameof(S_Note),
            TargetId = note.Id,
            Relation = "note"
        });

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Note {NoteId} persisted (source={Source})", note.Id, note.Source);
    }
}

public sealed class AgendaService : IAionAgendaService, IAgendaService
{
    private readonly AionDbContext _db;
    private readonly ILogger<AgendaService> _logger;

    public AgendaService(AionDbContext db, ILogger<AgendaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<S_Event> AddEventAsync(S_Event evt, CancellationToken cancellationToken = default)
    {
        evt.ReminderAt ??= evt.Start.AddHours(-2);
        await _db.Events.AddAsync(evt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var history = new S_HistoryEvent
        {
            Title = "Évènement planifié",
            Description = evt.Title,
            OccurredAt = DateTimeOffset.UtcNow,
            Links = new List<S_Link>()
        };

        foreach (var link in evt.Links)
        {
            history.Links.Add(new S_Link
            {
                SourceType = nameof(S_HistoryEvent),
                SourceId = history.Id,
                TargetType = link.TargetType,
                TargetId = link.TargetId,
                Relation = "agenda"
            });
        }

        await _db.HistoryEvents.AddAsync(history, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Event {EventId} added with reminder {Reminder}", evt.Id, evt.ReminderAt);
        return evt;
    }

    public async Task<IEnumerable<S_Event>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => await _db.Events
            .Where(e => e.Start >= from && e.Start <= to)
            .Include(e => e.Links)
            .OrderBy(e => e.Start)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IEnumerable<S_Event>> GetPendingRemindersAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
        => await _db.Events.Where(e => !e.IsCompleted && e.ReminderAt.HasValue && e.ReminderAt <= asOf)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

public sealed class FileStorageService : IFileStorageService
{
    private readonly string _storageRoot;
    private readonly AionDbContext _db;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(IOptions<StorageOptions> options, AionDbContext db, ILogger<FileStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _storageRoot = options.Value.RootPath ?? throw new InvalidOperationException("Storage root path must be provided");
        _db = db;
        _logger = logger;
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(_storageRoot, id.ToString());
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);

        var file = new F_File
        {
            Id = id,
            FileName = fileName,
            MimeType = mimeType,
            StoragePath = path,
            Size = fs.Length,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await _db.Files.AddAsync(file, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} stored at {Path}", id, path);
        return file;
    }

    public async Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.Files.FirstAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        Stream stream = new FileStream(file.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return stream;
    }

    public async Task<F_FileLink> LinkAsync(Guid fileId, string targetType, Guid targetId, string? relation = null, CancellationToken cancellationToken = default)
    {
        var link = new F_FileLink
        {
            FileId = fileId,
            TargetType = targetType,
            TargetId = targetId,
            Relation = relation
        };

        await _db.FileLinks.AddAsync(link, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} linked to {TargetType}:{TargetId}", fileId, targetType, targetId);
        return link;
    }

    public async Task<IEnumerable<F_File>> GetForAsync(string targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        var fileIds = await _db.FileLinks.Where(l => l.TargetType == targetType && l.TargetId == targetId)
            .Select(l => l.FileId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return await _db.Files.Where(f => fileIds.Contains(f.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class CloudBackupService : ICloudBackupService
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _backupFolder;
    private readonly ILogger<CloudBackupService> _logger;

    public CloudBackupService(IOptions<BackupOptions> options, ILogger<CloudBackupService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _backupFolder = options.Value.BackupFolder ?? throw new InvalidOperationException("Backup folder must be configured");
        _logger = logger;
        Directory.CreateDirectory(_backupFolder);
    }

    public async Task BackupAsync(string encryptedDatabasePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(encryptedDatabasePath))
        {
            throw new FileNotFoundException("Database file not found", encryptedDatabasePath);
        }

        var timestamp = DateTimeOffset.UtcNow;
        var backupFileName = $"aion-{timestamp:yyyyMMddHHmmss}.db";
        var destination = Path.Combine(_backupFolder, backupFileName);
        await using var source = new FileStream(encryptedDatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (var dest = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
            await dest.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var manifest = new BackupManifest
        {
            FileName = backupFileName,
            CreatedAt = timestamp,
            Size = new FileInfo(destination).Length,
            Sha256 = await ComputeHashAsync(destination, cancellationToken).ConfigureAwait(false),
            SourcePath = Path.GetFullPath(encryptedDatabasePath)
        };

        await using var manifestStream = new FileStream(Path.ChangeExtension(destination, ".json"), FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(manifestStream, manifest, ManifestSerializerOptions, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Backup created at {Destination} (hash {Hash})", destination, manifest.Sha256);
    }

    public async Task RestoreAsync(string destinationPath, CancellationToken cancellationToken = default)
    {
        var manifest = LoadLatestManifest();
        if (manifest is null)
        {
            throw new FileNotFoundException("No backup manifest found", _backupFolder);
        }

        var backupFile = Path.Combine(_backupFolder, manifest.FileName);
        if (!File.Exists(backupFile))
        {
            throw new FileNotFoundException("Backup file missing", backupFile);
        }

        var tempPath = destinationPath + ".restoring";
        await using (var source = new FileStream(backupFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }

        var computedHash = await ComputeHashAsync(tempPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(computedHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Backup integrity check failed: hash mismatch");
        }

        var backupExisting = destinationPath + ".bak";
        if (File.Exists(destinationPath))
        {
            File.Move(destinationPath, backupExisting, overwrite: true);
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        _logger.LogInformation("Backup restored transactionally from {BackupFile}", backupFile);
    }

    private BackupManifest? LoadLatestManifest()
    {
        var manifestFile = Directory.GetFiles(_backupFolder, "*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (manifestFile is null)
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestFile);
            return JsonSerializer.Deserialize<BackupManifest>(json, ManifestSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read backup manifest {Manifest}", manifestFile);
            return null;
        }
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}

public sealed class SearchService : ISearchService
{
    private readonly AionDbContext _db;

    public SearchService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<string>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var notes = await _db.NoteSearch
            .Where(n => EF.Functions.Match(n.Content, query))
            .Select(n => $"Note:{n.NoteId}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var records = await _db.RecordSearch
            .Where(r => EF.Functions.Match(r.Content, query))
            .Select(r => $"Record:{r.RecordId}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return notes.Concat(records);
    }
}

public sealed class AutomationService : IAionAutomationService, IAutomationService
{
    private readonly AionDbContext _db;
    private readonly ILogger<AutomationService> _logger;

    public AutomationService(AionDbContext db, ILogger<AutomationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<S_AutomationRule> AddRuleAsync(S_AutomationRule rule, CancellationToken cancellationToken = default)
    {
        await _db.AutomationRules.AddAsync(rule, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Automation rule {Rule} registered", rule.Name);
        return rule;
    }

    public async Task<IEnumerable<S_AutomationRule>> GetRulesAsync(CancellationToken cancellationToken = default)
        => await _db.AutomationRules
            .Include(r => r.Conditions)
            .Include(r => r.Actions)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default)
    {
        var rules = await GetRulesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var rule in rules.Where(r => r.IsEnabled && string.Equals(r.TriggerFilter, eventName, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogInformation("[automation] Rule {Rule} triggered by {Event}", rule.Name, eventName);
            // TODO: dispatch actions to background worker / message bus
        }
    }
}

public sealed class DashboardService : IDashboardService
{
    private readonly AionDbContext _db;

    public DashboardService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<DashboardWidget>> GetWidgetsAsync(CancellationToken cancellationToken = default)
        => await _db.Widgets.OrderBy(w => w.Order).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<DashboardWidget> SaveWidgetAsync(DashboardWidget widget, CancellationToken cancellationToken = default)
    {
        if (await _db.Widgets.AnyAsync(w => w.Id == widget.Id, cancellationToken).ConfigureAwait(false))
        {
            _db.Widgets.Update(widget);
        }
        else
        {
            await _db.Widgets.AddAsync(widget, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return widget;
    }
}

public sealed class TemplateService : IAionTemplateMarketplaceService, ITemplateService
{
    private readonly AionDbContext _db;
    private readonly string _marketplaceFolder;

    public TemplateService(AionDbContext db, IOptions<MarketplaceOptions> options)
    {
        _db = db;
        ArgumentNullException.ThrowIfNull(options);
        _marketplaceFolder = options.Value.MarketplaceFolder ?? throw new InvalidOperationException("Marketplace folder is required");
        Directory.CreateDirectory(_marketplaceFolder);
    }

    public async Task<TemplatePackage> ExportModuleAsync(Guid moduleId, CancellationToken cancellationToken = default)
    {
        var module = await _db.Modules
            .Include(m => m.EntityTypes).ThenInclude(e => e.Fields)
            .Include(m => m.Reports)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Actions)
            .Include(m => m.AutomationRules).ThenInclude(r => r.Conditions)
            .FirstAsync(m => m.Id == moduleId, cancellationToken)
            .ConfigureAwait(false);

        var payload = JsonSerializer.Serialize(module, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        var package = new TemplatePackage
        {
            Name = module.Name,
            Description = module.Description,
            Payload = payload,
            Version = "1.0.0"
        };

        await _db.Templates.AddAsync(package, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        return package;
    }

    public async Task<S_Module> ImportModuleAsync(TemplatePackage package, CancellationToken cancellationToken = default)
    {
        var module = JsonSerializer.Deserialize<S_Module>(package.Payload) ?? new S_Module { Name = package.Name };
        await CreateOrUpdateMarketplaceEntry(package, cancellationToken).ConfigureAwait(false);
        await _db.Modules.AddAsync(module, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return module;
    }

    private async Task CreateOrUpdateMarketplaceEntry(TemplatePackage package, CancellationToken cancellationToken)
    {
        var fileName = Path.Combine(_marketplaceFolder, $"{package.Id}.json");
        await File.WriteAllTextAsync(fileName, package.Payload, cancellationToken).ConfigureAwait(false);

        if (!await _db.Marketplace.AnyAsync(i => i.Id == package.Id, cancellationToken).ConfigureAwait(false))
        {
            await _db.Marketplace.AddAsync(new MarketplaceItem
            {
                Id = package.Id,
                Name = package.Name,
                Category = "Module",
                PackagePath = fileName
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var existing = await _db.Marketplace.FirstAsync(i => i.Id == package.Id, cancellationToken).ConfigureAwait(false);
            existing.PackagePath = fileName;
            existing.Name = package.Name;
            _db.Marketplace.Update(existing);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<MarketplaceItem>> GetMarketplaceAsync(CancellationToken cancellationToken = default)
    {
        // Synchronise le disque et la base pour rester cohérent avec les fichiers locaux
        foreach (var file in Directory.EnumerateFiles(_marketplaceFolder, "*.json"))
        {
            var id = Guid.Parse(Path.GetFileNameWithoutExtension(file));
            if (!await _db.Marketplace.AnyAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false))
            {
                await _db.Marketplace.AddAsync(new MarketplaceItem
                {
                    Id = id,
                    Name = Path.GetFileName(file),
                    Category = "Module",
                    PackagePath = file
                }, cancellationToken).ConfigureAwait(false);
            }
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await _db.Marketplace.ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class LifeService : IAionLifeLogService, ILifeService
{
    private readonly AionDbContext _db;

    public LifeService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<S_HistoryEvent> AddHistoryAsync(S_HistoryEvent evt, CancellationToken cancellationToken = default)
    {
        await _db.HistoryEvents.AddAsync(evt, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return evt;
    }

    public async Task<IEnumerable<S_HistoryEvent>> GetTimelineAsync(DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default)
    {
        var query = _db.HistoryEvents.Include(h => h.Links).AsQueryable();
        if (from.HasValue)
        {
            query = query.Where(h => h.OccurredAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(h => h.OccurredAt <= to.Value);
        }

        return await query.OrderByDescending(h => h.OccurredAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class PredictService : IAionPredictionService, IPredictService
{
    private readonly AionDbContext _db;

    public PredictService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<IEnumerable<PredictionInsight>> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var insights = new List<PredictionInsight>
        {
            new()
            {
                Kind = PredictionKind.Reminder,
                Message = "Hydrate your potager seedlings this evening.",
                GeneratedAt = DateTimeOffset.UtcNow
            }
        };

        await _db.Predictions.AddRangeAsync(insights, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return insights;
    }
}

public sealed class PersonaEngine : IAionPersonaEngine, IPersonaEngine
{
    private readonly AionDbContext _db;

    public PersonaEngine(AionDbContext db)
    {
        _db = db;
    }

    public async Task<UserPersona> GetPersonaAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.Personas.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        var defaultPersona = new UserPersona { Name = "Default", Tone = PersonaTone.Neutral };
        await _db.Personas.AddAsync(defaultPersona, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return defaultPersona;
    }

    public async Task<UserPersona> SavePersonaAsync(UserPersona persona, CancellationToken cancellationToken = default)
    {
        if (await _db.Personas.AnyAsync(p => p.Id == persona.Id, cancellationToken).ConfigureAwait(false))
        {
            _db.Personas.Update(persona);
        }
        else
        {
            await _db.Personas.AddAsync(persona, cancellationToken).ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return persona;
    }
}

public sealed class VisionService : IAionVisionService, IVisionService
{
    private readonly AionDbContext _db;

    public VisionService(AionDbContext db)
    {
        _db = db;
    }

    public async Task<S_VisionAnalysis> AnalyzeAsync(Guid fileId, VisionAnalysisType analysisType, CancellationToken cancellationToken = default)
    {
        var analysis = new S_VisionAnalysis
        {
            FileId = fileId,
            AnalysisType = analysisType,
            ResultJson = JsonSerializer.Serialize(new { summary = "Vision analysis placeholder" })
        };

        await _db.VisionAnalyses.AddAsync(analysis, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return analysis;
    }
}
