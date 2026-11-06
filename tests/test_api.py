"""Tests de l'interface backend (REST + WebSocket)."""
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "backend"))
os.environ["DB_PATH"] = ":memory:"

from fastapi.testclient import TestClient  # noqa: E402

import server  # noqa: E402

server.DB_PATH = ":memory:"


def test_websocket_etat_et_commandes():
    with TestClient(server.app) as client:
        with client.websocket_connect("/ws") as ws:
            hello = ws.receive_json()
            assert hello["type"] == "hello"
            assert len(hello["stations"]) == 4
            state = ws.receive_json()
            assert state["type"] == "state"
            assert {"stations", "products", "kpi", "config"} <= state.keys()

            ws.send_json({"cmd": "preset", "name": "double_controle"})
            for _ in range(20):
                msg = ws.receive_json()
                if msg["type"] == "state" and msg["config"]["servers"][2] == 2:
                    break
            assert msg["config"]["servers"][2] == 2
            assert msg["preset"] == "double_controle"

            ws.send_json({"cmd": "set_time", "station": 9, "value": 3})
            for _ in range(20):
                msg = ws.receive_json()
                if msg["type"] == "error":
                    break
            assert msg["type"] == "error"


def test_api_historique_et_export():
    with TestClient(server.app) as client:
        runs = client.get("/api/runs").json()
        assert runs and runs[0]["label"]
        h = client.get(f"/api/history?run={runs[0]['id']}").json()
        assert "samples" in h and "events" in h
        r = client.get("/api/export.csv")
        assert r.status_code == 200
        assert r.headers["content-type"].startswith("text/csv")
        assert client.get("/api/history?run=99999").status_code == 404
        assert client.get("/").status_code == 200
