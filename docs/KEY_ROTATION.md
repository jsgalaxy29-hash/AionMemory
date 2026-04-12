# Rotation de clé SQLCipher (procédure sûre)

## Objectif
La rotation de clé re-chiffre la base SQLite (SQLCipher) **sans perte** via `PRAGMA rekey`.
L’application crée une sauvegarde locale avant l’opération puis vérifie l’intégrité de la base avec la nouvelle clé.

## Pré-requis
- Une clé SQLCipher **>= 32 caractères**.
- Accès au dossier de sauvegarde local (`Aion:Backup:Folder`).
- S’assurer qu’aucune opération critique n’est en cours (imports massifs, migrations, etc.).

## Procédure via l’UI
1. Ouvrir la page **Backups**.
2. Saisir la nouvelle clé (et confirmation).
3. Cocher la case indiquant que la sauvegarde locale est prête.
4. Taper **ROTATE** puis lancer la rotation.

L’application :
- Crée une sauvegarde locale.
- Exécute `PRAGMA rekey` sur la base.
- Vérifie l’intégrité (`PRAGMA quick_check`) et l’ouverture avec la nouvelle clé.
- En cas d’échec, restaure automatiquement la dernière sauvegarde.

## Stockage de la nouvelle clé
- MAUI : la nouvelle clé est enregistrée dans `SecureStorage` (clé `aion_db_key`).
- CLI / tooling : renseigner `AION_DB_KEY` (ou `Aion:Database:EncryptionKey`) avec la nouvelle valeur.

## Rollback
Si la rotation échoue, la dernière sauvegarde locale est restaurée automatiquement.
En cas de besoin manuel, utilisez la dernière sauvegarde présente dans `Aion:Backup:Folder`.
