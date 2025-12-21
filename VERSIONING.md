# VERSIONING

## Tags
- Utiliser le schéma `vMAJOR.MINOR.PATCH` (ex : `v1.3.0`).
- Tagger depuis la branche principale après validation CI et publication des artefacts.
- Les versions pré-release utilisent un suffixe (`-beta.1`, `-rc.1`) aligné sur les builds de validation.

## Release notes (minimales)
- Chaque tag doit être accompagné d’un changelog synthétique : nouveautés, corrections, points de vigilance.  
- Mettre en avant les impacts migrations (DataEngine/EF Core), les changements de configuration (`appsettings.*`) et les évolutions de contrat public.
- Lier les tickets ou PRs majeures pour la traçabilité ; pas de contenus sensibles (logs, secrets).

## Processus
1. Mettre à jour la version applicative dans les props/manifestes concernés (AppHost, Infrastructure).  
2. Générer les binaires en Release via `pwsh ./scripts/build.ps1`.  
3. Rédiger les release notes minimales dans le dépôt (ex : `docs/release-notes/v1.3.0.md`) puis créer le tag `git tag v1.3.0`.  
4. Pousser tag et notes (`git push origin main --tags`) après validation `pwsh ./scripts/test.ps1`.  
