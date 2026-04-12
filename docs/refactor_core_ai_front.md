# Aion layering audit (Core / AI / UI)

- Projects in solution: Aion.Core (domain/meta-model & service contracts), Aion.AI (AI orchestrators/providers), Aion.AI.OpenAI (OpenAI-specific providers), Aion.Infrastructure (EF Core + services), AionMemory (MAUI/Blazor UI), AionMemory.Logic (UI helpers), plus test projects.
- Meta-model & entities (STable, SFieldDefinition, SViewDefinition, S_Module, S_Field, relations, notes, events, files) now live in **Aion.Core/Entities.cs**.
- Core service contracts (IDataEngine, IMetadataService, INoteService, IAgendaService, automation/dashboard/template/backup/search etc.) are in **Aion.Core/ServiceContracts.cs**; they are referenced by infrastructure and UI.
- DataEngine implementation and other concrete services remain in **Aion.Infrastructure/Services/CoreServices.cs** with EF DbContext in **Aion.Infrastructure/AionDbContext.cs**, keeping UI coupled to persistence via IDataEngine injections.
- AI contracts (intent detection, CRUD/query/report interpreters, module designer, vision, LLM/embeddings/transcription DTOs) now live in **Aion.AI/AiContracts.cs** so UI/infra depend on the AI layer rather than Core.
- Generic HTTP + Mistral AI providers and orchestrators stay in **Aion.AI**; OpenAI-specific providers are isolated in **Aion.AI.OpenAI** with an extension to wire them into DI.
- UI components/pages in **AionMemory/Components** still inject `IDataEngine` directly (DynamicForm, DynamicList, dashboard pages), so presentation remains tightly coupled to persistence contracts.
- AI usage from UI (Chat/Home pages) now routes through `IIntentDetector` imported from Aion.AI, but front-end continues to configure AI providers directly in `MauiProgram`.
- Infrastructure adapters (**Aion.Infrastructure/Adapters/AiAdapters.cs**) provide stub AI implementations and still directly reference LLM/embedding interfaces.
- Coupling issues: UI depends on DataEngine and domain entities; infrastructure mixes domain + AI implementations; open AI wiring requires explicit DI calls from UI; no façade yet separates UI from persistence/AI orchestration.
- Next steps: move GED/storage abstractions into Core, add façade services for UI, and migrate any remaining AI logic out of infrastructure/UI.
