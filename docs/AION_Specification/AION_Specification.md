# AION – Spécifications Techniques et Architecture

Ce document décrit l'architecture technique et le métamodèle d'AION, ainsi que les services qui le composent. Il intègre les modifications liées au système de notes dictées et les perspectives d’évolution.

## 1. Vue d'ensemble

AION est une application multiplateforme (mobile et desktop) basée sur .NET (MAUI Blazor Hybrid). Elle fonctionne hors‑ligne grâce à une base locale chiffrée (SQLite + SQLCipher) et synchronise les données via un cloud privé. Le cœur de l’application repose sur un moteur de données générique associé à des métadonnées décrivant les modules créés à la volée par l’IA.

## 2. Métamodèle de données

Le métamodèle permet de définir dynamiquement les structures de données manipulées par l’utilisateur. Les principales tables de métadonnées sont :

- **S_Module** : représente un module (ex : Potager, Finances). Contient `Id`, `Name`, `Icon`, `Description`, `CreatedAt`, `CreatedByAI`.
- **S_EntityType** : définit un type d’entité (ex : Culture, Parcelle). Attributs : `Id`, `ModuleId`, `Name` (nom technique), `Label` (nom affiché), `Description`.
- **S_Field** : définit un champ d’entité. Attributs : `Id`, `EntityTypeId`, `Name`, `Label`, `DataType` (String, Int, Decimal, Date, Bool, Enum, Relation, File), `IsRequired`, `IsSearchable`, `IsListVisible`, `DefaultValue`, `EnumValues`, `RelationTargetEntityTypeId`.
- **S_Relation** : définit une relation entre entités (OneToMany, ManyToMany). Attributs : `Id`, `FromEntityTypeId`, `ToEntityTypeId`, `RelationKind`, `RoleName`.
- **S_ReportDefinition** : décrit un rapport paramétrable (liste, agrégation, graphique). Attributs : `Id`, `ModuleId`, `Name`, `Description`, `DefinitionJson`.

### Modélisation des données utilisateur

Pour la première version, les données de l’utilisateur sont stockées dans une table générique :

- **F_Record** : `Id`, `EntityTypeId`, `JsonData`, `SearchText`, `CreatedAt`, `UpdatedAt`.

`JsonData` contient les valeurs des champs sous forme JSON. `SearchText` est un champ indexé plein texte concaténant les champs textuels et le contenu des notes liées (pour la recherche rapide).

À terme, un générateur pourra créer des tables physiques spécifiques à chaque module lorsque celui‑ci est stabilisé.

## 3. Services transverses

### 3.1 AionMetadataService

- CRUD des métadonnées (S_Module, S_EntityType, S_Field, S_Relation, S_ReportDefinition).
- Conversion d’un `ModuleDefinition` (renvoyé par l’IA) en enregistrements persistés.
- Versionning des schémas pour permettre les évolutions des modules.

### 3.2 AionDataEngine

- CRUD générique sur les données `F_Record`.
- Validation des champs selon `S_Field`.
- Interprétation des relations et jointures.
- Hooks avant/après création/édition pour déclencher automatisations et calculs.

### 3.3 AionNoteService (version améliorée)

- Gestion des notes : création, modification, suppression.
- Gestion des liens via `J_Note_Link` (note liée à un enregistrement).
- Support des notes dictées : enregistrement audio → transcription en texte (stockage en texte).
- Journal par enregistrement (fil chronologique de notes et actions).
- Interface riche (Markdown, pièces jointes, tags).
- Service d’IA pour la transcription et la catégorisation automatique.

### 3.4 AionAgendaService

- Gestion des événements calendaires (`S_Event`) : création, modification, suppression, rappels, répétitions.
- Liaison d’un événement à un enregistrement via `J_Event_Link`.
- Génération de rappels selon des règles (ex : N jours après un champ date).
- Vue calendrier (jour, semaine, mois, liste).

### 3.5 AionFileStorageService

- Upload, téléchargement et suppression de fichiers.
- Stockage chiffré dans un service cloud (Azure Blob, BackBlaze…).
- Table `F_File` pour les métadonnées (Id, EntityTypeId, RecordId, FileName, MimeType, Size, StorageKey, CreatedAt).
- Intégration avec le moteur de recherche (texte extrait, nom de fichier).

### 3.6 AionCloudBackupService

- Sauvegarde du fichier SQLite chiffré `Aion.db.enc` dans le cloud.
- Restauration automatique lors d’une réinstallation (avec passphrase locale).
- Synchronisation périodique et déclenchée (après X minutes d’inactivité ou modification importante).

### 3.7 AionSearchService

- Construction de l’index plein texte (FTS5) pour `F_Record.SearchText` et le contenu des notes.
- API de recherche multi‑module.
- Préparation d’un index vectoriel pour recherche sémantique (future version).

