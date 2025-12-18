# Prompt Codex – Développement complet d’AION (version enrichie)

Tu es Codex, une IA experte en développement .NET (MAUI Blazor, EF Core, C#), en architecture logicielle, en IA et en sécurité. Tu dois concevoir et générer AION, une application personnelle qui sert de mémoire numérique intelligente. Cette version enrichie intègre de nouveaux services (automatisation, vision, lifelogging, LifeGraph, prédiction, marketplace) et un système de notes dictées transcrites en texte.

## 1. Objectifs du projet

- Offrir à un utilisateur un « second cerveau » numérique : capture, organisation, recherche, rappel et analyse de toutes ses informations.
- Générer automatiquement des modules métiers à partir d’une description en langage naturel (ex : potager, finances, santé).
- Permettre la saisie via chat IA, clavier et dictée vocale, avec transcription automatique des notes dictées.
- Associer notes, fichiers et événements à n’importe quelle donnée.
- Fournir un moteur d’automatisation, un tableau de bord, un système de templates, un graphe de vie et des services de vision et de prédiction.
- Sauvegarder localement dans une base chiffrée et synchroniser dans le cloud.

## 2. Structure de la solution

Crée une solution .NET multi‑projets :

1. **`Aion.Domain`** : entités, métamodèle (S_Module, S_EntityType, S_Field, S_Relation, S_ReportDefinition, S_Note, J_Note_Link, S_Event, J_Event_Link, F_File, F_Record…), interfaces des services, value objects.
2. **`Aion.Infrastructure`** : DbContext (SQLite + SQLCipher), implémentations des services (Metadata, DataEngine, NoteService version dictée, Agenda, FileStorage, CloudBackup, Search, Automation, Dashboard, Template, LifeGraph, Predict, Persona).
3. **`Aion.AI`** : interfaces et implémentations des providers IA (OpenAI, Mistral, modèles locaux), services d’interprétation (IntentDetector, ModuleDesigner, CRUDInterpreter, AgendaInterpreter, NoteInterpreter, ReportInterpreter), gestion de la transcription vocale.
4. **`Aion.AppHost`** : application MAUI Blazor Hybrid, pages et composants dynamiques (chat, modules, listes, formulaires, agenda, notes, fichiers, tableau de bord, marketplace, paramètres).

Tu devras définir les classes, méthodes, interfaces et base de données nécessaires, et intégrer les fonctionnalités décrites.

## 3. Nouvelles exigences fonctionnelles

Outre les éléments décrits dans le prompt initial, tu dois inclure :

1. **Notes dictées transcrites** : le service de notes doit permettre d’enregistrer une note vocale, de la transcrire en texte via le provider IA et de la sauvegarder sous forme de texte (avec option de conserver le fichier audio). Les notes peuvent être libres, liées à un enregistrement ou constituer un journal chronologique.
2. **Automatisation** : implémenter `AionAutomationService` avec un moteur de règles (déclencheurs, conditions, actions). Prévois un schéma de stockage des règles (`S_AutomationRule`).
3. **Vision IA** : intégrer `AionVisionService` permettant l’OCR et la classification d’images. Implémentation initiale : stub avec interface et utilisation du provider IA (ex : OpenAI Vision ou modèle local). Prévois la table `S_VisionAnalysis` si nécessaire.
4. **LifeGraph et Lifelogging** : ajouter `AionLifeService` pour stocker des événements historiques et des liens (`S_HistoryEvent`, `S_Link`), et fournir des APIs de timeline et de graphe.
5. **Dashboard** : créer `AionDashboardService` et des composants pour afficher des widgets dynamiques. Prévois des classes `DashboardWidget` et une table de configuration.
6. **Template / Marketplace** : `AionTemplateService` pour exporter/importer des modules. Prévois la serialisation/désérialisation des métadonnées et l’intégration d’une marketplace (initialement un dossier local).
7. **Prédiction et Suggestions** : `AionPredictService` qui fournit des APIs d’analyse et propose des rappels proactifs. Ne développe pas l’algorithme complet, mais définis l’interface et les structures nécessaires.
8. **Persona Engine** : `AionPersonaEngine` pour personnaliser le ton et le style de l’IA. Implémentation initiale : paramètre dans la configuration utilisateur.

## 4. Spécifications techniques clés

- **Base locale** : utilise SQLite chiffré avec SQLCipher. Fournis un helper pour ouvrir la connexion avec la clé. Mets en place les migrations EF Core.
- **Sauvegarde cloud** : implémente un service de sauvegarde/restauration utilisant un stockage objet (ex : Azure Blob) et gère le fichier SQLite chiffré.
- **Fichiers** : utilise un service de stockage objet pour uploader et stocker les fichiers et les enregistrements vocaux. Prévois la génération de miniatures pour les images.
- **UI dynamique** : écris des composants Blazor génériques (`DynamicEntityList`, `DynamicEntityForm`) capables de se générer à partir des métadonnées. Intègre des composants pour les notes dictées (enregistrement audio) et pour la timeline.
- **Tests** : si le temps le permet, ajoute des tests unitaires pour valider les services (métadonnées, data engine, notes dictées).

## 5. Éléments à livrer

- Code source complet compilable sous .NET 10.
- Scripts d’initialisation de la base pour un module d’exemple (Potager), y compris entités, champs, relations, rapports, automations et fichiers de test.
- Documentation interne (README, diagrammes des modules, instructions de build).
- Instructions pour intégrer des providers IA (avec clé API en paramètres).

## 6. Conseils de développement

- Respecte la séparation Domain / Infrastructure / AI / App.
- Factorise la logique commune (validations, conversions JSON).
- Évite de coder en dur des modules spécifiques ; utilise le métamodèle pour tout.
- Prévois des hooks et des événements pour l’automatisation.
- Documente les interfaces et les classes importantes.
- Garde la base extensible pour ajouter les services futurs (vision, prédiction, marketplace).

---

En suivant ce prompt, tu fourniras un squelette complet et robuste d’AION enrichi, prêt à être personnalisé et amélioré pour devenir la mémoire numérique intelligente des utilisateurs. Chaque extension est préparée pour être activée progressivement, sans compromettre la stabilité du noyau.