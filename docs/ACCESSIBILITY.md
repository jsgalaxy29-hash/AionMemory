# Accessibilité UI (AppHost)

Ce document décrit les réglages d’accessibilité disponibles dans l’AppHost (MAUI/Blazor) et leur persistance.

## Options disponibles

Les contrôles se trouvent dans la navigation latérale, section **Accessibilité**.

- **Thème** : suit le système, force clair/sombre, ou active le **contraste élevé**.
- **Taille du texte** : applique un facteur d’agrandissement (100 %, 115 %, 130 %, 150 %).
- **Navigation simplifiée** : réduit le nombre d’entrées visibles et masque la liste détaillée des modules.

## Persistance

Les réglages sont sauvegardés via les préférences MAUI (`Microsoft.Maui.Storage.Preferences`) et restaurés au lancement.

Clés utilisées :

- `aion.ui.theme`
- `aion.ui.fontScale`
- `aion.ui.nav.simplified`

## Notes de rendu

- Le contraste élevé repose sur une palette dédiée et un accent jaune, et supprime les ombres décoratives.
- La taille du texte utilise une variable CSS (`--font-scale`) appliquée à la racine.
