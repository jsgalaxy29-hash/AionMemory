# Integration continue

Ce document decrit la CI ciblee pour le depot. A la date de mise a jour de cette documentation, aucun dossier `.github/` n'est versionne ici ; les etapes ci-dessous doivent donc etre lues comme le pipeline attendu, pas comme un workflow present dans le repo.

## Pipeline attendu

- Plateforme : Windows pour couvrir `Aion.AppHost` et les workloads MAUI.
- SDK .NET : version alignee sur `global.json`.
- Etapes minimales :
  1. `dotnet restore AionMemory.slnx`
  2. `dotnet build AionMemory.slnx -c Release`
  3. `dotnet test AionMemory.slnx -c Release`
- Verifications souhaitees :
  - `dotnet format --verify-no-changes`
  - collecte des resultats de test
  - eventuel scan de secrets

## Commandes locales recommandees

```bash
dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx -c Release
dotnet test AionMemory.slnx -c Release
```

Ou, en PowerShell :

```pwsh
pwsh ./scripts/build.ps1
pwsh ./scripts/test.ps1
```

## Notes

- Si une GitHub Actions est ajoutee plus tard, cette doc devra etre alignee sur les workflows reels du depot.
- Les jobs MAUI, secrets scan et publication d'artefacts ne doivent etre documentes ici que lorsqu'ils existent effectivement dans le repo.
