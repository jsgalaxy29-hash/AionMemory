# AGENTS

## Portée
Ces règles s'appliquent à l'intégralité du dépôt AionMemory.

## Architecture
- La solution est structurée en quatre projets : `Aion.Domain` (contrats/invariants), `Aion.Infrastructure` (implémentations, EF Core + SQLCipher), `Aion.AI` (orchestrateurs/providers IA) et `Aion.AppHost` (MAUI/Blazor pour l'UI/DI/navigation).
- `Aion.Domain` reste autonome : aucune dépendance vers l'Infrastructure ou l'UI ; seules les interfaces/contrats transitent vers les autres couches.
- Dans `Aion.Domain`, pas d'EF Core, pas d'IO direct, pas d'UI et aucune dépendance sortante. La nullabilité y est respectée comme dans le reste de la solution.
- L'injection de dépendances est obligatoire (pas de service locator, pas de `new` direct sur les services métiers dans l'UI/Infrastructure, pas de `new HttpClient` hors des points configurés).
- EF Core + SQLite/SQLCipher sont confinés à `Aion.Infrastructure`. Pas de connexion SQL brute ailleurs.

## Nullabilité et qualité
- La nullabilité est activée partout ; préférer les types non-null et valider explicitement les entrées/options.
- Activer les avertissements/analyseurs .NET par défaut ; corriger les avertissements introduits par les nouvelles modifications.
- Pas de breaking changes silencieux : toute modification de contrat ou de surface publique doit être documentée et validée.

## Données et persistance
- L'accès aux données s'appuie sur EF Core et SQLite chiffré (SQLCipher) depuis `Aion.Infrastructure`. Pas de connexions SQLite brutes en dehors des points d'entrée existants.
- Les migrations et triggers FTS doivent vivre dans les migrations EF Core ; éviter les scripts SQL ad-hoc côté runtime.

## Sécurité et secrets
- Aucun secret/versionnage de clés dans le dépôt. Utiliser les fichiers `appsettings.*.example.json`, les variables d'environnement ou `dotnet user-secrets` pour toute valeur sensible.

## Commandes de contrôle
- Toujours exécuter `pwsh ./scripts/build.ps1` puis `pwsh ./scripts/test.ps1` avant une PR.
- Commandes attendues avant livraison : `dotnet build` puis `dotnet test` à la racine pour vérifier la compatibilité CLI.

## Definition of Done
- Build et tests passent (Release) via les scripts fournis.
- Aucun avertissement supplémentaire introduit par la modification.
