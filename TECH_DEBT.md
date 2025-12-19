# TECH_DEBT

## DataEngine et métamodèle
- **Audit trail non branché sur les mutations DataEngine**  
  **Impact :** impossible de tracer précisément les insert/update/delete, y compris pour le soft-delete.  
  **Priorité :** moyenne.
- **Validation des lookups sans cache**  
  **Impact :** chaque insertion met à contribution la base cible ; risque de latence sur des modules très relationnels.  
  **Priorité :** moyenne.
- **Indexation FTS limitée au contenu brut**  
  **Impact :** les labels résolus ou champs calculés ne sont pas présents dans l’index, ce qui réduit la pertinence des recherches.  
  **Priorité :** moyenne.

## Infrastructure / stockage
- **Rotation des clés SQLCipher et du stockage fichier non outillée**  
  **Impact :** opération manuelle en cas de compromission ou de mise en conformité.  
  **Priorité :** critique.
- **Politiques de restauration/quotas backups minimales**  
  **Impact :** pas de contrôle fin sur la rétention ou l’intégrité des archives, risque d’échec silencieux.  
  **Priorité :** moyenne.
- **Surveillance temps-réel des migrations absente**  
  **Impact :** en cas d’échec migration en production, l’application peut rester partiellement initialisée sans alerte centralisée.  
  **Priorité :** moyenne.

## Aion.AI
- **Pas de gestion centralisée des timeouts/réessais par provider HTTP**  
  **Impact :** instabilité perçue en cas de lenteur réseau ; besoin d’un backoff uniformisé.  
  **Priorité :** moyenne.
- **Réponses non structurées tolérées uniquement par fallback**  
  **Impact :** risque de perte d’intention ou de paramètres ; nécessite un durcissement des prompts ou une validation plus stricte.  
  **Priorité :** moyenne.

## UI / MAUI Blazor
- **Couverture de tests UI minimale**  
  **Impact :** régressions silencieuses sur les vues dynamiques (modules/records).  
  **Priorité :** moyenne.
- **Offline/synchronisation non simulés dans les tests**  
  **Impact :** comportements spécifiques aux plateformes mobiles non validés en CI.  
  **Priorité :** moyenne.
