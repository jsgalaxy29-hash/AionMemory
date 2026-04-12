namespace Aion.Domain;

public sealed record KeyRotationResult(BackupManifest Backup, DateTimeOffset RotatedAt, string DatabasePath);
