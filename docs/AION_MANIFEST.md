# AionMemory — Manifeste & Vision (Socle Produit)

## Vision
AionMemory est un système de mémoire personnelle, souveraine et **local-first**.
Il vise à stocker, organiser et rappeler des informations personnelles sans
sacrifier la confidentialité ni la maîtrise utilisateur. Les données critiques
restent sur l’appareil par défaut, avec des mécanismes explicites et traçables
pour tout échange ou synchronisation.

## Principes non négociables
- **Privacy par défaut** : chiffrement local, aucun envoi réseau implicite, et
  séparation stricte des secrets (variables d’environnement, user-secrets).
- **Souveraineté des données** : l’utilisateur contrôle l’emplacement,
  l’export et la suppression. Pas de verrouillage propriétaire.
- **Explicabilité** : chaque résultat IA doit pouvoir être relié aux sources
  ou aux règles appliquées (logs, citations, provenance).
- **Contrôle humain** : validation explicite pour les actions sensibles
  (publication, partage, suppression). Pas d’automatisme irréversible.
- **Offline-first** : fonctionnement dégradé sans réseau, avec comportements
  déterministes et observables.

Voir aussi la section [Design Principles](../README.md#design-principles), qui
s’applique à toutes les décisions produit et techniques.

## Ce qu’AionMemory n’est PAS
- Un réseau social, un produit publicitaire ou une plateforme de collecte.
- Une IA autonome qui agit sans consentement ou sans journalisation.
- Un service cloud obligatoire : le cloud est optionnel et explicite.
- Une base de données opaque sans export ni contrôle de rétention.

## Portée durable
- Les principes ci-dessus guident l’architecture, le stockage, l’IA et l’UX.
- Toute évolution qui contredit ces principes doit être refusée ou documentée
  comme exception temporaire avec plan de correction.
