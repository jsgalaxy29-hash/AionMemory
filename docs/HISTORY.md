# History & Audit Trail (v1)

## Objectif
- Offrir un audit trail par table avec consultation chronologique des modifications et support du rollback manuel.

## Modèle de données
- `F_RecordAudit` conserve chaque mutation `Create/Update/Delete` avec : table, record, version, date, `DataJson` (snapshot) et `PreviousDataJson` pour les opérations qui remplacent ou suppriment une valeur.
- Les versions sont incrémentées à chaque opération CRUD pour refléter la séquence d'écriture. Les entrées d'audit ne sont jamais supprimées, même en cas de suppression hard du record.
- Le champ `ChangeType` est stocké sous forme de chaîne (conversion EF) afin de rester lisible et portable.

### Choix snapshot vs diff
- **Snapshot JSON** retenu pour `DataJson` et `PreviousDataJson` : l'audit capture l'état complet après chaque mutation (et l'état précédent pour `Update/Delete`).
- Avantages : lecture directe, rollback manuel simplifié (copier le dernier snapshot), pas de dépendance à un moteur de diff ou à l'ordre des champs JSON.
- Limites : volume potentiellement supérieur à un diff minimal, à mitiger via rétention/config ultérieure si nécessaire.

## API DataEngine
- Nouvelle méthode `GetHistoryAsync(tableId, recordId)` exposée via `IDataEngine/IAionDataEngine` pour récupérer des `ChangeSet` ordonnés par version.
- Les opérations `Insert/Update/Delete` publient automatiquement une entrée `F_RecordAudit` dans la même transaction que la mutation principale, garantissant un audit non destructif.

## Visualisation & rollback
- La timeline peut être alimentée directement par `ChangeSet` triés par version ou date (`ChangedAt`).
- Pour un rollback manuel, réinjecter le `DataJson` d'un `ChangeSet` antérieur via `UpdateAsync`, ou recréer le record supprimé à partir du snapshot final d'un `Delete`.
