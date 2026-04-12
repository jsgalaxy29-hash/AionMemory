# Aion layered refactor notes

## Existing solution structure
- Solution projects (`AionMemory.slnx`): Aion.Domain, Aion.Infrastructure, Aion.AI, AionMemory (MAUI/Blazor host) with related test projects.【F:AionMemory.slnx†L1-L15】
- The meta-model (tables, fields, views) currently sits in `Aion.Domain` within `Entities.cs` (classes `STable`, `SFieldDefinition`, `SViewDefinition`).【F:Aion.Domain/Entities.cs†L379-L470】
- DataEngine contracts are declared in `Aion.Domain/ServiceContracts.cs` and implemented in `Aion.Infrastructure/Services/CoreServices.cs` (class `AionDataEngine`).【F:Aion.Infrastructure/Services/CoreServices.cs†L52-L232】【F:Aion.Domain/ServiceContracts.cs†L8-L77】
- File storage/GED surface lives in `IFileStorageService` in the domain contracts and is implemented by `FileStorageService` in infrastructure.【F:Aion.Domain/ServiceContracts.cs†L56-L77】【F:Aion.Infrastructure/Services/CoreServices.cs†L823-L869】
- AI orchestrators (CRUD/query/report/vision) currently sit in `Aion.AI/Providers.cs`, mixing contracts and implementations against domain services.【F:Aion.AI/Providers.cs†L1-L880】
- UI components in `AionMemory` directly inject `IDataEngine` into Razor components (`DynamicForm`, `DynamicList`, dashboard pages, etc.).【F:AionMemory/Components/DynamicForm.razor†L57-L114】【F:AionMemory/Components/DynamicList.razor†L57-L120】

## Coupling observations
- Domain/meta-model is bundled with EF-facing infrastructure concerns (DataEngine implementation depends on EF `AionDbContext`), preventing a pure Core layer.【F:Aion.Infrastructure/Services/CoreServices.cs†L52-L232】
- AI implementations reference domain services directly (`VisionEngine` uses `IFileStorageService`, interpreters build domain entities), so AI contracts are not isolated from concrete infrastructure choices.【F:Aion.AI/Providers.cs†L820-L869】
- UI components talk straight to `IDataEngine` and entity types, so presentation logic is tightly bound to persistence models rather than interface façades like `ICoreEngine`.【F:AionMemory/Components/DynamicForm.razor†L57-L114】【F:AionMemory/Components/DynamicList.razor†L57-L120】

## Proposed target structure
- **Aion.Core**: new class library containing the meta-model (`STable`, `SFieldDefinition`, `SViewDefinition`), base entities/value objects, repository/data-engine contracts, GED abstractions (`IDocumentStore`, `IStorageProvider`, `IEncryptionService`), and a façade such as `ICoreEngine`/`IDataEngine`.
- **Aion.AI**: class library with AI contracts (`ICrudInterpreter`, `IQueryInterpreter`, `IModuleDesigner`, `IReportInterpreter`, `IVisionService`, `IActionRouter`) that depend only on `Aion.Core` models/contracts.
- **Aion.AI.OpenAI** (and other providers): concrete AI implementations that reference `Aion.AI` + `Aion.Core` but nothing in UI.
- **Aion.Front.Maui / Aion.Front.Blazor**: UI hosts consuming only `Aion.Core` and `Aion.AI` interfaces via DI; no direct EF/AI SDK usage.

## Initial refactor plan
1. **Create Aion.Core project** and move the meta-model, domain entities, and service contracts from `Aion.Domain`; leave infrastructure-specific implementations in separate namespaces to keep the core clean.
2. **Split AI contracts** out of `Aion.AI/Providers.cs` into explicit interfaces under `Aion.AI` and relocate concrete logic (including OpenAI/Mistral, etc.) into provider-specific projects such as `Aion.AI.OpenAI`.
3. **Introduce a Core façade** (`ICoreEngine`) aggregating CRUD/query/module/document operations so UI layers depend on a single orchestrator rather than concrete `IDataEngine` or EF types.
4. **Add an `IActionRouter`** that coordinates AI intents with Core services without UI references; wire it through DI in UI hosts.
5. **Refactor UI components/pages** to consume the new façade interfaces (e.g., via DI adapters) instead of accessing `IDataEngine` directly; replace entity references with DTOs where possible.
6. **Update solution references** to enforce layering: `Aion.Core` has no UI/AI references; `Aion.AI` depends on `Aion.Core`; UI projects depend only on these abstractions.

This document serves as the starting point for the layered refactor so subsequent changes can be organized and tracked.
