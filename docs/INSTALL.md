# Installation locale d'AionMemory

AionMemory est concu pour fonctionner localement, sans dependance cloud obligatoire.

## Pre-requis

- [.NET SDK](https://dotnet.microsoft.com/download) compatible avec `global.json`.
- Workload .NET MAUI (`dotnet workload install maui`).

## Installation recommandee

Depuis la racine du depot :

```bash
dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx
```

## Scripts d'installation

Des scripts sont presents dans `scripts/` :

- Windows : `pwsh ./scripts/install.ps1`
- macOS / Linux : `./scripts/install.sh`

Si le workload MAUI est deja installe :

- PowerShell : `pwsh ./scripts/install.ps1 -SkipWorkloadInstall`
- Bash : `./scripts/install.sh --skip-workload-install`

## Lancement

Le projet UI est `Aion.AppHost/Aion.AppHost.csproj`. Lancez-le avec votre IDE (Visual Studio, Rider) ou via `dotnet build -t:Run` selon la plateforme.

Exemple Windows :

```powershell
dotnet build Aion.AppHost/Aion.AppHost.csproj -t:Run -f net10.0-windows10.0.19041.0
```

## Premier demarrage

Au premier lancement, un assistant minimal demande :

1. Le chemin de la base locale chiffree (SQLite/SQLCipher).
2. La cle de chiffrement.
3. Le nom du profil local.

Ces parametres sont stockes localement via `SecureStorage` et `Preferences`. Aucune valeur n'est envoyee vers un service cloud.

## Depannage rapide

- Si le workload MAUI manque : `dotnet workload install maui`.
- Si la base est inaccessible, verifier les permissions sur le repertoire choisi.
- Si vous utilisez un chemin personnalise pour le stockage ou les backups, verifier qu'il existe et qu'il passe la validation d'options de l'infrastructure.
