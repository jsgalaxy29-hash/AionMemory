# Sécurité & Configuration

Objectif : aucune clé ni configuration sensible ne doit être versionnée. Les fichiers `appsettings.*.json` réels restent locaux ; seules les variantes `*.example.json` servent de modèles.

## Règles générales
- Garder les secrets hors du dépôt : les patterns `appsettings*.json` sont ignorés par Git (hors `*.example.json`).
- Utiliser les modèles fournis (`appsettings.OpenAI.example.json`, `appsettings.Mistral.example.json`, `appsettings.Development.example.json`) comme références locales, jamais directement en production.
- Préférer `dotnet user-secrets` pour le développement et les variables d’environnement pour la CI/production.

## Développement local : `dotnet user-secrets`
Initialiser le magasin de secrets sur le projet hôte MAUI :
```bash
dotnet user-secrets init --project src/Aion.AppHost/Aion.AppHost.csproj
```

Définir les valeurs sans les écrire sur disque :
```bash
# Base de données / stockage
dotnet user-secrets set "Aion:Database:EncryptionKey" "<clé-32+>" --project src/Aion.AppHost/Aion.AppHost.csproj
# Reutiliser la même clé côté stockage, ou en fournir une dédiée
dotnet user-secrets set "Aion:Storage:EncryptionKey" "<clé-32+>" --project src/Aion.AppHost/Aion.AppHost.csproj

# Provider OpenAI
dotnet user-secrets set "Aion:Ai:Provider" "openai" --project src/Aion.AppHost/Aion.AppHost.csproj
dotnet user-secrets set "Aion:Ai:ApiKey" "<OPENAI_API_KEY>" --project src/Aion.AppHost/Aion.AppHost.csproj

# Provider Mistral (si utilisé)
dotnet user-secrets set "Aion:Ai:Provider" "mistral" --project src/Aion.AppHost/Aion.AppHost.csproj
dotnet user-secrets set "Aion:Ai:ApiKey" "<MISTRAL_API_KEY>" --project src/Aion.AppHost/Aion.AppHost.csproj
```
Les chemins (`Aion:Storage:RootPath`, `Aion:Backup:Folder`, etc.) peuvent aussi être fournis via `user-secrets` si besoin.

## CI / Production : variables d’environnement
Exporter les valeurs dans l’environnement avant le déploiement/launch :
```bash
export ConnectionStrings__Aion="Data Source=/secure/path/aion.db;Cache=Private;Mode=ReadWriteCreate"
export Aion__Database__EncryptionKey="<clé-32+>"
export Aion__Storage__RootPath="/secure/path/storage"
export Aion__Storage__EncryptionKey="$Aion__Database__EncryptionKey"
export Aion__Backup__Folder="/secure/path/storage/backup"

# Provider AI (optionnel si mode offline)
export Aion__Ai__Provider="openai"
export Aion__Ai__ApiKey="<clé-provider>"
export Aion__Ai__BaseEndpoint="https://api.openai.com/v1"
```
Ces variables sont lues automatiquement par l’hôte (MAUI, CLI ou services) sans nécessiter de fichiers `appsettings` réels.

## Mode offline et valeurs par défaut
- Si aucune configuration AI n’est fournie, les validateurs laissent l’application démarrer avec des providers factices (pas d’appel réseau).
- L’infrastructure applique des valeurs de secours sûres : dossiers `data/storage`, `data/marketplace`, `data/storage/backup` sous le répertoire d’exécution, clé SQLCipher de développement (à remplacer en prod) et création automatique des dossiers.

## Vérifications anti-fuite
- Un scan `gitleaks` est exécuté en CI. Pour vérifier en local :
```bash
gitleaks detect --source .
```
- Avant de pousser, vérifier que `git status --short` n’affiche aucun `appsettings*.json` non-exemple.
