# AionMemory — Core Rules

## Ce qu’AionMemory est
- **Un système mémoire structuré** : une base de connaissances persistante, chiffrée, et gouvernée par des contrats explicites (`Aion.Domain`).
- **Une architecture en couches** :
  - `Aion.Domain` définit les contrats/invariants.
  - `Aion.Infrastructure` implémente l’accès aux données (EF Core + SQLCipher).
  - `Aion.AI` orchestre les capacités IA.
  - `Aion.AppHost` fournit l’UI, la navigation et la composition MAUI/Blazor.
- **Un produit sécurisé par défaut** : pas de secrets versionnés, configuration via `appsettings.*.example.json`, variables d’environnement ou user-secrets.
- **Un codebase stable** : nullabilité activée, pas de breaking change silencieux, règles d’architecture et validations CI.

## Ce qu’AionMemory n’est PAS
- **Pas une “god app”** : l’UI n’accède pas directement à l’Infrastructure.
- **Pas une base de données ouverte** : l’accès SQL brut est proscrit hors des points d’entrée Infrastructure.
- **Pas un endroit pour stocker des secrets** : aucune clé, token ou secret ne doit vivre dans le dépôt.
- **Pas un monolithe non testable** : toute évolution doit préserver la testabilité et les contrats de `Aion.Domain`.

## CI — garde-fous essentiels
- **Interdiction UI → Infrastructure** : pas de dépendance directe entre projets UI et `Aion.Infrastructure`.
- **Interdiction de secrets** : aucun fichier sensible (ex: `appsettings*.json` hors `.example.json`) ne doit être versionné.

## Checklist “PR validation”
- [ ] Les règles CORE sont respectées (UI ≠ Infrastructure, contrats stables, nullabilité conservée).
- [ ] Aucun secret ajouté (fichiers sensibles ou tokens en clair).
- [ ] Scripts de validation exécutés :
  - [ ] `pwsh ./scripts/build.ps1`
  - [ ] `pwsh ./scripts/test.ps1`
- [ ] Vérifications CLI exécutées :
  - [ ] `dotnet build`
  - [ ] `dotnet test`
