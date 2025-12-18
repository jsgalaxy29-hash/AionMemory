# AION Backlog (Epics / Features / Tasks)

## Epic: Métamodèle & DataEngine
Description: Étendre le métamodèle et le moteur de données générique pour générer, valider et persister des modules créés par l’IA, avec indexation et hooks d’extension.

### Feature: Gestion complète des métadonnées (S_Module, S_EntityType, S_Field, S_Relation, S_ReportDefinition)
User story: En tant que concepteur de modules, je veux définir et versionner les métadonnées afin que l’UI et le DataEngine génèrent dynamiquement les écrans et la validation.
Tasks:
- Implémenter le CRUD `AionMetadataService` (Domain + Infrastructure) pour S_Module et S_EntityType. `area:domain`, `priority:high`, `type:feature`
- Ajouter la persistance EF Core pour S_Field, S_Relation, S_ReportDefinition et migrations associées. `area:infra`, `priority:high`, `type:feature`
- Créer une API de conversion `ModuleDefinition -> Metamodel` (mapper dans Domain). `area:domain`, `priority:medium`, `type:feature`
- Ajouter le versioning des schémas (colonnes CreatedByAI, Version, IsActive). `area:infra`, `priority:medium`, `type:feature`
- Écrire des tests unitaires pour le CRUD métadonnées et le mapper. `area:test`, `priority:medium`, `type:feature`

### Feature: DataEngine générique sur F_Record
User story: En tant qu’utilisateur, je veux que mes données soient stockées et validées selon le métamodèle pour créer/mettre à jour des enregistrements via le chat ou l’UI.
Tasks:
- Implémenter les opérations CRUD génériques dans `AionDataEngine` avec validation basée sur S_Field. `area:domain`, `priority:high`, `type:feature`
- Gérer les relations (OneToMany, ManyToMany) et la résolution des liens lors des requêtes. `area:infra`, `priority:high`, `type:feature`
- Alimenter `SearchText` (FTS5) avec concaténation des champs textuels et notes liées. `area:infra`, `priority:medium`, `type:feature`
- Ajouter des hooks avant/après (events ou interceptors) pour automatisations et calculs. `area:domain`, `priority:medium`, `type:feature`
- Tests d’intégration CRUD et indexation sur F_Record. `area:test`, `priority:medium`, `type:feature`

### Feature: Génération de rapports génériques
User story: En tant qu’utilisateur, je veux des listes et rapports paramétrables basés sur mes modules afin d’obtenir des statistiques sans développement.
Tasks:
- Implémenter l’interpréteur de rapports utilisant S_ReportDefinition (listes, agrégations, graphiques). `area:domain`, `priority:medium`, `type:feature`
- Ajouter des composants Razor dynamiques pour afficher listes/graphes à partir du JSON de définition. `area:ui`, `priority:medium`, `type:feature`
- Connecter le moteur de rapport à `AionDataEngine` pour requêtes filtrées. `area:infra`, `priority:medium`, `type:feature`
- Ajouter des tests unitaires pour le rendu de rapports simples. `area:test`, `priority:low`, `type:feature`

## Epic: IA & orchestrateurs
Description: Construire la couche IA (provider, détecteur d’intentions, générateur de modules, interpréteurs CRUD/agenda/notes/rapports) et orchestrer les appels au DataEngine et aux services transverses.

### Feature: IA Provider multi-modèles
User story: En tant qu’admin, je veux configurer un provider IA (OpenAI/Mistral/local) pour traiter les requêtes chat et génération de modules.
Tasks:
- Implémenter `IAionAIProvider` avec au moins un provider concret configurable via appsettings. `area:ai`, `priority:high`, `type:feature`
- Ajouter la configuration `AionAiOptions.ModelName` et `Endpoint` et validation. `area:infra`, `priority:medium`, `type:chore`
- Mettre en place le logging/trace des requêtes IA (prompt, tokens). `area:infra`, `priority:medium`, `type:chore`
- Tests simulés avec un provider stub pour vérifier les flux. `area:test`, `priority:medium`, `type:feature`

