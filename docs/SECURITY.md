# Securite & Configuration

Objectif : aucune cle ni configuration sensible ne doit etre versionnee. Les fichiers `appsettings.*.json` reels restent locaux ; seules les variantes `*.example.json` servent de modeles.

## Regles generales

- Garder les secrets hors du depot : les patterns `appsettings*.json` sont ignores par Git, hors `*.example.json`.
- Utiliser les modeles fournis (`appsettings.OpenAI.example.json`, `appsettings.Mistral.example.json`, `appsettings.Development.example.json`) comme references locales.
- Preferer `dotnet user-secrets` pour le developpement desktop/CLI et les variables d'environnement pour la CI/production.
- Dans l'hote MAUI, certaines valeurs locales peuvent aussi etre stockees via `SecureStorage`.

## Developpement local : `dotnet user-secrets`

Initialiser le magasin de secrets sur le projet hote MAUI :

```bash
dotnet user-secrets init --project Aion.AppHost/Aion.AppHost.csproj
```

Definir les valeurs sans les ecrire dans un fichier versionne :

```bash
# Base de donnees / stockage
dotnet user-secrets set "Aion:Database:EncryptionKey" "<cle-32+>" --project Aion.AppHost/Aion.AppHost.csproj
dotnet user-secrets set "Aion:Storage:EncryptionKey" "<cle-32+>" --project Aion.AppHost/Aion.AppHost.csproj

# Provider OpenAI
dotnet user-secrets set "Aion:Ai:Provider" "openai" --project Aion.AppHost/Aion.AppHost.csproj
dotnet user-secrets set "Aion:Ai:ApiKey" "<OPENAI_API_KEY>" --project Aion.AppHost/Aion.AppHost.csproj

# Provider Mistral
dotnet user-secrets set "Aion:Ai:Provider" "mistral" --project Aion.AppHost/Aion.AppHost.csproj
dotnet user-secrets set "Aion:Ai:ApiKey" "<MISTRAL_API_KEY>" --project Aion.AppHost/Aion.AppHost.csproj
```

Les chemins (`Aion:Storage:RootPath`, `Aion:Backup:Folder`, etc.) peuvent aussi etre fournis via `user-secrets` si besoin.

## MAUI : stockage local securise

Au premier lancement, l'ecran `/setup` peut demander :

- le chemin de la base locale chiffree ;
- la cle de chiffrement ;
- le nom du profil local.

Dans ce cas, les valeurs locales sont stockees via `Preferences` et `SecureStorage`.

## CI / Production : variables d'environnement

Exporter les valeurs dans l'environnement avant le lancement :

```bash
export ConnectionStrings__Aion="Data Source=/secure/path/aion.db;Cache=Private;Mode=ReadWriteCreate"
export Aion__Database__EncryptionKey="<cle-32+>"
export Aion__Storage__RootPath="/secure/path/storage"
export Aion__Storage__EncryptionKey="$Aion__Database__EncryptionKey"
export Aion__Backup__Folder="/secure/path/storage/backup"

# Provider AI optionnel si mode offline
export Aion__Ai__Provider="openai"
export Aion__Ai__ApiKey="<cle-provider>"
export Aion__Ai__BaseEndpoint="https://api.openai.com/v1"
```

Ces variables sont lues par l'infrastructure et par l'hote MAUI sans necessiter de fichiers `appsettings` reels.

## Mode offline et valeurs par defaut

- Si aucune configuration AI n'est fournie, l'application peut demarrer avec des providers inactifs/mock/offline selon la configuration resolue.
- Dans l'hote MAUI, les dossiers par defaut sont crees sous `FileSystem.AppDataDirectory` (`storage`, `marketplace`, `storage/backup`).
- Hors MAUI, l'infrastructure peut utiliser un dossier `data/` sous le repertoire d'execution et appliquer des valeurs de dev/test pour simplifier le demarrage local.

## Verifications anti-fuite

- Avant de pousser, verifier que `git status --short` n'affiche aucun `appsettings*.json` non-exemple.
- Si vous utilisez `gitleaks` localement :

```bash
gitleaks detect --source .
```
