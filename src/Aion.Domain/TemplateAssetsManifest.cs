using System;
using System.Collections.Generic;

namespace Aion.Domain;

public sealed class TemplateAssetsManifest
{
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<TemplateAsset> Assets { get; set; } = new();
}

public sealed class TemplateAsset
{
    public string LogicalName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? MimeType { get; set; }
}
