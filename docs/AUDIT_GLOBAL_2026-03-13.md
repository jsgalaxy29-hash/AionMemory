# Audit global de la solution

_Date : 2026-03-13_

## 1. Build / structure
- **Structure solution** : `AionMemory.slnx` référence Domain, Infrastructure, AI, Composition, RecoveryTool, AppHost et l’ensemble des projets de tests/benchmarks (périmètre complet présent).【F:AionMemory.slnx†L1-L18】
- **SDK/toolchain** : la solution est verrouillée sur un SDK **preview** `.NET 10.0.100-preview.6` avec `allowPrerelease=true`, ce qui augmente le risque de volatilité CI/CD et de breakages liés à l’outillage en avance de phase.【F:global.json†L1-L7】
- **Qualité build** : nullabilité, analyseurs et règles de style sont activés globalement, mais `TreatWarningsAsErrors` est conditionné à `CI=true` dans `Directory.Build.props`. En local, des warnings peuvent passer sans bloquer le build si les projets ne forcent pas déjà l’option côté `.csproj`.【F:Directory.Build.props†L2-L20】
- **Gestion des dépendances** : versions NuGet centralisées (bon point de gouvernance) ; présence mixte `10.0.0` et `10.0.11` pour MAUI/WebView qui peut nécessiter vigilance sur la compatibilité runtime/workloads.【F:Directory.Packages.props†L3-L32】
- **AppHost multi-cibles MAUI** : Android/iOS/MacCatalyst (+ Windows conditionnel) → structure cohérente avec cible MAUI Blazor Hybrid, mais dépendante de workloads installés sur les environnements de build/tests.【F:src/Aion.AppHost/Aion.AppHost.csproj†L3-L32】
- **Risque de test UI** : `Aion.AppHost.UI.Tests` référence directement `Aion.AppHost.csproj` MAUI ; selon l’environnement de test (agents Linux sans workloads), cela peut fragiliser l’exécution des tests UI BUnit.【F:tests/Aion.AppHost.UI.Tests/Aion.AppHost.UI.Tests.csproj†L19-L29】

## 2. Audit Aion.Domain
- **Dépendances techniques interdites (partielles)** : le projet n’importe pas EF Core directement (bon point), mais les entités Domain embarquent fortement des attributs `DataAnnotations` et même `Schema` pour coller au modèle de persistance, ce qui tire le Domain vers des préoccupations de stockage/validation technique.【F:src/Aion.Domain/Entities.cs†L1-L12】
- **Domain trop orienté “data model”** : `Entities.cs` concentre un très grand nombre d’énums/entités transverses (modules, automation, notes, agenda, vision, etc.) dans un seul fichier, rendant les frontières métier moins lisibles et compliquant l’évolution isolée par sous-domaines.【F:src/Aion.Domain/Entities.cs†L13-L112】
- **Infrastructure déguisée dans Domain** : `InfrastructureOptions.cs` expose des options techniques (DB, storage, backup, cloud backup) et inclut des checks platform (`OperatingSystem.IsAndroid/IsIOS`) dans le Domain, ce qui casse la pureté métier attendue.【F:src/Aion.Domain/InfrastructureOptions.cs†L5-L57】
- **Contrats très couplés / surface large** : `ServiceContracts.cs` regroupe un grand nombre d’interfaces hétérogènes (data engine, notes, agenda, automation, sync, backup, search, etc.) dans une seule surface centrale ; cela augmente le couplage inter-modules et la difficulté de versioning des contrats.【F:src/Aion.Domain/ServiceContracts.cs†L1-L225】

