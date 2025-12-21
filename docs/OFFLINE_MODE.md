# Mode hors-ligne / IA inactive

Ce dépôt doit fonctionner même sans configuration IA. Le mode « IA inactive » est déclenché lorsqu’aucun provider n’est configuré (`Aion:Ai` sans clé ni endpoint).

## Comportement
- **Sélecteur IA** : `AiProviderSelector.GetStatus()` retourne `IsConfigured=false` et force le provider `inactive`.
- **Providers** : le factory route alors vers des providers no-op (`inactive`) qui lèvent `AiUnavailableException` au lieu d’appels réseau.
- **UI (AppHost)** :
  - Badge « IA inactive » dans la topbar.
  - Chat/Home/Module Builder désactivent les actions IA et affichent un message non bloquant.
  - Les autres modules (CRUD, recherche locale, navigation) restent fonctionnels.

## Module Builder hors-ligne
- Quand l’IA est inactive, la génération automatique est désactivée.
- Nous choisissons de **refuser** la génération IA dans ce mode pour éviter des erreurs silencieuses. Un éditeur manuel JSON (ModuleSpec) pourra être ajouté ultérieurement si besoin.

## Rétablir l’IA
Configurer `Aion:Ai` (provider + clé/endpoint ou provider `mock/local`) via `appsettings.*`, variables d’environnement ou secrets utilisateur, puis redémarrer l’application.
