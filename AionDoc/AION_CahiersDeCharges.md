# AION – Cahiers des charges des services

Ce document décrit les cahiers des charges fonctionnels des principaux services à développer autour d’AION afin d’enrichir l’application.

## 1. Service d'automatisation (AionAutomationService)

**Objectif**

Permettre à l’utilisateur de définir des règles d’automatisation afin d’exécuter des actions automatiquement lorsqu’un événement se produit. L’objectif est de simplifier la gestion des modules et de proposer un équivalent personnel d’IFTTT/Zapier.

**Fonctionnalités**

- Définir des déclencheurs (triggers) :
  - création d’un enregistrement d’un certain type ;
  - modification d’un champ (changement de statut, dépassement d’une valeur…) ;
  - arrivée d’une date/heure (agenda) ;
  - entrée ou sortie d’une plage de dates ;
  - conditions multiples (ET/OU).

- Définir des actions :
  - créer une note liée à l’enregistrement concerné ;
  - créer un événement agenda ou un rappel ;
  - envoyer une notification à l’utilisateur ;
  - mettre à jour un champ sur l’enregistrement ;
  - lancer un rapport ou un export automatique ;
  - exécuter plusieurs actions en séquence.

- UI pour composer des règles :
  - propose une bibliothèque de règles prédéfinies (ex : « Lorsqu’une culture est créée, créer un rappel de récolte dans 3 mois ») ;
  - éditeur de règles visuel ou formulaires simples ;
  - suggestions de l’IA pour automatiser des tâches répétitives.

- Gestion des règles :
  - activer/désactiver une règle ;
  - afficher l’historique des exécutions ;
  - prévenir l’utilisateur en cas d’erreur.

**Intégration et données**

- S'appuie sur AionDataEngine pour détecter les événements CRUD.
- Utilise AionAgendaService, AionNoteService et AionSearchService pour créer des actions.
- Les règles sont stockées dans une table dédiée (`S_AutomationRule`) avec un schéma décrivant trigger, conditions et actions.

**Exemple d’usage**

« À chaque fois que j’ajoute une dépense supérieure à 100 €, envoie‑moi une notification et crée une note pour justifier l’achat. »

## 2. Service Vision IA (AionVisionService)

**Objectif**

Permettre à l’utilisateur d’ajouter des informations dans AION à partir de photos ou de documents, grâce à la reconnaissance visuelle et à l’OCR (reconnaissance optique de caractères).

**Fonctionnalités**

- **OCR de documents** : extraction de texte depuis des images (factures, tickets, notes manuscrites) pour remplir automatiquement des champs et créer des enregistrements dans les modules concernés (ex : module finances ou santé).
- **Reconnaissance d’objets** : identification automatique de catégories d’objets (plantes, aliments, produits) pour alimenter des modules comme le jardinage ou l’inventaire.
- **Classification** : suggestion du module approprié pour stocker une photo (ex : « Il semble que cette image corresponde à une facture. Voulez‑vous l’ajouter à vos dépenses ? »).
- **Recherche visuelle** : possibilité de rechercher une photo en décrivant son contenu (« Retrouve la photo de mes tomates de juillet »).

**Interface utilisateur**

- Bouton « Scanner un document » ou « Ajouter une photo » accessible depuis chaque module.
- Écran d’aperçu avec possibilité de recadrer l’image et de choisir l’action (OCR, classification).
- Suggestions de remplissage de champs après analyse.

**Intégration et données**

- S'appuie sur AionFileStorageService pour le stockage.
- Doit générer ou mettre à jour des enregistrements via AionDataEngine.
- Doit pouvoir déclencher des automatisations (ex : créer une note ou un rappel suite à un scan).

**Future extension**

- Détection de maladies de plantes via IA agronomique.
- Réalité augmentée pour identifier des objets en temps réel (lien avec module potager ou santé).

## 3. Service LifeGraph & Lifelogging (AionLifeService)

**Objectif**

Offrir à l’utilisateur une vision globale de son activité et de ses données sous forme de timeline et de graphe relationnel.

**Fonctionnalités**

- **Timeline de vie** :
  - journal automatique regroupant événements, créations/modifications d’enregistrements, notes et rappels ;
  - filtrage par module, période ou thématique ;
  - possibilité de compléter la timeline avec des mémos personnels (voix ou texte).

- **Graphe sémantique** :
  - représentation visuelle des relations entre enregistrements (personnes, projets, tâches, lieux) ;
  - navigation interactive dans le graphe (cliquer sur un nœud pour voir les données associées) ;
  - possibilité de créer des liens manuellement entre deux enregistrements.

- **Insights** :
  - résumés périodiques générés par l’IA (ce que vous avez accompli, ce qui est en retard, tendances).

**Interface utilisateur**

