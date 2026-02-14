# AION Checklist

Checklist structurée pour valider les livrables AION avant diffusion (démonstration, sprint review ou release). Les sections peuvent être copiées dans un ticket et cochées au fil de l'avancement.

## 1. Architecture & Code
- [ ] Dépendances restaurées et builds `dotnet` verts (solution + tests unitaires).
- [ ] Couverture des domaines clés : `Aion.Domain`, `Aion.Infrastructure`, `Aion.AI`, `AionMemory.Logic`, hôte MAUI.
- [ ] Pas d'exception à l'initialisation (options, DI, configuration).
- [ ] Interfaces IA (`IEmbeddingProvider`, `ILLMProvider`, etc.) branchées sur un provider réel ou stub documenté.
- [ ] Migrations ou schéma SQLite alignés avec les entités du domaine.

## 2. Sécurité & Données
- [ ] Clé SQLCipher fournie via `AION_DB_KEY` ou secret OS, jamais commitée.
- [ ] Chemins `storage`, `storage/backup`, `marketplace` validés et accessibles.
- [ ] Logs sensibles évitent d'exposer clés/tokens ; rotation minimale configurée.
- [ ] Export/backup chiffré priorisé, restauration testée.
- [ ] Données de démo (Potager, marketplace) isolées des environnements réels.

## 3. Fonctionnel & UX (MAUI Blazor)
- [ ] Navigation MAUI opérationnelle (page principale, composants dynamiques).
- [ ] Formulaires dynamiques (Potager, marketplace) rendent les champs attendus.
- [ ] Dictée/vision : placeholders ou intégrations testées sans crash.
- [ ] Accessibilité de base : contrastes, labels, tailles tactiles, messages d'erreur clairs.
- [ ] Mode offline/latence géré (états de chargement, cache minimal si dispo).

## 4. IA & Automation
- [ ] Embeddings générés et indexés pour la recherche sémantique.
- [ ] Génération LLM testée pour au moins un scénario (rapport, résumé, interprétation CRUD/report).
- [ ] Détection d'intention routée vers les bons modules ou documentée comme work-in-progress.
- [ ] Automatisations planifiées (rappels, sauvegardes) configurées et observables dans les logs.

## 5. Qualité & Observabilité
- [ ] Tests unitaires pertinents pour les services critiques (storage, IA, marketplace).
- [ ] Lint/format appliqué (C#, Razor, MAUI) ; avertissements compilateur traités.
- [ ] Logs structurés sur les actions sensibles (import/export, génération IA, rotation de clé).
- [ ] Tableaux de bord ou métriques minimales (temps de réponse, erreurs) prêts pour instrumentation ultérieure.

## 6. Livraison & Documentation
- [ ] README/notes de déploiement mises à jour (commandes `dotnet`, configuration).
- [ ] Licences et mentions légales vérifiées (dépendances, contenus IA générés).
- [ ] Checklist copiée dans le ticket ou la PR et cochée.
- [ ] Plan de démonstration prêt (parcours utilisateur, données de test, captures ou vidéo).
