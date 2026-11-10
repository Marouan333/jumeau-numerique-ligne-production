"""Transforme les mesures du banc d'essai Unity en tableaux Markdown.

Entrée  : docs/rapport/data/resultats_unity.json (écrit par Twin > Lancer le banc d'essai)
Référence : docs/rapport/data/resultats.json (simulation hors ligne, pas de 0,1 s, version Three.js d'origine)
Sortie  : docs/resultats-unity.md

Usage : python tools/resultats_vers_markdown.py
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "rapport" / "data" / "resultats_unity.json"
REF = ROOT / "docs" / "rapport" / "data" / "resultats.json"
OUT = ROOT / "docs" / "resultats-unity.md"


def f(v, d=0):
    return f"{v:,.{d}f}".replace(",", " ").replace(".", ",")


def gain(v, base):
    if not base:
        return ""
    g = round((v / base - 1) * 100)
    return f" ({'+' if g > 0 else ''}{g} %)" if g else ""


def table(rows, ref_rows):
    base = rows[0]
    out = ["| Indicateur | " + " | ".join(r["label"] for r in rows) + " |", "|---|" + "---|" * len(rows)]
    lines = [
        ("Produits finis (1 h)", lambda r: r["produced"], 0, True),
        ("Cadence moyenne (produits/min)", lambda r: r["throughput_avg"], 2, True),
        ("Rejets", lambda r: r["rejected"], 0, False),
        ("Attente au contrôle (s)", lambda r: r["wait_controle"], 0, False),
        ("En-cours moyen", lambda r: r["avg_wip"], 1, False),
        ("Séjour moyen (s)", lambda r: r["lead_time"], 0, False),
        ("Pannes", lambda r: r["failures"], 0, False),
        ("Arrêts cumulés (s)", lambda r: r["downtime_total"], 0, False),
    ]
    for name, get, d, with_gain in lines:
        cells = [f(get(r), d) + (gain(get(r), get(base)) if with_gain and r is not base else "") for r in rows]
        out.append(f"| {name} | " + " | ".join(cells) + " |")
    out.append("| Goulot observé | " + " | ".join(r["bottleneck_observed"] for r in rows) + " |")
    out.append("| Goulot théorique | " + " | ".join(r["bottleneck_theoretical"] for r in rows) + " |")
    out.append("| Cadence théorique max (/min) | " + " | ".join(f(r["theoretical"], 1) for r in rows) + " |")
    out.append("| Utilisation du contrôle | " + " | ".join(f"{round(r['util'][2] * 100)} %" for r in rows) + " |")
    out.append("")
    if ref_rows:
        out += ["Écart avec la simulation hors ligne de référence (produits finis sur 1 h) :", "",
                "| Scénario | Mesuré avec Unity | Référence hors ligne | Écart |", "|---|---|---|---|"]
        for r in rows:
            ref = next((x for x in ref_rows if x["key"] == r["scenario"]), None)
            if ref:
                ecart = round((r["produced"] / ref["produced"] - 1) * 100, 1)
                out.append(f"| {r['label']} | {r['produced']} | {ref['produced']} | {f(ecart, 1)} % |")
        out.append("")
    return out


def main():
    data = json.loads(SRC.read_text(encoding="utf-8"))
    ref = json.loads(REF.read_text(encoding="utf-8")) if REF.exists() else {}
    rows = data["rows"]
    md = [
        "# Résultats des scénarios d'optimisation, mesurés avec la scène Unity",
        "",
        f"Mesures du {data['date']}, avec la scène Unity connectée au backend (flux WebSocket réel).",
        f"Chaque scénario est joué {int(data['duration_s'])} s simulées (1 h) à la vitesse ×{int(data['speed'])}, "
        "avec le backend lancé avec `SIM_SEED=7` pour que les pannes et les rejets soient reproductibles.",
        "Ces valeurs sont lues sur le flux WebSocket réel, celui qui anime la scène 3D, et non sur une simulation séparée.",
        "",
    ]
    for enabled, title, key in [(True, "Avec pannes aléatoires", "avec_pannes"), (False, "Sans panne", "sans_pannes")]:
        group = [r for r in rows if r["failures_enabled"] == enabled]
        if not group:
            continue
        md += [f"## {title}", ""] + table(group, ref.get(key))
    # L'analyse ci-dessous est rédigée d'après les mesures du 2026-11-10 ; la relire si elles changent.
    md += [
        "## Lecture",
        "",
        "- **Goulot initial.** Le contrôle qualité (15 s) est le goulot : il est utilisé à 96 % avec pannes et à 100 % sans panne, "
        "sa file reste pleine (attente moyenne d'environ 114 s devant lui) et la cadence reste sous le maximum théorique de 4 produits/min "
        "(3,63 avec pannes, 3,77 sans panne).",
        "- **Traiter le goulot.** Passer le contrôle à 8 s ou le doubler donne presque le même résultat : environ 393 produits/h avec pannes "
        "(contre 218, soit +80 %) et environ 422 sans panne (contre 226, soit +87 %). Le goulot passe au traitement (8 s), "
        "et l'attente devant le contrôle tombe à moins de 15 s.",
        "- **Convoyeur adaptatif.** La production ne change pas (218 avec pannes, 226 sans), car le goulot reste le contrôle. "
        "En revanche l'en-cours moyen est divisé par deux (environ 9,4 contre 19) et l'attente devant le contrôle passe de 114 à 40 s.",
        "- **Effet des pannes.** Elles retirent environ 3 % de production à la situation initiale et environ 7 % aux scénarios améliorés "
        "(393 contre 422), car le traitement devient le poste limitant et chaque arrêt y coûte du débit.",
        "- **Cohérence avec la simulation hors ligne.** Les écarts de production sont inférieurs à 1,5 % ; la situation initiale et le "
        "convoyeur adaptatif donnent exactement la même production, et les séries sans panne sont identiques à la référence. Les petits écarts viennent probablement du pas de simulation "
        "(0,05 s en ligne, 0,1 s hors ligne).",
        "",
        "## Limites",
        "",
        "- Une seule graine aléatoire (7) : les valeurs avec pannes dépendent de ce tirage. Pour une étude statistique, répéter avec plusieurs graines.",
        "- Les deux scénarios améliorés sont presque équivalents dans ce modèle : le choix entre automatiser le contrôle et le doubler relève d'un coût que le modèle ne contient pas.",
        "",
    ]
    OUT.write_text("\n".join(md), encoding="utf-8")
    print(f"Écrit : {OUT}")


if __name__ == "__main__":
    main()