### Feature: Détecteur d’intentions & orchestrateur
User story: En tant qu’utilisateur, je veux que l’IA comprenne mes requêtes (module, CRUD, note, rappel, rapport) afin d’exécuter automatiquement l’action.
Tasks:
- Implémenter `IntentDetector` dans `Aion.AI` pour classer les intentions principales. `area:ai`, `priority:high`, `type:feature`
- Créer l’orchestrateur central qui route vers DataEngine, NoteService, AgendaService selon l’intention. `area:infra`, `priority:high`, `type:feature`
- Ajouter des prompts templates pour la détection et la génération de plans d’action. `area:ai`, `priority:medium`, `type:chore`
- Scénarios de tests bout-en-bout avec requêtes naturelles (seed de données Potager). `area:test`, `priority:medium`, `type:feature`

### Feature: Module Designer 3.0
User story: En tant qu’utilisateur, je veux que l’IA propose et affine un module complet (entités, champs, rapports) à partir d’une phrase afin de démarrer rapidement un domaine.
Tasks:
- Implémenter `DesignModuleAsync` pour produire un `ModuleDefinition` structuré. `area:ai`, `priority:high`, `type:feature`
- Ajouter des sources référentielles (schema.org/ontologies) dans le prompt ou un référentiel local. `area:ai`, `priority:medium`, `type:feature`
- Intégrer une boucle de clarification (questions/réponses) avant création. `area:ai`, `priority:medium`, `type:feature`
- Connecter la sortie au `AionMetadataService` pour persistance et activation du module. `area:infra`, `priority:high`, `type:feature`
- Tests de génération sur des domaines variés (potager, finances, santé). `area:test`, `priority:medium`, `type:feature`

## Epic: Infrastructure & stockage
Description: Sécuriser et industrialiser la base locale SQLCipher, le stockage de fichiers et la sauvegarde cloud.

### Feature: Base SQLite chiffrée & migrations
User story: En tant qu’utilisateur, je veux que mes données soient chiffrées localement et migrées sans perte afin de protéger ma mémoire numérique.
Tasks:
- Configurer SQLCipher avec clé provenant de `AionDatabaseOptions` (env var AION_DB_KEY). `area:infra`, `priority:high`, `type:feature`
- Ajouter des migrations EF Core initiales couvrant métamodèle, F_Record, fichiers, notes, agenda. `area:infra`, `priority:high`, `type:feature`
- Mettre en place des tests d’intégration sur la création de base chiffrée. `area:test`, `priority:medium`, `type:feature`
- Ajouter la rotation de clé (re-chiffrement) via commande d’admin. `area:infra`, `priority:low`, `type:feature`

### Feature: File Storage sécurisé
User story: En tant qu’utilisateur, je veux uploader et consulter des fichiers (photos, PDF, audio) liés à mes enregistrements, avec stockage chiffré et métadonnées.
Tasks:
- Implémenter `AionFileStorageService` (upload/download/delete) avec stockage local + stub cloud. `area:infra`, `priority:high`, `type:feature`
- Créer la table `F_File` et liens vers EntityType/Record. `area:infra`, `priority:high`, `type:feature`
- Intégrer l’extraction de texte pour indexation (OCR pipeline stub). `area:ai`, `priority:medium`, `type:feature`
- Tests d’upload et de liaison fichier→record. `area:test`, `priority:medium`, `type:feature`

### Feature: Sauvegarde cloud
User story: En tant qu’utilisateur, je veux que ma base chiffrée soit sauvegardée et restaurable automatiquement afin de ne rien perdre.
Tasks:
- Implémenter `AionCloudBackupService` (upload/restore) vers un fournisseur S3-compatible. `area:infra`, `priority:high`, `type:feature`
- Scheduler de synchronisation périodique + déclenchement après modifications majeures. `area:infra`, `priority:medium`, `type:feature`
- Gestion des conflits et vérification d’intégrité avant restauration. `area:infra`, `priority:medium`, `type:feature`
- Tests d’intégration de backup/restore sur base chiffrée. `area:test`, `priority:medium`, `type:feature`

## Epic: UI MAUI Blazor / Aion Memory
Description: Construire l’interface MAUI Blazor Hybrid avec chat IA, modules dynamiques, agenda, notes, fichiers, dashboards.

### Feature: Chat IA central
User story: En tant qu’utilisateur, je veux discuter avec l’IA pour créer des modules, saisir des données et obtenir des rapports afin d’utiliser AION de façon naturelle.
Tasks:
- Créer la page d’accueil Blazor avec composant chat (texte + micro). `area:ui`, `priority:high`, `type:feature`
- Connecter le chat à l’orchestrateur IA (appel provider + actions). `area:ai`, `priority:high`, `type:feature`
- Ajouter l’historique des conversations et résumés de contexte. `area:ui`, `priority:medium`, `type:feature`
- Tests UI basiques (interaction simulée) pour le flux chat→action. `area:test`, `priority:medium`, `type:feature`

