# AION Memory

AION is a modular personal memory stack built with .NET 10. This repository provides a MAUI Blazor Hybrid host wired to domain models, infrastructure services, AI providers and a composition root used at startup.

## Projects

- **Aion.Domain**: entities, value objects and service contracts.
- **Aion.Infrastructure**: EF Core `AionDbContext`, SQLCipher-ready SQLite support, migrations and service implementations.
- **Aion.AI**: AI orchestration, provider selection, mock/offline providers, interpreters and module tooling.
- **Aion.Composition**: composition root that registers infrastructure, AI services and platform defaults through `AddAionCore`.
- **Aion.AppHost**: MAUI Blazor Hybrid host that runs first-launch setup, initializes the database and serves the UI.

## Running

```bash
dotnet restore
dotnet build
dotnet build Aion.AppHost/Aion.AppHost.csproj -t:Run -f net10.0-windows10.0.19041.0
```

The app bootstraps an encrypted SQLite database (`aion.db`), applies migrations and seeds the Potager demo module when the database is initialized. In the MAUI host, storage is written under `FileSystem.AppDataDirectory`.

## Usage notes

1. **Configure the encrypted database**
   - use `AION_DB_KEY` or `Aion:Database:EncryptionKey` when you want an explicit key from configuration;
   - in the MAUI host, the first-run flow can also persist the key locally through `SecureStorage`;
   - storage, marketplace and backup folders are created automatically when defaults are used.

2. **Launch the MAUI host**
   - `dotnet build Aion.AppHost/Aion.AppHost.csproj -t:Run -f <target>`;
   - `MauiProgram` configures options, secure local storage, UI services and the application composition root.

3. **First launch**
   - the `/setup` page asks for the local database path, the encryption key and the profile name;
   - those values are stored locally through `Preferences` and `SecureStorage`.

## Security and compliance

- **Encryption**: SQLCipher is applied when the database connection opens.
- **Storage surface**: storage, marketplace and backup folders are isolated and validated by infrastructure options.
- **Secrets**: do not commit secrets; use environment variables, local secure storage or user secrets.
- **Traceability**: sensitive operations should remain observable through structured logging and audit services.

## AI support

- **Embeddings and semantic search**: available through domain contracts and AI services.
- **LLM and assistants**: report generation, summaries, CRUD/report interpretation and routing.
- **Transcription and vision**: available through provider abstractions with mock/offline fallbacks.
- **Automation**: orchestrated through domain and infrastructure services so actions stay auditable.

## Extensibility

- Add or evolve EF Core migrations from `Aion.Infrastructure`.
- Extend the dynamic Razor components and module runtime.
- Plug in additional AI providers by implementing the domain contracts already used by the app.
