# Release notes

Créez un fichier par version publiée sous la forme `docs/release-notes/vMAJOR.MINOR.PATCH.md`.
Chaque fichier doit suivre les attentes de `VERSIONING.md` :

- sections explicites `## Nouveautés`, `## Corrections`, `## Points de vigilance` pour tracer les changements,
- mentions des migrations de données, impacts de configuration (`appsettings.*`), et modifications de surface publique.

Les notes sont validées automatiquement en CI lors d'un tag `v*` ; un fichier manquant ou sans sections obligatoires fait échouer la pipeline.
