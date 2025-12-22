using System.IO.Compression;
using System.Text.Json;
using Aion.Domain;
using Aion.Domain.ModuleBuilder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aion.Infrastructure.Services;

public sealed class DataExportService : IDataExportService
{
    private const string ManifestFileName = "manifest.json";
    private const string ModulesFileName = "modules.json";
    private const string RecordsFileName = "records.ndjson";
    private const string AttachmentsFileName = "attachments.json";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AionDbContext _db;
    private readonly IStorageService _storage;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<DataExportService> _logger;

    public DataExportService(
        AionDbContext db,
        IStorageService storage,
        IOptions<StorageOptions> storageOptions,
        ILogger<DataExportService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
        _storageOptions = storageOptions.Value;

        if (string.IsNullOrWhiteSpace(_storageOptions.RootPath))
        {
            throw new InvalidOperationException("Storage root path must be configured for export operations");
        }
    }

    public async Task<DataExportResult> ExportAsync(string destinationPath, bool asArchive = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var workingFolder = asArchive
            ? Path.Combine(Path.GetTempPath(), $"aion-export-{Guid.NewGuid():N}")
            : destinationPath;

        Directory.CreateDirectory(workingFolder);

        var tables = await _db.Tables
            .Include(t => t.Fields)
            .Include(t => t.Views)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var records = await _db.Records
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var files = await _db.Files.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var links = await _db.FileLinks.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        var modules = BuildModuleSpecs(tables);

        await WriteModulesAsync(modules, Path.Combine(workingFolder, ModulesFileName), cancellationToken).ConfigureAwait(false);
        await WriteRecordsAsync(tables, records, Path.Combine(workingFolder, RecordsFileName), cancellationToken).ConfigureAwait(false);
        var attachmentCount = await WriteAttachmentsAsync(files, links, workingFolder, cancellationToken).ConfigureAwait(false);

        var manifest = new DataExportManifest
        {
            ExportedAt = DateTimeOffset.UtcNow,
            ModuleCount = modules.Count,
            TableCount = tables.Count,
            RecordCount = records.Count,
            AttachmentCount = attachmentCount,
            ModulesFile = ModulesFileName,
            RecordsFile = RecordsFileName,
            AttachmentsFile = AttachmentsFileName,
            IsArchive = asArchive,
            Tables = tables.ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase)
        };