### Feature: Explorateur de modules dynamiques
User story: En tant qu’utilisateur, je veux naviguer dans mes modules, afficher des listes et formulaires générés à partir des métadonnées pour gérer mes données.
Tasks:
- Développer des composants Razor pour listes/formulaires dynamiques selon S_Field (types, validation). `area:ui`, `priority:high`, `type:feature`
- Gérer les relations (sélecteur, sous-listes) dans les formulaires. `area:ui`, `priority:medium`, `type:feature`
- Ajouter la navigation module → entité → enregistrement. `area:ui`, `priority:medium`, `type:feature`
- Tests UI sur un module seed (Potager) pour valider le rendu. `area:test`, `priority:medium`, `type:feature`

### Feature: Agenda & rappels UI
User story: En tant qu’utilisateur, je veux voir et planifier mes événements/rappels liés à mes enregistrements pour suivre mes échéances.
Tasks:
- Créer la page Agenda (vue calendrier jour/semaine/mois). `area:ui`, `priority:high`, `type:feature`
- Lier les événements à des enregistrements via `J_Event_Link`. `area:infra`, `priority:medium`, `type:feature`
- Support des récurrences et rappels (UI + service). `area:ui`, `priority:medium`, `type:feature`
- Tests fonctionnels sur la création/modification d’événements. `area:test`, `priority:medium`, `type:feature`

### Feature: Notes UI (texte + dictée)
User story: En tant qu’utilisateur, je veux enregistrer et lier des notes dictées ou textuelles à mes données pour enrichir chaque enregistrement.
Tasks:
- Créer la page Notes avec éditeur riche (Markdown) et upload audio. `area:ui`, `priority:high`, `type:feature`
- Intégrer la transcription (appel provider) et stockage texte. `area:ai`, `priority:high`, `type:feature`
- Ajouter la vue Journal sur la fiche d’un enregistrement. `area:ui`, `priority:medium`, `type:feature`
- Tests UI de création/liaison de notes et vérification de transcription. `area:test`, `priority:medium`, `type:feature`

### Feature: Fichiers & galerie
User story: En tant qu’utilisateur, je veux gérer mes fichiers liés aux enregistrements pour enrichir mes données visuellement.
Tasks:
- Créer la galerie de fichiers (vignettes, aperçu). `area:ui`, `priority:medium`, `type:feature`
- Ajouter l’upload drag & drop et liaison record. `area:ui`, `priority:medium`, `type:feature`
- Intégrer la recherche par nom/texte extrait. `area:ai`, `priority:medium`, `type:feature`
- Tests UI de cycle upload→affichage→suppression. `area:test`, `priority:medium`, `type:feature`

### Feature: Dashboard
User story: En tant qu’utilisateur, je veux un tableau de bord personnalisable regroupant widgets (listes, stats, rappels) pour suivre mes modules.
Tasks:
- Créer la page Dashboard avec grille de widgets. `area:ui`, `priority:medium`, `type:feature`
- Implementer quelques widgets par défaut (tâches en retard, rappels à venir, dernières notes). `area:ui`, `priority:medium`, `type:feature`
- Persister le layout utilisateur (JSON). `area:infra`, `priority:low`, `type:feature`
- Tests UI pour ajout/suppression/déplacement de widgets. `area:test`, `priority:low`, `type:feature`

## Epic: Notes & Agenda
Description: Renforcer les services de notes dictées et d’agenda pour offrir journalisation, transcription et rappels intelligents.

### Feature: NoteService complet
User story: En tant qu’utilisateur, je veux créer des notes (texte/dictée), les lier à des enregistrements et bénéficier de la transcription pour les retrouver facilement.
Tasks:
- Implémenter `AionNoteService` avec CRUD, transcription et liens `J_Note_Link`. `area:domain`, `priority:high`, `type:feature`
- Ajouter la classification automatique (tags) via IA. `area:ai`, `priority:medium`, `type:feature`
- Journal par enregistrement (timeline de notes/actions). `area:infra`, `priority:medium`, `type:feature`
- Tests unitaires sur création dictée→transcription→stockage texte. `area:test`, `priority:medium`, `type:feature`

