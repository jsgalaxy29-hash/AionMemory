# Tests & qualite

## Objectifs

- Tests deterministes et offline pour Domain / Infrastructure / AI / UI.
- Couverture exportable.
- Verification locale via scripts standard.
- Compatibilite CLI a la racine du depot.

## Commandes locales recommandees

```pwsh
pwsh ./scripts/build.ps1
pwsh ./scripts/test.ps1
```

## Commandes CLI

```sh
dotnet build
dotnet test
```

## Couverture

```sh
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults
```

Le collecteur `coverlet.collector` est reference par les projets de test.

## Mocks & offline

Les tests doivent rester offline :

- `Aion.AI/Providers.Mock/MockAiProviders.cs` fournit des modeles IA et transcription deterministes.
- Les tests d'infrastructure utilisent des stores en memoire, par exemple `InMemoryCloudObjectStore`.
- Aucun appel reseau reel n'est attendu pendant les tests.

## Golden tests IA

Les tests de reference valident des prompts representatifs et des sorties attendues avec des mocks :

- `tests/Aion.AI.Tests/IntentDetectorGoldenTests.cs`
- `tests/Aion.AI.Tests/IntentRouterGoldenTests.cs`

## UI

Des tests bUnit couvrent les flows cles dans `tests/Aion.AppHost.UI.Tests`.

## Quality gates attendus

Quand une CI est branchee sur le depot, les garde-fous attendus sont :

- `dotnet format --verify-no-changes`
- `dotnet test` avec collecte de couverture
- un build MAUI de validation ou planifie
