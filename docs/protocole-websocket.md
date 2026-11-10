# Protocole WebSocket du jumeau numérique

Contrat entre le backend Python (`backend/server.py`) et les clients : la scène Unity WebGL et le dashboard web. Tous les clients lisent le même flux, donc ils restent synchronisés.

- URL : `ws://localhost:8000/ws`
- Format : un message JSON par trame texte
- Le serveur est la seule source de vérité. Les clients n'envoient que des commandes.

## Temps et cadence

| Élément | Valeur |
|---|---|
| Pas de simulation | 0,05 s simulée |
| Envoi de l'état | toutes les 0,1 s réelle (10 Hz) |
| Vitesse | 0,25 à 20 (secondes simulées par seconde réelle), 4 par défaut |

Un client 3D doit interpoler entre deux messages `state` pour animer à 60 images par seconde. Conseil : garder les deux derniers états, afficher avec un retard fixe d'environ 150 ms, et interpoler la progression (`p`) des produits.

## Serveur vers client

### `hello` (une fois, à la connexion)

```json
{ "type": "hello",
  "stations": [{"key": "alimentation", "name": "Alimentation"}, "..."],
  "presets": {"initial": "Situation initiale", "controle_auto": "Contrôle automatisé (8 s)",
              "double_controle": "2 postes de contrôle", "convoyeur_adaptatif": "Convoyeur adaptatif"} }
```

### `state` (10 Hz)

```json
{ "type": "state", "speed": 4, "paused": false, "preset": "initial", "run_id": 3,
  "config": { "times": [5, 8, 15, 7], "servers": [1, 1, 1, 1], "adaptive": false,
              "failures": true, "buffer": 8, "wip_limit": 3 },
  "stations": [ ... ], "products": [ ... ], "kpi": { ... },
  "history": [[1, 0.0], [2, 0.0]], "events": [ ... ], "exits": [[11, "ok"]] }
```

**`stations[i]`** (index 0 à 3 : Alimentation, Traitement, Contrôle qualité, Emballage)

| Champ | Sens |
|---|---|
| `key`, `name` | identifiant et nom affiché |
| `state` | `marche`, `attente` (pas de produit), `bloque` (aval plein), `panne` |
| `time` | temps de cycle (s) |
| `queue` | taille de la file en amont |
| `util`, `avail`, `blocked` | taux d'utilisation, de disponibilité, de blocage (0 à 1) |
| `wait` | attente moyenne en file (s) |
| `downtime`, `failures` | temps d'arrêt cumulé (s) et nombre de pannes |
| `repair_in` | secondes restantes avant la fin de réparation (0 si en marche) |
| `mtbf_obs`, `mttr_obs` | MTBF et MTTR observés, `null` tant qu'il n'y a pas eu de panne |
| `servers[]` | machines en parallèle : `busy`, `done` (fini, attend de la place), `progress` (0 à 1) |

**`products[]`** : un objet par produit présent sur la ligne

| `s` | Situation | Champs |
|---|---|---|
| `q` | en file devant le poste `st` | `id`, `st`, `k` (rang dans la file) |
| `p` | en traitement au poste `st` | `id`, `st`, `k` (machine), `p` (progression 0 à 1), `rej` (sera rejeté) |
| `t` | sur le convoyeur vers le poste `st` | `id`, `st`, `p` (progression 0 à 1 du transfert) |

**`kpi`**

`t`, `produced`, `rejected`, `reject_pct`, `throughput_now` (produits/min sur la dernière minute), `throughput_avg`, `theoretical`, `theoretical_hour`, `wip`, `avg_wip`, `lead_time`, `wait_controle`, `bottleneck_theoretical`, `bottleneck_observed`, `downtime_total`, `failures`.

**`events[]`** : `{ "t": 31.0, "text": "Panne : Contrôle qualité (17 s)", "kind": "panne" }`. Valeurs de `kind` : `info`, `panne`, `ok`.

**`exits[]`** : produits sortis depuis le message précédent, `[id, "ok"]` (fini) ou `[id, "rej"]` (rejeté). Sert à déclencher l'animation de sortie ou le bac de rebut.

### `compare`

Réponse à la commande `compare`, en deux temps :

```json
{ "type": "compare", "status": "running" }
{ "type": "compare", "status": "done", "duration": 3600, "results": [ { "key": "...", "label": "...", "config": {}, "produced": 218, "...": 0 } ] }
```

### `error`

```json
{ "type": "error", "message": "poste inconnu" }
```

## Client vers serveur (commandes)

| Commande | Message | Limites |
|---|---|---|
| Scénario | `{"cmd":"preset","name":"controle_auto"}` | noms listés dans `hello.presets` |
| Temps d'un poste | `{"cmd":"set_time","station":2,"value":8}` | 1 à 60 s |
| Machines en parallèle | `{"cmd":"set_servers","station":2,"value":2}` | 1 à 3 |
| Convoyeur adaptatif | `{"cmd":"adaptive","value":true}` | booléen |
| Pannes aléatoires | `{"cmd":"failures","value":false}` | booléen |
| Provoquer une panne | `{"cmd":"break","station":1}` | poste 0 à 3 |
| Réparer | `{"cmd":"repair","station":1}` | poste 0 à 3 |
| Vitesse | `{"cmd":"speed","value":10}` | 0,25 à 20 |
| Pause | `{"cmd":"pause","value":true}` | booléen |
| Remise à zéro | `{"cmd":"reset"}` | |
| Comparaison | `{"cmd":"compare","duration":3600}` | 600 à 28800 s simulées |

## API REST (historique)

| Route | Rôle |
|---|---|
| `GET /api/state` | instantané de l'état courant |
| `GET /api/runs` | liste des exécutions enregistrées |
| `GET /api/history?run=<id>` | relevés et événements d'une exécution |
| `GET /api/export.csv?run=<id>` | export CSV (toutes les exécutions si `run` est absent) |

## Notes pour le client Unity

- Une seule simulation est partagée : tous les clients voient la même ligne et la même vitesse.
- Mapper chaque `station` (0 à 3) sur un emplacement fixe de la scène. Les files et le convoyeur se déduisent de `queue`, `k` et `p`.
- Un produit `s = t` avance de la station précédente vers la station `st`. Sa position est une interpolation avec `p`.
- Ne jamais calculer la logique côté Unity : seul l'affichage dépend de ce flux.
- Les messages `state` arrivent à 10 Hz. Ne pas bloquer le thread principal en traitant le JSON : décoder, puis appliquer à la scène dans `Update`.
