"""Backend du jumeau numérique : simulation + diffusion WebSocket.

Lancer :  python backend/server.py   (puis ouvrir http://localhost:8000)
"""
from __future__ import annotations

import asyncio
import json
import os
from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.staticfiles import StaticFiles

from sim import N, PRESETS, STATIONS, Sim, compare_presets

FRONTEND = Path(__file__).resolve().parent.parent / "frontend"
TICK = 0.1      # période d'envoi au navigateur (s réelles)
SUBSTEP = 0.05  # pas de simulation (s simulées)


class Hub:
    def __init__(self):
        self.sim = Sim()
        self.sim.track_exits = True
        self.speed = 4.0
        self.paused = False
        self.preset = "initial"
        self.clients: set[WebSocket] = set()


hub = Hub()


async def sim_loop():
    while True:
        if not hub.paused:
            for _ in range(max(1, round(hub.speed * TICK / SUBSTEP))):
                hub.sim.step(SUBSTEP)
        msg = json.dumps({"type": "state", "speed": hub.speed, "paused": hub.paused,
                          "preset": hub.preset, **hub.sim.snapshot()})
        for ws in list(hub.clients):
            try:
                await ws.send_text(msg)
            except Exception:
                hub.clients.discard(ws)
        await asyncio.sleep(TICK)


@asynccontextmanager
async def lifespan(_app: FastAPI):
    task = asyncio.create_task(sim_loop())
    yield
    task.cancel()


app = FastAPI(title="Jumeau numérique – ligne de production", lifespan=lifespan)


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
            sim._event(f"Scénario : {PRESETS[name]['label']}")
    elif cmd == "set_time":
        i = _station(m)
        sim.set_time(i, max(1.0, min(60.0, float(m["value"]))))
        hub.preset = "custom"
    elif cmd == "set_servers":
        sim.set_servers(_station(m), int(m["value"]))
        hub.preset = "custom"
    elif cmd == "failures":
        sim.set_failures(bool(m["value"]))
    elif cmd == "adaptive":
        sim.cfg["adaptive"] = bool(m["value"])
        hub.preset = "custom"
    elif cmd == "speed":
        hub.speed = max(0.25, min(20.0, float(m["value"])))
    elif cmd == "pause":
        hub.paused = bool(m["value"])
    elif cmd == "reset":
        sim.reset()
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


@app.get("/api/state")
def api_state():
    return {"speed": hub.speed, "paused": hub.paused, "preset": hub.preset, **hub.sim.snapshot()}


app.mount("/", StaticFiles(directory=FRONTEND, html=True), name="frontend")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=int(os.environ.get("PORT", 8000)))
