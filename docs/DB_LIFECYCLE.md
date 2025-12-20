# AionMemory database lifecycle (SQLite + SQLCipher + EF Core 10)

## Objectifs
- Base de données chiffrée et déterministe : SQLite + SQLCipher, migrations EF Core uniquement (pas d’`EnsureCreated`).
- Métamodèle persistant : tables `Tables`, `TableFields`, `TableViews` + données génériques `Records` reliées au métamodèle.
- Cycle de vie reproductible en dev/test/production avec clés tenues hors des logs.

## Connexion et chiffrement
- La connexion est configurée via `AionDatabaseOptions` (connection string) + `EncryptionKey` séparée (jamais stockée dans la connection string).
- `SqliteConnectionFactory` :
  - Nettoie la chaîne (suppression `Password/Pwd`), force `Mode=ReadWriteCreate`, `Cache=Private`, `ForeignKeys=ON`.
  - Ajoute l’intercepteur `SqliteEncryptionInterceptor` qui applique `PRAGMA key = $key` paramétré et `cipher_memory_security = ON` dès l’ouverture.
- `SqliteCipherDevelopmentDefaults` fournit une configuration SQLCipher prête à l’emploi pour le dev/test (fichier local + clé de démo), surchargée par `AION_DB_KEY` si présente.

## Modèle EF Core
- `AionDbContext` expose les entités métier et le métamodèle :
  - `STable` → table `Tables`, index unique sur `Name`.
  - `SFieldDefinition` → `TableFields`, clé étrangère vers `Tables`, index unique `(TableId, Name)`.
  - `SViewDefinition` → `TableViews`, clé étrangère vers `Tables`, index unique `(TableId, Name)`.
  - `F_Record` → `Records`, colonne `EntityTypeId` référencée vers `Tables` (cascade en cas de suppression de table), index `(TableId, CreatedAt)`.
- Les types enums sont stockés en `TEXT` et les longueurs/nullabilité suivent les attributs du domaine.

## Initialisation et migrations
`DependencyInjectionExtensions.EnsureAionDatabaseAsync` orchestre le cycle de vie :
1. Ouvre explicitement la connexion (application de la clé SQLCipher via l’intercepteur).
2. Applique uniquement `Database.Migrate()` pour exécuter les migrations EF Core.
3. Valide le schéma (présence de tables critiques comme `Modules`).
4. Exécute le seed de démonstration.

En cas d’échec, les logs indiquent uniquement le chemin de la base (`DataSource`), jamais la clé.

## Commandes utiles
- Mise à jour schema locale : `dotnet ef database update --project src/Aion.Infrastructure`
- Vérification CLI : `dotnet build`, puis `dotnet test` (ou `pwsh ./scripts/build.ps1`, `pwsh ./scripts/test.ps1` pour l’orchestration complète).

## Tests d’intégration
- `DatabaseLifecycleTests` couvrent l’ouverture SQLCipher, l’application des migrations, la validation de schéma et l’échec attendu en cas de clé incorrecte pour une base déjà chiffrée.
