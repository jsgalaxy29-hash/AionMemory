# ARCHITECTURE FREEZE V1

Version: **v1.0.0-alpha**  
Date: **2025-12-25**

## Objectif
Établir une base stable pour faire évoluer la solution sans dette : contrats publics figés, surface API stable, et règles de versioning associées.

## API publiques stables
Les surfaces suivantes sont considérées **stables**. Toute modification impose un **bump de version** (voir section “Versioning & changement de contrat”).

### Contrats Domain
Fichiers de référence (tous dans `src/Aion.Domain`):
- `ServiceContracts.cs` (IAionDataEngine, IDataEngine, INoteService, IAgendaService, IAutomationService, ISyncEngine, etc.)
- `StorageContracts.cs` (IStorageService)
- `TenancyContracts.cs` (ITenancyService, IWorkspaceContext, IWorkspaceContextAccessor)
- `ImportExportContracts.cs` (IDataExportService, IDataImportService)
- `Extensions.cs` (IExtensionCatalog, IExtensionState)
- `Observability.cs` (IOperationScope, IOperationScopeFactory)
- `ModuleBuilder/ModuleBuilderContracts.cs` (IModuleSpecDesigner, IModuleValidator, IModuleApplier)
- `AI/AiContracts.cs` (IIntentDetector, IModuleDesigner, ICrudInterpreter, IAionVisionService, etc.)

### Contrats UI/Infrastructure exposés
- Les projets UI/Infrastructure **consomment** les contrats Domain, mais n’en définissent pas de nouveaux.
- Tout contrat additionnel doit être défini dans `Aion.Domain`.

## Contrats figés
Les points suivants sont **figés** pour V1 :
- Signatures publiques des interfaces listées ci-dessus.
- DTOs publics associés aux contrats Domain (types publics utilisés par les interfaces Domain).
- Règles de nullabilité et contraintes implicites exprimées par les contrats.

## Ce qui peut encore changer
- Implémentations dans `Aion.Infrastructure` (EF Core, SQLCipher, services internes).
- Providers IA dans `Aion.AI` et `Aion.AI.Providers.*`.
- UI/UX et navigation dans `Aion.AppHost` (sous réserve de respecter les contrats Domain).
- Tests, scripts, pipelines, tooling, documentation (hors versioning des contrats).

## Versioning & changement de contrat
- **Toute modification** des contrats Domain listés ci-dessus doit **bump la version** dans ce document.
- Le bump doit être explicite (ex: `v1.0.1-alpha`, `v1.1.0`, etc.).
- Les checks CI empêchent la modification des contrats sans mise à jour de ce document.

## Règles CI
- Si un fichier sous `src/Aion.Domain/` change, `docs/ARCHITECTURE_FREEZE_V1.md` doit être modifié dans le même commit.
- Cette règle s’applique aux PR et aux pushes sur branches feature.