### Feature: AgendaService et rappels
User story: En tant qu’utilisateur, je veux planifier des événements avec rappels et les lier à mes enregistrements afin de suivre mes échéances.
Tasks:
- Implémenter `AionAgendaService` (S_Event, rappels, récurrence). `area:domain`, `priority:high`, `type:feature`
- Liaison `J_Event_Link` et génération de rappels basés sur champs date. `area:infra`, `priority:medium`, `type:feature`
- Notifications locales (MAUI) pour rappels. `area:ui`, `priority:medium`, `type:feature`
- Tests fonctionnels sur création/modification/suppression d’événements. `area:test`, `priority:medium`, `type:feature`

## Epic: Automatisation
Description: Permettre aux utilisateurs d’automatiser des actions (notes, rappels, mises à jour) en réponse aux événements du DataEngine ou de l’agenda.

### Feature: Règles d’automatisation
User story: En tant qu’utilisateur, je veux définir des règles « si… alors… » pour automatiser la création de notes, rappels ou mises à jour afin de gagner du temps.
Tasks:
- Créer le modèle `S_AutomationRule` et son stockage. `area:infra`, `priority:high`, `type:feature`
- Implémenter le moteur d’événements (triggers CRUD, dates, conditions) branché sur DataEngine. `area:domain`, `priority:high`, `type:feature`
- Ajouter les actions disponibles (note, événement, notification, update de champ). `area:infra`, `priority:medium`, `type:feature`
- UI de composition de règle avec bibliothèque de templates. `area:ui`, `priority:medium`, `type:feature`
- Tests end-to-end sur règles prédéfinies (ex: dépense > seuil). `area:test`, `priority:medium`, `type:feature`

## Epic: Vision & OCR
Description: Offrir des capacités d’analyse visuelle (OCR, classification) pour alimenter automatiquement les modules et la recherche.

### Feature: Pipeline OCR
User story: En tant qu’utilisateur, je veux scanner un document et récupérer le texte dans mes modules pour éviter la saisie manuelle.
Tasks:
- Intégrer un service OCR (stub + provider cloud/local) exposé via `AionVisionService`. `area:ai`, `priority:high`, `type:feature`
- Ajouter un endpoint/commande pour associer le texte OCR à un record ciblé. `area:infra`, `priority:medium`, `type:feature`
- Indexer le texte OCR dans `SearchText` et `F_File`. `area:infra`, `priority:medium`, `type:feature`
- Tests sur un set d’images de facture/ticket. `area:test`, `priority:medium`, `type:feature`

### Feature: Classification & suggestion de module
User story: En tant qu’utilisateur, je veux que l’IA suggère le module approprié pour une photo (facture, plante, document) afin de gagner du temps.
Tasks:
- Implémenter un classifieur d’images (labels principaux) dans `AionVisionService`. `area:ai`, `priority:medium`, `type:feature`
- Ajouter une action d’auto-création d’enregistrement proposé (ex: dépense). `area:infra`, `priority:medium`, `type:feature`
- UI d’import photo avec suggestions de création/liaison. `area:ui`, `priority:medium`, `type:feature`
- Tests fonctionnels sur un jeu de photos échantillon. `area:test`, `priority:low`, `type:feature`

## Epic: LifeGraph / Lifelogging
Description: Construire la timeline de vie et le graphe relationnel reliant enregistrements, événements, notes et fichiers pour fournir des insights.

### Feature: Timeline de vie
User story: En tant qu’utilisateur, je veux voir la chronologie des événements, notes et actions afin de comprendre mes activités récentes.
Tasks:
- Créer la table `S_HistoryEvent` alimentée par DataEngine/Agenda/Notes. `area:infra`, `priority:medium`, `type:feature`
- Implémenter le service de timeline (filtrage par module/période). `area:domain`, `priority:medium`, `type:feature`
- Page Blazor « Timeline » avec défilement infini. `area:ui`, `priority:medium`, `type:feature`
- Tests d’intégration sur alimentation et affichage de la timeline. `area:test`, `priority:medium`, `type:feature`

### Feature: Graphe sémantique
User story: En tant qu’utilisateur, je veux naviguer dans un graphe de relations entre mes enregistrements pour découvrir des liens et insights.
Tasks:
- Modéliser `S_Link` (liaisons manuelles ou automatiques). `area:infra`, `priority:medium`, `type:feature`
- Implémenter un service de graphe exposant les voisins et types de liens. `area:domain`, `priority:medium`, `type:feature`
- Composant UI de visualisation (force-directed graph ou équivalent). `area:ui`, `priority:medium`, `type:feature`
- Tests sur navigation graphe (ajout/suppression de liens). `area:test`, `priority:low`, `type:feature`

