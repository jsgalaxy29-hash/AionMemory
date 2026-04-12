# UI dynamique AionMemory

Cette itération met en place une interface 100 % générée à partir du métamodèle (tables, vues et champs) pour l’hôte MAUI/Blazor.

## ModuleHost
- Route : `/ModuleHost/{id:guid}` (alias `/module/{id}`).
- Charge le module via `IMetadataService`, matérialise les tables via `ITableDefinitionService` puis récupère les définitions complètes (vues comprises) via `IModuleViewService`.
- Sélection de l’entité et de la vue par l’utilisateur ; la vue par défaut (`IsDefault` ou `DefaultView`) est appliquée automatiquement (taille de page, tri de tête de vue).
- Rend trois blocs synchronisés :
  - `DynamicList` (liste + pagination/sort/recherche) sur la table active.
  - `DynamicForm` (création/édition) sur le même contexte.
  - `DynamicTimeline` (notes/évènements paginés) filtrée sur le couple TargetType/TargetId courant.
- Navigation et DI : aucun accès direct au DbContext, uniquement les services d’orchestration (`IRecordQueryService`, `IModuleViewService`, etc.).

## DynamicList
- Utilise `Virtualize` pour la virtualisation et `QuerySpec` pour la pagination (`Skip/Take`), le tri, le full-text et les vues (`View`).
- Contrôles UI : sélection de vue, tri (champ + direction), pagination (page précédente/suivante, taille de page) et recherche FTS immédiate.
- S’appuie exclusivement sur `IRecordQueryService`/`IModuleViewService` pour charger les métadonnées et les données, sans requête directe EF/DbContext.

## DynamicForm
- Rend chaque champ en fonction de `FieldDataType` (Text, Number, Decimal, Bool, Date/DateTime, etc.).
- Validation côté UI basée sur `SFieldDefinition` : requis, longueurs min/max, motifs, bornes numériques. Les erreurs serveur (ex. contraintes uniques) sont surfacées après appel au `IRecordQueryService.SaveAsync`.
- Les dates sont normalisées en ISO-8601 avant envoi au moteur, évitant les erreurs de validation côté DataEngine.

## DynamicTimeline
- Agrège notes (`INoteService`) et évènements (`IAgendaService`) dans une liste paginée ordonnée par date décroissante.
- Filtrage contextuel sur `TargetType`/`TargetId` pour refléter l’entité/enregistrement courant.
- Pagination intégrée (PageSize configurable) avec rafraîchissement manuel.

## Shell MAUI/Blazor
- Nouveau layout latéral listant les modules et actions rapides (Accueil, Dashboard, Marketplace).
- Le contexte de navigation (`UiState`) est mis à jour lors de la sélection d’un module/entité pour que les pages enfant reçoivent la bonne portée métamodèle.

