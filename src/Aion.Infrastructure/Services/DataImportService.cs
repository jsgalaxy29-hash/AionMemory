using System.IO.Compression;
using System.Text.Json;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aion.Infrastructure.Services;

public sealed class DataImportService : IDataImportService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AionDbContext _db;
    private readonly IModuleApplier _moduleApplier;
    private readonly IDataEngine _dataEngine;
    private readonly IFileStorageService _fileStorage;
    private readonly IStorageService _storage;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<DataImportService> _logger;
    private readonly ISecurityAuditService _securityAudit;

    public DataImportService(
        AionDbContext db,
        IModuleApplier moduleApplier,
        IDataEngine dataEngine,
        IFileStorageService fileStorage,
        IStorageService storage,
        IOptions<StorageOptions> storageOptions,
        ILogger<DataImportService> logger,
        ISecurityAuditService securityAudit)
    {
        _db = db;
        _moduleApplier = moduleApplier;
        _dataEngine = dataEngine;
        _fileStorage = fileStorage;
        _storage = storage;
        _storageOptions = storageOptions.Value;
        _logger = logger;
        _securityAudit = securityAudit;
    }

    public async Task<DataImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var workingFolder = await PrepareWorkingFolderAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        await using var cleanup = new CleanupFolder(workingFolder, sourcePath);

        var manifestPath = Path.Combine(workingFolder, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Export manifest not found", manifestPath);
        }

        DataExportManifest manifest;
        await using (var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            manifest = await JsonSerializer.DeserializeAsync<DataExportManifest>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Unable to deserialize export manifest");
        }

        if (!string.Equals(manifest.Version, ImportExportVersions.V1, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported export version {manifest.Version}. Expected {ImportExportVersions.V1}.");
        }

        var modulesFile = Path.Combine(workingFolder, manifest.ModulesFile);
        var recordsFile = Path.Combine(workingFolder, manifest.RecordsFile);
        var attachmentsFile = Path.Combine(workingFolder, manifest.AttachmentsFile);

        var tableMappings = await ImportModulesAsync(modulesFile, cancellationToken).ConfigureAwait(false);
        var tableLookup = await BuildTableLookupAsync(cancellationToken).ConfigureAwait(false);
        var (inserted, updated) = await ImportRecordsAsync(recordsFile, tableMappings, tableLookup, cancellationToken).ConfigureAwait(false);
        var (attachmentsImported, attachmentsLinked) = await ImportAttachmentsAsync(attachmentsFile, workingFolder, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Import completed: {Inserted} inserted, {Updated} updated, {Attachments} attachments ({Linked} links)",
            inserted,
            updated,
            attachmentsImported,
            attachmentsLinked);

        await _securityAudit.LogAsync(new SecurityAuditEvent(
            SecurityAuditCategory.DataImport,
            "data.import",
            metadata: new Dictionary<string, object?>
            {
                ["tableCount"] = manifest.TableCount,
                ["recordCount"] = manifest.RecordCount,
                ["attachmentCount"] = manifest.AttachmentCount,
                ["recordsInserted"] = inserted,
                ["recordsUpdated"] = updated,
                ["attachmentsImported"] = attachmentsImported,
                ["attachmentsLinked"] = attachmentsLinked
            }), cancellationToken).ConfigureAwait(false);

        return new DataImportResult
        {
            RecordsInserted = inserted,
            RecordsUpdated = updated,
            AttachmentsImported = attachmentsImported,
            AttachmentsLinked = attachmentsLinked,
            TableMappings = new Dictionary<Guid, Guid>(tableMappings)
        };
    }

    private async Task<Dictionary<Guid, Guid>> ImportModulesAsync(string modulesFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(modulesFile))
        {
            throw new FileNotFoundException("Modules file not found", modulesFile);
        }

        await using var stream = new FileStream(modulesFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var modules = await JsonSerializer.DeserializeAsync<List<ModuleSpec>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Invalid modules file");

        var mapping = new Dictionary<Guid, Guid>();

        foreach (var module in modules)
        {
            foreach (var table in module.Tables)
            {
                if (!table.Id.HasValue)
                {
                    table.Id = Guid.NewGuid();
                }

                var existing = await _db.Tables.AsNoTracking().FirstOrDefaultAsync(t => t.Name == table.Slug, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    mapping[table.Id.Value] = existing.Id;
                    table.Id = existing.Id;
                }
            }

            await _moduleApplier.ApplyAsync(module, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return mapping;
    }

    private async Task<(int Inserted, int Updated)> ImportRecordsAsync(
        string recordsFile,
        IReadOnlyDictionary<Guid, Guid> tableMappings,
        IReadOnlyDictionary<string, Guid> tableLookup,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(recordsFile))
        {
            throw new FileNotFoundException("Records file not found", recordsFile);
        }

        var inserted = 0;
        var updated = 0;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        await foreach (var line in ReadLinesAsync(recordsFile, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var payload = JsonSerializer.Deserialize<RecordImportPayload>(line, options);
            if (payload is null)
            {
                continue;
            }

            var targetTableId = ResolveTargetTable(payload.TableId, payload.Table, tableMappings, tableLookup);
            if (targetTableId is null)
            {
                _logger.LogWarning("Skipping record {RecordId} because table {Table} was not found", payload.RecordId, payload.Table);
                continue;
            }

            var existing = await _dataEngine.GetAsync(targetTableId.Value, payload.RecordId, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                await _dataEngine.InsertAsync(targetTableId.Value, payload.Data.GetRawText(), cancellationToken).ConfigureAwait(false);
                inserted++;
            }
            else
            {
                await _dataEngine.UpdateAsync(targetTableId.Value, payload.RecordId, payload.Data.GetRawText(), cancellationToken).ConfigureAwait(false);
                updated++;
            }
        }

        return (inserted, updated);
    }

    private async Task<(int Imported, int Linked)> ImportAttachmentsAsync(
        string attachmentsFile,
        string workingFolder,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(attachmentsFile))
        {
            return (0, 0);
        }

        await using var stream = new FileStream(attachmentsFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entries = await JsonSerializer.DeserializeAsync<List<AttachmentManifestEntry>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false)
            ?? new List<AttachmentManifestEntry>();

        var imported = 0;
        var linked = 0;

        foreach (var entry in entries)
        {
            var packagePath = Path.Combine(workingFolder, entry.PackagePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(packagePath))
            {
                _logger.LogWarning("Attachment payload missing for {FileId} at {PackagePath}", entry.FileId, packagePath);
                continue;
            }

            var existing = await _db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == entry.FileId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                await EnsureLinksAsync(entry, existing.Id, cancellationToken).ConfigureAwait(false);
                linked += entry.Links.Count;
                continue;
            }

            await using var payload = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var stored = await _storage.SaveAsync(entry.FileName, payload, cancellationToken).ConfigureAwait(false);
            if (_storageOptions.RequireIntegrityCheck &&
                !string.IsNullOrWhiteSpace(entry.Sha256) &&
                !string.Equals(stored.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Attachment integrity check failed for {entry.FileId}");
            }

            var file = new F_File
            {
                Id = entry.FileId,
                FileName = entry.FileName,
                MimeType = entry.MimeType,
                Size = entry.Size,
                Sha256 = stored.Sha256,
                StoragePath = stored.Path,
                UploadedAt = DateTimeOffset.UtcNow
            };

            _db.Files.Add(file);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await EnsureLinksAsync(entry, file.Id, cancellationToken).ConfigureAwait(false);
            linked += entry.Links.Count;
            imported++;
        }

        return (imported, linked);
    }

    private static Guid? ResolveTargetTable(Guid? sourceTableId, string? tableName, IReadOnlyDictionary<Guid, Guid> tableMappings, IReadOnlyDictionary<string, Guid> tableLookup)
    {
        if (sourceTableId.HasValue && tableMappings.TryGetValue(sourceTableId.Value, out var mapped))
        {
            return mapped;
        }

        if (!string.IsNullOrWhiteSpace(tableName))
        {
            if (tableLookup.TryGetValue(tableName, out var tableId))
            {
                return tableId;
            }
        }

        return null;
    }

    private async Task EnsureLinksAsync(AttachmentManifestEntry entry, Guid fileId, CancellationToken cancellationToken)
    {
        foreach (var link in entry.Links)
        {
            var exists = await _db.FileLinks
                .AsNoTracking()
                .AnyAsync(l => l.FileId == fileId && l.TargetId == link.TargetId && l.TargetType == link.TargetType && l.Relation == link.Relation, cancellationToken)
                .ConfigureAwait(false);
            if (exists)
            {
                continue;
            }

            await _fileStorage.LinkAsync(fileId, link.TargetType, link.TargetId, link.Relation, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is not null)
            {
                yield return line;
            }
        }
    }

    private static async Task<string> PrepareWorkingFolderAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (Directory.Exists(sourcePath))
        {
            return sourcePath;
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Import source not found", sourcePath);
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), $"aion-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);

        await using var archiveStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);

        archive.ExtractToDirectory(tempFolder);
        return tempFolder;
    }

    private sealed record RecordImportPayload
    {
        public string Table { get; init; } = string.Empty;
        public Guid TableId { get; init; }
        public Guid RecordId { get; init; }
        public JsonElement Data { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? UpdatedAt { get; init; }
        public DateTimeOffset? DeletedAt { get; init; }
    }

    private async Task<Dictionary<string, Guid>> BuildTableLookupAsync(CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var tables = await _db.Tables.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var table in tables)
        {
            lookup[table.Name] = table.Id;
        }

        return lookup;
    }

    private sealed class CleanupFolder : IAsyncDisposable
    {
        private readonly string _folder;
        private readonly string _sourcePath;

        public CleanupFolder(string folder, string sourcePath)
        {
            _folder = folder;
            _sourcePath = sourcePath;
        }

        public ValueTask DisposeAsync()
        {
            if (!Directory.Exists(_folder))
            {
                return ValueTask.CompletedTask;
            }

            if (string.Equals(_folder, _sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }

            return ValueTask.CompletedTask;
        }
    }
}
