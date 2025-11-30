# Checklist par couche

Ce document suit la progression des travaux par couche logique et rappelle la roadmap proposée. Les cases peuvent être cochées à mesure que les fonctionnalités sont stabilisées.

## A. Aion.Domain
- [ ] Compléter/normaliser le métamodèle STable/SField/SView (fields calculés, contraintes, mapping avec S_Field/S_EntityType). (partiel : propriétés ajoutées mais pas encore validées par des scénarios réels)
- [x] Ajouter les value objects (ex : Email, Phone, Money) si requis par les docs ; préciser la nullability par défaut. (Email/Phone/Money présents avec validations de base)
- [ ] Aligner S_Relation/S_Field avec les exigences de `AION_Specification` (enum values, flags IsSearchable/IsListVisible, etc.). (partiel : flags principaux présents, conformité complète non vérifiée)
- [ ] Documenter/figer les contrats IA (IIntentDetector, IModuleDesigner, ICrudInterpreter, IReportInterpreter, IVisionService) et leurs modèles d’E/S. (interfaces existantes, pas de validation formelle)

## B. Aion.Infrastructure
- [x] Supprimer la dépendance directe sur Aion.AI ou isoler les orchestrateurs via interfaces domaine. (csproj ne référence qu’Aion.Domain)
- [ ] Étendre AionDataEngine : génération de vues/filters, validation avancée, support des relations/lookups, indexation full-text/embeddings. (partiel : CRUD et validation basique, pas d’indexation sémantique)
- [ ] Couvrir STable/SField/SView dans les migrations et synchroniser avec DataEngine ; fournir scripts SQLCipher et initialisation automatique. (migrations présentes, synchronisation et scripts à vérifier)
- [ ] Finaliser FileStorage chiffré et sécurisation des clés (SecureStorage, rotation) ; ajouter gestion des quotas. (Je ne sais pas – stockage chiffré non audité)
- [ ] Finaliser Backup/Restore (intégrité, rotation, planification) et logs structurés. (Je ne sais pas – flux non vérifié)
- [ ] Implémenter Marketplace/TemplateService (export/import de modules, signature/versionning) et DashboardService complet. (partiel : entités présentes, services non finalisés)
- [ ] Ajouter tests EF Core/integration (DbContext, services, interceptors SQLCipher) + fixtures SQLite. (tests partiels, pas d’exécution)

## C. Aion.AI
- [ ] Définir abstractions explicites pour LLM/Embeddings/Transcription/Vision (providers) indépendantes de l’infrastructure. (partiel : interfaces dans le domaine, implémentations stubs)
- [ ] Brancher un provider réel (OpenAI/Mistral/Ollama) avec configuration sécurisée (clés, timeouts, retries) et gérer les erreurs. (non fait, providers actuels restent simulés)
- [ ] Compléter orchestrateurs : IntentDetector, ModuleDesigner, CrudInterpreter, ReportInterpreter, Agenda/Noto/Vision interpreters (prompts, parsing robuste). (partiel : orchestrateurs basiques, parsing minimal)
- [ ] Ajouter un moteur de recherche sémantique (embeddings) connecté à ISearchService/IAionDataEngine. (non commencé)
- [ ] Couvrir les workflows IA par tests unitaires (prompts parsés, fallback stub) et mocks d’API. (non commencé)

## D. Aion.AppHost (MAUI/Blazor)
- [ ] Page d’accueil modules avec création (+) et navigation cohérente ; relier ModuleBuilder et metadata store. (Je ne sais pas – scénario non vérifié)
- [ ] Lier DynamicForm/DynamicList/DynamicTimeline au DataEngine/métamodèle (chargement STable/SField, validation, CRUD auto). (partiel : DynamicForm existe mais intégration non testée)
- [ ] Compléter pages Modules/RecordDetail/Agenda/Notes/Dashboard pour utiliser réellement les services (IAionDataEngine, NoteService, AgendaService, Dashboard). (Je ne sais pas – intégration non auditée)
- [ ] Ajouter UI pour marketplace/templates (import/export), backups, persona, prédictions, vision. (non commencé)
- [ ] Intégrer chat/IA dans l’UI (appel orchestrateurs, rendu des propositions) et gérer états/erreurs. (partiel : composant Chat présent mais flux non validé)
- [ ] Prévoir thèmes/réactivité et ergonomie mobile ; ajouter tests UI (bUnit) si possible. (non commencé)

## Roadmap suggérée
- **v0 (stabilisation)** : sécuriser configuration SQLCipher/stockage, migrations complètes, DataEngine CRUD + formulaires dynamiques basiques, provider IA stub unique.
- **v1 (fonctionnel)** : provider IA réel, orchestrateurs finalisés (intent/CRUD/report), Module Builder connecté, marketplace/import-export, sauvegardes planifiées + logs.
- **v2 (avancé)** : recherche sémantique et vision, automatisations complètes, dashboards widgets, persona/predictions, CI/CD + tests automatiques.
