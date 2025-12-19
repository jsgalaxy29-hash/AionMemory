# ARCHITECTURE_RULES

## Couche et dépendances
- `Aion.Domain` est autonome (contrats, modèles, QuerySpec) : aucune dépendance vers l’Infrastructure ou l’UI.
- `Aion.Infrastructure` implémente les contrats Domain et fournit l’EF Core/SQLCipher. Les appels se font uniquement via les interfaces Domain.
- `Aion.AppHost`/UI consomment les services via `RecordQueryService`, `TableDefinitionService` ou `IDataEngine` injecté. Pas d’accès direct au `DbContext`.
- L’UI ne référence jamais `Aion.Infrastructure` directement ; la résolution se fait via DI.

## DataEngine
- Toute manipulation de données passe par `IDataEngine` et `QuerySpec` (filtres structurés, pagination, FTS). Pas de requêtes SQL brutes depuis l’UI.
- Les définitions de tables (`STable`, `SFieldDefinition`, `SViewDefinition`) vivent dans Domain ; les validations sont assurées côté Infrastructure avant persistance.
- Les vues (`SViewDefinition`) sont utilisées pour exprimer des filtres simples ; privilégier `QuerySpec` pour la logique avancée.

## Infrastructure / SQLCipher
- La configuration DB passe par `Aion:Database:*` et `AionDatabaseOptions`; la clé SQLCipher n’est jamais injectée dans la chaîne de connexion.
- `SqliteConnectionFactory` + `SqliteEncryptionInterceptor` sont les seuls points d’entrée pour ouvrir la base ; ne pas créer de `SqliteConnection` en dehors d’EF Core.
- Les migrations EF Core doivent couvrir STable/SField/SView et les tables FTS; ajouter les triggers FTS dans les migrations, pas dans le code runtime.

## Aion.AI
- La sélection de provider se fait via `AiProviderSelector` + keyed services (`http`, `openai`, `mistral`, `local`). Ne pas instancier de provider en direct.
- Les orchestrateurs (`IntentRecognizer`, designers, interpreters) doivent tolérer des réponses invalides et retourner un fallback structuré plutôt que propager l’erreur.
- Les tests IA utilisent des stubs/mock HTTP ; aucun appel réseau réel en CI.

## MAUI / Blazor
- Les composants UI utilisent les services d’orchestration (`RecordQueryService`, `ModuleViewService`) et ne manipulent pas les options SQL/AI directement.
- Les dépendances UI → Infrastructure sont interdites ; injecter uniquement des contrats Domain ou des services AppHost.
- Les règles de navigation/blocage plateforme (Android/iOS) doivent rester dans les services (ex : backups), pas dans les composants.

## Conventions de contribution
- Branches de travail : `feature/*`, `fix/*`, ou branches dédiées (ex : `feature/dataengine-hardening`).
- Messages de commit en impératif court, décrivant l’effet métier (ex : “Add DataEngine integration tests”).
- Pas de ressources sensibles en repo (clé SQLCipher, API keys) : utiliser variables d’environnement ou secrets locaux.
