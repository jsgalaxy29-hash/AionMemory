# Synchronisation locale-first (v1)

Cette itération active la synchronisation de la « mémoire » Aion entre plusieurs appareils sans dépendance à un cloud public. Le moteur reste agnostique du backend (WebDAV, S3-like, filesystem distant), et fournit un plan bidirectionnel avec résolution de conflits explicite.

## Contrats Domain

- `SyncItem` : métadonnées minimales d’un artefact synchronisé (chemin logique, `ModifiedAt`, `Version`, taille et empreinte optionnelles).
- `SyncState` : état comparé local/distant + action (Upload/Download/None/Conflict/Delete) et éventuel `SyncConflict`.
- `SyncConflict` : trace de la règle appliquée (`LastWriteWins` v1), côté préféré (`Local` ou `Remote`) et raison textuelle.

## Règles de priorité

- **LastWriteWins (v1)** : on compare d’abord `ModifiedAt`, puis `Version` en cas d’égalité.  
  - Si un seul côté existe → action Upload/Download selon le cas.  
  - Si les deux côtés divergent → un `SyncConflict` est émis avec la recommandation (côté le plus récent) et la raison.
- Le traçage de conflit est systématique même si une action (Upload/Download) est suggérée.

## Moteur de sync (Infrastructure)

- `SyncEngine` compare deux `ISyncBackend` et calcule un plan ordonné de `SyncState`.
- Équivalences : items identiques si `Version`/`ModifiedAt` concordent, ou si la hash + taille correspondent.
- Conflits : détectés dès que les métadonnées diffèrent sur les deux côtés; la préférence suit `LastWriteWins` mais reste annotée.

### Backend filesystem (mock)

- `FileSystemSyncBackend` expose un backend de test basé sur un dossier local (chemins relatifs, horodatage `LastWriteTimeUtc`, hash optionnelle SHA-256).
- Sert de stand-in pour des backends WebDAV/S3 à implémenter ultérieurement.

## Données versionnées pour la sync

- Modules et enregistrements exposent désormais `ModifiedAt` et `Version` pour refléter les mutations (création, mise à jour, soft-delete).
- Les incréments se font côté `DataEngine` / `MetadataService`, en alignement avec `LastWriteWins`.

## Sécurité et chiffrement

- Aucune couche de chiffrement supplémentaire n’est ajoutée pour la sync : SQLCipher couvre déjà la base locale, et le backend distant doit s’appuyer sur ses propres primitives de transport/chiffrement (TLS, etc.).

## Limites connues

- Pas de détection de renommage ou de suppression distante dédiée ; les divergences se traduisent par des conflits à traiter.
- L’application du plan (transferts effectifs) reste à implémenter pour les backends cibles.
