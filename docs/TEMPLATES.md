# Templates & catalogue local

Ce document décrit le format et les usages de la v1 des templates (marketplace locale uniquement).

## Objectifs v1

- Exporter un module en **template local** (ModuleSpec + manifest d'assets).
- Importer un template de manière **idempotente** (réexécuter l'import ne crée pas de doublons).
- Gérer les templates via la page **Catalogue templates**.
- **Aucune marketplace en ligne** (v1 locale).

## Contenu d'un template

Un template est un fichier JSON stocké dans le dossier marketplace. Il contient :

- **Payload** : le `ModuleSpec` sérialisé.
- **AssetsManifest** : manifest des assets (v1 vide par défaut).
- **Version** : version du template (chaîne).
- **Author** : auteur (identifiant local).
- **Signature** : hash simple calculé à l’export.

> Remarque : `Payload` et `AssetsManifest` sont stockés sous forme de chaînes JSON sérialisées.  
> Cela simplifie la compatibilité avec le stockage en base v1.

### Manifest d'assets (v1)

Le manifest inclut une date d’export et une liste d’assets. En v1, la liste est généralement vide.

```json
{
  "exportedAt": "2025-01-01T10:00:00Z",
  "assets": []
}
```

## Signature (hash + auteur)

La signature est un **hash SHA-256** du contenu suivant :

```
author|version|payload|assetsManifest
```

**Limites (v1)** :

- ce n’est **pas une signature cryptographique** (pas de clé privée/ publique).
- elle sert uniquement à détecter des modifications accidentelles.
- elle ne fournit **aucune garantie d’authenticité**.

## Import / export

- **Exporter** : depuis l’UI, choisir un module puis « Exporter / publier ».
- **Importer** : depuis un fichier JSON (upload) ou le catalogue local.
- **Idempotence** : réimporter le même template réapplique le ModuleSpec sans créer de module dupliqué.

## Emplacement marketplace

Le dossier marketplace est configuré via `Aion:Marketplace:Folder`.  
La page **Catalogue templates** liste les fichiers présents dans ce dossier.
