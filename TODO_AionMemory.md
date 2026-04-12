# TODO AionMemory

## État global
- Projet au stade de squelette avancé : modèles de domaine, DbContext/migrations, services métiers et fournisseurs IA factices sont en place, ainsi qu’une UI MAUI/Blazor esquissée. La cohérence modulaire reste partielle (références transverses, orchestrations IA/infrastructure incomplètes) et plusieurs fonctionnalités sont uniquement définies dans les documents.

## Forces actuelles
- Métamodèle et contrats alignés (modules, entités, champs, automations, notes, agenda, stockage, marketplace, persona, prédictions). 
- Infrastructure EF Core + SQLite/SQLCipher avec validations d’options, services principaux (DataEngine, Notes dictées, Agenda, Stockage, Backups) et vues de recherche. 
- Couche IA prête à recevoir des providers HTTP (LLM, embeddings, transcription, vision) avec orchestrateurs légers (intent detection, module design, CRUD/report interpreter). 
- UI MAUI/Blazor disposant de composants dynamiques (liste, formulaire, timeline, module builder) et pages modules/notes/agenda/dashboard.

## Gros chantiers restants (priorités)
1) Revoir la séparation des dépendances (Infrastructure dépend de l’IA) et clarifier les interfaces injectées. 
2) Consolider le métamodèle STable/SField/SView et l’aligner avec S_Field/S_EntityType, y compris validations et synchronisation DataEngine/DB. 
3) Finaliser les orchestrateurs IA (intent, CRUD/report, agenda/note/vision) et brancher un provider réel (OpenAI/équivalent) avec configuration sécurisée. 
4) Compléter le DataEngine (requêtes dynamiques, vues, filtres, calculs, indexes) et le relier à la UI (DynamicForm/DynamicList). 
5) Mettre en place la partie marketplace/templates (export/import, signature, listing) et l’UI d’accueil des modules + création (Module Builder). 
6) Fermer les boucles de sécurité (gestion clés SQLCipher, stockage chiffré, backups + rotation, logs/audit). 
7) Ajouter tests unitaires/intégration et pipeline CI ; couvrir validation options, encryption, orchestrations IA et composants dynamiques.

## Checklist par couche

### A. Aion.Domain
- [ ] Compléter/normaliser le métamodèle STable/SField/SView (fields calculés, contraintes, mapping avec S_Field/S_EntityType). 
- [ ] Ajouter les value objects (ex : Email, Phone, Money) si requis par les docs ; préciser nullability par défaut. 
- [ ] Aligner S_Relation/S_Field avec les exigences de `AION_Specification` (enum values, flags IsSearchable/IsListVisible, etc.). 
- [ ] Documenter/figer les contrats IA (IIntentDetector, IModuleDesigner, ICrudInterpreter, IReportInterpreter, IVisionService) et leurs modèles d’E/S.

### B. Aion.Infrastructure
- [ ] Supprimer la dépendance directe sur Aion.AI ou isoler les orchestrateurs via interfaces domaine. 
- [ ] Étendre AionDataEngine : génération de vues/filters, validation avancée, support des relations/lookups, indexation full-text/embeddings. 
- [ ] Couvrir STable/SField/SView dans les migrations et synchroniser avec DataEngine ; fournir scripts SQLCipher et initialisation automatique. 
- [ ] Finaliser FileStorage chiffré et sécurisation des clés (SecureStorage, rotation) ; ajouter gestion des quotas. 
- [ ] Finaliser Backup/Restore (intégrité, rotation, planification) et logs structurés. 
- [ ] Implémenter Marketplace/TemplateService (export/import de modules, signature/versionning) et DashboardService complet. 
- [ ] Ajouter tests EF Core/integration (DbContext, services, interceptors SQLCipher) + fixtures SQLite.

### C. Aion.AI
- [ ] Définir abstractions explicites pour LLM/Embeddings/Transcription/Vision (providers) indépendantes de l’infrastructure. 
- [ ] Brancher un provider réel (OpenAI/Mistral/Ollama) avec configuration sécurisée (clés, timeouts, retries) et gérer les erreurs. 
- [ ] Compléter orchestrateurs : IntentDetector, ModuleDesigner, CrudInterpreter, ReportInterpreter, Agenda/Noto/Vision interpreters (prompts, parsing robuste). 
- [ ] Ajouter un moteur de recherche sémantique (embeddings) connecté à ISearchService/IAionDataEngine. 
- [ ] Couvrir les workflows IA par tests unitaires (prompts parsés, fallback stub) et mocks d’API.

### D. Aion.AppHost (MAUI/Blazor)
- [ ] Page d’accueil modules avec création (+) et navigation cohérente ; relier ModuleBuilder et metadata store. 
- [ ] Lier DynamicForm/DynamicList/DynamicTimeline au DataEngine/métamodèle (chargement STable/SField, validation, CRUD auto). 
- [ ] Compléter pages Modules/RecordDetail/Agenda/Notes/Dashboard pour utiliser réellement les services (IAionDataEngine, NoteService, AgendaService, Dashboard). 
- [ ] Ajouter UI pour marketplace/templates (import/export), backups, persona, prédictions, vision. 
- [ ] Intégrer chat/IA dans l’UI (appel orchestrateurs, rendu des propositions) et gérer états/erreurs. 
- [ ] Prévoir thèmes/réactivité et ergonomie mobile ; ajouter tests UI (bUnit) si possible.

## Roadmap suggérée
- **v0 (stabilisation)** : sécuriser configuration SQLCipher/stockage, migrations complètes, DataEngine CRUD + formulaires dynamiques basiques, provider IA stub unique. 
- **v1 (fonctionnel)** : provider IA réel, orchestrateurs finalisés (intent/CRUD/report), Module Builder connecté, marketplace/import-export, sauvegardes planifiées + logs. 
- **v2 (avancé)** : recherche sémantique et vision, automatisations complètes, dashboards widgets, persona/predictions, CI/CD + tests automatiques.
