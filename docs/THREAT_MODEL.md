# Modèle de menace (Threat Model)

Objectif : renforcer la sécurité de façon pragmatique, sans complexifier inutilement le projet.

## Données à protéger
- **Données personnelles et sensibles** : notes, événements, pièces jointes, résumés IA, historiques.
- **Identifiants et secrets** : clés d’API IA, clés de chiffrement DB/stockage.
- **Métadonnées d’activité** : journaux d’exécution, résumés d’erreurs, traces de traitement.

## Menaces réalistes
1. **Compromission locale du disque**  
   Attaquant ayant accès aux fichiers de l’application (DB SQLite, storage, backups).
2. **Fuite involontaire dans les logs**  
   Prompts, données utilisateur ou clés apparaissant en clair dans les logs.
3. **Injection de prompt via données internes**  
   Texte en mémoire/stockage contenant des instructions visant à détourner une réponse IA.
4. **Mauvaise configuration**  
   Clé de chiffrement par défaut, désactivation du chiffrement, tracing de prompts activé en production.

## Mesures en place (résumé)
- **Chiffrement au repos**
  - **DB** : SQLCipher obligatoire, clé minimale (>= 32 chars) et rejet des clés par défaut hors dev/test.
  - **Storage** : chiffrement AES-GCM des payloads (activé par défaut) + intégrité optionnelle.
- **Logs et erreurs**
  - Prompts redirigés en logs sous forme **redacted** par défaut.
  - Messages d’erreur techniques sans données sensibles dans les journaux courants.
- **IA / prompt injection**
  - Les prompts intégrant du contexte marquent explicitement ce contexte comme **non fiable**.
  - Les champs issus des enregistrements sont normalisés pour limiter les effets de formatage.
- **CI / validation**
  - Scan secrets (gitleaks).
  - Vérifications basiques contre la désactivation du chiffrement et le tracing des prompts.

## Non-objectifs (hors périmètre)
- **Attaques sur appareil compromis** : malware avec accès mémoire/OS.
- **Protection réseau avancée** : MITM, reverse engineering local, hardening OS.
- **Sécurité multi-tenant avancée** : isolation forte entre espaces/tenants.
- **Défense contre des modèles IA malveillants** : comportement imprévisible d’un provider compromis.

## Hypothèses
- L’environnement d’exécution est raisonnablement fiable (OS non compromis).
- L’utilisateur/provisioning fournit des clés sécurisées en production.
- Les providers IA peuvent être indisponibles ou retourner des réponses non structurées.
