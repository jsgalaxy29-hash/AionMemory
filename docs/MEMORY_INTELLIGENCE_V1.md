# Memory Intelligence v1

La première itération de la mémoire intelligente fournit un pipeline explicite pour transformer des lots de données (notes, événements, enregistrements) en insights structurés. Le calcul est toujours déclenché explicitement par l'application ou les tests : aucune tâche en arrière-plan n'est lancée automatiquement.

## Fonctionnalités
- **Résumé automatique** : synthèse textuelle d'un lot de `MemoryRecord`.
- **Regroupement** : extraction de sujets/thèmes (`MemoryTopic`) pour catégoriser le lot.
- **Suggestions de liens** : propositions de liens croisés entre enregistrements (`MemoryLinkSuggestion`).

## Flux
1. L'UI ou un orchestrateur compose une `MemoryAnalysisRequest` avec la liste des `MemoryRecord` concernés (notes, événements, enregistrements métier).
2. `IMemoryAnalyzer` (implémenté par `MemoryAnalyzer` ou un mock) produit un `MemoryAnalysisResult` structuré.
3. `MemoryIntelligenceService` persiste l'insight dans la table `MemoryInsights` via `MemoryInsight.FromAnalysis`.
4. Les insights peuvent être relus via `IMemoryIntelligenceService.GetRecentAsync` pour l'UI (widgets, timeline, etc.).

## Modèle de données
- **MemoryInsight** : summary + JSON stockant les topics et les liens suggérés, compteur d'enregistrements et horodatage de génération.
- Table `MemoryInsights` avec index sur `GeneratedAt` pour récupérer rapidement les dernières synthèses.

## Tests
- Tests AI : parsing JSON, fallback robuste, mock provider pour validation rapide.
- Tests Infrastructure : persistance SQLite in-memory et ordre de restitution.

## Configuration
- `IMemoryAnalyzer` est injecté via DI (scoped). Les environnements sans LLM peuvent utiliser `MockMemoryAnalyzer` ou les stubs (`AddAiAdapters`).
- Aucun déclencheur automatique : l'appel à `IMemoryIntelligenceService.AnalyzeAsync` doit être initié par l'application.