        var manifestPath = Path.Combine(workingFolder, ManifestFileName);
        await using (var manifestStream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        string packagePath;
        if (asArchive)
        {
            packagePath = NormalizeArchivePath(destinationPath);
            if (File.Exists(packagePath))
            {
                File.Delete(packagePath);
            }

            ZipFile.CreateFromDirectory(workingFolder, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            _logger.LogInformation("Data export archive created at {PackagePath}", packagePath);
            Directory.Delete(workingFolder, recursive: true);
        }
        else
        {
            packagePath = workingFolder;
            _logger.LogInformation("Data export folder created at {PackagePath}", packagePath);
        }

        return new DataExportResult(manifest, packagePath);
    }

    private static List<ModuleSpec> BuildModuleSpecs(IEnumerable<STable> tables)
    {
        var module = new ModuleSpec
        {
            Slug = "aion-export",
            DisplayName = "Aion Export",
            Tables = tables.Select(MapTable).ToList()
        };

        return new List<ModuleSpec> { module };
    }

    private static TableSpec MapTable(STable table)
    {
        return new TableSpec
        {
            Id = table.Id,
            Slug = table.Name,
            DisplayName = table.DisplayName,
            Description = table.Description,
            IsSystem = table.IsSystem,
            SupportsSoftDelete = table.SupportsSoftDelete,
            HasAuditTrail = table.HasAuditTrail,
            DefaultView = table.DefaultView,
            RowLabelTemplate = table.RowLabelTemplate,
            Fields = table.Fields.Select(MapField).ToList(),
            Views = table.Views.Select(MapView).ToList()
        };
    }

    private static FieldSpec MapField(SFieldDefinition field)
    {
        return new FieldSpec
        {
            Id = field.Id,
            Slug = field.Name,
            Label = field.Label,
            DataType = MapDataType(field.DataType),
            IsRequired = field.IsRequired,
            IsSearchable = field.IsSearchable,
            IsListVisible = field.IsListVisible,
            IsPrimaryKey = field.IsPrimaryKey,
            IsUnique = field.IsUnique,
            IsIndexed = field.IsIndexed,
            IsFilterable = field.IsFilterable,
            IsSortable = field.IsSortable,
            IsHidden = field.IsHidden,
            IsReadOnly = field.IsReadOnly,
            IsComputed = field.IsComputed,
            DefaultValue = DeserializeDefault(field.DefaultValue),
            EnumValues = string.IsNullOrWhiteSpace(field.EnumValues) ? null : field.EnumValues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Lookup = string.IsNullOrWhiteSpace(field.LookupTarget)
                ? null
                : new LookupSpec
                {
                    TargetTableSlug = field.LookupTarget!,
                    LabelField = field.LookupField
                },
            ComputedExpression = field.ComputedExpression,
            MinLength = field.MinLength,
            MaxLength = field.MaxLength,
            MinValue = field.MinValue,
            MaxValue = field.MaxValue,
            ValidationPattern = field.ValidationPattern,
            Placeholder = field.Placeholder,
            Unit = field.Unit
        };
    }

    private static ViewSpec MapView(SViewDefinition view)
    {
        return new ViewSpec
        {
            Id = view.Id,
            Slug = view.Name,
            DisplayName = view.DisplayName,
            Description = view.Description,
            Filter = DeserializeFilter(view.QueryDefinition),
            Sort = view.SortExpression,
            PageSize = view.PageSize,
            Visualization = view.Visualization,
            IsDefault = view.IsDefault
        };
    }

    private async Task WriteModulesAsync(IEnumerable<ModuleSpec> modules, string destination, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, modules, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteRecordsAsync(IEnumerable<STable> tables, IEnumerable<F_Record> records, string destination, CancellationToken cancellationToken)
    {
        var tableLookup = tables.ToDictionary(t => t.Id);
        await using var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream);

        foreach (var record in records)
        {
            if (!tableLookup.TryGetValue(record.TableId, out var table))
            {
                continue;
            }

            var payload = new
            {
                table = table.Name,
                tableId = table.Id,
                recordId = record.Id,
                data = JsonSerializer.Deserialize<JsonElement>(record.DataJson),
                createdAt = record.CreatedAt,
                updatedAt = record.UpdatedAt,
                deletedAt = record.DeletedAt
            };

            var line = JsonSerializer.Serialize(payload, SerializerOptions);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> WriteAttachmentsAsync(IEnumerable<F_File> files, IEnumerable<F_FileLink> links, string workingFolder, CancellationToken cancellationToken)
    {
        var root = _storageOptions.RootPath!;
        var attachmentEntries = new List<AttachmentManifestEntry>();
        var attachmentsRoot = Path.Combine(workingFolder, "attachments");
        Directory.CreateDirectory(attachmentsRoot);

        foreach (var file in files)
        {
            var packagePath = Path.Combine("attachments", file.Id.ToString("N"), file.FileName);
            var targetPath = Path.Combine(workingFolder, packagePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            try
            {
                await using var source = await _storage.OpenReadAsync(file.StoragePath, _storageOptions.RequireIntegrityCheck ? file.Sha256 : null, cancellationToken).ConfigureAwait(false);
                await using var dest = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to export attachment {FileId} at {StoragePath}", file.Id, file.StoragePath);
                continue;
            }

            var linked = links
                .Where(l => l.FileId == file.Id)
                .Select(l => new AttachmentLink
                {
                    TargetType = l.TargetType,
                    TargetId = l.TargetId,
                    Relation = l.Relation
                })
                .ToList();

            attachmentEntries.Add(new AttachmentManifestEntry
            {
                FileId = file.Id,
                FileName = file.FileName,
                MimeType = file.MimeType,
                Size = file.Size,
                Sha256 = file.Sha256,
                StoragePath = file.StoragePath,
                PackagePath = packagePath.Replace(Path.DirectorySeparatorChar, '/'),
                Links = linked
            });
        }

        var manifestPath = Path.Combine(workingFolder, AttachmentsFileName);
        await using (var stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, attachmentEntries, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        return attachmentEntries.Count;
    }

    private static string MapDataType(FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Number or FieldDataType.Int => ModuleFieldDataTypes.Number,
            FieldDataType.Decimal => ModuleFieldDataTypes.Decimal,
            FieldDataType.Boolean => ModuleFieldDataTypes.Boolean,
            FieldDataType.Date => ModuleFieldDataTypes.Date,
            FieldDataType.DateTime => ModuleFieldDataTypes.DateTime,
            FieldDataType.Enum => ModuleFieldDataTypes.Enum,
            FieldDataType.Lookup => ModuleFieldDataTypes.Lookup,
            FieldDataType.File => ModuleFieldDataTypes.File,
            FieldDataType.Note => ModuleFieldDataTypes.Note,
            FieldDataType.Json => ModuleFieldDataTypes.Json,
            FieldDataType.Tags => ModuleFieldDataTypes.Tags,
            _ => ModuleFieldDataTypes.Text
        };
    }

    private static JsonElement? DeserializeDefault(string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(defaultValue);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse(JsonSerializer.Serialize(defaultValue)).RootElement.Clone();
        }
    }

    private static Dictionary<string, string?>? DeserializeFilter(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(definition);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeArchivePath(string destinationPath)
    {
        if (Path.GetExtension(destinationPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return destinationPath;
        }

        return Path.HasExtension(destinationPath)
            ? Path.ChangeExtension(destinationPath, ".zip")
            : Path.Combine(destinationPath, "export.zip");
    }
}
