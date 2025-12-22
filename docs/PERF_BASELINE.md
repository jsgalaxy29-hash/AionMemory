# Baseline de performance DataEngine / Search

## Périmètre
- `Aion.Infrastructure.Services.AionDataEngine`
- Chemin critique mesuré : insert massif, requête paginée, recherche FTS `MATCH`.
- Storage : SQLite en mémoire + migrations existantes (`AionDbContext`).

## Méthodologie
- Benchmarks implémentés avec [BenchmarkDotNet](https://benchmarkdotnet.org/) dans le projet `tests/Aion.Benchmarks`.
- Configuration `SimpleJob(Net100, short)` avec 1 warmup / 1 itération (`--job short --warmupCount 1 --iterationCount 1`) pour limiter la durée sur CI manuelle.
- Jeu de données :
  - 1 table dédiée aux insertions massives (10k enregistrements) réinitialisée à chaque itération.
  - 1 table dédiée aux requêtes et à la recherche, pré-peuplée avec 5k enregistrements (champs `title`, `priority`, `content`).
  - Recherche plein-texte via la vue FTS `RecordSearch` déjà provisionnée par les migrations.
- Mesures capturées :
  - `Insert 10k records` (10 000 appels `InsertAsync`).
  - `Paginated query (50 items)` (`QueryAsync` avec `Skip=200`, `Take=50`, tri sur `priority`).
  - `FTS search (MATCH)` (`SearchAsync` avec `Take=20`).

## Exécution
- Local :
  ```bash
  dotnet run --project tests/Aion.Benchmarks/Aion.Benchmarks.csproj --configuration Release -- --job short --warmupCount 1 --iterationCount 1
  ```
- GitHub Actions manuel : workflow `.github/workflows/benchmarks.yml` (job `run-benchmarks`). Il publie `BenchmarkDotNet.Artifacts` en artefact.

## Résultats
> ⚠️ Impossible d’exécuter les benchmarks dans l’environnement sandbox : le SDK .NET 10 n’est pas disponible et le téléchargement est bloqué (403). Lancer le workflow manuel sur GitHub pour obtenir les métriques ci-dessous.

| Benchmark | Mean (ms) | Error | StdDev |
| --- | --- | --- | --- |
| Insert 10k records | _à remplir via workflow_ | | |
| Paginated query (50 items) | _à remplir via workflow_ | | |
| FTS search (MATCH) | _à remplir via workflow_ | | |

## Notes
- Les benchmarks utilisent `NullSearchService` pour éviter l’indexation externe, en s’appuyant uniquement sur la FTS SQLCipher/SQLite existante.
- Les tables de bench réutilisent le même schéma que la prod (métadonnées + index), sans ad hoc SQL hors migrations.
- Le workflow est volontairement manuel pour ne pas bloquer la CI principale.
