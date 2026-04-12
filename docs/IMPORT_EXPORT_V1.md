# Import/Export v1 (offline JSON)

## Objectif
Exporter puis réimporter une “mémoire” (modules, enregistrements et pièces jointes) sous forme de fichiers JSON/NDJSON ou d’une archive `.zip`, sans dépendance réseau.

## Contenu du paquet
- `manifest.json` : métadonnées d’export (version `1.0`, compteurs, fichiers référencés, mapping des tables).
- `modules.json` : liste des `ModuleSpec` (voir `docs/MODULE_BUILDER_V1.md`) représentant les tables et vues.
- `records.ndjson` : un enregistrement JSON par ligne `{ table, tableId, recordId, data, createdAt, updatedAt, deletedAt }`.
- `attachments.json` : manifeste des pièces jointes avec empreinte SHA-256 et liens.
- `attachments/…` : fichiers binaires correspondants aux entrées du manifeste.

## Export
- Service : `IDataExportService` (`Aion.Infrastructure.Services.DataExportService`).
- Sortie : dossier cible ou archive `.zip` (`asArchive=true`).
- Conserve les IDs de tables et d’enregistrements. Les pièces jointes sont copiées en clair (si déjà protégées par SQLCipher, pas de chiffrage additionnel).

## Import
- Service : `IDataImportService` (`Aion.Infrastructure.Services.DataImportService`).
- Idempotent : si une table du même `slug` existe, ses enregistrements sont mis à jour (sinon insérés). Les fichiers déjà présents (même `FileId`) ne sont pas réimportés.
- Ordre appliqué : modules → enregistrements → pièces jointes (avec liens).

## Notes de sécurité
- Aucun secret n’est exporté.
- Les pièces jointes sont copiées telles quelles ; utiliser un stockage chiffré (SQLCipher + `StorageOptions`) pour protéger les données au repos.

## Vérifications recommandées
- Avant PR : `pwsh ./scripts/build.ps1` puis `pwsh ./scripts/test.ps1`.
- En local CLI : `dotnet build` puis `dotnet test`.
