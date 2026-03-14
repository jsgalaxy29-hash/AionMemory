namespace Aion.Domain;

public sealed record CloudBackupEntry
{
    public string Id { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed record CloudRestorePreview
{
    public CloudBackupEntry Backup { get; init; } = new();
    public IReadOnlyList<string> Entries { get; init; } = Array.Empty<string>();
    public bool Applied { get; init; }
}
