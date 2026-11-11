# DPM Score (ADC), reconstitué depuis 75 parties ADC de dpm.lol

Les données ont été relevées le 10/11/2026.

**Score = min(100, 15 + global + lane + objectifs + équipe + résultat + bot)**

Partie « courte » = moins de 20 min. Les valeurs entre crochets sont les plafonds.

| Catégorie | Détail | Formule |
|---|---|---|
| global | kills / assists | 0,75 chacun |
| | morts | −1,5 chacune ; **0 mort = +4** |
| | CS/min | 1,8 × (csm − 6,1) [±10] — courte : réf 6,1/1,3 |
| | or/min | 0,04 × (gpm − 350) [±10] — courte : réf 350/1,3 |
| | dégâts/min | 0,00675 × dpm [≤10] — courte : 0,01125 × dpm |
| | vision/min | 7 × (vspm − 0,55) [±10] — courte : réf 0,25 |
| | first blood | +5 |
| lane [−5 ; 10] | écart XP vs adversaire | 0,001 × diff |
| | écart or | 0,0015 × diff |
| | vision | 2 × (vspm moi / vspm adv − 1) |
| | avance max CS | 0,08 × maxCsAdvantageOnLaneOpponent |
| objectifs [≤20] | dragons (équipe) | 2 chacun (courte : 4) |
| | barons / héraut / grubs | 2 / 3 / 0,5 |
| | dégâts objectifs | 0,00012 × dmg [≤4] |
| | vols | 3,5 |
| équipe [≤10] | KP | 0,12 × KP% − 3 |
| | % dégâts subis | 0,03 × % |
| | % dégâts équipe | 0,1 × % |
| résultat | victoire / défaite | +3 / −3 |
| bot [−5 ; 20] | solo kills | 1,5 chacun [≤10] |
| | sbires de lane à 10 min | 0,37 × (n − 51) |
| | tourelles détruites | 0,75 chacune |
| | 1re tourelle de l'équipe | +5 |
| | plaques | 0,3 × `challenges.turretPlatesTaken` [≤2,5] |

Toutes les lignes collent à l'identique sur les 75 parties. Le calcul à partir de l'API Riot a aussi été vérifié avec `--check` : 0,00 d'écart sur 5 parties.

Une incertitude reste sur les dragons : ×2 ou ×4 a été observé seulement en dessous / au-dessus de 20 min.
