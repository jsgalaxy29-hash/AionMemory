using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Aion.AI;
using Aion.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ISearchService _search;

    public AionDataEngine(AionDbContext db, ILogger<AionDataEngine> logger, ISearchService search)
    {
        _db = db;
        _logger = logger;
        _search = search;
    }

    public async Task<STable> CreateTableAsync(STable table, CancellationToken cancellationToken = default)
    {
        NormalizeTableDefinition(table);
        ValidateTableDefinition(table);

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
        var validatedJson = await ValidateRecordAsync(entityTypeId, dataJson, cancellationToken).ConfigureAwait(false);

        var record = new F_Record
        {
            EntityTypeId = entityTypeId,
            DataJson = validatedJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _db.Records.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} inserted for entity {EntityTypeId}", record.Id, entityTypeId);
        await _search.IndexRecordAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    public async Task<F_Record> UpdateAsync(Guid entityTypeId, Guid id, string dataJson, CancellationToken cancellationToken = default)
    {
        var record = await _db.Records.FirstOrDefaultAsync(r => r.EntityTypeId == entityTypeId && r.Id == id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Record {id} not found for entity {entityTypeId}");

        record.DataJson = await ValidateRecordAsync(entityTypeId, dataJson, cancellationToken).ConfigureAwait(false);
        record.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Records.Update(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Record {RecordId} updated for entity {EntityTypeId}", id, entityTypeId);
        await _search.IndexRecordAsync(record, cancellationToken).ConfigureAwait(false);
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
        await _search.RemoveAsync("Record", id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IEnumerable<F_Record>> QueryAsync(Guid entityTypeId, string? filter = null, IDictionary<string, string?>? equals = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Records.AsQueryable().Where(r => r.EntityTypeId == entityTypeId);
        var table = await GetTableAsync(entityTypeId, cancellationToken).ConfigureAwait(false);

        if (table is not null)
        {
            var viewFilter = ResolveViewFilter(filter, table);
            equals = MergeEqualsFilters(equals, viewFilter);
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(r => _db.RecordSearch
                .Where(s => s.EntityTypeId == entityTypeId && s.Content.Contains(filter))
                .Select(s => s.RecordId)
                .Contains(r.Id));
        }

        if (equals is not null)
        {
            foreach (var clause in equals.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)))
            {
                EnsureFieldExists(table, clause.Key);
                var searchText = $"\"{clause.Key}\":\"{clause.Value}\"";
                query = query.Where(r => r.DataJson != null && r.DataJson.Contains(searchText));
            }
        }

        return await query.OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void NormalizeTableDefinition(STable table)
    {
        foreach (var field in table.Fields)
        {
            field.TableId = table.Id;
        }

        foreach (var view in table.Views)
        {
            view.TableId = table.Id;
        }
    }

    private static void ValidateTableDefinition(STable table)
    {
        if (string.IsNullOrWhiteSpace(table.Name))
        {
            throw new InvalidOperationException("Table name is required");
        }

        var duplicate = table.Fields
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Field name '{duplicate.Key}' is duplicated in table {table.Name}");
        }

        foreach (var view in table.Views)
        {
            ValidateViewDefinition(view, table);
        }
    }

    private static void ValidateViewDefinition(SViewDefinition view, STable table)
    {
        if (string.IsNullOrWhiteSpace(view.QueryDefinition))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(view.QueryDefinition);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    EnsureFieldExists(table, property.Name);
                }
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid view definition for view {view.Name}", ex);
        }
    }

    private static void EnsureFieldExists(STable? table, string fieldName)
    {
        if (table is null)
        {
            return;
        }

        if (!table.Fields.Any(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Field '{fieldName}' is not defined for table {table.Name}");
        }
    }

    private async Task<string> ValidateRecordAsync(Guid tableId, string dataJson, CancellationToken cancellationToken)
    {
        var table = await GetTableAsync(tableId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Table {tableId} not found");

        using var payload = JsonDocument.Parse(string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (payload.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in payload.RootElement.EnumerateObject())
            {
                EnsureFieldExists(table, property.Name);
                values[property.Name] = ExtractValue(property.Value);
            }
        }

        foreach (var field in table.Fields)
        {
            if (!values.ContainsKey(field.Name))
            {
                if (field.DefaultValue is not null)
                {
                    values[field.Name] = field.DefaultValue;
                }
                else if (field.IsRequired)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' is required for table {table.Name}");
                }
            }

            if (values.TryGetValue(field.Name, out var value))
            {
                ValidateFieldValue(field, value);
            }
        }

        return JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = false });
    }

    private static object? ExtractValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };

    private static void ValidateFieldValue(SFieldDefinition field, object? value)
    {
        if (value is null)
        {
            if (field.IsRequired)
            {
                throw new InvalidOperationException($"Field '{field.Name}' is required");
            }
            return;
        }

        switch (field.DataType)
        {
            case FieldDataType.Text:
            case FieldDataType.Note:
            case FieldDataType.Lookup:
            case FieldDataType.Tags:
            case FieldDataType.Json:
            case FieldDataType.File:
                if (value is not string)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects text content");
                }
                break;
            case FieldDataType.Number:
                if (value is not long && value is not int)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects an integer number");
                }
                break;
            case FieldDataType.Decimal:
                if (value is not double && value is not decimal && value is not float)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects a decimal number");
                }
                break;
            case FieldDataType.Boolean:
                if (value is not bool)
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects a boolean value");
                }
                break;
            case FieldDataType.Date:
            case FieldDataType.DateTime:
                if (value is not string dateString || !DateTimeOffset.TryParse(dateString, out _))
                {
                    throw new InvalidOperationException($"Field '{field.Name}' expects an ISO-8601 date/time string");
                }
                break;
            case FieldDataType.Calculated:
                // Calculated fields are validated by automation layer; accept any value but presence is optional.
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field.DataType), $"Unsupported data type {field.DataType}");
        }
    }

    private static IDictionary<string, string?>? ResolveViewFilter(string? filter, STable table)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var view = table.Views.FirstOrDefault(v => string.Equals(v.Name, filter, StringComparison.OrdinalIgnoreCase));
        if (view is null || string.IsNullOrWhiteSpace(view.QueryDefinition))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(view.QueryDefinition);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IDictionary<string, string?>? MergeEqualsFilters(IDictionary<string, string?>? original, IDictionary<string, string?>? viewFilter)
    {
        if (viewFilter is null || viewFilter.Count == 0)
        {
            return original;
        }

        if (original is null)
        {
            return new Dictionary<string, string?>(viewFilter, StringComparer.OrdinalIgnoreCase);
        }

        foreach (var kv in viewFilter)
        {
            if (!original.ContainsKey(kv.Key))
            {
                original[kv.Key] = kv.Value;
            }
        }

        return original;
    }
}

