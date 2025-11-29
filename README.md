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
   dotnet build AionMemory/AionMemory.csproj -t:Run -f net10.0-android
   ```

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
- **Aion.Domain** : entités, contrats et événements.
- **Aion.Infrastructure** : EF Core + SQLite chiffré, services métiers (stockage, backup, marketplace, recherche…).
- **Aion.AI** : implémentations factices pour les fournisseurs IA.
- **AionMemory** : application MAUI Blazor hybride qui consomme les couches ci-dessus.

## Notes supplémentaires
- La base de données et les dossiers sont créés dans le répertoire de données applicatif (`FileSystem.AppDataDirectory`).
- Pour changer l’emplacement du stockage ou des sauvegardes, fournir des chemins absolus et existants via la configuration (sinon la validation échoue).
