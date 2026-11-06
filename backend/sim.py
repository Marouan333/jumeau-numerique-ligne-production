"""Simulation d'une ligne de production à 4 postes (pas de temps fixe).

Ligne : Alimentation -> Traitement -> Contrôle qualité -> Emballage.
Chaque poste peut avoir plusieurs machines en parallèle ("servers"), une file
d'attente en amont (tampon de capacité limitée), des pannes aléatoires et un
temps de cycle réglable. Les produits circulent entre les postes sur un
convoyeur (temps de transfert).
"""
from __future__ import annotations

import random
from collections import deque
from copy import deepcopy

STATIONS = [
    {"key": "alimentation", "name": "Alimentation"},
    {"key": "traitement", "name": "Traitement"},
    {"key": "controle", "name": "Contrôle qualité"},
    {"key": "emballage", "name": "Emballage"},
]
N = len(STATIONS)
CONTROLE = 2

DEFAULT_CONFIG = {
    "times": [5.0, 8.0, 15.0, 7.0],      # temps de traitement (s)
    "servers": [1, 1, 1, 1],             # machines en parallèle par poste
    "transfer": 2.0,                     # temps de transfert convoyeur (s)
    "buffer": 8,                         # capacité de la file avant chaque poste
    "reject_rate": 0.05,                 # taux de rejet au contrôle
    "failures": True,                    # pannes aléatoires
    "mtbf": [600.0, 500.0, 400.0, 600.0],  # temps moyen entre pannes (s)
    "mttr": [20.0, 30.0, 25.0, 20.0],      # temps moyen de réparation (s)
    "adaptive": False,                   # convoyeur adaptatif
    "wip_limit": 3,                      # file max en mode adaptatif
}

PRESETS = {
    "initial": {"label": "Situation initiale", "times": [5, 8, 15, 7], "servers": [1, 1, 1, 1], "adaptive": False},
    "controle_auto": {"label": "Contrôle automatisé (8 s)", "times": [5, 8, 8, 7], "servers": [1, 1, 1, 1], "adaptive": False},
    "double_controle": {"label": "2 postes de contrôle", "times": [5, 8, 15, 7], "servers": [1, 1, 2, 1], "adaptive": False},
    "convoyeur_adaptatif": {"label": "Convoyeur adaptatif", "times": [5, 8, 15, 7], "servers": [1, 1, 1, 1], "adaptive": True},
}


class Product:
    __slots__ = ("id", "t_created", "t_enq", "reject")

    def __init__(self, pid: int, t: float):
        self.id = pid
        self.t_created = t
        self.t_enq = t
        self.reject = False


class Server:
    __slots__ = ("p", "rem", "done")

    def __init__(self):
        self.p: Product | None = None
        self.rem = 0.0
        self.done = False  # traitement fini, en attente de place en aval


