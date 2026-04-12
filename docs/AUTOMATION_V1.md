# AUTOMATION_V1

> PROMPT 25 — AUTOMATION ENGINE v1

## Objectifs

- Déclencher des règles déterministes en fonction d’évènements (`IF / THEN`).
- Évaluer explicitement des conditions sans logique cachée.
- Exécuter un petit nombre d’actions v1 : ajout de tag, création de note, planification de rappel.

## Modèle de règle

- **Déclencheur** : `AutomationTriggerType` (`OnCreate`, `OnUpdate`, `OnDelete`, `Event`, `Scheduled`).  
  La règle est activée si le trigger correspond et que `TriggerFilter` correspond exactement au nom de l’évènement.
- **Conditions** : liste d’expressions sérialisées (JSON) `AutomationConditionDefinition` avec :
  - `Left` : chemin dans le payload (ex. `data.Title`, `tableId`, `recordId`).
  - `Operator` : `Equals | NotEquals | Contains | StartsWith | EndsWith | GreaterThan | LessThan | Exists | NotExists`.
  - `Right` : valeur de comparaison (optionnelle).
- **Actions** : liste ordonnée d’actions (`AutomationActionType`) exécutées séquentiellement.
  - `Tag` : ajoute un tag sur un enregistrement (`tag` requis, `field` optionnel – défaut `Tags`).
  - `CreateNote` : crée une note texte (`title` requis, `content` optionnel, payload sérialisé par défaut).
  - `ScheduleReminder` : crée un évènement agenda (`title` requis, `start`/`reminderAt` ISO-8601 optionnels).

Les paramètres d’action sont stockés en JSON (`ParametersJson`) et conservés en l’état pour transparence.

## Évènements disponibles

- **DataEngine** : `record.created`, `record.updated`, `record.deleted` avec payload `{ tableId, recordId, data }`.
- **Custom** : via `IAutomationOrchestrator.TriggerAsync` qui publie un évènement `AutomationTriggerType.Event` avec un payload sérialisé.

## Déroulement d’exécution

1. Sélection des règles actives correspondant au trigger et au filtre (ordre déterministe : `Name`, puis `Id`).
2. Évaluation des conditions (ordre déterministe : `Id` croissant). Si une condition échoue, l’exécution est marquée `Skipped`.
3. Exécution séquentielle des actions. Les actions non prises en charge sont ignorées avec un résultat explicite.
4. Chaque exécution est journalisée dans `AutomationExecutions` avec le snapshot complet du payload.

## Conception et garanties

- Moteur déterministe (ordre stable des règles/conditions/actions, aucun comportement implicite).
- Payload sérialisé JSON pour auditabilité, sans mutation implicite.
- Boucles évitées pour les actions internes (`Tag` repose sur un contexte d’exécution qui supprime les triggers imbriqués).
- Pas de dépendance EF Core ou IO dans le domaine : le moteur vit dans `Aion.Infrastructure`, les contrats dans `Aion.Domain`.
