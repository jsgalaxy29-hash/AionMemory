# Aion Memory

Application MAUI Blazor qui orchestre l’agent mémoire AION (domaine, infrastructure et IA) dans une app mobile/desktop. Ce dépôt inclut les projets de base (domain, infrastructure, IA) ainsi que l’hôte MAUI.

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

## Configuration
L’application utilise des options strictement validées au démarrage :
- **Base de données** :
  - Chaîne de connexion SQLite (par défaut `aion.db` dans le répertoire applicatif).
  - Clé d’encryption SQLCipher (`AION_DB_KEY`). Si non fournie, une clé 32 octets est générée et stockée via `SecureStorage`.
- **Stockage** : chemin racine pour les pièces jointes et exports (`storage`).
- **Marketplace** : dossier pour les packages de modules (`marketplace`).
- **Backups** : dossier de sauvegarde chiffrée (`storage/backup`).

La configuration peut être passée via `appsettings.*`, variables d’environnement (`Aion:*`) ou `ConnectionStrings:Aion`. Toute valeur manquante ou chemin inexistant bloque le démarrage pour éviter une configuration partielle.

## Structure des projets
- `/src/Aion.Domain` : entités, contrats et invariants.
- `/src/Aion.Infrastructure` : EF Core + SQLite chiffré, services métiers (stockage, backup, marketplace, recherche…).
- `/src/Aion.AI` : moteur IA générique (contrats, implémentations HTTP par défaut) et adaptateurs factices.
- `/src/Aion.AI/Providers.OpenAI` & `/src/Aion.AI/Providers.Mistral` : fournisseurs IA interchangeables.
- `/src/Aion.AppHost` : hôte MAUI Blazor Hybrid, uniquement pour l’UI/DI/navigation.
- `/tests` : batteries de tests unitaires par couche.

## Configuration & sécurité
- **Ne pas versionner de secrets** : les exemples `appsettings.OpenAI.example.json` et `appsettings.Mistral.example.json` servent de modèles. Les vrais fichiers sont ignorés par Git.
- En développement, utiliser `dotnet user-secrets` pour injecter les clés (`Aion:Ai:ApiKey`, `AION_DB_KEY`, etc.).
- En CI/production, préférer les variables d’environnement (`Aion:*`) ou les coffres-forts secrets.

## Repo conventions
Les règles d’architecture, de qualité et de sécurité sont décrites dans [AGENTS.md](./AGENTS.md). Merci de les suivre avant toute contribution.

## Notes supplémentaires
- La base de données et les dossiers sont créés dans le répertoire de données applicatif (`FileSystem.AppDataDirectory`).
- Pour changer l’emplacement du stockage ou des sauvegardes, fournir des chemins absolus et existants via la configuration (sinon la validation échoue).
- La documentation produit est rangée dans `/docs` avec les sous-dossiers `AION_Vision`, `AION_Specification` et `AION_Prompts`.
