# Installation locale d'AionMemory

AionMemory est conçu pour fonctionner **localement** : aucune dépendance cloud et aucun tracking obligatoire.

## Pré-requis
- [.NET SDK](https://dotnet.microsoft.com/download) (voir `global.json`).
- Workload .NET MAUI (`dotnet workload install maui`).

## Installation rapide (scripts)

Depuis la racine du dépôt :

### Windows (PowerShell)
```powershell
pwsh ./scripts/install.ps1
```

### macOS / Linux (bash)
```bash
./scripts/install.sh
```

Les scripts restaurent les dépendances puis compilent `Aion.AppHost`. Utilisez l'option suivante si le workload MAUI est déjà installé :

- PowerShell : `pwsh ./scripts/install.ps1 -SkipWorkloadInstall`
- Bash : `./scripts/install.sh --skip-workload-install`

## Lancement
Le projet UI est `src/Aion.AppHost/Aion.AppHost.csproj`. Lancez-le avec votre IDE (Visual Studio, Rider) ou via `dotnet build -t:Run` selon la plateforme.

## Premier démarrage (wizard)
Au premier lancement, un assistant minimal vous demande :

1. **Le chemin de la base locale chiffrée** (SQLite/SQLCipher).
2. **La clé de chiffrement** (32 caractères minimum).
3. **Le nom du profil local**.

Ces paramètres sont stockés **localement** (SecureStorage/Preferences). Aucune valeur n'est envoyée vers un service cloud.

## Dépannage rapide
- Si le workload MAUI manque : `dotnet workload install maui`.
- Si la base est inaccessible, vérifiez les permissions sur le répertoire choisi.
