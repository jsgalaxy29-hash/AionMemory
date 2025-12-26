using System;

namespace Aion.Domain;

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
    public bool EncryptPayloads { get; set; } = true;
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
    public bool EnableBackgroundServices { get; set; } = !(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());
}

public sealed record CloudBackupOptions
{
    public bool Enabled { get; set; }
    public string? Endpoint { get; set; }
    public string? Bucket { get; set; }
    public string? Region { get; set; } = "us-east-1";
    public string? AccessKeyId { get; set; }
    public string? SecretAccessKey { get; set; }
    public string? Prefix { get; set; }
    public bool UsePathStyle { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int MaxBackups { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
    public int RetryBaseDelayMs { get; set; } = 500;
}

public sealed record AutomationSchedulerOptions
{
    public bool EnableBackgroundServices { get; set; } = !(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());
    public int PollingIntervalSeconds { get; set; } = 60;
}
