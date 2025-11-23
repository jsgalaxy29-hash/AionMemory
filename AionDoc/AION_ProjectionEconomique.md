# AION – Projection Économique et Modèle Financier

Ce document synthétise les estimations de coûts et de revenus pour le projet AION, afin d'évaluer sa viabilité économique et de construire un modèle financier.

## 1. Hypothèses de base

- **Modèle freemium** avec trois niveaux :
  - **Gratuit** : accès limité à l’IA (modèle local ou mini), 1 Go de stockage cloud, modules illimités.
  - **Premium** (~4,90 € / mois) : accès à des modèles IA avancés (OpenAI/Mistral), 50 Go de stockage, fonctionnalités avancées (OCR, prédiction).
  - **Pro** (~9,90 € / mois) : 200 Go de stockage, services prédictifs et vision illimitée, support prioritaire.
- **Stockage cloud** : coût moyen de 0,002 € par Go par mois (BackBlaze B2) + 0,0004 € par requête (upload/download).
- **IA** : modèle mini (~0,15 € / million de tokens) pour les utilisateurs gratuits, modèle GPT‑4.1 (~1 € / million) pour les utilisateurs premium, modèle Mistral (~3 € / million) pour les utilisateurs pro.
- **Trafic et bande passante** : coût approximatif 0,04 € / Go (cloud provider).
- **Taux de conversion** : 5 % des utilisateurs gratuits passent en Premium, 1 % en Pro.
- **Croissance utilisateur** : 1 000 utilisateurs au lancement, +10 % par mois (scénario réaliste).

## 2. Structure des coûts

### 2.1 Coût fixe mensuel

- Hébergement du backend, serveurs API, base de données partagée : ~100 €.
- Frais de stockage initial (100 Go de base) : ~0,20 €.
- Licence et abonnements nécessaires (plateformes IA mini) : ~50 €.
- Maintenance et support : variable, estimée à 1 000 € / mois au lancement (équipe réduite).

### 2.2 Coût variable par utilisateur (moyenne mensuelle)

| Poste                   | Gratuit (€/util) | Premium (€/util) | Pro (€/util) |
|-------------------------|------------------|------------------|--------------|
| IA (tokens)            | 0,10            | 0,70             | 1,50         |
| Stockage (Go)          | 0,05            | 0,50             | 2,00         |
| Trafic réseau          | 0,02            | 0,05             | 0,10         |
| Fichiers (upload/etc.) | 0,01            | 0,05             | 0,10         |
| **Total variable**     | **0,18 €**      | **1,30 €**       | **3,70 €**   |

## 3. Prévision des revenus (scénario réaliste)

- Lancement : 1 000 utilisateurs, dont 5 % Premium (50) et 1 % Pro (10).
- Croissance : +10 % d’utilisateurs par mois.
- Taux de conversion stable.

| Mois | Utilisateurs totaux | Premium (5 %) | Pro (1 %) | Revenus mensuels (approx.) |
|-----:|---------------------|--------------:|----------:|---------------------------:|
| 1    | 1 000              | 50            | 10        | 50×4,90 + 10×9,90 = 345 € |
| 6    | 1 610              | 81            | 16        | 81×4,90 + 16×9,90 ≈ 595 € |
| 12   | 2 853              | 143           | 29        | 143×4,90 + 29×9,90 ≈ 1 102 € |
| 24   | 7 414              | 371           | 74        | 371×4,90 + 74×9,90 ≈ 2 823 € |
| 36   | 19 262             | 963           | 193       | 963×4,90 + 193×9,90 ≈ 7 003 € |

Les revenus augmentent progressivement avec le nombre d’utilisateurs convertis.

## 4. Analyse de rentabilité

- **Seuil de rentabilité** : en supposant des coûts fixes mensuels de ~1 150 € et des coûts variables en fonction des utilisateurs, AION atteint le break‑even lorsque les revenus Premium + Pro couvrent les coûts. Avec les hypothèses ci‑dessus, le seuil est atteint vers **18 – 24 mois** (environ 2 500 utilisateurs, dont 125 Premium et 25 Pro).
- **Marge brute sur Premium** : 4,90 € – 1,30 € = 3,60 € par utilisateur.
- **Marge brute sur Pro** : 9,90 € – 3,70 € = 6,20 € par utilisateur.

## 5. Projections à 5 ans (scénarios)

### 5.1 Scénario optimiste

- Croissance de 15 % par mois.
- Taux de conversion Premium : 8 %, Pro : 2 %.
- Introduction d’une offre Enterprise à 19,90 €.
- Bénéfice net annuel > 200 000 € dès la 3ème année.

### 5.2 Scénario réaliste

- Croissance de 10 % par mois.
- Taux de conversion Premium : 5 %, Pro : 1 %.
- Lancement de nouvelles fonctionnalités (vision, prédiction) qui attirent des abonnements.
- Bénéfice net modéré (~50 000 €/an) vers la 4ème année.

### 5.3 Scénario prudent

- Croissance de 5 % par mois.
- Conversion faible (3 % Premium, 0,5 % Pro).
- Nécessité de capitaliser sur la marketplace et de proposer des modules payants.
- Break‑even atteint plus tard (> 36 mois).

## 6. Conclusion

AION présente un potentiel de rentabilité intéressant à moyen terme. Le modèle freemium permet d’acquérir une base d’utilisateurs large, tandis que les abonnements Premium et Pro financent les coûts variables. Les services avancés (vision, prédiction, automation, marketplace) offrent des leviers de monétisation supplémentaires. Une gestion prudente des coûts IA et de stockage et un investissement initial modéré assurent la viabilité du projet.