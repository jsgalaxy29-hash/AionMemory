# Recuperation & resilience

Objectif : une memoire ne doit jamais etre perdue. La recuperation prime sur la perfection : on prefere conserver le maximum de donnees meme si l'etat final n'est pas ideal.

## Scenarios de corruption pris en compte

- Base chiffree inaccessible : cle SQLCipher incorrecte.
- Fichier tronque ou altere : crash, copie incomplete, corruption disque.
- Index endommages : incoherences sur pages ou FTS.
- Cles etrangeres incoherentes : lignes orphelines detectees par SQLite.

## Verifications d'integrite au demarrage

Au demarrage, Aion verifie l'integrite de la base et bloque l'initialisation si la base est jugee invalide. Le flux normal consiste alors a utiliser l'outil de recuperation.

## Outil de recuperation

Le projet `Aion.RecoveryTool` permet de :

- verifier l'integrite d'une base en lecture seule ;
- exporter une nouvelle base saine a partir de la source.

### Verifier l'integrite

```bash
dotnet run --project Aion.RecoveryTool/Aion.RecoveryTool.csproj -- check \
  --connection "Data Source=/chemin/vers/aion.db" \
  --key "<cle SQLCipher>"
```

### Exporter une base saine

```bash
dotnet run --project Aion.RecoveryTool/Aion.RecoveryTool.csproj -- export \
  --connection "Data Source=/chemin/vers/aion.db" \
  --key "<cle SQLCipher>" \
  --output "/chemin/vers/aion_recovered.db"
```

Le fichier d'origine est ouvert en lecture seule et un nouveau fichier est cree via une copie SQLite interne.

## Procedure recommandee

1. Arreter l'application.
2. Lancer un `check` avec l'outil de recuperation.
3. Si le check echoue, lancer `export` pour creer une nouvelle base.
4. Sauvegarder l'ancienne base puis remplacer la base active par la nouvelle.
5. Redemarrer l'application.

## Notes

- Les exports produisent une base chiffree avec la meme cle, sauf evolution explicite de l'outil.
- Conserver l'ancienne base pour toute analyse post-mortem.