## Epic: Marketplace de templates
Description: Fournir une bibliothèque de modules/templates partageables pour accélérer la création de nouveaux domaines.

### Feature: Catalogue de templates
User story: En tant qu’utilisateur, je veux parcourir et installer des templates de modules prêts à l’emploi afin d’éviter de repartir de zéro.
Tasks:
- Définir le format de package template (JSON + assets) et le dossier `marketplace`. `area:domain`, `priority:medium`, `type:feature`
- Implémenter un loader/exporter de template dans `AionTemplateService`. `area:infra`, `priority:medium`, `type:feature`
- UI de catalogue avec recherche et installation/désinstallation. `area:ui`, `priority:medium`, `type:feature`
- Tests d’import/export d’un template (ex: Potager). `area:test`, `priority:medium`, `type:feature`

### Feature: Partage communautaire
User story: En tant que créateur, je veux partager mes templates avec d’autres utilisateurs pour enrichir l’écosystème AION.
Tasks:
- Ajouter la signature et métadonnées auteur/licence dans les packages. `area:domain`, `priority:low`, `type:feature`
- Implémenter un dépôt distant (stub HTTP) pour publier/télécharger. `area:infra`, `priority:medium`, `type:feature`
- UI de publication et mise à jour de template. `area:ui`, `priority:low`, `type:feature`
- Tests de round-trip publication→installation. `area:test`, `priority:low`, `type:feature`

## Epic: Tests & Qualité
Description: Mettre en place une stratégie de tests et de qualité couvrant domaine, infra, UI et IA simulée.

### Feature: Suite de tests automatisés
User story: En tant que développeur, je veux une suite de tests couvrant le domaine et l’infra pour sécuriser les évolutions rapides du métamodèle et des services IA.
Tasks:
- Configurer des projets de tests unitaires et d’intégration (Domain, Infrastructure, UI). `area:test`, `priority:high`, `type:feature`
- Ajouter des fixtures de base de données chiffrée pour tests. `area:test`, `priority:medium`, `type:feature`
- Mock/stub des providers IA, OCR et stockage pour tests déterministes. `area:test`, `priority:medium`, `type:feature`
- Rapports de couverture et intégration CI. `area:test`, `priority:medium`, `type:chore`

### Feature: QA IA & prompts
User story: En tant que product owner, je veux vérifier la qualité des réponses IA (intent, module design, transcription) pour garantir l’utilisabilité.
Tasks:
- Définir un jeu de prompts/réponses attendues (golden tests). `area:ai`, `priority:medium`, `type:feature`
- Automatiser l’évaluation via tests enregistrés (basés sur stubs). `area:test`, `priority:medium`, `type:feature`
- Mettre en place un feedback loop pour ajuster les prompts. `area:ai`, `priority:medium`, `type:chore`

## Epic: Déploiement & CI/CD
Description: Industrialiser l’intégration continue, la livraison MAUI/desktop et les paramètres de configuration sécurisés.

### Feature: Pipeline CI/CD
User story: En tant qu’équipe, je veux builder, tester et publier automatiquement l’app pour garantir des livraisons fréquentes et stables.
Tasks:
- Configurer GitHub Actions pour build .NET 10 (restore, build, test). `area:infra`, `priority:high`, `type:chore`
- Ajouter la publication d’artefacts MAUI (Android/iOS) et desktop (Win/Mac) en nightly. `area:infra`, `priority:medium`, `type:feature`
- Intégrer l’analyse de code (lint, format) et couverture. `area:test`, `priority:medium`, `type:chore`
- Stocker les secrets (AION_DB_KEY, AI keys) via GitHub Secrets. `area:infra`, `priority:high`, `type:chore`

### Feature: Configuration & observabilité
User story: En tant qu’ops, je veux configurer facilement les providers (AI, stockage, backup) et observer les métriques pour maintenir la plateforme.
Tasks:
- Centraliser les options (AI, DB, Storage, Backup) dans appsettings avec validation. `area:infra`, `priority:medium`, `type:feature`
- Ajouter la télémétrie (logs structurés, traces, métriques) dans AppHost. `area:infra`, `priority:medium`, `type:chore`
- Dashboard de supervision (console ou dashboard web léger) pour surveiller jobs de backup/automation. `area:ui`, `priority:low`, `type:feature`
- Tests de configuration invalides et comportements de fallback. `area:test`, `priority:low`, `type:feature`

