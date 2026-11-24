#!/usr/bin/env python3
"""Build the shipped Combat Lab difficulty DB (combat/difficulty-db.json) from fought calibration fights plus the model.

One row per biome / creature / stars (0-2), in the same format the game writes (CombatDifficultyDb.cs), plus:
  source          "fought" when a mid player (the tier's q2 sword build) fought it, else "model"
  builds          every calibrated build of the tier at q2: win rate, kill time, and where the number comes from
  best / worst    the builds with the highest and lowest win rate
  playersNeeded   from a fought team climb when there is one (big creatures), else estimated: Valheim gives a creature
                  +30% health and +4% damage per extra player nearby, so a team of N moves the solo win curve by
                  k_team * ln(N^2 / (health scale * damage scale)); k_team is fitted on the fought team climbs (real
                  teams get far less than ideal teamwork). Marked "playersNeededSource".
Fought numbers always win over model numbers. Rows the game writes later (a user's own rebuild) replace these.

Usage:
  combat_build_db.py --gamedata gamedata.json --predictions predictions.json --cal cal-*.json [--teams difficulty-db.json]
                     --out Assets/Presets/templates/combat/difficulty-db.json
"""
import argparse, datetime, json, math

HEALTH_PER_PLAYER, DAMAGE_PER_PLAYER = 0.3, 0.04      # Game.m_healthScalePerPlayer / m_damageScalePerPlayer (1.0 defaults)
NEED_WIN = 0.5
MAX_TEAM = 4


def logit(p): p = min(0.995, max(0.005, p)); return math.log(p / (1 - p))
def sig(z): return 1 / (1 + math.exp(-z))


