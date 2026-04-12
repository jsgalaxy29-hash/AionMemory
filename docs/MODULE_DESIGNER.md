# Module Designer IA

Ce document décrit le workflow `DesignModuleAsync` qui génère un `ModuleSpec` complet et applique la spec via le `ModuleApplier`.

## Objectif

- Générer un `ModuleSpec v1` conforme à `docs/ModuleSpec.schema.json`.
- Interroger l’utilisateur si des informations essentielles manquent (boucle de clarification).
- Optionnellement enrichir la conception avec des sources `schema.org`.

## Contrats

Namespace : `Aion.Domain.ModuleBuilder`

- `ModuleDesignRequest` :
  - `Prompt` : description utilisateur.
  - `Locale` : langue (défaut `fr-FR`).
  - `UseSchemaOrg` : active l’enrichissement par des sources `schema.org`.
  - `Answers` : réponses aux questions de clarification (liste de `ModuleDesignAnswer`).
- `ModuleDesignResult` :
  - `Spec` : `ModuleSpec` si la conception est complète.
  - `Questions` : questions de clarification si la conception est incomplète.
  - `Sources` : références optionnelles (ex. `schema.org`).
  - `RawJson` : JSON brut renvoyé par l’IA.
- `ModuleDesignApplyResult` :
  - `Design` : résultat de conception.
  - `Tables` : tables créées/appliquées (résultat de `ModuleApplier`).

## Boucle de clarification

1. Appel `DesignModuleAsync`.
2. Si `Questions` est non vide, l’UI collecte les réponses.
3. L’UI rappelle `DesignModuleAsync` avec `Answers` renseignées.
4. Répéter jusqu’à obtention d’un `ModuleSpec` valide (`IsComplete`).

## Application du module

Utiliser `DesignAndApplyAsync` (service `IModuleDesignService`) :

1. Génère le `ModuleSpec` via l’IA.
2. Valide la spec.
3. Applique la spec via `IModuleApplier`.

## Notes sur schema.org

Quand `UseSchemaOrg = true`, l’IA ajoute des sources au format :

```json
{ "title": "Thing", "url": "https://schema.org/Thing", "type": "schema.org" }
```

Ces sources servent de références fonctionnelles, sans modifier la structure finale du `ModuleSpec`.
