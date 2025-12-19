# Module Builder IA v1

Cette version introduit un pipeline strict *spec → validate → apply* pour créer ou mettre à jour des modules via l’IA. La logique suit un contrat JSON unique et reste idempotente afin que plusieurs exécutions donnent le même résultat.

## Contrat ModuleSpec v1
- **Classes C#** : `ModuleSpec`, `TableSpec`, `FieldSpec`, `ViewSpec`, `LookupSpec` (`Aion.Domain.ModuleBuilder`).
- **Version** : `1.0` via `ModuleSpecVersions.V1`.
- **Schéma JSON** : `docs/ModuleSpec.schema.json`.
- **Types de champ** : `ModuleFieldDataTypes` expose les types autorisés et le mapping vers `FieldDataType`.

## Validation
Service : `ModuleValidator` (`Aion.Infrastructure.ModuleBuilder`, interface `IModuleValidator`).

Vérifications clés :
- Slugs module/table/field/view non vides et uniques (dans la spec et en base pour les tables).
- `dataType` membre de la liste autorisée.
- Cohérence `required/default` + compatibilité de type (numérique, date ISO, booléen, enum, etc.) et respect des bornes `min/max` + `validationPattern`.
- `enumValues` requis pour `Enum`, sans doublons et alignés avec le défaut éventuel.
- `lookup.targetTableSlug` doit exister (dans la même spec si `ModuleId` null, sinon spec ou base).
- Vues : clés de filtre et champ de tri référencent des champs déclarés; `defaultView` correspond à une vue.

En cas d’erreur, `ModuleValidationException` agrège toutes les anomalies.

## Application (idempotente)
Service : `ModuleApplier` (`Aion.Infrastructure.ModuleBuilder`, interface `IModuleApplier`).

- Upsert des `STable`, `SFieldDefinition`, `SViewDefinition` à partir de la spec.
- Normalise `displayName`, sérialise les valeurs par défaut, mappe les énumérations et lookups.
- Crée une vue minimale “all” si aucune n’est fournie et force une vue par défaut.
- Réexécuter `ApplyAsync` avec la même spec ne duplique rien.

## ModuleDesigner IA
Implémentation : `ModuleSpecDesigner` (`Aion.AI.ModuleBuilder`, interface `IModuleSpecDesigner`).
- Utilise le provider LLM existant (`ILLMProvider`).
- Génère un JSON compact `ModuleSpec v1` sans texte hors JSON.
- Expose le JSON brut via `LastGeneratedJson`.

## Orchestration end-to-end
Service : `ModuleBuilderService` (`Aion.Infrastructure.ModuleBuilder`).
- `DesignAsync(prompt)` : génère puis valide; lève `ModuleValidationException` si la spec est invalide (aucune application DB).
- `DesignAndApplyAsync(prompt)` : génère, valide et applique en un appel.

## Tests d’intégration
Fichier : `tests/Aion.Infrastructure.Tests/ModuleBuilderTests.cs`
- **Créer module simple** : table + 5 champs + 1 vue liste, application deux fois pour vérifier l’idempotence.
- **Modifier module** : ajout d’un champ et d’une vue form sur une spec existante + test d’idempotence.

## Points clés d’utilisation
1. Construire/valider : `await moduleValidator.ValidateAndThrowAsync(spec);`
2. Appliquer : `await moduleApplier.ApplyAsync(spec);`
3. IA end-to-end : `await moduleBuilderService.DesignAndApplyAsync(prompt);`

Les services sont enregistrés dans l’injection de dépendances (`AddAionAi` pour le designer IA, `AddAionInfrastructure` pour la validation/appliqueur/orchestrateur).