## 3. Audit Aion.Infrastructure
- **Composition infra surchargée** : `DependencyInjectionExtensions` gère à la fois options/configuration, validations de sécurité, EF Core, enregistrements métier, extensions, sync, backups cloud et hosted services ; le composition root infra est central mais très dense (dette de lisibilité/évolutivité).【F:src/Aion.Infrastructure/DependencyInjectionExtensions.cs†L22-L190】
- **Isolation EF/SQLite/SQLCipher** : l’usage SQLCipher (PRAGMA key, interceptor/factory) est bien confiné à Infrastructure/RecoveryTool ; pas d’accès SQL brut visible hors couches techniques autorisées (hors construction de connection string côté AppHost).【F:src/Aion.Infrastructure/SqliteEncryptionInterceptor.cs†L1-L56】【F:src/Aion.Infrastructure/SqliteConnectionFactory.cs†L1-L55】
- **DbContext monolithique** : `AionDbContext` expose un nombre très élevé de `DbSet` couvrant quasiment tout le produit, avec un `OnModelCreating` massif ; dette forte de modularité et risque d’effet de bord sur migrations/perf.【F:src/Aion.Infrastructure/AionDbContext.cs†L23-L84】【F:src/Aion.Infrastructure/AionDbContext.cs†L86-L180】
- **Services métier consolidés dans un “god file”** : `CoreServices.cs` (3726 lignes) contient de nombreuses classes de responsabilités différentes (`AionDataEngine`, `NoteService`, `Agenda`, `Template`, `Persona`, `Vision`, etc.), ce qui rend le code difficile à maintenir et à tester finement.【F:src/Aion.Infrastructure/Services/CoreServices.cs†L1-L3726】
- **Migrations nombreuses et vivantes** : présence de nombreuses migrations EF Core (bon pour historisation), mais la volumétrie et la croissance du snapshot indiquent une complexité schema élevée à surveiller.【F:src/Aion.Infrastructure/Migrations/AionDbContextModelSnapshot.cs†L1-L2255】

## 4. Audit Aion.AI
- **Wiring IA centralisé mais volumineux** : `AddAionAi` enregistre beaucoup de providers (HTTP/mock/offline/local/inactive), des keyed services et des aliases ; c’est flexible mais complexe à auditer/debugger rapidement.【F:src/Aion.AI/ServiceCollectionExtensions.cs†L13-L111】
- **Fichier providers trop large** : `Providers.cs` (1105 lignes) rassemble clients HTTP, intent recognizer, interpreters CRUD/agenda/note/report, vision engine ; responsabilités mélangées dans un même fichier/source set.【F:src/Aion.AI/Providers.cs†L1-L1105】
- **Fallbacks/stubs explicites (temporaires par nature)** : providers `Inactive`, `Mock`, `Offline` renvoient des réponses contrôlées/no-op ; bon pour résilience offline, mais c’est une zone à gouverner strictement pour éviter un usage involontaire en production.【F:src/Aion.AI/InactiveProviders.cs†L9-L120】【F:src/Aion.AI/Providers.Mock/MockAiProviders.cs†L1-L211】【F:src/Aion.AI/Providers.Offline/OfflineAiProviders.cs†L1-L194】
- **Service locator partiel dans factory** : `AiModelFactory` résout les implémentations via `IServiceProvider.GetKeyedService`, ce qui simplifie le routage dynamique mais rend le graphe de dépendances moins explicite/testable qu’une injection orientée stratégie explicite.【F:src/Aion.AI/AiModelFactory.cs†L12-L56】
- **Structured outputs** : mécanisme de validation/correction JSON présent (bon point), mais approche par retries + prompt correction dépend fortement de la robustesse des modèles et nécessite tests de non-régression continus par schéma.【F:src/Aion.AI/StructuredJson/StructuredJsonResponseHandler.cs†L24-L92】

