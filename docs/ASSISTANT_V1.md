# Assistant V1 (mémoire ancrée)

## Objectifs
- Répondre uniquement à partir de la mémoire interne (records, historique, insights persistés).
- Citer les sources via leurs `RecordId` pour chaque réponse.
- Refuser/indiquer clairement quand aucune donnée n'est disponible.

## Composants
- **MemoryContextBuilder** : agrège le contexte pertinent à partir de la recherche globale (`ISearchService`), de l'historique (`ILifeService`) et des insights mémorisés (`IMemoryIntelligenceService`).
- **ChatAnswerer** : assemble un prompt strict, envoie la requête au modèle LLM et parse les citations. Fallback explicite si le contexte est vide ou la réponse illisible.
- **Contracts** : `AssistantAnswerRequest`, `MemoryContextRequest`, `MemoryContextResult`, `AssistantAnswer` exposés dans `Aion.Domain`.
- **DI** : `IMemoryContextBuilder` et `IChatAnswerer` enregistrés via `AddAionAi` et `AddAiAdapters`.

## Prompt (résumé)
```
Tu es l'assistant AION. Langue: <locale>.
Tu DOIS répondre uniquement avec les éléments ci-dessous. Cite les sources via leurs RecordId dans [brackets].
Si une information manque, dis-le explicitement et n'invente rien.
Réponds uniquement en JSON compact: {"message":"...","citations":["guid"],"fallback":false}.
Contexte mémoire (records, history, insights):
- id:<guid> type:<type> titre:<title> extrait:<snippet> score:<score>
Question utilisateur: <question>
Fin du contexte. Ne sors pas de ces informations.
```

## Tests
- Couverture via `tests/Aion.AI.Tests/AssistantContextTests.cs` :
  - Construction de contexte multi-sources.
  - Fallback si contexte vide.
  - Parsing des citations LLM avec filtrage sur les IDs connus.

## Notes
- Les limites de contexte sont configurables (`RecordLimit`, `HistoryLimit`, `InsightLimit`).
- Le modèle ne doit jamais inventer de contenu hors des éléments fournis.
- Le fallback mentionne explicitement l'absence de données ou une réponse invalide.
