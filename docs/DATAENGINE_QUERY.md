# DataEngine Query Strategy

## Choix d'indexation

Nous avons retenu **l'option B** (table `RecordIndexes` de type EAV) afin d'obtenir des filtres et tris stables sur SQLite/SQLCipher :

- Les expressions `json_extract` ne sont pas indexables de façon portable dans SQLite, ce qui rend les filtres `WHERE json_extract(...) = ...` trop lents dès que les tables grossissent.
- Une table d'index dédiée stocke les valeurs normalisées par champ (`StringValue`, `NumberValue`, `DateValue`, `BoolValue`) et offre des index composites (`EntityTypeId`, `FieldName`, valeur) pour les égalités et comparaisons.
- Les entrées sont maintenues côté service lors des insertions/mises à jour et supprimées en cascade lors des deletions, garantissant la cohérence sans triggers SQL supplémentaires.

## Résolution de requêtes

- **Filtres structurés** : chaque `QueryFilter` se traduit en sous-requête sur `RecordIndexes` pour le champ ciblé, avec opérateurs `Equals`, `GreaterThan(*)`, `LessThan(*)` et `Contains` (LIKE escapé) côté SQL.
- **Tri/pagination** : l'ordre est appliqué en SQL en s'appuyant sur les valeurs présentes dans `RecordIndexes`, puis `Skip/Take` est appliqué côté base.
- **Full-text** : la recherche `FullText` passe par la table virtuelle FTS5 `RecordSearch` et l'opérateur `MATCH`.
- **Projection et vues** : les filtres de vues sont convertis en filtres d'égalité puis combinés avec les filtres structurés dans la même requête SQL.

## Pourquoi cette approche ?

- Compatible SQLCipher et EF Core sans dépendre d'extensions SQLite spécifiques.
- Indexes dédiés pour égalités/plages, et FTS5 séparée pour la recherche plein texte.
- Pas de traitement en mémoire : filtres, tris et pagination sont tous délégués au moteur SQL.