## 5. Audit Aion.AppHost
- **Bootstrap dense** : `MauiProgram` mélange configuration fichiers/secrets, création des chemins locaux, résolution des clés, et enregistrements DI de l’ensemble de l’app. Fonctionnel mais fortement centralisé et sensible aux évolutions transverses.【F:src/Aion.AppHost/MauiProgram.cs†L24-L126】
- **Dépendances concrètes en UI** : certains composants injectent des implémentations concrètes (`ModuleDesignerService`) au lieu d’interfaces, augmentant le couplage UI → implémentation AI.【F:src/Aion.AppHost/Components/Pages/Modules.razor†L2-L8】
- **Responsabilité métier dans Razor pages** : `Marketplace.razor` (473 lignes) intègre orchestration de chargement, import/export template, upload vision, suggestions, états UX ; logique métier/applicative dense dans la couche UI.【F:src/Aion.AppHost/Components/Pages/Marketplace.razor†L1-L473】
- **Pattern service locator côté init** : `AppInitializationService` injecte `IServiceProvider` puis résout dynamiquement plusieurs services dans un scope, ce qui réduit la transparence du wiring et complique les tests unitaires ciblés.【F:src/Aion.AppHost/Services/AppInitializationService.cs†L12-L81】

## 6. Composition / DI
- **Composition root “réel” multi-points** : le câblage est réparti entre Infrastructure (`AddAionInfrastructure`), AI (`AddAionAi`) et AppHost (`MauiProgram.ConfigureServices`) ; il existe un centre principal côté AppHost, mais pas un unique composition root agnostique hôte.【F:src/Aion.Infrastructure/DependencyInjectionExtensions.cs†L22-L190】【F:src/Aion.AI/ServiceCollectionExtensions.cs†L13-L111】【F:src/Aion.AppHost/MauiProgram.cs†L111-L167】
- **Projet Composition minimal** : `Aion.Composition` agit surtout comme façade/forwarder vers Infrastructure, sans orchestration transverse complète Domain+AI+AppHost.【F:src/Aion.Composition/InfrastructureHostingExtensions.cs†L10-L17】
- **Impact MAUI Blazor Hybrid** : la dispersion actuelle du wiring n’empêche pas MAUI Hybrid, mais complique l’émergence d’un host alternatif (CLI/web/service worker) qui voudrait réutiliser exactement la même composition applicative.

## 7. Tests
- **Présence d’une base de tests multi-couches** : tests Domain, Infrastructure, AI, AppHost UI et intégration existent dans des projets dédiés (signal positif de maturité).【F:AionMemory.slnx†L12-L17】
- **Couverture logique orientée services clés** : ex. smoke end-to-end module applier + data engine ; module builder/validation ; tests UI BUnit basiques pour pages clés.【F:tests/Aion.Tests/EndToEndSmokeTests.cs†L14-L96】【F:tests/Aion.Infrastructure.Tests/ModuleBuilderTests.cs†L17-L138】【F:tests/Aion.AppHost.UI.Tests/ModulesHomeTests.cs†L11-L88】
- **Manques prioritaires observés** : peu d’indications de tests de non-régression pour les très gros fichiers transverses (`CoreServices.cs`, `Providers.cs`, pages Razor les plus denses) et pour la qualité du wiring DI multi-provider en conditions réelles.
- **Fragilité potentielle d’exécution** : dépendance MAUI dans tests UI + exigences workloads peuvent réduire la portabilité des runs CI Linux minimalistes.【F:tests/Aion.AppHost.UI.Tests/Aion.AppHost.UI.Tests.csproj†L19-L29】

## 8. Documentation
- **README racine globalement aligné** avec architecture MAUI, scripts de build/test, et principes local-first/sécurité.【F:README.md†L1-L74】
- **`docs/README.md` partiellement obsolète/trompeur** : il parle d’un “console host meant to evolve into MAUI” et “Aion.AppHost: Console bootstrap”, alors que l’hôte est déjà MAUI multi-target dans le code actuel.【F:docs/README.md†L3-L11】【F:src/Aion.AppHost/Aion.AppHost.csproj†L3-L9】
- **Doc sécurité utile mais à clarifier** : mention de defaults de dev (clé de développement/fallbacks) cohérente avec l’intention offline, mais mérite d’être plus explicite sur les garde-fous de production et l’interdiction stricte de clés par défaut hors dev/test.【F:docs/SECURITY.md†L49-L52】【F:src/Aion.Infrastructure/DependencyInjectionExtensions.cs†L48-L50】

