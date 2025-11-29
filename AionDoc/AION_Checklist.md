# Checklist par couche

Ce document suit la progression des travaux par couche logique et rappelle la roadmap proposée. Les cases peuvent être cochées à
 mesure que les fonctionnalités sont stabilisées.

## Synthèse rapide (priorités immédiates)
- [ ] Sécuriser la base technique : migrations complètes (STable/SField/SView) + configuration SQLCipher prête en env. dev/test.
- [ ] Finaliser les abstractions IA (contrats + provider réel) et raccorder un orchestrateur de bout en bout (Intent ou CRUD).
- [ ] Brancher le Module Builder/DynamicForm au DataEngine pour afficher/éditer un module réel.
- [ ] Mettre en place les flux de sauvegarde/restauration + logs structurés avec planification minimale.
- [ ] Introduire un cycle de tests automatisés (EF Core + orchestrateurs IA) pour verrouiller les régressions.

## A. Aion.Domain
- [ ] Compléter/normaliser le métamodèle STable/SField/SView (fields calculés, contraintes, mapping avec S_Field/S_EntityType).
  - Inclure exemples de définitions JSON et conventions de nommage pour les vues/listes.
- [ ] Ajouter les value objects (ex : Email, Phone, Money) si requis par les docs ; préciser la nullability par défaut.
  - Documenter validation et conversions (ex : Money <-> decimal, Phone <-> string) dans un guide rapide.
- [ ] Aligner S_Relation/S_Field avec les exigences de `AION_Specification` (enum values, flags IsSearchable/IsListVisible, etc.).
  - Capturer les mappings de valeurs d’énumération dans un tableau unique pour réduire les divergences.
- [ ] Documenter/figer les contrats IA (IIntentDetector, IModuleDesigner, ICrudInterpreter, IReportInterpreter, IVisionService) et leurs modèles d’E/S.
  - Ajouter jeux d’inputs/outputs d’exemple pour les tests unitaires des orchestrateurs.

## B. Aion.Infrastructure
- [ ] Supprimer la dépendance directe sur Aion.AI ou isoler les orchestrateurs via interfaces domaine.
  - Introduire des adaptateurs/providers configurables pour chaque orchestrateur afin de faciliter le swap (stub vs prod).
- [ ] Étendre AionDataEngine : génération de vues/filters, validation avancée, support des relations/lookups, indexation full-text/embeddings.
  - Prioriser un flux CRUD simple démontrable (STable + SField minimal) avant d’étendre aux filtres.
- [ ] Couvrir STable/SField/SView dans les migrations et synchroniser avec DataEngine ; fournir scripts SQLCipher et initialisation automatique.
  - Vérifier la compatibilité CLI (dotnet-ef) et documenter les paramètres de chiffrement par environnement.
- [ ] Finaliser FileStorage chiffré et sécurisation des clés (SecureStorage, rotation) ; ajouter gestion des quotas.
  - Ajouter un test d’intégration simple (upload/download) sur SQLite pour valider la configuration.
- [ ] Finaliser Backup/Restore (intégrité, rotation, planification) et logs structurés.
  - Définir un plan minimal (ex : snapshot quotidien + 7 jours) et enregistrer les métriques clés (durée, taille, statut).
- [ ] Implémenter Marketplace/TemplateService (export/import de modules, signature/versionning) et DashboardService complet.
- [ ] Ajouter tests EF Core/integration (DbContext, services, interceptors SQLCipher) + fixtures SQLite.
  - Visibiliser les chemins happy path/erreur à couvrir en priorité.

## C. Aion.AI
- [ ] Définir abstractions explicites pour LLM/Embeddings/Transcription/Vision (providers) indépendantes de l’infrastructure.
  - Ajouter un tableau de compatibilité (provider → features supportées) pour guider la configuration.
- [ ] Brancher un provider réel (OpenAI/Mistral/Ollama) avec configuration sécurisée (clés, timeouts, retries) et gérer les erreurs.
  - Prévoir un mode dégradé (stub) activable par variable d’environnement et tracer les décisions de fallback.
- [ ] Compléter orchestrateurs : IntentDetector, ModuleDesigner, CrudInterpreter, ReportInterpreter, Agenda/Noto/Vision interpreters (prompts, parsing robuste).
  - Lister les prompts cibles dans un dossier partagé et inclure les schémas JSON attendus.
- [ ] Ajouter un moteur de recherche sémantique (embeddings) connecté à ISearchService/IAionDataEngine.
- [ ] Couvrir les workflows IA par tests unitaires (prompts parsés, fallback stub) et mocks d’API.
  - Prioriser les tests de parsing et la détection d’erreurs de provider (timeouts, quota, format inattendu).

## D. Aion.AppHost (MAUI/Blazor)
- [ ] Page d’accueil modules avec création (+) et navigation cohérente ; relier ModuleBuilder et metadata store.
  - Inclure un parcours de démo (création rapide d’un module « Contacts ») pour valider bout en bout.
- [ ] Lier DynamicForm/DynamicList/DynamicTimeline au DataEngine/métamodèle (chargement STable/SField, validation, CRUD auto).
  - Ajouter des states de chargement/erreur visibles et instrumentation minimale (logs). 
- [ ] Compléter pages Modules/RecordDetail/Agenda/Notes/Dashboard pour utiliser réellement les services (IAionDataEngine, NoteService, AgendaService, Dashboard).
- [ ] Ajouter UI pour marketplace/templates (import/export), backups, persona, prédictions, vision.
  - Prévoir un drop zone ou sélecteur de fichier pour l’import, avec feedback utilisateur clair.
- [ ] Intégrer chat/IA dans l’UI (appel orchestrateurs, rendu des propositions) et gérer états/erreurs.
- [ ] Prévoir thèmes/réactivité et ergonomie mobile ; ajouter tests UI (bUnit) si possible.

## Roadmap suggérée
- **v0 (stabilisation)** : sécuriser configuration SQLCipher/stockage, migrations complètes, DataEngine CRUD + formulaires dynamiques basiques, provider IA stub unique.
- **v1 (fonctionnel)** : provider IA réel, orchestrateurs finalisés (intent/CRUD/report), Module Builder connecté, marketplace/import-export, sauvegardes planifiées + logs.
- **v2 (avancé)** : recherche sémantique et vision, automatisations complètes, dashboards widgets, persona/predictions, CI/CD + tests automatiques.