public sealed class NoteService : IAionNoteService, INoteService
{
    private readonly AionDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAudioTranscriptionProvider _transcriptionProvider;
    private readonly ISearchService _search;
    private readonly ILogger<NoteService> _logger;

    public NoteService(AionDbContext db, IFileStorageService fileStorage, IAudioTranscriptionProvider transcriptionProvider, ISearchService search, ILogger<NoteService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _transcriptionProvider = transcriptionProvider;
        _search = search;
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
        await _search.IndexNoteAsync(note, cancellationToken).ConfigureAwait(false);
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
    private readonly StorageOptions _options;
    private readonly AionDbContext _db;
    private readonly ISearchService _search;
    private readonly ILogger<FileStorageService> _logger;
    private readonly byte[] _encryptionKey;

    public FileStorageService(IOptions<StorageOptions> options, AionDbContext db, ISearchService search, ILogger<FileStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _storageRoot = _options.RootPath ?? throw new InvalidOperationException("Storage root path must be provided");
        _db = db;
        _search = search;
        _logger = logger;
        _encryptionKey = DeriveKey(_options.EncryptionKey ?? throw new InvalidOperationException("Storage encryption key missing"));
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<F_File> SaveAsync(string fileName, Stream content, string mimeType, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(_storageRoot, id.ToString());
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException($"File {fileName} exceeds the configured limit of {_options.MaxFileSizeBytes / (1024 * 1024)} MB");
        }

        await EnsureStorageQuotaAsync(buffer.Length, cancellationToken).ConfigureAwait(false);

        buffer.Position = 0;
        var hash = ComputeHash(buffer);
        buffer.Position = 0;

        var encryptedPayload = Encrypt(buffer.ToArray());
        await File.WriteAllBytesAsync(path, encryptedPayload, cancellationToken).ConfigureAwait(false);

        var file = new F_File
        {
            Id = id,
            FileName = fileName,
            MimeType = mimeType,
            StoragePath = path,
            Size = buffer.Length,
            Sha256 = hash,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await _db.Files.AddAsync(file, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("File {FileId} stored at {Path}", id, path);
        await _search.IndexFileAsync(file, cancellationToken).ConfigureAwait(false);
        return file;
    }

    public async Task<Stream> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var file = await _db.Files.FirstAsync(f => f.Id == fileId, cancellationToken).ConfigureAwait(false);
        var encryptedBytes = await File.ReadAllBytesAsync(file.StoragePath, cancellationToken).ConfigureAwait(false);
        var decrypted = Decrypt(encryptedBytes);

        if (_options.RequireIntegrityCheck && !string.IsNullOrWhiteSpace(file.Sha256))
        {
            var computedHash = ComputeHash(new MemoryStream(decrypted));
            if (!string.Equals(computedHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("File integrity validation failed");
            }
        }

        return new MemoryStream(decrypted, writable: false);
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

    private async Task EnsureStorageQuotaAsync(long incomingFileSize, CancellationToken cancellationToken)
    {
        var usedBytes = await _db.Files.SumAsync(f => f.Size, cancellationToken).ConfigureAwait(false);
        if (usedBytes + incomingFileSize > _options.MaxTotalBytes)
        {
            throw new InvalidOperationException("Storage quota exceeded; delete files before uploading new content.");
        }
    }

    private static byte[] DeriveKey(string material)
    {
        try
        {
            return Convert.FromBase64String(material);
        }
        catch (FormatException)
        {
            return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material));
        }
    }

    private byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_encryptionKey);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        return payload;
    }

