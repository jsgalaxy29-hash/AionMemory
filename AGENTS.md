# AGENTS

## Portée
Ces règles s'appliquent à l'intégralité du dépôt AionMemory.

## Architecture
- La solution est structurée en quatre projets : `Aion.Domain` (contrats/invariants), `Aion.Infrastructure` (implémentations, EF Core + SQLCipher), `Aion.AI` (orchestrateurs/providers IA) et `Aion.AppHost` (MAUI/Blazor pour l'UI/DI/navigation).
- `Aion.Domain` reste autonome : aucune dépendance vers l'Infrastructure ou l'UI ; seules les interfaces/contrats transitent vers les autres couches.
- Toute nouvelle fonctionnalité passe par l'injection de dépendances (pas de service locator ou new direct sur les services métiers dans l'UI/Infrastructure).

## Nullabilité et qualité
- La nullabilité est activée partout ; préférer les types non-null et valider explicitement les entrées/options.
- Activer les avertissements/analyseurs .NET par défaut ; corriger les avertissements introduits par les nouvelles modifications.

## Données et persistance
- L'accès aux données s'appuie sur EF Core et SQLite chiffré (SQLCipher) depuis `Aion.Infrastructure`. Pas de connexions SQLite brutes en dehors des points d'entrée existants.
- Les migrations et triggers FTS doivent vivre dans les migrations EF Core ; éviter les scripts SQL ad-hoc côté runtime.

## Sécurité et secrets
- Aucun secret/versionnage de clés dans le dépôt. Utiliser les fichiers `appsettings.*.example.json`, les variables d'environnement ou `dotnet user-secrets` pour toute valeur sensible.

## Commandes de contrôle
- Commandes attendues avant livraison : `dotnet build` puis `dotnet test` à la racine.
