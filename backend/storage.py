"""Stockage de l'historique de production (SQLite, bibliothèque standard).

Trois tables :
- runs    : une ligne par exécution (démarrage, scénario appliqué, remise à zéro) ;
- samples : un relevé des KPI par minute simulée ;
- events  : pannes, remises en service, changements de réglages.
"""
from __future__ import annotations

import csv
import io
import json
import sqlite3
import threading
from datetime import datetime
from pathlib import Path

SCHEMA = """
CREATE TABLE IF NOT EXISTS runs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    started_at TEXT NOT NULL,
    label TEXT NOT NULL,
    config TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS samples (
    run_id INTEGER NOT NULL REFERENCES runs(id),
    t REAL NOT NULL,
    throughput REAL, throughput_avg REAL,
    produced INTEGER, rejected INTEGER, wip INTEGER,
    wait_controle REAL, bottleneck TEXT,
    util TEXT, avail TEXT, queues TEXT
);
CREATE TABLE IF NOT EXISTS events (
    run_id INTEGER NOT NULL REFERENCES runs(id),
    t REAL NOT NULL,
    kind TEXT NOT NULL,
    text TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_samples_run ON samples(run_id, t);
CREATE INDEX IF NOT EXISTS idx_events_run ON events(run_id, t);
"""

CSV_COLUMNS = ["run_id", "t", "throughput", "throughput_avg", "produced", "rejected", "wip",
               "wait_controle", "bottleneck", "util", "avail", "queues"]


class Storage:
    def __init__(self, path: str | Path = ":memory:"):
        if str(path) != ":memory:":
            Path(path).parent.mkdir(parents=True, exist_ok=True)
        self.db = sqlite3.connect(str(path), check_same_thread=False)
        self.db.row_factory = sqlite3.Row
        self.db.executescript(SCHEMA)
        self.lock = threading.Lock()

    def _exec(self, sql: str, args: tuple = ()) -> sqlite3.Cursor:
        with self.lock:
            cur = self.db.execute(sql, args)
            self.db.commit()
            return cur

    # ------------------------------------------------------------ écriture
    def start_run(self, label: str, config: dict) -> int:
        cur = self._exec("INSERT INTO runs (started_at, label, config) VALUES (?, ?, ?)",
                         (datetime.now().isoformat(timespec="seconds"), label, json.dumps(config)))
        return cur.lastrowid

    def add_sample(self, run_id: int, kpi: dict, stations: list[dict]):
        self._exec(
            "INSERT INTO samples VALUES (?,?,?,?,?,?,?,?,?,?,?,?)",
            (run_id, kpi["t"], kpi["throughput_now"], kpi["throughput_avg"], kpi["produced"],
             kpi["rejected"], kpi["wip"], kpi["wait_controle"], kpi["bottleneck_observed"],
             json.dumps([s["util"] for s in stations]),
             json.dumps([s["avail"] for s in stations]),
             json.dumps([s["queue"] for s in stations])))

    def add_event(self, run_id: int, t: float, kind: str, text: str):
        self._exec("INSERT INTO events VALUES (?,?,?,?)", (run_id, t, kind, text))

    # ------------------------------------------------------------- lecture
    def runs(self) -> list[dict]:
        rows = self._exec("""
            SELECT r.id, r.started_at, r.label, r.config,
                   COUNT(s.t) AS samples, MAX(s.t) AS duration, MAX(s.produced) AS produced
            FROM runs r LEFT JOIN samples s ON s.run_id = r.id
            GROUP BY r.id ORDER BY r.id DESC""").fetchall()
        return [{**dict(r), "config": json.loads(r["config"])} for r in rows]

    def history(self, run_id: int) -> dict:
        samples = self._exec("SELECT * FROM samples WHERE run_id = ? ORDER BY t", (run_id,)).fetchall()
        events = self._exec("SELECT t, kind, text FROM events WHERE run_id = ? ORDER BY t", (run_id,)).fetchall()
        out = []
        for s in samples:
            d = dict(s)
            for k in ("util", "avail", "queues"):
                d[k] = json.loads(d[k])
            out.append(d)
        return {"run_id": run_id, "samples": out, "events": [dict(e) for e in events]}

    def export_csv(self, run_id: int | None = None) -> str:
        sql = "SELECT * FROM samples" + (" WHERE run_id = ?" if run_id else "") + " ORDER BY run_id, t"
        rows = self._exec(sql, (run_id,) if run_id else ()).fetchall()
        buf = io.StringIO()
        w = csv.writer(buf, delimiter=";")
        w.writerow(CSV_COLUMNS)
        for r in rows:
            w.writerow([r[c] for c in CSV_COLUMNS])
        return buf.getvalue()
