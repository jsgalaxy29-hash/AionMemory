# Récupération & Résilience (Disaster Recovery)

Objectif : une mémoire ne doit jamais être perdue. La récupération prime sur la perfection : on préfère conserver le maximum de données même si l’état n’est pas idéal.

## Scénarios de corruption pris en compte

- **Base chiffrée inaccessible** : clé SQLCipher incorrecte → erreurs de type « file is not a database ».
- **Fichier tronqué / altéré** : coupure disque, crash, copie incomplète → « database disk image is malformed ».
- **Index endommagés** : incohérence entre pages ou FTS → échecs d’`integrity_check`.
- **Clés étrangères incohérentes** : `foreign_key_check` signale des lignes orphelines.

## Vérifications d’intégrité au démarrage

Au démarrage, Aion exécute :

- `PRAGMA integrity_check` pour vérifier l’intégrité globale de la base.
- `PRAGMA foreign_key_check` pour détecter les incohérences relationnelles.

En cas d’échec, l’application bloque le démarrage et demande d’exécuter un export de récupération (voir ci-dessous).

## Outil de réparation (lecture seule / export)

Le projet `Aion.RecoveryTool` permet de :

- **Vérifier** l’intégrité d’une base en lecture seule.
- **Exporter** une nouvelle base saine en lecture seule côté source.

### Vérifier l’intégrité

```bash

dotnet run --project src/Aion.RecoveryTool -- check \
  --connection "Data Source=/chemin/vers/aion.db" \
  --key "<clé SQLCipher>"
```

### Exporter une base saine

```bash

dotnet run --project src/Aion.RecoveryTool -- export \
  --connection "Data Source=/chemin/vers/aion.db" \
  --key "<clé SQLCipher>" \
  --output "/chemin/vers/aion_recovered.db"
```

Le fichier d’origine est ouvert en **lecture seule** et un nouveau fichier est créé via une copie interne SQLite.

## Procédure de récupération recommandée

1. Arrêter l’application.
2. Lancer un **check** avec l’outil ci-dessus.
3. Si le check échoue, lancer **export** pour créer une nouvelle base.
4. Remplacer l’ancienne base par la nouvelle (après sauvegarde du fichier original).
5. Redémarrer l’application.

## Notes

- Les exports produisent une base chiffrée avec la même clé.
- Conserver l’ancienne base pour toute analyse post-mortem.
