# Baseline de performance DataEngine / Search

## Perimetre

- `Aion.Infrastructure.Services.AionDataEngine`
- Chemin critique mesure : insert massif, requete paginee, recherche FTS `MATCH`
- Storage : SQLite en memoire + migrations existantes

## Methodologie

- Benchmarks implementes avec [BenchmarkDotNet](https://benchmarkdotnet.org/) dans `tests/Aion.Benchmarks`.
- Configuration courte pour limiter la duree d'execution.
- Jeu de donnees representatif des scenarios DataEngine / Search.

## Execution

En local :

```bash
dotnet run --project tests/Aion.Benchmarks/Aion.Benchmarks.csproj --configuration Release -- --job short --warmupCount 1 --iterationCount 1
```

## Resultats

Ce document decrit la methode et la commande de reference. Les metriques doivent etre renseignees a partir d'une execution reelle dans un environnement de benchmark stable.

| Benchmark | Mean (ms) | Error | StdDev |
| --- | --- | --- | --- |
| Insert 10k records | a mesurer | | |
| Paginated query (50 items) | a mesurer | | |
| FTS search (MATCH) | a mesurer | | |

## Notes

- Les benchmarks utilisent le schema et les migrations du projet.
- Si une automatisation ou un workflow de bench est ajoute plus tard, documenter ici son emplacement reel dans le depot.
