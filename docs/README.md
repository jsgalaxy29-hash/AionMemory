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

## Roadmap produit (squelette)

- **Checklist détaillée par couche** : voir `AION_Checklist.md` pour suivre l’avancement Domain/Infrastructure/AI/AppHost.
- **MVP (itérations 0-1)** :
  - finaliser la persistance chiffrée (SQLite + SQLCipher) et la validation stricte des chemins de stockage ;
  - exposer les formulaires dynamiques (Potager, MarketPlace) via les Razor Components de l’hôte ;
  - brancher un fournisseur IA unique (OpenAI ou local) sur les interfaces `Aion.Domain` pour embeddings, LLM et transcription ;
  - tracer les événements critiques (sauvegarde, export, génération IA) pour audit minimal.
- **Itérations 2-3** :
  - étendre le moteur de synchronisation (sauvegardes planifiées, rotation et restauration) ;
  - intégrer la détection d’intention et le routage de modules (automatisation, dashboard, vision) ;
  - enrichir les contrôles d’accès (verrouillage par biométrie/OS, séparation des clés SQLCipher) ;
  - déployer un pipeline CI (build, tests, analyse statique) avec packaging MAUI.
- **Itérations 4+** :
  - marketplace de templates/modules versionnée avec signature des packages ;
  - tableau de bord LifeGraph et prédictions (score de santé du jardin, alertes) ;
  - mode offline-first et cache local des embeddings ;
  - ouverture d’API (gRPC/REST) sécurisée pour agents externes.

## Usages : configuration et exécution

1. **Configurer la base chiffrée** :
   - définir `AION_DB_KEY` (32 octets) dans les variables d’environnement ou `appsettings.*` ;
   - vérifier que le chemin `storage` et `storage/backup` existent ou laisser l’application les créer ;
   - optionnel : renseigner `ConnectionStrings:Aion` pour surcharger l’emplacement de `aion.db`.
2. **Lancer l’hôte console** :
   - `dotnet restore && dotnet run --project src/Aion.AppHost/Aion.AppHost.csproj` ;
   - l’hôte injecte les options via `AionDatabaseOptions` et démarre les services de sauvegarde/marketplace.
3. **Lancer MAUI** :
   - `dotnet build src/Aion.AppHost/Aion.AppHost.csproj -t:Run -f <target>` ;
   - la configuration validée côté infrastructure est réutilisée via `MauiProgram`.

## Sécurité et conformité

- **Chiffrement** : SQLCipher activé dès l’ouverture de la base ; la clé n’est jamais journalisée et peut provenir d’un coffre (OS/SecureStorage).
- **Surface de stockage** : dossiers `storage`, `marketplace` et `storage/backup` sont isolés et validés ; la sauvegarde chiffrée est prioritaire sur les exports bruts.
- **Secrets** : pas de secret en clair dans le code ; utiliser les variables d’environnement ou un provider de configuration sécurisé.
- **Traçabilité** : prévoir des logs structurés pour les actions sensibles (import/export, déclenchement IA, rotations de clé) avant la mise en prod.

## Scénarios IA supportés (squelette)

- **Embeddings & recherche sémantique** : interfaces dans `Aion.Domain` ; implémentation stub dans `Aion.AI` à remplacer par un provider (OpenAI, local). Les embeddings sont utilisés par le moteur de recherche et la marketplace.
- **LLM & agents** : appels textuels (génération de rapports, résumés de notes) et interprétation de CRUD/report. Le routage d’intention reliera l’agent à des modules métier (Potager, dashboards).
- **Transcription & vision** : placeholders pour dictée et vision ; ils peuvent s’appuyer sur un service externe (Whisper, Azure Speech, OCR). Prévoir le stockage des médias dans `storage` et l’indexation dans la base chiffrée.
- **Automatisation** : déclencheurs basés sur le calendrier ou l’état des modules (ex. rappel d’arrosage). Les actions sont orchestrées via les services du domaine pour rester auditables.

## Configuration Notes

- SQLCipher key must be supplied via configuration (`AION_DB_KEY` env var) and is injected into the DbContext through `AionDatabaseOptions`.
- Marketplace packages are serialized under the `marketplace` folder; backups are stored under `storage/backup`.
- Replace the stub AI providers in `Aion.AI` with real integrations (OpenAI, Mistral, local models) by implementing the interfaces defined in `Aion.Domain`.

## Extensibility

- Add EF Core migrations from `Aion.Infrastructure` once schema stabilizes.
- Expand the dynamic Razor components to render field types, validation, and automation triggers.
- Plug in dashboard widgets and LifeGraph visualizations directly using the models defined in the domain layer.
