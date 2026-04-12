# Recherche plein texte (FTS5)

La recherche plein texte est exposée via `IDataEngine.SearchAsync(tableId, query, options)`, distincte de `QueryAsync` pour la pagination/ranking dédiés.

## Syntaxe de requête
- Repose sur FTS5 SQLite (`MATCH`).
- Opérateurs pris en charge : `AND` (par défaut), `OR`, guillemets pour les expressions exactes (`"alpha beta"`), `NEAR/` pour la proximité, et le préfixe `*` (`alph*`) pour les préfixes.
- Les requêtes sont envoyées telles quelles au moteur FTS5 : valider/échapper côté appelant si nécessaire.

## Ranking & snippets
- Le score utilise `bm25(RecordSearch)` inversé (`Score = 1 / (bm25 + 1)`) pour retourner les meilleurs documents en premier.
- Les snippets proviennent de `snippet(RecordSearch, 2, HighlightBefore, HighlightAfter, ' … ', SnippetTokens)`.
- Les marqueurs par défaut sont `<mark>`/`</mark>`. Fournir des marqueurs sûrs pour l’UI (ex. `<b>`/`</b>`).

## Options
`SearchOptions` applique automatiquement la pagination (obligatoire) :
- `Take` : taille de page (défaut 20, max 100).
- `Skip` : offset (défaut 0).
- `HighlightBefore` / `HighlightAfter` : balises d’enrobage pour les termes trouvés.
- `SnippetTokens` : nombre de tokens autour des correspondances (défaut 12).
- `Language` : réservé pour une future config FTS/colline, non exploité à ce stade.

## Limitations connues
- L’index FTS est alimenté par `Records` via les triggers `RecordSearch_*` (contenu JSON brut). Pas d’indexation champ par champ ni pondération spécifique.
- Pas de LIKE sur gros volumes : la recherche passe par `MATCH` uniquement.
- Les résultats incluent `RecordId`, `Score`, `Snippet` ; recharger l’enregistrement complet via `GetAsync` ou `QueryAsync` si nécessaire.
