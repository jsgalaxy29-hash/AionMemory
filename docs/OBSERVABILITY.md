# Observabilité

Ce dépôt trace désormais les opérations clés avec des IDs de corrélation et des durées mesurées, sans exposer les charges utiles sensibles.

## Corrélation & scopes

- `OperationContext` (Domain) transporte `CorrelationId` (TraceId) et `OperationId` (SpanId) dérivés d’une `Activity`.
- `IOperationScopeFactory`/`OperationScopeFactory` (Infrastructure) créent des scopes `Activity` et des scopes `ILogger` associés (`CorrelationId`, `OperationId`, `Operation`, `TableId`, `RecordId`, etc.).
- Un fallback `NoopOperationScopeFactory` est enregistré côté AI pour garantir que la pipeline ne plante pas si aucun logger/observabilité n’est configuré (utile en tests ou en mode stub).
- L’UI MAUI force le format W3C pour les `Activity` afin de garantir des IDs de corrélation cohérents dans l’ensemble de l’application.

## Journalisation structurée

- `AionDataEngine` trace les opérations CRUD et de recherche (`DataEngine.CreateTable/Insert/Update/Delete/Query/QueryResolved/Search`) avec la corrélation et la durée (ms). Aucun log ne contient le `DataJson` complet.
- `ModuleApplier` trace `Module.Apply` avec le slug du module et la durée appliquée.
- Les scopes sont ouverts dès le début de l’opération pour embarquer les IDs de corrélation sur tous les logs imbriqués.

## Prompts IA et redaction

- Les prompts ne sont **jamais** journalisés en clair par défaut. Les logs utilisent une version redacted `[redacted:length=…]`.
- L’option `Aion:Ai:EnablePromptTracing` (bool) permet de journaliser les prompts en clair **uniquement** en niveau `Debug` si activée explicitement.
- `IntentRecognizer` et `BasicIntentDetector` mesurent la durée des appels au LLM et ajoutent les scopes de corrélation sans exposer le contenu utilisateur.

## Métriques unifiées

- Les métriques sont émises via `System.Diagnostics.Metrics` et restent inertes si aucun sink n’est configuré.
- **DataEngine**: `aion.dataengine.duration_ms` + `aion.dataengine.errors` avec `operation=Create|Update|Query|Search`.
- **AI**: `aion.ai.request.duration_ms`, `aion.ai.request.errors`, `aion.ai.request.retries`, `aion.ai.tokens` et `aion.ai.request.cost` (si la réponse fournisseur expose un `usage` exploitable).
- **Sync**: `aion.sync.replays` pour les opérations déjà appliquées, `aion.sync.conflicts` pour les conflits détectés.

## Migrations & alertes

- Les migrations SQLCipher/EF Core échouées sont logguées en **critique** (`LogCritical`) avec la source de données.
- L’UI remonte un message minimal si l’initialisation de la base échoue, sans exposer d’informations sensibles.

## Bonnes pratiques

- Conserver la journalisation structurée (`ILogger.BeginScope` avec dictionnaires) pour enrichir automatiquement les traces.
- Ne jamais logguer de `DataJson`, de prompts ou de secrets ; préférer des longueurs, IDs ou métadonnées.
- Réutiliser `IOperationScopeFactory` dans les nouvelles opérations longues (ex: nouvelles commandes DataEngine ou IA) afin de bénéficier des corrélations automatiques et des mesures de latence.
