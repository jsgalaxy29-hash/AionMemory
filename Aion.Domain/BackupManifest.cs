using System.Text.Json.Serialization;

namespace Aion.Domain;

public sealed record BackupManifest
{
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("encrypted")]
    public bool IsEncrypted { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; } = string.Empty;

    [JsonPropertyName("storageArchive")]
    public string? StorageArchivePath { get; set; }

    [JsonPropertyName("storageSha256")]
    public string? StorageSha256 { get; set; }

    [JsonPropertyName("storageSize")]
    public long? StorageSize { get; set; }

    [JsonPropertyName("storageRoot")]
    public string? StorageRoot { get; set; }
}