## 4. Couche IA (Aion.AI)

### 4.1 IA Provider

- Abstraction `IAionAIProvider` pour permettre l’intégration de différents moteurs (OpenAI, Mistral, modèle local).
- Méthodes principales :
  - `ChatAsync(string userMessage, AionContext context)`: conversation générale.
  - `DesignModuleAsync(string userPrompt)`: génération d’un `ModuleDefinition` à partir d’un domaine.
  - `InterpretUserMessageAsync(string message)`: détection d’intention et création d’un plan d’action (comprenant CRUD, notes, agenda, reporting).

### 4.2 Intent Detector

- Identifie si la requête utilisateur concerne :
  - la création d’un module,
  - la manipulation d’un module existant (add/modify/delete),
  - la création d’une note,
  - la planification d’un rappel,
  - une recherche,
  - une génération de rapport.

### 4.3 Module Designer

- Construit un `ModuleDefinition` : listes d’entités, champs, relations, rapports.
- Utilise des sources référentielles (ontologies, bases de modèles métiers).
- Interaction avec l’utilisateur pour affiner le modèle.

### 4.4 CRUD Interpreter

- Transforme les commandes conversationnelles en appels au `AionDataEngine`.
- Exemple : « Crée une nouvelle culture de tomates » → CreateRecord pour `EntityType=Culture`.

### 4.5 Agenda Interpreter

- Détecte les demandes de rappels et d’événements.
- Appelle `AionAgendaService` pour créer/planifier un rappel.

### 4.6 Note Interpreter

- Assure la création de notes dictées ou textuelles.
- Demande à la couche transcription d’obtenir le texte depuis l’audio.

### 4.7 Report Interpreter

- Génère les rapports en utilisant `S_ReportDefinition` ou en créant un rapport ad hoc.

## 5. Interface utilisateur

### 5.1 MAUI Blazor Hybrid

- L’interface est développée avec Blazor et hébergée dans une application MAUI pour permettre un fonctionnement natif Android, iOS, Windows et macOS.
- Les pages principales :
  - **Accueil (Chat IA)** : conversation, suggestions, accès rapide aux modules.
  - **Modules** : liste des modules disponibles, création et modification de modules.
  - **Explorateur de module** : listes et formulaires CRUD générés dynamiquement en fonction du métamodèle.
  - **Agenda** : vue calendrier et liste des événements.
  - **Notes** : liste, recherche et création de notes (texte et dictée).
  - **Fichiers** : galerie des fichiers et documents.
  - **Dashboard** (future version) : widgets personnalisables.
  - **Marketplace** (future version) : import/export de templates.
  - **Paramètres** : configuration du provider IA, des sauvegardes cloud, des quotas et du persona.

### 5.2 Dynamic UI

- Les formulaires et listes sont générés dynamiquement à partir de `S_Field` : le moteur UI doit savoir afficher un champ selon son type et ses attributs.
- Les relations apparaissent sous forme de sélecteurs ou de sous‑listes.
- Les notes et les événements liés sont accessibles depuis la vue détaillée d’un enregistrement.

## 6. Sécurité et confidentialité

- La base locale est chiffrée via SQLCipher, avec une clé générée localement et stockée via SecureStorage.
- Le cloud ne stocke que des fichiers chiffrés (aucune donnée en clair côté serveur).
- Les communications avec les services IA et le stockage sont sécurisées par HTTPS.
- Les notes dictées transitent uniquement vers le service de transcription (local si possible ou cloud chiffré).
- Respect des lois européennes (RGPD).

## 7. Perspectives d’évolution technique

- **Génération de tables physiques** : création automatique de tables SQLite pour optimiser les performances des modules stabilisés.
- **Index vectoriel local** : intégration d’un moteur vectoriel (Qdrant, SQLite‑Vec) pour recherche sémantique hors ligne.
- **Analyse locale d’images** : intégration de modèles vision embarqués pour l’OCR et la classification sans dépendre du cloud.
- **Synchronisation multi‑appareils** : stratégies de merge et de résolution de conflits, gestion de comptes multi‑utilisateurs.
- **Plugin système** : API pour permettre à des développeurs externes d’étendre AION (widgets, services, connecteurs).

## 8. Conclusion

Cette spécification fournit une base complète pour la mise en œuvre d’AION. Elle présente le métamodèle, la structure de la base, les services transverses et la couche IA. Elle intègre la modification majeure du système de notes (notes dictées enregistrées en texte) ainsi que les ambitions d’évolution vers des services plus avancés (automatisation, vision, lifelogging, graphes sémantiques, prédiction). Cette architecture modulaire garantit la pérennité du projet et sa capacité à s’adapter aux besoins futurs.