# Liste des problèmes identifiés
## P0 — Bloquants
1. **Validation exécutable impossible dans cet environnement** (`dotnet` et `pwsh` absents), donc impossibilité de confirmer build/tests/warnings réellement à l’instant T.

## P1 — Dette grave
1. **Monolithes de code** : `CoreServices.cs` (3726 lignes) et `Providers.cs` (1105 lignes) concentrent trop de responsabilités critiques.【F:src/Aion.Infrastructure/Services/CoreServices.cs†L1-L3726】【F:src/Aion.AI/Providers.cs†L1-L1105】
2. **Domain partiellement pollué par des préoccupations techniques** (options infra + checks OS).【F:src/Aion.Domain/InfrastructureOptions.cs†L5-L57】
3. **Composition DI dispersée** entre plusieurs extensions/hôtes, complexifiant l’évolution et la testabilité système.【F:src/Aion.Infrastructure/DependencyInjectionExtensions.cs†L22-L190】【F:src/Aion.AppHost/MauiProgram.cs†L111-L167】

## P2 — Maintenabilité
1. **DbContext massif** (nombreux DbSets + mapping volumineux).【F:src/Aion.Infrastructure/AionDbContext.cs†L23-L180】
2. **Service locator dans `AppInitializationService` et `AiModelFactory`** (dépendances implicites).【F:src/Aion.AppHost/Services/AppInitializationService.cs†L14-L79】【F:src/Aion.AI/AiModelFactory.cs†L35-L56】
3. **Couplage UI à des implémentations concrètes AI** (`ModuleDesignerService`).【F:src/Aion.AppHost/Components/Pages/Modules.razor†L2-L8】
4. **Documentation interne non homogène** (`docs/README.md` vs réalité MAUI).【F:docs/README.md†L3-L11】【F:src/Aion.AppHost/Aion.AppHost.csproj†L3-L9】

## P3 — Améliorations futures
1. Renforcer les tests de non-régression autour des orchestrations/fallbacks IA.
2. Isoler davantage la logique applicative des composants Razor les plus denses.
3. Extraire des modules de configuration DI plus ciblés (par capability).

# Quick wins immédiats
1. Mettre à jour `docs/README.md` pour refléter l’état MAUI réel.
2. Introduire des interfaces pour les services concrets injectés dans les pages Razor.
3. Découper les fichiers “god classes” en sous-domaines fonctionnels sans changer les contrats publics.
4. Ajouter une vérification CI explicite de la présence des workloads/SDK nécessaires.

# Refactorings structurants recommandés
1. **Découpage vertical Infrastructure** : séparer DataEngine, Notes, Agenda, Templates, Vision, etc. hors `CoreServices.cs`.
2. **Découpage vertical AI** : séparer providers HTTP, interpreters, intent routing, et vision dans des unités indépendantes.
3. **Nettoyage Domain** : déplacer les options infra/platform hors Domain vers une couche Application/Composition.
4. **Composition root unifié** : créer un module de composition transverse (hôte-agnostique), puis l’appeler depuis MAUI/CLI/tests.

# Ce que tu as réellement vérifié
- **Fichiers lus** : solution, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `README`, docs sécurité/architecture, projets `.csproj`, et fichiers clés Domain/Infrastructure/AI/AppHost/tests.
- **Projets inspectés** : `Aion.Domain`, `Aion.Infrastructure`, `Aion.AI` (+ providers), `Aion.AppHost`, `Aion.Composition`, `Aion.RecoveryTool`, et suites de tests.
- **Zones inspectées** : build/SDK, dépendances, DI/wiring, contrats métier, persistance/chiffrement, stubs IA/fallback, pages Razor, couverture de tests, documentation.
- **Limites** : audit statique uniquement dans cet environnement (pas d’exécution `dotnet`/`pwsh` possible faute de binaires installés).