def team_win(p1, n, k):
    """Estimated win chance of n mid players together, from one player's win chance."""
    hs = 1 + HEALTH_PER_PLAYER * (n - 1); ds = 1 + DAMAGE_PER_PLAYER * (n - 1)
    return sig(logit(p1) + k * math.log(n * n / (hs * ds)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gamedata", required=True)
    ap.add_argument("--predictions", required=True)
    ap.add_argument("--cal", action="append", required=True)
    ap.add_argument("--teams", default="", help="a difficulty-db.json written by the game (fought team climbs)")
    ap.add_argument("--reference", default="sword", help="the build that stands for 'a mid player' (the team climbs use it)")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()

    creatures = json.load(open(a.gamedata))["creatures"]
    pr = json.load(open(a.predictions)); k = pr["k"]

    # fought cells: (tier, build, foe, stars) -> numbers; plain build rows only (no bot-skill or team variants)
    fought = {}
    for path in a.cal:
        for c in json.load(open(path)).get("cells", []):
            lo = c["loadout"]
            if " (" in lo or c.get("runs", 0) == 0: continue
            decided = c.get("wins", 0) + c.get("losses", c["runs"] - c.get("wins", 0) - c.get("timeouts", 0))
            if decided * 2 < c["runs"]: continue                 # mostly stalled/timed out: no answer, the model fills it
            parts = lo.replace("cal-", "").split(" ") if " " in lo else lo.replace("cal-", "").split("-", 1)
            if len(parts) != 2: continue
            tier, build = parts
            foe, *rest = c["foe"].split(" ")
            stars = int(rest[0].rstrip("*")) if rest and rest[0].rstrip("*").isdigit() else 0
            fought[(tier, build, foe, stars)] = {"win": c["wins"] / decided, "runs": decided, "wins": c["wins"],
                                                  "ttk": c.get("ttkMean", 0), "damagePct": c.get("damageTakenPct", 0)}

    # model at the calibrated weapon level (q2), own biome only
    model = {}
    for p in pr["predictions"]:
        if p["quality"] != 2 or p.get("tierUp"): continue
        build = p["build"].replace(f"cal-{p['tier']}-", "")
        model[(p["tier"], build, p["foe"], p["stars"])] = p

    teams = {}
    if a.teams:
        for key, r in json.load(open(a.teams)).get("rows", {}).items():
            teams[key] = r

    # team effect fitted on the fought team climbs (the share of the ideal gain that a real team gets: big creatures
    # swing through several players, and players get in each other's way). Falls back to the solo k.
    k_team = k
    pts = []
    for r in teams.values():
        t = {e["size"]: e for e in r.get("teams", []) if e.get("runs")}
        if 1 not in t: continue
        p1 = (t[1]["wins"] + 0.5) / (t[1]["runs"] + 1)
        for n, e in t.items():
            if n > 1: pts.append((p1, n, (e["wins"] + 0.5) / (e["runs"] + 1), e["runs"]))
    if pts:
        best = None
        for kt in [x / 20 for x in range(0, 81)]:
            err = sum(runs * (team_win(p1, n, kt) - obs) ** 2 for p1, n, obs, runs in pts)
            if best is None or err < best[0]: best = (err, kt)
        k_team = best[1]
        print(f"team effect fitted on {len(pts)} fought team sizes: k_team = {k_team:.2f} (ideal teamwork would be {k:.2f})")

    rows = {}
    keys = sorted({(t, f, s) for (t, b, f, s) in list(model) + list(fought)})
    for tier, foe, stars in keys:
        builds = {}
        for (t, b, f, s), v in fought.items():
            if (t, f, s) == (tier, foe, stars):
                builds[b] = {"win": round(v["win"], 3), "ttk": round(v["ttk"], 1), "runs": v["runs"], "source": "fought"}
        for (t, b, f, s), p in model.items():
            if (t, f, s) == (tier, foe, stars) and b not in builds:
                builds[b] = {"win": p["win"], "ttk": p["ttk"], "source": "model"}
        ref = builds.get(a.reference) or next(iter(builds.values()))
        fr = fought.get((tier, a.reference, foe, stars))
        key = f"{tier}/{foe}/{stars}"
        p1 = (fr["wins"] + 0.5) / (fr["runs"] + 1) if fr else ref["win"]

        team_row = teams.get(key)
        if team_row and team_row.get("teams"):
            needed, src, tried, team_list = team_row["playersNeeded"], "fought", team_row.get("teamTried", MAX_TEAM), team_row["teams"]
            # one player: pool the climb's own solo round with the calibration fights (two samples of the same fight)
            t1 = next((e for e in team_list if e.get("size") == 1 and e.get("runs")), None)
            if t1 and fr:
                pooled = (t1["wins"] + fr["wins"]) / (t1["runs"] + fr["runs"])
                if pooled >= NEED_WIN: needed = 1
                elif needed == 1:
                    needed = next((n for n in range(2, MAX_TEAM + 1) if team_win(pooled, n, k_team) >= NEED_WIN), 0)
                    src = "fought + model"
        else:
            needed, src, tried, team_list = 0, "model", MAX_TEAM, []
            for n in range(1, MAX_TEAM + 1):
                w = p1 if n == 1 else team_win(p1, n, k_team)
                team_list.append({"size": n, "win": round(w, 2)})
                if w >= NEED_WIN: needed = n; break

        ranked = sorted(builds.items(), key=lambda kv: -kv[1]["win"])
        rows[key] = {
            "biome": tier, "foe": foe, "name": creatures.get(foe, {}).get("name", foe), "stars": stars,
            "health": round(creatures.get(foe, {}).get("health", 0) * (1 + stars)),
            "loadout": f"cal-{tier}-{a.reference}", "bot": "fighter", "scenario": "calibration + model",
            "source": "fought" if fr else "model",
            "playersNeeded": needed, "playersNeededSource": src, "teamTried": tried, "needWin": NEED_WIN,
            "solo": {"runs": fr["runs"] if fr else 0, "wins": fr["wins"] if fr else 0, "winRate": round(fr["win"] if fr else ref["win"], 3),
                     "ttk": round(fr["ttk"] if fr else ref["ttk"], 1), "damageTakenPct": round(fr["damagePct"], 1) if fr else None},
            "teams": team_list,
            "builds": dict(ranked),
            "best": ranked[0][0], "worst": ranked[-1][0],
        }

    doc = {
        "about": "DrakesTestLab Combat Lab: game difficulty baseline. A mid player of each biome (q2 gear of the tier, the tier's skills, "
                 "real meals, the 'fighter' bot) against each creature at 0-2 stars: win rate alone, per weapon build, and how many such "
                 "players it takes to win at least half the time. 'fought' numbers are measured fights (8 per cell, 3x clock in lanes, "
                 "validated against 1x); 'model' numbers are calculated from them and the game's own data (spot-checked: 34 of 36 fresh fights held). "
                 "Rows written by your own runs replace these. Full rebuild: -testlab-combat \"cal-*\" then tools/combat_predict.py and tools/combat_build_db.py (takes hours).",
        "updated": datetime.datetime.now().strftime("%Y-%m-%dT%H:%M:%S"),
        "model": {"k": k, "kTeam": k_team, "healthPerPlayer": HEALTH_PER_PLAYER, "damagePerPlayer": DAMAGE_PER_PLAYER},
        "rows": dict(sorted(rows.items(), key=lambda kv: kv[0].lower())),
    }
    import os
    os.makedirs(os.path.dirname(a.out), exist_ok=True)
    json.dump(doc, open(a.out, "w"), indent=1)
    src = {}
    for r in rows.values(): src[r["source"]] = src.get(r["source"], 0) + 1
    print(f"{len(rows)} rows ({', '.join(f'{v} {k}' for k, v in src.items())}) -> {a.out}")
    for key, r in rows.items():
        if r["playersNeeded"] != 1:
            print(f"  {key:32} solo {r['solo']['winRate']:4.0%}  players needed {r['playersNeeded'] or '>' + str(r['teamTried'])} ({r['playersNeededSource']})")


if __name__ == "__main__":
    main()
