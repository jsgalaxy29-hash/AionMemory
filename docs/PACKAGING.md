# Packaging produit (Solo / Famille / Pro)

## Objectif
Permettre l’usage d’AionMemory par plusieurs profils d’utilisation sans fork de code, en s’appuyant sur une structure commune **Tenant → Workspace → Profile**.

## Modèle minimal
| Concept | Rôle | Notes |
| --- | --- | --- |
| **Tenant** | Regroupement logique (foyer, équipe, organisation). | Porte le niveau de packaging (`Solo`, `Family`, `Pro`). |
| **Workspace** | **Clé de partition** des données (isolation logique). | Un tenant possède un ou plusieurs workspaces. |
| **Profile** | Préférences et identité locale d’un utilisateur. | Sélectionné au démarrage, sans authentification externe. |

## Niveaux de packaging
### Solo
- **1 tenant**, **1 workspace**, **1 profil** local.
- Données locales, pas de synchronisation externe.

### Famille
- **1 tenant**, **N workspaces** (ex. un par membre).
- **N profils** locaux.
- Partage optionnel via un workspace commun (option future).

### Pro
- **N tenants**, **N workspaces** par tenant.
- **N profils** locaux, permissions internes (rôles) uniquement.
- Synchronisation activable **sans SaaS** (mécanisme de sync interne).

## Isolation des données
- La base de données ajoute un **WorkspaceId** sur les entités persistées.
- Les requêtes EF Core sont filtrées par **WorkspaceId** (clé de partition).
- Le contexte de workspace est injecté via `IWorkspaceContext`.

## Démarrage et sélection
- Au lancement, l’app propose un **choix de workspace et de profil**.
- Le choix est mémorisé localement via les préférences MAUI.

## Limites actuelles
- Pas d’authentification externe.
- Pas de SaaS ni d’intégration cloud imposée.
- Gestion avancée des utilisateurs (invitation, MFA) **non couverte** à ce stade.
