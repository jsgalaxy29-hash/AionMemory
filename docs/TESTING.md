# Tests & qualité

## Objectifs
- Tests déterministes (sans accès réseau) pour Domain / Infrastructure / AI / UI.
- Couverture exportable (XPlat Code Coverage).
- Vérifications de format et analyzers en CI.
- Exécution en local via scripts standard.

## Commandes locales (recommandées)
```pwsh
pwsh ./scripts/build.ps1
pwsh ./scripts/test.ps1
```

## Commandes CLI (compatibilité)
```sh
dotnet build
```

```sh
dotnet test
```

## Couverture
```sh
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults
```
Le collecteur `coverlet.collector` est référencé pour les projets de test.

## Mocks & offline
Les tests doivent rester offline :
- `Aion.AI/Providers.Mock/MockAiProviders.cs` fournit des modèles IA et transcription déterministes.
- Les tests d’infrastructure utilisent des stores en mémoire (ex. `InMemoryCloudObjectStore`).
- Aucune requête réseau réelle n’est autorisée pendant les tests.

## Golden tests IA
Les tests de référence (golden) valident des prompts représentatifs et des sorties attendues avec des mocks :
- `tests/Aion.AI.Tests/IntentDetectorGoldenTests.cs`
- `tests/Aion.AI.Tests/IntentRouterGoldenTests.cs`

## UI (bUnit)
Des tests bUnit couvrent les flows clés dans `tests/Aion.AppHost.UI.Tests`.

## CI & quality gates
Le workflow CI exécute :
- `dotnet format --verify-no-changes`
- `dotnet test` avec collecte de couverture
- Build MAUI nightly (job planifié)
