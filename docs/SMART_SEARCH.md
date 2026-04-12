# SMART_SEARCH v1

## Objectif

Combiner la recherche plein texte FTS5 existante et une couche sémantique optionnelle pour mieux classer les enregistrements sans perdre l’aspect explicable des résultats.

## Architecture

- **FTS5** reste la base : les requêtes sont évaluées sur la vue `RecordSearch` avec `bm25` et snippets générés côté SQLite.
- **Embeddings** : une nouvelle table `Embeddings` stocke, par enregistrement, un vecteur sérialisé (`EntityTypeId`, `RecordId`, `Vector`). Les vecteurs sont générés via `IEmbeddingProvider` lorsque ce provider est disponible.
- **Lecture paginée** : la récupération des embeddings pour la requête sémantique se fait par pages afin de limiter l’usage mémoire lorsque la table grossit.
- **Indexation** : à chaque `Insert`/`Update`, `AionDataEngine` recalculle les indexes structurés et, si possible, l’embedding correspondant. Suppression hard = cascade sur la ligne d’embedding.

## Algorithme `SearchSmartAsync`

1. **FTS** : exécute la requête FTS (plafonnée à 3× la pagination demandée) pour obtenir score BM25 inversé + snippet.
2. **Sémantique** (optionnel) : si un `IEmbeddingProvider` est configuré et répond, un embedding de la requête est comparé (cosine) aux vecteurs présents dans `Embeddings` pour la table ciblée.
3. **Fusion** : les scores sont combinés (`65%` FTS / `35%` cosine lorsque les deux sont présents). Les résultats purement sémantiques sont conservés mais restent reclassés avec les résultats FTS, puis `Skip/Take` sont appliqués.
4. **Explicabilité** : les snippets proviennent toujours du contenu FTS ou d’un fallback textuel (`DataJson` tronqué), jamais d’une hallucination modèle.

## Comportements attendus

- **Fallback automatique** : sans provider d’embeddings ou en cas d’erreur d’appel, `SearchSmartAsync` retombe sur le classement FTS standard (mêmes snippets, mêmes résultats).
- **Sécurité des données** : aucun secret ou configuration modèle n’est stocké en base ; seule la valeur vectorielle sérialisée (JSON) est conservée.
- **Compatibilité** : les tests utilisent un provider déterministe pour vérifier la fusion FTS + cosinus sur un dataset réduit.

## Points de configuration

- Enregistrer un `IEmbeddingProvider` (OpenAI/Mistral/HTTP/local) via la DI pour activer l’indexation sémantique.
- Les poids FTS/Sémantique sont codés dans `AionDataEngine` (0,65 / 0,35) et peuvent être ajustés ultérieurement si nécessaire.
