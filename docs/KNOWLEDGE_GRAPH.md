# Knowledge Graph v1 (relations explicites)

Cette première itération expose un graphe de connaissances personnel basé exclusivement sur des relations explicites entre enregistrements existants dans le DataEngine.

## Concepts
- **KnowledgeNode** : représente un enregistrement (`TableId`, `RecordId`) et stocke un titre résolu pour l’affichage.
- **KnowledgeEdge** : relation dirigée entre deux nœuds, typée (`LinkedTo`, `DependsOn`, `RelatedTo`).
- **KnowledgeGraphSlice** : sous‑ensemble du graphe centré sur un nœud racine (utilisé pour la navigation).

## Persistance
- Nouvelles tables SQLite : `KnowledgeNodes` (unique par `TableId`/`RecordId`) et `KnowledgeEdges` (unique par `(FromNodeId, ToNodeId, RelationType)`).
- Index : contraintes d’unicité ci‑dessus, index supplémentaire sur `RelationType`.
- Création via la migration `20250425000000_KnowledgeGraph`.

## API DataEngine
Deux nouvelles méthodes sur `IDataEngine` / `IAionDataEngine` :

- `LinkRecordsAsync(fromTableId, fromRecordId, toTableId, toRecordId, relationType)`  
  - Vérifie l’existence des tables et enregistrements.  
  - Génère/actualise les nœuds avec un titre dérivé du `RowLabelTemplate` puis du premier champ texte.  
  - Évite les doublons en réutilisant un edge existant pour la même paire et le même type.

- `GetKnowledgeGraphAsync(tableId, recordId, depth)`  
  - Garantit la présence du nœud racine.  
  - Parcourt en largeur les edges connectés jusqu’à la profondeur demandée (0 = seulement le nœud).  
  - Retourne `KnowledgeGraphSlice` contenant le nœud racine, les nœuds visités et les edges rencontrés.

## Limitations connues
- Relations dirigées uniquement (pas de symétrie automatique).  
- Aucune pondération/score : seules les relations déclarées sont prises en compte.  
- Pas de suppression cascade automatique côté DataEngine : si un enregistrement est supprimé manuellement hors DataEngine, les nœuds/edges correspondants restent à nettoyer via la base (les FKs empêchent les orphelins).

## Tests
- Tests unitaires (`tests/Aion.Tests/DataEngineKnowledgeGraphTests.cs`) couvrant la création d’edges, la résolution des titres de nœuds et le respect de la profondeur de parcours.
