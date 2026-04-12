# AGENTS

## Portee

Ces regles s'appliquent a l'integralite du depot AionMemory.

## Architecture

- La solution est structuree autour de quatre couches principales : `Aion.Domain` (contrats/invariants), `Aion.Infrastructure` (implementations, EF Core + SQLCipher), `Aion.AI` (orchestrateurs/providers IA) et `Aion.AppHost` (MAUI/Blazor pour l'UI/DI/navigation).
- Le depot contient aussi des projets de support comme `Aion.Composition` (composition racine), les providers IA specialises (`Aion.AI/Providers.OpenAI`, `Aion.AI/Providers.Mistral`) et `Aion.RecoveryTool`.
- `Aion.Domain` reste autonome : aucune dependance vers l'Infrastructure ou l'UI ; seules les interfaces/contrats transitent vers les autres couches.
- Dans `Aion.Domain`, pas d'EF Core, pas d'IO direct, pas d'UI et aucune dependance sortante.
- L'injection de dependances est obligatoire.
- EF Core + SQLite/SQLCipher sont confines a `Aion.Infrastructure`. Pas de connexion SQL brute ailleurs.

## Nullabilite et qualite

- La nullabilite est activee partout ; preferer les types non-null et valider explicitement les entrees/options.
- Activer les avertissements/analyseurs .NET par defaut ; corriger les avertissements introduits par les nouvelles modifications.
- Pas de breaking changes silencieux : toute modification de contrat ou de surface publique doit etre documentee et validee.

## Donnees et persistance

- L'acces aux donnees s'appuie sur EF Core et SQLite chiffre (SQLCipher) depuis `Aion.Infrastructure`.
- Les migrations et triggers FTS doivent vivre dans les migrations EF Core ; eviter les scripts SQL ad-hoc cote runtime.

## Securite et secrets

- Aucun secret ni versionnage de cles dans le depot.
- Utiliser les fichiers `appsettings.*.example.json`, les variables d'environnement, `dotnet user-secrets` ou le stockage securise local MAUI pour toute valeur sensible.

## Commandes de controle

- Toujours executer `pwsh ./scripts/build.ps1` puis `pwsh ./scripts/test.ps1` avant une PR.
- Commandes attendues avant livraison : `dotnet build` puis `dotnet test` a la racine pour verifier la compatibilite CLI.

## Definition of Done

- Build et tests passent (Release) via les scripts fournis.
- Aucun avertissement supplementaire introduit par la modification.
