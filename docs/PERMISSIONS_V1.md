# Permissions v1 (RBAC minimal)

Cette version introduit un contrôle d'accès simple et cohérent avec les contrats AION :

## Modèle
- **Rôles** : `Admin`, `User`
- **Actions** : `Read`, `Write`, `Delete`, `ManageSchema`
- **Portée** : `PermissionScope` porte un `TableId` obligatoire et un `RecordId` optionnel (scope `Record`), sinon scope `Table`.
- **Invariants** :
  - `TableId` et `UserId` ne peuvent pas être vides.
  - `RecordId` ne peut pas être vide lorsqu'il est renseigné.
  - L'action doit être définie dans l'enum `PermissionAction`.

## Règles d'autorisation
- Le rôle `Admin` accorde tous les droits.
- Un droit explicite (`Permission`) est requis pour chaque action/table. Un droit de table couvre aussi les enregistrements de cette table. Un droit de scope `Record` ne couvre que l'enregistrement ciblé.
- `ManageSchema` protège la création/édition de tables (et la génération de vues simples).
- Les permissions sont évaluées via `IAuthorizationService.AuthorizeAsync(userId, action, scope)`.

## Implémentation
- **Données** : tables EF `Roles` (UserId, Kind) et `Permissions` (UserId, Action, TableId, RecordId).
- **Service** : `AuthorizationService` applique les règles ci-dessus. `CurrentUserService` expose un utilisateur courant minimal (défaut: `00000000-0000-0000-0000-000000000001`).
- **Surcouche** : `AuthorizedDataEngine` décore `IAionDataEngine` et vérifie les droits avant chaque opération CRUD et les actions schéma.
- **UI** : les composants DynamicForm/DynamicList désactivent les boutons d'écriture/suppression quand l'autorisation échoue.

## Limitations v1
- Pas de gestion multi-utilisateurs avancée (le service utilisateur courant est minimal).
- Pas de délégation/hiérarchie de rôles, ni de groupes.
- Pas de logique de propagation automatique des droits entre modules/tables.
