# ROADMAP

## Court terme (stabilité)
- **Durcir la synchronisation offline/online** : mettre en place un orchestrateur de sync transactionnel (file d’outbox, replays idempotents, détection de conflits) et couvrir les scénarios de perte de réseau dans les tests UI.  
- **Permissions avancées** : introduire un modèle de rôles/scopes par module et par champ (lecture/écriture/export). La navigation et les actions UI doivent refléter les refus dès le chargement.  
- **Observabilité** : unifier les logs applicatifs/plateforme, exposer des métriques DataEngine/IA (latence, coût, erreurs) et ajouter des alertes sur les migrations échouées.

## Moyen terme (expérience produit)
- **Collaboration** : support du partage sélectif (workspace, module, enregistrement) avec notifications et historiques d’accès.  
- **Designer de modules versionné** : gestion des évolutions de schéma (draft/publish, migrations assistées, rollback) et contrôle de compatibilité des données existantes.  
- **UX mobile** : améliorer les parcours hors-ligne (file d’attente des actions, reprise visible) et proposer un mode accessibilité (contrastes, gestures).

## Long terme (extension)
- **Plugins IA** : catalogue de prompts/outils versionnés, sandboxés, avec quotas par workspace.  
- **Interop** : connecteurs vers sources externes (webhooks, stockage objet) via `IDataEngine` avec isolation stricte.  
- **Sécurité avancée** : rotation automatisée des clés SQLCipher, politique de rétention configurable, et audits complets des mutations (DataEngine + IA).  
