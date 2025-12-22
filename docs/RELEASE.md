# Préparer une release

Ce guide couvre la préparation d'une release sans publication store. Il applique les règles de `VERSIONING.md` et aligne la CI sur le processus (tags et changelog).

## Prérequis
- Windows avec le workload `.NET MAUI` installé (`dotnet workload install maui`).
- Accès aux scripts racine : `pwsh ./scripts/build.ps1`, `pwsh ./scripts/test.ps1` et `tools/publish.ps1`.
- Aucun secret ou configuration sensible ne doit être présent dans le dépôt (seuls les `appsettings.*.example.json` sont autorisés).

## Versioning & release notes
1. Choisir le tag : `vMAJOR.MINOR.PATCH` (suffixes `-beta.N`/`-rc.N` autorisés pour les pré-versions).
2. Mettre à jour les versions applicatives dans les manifests si nécessaire.
3. Créer les notes dans `docs/release-notes/vX.Y.Z.md` avec les sections obligatoires :
   - `## Nouveautés`
   - `## Corrections`
   - `## Points de vigilance`
   Documenter les migrations (EF/DataEngine), changements d'appsettings et évolutions de surface publique.
4. Valider localement si besoin : `pwsh ./tools/validate-release-tag.ps1 -Tag vX.Y.Z`.

## Build et tests
Avant de tagger, exécuter :

```pwsh
pwsh ./scripts/build.ps1
pwsh ./scripts/test.ps1
```

Pour vérifier la compatibilité CLI :

```pwsh
dotnet build AionMemory.slnx -c Release
dotnet test AionMemory.slnx -c Release
```

## Publier les artefacts AppHost (local)
Utilisez `tools/publish.ps1` depuis Windows pour générer les binaires MAUI (sans signature) :

```pwsh
pwsh ./tools/publish.ps1 -Targets windows -Configuration Release
```

- Les artefacts sont générés dans `artifacts/publish/Aion.AppHost` avec une archive `.zip` par cible.
- Le script vérifie qu'aucun fichier `appsettings*.json` autre que les variantes `.example.json` n'est embarqué.

## Pipeline CI
- La CI se déclenche sur `v*` et refuse un tag qui ne suit pas le schéma ou sans release notes complètes.
- Sur tag valide :
  - Build + tests (Release).
  - Validation des notes (`docs/release-notes/vX.Y.Z.md`).
  - Publication automatique des artefacts AppHost Windows en `.zip`.

## Tag et push
1. Créer le tag après validation locale : `git tag vX.Y.Z`.
2. Pousser le tag et les notes : `git push origin main --tags`.
3. Vérifier la pipeline GitHub Actions : artefacts AppHost disponibles, aucun échec sécurité (gitleaks) ou signature requise.

> Code signing : la pipeline produit des builds non signés (`WindowsPackageType=None`). Documenter toute exception éventuelle plutôt que de bloquer la release.
