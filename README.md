# Jumeau numérique d'une ligne de production

Projet Industrie 4.0 – ENSAM. Ligne simplifiée à 4 postes : **Alimentation (5 s) → Traitement (8 s) → Contrôle qualité (15 s) → Emballage (7 s)**.

- **Backend Python** (`backend/`) : simulation de la ligne (files d'attente, blocages, pannes, rejets) + diffusion temps réel par WebSocket.
- **Frontend Three.js** (`frontend/`) : ligne en 3D animée + dashboard KPI + pilotage des scénarios d'optimisation.

## Lancer

Python 3.10 à 3.13 :

```bash
python -m venv .venv
source .venv/bin/activate        # Windows : .venv\Scripts\activate
pip install -r requirements.txt
python backend/server.py
```

Puis ouvrir <http://localhost:8000>. Three.js est chargé depuis un CDN (connexion Internet nécessaire).

## Architecture

```
 navigateur                                   backend Python
┌──────────────────────────┐   WebSocket   ┌────────────────────────┐
│ scene.js  (3D Three.js)  │ ◀── état ──── │ server.py (FastAPI)    │
│ app.js    (dashboard)    │ ─ commandes ▶ │ sim.py    (simulation) │
└──────────────────────────┘    /ws        └────────────────────────┘
```

Le backend fait avancer la simulation par pas de 0,05 s simulée et envoie l'état complet 10 fois par seconde. La 3D et le dashboard lisent **le même flux**, donc ils restent synchronisés.

| Fichier | Rôle | Responsable |
|---|---|---|
| `backend/sim.py` | Logique de la ligne, pannes, KPI, comparaison de scénarios | Simulation industrielle |
| `backend/server.py` | Serveur WebSocket, commandes, diffusion | Backend & Communication |
| `frontend/js/scene.js` | Modèles 3D, convoyeur, animations, états visuels | 3D & Architecture / Animation |
| `frontend/js/app.js` | KPI, tableau des postes, graphe, contrôles | Dashboard & KPI |
| Comparaison (`compare_presets`) | Scénarios avant/après, export CSV | Optimisation & Validation |

## Protocole WebSocket (`/ws`)

**Backend → navigateur**, message `state` (10 Hz) :

```json
{
  "type": "state", "speed": 4, "paused": false, "preset": "initial",
  "config": { "times": [5, 8, 15, 7], "servers": [1, 1, 1, 1], "adaptive": false, "failures": true },
  "stations": [{ "name": "Contrôle qualité", "state": "marche", "queue": 6, "util": 0.96, "avail": 0.97, "wait": 110.0, "servers": [{ "busy": true, "progress": 0.4 }] }],
  "products": [{ "id": 12, "st": 2, "s": "q", "k": 0 }],
  "kpi": { "throughput_now": 3.9, "theoretical": 4.0, "produced": 120, "rejected": 6, "wip": 18, "bottleneck_observed": "Contrôle qualité" },
  "history": [[1, 0.0], [2, 0.0]],
  "events": [{ "t": 31.0, "text": "Panne : Contrôle qualité (17 s)", "kind": "panne" }],
  "exits": [[11, "ok"]]
}
```

États des postes : `marche`, `attente` (pas de produit), `bloque` (aval plein), `panne`.
Produits : `s` = `q` (en file), `p` (en traitement, `p` = progression), `t` (sur le convoyeur vers le poste `st`).

**Navigateur → backend**, commandes :

| Commande | Exemple |
|---|---|
| Appliquer un scénario | `{"cmd":"preset","name":"controle_auto"}` |
| Temps d'un poste | `{"cmd":"set_time","station":2,"value":8}` |
| Machines en parallèle | `{"cmd":"set_servers","station":2,"value":2}` |
| Convoyeur adaptatif | `{"cmd":"adaptive","value":true}` |
| Pannes aléatoires | `{"cmd":"failures","value":false}` |
| Provoquer / réparer une panne | `{"cmd":"break","station":1}` · `{"cmd":"repair","station":1}` |
| Vitesse, pause, remise à zéro | `{"cmd":"speed","value":10}` · `{"cmd":"pause","value":true}` · `{"cmd":"reset"}` |
| Comparaison des scénarios | `{"cmd":"compare","duration":3600}` → réponse `{"type":"compare","results":[...]}` |

## Scénarios d'optimisation

| Scénario | Réglage | Effet attendu |
|---|---|---|
| Situation initiale | contrôle 15 s | goulot au contrôle, 4 produits/min max |
| Contrôle automatisé | contrôle 8 s | goulot déplacé au traitement, 7,5/min max |
| 2 postes de contrôle | 2 × 15 s en parallèle | 7,5/min max |
| Convoyeur adaptatif | files limitées à 3, convoyeur ralenti devant une file chargée | même débit, moins d'en-cours |

Exemple de résultats (1 h simulée, pannes activées, graine fixe) :

| Indicateur | Initiale | Contrôle 8 s | 2 contrôles | Adaptatif |
|---|---|---|---|---|
| Produits / h | 218 | 390 (+79 %) | 389 (+78 %) | 218 |
| Attente au contrôle | 115 s | 9 s | 16 s | 41 s |
| En-cours moyen | 19,0 | 13,4 | 14,9 | 9,4 |

Le débit réel est inférieur au débit théorique à cause des pannes (MTBF 400–600 s, MTTR 20–30 s), des transferts (2 s) et des rejets (5 %). Tous ces paramètres sont dans `DEFAULT_CONFIG` (`backend/sim.py`).
