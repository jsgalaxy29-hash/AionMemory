# AION Memory Skeleton

AION is a modular personal memory stack built with .NET 10. This repository provides a skeleton implementation that wires together domain models, infrastructure services, AI provider stubs, and a console host meant to evolve into a MAUI Blazor Hybrid front end.

## Projects

- **Aion.Domain**: All entities and service contracts (metadata, data engine, notes with dictation, automation, vision, life logging, dashboards, templates/marketplace, prediction, persona engine).
- **Aion.Infrastructure**: EF Core `AionDbContext`, SQLCipher-ready SQLite helper, service implementations, and a Potager demo seed.
- **Aion.AI**: Stub implementations for LLM, embeddings, transcription, intent detection, module design, CRUD/report interpreters, and vision placeholders.
- **Aion.AppHost**: Console bootstrap that wires DI, seeds demo data, and hosts Razor components for dynamic lists/forms, dictation, timeline, and dashboard sketches.

## Running

```bash
 dotnet restore
 dotnet build
 dotnet run --project src/Aion.AppHost/Aion.AppHost.csproj
```

The app bootstraps an encrypted SQLite database (`aion.db`) and seeds the Potager module with sample data. Storage is written under the application directory.

## Configuration Notes

- SQLCipher key must be supplied via configuration (`AION_DB_KEY` env var) and is injected into the DbContext through `AionDatabaseOptions`.
- Marketplace packages are serialized under the `marketplace` folder; backups are stored under `storage/backup`.
- Replace the stub AI providers in `Aion.AI` with real integrations (OpenAI, Mistral, local models) by implementing the interfaces defined in `Aion.Domain`.

## Extensibility

- Add EF Core migrations from `Aion.Infrastructure` once schema stabilizes.
- Expand the dynamic Razor components to render field types, validation, and automation triggers.
- Plug in dashboard widgets and LifeGraph visualizations directly using the models defined in the domain layer.