    private byte[] Decrypt(ReadOnlySpan<byte> payload)
    {
        var nonce = payload[..12];
        var tag = payload[12..28];
        var cipher = payload[28..];
        var plaintext = new byte[cipher.Length];

        using var aes = new AesGcm(_encryptionKey);
        aes.Decrypt(nonce, cipher, tag, plaintext);
        return plaintext;
    }

    private static string ComputeHash(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexString(hash);
    }
}

public sealed class CloudBackupService : ICloudBackupService
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _backupFolder;
    private readonly BackupOptions _options;
    private readonly ILogger<CloudBackupService> _logger;

    public CloudBackupService(IOptions<BackupOptions> options, ILogger<CloudBackupService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _backupFolder = _options.BackupFolder ?? throw new InvalidOperationException("Backup folder must be configured");
        _logger = logger;
        Directory.CreateDirectory(_backupFolder);
    }

    public async Task BackupAsync(string encryptedDatabasePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(encryptedDatabasePath))
        {
            throw new FileNotFoundException("Database file not found", encryptedDatabasePath);
        }

        var databaseInfo = new FileInfo(encryptedDatabasePath);
        if (databaseInfo.Length > _options.MaxDatabaseSizeBytes)
        {
            throw new InvalidOperationException($"Database exceeds the maximum backup size of {_options.MaxDatabaseSizeBytes / (1024 * 1024)} MB");
        }

        var timestamp = DateTimeOffset.UtcNow;
        var backupFileName = $"aion-{timestamp:yyyyMMddHHmmss}.db";
        var destination = Path.Combine(_backupFolder, backupFileName);
        await using var source = new FileStream(encryptedDatabasePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var dest = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);

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
        if (_options.RequireIntegrityCheck && !string.Equals(computedHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
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
    private static readonly JsonSerializerOptions EmbeddingSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AionDbContext _db;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly ILogger<SearchService> _logger;

    public SearchService(AionDbContext db, ILogger<SearchService> logger, IServiceProvider serviceProvider)
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
        var entity = await _db.EntityTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == record.EntityTypeId, cancellationToken)
            .ConfigureAwait(false);

        var title = entity?.Name ?? $"Enregistrement {record.Id}";
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

        var notes = await _db.NoteSearch
            .Where(n => n.Content.Contains(query))
            .Select(n => new { n.NoteId, n.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        hits.AddRange(notes.Select(n => new SearchHit("Note", n.NoteId, $"Note {n.NoteId:N}", BuildSnippet(n.Content), 0.6)));

        var records = await _db.RecordSearch
            .Where(r => r.Content.Contains(query))
            .Select(r => new { r.RecordId, r.EntityTypeId, r.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in records)
        {
            hits.Add(new SearchHit("Record", record.RecordId, $"Enregistrement {record.EntityTypeId:N}", BuildSnippet(record.Content), 0.5));
        }

        var files = await _db.FileSearch
            .Where(f => f.Content.Contains(query))
            .Select(f => new { f.FileId, f.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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

public sealed class AutomationService : IAionAutomationService, IAutomationService
{
    private readonly AionDbContext _db;
    private readonly ILogger<AutomationService> _logger;
    private readonly IAutomationOrchestrator _orchestrator;

    public AutomationService(AionDbContext db, ILogger<AutomationService> logger, IAutomationOrchestrator orchestrator)
    {
        _db = db;
        _logger = logger;
        _orchestrator = orchestrator;
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

    public Task<IEnumerable<AutomationExecution>> TriggerAsync(string eventName, object payload, CancellationToken cancellationToken = default)
        => _orchestrator.TriggerAsync(eventName, payload, cancellationToken);
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
