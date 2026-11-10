"""Backend du jumeau numérique : simulation + diffusion WebSocket + historique SQLite.

Lancer :  python backend/server.py   (puis ouvrir http://localhost:8000)
"""
from __future__ import annotations

import asyncio
import json
import os
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Optional

from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.responses import Response
from fastapi.staticfiles import StaticFiles

from sim import N, PRESETS, STATIONS, Sim, compare_presets
from storage import Storage

ROOT = Path(__file__).resolve().parent.parent
FRONTEND = ROOT / "frontend"
DB_PATH = os.environ.get("DB_PATH", str(ROOT / "data" / "jumeau.db"))
TICK = 0.1           # période d'envoi au navigateur (s réelles)
SUBSTEP = 0.05       # pas de simulation (s simulées)
SAMPLE_EVERY = 60.0  # relevé des KPI dans l'historique (s simulées)
# Graine aléatoire facultative (SIM_SEED=7) : rend les pannes et les rejets reproductibles,
# par exemple pour mesurer les scénarios avec la scène Unity (voir docs/resultats-unity.md).
SEED = int(os.environ["SIM_SEED"]) if os.environ.get("SIM_SEED") else None


class Hub:
    def __init__(self, storage: Storage):
        self.storage = storage
        self.sim = Sim(seed=SEED)
        self.sim.track_exits = True
        self.sim.on_event = self._log_event
        self.speed = 4.0
        self.paused = False
        self.preset = "initial"
        self.clients: set[WebSocket] = set()
        self.new_run(PRESETS["initial"]["label"])

    def _log_event(self, t: float, kind: str, text: str):
        self.storage.add_event(self.run_id, t, kind, text)

    def new_run(self, label: str):
        cfg = {k: self.sim.cfg[k] for k in ("times", "servers", "adaptive", "failures")}
        self.run_id = self.storage.start_run(label, cfg)
        self.next_sample = SAMPLE_EVERY

    def step(self):
        for _ in range(max(1, round(self.speed * TICK / SUBSTEP))):
            self.sim.step(SUBSTEP)
            if self.sim.t >= self.next_sample:
                self.storage.add_sample(self.run_id, self.sim.kpis(), self.sim.stations())
                self.next_sample += SAMPLE_EVERY


hub: Hub | None = None


async def sim_loop():
    while True:
        if not hub.paused:
            hub.step()
        msg = json.dumps({"type": "state", "speed": hub.speed, "paused": hub.paused,
                          "preset": hub.preset, "run_id": hub.run_id, **hub.sim.snapshot()})
        for ws in list(hub.clients):
            try:
                await ws.send_text(msg)
            except Exception:
                hub.clients.discard(ws)
        await asyncio.sleep(TICK)


@asynccontextmanager
async def lifespan(_app: FastAPI):
    global hub
    hub = Hub(Storage(DB_PATH))
    task = asyncio.create_task(sim_loop())
    yield
    task.cancel()


app = FastAPI(title="Jumeau numérique – ligne de production", lifespan=lifespan)


@app.middleware("http")
async def unity_webgl_headers(request, call_next):
    """Les fichiers .unityweb du build WebGL sont gzippés : on l'annonce pour que le navigateur les
    décompresse lui-même (démarrage plus rapide) et on donne le bon type MIME."""
    response = await call_next(request)
    path = request.url.path
    if path.endswith(".unityweb"):
        response.headers["Content-Encoding"] = "gzip"
        if ".wasm." in path:
            response.headers["Content-Type"] = "application/wasm"
        elif ".js." in path:
            response.headers["Content-Type"] = "application/javascript"
        else:
            response.headers["Content-Type"] = "application/octet-stream"
    return response


def _station(m: dict) -> int:
    i = int(m.get("station", 0))
    if not 0 <= i < N:
        raise ValueError("poste inconnu")
    return i


async def handle(ws: WebSocket, m: dict):
    sim, cmd = hub.sim, m.get("cmd")
    if cmd == "preset":
        name = m.get("name")
        if name in PRESETS:
            sim.apply_preset(PRESETS[name])
            hub.preset = name
            hub.new_run(PRESETS[name]["label"])
            sim._event(f"Scénario : {PRESETS[name]['label']}")
    elif cmd == "set_time":
        i = _station(m)
        sim.set_time(i, max(1.0, min(60.0, float(m["value"]))))
        hub.preset = "custom"
        sim._event(f"Réglage : {STATIONS[i]['name']} = {sim.cfg['times'][i]:g} s")
    elif cmd == "set_servers":
        sim.set_servers(_station(m), int(m["value"]))
        hub.preset = "custom"
    elif cmd == "failures":
        sim.set_failures(bool(m["value"]))
        sim._event("Pannes aléatoires " + ("activées" if m["value"] else "désactivées"))
    elif cmd == "adaptive":
        sim.cfg["adaptive"] = bool(m["value"])
        hub.preset = "custom"
        sim._event("Convoyeur adaptatif " + ("activé" if m["value"] else "désactivé"))
    elif cmd == "speed":
        hub.speed = max(0.25, min(20.0, float(m["value"])))
    elif cmd == "pause":
        hub.paused = bool(m["value"])
    elif cmd == "reset":
        sim.reset()
        hub.new_run("Remise à zéro" if hub.preset == "custom" else PRESETS[hub.preset]["label"])
    elif cmd == "break":
        sim.force_break(_station(m))
    elif cmd == "repair":
        sim.repair(_station(m))
    elif cmd == "compare":
        duration = max(600.0, min(8 * 3600.0, float(m.get("duration", 3600))))
        await ws.send_text(json.dumps({"type": "compare", "status": "running"}))
        rows = await asyncio.to_thread(compare_presets, sim.cfg["failures"], dict(sim.cfg), duration)
        await ws.send_text(json.dumps({"type": "compare", "status": "done", "duration": duration, "results": rows}))


@app.websocket("/ws")
async def ws_endpoint(ws: WebSocket):
    await ws.accept()
    hub.clients.add(ws)
    await ws.send_text(json.dumps({
        "type": "hello", "stations": STATIONS,
        "presets": {k: v["label"] for k, v in PRESETS.items()},
    }))
    try:
        while True:
            try:
                await handle(ws, json.loads(await ws.receive_text()))
            except (ValueError, KeyError, TypeError) as exc:
                await ws.send_text(json.dumps({"type": "error", "message": str(exc)}))
    except WebSocketDisconnect:
        pass
    finally:
        hub.clients.discard(ws)


# ------------------------------------------------------------------ REST
@app.get("/api/state")
def api_state():
    snap = hub.sim.snapshot()
    snap.pop("exits")
    return {"speed": hub.speed, "paused": hub.paused, "preset": hub.preset, "run_id": hub.run_id, **snap}


@app.get("/api/runs")
def api_runs():
    return hub.storage.runs()


@app.get("/api/history")
def api_history(run: int):
    data = hub.storage.history(run)
    if not data["samples"] and not data["events"] and run != hub.run_id:
        raise HTTPException(404, "exécution inconnue")
    return data


@app.get("/api/export.csv")
def api_export(run: Optional[int] = None):
    name = f"historique_run{run}.csv" if run else "historique_complet.csv"
    return Response("﻿" + hub.storage.export_csv(run), media_type="text/csv; charset=utf-8",
                    headers={"Content-Disposition": f'attachment; filename="{name}"'})


app.mount("/", StaticFiles(directory=FRONTEND, html=True), name="frontend")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=int(os.environ.get("PORT", 8000)))
