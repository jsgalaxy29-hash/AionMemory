namespace Aion.Infrastructure;

public sealed record AionDatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string EncryptionKey { get; set; } = string.Empty;
}

public sealed record StorageOptions
{
    public string? RootPath { get; set; }
}

public sealed record MarketplaceOptions
{
    public string? MarketplaceFolder { get; set; }
}

public sealed record BackupOptions
{
    public string? BackupFolder { get; set; }
    public int RetentionDays { get; set; } = 30;
    public int MaxBackups { get; set; } = 10;
}
