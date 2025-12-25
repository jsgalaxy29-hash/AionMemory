# Trust UX — explicabilité & contrôle

Objectif : garantir que l'utilisateur comprend **ce que fait le système**, **pourquoi** et **avec quelles données**.

## Principes
- **Pas de magie opaque** : chaque suggestion, résumé ou lien proposé doit être justifié.
- **Traçabilité** : les données sources et les règles utilisées sont toujours accessibles.
- **Contrôle utilisateur** : l'utilisateur peut consulter les explications avant d'agir.

## UI : écrans « Pourquoi ? »

Un écran dédié `/trust-ux` expose :
- les **suggestions IA** et leur justification,
- les **résumés IA** issus des analyses mémoire,
- les **liens proposés** entre enregistrements.

Chaque section affiche :
1. **Action** : ce que le système propose de faire.
2. **Pourquoi** : la raison explicite.
3. **Données utilisées** : sources précises (identifiants, titres, extraits).
4. **Règles appliquées** : heuristiques ou contraintes guidant la recommandation.

## Métadonnées explicatives (IA)

Les services IA doivent fournir des explications structurées via `MemoryAnalysisExplanation` :

```json
{
  "summary": "texte concis",
  "topics": [{ "name": "...", "keywords": [] }],
  "links": [
    {
      "fromId": "uuid",
      "toId": "uuid",
      "reason": "texte",
      "fromType": "note|event|record",
      "toType": "...",
      "explanation": {
        "sources": [
          {
            "recordId": "uuid",
            "title": "...",
            "sourceType": "note|event|record",
            "snippet": "..."
          }
        ],
        "rules": [
          { "code": "rule-id", "description": "..." }
        ]
      }
    }
  ],
  "explanation": {
    "sources": [
      {
        "recordId": "uuid",
        "title": "...",
        "sourceType": "note|event|record",
        "snippet": "..."
      }
    ],
    "rules": [
      { "code": "rule-id", "description": "..." }
    ]
  }
}
```

## Données persistées

`MemoryInsight` conserve désormais :
- `Summary`
- `Topics`
- `SuggestedLinks`
- `Explanation` (sources + règles)

Cela permet d'afficher les détails d'explicabilité dans l'UI sans recalcul.
