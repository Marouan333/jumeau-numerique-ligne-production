# Résultats des scénarios d'optimisation, mesurés avec la scène Unity

Mesures du 2026-11-10 11:01, avec la scène Unity connectée au backend (flux WebSocket réel).
Chaque scénario est joué 3600 s simulées (1 h) à la vitesse ×20, avec le backend lancé avec `SIM_SEED=7` pour que les pannes et les rejets soient reproductibles.
Ces valeurs sont lues sur le flux WebSocket réel, celui qui anime la scène 3D, et non sur une simulation séparée.

## Avec pannes aléatoires

| Indicateur | Situation initiale | Contrôle automatisé (8 s) | 2 postes de contrôle | Convoyeur adaptatif |
|---|---|---|---|---|
| Produits finis (1 h) | 218 | 393 (+80 %) | 394 (+81 %) | 218 |
| Cadence moyenne (produits/min) | 3,63 | 6,55 (+80 %) | 6,57 (+81 %) | 3,63 |
| Rejets | 12 | 22 | 23 | 12 |
| Attente au contrôle (s) | 115 | 9 | 13 | 40 |
| En-cours moyen | 19,0 | 13,6 | 14,7 | 9,4 |
| Séjour moyen (s) | 286 | 115 | 125 | 145 |
| Pannes | 25 | 30 | 30 | 25 |
| Arrêts cumulés (s) | 588 | 659 | 697 | 588 |
| Goulot observé | Contrôle qualité | Traitement | Traitement | Contrôle qualité |
| Goulot théorique | Contrôle qualité | Traitement | Traitement | Contrôle qualité |
| Cadence théorique max (/min) | 4,0 | 7,5 | 7,5 | 4,0 |
| Utilisation du contrôle | 96 % | 93 % | 87 % | 96 % |

Écart avec la simulation hors ligne de référence (produits finis sur 1 h) :

| Scénario | Mesuré avec Unity | Référence hors ligne | Écart |
|---|---|---|---|
| Situation initiale | 218 | 218 | 0,0 % |
| Contrôle automatisé (8 s) | 393 | 390 | 0,8 % |
| 2 postes de contrôle | 394 | 389 | 1,3 % |
| Convoyeur adaptatif | 218 | 218 | 0,0 % |

## Sans panne

| Indicateur | Situation initiale | Contrôle automatisé (8 s) | 2 postes de contrôle | Convoyeur adaptatif |
|---|---|---|---|---|
| Produits finis (1 h) | 226 | 422 (+87 %) | 421 (+86 %) | 226 |
| Cadence moyenne (produits/min) | 3,77 | 7,03 (+86 %) | 7,02 (+86 %) | 3,77 |
| Rejets | 12 | 24 | 24 | 12 |
| Attente au contrôle (s) | 114 | 0 | 0 | 40 |
| En-cours moyen | 19,2 | 12,2 | 13,0 | 9,5 |
| Séjour moyen (s) | 280 | 97 | 104 | 142 |
| Pannes | 0 | 0 | 0 | 0 |
| Arrêts cumulés (s) | 0 | 0 | 0 | 0 |
| Goulot observé | Contrôle qualité | Traitement | Traitement | Contrôle qualité |
| Goulot théorique | Contrôle qualité | Traitement | Traitement | Contrôle qualité |
| Cadence théorique max (/min) | 4,0 | 7,5 | 7,5 | 4,0 |
| Utilisation du contrôle | 100 % | 100 % | 93 % | 100 % |

Écart avec la simulation hors ligne de référence (produits finis sur 1 h) :

| Scénario | Mesuré avec Unity | Référence hors ligne | Écart |
|---|---|---|---|
| Situation initiale | 226 | 226 | 0,0 % |
| Contrôle automatisé (8 s) | 422 | 422 | 0,0 % |
| 2 postes de contrôle | 421 | 421 | 0,0 % |
| Convoyeur adaptatif | 226 | 226 | 0,0 % |

## Lecture

- **Goulot initial.** Le contrôle qualité (15 s) est le goulot : il est utilisé à 96 % avec pannes et à 100 % sans panne, sa file reste pleine (attente moyenne d'environ 114 s devant lui) et la cadence reste sous le maximum théorique de 4 produits/min (3,63 avec pannes, 3,77 sans panne).
- **Traiter le goulot.** Passer le contrôle à 8 s ou le doubler donne presque le même résultat : environ 393 produits/h avec pannes (contre 218, soit +80 %) et environ 422 sans panne (contre 226, soit +87 %). Le goulot passe au traitement (8 s), et l'attente devant le contrôle tombe à moins de 15 s.
- **Convoyeur adaptatif.** La production ne change pas (218 avec pannes, 226 sans), car le goulot reste le contrôle. En revanche l'en-cours moyen est divisé par deux (environ 9,4 contre 19) et l'attente devant le contrôle passe de 114 à 40 s.
- **Effet des pannes.** Elles retirent environ 3 % de production à la situation initiale et environ 7 % aux scénarios améliorés (393 contre 422), car le traitement devient le poste limitant et chaque arrêt y coûte du débit.
- **Cohérence avec la simulation hors ligne.** Les écarts de production sont inférieurs à 1,5 % ; la situation initiale et le convoyeur adaptatif donnent exactement la même production, et les séries sans panne sont identiques à la référence. Les petits écarts viennent probablement du pas de simulation (0,05 s en ligne, 0,1 s hors ligne).

## Limites

- Une seule graine aléatoire (7) : les valeurs avec pannes dépendent de ce tirage. Pour une étude statistique, répéter avec plusieurs graines.
- Les deux scénarios améliorés sont presque équivalents dans ce modèle : le choix entre automatiser le contrôle et le doubler relève d'un coût que le modèle ne contient pas.