- Page « Timeline » montrant les événements par ordre chronologique.
- Page « Graphe » affichant un réseau interactif.
- Widgets intégrables dans le tableau de bord.

**Intégration et données**

- Utilise l’historique de AionDataEngine, AionAgendaService, AionNoteService.
- Les relations et les événements sont stockés dans des tables supplémentaires (ex : `S_HistoryEvent`, `S_Link`).

**Future extension**

- Lifelogging automatique des lieux visités (via GPS, respectant la vie privée).
- Reconnaissance automatique des personnes et des activités dans les photos (avec consentement).

## 4. Service Module Builder 3.0

**Objectif**

Étendre les capacités du générateur de modules pour permettre à l’IA de concevoir des modules plus complexes, en s’appuyant sur des ontologies et des bases de connaissances.

**Fonctionnalités**

- Intégration de sources de référence (schema.org, bases de données de pratiques métiers) pour enrichir les modèles.
- Capacité à fusionner plusieurs patterns de modules (gestion de projet, inventaire, suivi financier, CRM) afin de créer des modules hybrides.
- Interaction avec l’utilisateur pour préciser les besoins lorsqu’un domaine est ambigu.
- Génération de rapports et de tableaux de bord avancés (graphes, prévisions).
- Suggestion de champs calculés (durées, totaux, moyennes).

**Interface utilisateur**

- Dialogue amélioré dans le chat IA : l’assistant pose des questions de clarification, propose des modèles d’entités et attend la validation de l’utilisateur.
- Possibilité d’éditer le modèle proposé avant la création finale (ajout, suppression ou renommage de champs).

**Intégration et données**

- Implémente le contrat `AionModuleDesigner`.
- Doit être capable de produire une structure compatible avec AionMetadataService et AionDataEngine.
- Évolution du métamodèle pour accepter des types d’attributs plus riches (tableaux, structures JSON, unités de mesure).

## 5. Service de Notes (AionNoteService) — Spécification améliorée

**Objectif**

Offrir une gestion complète des notes, incluant les notes dictées, la transcription, la classification automatique et le journal des enregistrements.

**Fonctionnalités**

- **Dictée et transcription automatique** : l’utilisateur peut enregistrer une note vocale qui est automatiquement transcrite en texte (les notes sont sauvegardées en texte afin de faciliter la recherche et la synthèse).
- **Notes libres** : création de notes indépendantes (mémos, pensées, idées) avec possibilité d’y associer des tags et des thématiques.
- **Notes liées** : possibilité de lier une note à un ou plusieurs enregistrements de n’importe quel module.
- **Journal d’enregistrement** : chaque entité peut avoir son journal chronologique alimenté par l’utilisateur ou par l’IA (logs des actions, réflexions personnelles, photos).
- **Classification automatique** : l’IA propose des catégories de notes et suggère des liens avec des modules existants.
- **Recherche dans les notes** : plein texte et recherche sémantique pour retrouver rapidement un passage.

**Interface utilisateur**

- Editeur de note riche (Markdown / RTF) avec enregistrement audio intégré.
- Liste des notes avec filtres (date, tags, modules liés).
- Vue “Journal” sur chaque fiche d’enregistrement.

**Intégration et données**

- Utilise `S_Note` et `J_Note_Link`.
- Service d’IA pour la transcription vocale et la classification.

**Future extension**

- Résumés automatiques par l’IA (TL;DR).
- Analyse des sentiments pour les notes personnelles.

## 6. Service Dashboard (AionDashboardService)

**Objectif**

Fournir à l’utilisateur des vues synthétiques et dynamiques résumant les informations clés par module ou de manière globale.

**Fonctionnalités**

- Création automatique de tableaux de bord standard (liste des tâches, statistiques par catégorie, rappels imminents).
- Widgets personnalisables (graphiques, liste, indicateurs) que l’utilisateur peut ajouter, déplacer et redimensionner.
- Rafraîchissement dynamique (updates en temps réel).
- Sauvegarde et partage des layouts de tableau de bord.
- Intégration des insights IA (alertes, recommandations).

**Interface utilisateur**

- Page “Dashboard” accessible depuis le menu.
- Galerie de widgets disponibles.
- Mode édition permettant l’organisation du tableau.

**Intégration et données**

- Consomme les rapports de `AionReportDefinition`.
- S’appuie sur `AionDataEngine` et `AionAgendaService` pour alimenter les widgets.

**Future extension**

- Dashboards prédéfinis pour chaque type de module (finances, santé, sport…).
- Export en PDF ou partage de tableaux de bord via un lien.

## 7. Service Template / Marketplace (AionTemplateService)

**Objectif**

Créer une bibliothèque de modules et de templates partageables, afin de favoriser l’entraide et la collaboration.

**Fonctionnalités**

