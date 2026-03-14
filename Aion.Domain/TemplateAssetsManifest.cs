using System;
using System.Collections.ObjectModel;

namespace Aion.Domain;

public sealed class TemplateAssetsManifest
{
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public Collection<TemplateAsset> Assets { get; } = new();
}

public sealed class TemplateAsset
{
    public string LogicalName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? MimeType { get; set; }
}
