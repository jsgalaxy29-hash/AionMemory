# Preparer une release

Ce guide couvre la preparation d'une release sans publication store. Il applique les regles de `VERSIONING.md`.

## Prerequis

- Windows avec le workload `.NET MAUI` installe.
- Acces aux scripts racine : `pwsh ./scripts/build.ps1`, `pwsh ./scripts/test.ps1` et `tools/publish.ps1`.
- Aucun secret ni configuration sensible ne doit etre present dans le depot.

## Versioning & release notes

1. Choisir le tag : `vMAJOR.MINOR.PATCH` (suffixes `-beta.N`/`-rc.N` autorises).
2. Mettre a jour les versions applicatives dans les manifests si necessaire.
3. Creer les notes dans `docs/release-notes/vX.Y.Z.md` avec les sections obligatoires :
   - `## Nouveautes`
   - `## Corrections`
   - `## Points de vigilance`
4. Valider localement si besoin : `pwsh ./tools/validate-release-tag.ps1 -Tag vX.Y.Z`.

## Build et tests

Avant de tagger :

```pwsh
pwsh ./scripts/build.ps1
pwsh ./scripts/test.ps1
```

Verification CLI :

```pwsh
dotnet build AionMemory.slnx -c Release
dotnet test AionMemory.slnx -c Release
```

## Publier les artefacts AppHost en local

```pwsh
pwsh ./tools/publish.ps1 -Targets windows -Configuration Release
```

- Les artefacts sont generes dans `artifacts/publish/Aion.AppHost`.
- Le script verifie qu'aucun `appsettings*.json` non-example n'est embarque.

## Pipeline CI

Si une pipeline de release est branchee au depot, elle devrait idealement :

- verifier le schema du tag ;
- verifier la presence des release notes ;
- rejouer build + tests en Release ;
- publier les artefacts AppHost attendus.

Ne supposez pas l'existence d'une GitHub Actions particuliere tant qu'elle n'est pas versionnee dans le depot.

## Tag et push

1. Creer le tag apres validation locale : `git tag vX.Y.Z`.
2. Pousser le tag et les notes : `git push origin main --tags`.
3. Verifier les artefacts et les checks du systeme CI reel utilise par l'equipe.

> Code signing : les builds actuels sont non signes (`WindowsPackageType=None`) tant qu'une strategie de signature n'est pas ajoutee explicitement.
