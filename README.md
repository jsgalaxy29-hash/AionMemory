# Aion Memory

Application MAUI Blazor qui orchestre l’agent mémoire AION (domaine, infrastructure et IA) dans une app mobile/desktop. Ce dépôt inclut les projets de base (domain, infrastructure, IA) ainsi que l’hôte MAUI.

## Vision
AionMemory est une mémoire personnelle, souveraine et local-first. Les données
restent locales par défaut, le fonctionnement offline est garanti, et toute
interaction réseau est explicite et traçable.

## Design Principles
- **Local-first** : données stockées localement par défaut, synchronisation
  optionnelle et explicite.
- **Confidentialité par défaut** : chiffrement local, absence d’envoi implicite.
- **Explicabilité** : résultats IA reliés aux sources et aux règles appliquées.
- **Contrôle humain** : actions sensibles validées par l’utilisateur.
- **Résilience offline** : comportements déterministes sans réseau.

Voir [docs/AION_MANIFEST.md](./docs/AION_MANIFEST.md) pour le manifeste produit.

## Prérequis
- .NET 10 (SDK preview) avec les workloads MAUI installés (`dotnet workload install maui-android maui-ios maui-maccatalyst`).
- Un émulateur ou un appareil pour la plateforme ciblée (Android, iOS, macOS Catalyst ou Windows via `net10.0-windows10.0.19041.0`).
- Git et les outils propres à votre OS (Java/Android SDK pour Android, Xcode pour iOS/macOS).

## Installation
1. Cloner le dépôt :
   ```bash
   git clone https://github.com/…/AionMemory.git
   cd AionMemory
   ```
2. Restaurer et construire la solution :
   ```bash
   dotnet restore AionMemory.slnx
   dotnet build AionMemory.slnx
   ```
3. Lancer l’application MAUI sur la plateforme souhaitée (exemple Android) :
   ```bash
   dotnet build src/Aion.AppHost/Aion.AppHost.csproj -t:Run -f net10.0-android
   ```

## Build & Test
- Build (Release) : `pwsh ./scripts/build.ps1`
- Tests (Release) : `pwsh ./scripts/test.ps1`
- Validation (Release) : Windows `pwsh ./tools/validate.ps1`, Linux/macOS `bash ./tools/validate.sh`

## Configuration
La configuration est lue depuis les fichiers `appsettings.*` (non versionnés), `dotnet user-secrets` et/ou les variables d’environnement (`Aion:*`, `ConnectionStrings:Aion`). L’application démarre en mode offline lorsque rien n’est configuré : les providers IA retournent des stubs et les dossiers par défaut (`data/storage`, `data/marketplace`, `data/storage/backup`) sont créés automatiquement sous le répertoire d’exécution.

- **Base de données** : SQLite + SQLCipher ; en développement/tests, une clé est générée à l'exécution si aucune configuration n'est fournie (pas de clé statique versionnée par défaut). Fournir `AION_DB_KEY`/`Aion:Database:EncryptionKey` en production.
- **Stockage / Marketplace / Backups** : chemins configurables ; valeurs de secours générées si aucune configuration n’est fournie.
- **IA** : OpenAI/Mistral configurables via `Aion:Ai:*`. Sans clé ou endpoint, l’app reste offline et ne tente aucun appel réseau.

Voir [docs/SECURITY.md](./docs/SECURITY.md) pour les instructions détaillées (user-secrets en dev, variables d’environnement en CI/production) et les modèles `*.example.json`.

## Structure des projets
- `/src/Aion.Domain` : contrats, invariants et modèles métier, sans dépendance sortante.
- `/src/Aion.Infrastructure` : implémentations EF Core + SQLite/SQLCipher, persistance et services métiers.
- `/src/Aion.AI` : orchestration IA (sélecteur de provider, modèles HTTP/offline/mock, interpréteurs).
- `/src/Aion.AI/Providers.OpenAI` & `/src/Aion.AI/Providers.Mistral` : fournisseurs IA spécifiques enregistrés en complément.
- `/src/Aion.Composition` : composition racine DI (`AddAionCore`) et politiques runtime de plateforme.
- `/src/Aion.AppHost` : hôte MAUI Blazor Hybrid (UI, navigation, options locales et services d’application).
- `/src/Aion.RecoveryTool` : outil CLI de récupération/export pour bases chiffrées.
- `/tests` : suites de tests par couche (Domain, Infrastructure, AI, AppHost, Composition).

## Configuration & sécurité
- **Ne pas versionner de secrets** : les exemples `appsettings.OpenAI.example.json`, `appsettings.Mistral.example.json` et `appsettings.Development.example.json` servent de modèles. Les vrais fichiers sont ignorés par Git.
- En développement, utiliser `dotnet user-secrets` pour injecter les clés (`Aion:Ai:ApiKey`, `AION_DB_KEY`, etc.) sans écrire de fichiers locaux.
- En CI/production, préférer les variables d’environnement (`Aion:*`, `ConnectionStrings:Aion`) ou les coffres-forts secrets. Les validateurs tolèrent l’absence d’IA pour permettre un mode offline contrôlé.

## Repo conventions
Les règles d’architecture, de qualité et de sécurité sont décrites dans [AGENTS.md](./AGENTS.md). Merci de les suivre avant toute contribution.

## Notes supplémentaires
- La base de données et les dossiers sont créés dans le répertoire de données applicatif (`FileSystem.AppDataDirectory`).
- Pour changer l’emplacement du stockage ou des sauvegardes, fournir des chemins absolus et existants via la configuration (sinon la validation échoue).
- La documentation produit est rangée dans `/docs` avec les sous-dossiers `AION_Vision`, `AION_Specification` et `AION_Prompts`.