- Permettre à l’utilisateur d’exporter un module (structure + rapports + widgets) sous forme de template.
- Permettre d’importer un template depuis la marketplace AION.
- Catalogue de templates avec descriptions, notes et évaluations.
- Filtrage par catégorie (jardinage, finance, santé…).
- Gestion des dépendances lors de l’import (ex : types d’entités partagés entre modules).
- Vérification de compatibilité avec la version d’AION.

**Interface utilisateur**

- Page “Marketplace” listant les templates populaires et récents.
- Détail d’un template (description, captures d’écran, liste des entités, avis utilisateurs).
- Bouton “Importer” avec options (importer intégralement ou fusionner avec un module existant).

**Intégration et données**

- Stockage des templates dans un format JSON/ZIP.
- Service backend pour la marketplace (hébergement, modération).
- Contrats d’import/export dans `AionTemplateService`.

**Future extension**

- Possibilité de vendre des templates premium.
- Système de contribution et de commentaires.

## 8. Service de stockage de fichiers (AionFileStorageService)

**Objectif**

Gérer l’upload, le stockage, l’indexation et l’accès aux fichiers associés aux enregistrements (images, PDF, audio).

**Fonctionnalités**

- Upload de fichiers via l’interface (drag & drop, capture photo, enregistrement audio).
- Génération de vignettes pour les images.
- Extraction de métadonnées (EXIF, dimensions, durée audio).
- Association d’un fichier à un ou plusieurs enregistrements via `F_File`.
- Suppression et édition des fichiers.
- Indexation dans le moteur de recherche (nom de fichier, texte extrait par OCR).
- Gestion d’un quota de stockage par utilisateur.

**Interface utilisateur**

- Bouton “Ajouter un fichier” dans les formulaires et les vues détails.
- Galerie/visualisation des fichiers liés à un enregistrement.
- Page “Fichiers” listant l’ensemble des fichiers par catégorie ou par date.

**Intégration et données**

- Utilise un fournisseur de stockage cloud (Azure Blob, S3, BackBlaze).
- `AionFileStorageService` expose les opérations Upload/Download/Delete.
- `F_File` stocke les métadonnées et la clé d’accès au fichier.

**Future extension**

- Partage direct d’un fichier vers d’autres apps (mail, messagerie).
- Conversion automatique (pdf ↔ image).

## 9. Service Prédictif et Suggestions (AionPredictService)

**Objectif**

Analyser les données de l’utilisateur pour prédire les actions futures et proposer des recommandations pertinentes.

**Fonctionnalités**

- Analyse des tendances (dépenses, activités, plantations, performances sportives).
- Prédiction d’échéances (récolte probable, date de prochaine dépense récurrente).
- Propositions de rappels proactifs (« Vous n’avez pas arrosé vos plantes depuis 7 jours »).
- Recommandations d’optimisation (répartition des tâches, rééquilibrage du budget).
- Sélection de rapports pertinents à consulter.

**Interface utilisateur**

- Notifications push ou messages dans le chat IA.
- Widget “Suggestions” dans le tableau de bord.
- Paramètres pour choisir le niveau de proactivité souhaité.

**Intégration et données**

- Se base sur les historiques d’enregistrement, d’événements et de notes.
- Utilise des modèles statistiques ou d’apprentissage automatique (ML) hébergés localement ou dans le cloud.
- Tient compte des préférences de confidentialité.

**Future extension**

- Prise en compte du contexte externe (météo, événements publics).
- Personnalisation du modèle en fonction du profil de l’utilisateur.

## 10. Service Persona / Style (AionPersonaEngine)

**Objectif**

Adapter le ton, le style et les réponses d’AION à la manière de penser et aux préférences de l’utilisateur.

**Fonctionnalités**

- Analyse de la façon dont l’utilisateur formule ses demandes pour apprendre son style.
- Adaptation du vocabulaire, du niveau de détail, des références culturelles.
- Paramètres d’humeur et de personnalité (sérieux, humoristique, concis, détaillé).
- Respect strict des consignes de vérité et de neutralité.

**Interface utilisateur**

- Section “Personnalisation” pour choisir ou ajuster son persona.
- Possibilité de basculer entre plusieurs personas (professionnel, familial, créatif…).
- Feedback de l’utilisateur pour améliorer la pertinence.

**Intégration et données**

- Se branche sur les services IA (AionAIProvider).
- N’altère pas le contenu des données, uniquement la forme des réponses.
- Utilise un modèle local ou cloud en respectant la vie privée.

**Future extension**

- Personnalités partagées (adopter la “persona d’un auteur célèbre”).
- Mode “lecture à haute voix” avec différentes voix.

---

Ces cahiers des charges décrivent les principaux axes d’enrichissement d’AION. Chaque service est conçu pour s’intégrer harmonieusement au moteur central (métamodèle, IA, notes, agenda, reporting) et offrir à l’utilisateur une expérience toujours plus personnalisée et productive.