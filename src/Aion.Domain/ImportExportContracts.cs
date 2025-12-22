using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Aion.Domain;

public static class ImportExportVersions
{
    public const string V1 = "1.0";
}

public sealed record AttachmentManifestEntry
{
    [JsonPropertyName("fileId")]
    public Guid FileId { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("storagePath")]
    public string StoragePath { get; set; } = string.Empty;

    [JsonPropertyName("packagePath")]
    public string PackagePath { get; set; } = string.Empty;

    [JsonPropertyName("links")]
    public List<AttachmentLink> Links { get; set; } = new();
}

public sealed record AttachmentLink
{
    [JsonPropertyName("targetType")]
    public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("targetId")]
    public Guid TargetId { get; set; }

    [JsonPropertyName("relation")]
    public string? Relation { get; set; }
}

public sealed record DataExportManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = ImportExportVersions.V1;

    [JsonPropertyName("exportedAt")]
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("moduleCount")]
    public int ModuleCount { get; set; }

    [JsonPropertyName("tableCount")]
    public int TableCount { get; set; }

    [JsonPropertyName("recordCount")]
    public int RecordCount { get; set; }

    [JsonPropertyName("attachmentCount")]
    public int AttachmentCount { get; set; }

    [JsonPropertyName("modulesFile")]
    public string ModulesFile { get; set; } = "modules.json";

    [JsonPropertyName("recordsFile")]
    public string RecordsFile { get; set; } = "records.ndjson";

    [JsonPropertyName("attachmentsFile")]
    public string AttachmentsFile { get; set; } = "attachments.json";

    [JsonPropertyName("isArchive")]
    public bool IsArchive { get; set; }

    [JsonPropertyName("tables")]
    public Dictionary<string, Guid> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record DataExportResult(DataExportManifest Manifest, string PackagePath);

public sealed record DataImportResult
{
    public int RecordsInserted { get; init; }

    public int RecordsUpdated { get; init; }

    public int AttachmentsImported { get; init; }

    public int AttachmentsLinked { get; init; }

    public IReadOnlyDictionary<Guid, Guid> TableMappings { get; init; } = new Dictionary<Guid, Guid>();
}

public interface IDataExportService
{
    Task<DataExportResult> ExportAsync(string destinationPath, bool asArchive = false, CancellationToken cancellationToken = default);
}

public interface IDataImportService
{
    Task<DataImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}
