# Infrastructure hardening notes

## Database lifecycle
- `EnsureAionDatabaseAsync` now applies EF Core migrations once at startup and validates that required tables (e.g., `Modules`) exist. If validation fails, startup stops instead of mixing `EnsureCreated`/`Migrate`, which avoids nondeterministic schema creation.
- Schema validation uses the EF-managed connection so SQLCipher pragmas are applied before checks, preventing plain-text opens.
- Existing migrations remain idempotent; FTS virtual tables and triggers rely on `IF NOT EXISTS` and `DROP TRIGGER IF EXISTS`, so repeated migration runs do not duplicate objects across platforms.

## SQLCipher key handling
- The SQLCipher key still comes from `Aion:Database:EncryptionKey`/`AION_DB_KEY` (or SecureStorage on MAUI). Connection strings are sanitized before use to strip any embedded passwords; the key is only applied via the encryption interceptor.
- The encryption interceptor now applies `PRAGMA key` with a parameterized command to keep the key out of command text and logs, and it enables `cipher_memory_security` on every open. No logs or exceptions emit the key.

## Background services on mobile
- `BackupOptions.EnableBackgroundServices` defaults to `false` on Android/iOS (true elsewhere). `BackupCleanupService` and `BackupSchedulerService` short-circuit when disabled, preventing unsupported/battery-heavy loops on mobile.
- Automated backups/cleanup require explicit opt-in on mobile platforms; manual backup/restore flows remain available.
