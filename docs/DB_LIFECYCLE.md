# AionMemory database lifecycle (SQLite + SQLCipher + EF Core 10)

## Objectifs

- Base chiffree et deterministe : SQLite + SQLCipher, migrations EF Core uniquement.
- Metamodele persistant : tables de definitions et donnees generiques reliees au metamodele.
- Cycle de vie reproductible en dev/test/production avec cles tenues hors des logs.

## Connexion et chiffrement

- La connexion est configuree via `AionDatabaseOptions` (`ConnectionString`) et une `EncryptionKey` separee.
- `SqliteConnectionFactory` nettoie la connection string, force les options SQLite utiles et ajoute `SqliteEncryptionInterceptor`.
- `SqliteEncryptionInterceptor` applique la cle SQLCipher a l'ouverture de la connexion.
- `SqliteCipherDevelopmentDefaults` peut fournir des valeurs de dev/test hors MAUI ; dans l'hote MAUI, les valeurs peuvent aussi venir du setup local et de `SecureStorage`.

## Modele EF Core

- `AionDbContext` expose les entites metier et le metamodele.
- Les migrations EF Core restent la source de verite du schema.
- Les validations de schema critiques sont rejouees au demarrage via l'initialisation applicative.

## Initialisation et migrations

`DependencyInjectionExtensions.EnsureAionDatabaseAsync` orchestre le cycle de vie :

1. ouverture explicite de la connexion ;
2. application des migrations EF Core ;
3. verification de l'integrite et de la presence des tables critiques ;
4. execution du seed de demonstration.

En cas d'echec, les logs indiquent le chemin de la base, jamais la cle.

## Commandes utiles

- Mise a jour locale du schema :

  ```bash
  dotnet ef database update --project Aion.Infrastructure/Aion.Infrastructure.csproj
  ```

- Verification CLI :

  ```bash
  dotnet build
  dotnet test
  ```

- Orchestration repo :

  ```pwsh
  pwsh ./scripts/build.ps1
  pwsh ./scripts/test.ps1
  ```

## Tests d'integration

- `DatabaseLifecycleTests` couvrent l'ouverture SQLCipher, l'application des migrations, la validation de schema et l'echec attendu en cas de cle incorrecte.
