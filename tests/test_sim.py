"""Tests de cohérence du modèle (Optimisation & Validation).

Lancer :  python -m pytest -q
"""
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "backend"))

from sim import N, PRESETS, Sim, compare_presets, run_headless  # noqa: E402
from storage import Storage  # noqa: E402

DT = 0.1


def run(cfg: dict, duration: float, seed: int = 1) -> Sim:
    sim = Sim(cfg, seed=seed)
    for _ in range(int(duration / DT)):
        sim.step(DT)
    return sim


def steady_rate(cfg: dict, warmup: float = 600, window: float = 3600) -> float:
    """Débit (produits/min) mesuré après la mise en régime."""
    sim = run(cfg, warmup)
    start = sim.produced + sim.rejected
    for _ in range(int(window / DT)):
        sim.step(DT)
    return (sim.produced + sim.rejected - start) / window * 60


NO_FAIL = {"failures": False, "reject_rate": 0.0}


# ------------------------------------------------------------ débit théorique
@pytest.mark.parametrize("preset, expected", [
    ("initial", 4.0),           # 60 / 15 s
    ("controle_auto", 7.5),     # 60 / 8 s, le traitement devient limitant
    ("double_controle", 7.5),   # 2 × 15 s en parallèle = 7,5 s effectifs
])
def test_debit_regime_egal_au_debit_theorique(preset, expected):
    cfg = {**PRESETS[preset], **NO_FAIL}
    assert steady_rate(cfg) == pytest.approx(expected, rel=0.05)


def test_debit_theorique_calcule():
    sim = Sim(PRESETS["initial"])
    assert sim.kpis()["theoretical"] == pytest.approx(4.0)
    sim = Sim(PRESETS["controle_auto"])
    assert sim.kpis()["theoretical"] == pytest.approx(7.5)


# --------------------------------------------------------------- invariants
@pytest.mark.parametrize("preset", list(PRESETS))
def test_conservation_des_produits(preset):
    """Tout produit créé est soit fini, soit rejeté, soit encore dans la ligne."""
    sim = run(PRESETS[preset], 1800)
    created = sim.next_id - 1
    assert created == sim.produced + sim.rejected + sim.wip()


@pytest.mark.parametrize("preset", list(PRESETS))
def test_files_ne_depassent_pas_la_capacite(preset):
    sim = Sim(PRESETS[preset], seed=3)
    limit = sim.cfg["wip_limit"] if sim.cfg["adaptive"] else sim.cfg["buffer"]
    for _ in range(int(1800 / DT)):
        sim.step(DT)
        for i in range(1, N):
            assert sim._load(i) <= limit


def test_utilisation_et_disponibilite_bornees():
    sim = run(PRESETS["initial"], 1800)
    for s in sim.stations():
        assert 0 <= s["util"] <= 1
        assert 0 <= s["avail"] <= 1


# -------------------------------------------------------------------- pannes
def test_pannes_reduisent_disponibilite_et_debit():
    with_fail = run_headless({**PRESETS["initial"], "failures": True}, 3600, seed=5)
    without = run_headless({**PRESETS["initial"], "failures": False}, 3600, seed=5)
    assert with_fail["failures"] > 0
    assert with_fail["downtime_total"] > 0
    assert without["downtime_total"] == 0
    assert with_fail["produced"] < without["produced"]


def test_panne_provoquee_arrete_le_poste():
    sim = run({**PRESETS["initial"], "failures": False}, 120)
    sim.force_break(2)
    assert sim.station_state(2) == "panne"
    busy_before = sim.busy[2]
    for _ in range(50):
        sim.step(DT)
    assert sim.busy[2] == busy_before  # aucun traitement pendant la panne
    sim.repair(2)
    sim.step(DT)
    assert sim.station_state(2) != "panne"


def test_rejets_proches_du_taux_parametre():
    k = run_headless({**PRESETS["controle_auto"], "failures": False}, 4 * 3600, seed=11)
    assert k["reject_pct"] == pytest.approx(5.0, abs=1.5)


# -------------------------------------------------------------- optimisation
def test_goulot_identifie_et_deplace():
    before = run_headless({**PRESETS["initial"], "failures": False}, 3600)
    after = run_headless({**PRESETS["controle_auto"], "failures": False}, 3600)
    assert before["bottleneck_observed"] == "Contrôle qualité"
    assert after["bottleneck_observed"] == "Traitement"
    assert after["produced"] > 1.6 * before["produced"]


def test_convoyeur_adaptatif_reduit_en_cours_sans_perte_de_debit():
    base = run_headless({**PRESETS["initial"], "failures": False}, 3600)
    adapt = run_headless({**PRESETS["convoyeur_adaptatif"], "failures": False}, 3600)
    assert adapt["avg_wip"] < base["avg_wip"]
    assert adapt["produced"] >= 0.97 * base["produced"]


def test_comparaison_reproductible():
    a = compare_presets(True, duration=1200)
    b = compare_presets(True, duration=1200)
    assert [r["produced"] for r in a] == [r["produced"] for r in b]
    assert [r["key"] for r in a] == list(PRESETS)


def test_changement_de_machines_en_cours_de_route():
    sim = run(PRESETS["initial"], 600)
    created_before = sim.next_id - 1
    sim.set_servers(2, 2)
    sim.set_servers(2, 1)
    assert len(sim.servers[2]) == 1
    assert sim.next_id - 1 == created_before
    assert sim.next_id - 1 == sim.produced + sim.rejected + sim.wip()


# ------------------------------------------------------------------ stockage
def test_stockage_historique_et_export_csv():
    st = Storage(":memory:")
    sim = Sim(PRESETS["initial"], seed=2)
    run_id = st.start_run("test", {"times": sim.cfg["times"]})
    sim.on_event = lambda t, kind, text: st.add_event(run_id, t, kind, text)
    for minute in range(1, 6):
        for _ in range(int(60 / DT)):
            sim.step(DT)
        st.add_sample(run_id, sim.kpis(), sim.stations())
    sim.force_break(1)
    h = st.history(run_id)
    assert len(h["samples"]) == 5
    assert h["events"][-1]["kind"] == "panne"
    assert st.runs()[0]["samples"] == 5
    csv = st.export_csv(run_id).splitlines()
    assert csv[0].startswith("run_id;t;throughput")
    assert len(csv) == 6
