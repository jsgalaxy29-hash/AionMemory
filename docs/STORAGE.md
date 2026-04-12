# Storage & Backup

This document outlines how persistent storage is handled for attachments and database backups in AionMemory.

## Storage service

- **Implementation:** `Aion.Infrastructure.Services.StorageService` (`IStorageService` contract).
- **Root:** `Aion:Storage:RootPath` (defaults to `data/storage`); all paths stored in the database are **relative** to this root and validated to stay inside it.
- **Layout:** files are saved under `attachments/<prefix>/<segment>/<id>.<ext>` to keep a shallow, stable folder structure.
- **Integrity:** payloads are hashed with SHA-256 (plaintext) when saved; hashes are verified on read when `StorageOptions.RequireIntegrityCheck` is enabled.
- **Encryption:** controlled by `StorageOptions.EncryptPayloads` (default **true**). When enabled, payloads are wrapped with AES-GCM using the configured storage key. When disabled, only the SQLCipher-encrypted database protects metadata—document this choice in configuration.
- **Quota:** `StorageOptions.MaxFileSizeBytes` and `MaxTotalBytes` guard uploads (enforced by `FileStorageService` before writing metadata).

### File storage

`FileStorageService` uses `IStorageService` to persist attachment streams while indexing metadata in the database. Stored paths remain relative; integrity verification uses the stored hash plus the storage option.

## Backup & restore

- **Service:** `BackupService` creates snapshot folders under the configured backup directory (default `storage/backup`).
- **Snapshot contents:**
  - `database.db` (or `database.db.enc` when `encrypt=true`) copied from the SQLCipher database.
  - `storage.zip` containing the storage root **excluding** the backup folder itself.
  - Manifest `<snapshot>.json` at the backup root referencing both artifacts (relative paths), hashes, sizes, and the storage root used during creation.
- **Restore:** `RestoreService` validates hashes when `BackupOptions.RequireIntegrityCheck` is on, restores the database transactionally (`.restoring` → swap), then restores storage to a temporary folder before swapping it into place. Existing database and storage locations are renamed with a `.bak` suffix before replacement.
- **Cleanup:** `BackupCleanupService` trims old backups by manifest date/retention and removes associated artifacts (database copy, storage archive, snapshot folder).

## Encryption notes

- The primary database remains encrypted with SQLCipher.
- Storage payload encryption is configurable via `StorageOptions.EncryptPayloads`. When disabled, ensure the storage root sits on trusted storage and rely on SQLCipher for database content encryption.
