# Intégration Continue

Ce dépôt utilise une CI GitHub Actions pour éviter les régressions sur les branches principales et les branches de fonctionnalités.

## Workflow principal (`.github/workflows/ci.yml`)

- **Déclencheurs** : `push` sur `main`, `feature/**` et les tags `v*`, ainsi que `pull_request` vers `main`.
- **Plateforme** : Windows (`windows-latest`) afin de couvrir le projet MAUI.
- **SDK .NET** : version alignée sur `global.json` (`10.0.100-preview.6.24452.5`), avec installation préalable du workload `maui`.
- **Pipeline** :
  1. Restauration des dépendances : `dotnet restore AionMemory.slnx`
  2. Compilation Release : `dotnet build AionMemory.slnx --configuration Release --no-restore /p:ContinuousIntegrationBuild=true`
  3. Tests Release : `dotnet test AionMemory.slnx --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"`
  4. Publication des résultats de tests (.trx) comme artefacts.
- **Cache NuGet** : activé via `actions/setup-dotnet` avec les chemins `*.csproj`, `*.props`, `*.targets` et `packages.lock.json`.
- **Qualité** :
  - Nullabilité et analyseurs activés via `Directory.Build.props`.
  - `TreatWarningsAsErrors` appliqué aux projets Domain, Infrastructure et AI (ainsi qu'en CI via `CI=true`).
  - Build déterministe (`/p:ContinuousIntegrationBuild=true`).
- **Sécurité** : job `secret-scan` exécuté sur Ubuntu avec `gitleaks` pour prévenir toute fuite de secrets. Aucune configuration sensible réelle n'est versionnée ; seules des variantes `appsettings.*.example.json` sont fournies.

### Flux release (tag `v*`)
- Condition : tag `vMAJOR.MINOR.PATCH` (pré-releases `-beta.N`/`-rc.N` acceptées) poussé sur le dépôt.
- Étapes additionnelles :
  1. Validation du tag et des release notes via `tools/validate-release-tag.ps1 -Tag ${{ github.ref_name }}` (vérifie la présence de `docs/release-notes/<tag>.md` et des sections obligatoires).
  2. Publication des artefacts AppHost Windows en Release (non signés) via `tools/publish.ps1 -Targets windows -Configuration Release`, puis upload du `.zip` dans les artefacts d'action.
- Les builds restent non signés (`WindowsPackageType=None`) ; documenter les besoins de signature éventuels avant diffusion externe.

## Commandes locales recommandées

Alignez-vous sur la CI en exécutant :

```bash
dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx -c Release
dotnet test AionMemory.slnx -c Release
```

Pour les pipelines en PowerShell, utilisez `pwsh ./scripts/build.ps1` puis `pwsh ./scripts/test.ps1`.
