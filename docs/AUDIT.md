# Audit trail

## Objectif
Ce document décrit les traces d’audit utilisées par AionMemory pour suivre les mutations métier et les actions critiques.
Les audits sont **non destructifs** : aucune entrée n’est supprimée automatiquement.

## Tables d’audit

### `RecordAudits` (F_RecordAudit)
* **Portée** : mutations des enregistrements DataEngine.
* **Écritures** : `Create`, `Update`, `Delete`, `SoftDelete`.
* **Contenu** : snapshot JSON après mutation (`DataJson`) + précédent (`PreviousDataJson`), version, utilisateur, horodatage.
* **Transaction** : l’audit est écrit dans la même transaction que la mutation.

### `SecurityAuditLogs` (S_SecurityAuditLog)
* **Portée** : actions critiques et modifications de schéma.
* **Catégories** : schéma, export, import, backup, restore, modules.
* **Contenu** : action, cible (type/id), utilisateur, workspace, corrélation/opération, métadonnées JSON.
* **Protection** : les métadonnées sont redactionnées pour éviter les valeurs sensibles (ex. secrets, tokens).

## Rétention
* Aucune purge automatique n’est appliquée.
* Les politiques de conservation doivent être gérées par l’exploitation (sauvegarde/archivage/purge manuelle).

## Limites
* `RecordAudits` contient des snapshots complets et peut inclure des données sensibles si elles sont stockées dans les records.
* `SecurityAuditLogs` ne stocke pas de payload complet ; seules des métadonnées minimales sont conservées et redactionnées.
* Les audits sont conçus pour l’auditabilité, pas pour la reconstruction parfaite d’un historique métier complexe.
