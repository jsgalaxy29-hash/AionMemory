namespace Aion.Infrastructure;

public sealed record AionDatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string EncryptionKey { get; set; } = string.Empty;
}

public sealed record StorageOptions
{
    public string? RootPath { get; set; }
    public string? EncryptionKey { get; set; }
    public long MaxTotalBytes { get; set; } = 512L * 1024 * 1024;
    public long MaxFileSizeBytes { get; set; } = 50L * 1024 * 1024;
    public bool RequireIntegrityCheck { get; set; } = true;
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
    public long MaxDatabaseSizeBytes { get; set; } = 1024L * 1024 * 1024;
    public bool AutoRestoreLatest { get; set; }
    public bool RequireIntegrityCheck { get; set; } = true;
    public int BackupIntervalMinutes { get; set; } = 24 * 60;
}