class Sim:
    def __init__(self, config: dict | None = None, seed: int | None = None):
        self.cfg = deepcopy(DEFAULT_CONFIG)
        if config:
            self.cfg.update(deepcopy(config))
        self.seed = seed
        self.on_event = None  # rappel (t, kind, text) pour l'historique
        self.track_exits = False
        self.reset()

    # ------------------------------------------------------------------ état
    def reset(self):
        c = self.cfg
        self.rng = random.Random(self.seed)
        self.t = 0.0
        self.next_id = 1
        self.queues = [deque() for _ in range(N)]
        self.servers = [[Server() for _ in range(int(c["servers"][i]))] for i in range(N)]
        self.transit: list[list] = []  # [produit, poste destination, temps restant]
        self.down = [False] * N
        self.down_rem = [0.0] * N
        self.next_fail = [self._draw_fail(i) for i in range(N)]
        self.busy = [0.0] * N
        self.downtime = [0.0] * N
        self.blocked = [0.0] * N
        self.wait_sum = [0.0] * N
        self.wait_n = [0] * N
        self.lead_sum = 0.0
        self.wip_area = 0.0
        self.produced = 0
        self.rejected = 0
        self.failures_count = 0
        self.fail_n = [0] * N
        self.exit_times: deque[float] = deque()
        self.history: deque = deque(maxlen=180)
        self._next_hist = 1.0
        self.events: deque = deque(maxlen=15)
        self.exits: list = []  # [(id, "ok"|"rej")] vidé à chaque snapshot

    def apply_preset(self, preset: dict):
        for k in ("times", "servers", "adaptive"):
            self.cfg[k] = deepcopy(preset[k])
        self.cfg["times"] = [float(x) for x in self.cfg["times"]]
        self.reset()

    # -------------------------------------------------------------- réglages
    def set_time(self, i: int, value: float):
        self.cfg["times"][i] = float(value)

    def set_servers(self, i: int, n: int):
        n = max(1, min(3, int(n)))
        self.cfg["servers"][i] = n
        servers = self.servers[i]
        while len(servers) < n:
            servers.append(Server())
        while len(servers) > n:
            idle = next((s for s in servers if s.p is None), None)
            if idle is not None:
                servers.remove(idle)
                continue
            s = servers.pop()
            if i > 0:  # remet le produit en tête de file
                s.p.t_enq = self.t
                self.queues[i].appendleft(s.p)
        self._event(f"{STATIONS[i]['name']} : {n} machine(s)")

    def set_failures(self, enabled: bool):
        self.cfg["failures"] = bool(enabled)
        if enabled:
            self.next_fail = [self._draw_fail(i) for i in range(N)]

    def force_break(self, i: int):
        if not self.down[i]:
            self._break(i)

    def repair(self, i: int):
        if self.down[i]:
            self.down_rem[i] = 0.0

    # ------------------------------------------------------------ simulation
    def _draw_fail(self, i: int) -> float:
        return self.t + self.rng.expovariate(1.0 / self.cfg["mtbf"][i])

    def _break(self, i: int):
        self.down[i] = True
        self.down_rem[i] = self.cfg["mttr"][i] * self.rng.uniform(0.6, 1.4)
        self.failures_count += 1
        self.fail_n[i] += 1
        self._event(f"Panne : {STATIONS[i]['name']} ({self.down_rem[i]:.0f} s)", "panne")

    def _event(self, text: str, kind: str = "info"):
        self.events.appendleft({"t": round(self.t, 1), "text": text, "kind": kind})
        if self.on_event:
            self.on_event(round(self.t, 1), kind, text)

    def _load(self, dest: int) -> int:
        return len(self.queues[dest]) + sum(1 for tr in self.transit if tr[1] == dest)

    def _release(self, i: int, s: Server, limit: int) -> bool:
        p = s.p
        if i == N - 1:
            self.produced += 1
            self.exit_times.append(self.t)
            self.lead_sum += self.t - p.t_created
            if self.track_exits:
                self.exits.append((p.id, "ok"))
        elif p.reject:
            self.rejected += 1
            if self.track_exits:
                self.exits.append((p.id, "rej"))
        else:
            dest = i + 1
            if self._load(dest) >= limit:
                return False
            self.transit.append([p, dest, self.cfg["transfer"]])
        s.p = None
        s.done = False
        return True

    def step(self, dt: float):
        c = self.cfg
        self.t += dt
        t = self.t

        # pannes / réparations
        for i in range(N):
            if self.down[i]:
                self.downtime[i] += dt
                self.down_rem[i] -= dt
                if self.down_rem[i] <= 0:
                    self.down[i] = False
                    self.next_fail[i] = self._draw_fail(i)
                    self._event(f"{STATIONS[i]['name']} : remise en service", "ok")
            elif c["failures"] and t >= self.next_fail[i]:
                self._break(i)

        # convoyeur (ralentit devant une file chargée en mode adaptatif)
        keep = []
        for tr in self.transit:
            factor = 0.5 if c["adaptive"] and len(self.queues[tr[1]]) >= 2 else 1.0
            tr[2] -= dt * factor
            if tr[2] <= 0:
                tr[0].t_enq = t
                self.queues[tr[1]].append(tr[0])
            else:
                keep.append(tr)
        self.transit = keep

        # postes, de l'aval vers l'amont pour libérer la place d'abord
        limit = c["wip_limit"] if c["adaptive"] else c["buffer"]
        for i in reversed(range(N)):
            for s in self.servers[i]:
                if s.done and not self._release(i, s, limit):
                    self.blocked[i] += dt
                    continue
                if s.p is None and not self.down[i]:
                    if i == 0:
                        s.p = Product(self.next_id, t)
                        self.next_id += 1
                    elif self.queues[i]:
                        p = self.queues[i].popleft()
                        self.wait_sum[i] += t - p.t_enq
                        self.wait_n[i] += 1
                        s.p = p
                    else:
                        continue
                    s.rem = c["times"][i]
                if s.p is not None and not s.done and not self.down[i]:
                    s.rem -= dt
                    self.busy[i] += dt
                    if s.rem <= 1e-9:
                        s.done = True
                        if i == CONTROLE:
                            s.p.reject = self.rng.random() < c["reject_rate"]
                        self._release(i, s, limit)

        self.wip_area += self.wip() * dt
        while self.exit_times and self.exit_times[0] < t - 60:
            self.exit_times.popleft()
        if t >= self._next_hist:
            self.history.append([round(t), round(self.throughput(), 2)])
            self._next_hist += 1.0

    # ------------------------------------------------------------------ KPI
    def wip(self) -> int:
        return (sum(len(q) for q in self.queues) + len(self.transit)
                + sum(1 for st in self.servers for s in st if s.p is not None))

    def throughput(self) -> float:
        """Produits finis par minute sur la dernière minute glissante."""
        window = min(60.0, self.t)
        return len(self.exit_times) / window * 60 if window >= 1 else 0.0

    def station_state(self, i: int) -> str:
        if self.down[i]:
            return "panne"
        servers = self.servers[i]
        if any(s.p is not None and not s.done for s in servers):
            return "marche"
        if any(s.done for s in servers):
            return "bloque"
        return "attente"

    def kpis(self) -> dict:
        c = self.cfg
        t = max(self.t, 1e-9)
        eff = [c["times"][i] / len(self.servers[i]) for i in range(N)]
        util = [self.busy[i] / (t * len(self.servers[i])) for i in range(N)]
        blocked = [self.blocked[i] / (t * len(self.servers[i])) for i in range(N)]
        theo_bn = max(range(N), key=lambda i: eff[i])
        # goulot observé : poste le plus occupé sans être bloqué par l'aval
        obs_bn = max(range(N), key=lambda i: util[i] - blocked[i]) if self.t > 240 else theo_bn
        theoretical = 60.0 / max(eff)
        finished = self.produced + self.rejected
        return {
            "t": round(self.t, 1),
            "produced": self.produced,
            "rejected": self.rejected,
            "reject_pct": round(100 * self.rejected / finished, 1) if finished else 0.0,
            "throughput_now": round(self.throughput(), 2),
            "throughput_avg": round(self.produced / t * 60, 2),
            "theoretical": round(theoretical, 2),
            "theoretical_hour": round(theoretical * 60),
            "wip": self.wip(),
            "avg_wip": round(self.wip_area / t, 2),
            "lead_time": round(self.lead_sum / self.produced, 1) if self.produced else 0.0,
            "wait_controle": round(self.wait_sum[CONTROLE] / self.wait_n[CONTROLE], 1) if self.wait_n[CONTROLE] else 0.0,
            "bottleneck_theoretical": STATIONS[theo_bn]["name"],
            "bottleneck_observed": STATIONS[obs_bn]["name"],
            "downtime_total": round(sum(self.downtime), 1),
            "failures": self.failures_count,
        }

    def stations(self) -> list[dict]:
        t = max(self.t, 1e-9)
        out = []
        for i in range(N):
            servers = self.servers[i]
            out.append({
                **STATIONS[i],
                "state": self.station_state(i),
                "time": self.cfg["times"][i],
                "queue": len(self.queues[i]),
                "util": round(self.busy[i] / (t * len(servers)), 3),
                "avail": round(1 - self.downtime[i] / t, 3),
                "blocked": round(self.blocked[i] / (t * len(servers)), 3),
                "wait": round(self.wait_sum[i] / self.wait_n[i], 1) if self.wait_n[i] else 0.0,
                "downtime": round(self.downtime[i], 1),
                "repair_in": round(self.down_rem[i], 1) if self.down[i] else 0,
                "failures": self.fail_n[i],
                # MTBF / MTTR observés (temps de bon fonctionnement / nombre de pannes)
                "mtbf_obs": round((self.t - self.downtime[i]) / self.fail_n[i], 0) if self.fail_n[i] else None,
                "mttr_obs": round(self.downtime[i] / self.fail_n[i], 1) if self.fail_n[i] else None,
                "servers": [{
                    "busy": s.p is not None,
                    "done": s.done,
                    "progress": round(1 - s.rem / self.cfg["times"][i], 3) if s.p is not None else 0,
                } for s in servers],
            })
        return out

    def products(self) -> list[dict]:
        out = []
        for i in range(N):
            for k, p in enumerate(self.queues[i]):
                out.append({"id": p.id, "st": i, "s": "q", "k": k})
            for j, s in enumerate(self.servers[i]):
                if s.p is not None:
                    prog = 1.0 if s.done else 1 - s.rem / self.cfg["times"][i]
                    out.append({"id": s.p.id, "st": i, "s": "p", "k": j,
                                "p": round(max(0.0, min(1.0, prog)), 3), "rej": s.p.reject})
        for p, dest, rem in self.transit:
            out.append({"id": p.id, "st": dest, "s": "t",
                        "p": round(1 - rem / self.cfg["transfer"], 3)})
        return out

    def snapshot(self) -> dict:
        exits, self.exits = self.exits, []
        return {
            "config": {k: self.cfg[k] for k in ("times", "servers", "adaptive", "failures", "buffer", "wip_limit")},
            "stations": self.stations(),
            "products": self.products(),
            "kpi": self.kpis(),
            "history": list(self.history),
            "events": list(self.events),
            "exits": exits,
        }


# ------------------------------------------------------------ comparaison
def run_headless(config: dict, duration: float = 3600.0, seed: int = 7, dt: float = 0.1) -> dict:
    sim = Sim(config, seed=seed)
    for _ in range(int(duration / dt)):
        sim.step(dt)
    k = sim.kpis()
    k["util"] = [round(s["util"], 3) for s in sim.stations()]
    return k


def compare_presets(failures: bool, current: dict | None = None, duration: float = 3600.0) -> list[dict]:
    """Simule chaque scénario sur la même durée et la même graine aléatoire."""
    rows = []
    scenarios = [(key, p["label"], p) for key, p in PRESETS.items()]
    if current is not None:
        scenarios.append(("actuel", "Réglages actuels", current))
    for key, label, p in scenarios:
        cfg = {"times": [float(x) for x in p["times"]], "servers": list(p["servers"]),
               "adaptive": bool(p["adaptive"]), "failures": failures}
        rows.append({"key": key, "label": label, "config": cfg, **run_headless(cfg, duration)})
    return rows
