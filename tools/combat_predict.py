#!/usr/bin/env python3
"""Combat Lab difficulty model: calculate the fights nobody fought, from the game's numbers plus calibration fights.

Calibration fights (cal-* scenarios) measure what the game data cannot tell: how well a real-style player uses a build
against a creature. For each build x creature the model keeps:
  efficiency  = measured damage per second / the weapon's damage per hit against that creature
                (attack speed, hits that land, time spent blocking, dodging and walking)
  pressure    = measured damage taken per second (the creature's damage after the player's armor, blocks and parries)
Then for any other combination it calculates:
  weapon level   damage + damagePerLevel x (q - 1)       -> damage per hit -> DPS = efficiency x damage per hit
  resistances    per damage type (Weak 1.5, Resistant 0.5, Immune 0 ...) from the game's creature data
  stars          health x (1 + stars), damage x (1 + 0.5 x stars)  (Valheim's level scaling)
  armor          Valheim's armor formula on the creature's hit: dmg - armor, or dmg^2 / (4 armor) past half
  win chance     how long the player lasts vs how long the kill takes (a logistic curve fitted to the calibration fights)
A build/creature pair never fought (another biome's creature) borrows the build's median efficiency: a rougher guess,
marked "borrowed".

Assumptions to keep in mind: the skill factor is not modelled (calibration is at the tier's skill; predictions for the same
tier are fine, other skill levels are not); blocking and parrying are folded into "pressure" at the calibrated armor and
shield; backstab openers are added only when asked (--opener: first hit x the weapon's backstab bonus).

Usage:
  combat_predict.py --gamedata gamedata.json --cal cal-blackforest-1.json [--cal more.json] --loadouts Tests/combat/loadouts
                    [--out predictions.json] [--spot N --spot-out Tests/combat/scenarios]
"""
import argparse, glob, json, math, os, random, statistics as st

RESIST = {"Normal": 1.0, "Resistant": 0.5, "VeryResistant": 0.25, "Weak": 1.5, "VeryWeak": 2.0, "Immune": 0.0, "Ignore": 0.0,
          "SlightlyResistant": 0.75, "SlightlyWeak": 1.25}
TYPES = ("blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit")   # chop / pickaxe only hit trees and rocks


def per_hit(item, creature, q):
    """Damage of one plain hit at weapon level q against the creature's resistances (before skill and armor)."""
    total = 0.0
    for t in TYPES:
        base = item.get("damage", {}).get(t, 0) + item.get("damagePerLevel", {}).get(t, 0) * (q - 1)
        total += base * RESIST.get(creature.get("resist", {}).get(t, "Normal"), 1.0)
    return total


def armor_after(dmg, armor):
    """Valheim: damage below twice the armor is squared down, above it the armor is subtracted."""
    if armor <= 0: return dmg
    return dmg - armor if dmg > armor * 2 else dmg * dmg / (4 * armor)


def creature_hit(creature):
    """A typical hit from the creature: the mean of its own weapons' damage (types that hurt players)."""
    hits = [sum(a.get("damage", {}).get(t, 0) for t in TYPES) for a in creature.get("attacks", [])]
    hits = [h for h in hits if h > 0]
    return st.mean(hits) if hits else None


def load_loadouts(folder):
    out = {}
    for f in glob.glob(os.path.join(folder, "*.json")):
        d = json.load(open(f))
        out[d["name"]] = d
    return out


def kit(loadout, items, q=None):
    """Weapon prefab, its quality, the total armor at its level, and the meals' health."""
    weapon = None; armor = 0.0; quality = 2
    for it in loadout.get("items", []):
        data = items.get(it["prefab"], {}); lvl = q or it.get("quality", 1)
        if "armor" in data:
            armor += data["armor"] + data.get("armorPerLevel", 0) * (lvl - 1)
        elif data.get("damage") and data.get("type") != "Shield" and weapon is None:
            weapon = it["prefab"]; quality = lvl
    hp = 25 + sum(items.get(m, {}).get("health", 0) for m in loadout.get("meals", []))
    return weapon, quality, armor, hp


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gamedata", required=True)
    ap.add_argument("--cal", action="append", required=True)
    ap.add_argument("--loadouts", required=True)
    ap.add_argument("--out", default="")
    ap.add_argument("--opener", action="store_true", help="add a backstab opener (first hit x backstab bonus) to stealth builds")
    ap.add_argument("--spot", type=int, default=0, help="write N random predicted combos as spot-check scenarios")
    ap.add_argument("--spot-out", default="")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--spot-tiers", default="", help="comma list: spot-check only these biomes, the same number from each")
    ap.add_argument("--no-anchor", action="store_true", help="no per-pair offset (the plain curve; for comparing)")
    a = ap.parse_args()

    gd = json.load(open(a.gamedata)); items = gd["items"]; creatures = gd["creatures"]
    loadouts = load_loadouts(a.loadouts)
    label_to_lo = {}
    for name, lo in loadouts.items():
        if lo.get("build") and name == f"cal-{lo['tier']}-{lo['build']}":     # the base build, not a spot-check level of it
            label_to_lo[f"{lo['tier']} {lo['build']}"] = name
        label_to_lo[name] = name

    # ---- calibration: efficiency and pressure per build x creature (0 stars); starred cells fit the star factors
    cal = {}
    star_cells = []
    star_fought = []
    for path in a.cal:
        for c in json.load(open(path)).get("cells", []):
            label = c["loadout"].split(" (")[0]
            lo_name = label_to_lo.get(label)
            foe = c["foe"].split(" ")[0]
            star_part = c["foe"].split(" ")[1] if " " in c["foe"] else ""
            stars = int(star_part.rstrip("*")) if star_part.endswith("*") and star_part[:-1].isdigit() else 0
            if not lo_name or foe not in creatures or c.get("runs", 0) == 0 or c.get("dpsDealt", 0) <= 0:
                continue
            decided = c.get("wins", 0) + c.get("losses", c["runs"] - c.get("wins", 0) - c.get("timeouts", 0))
            if decided * 2 < c["runs"]:
                continue                                              # mostly stalled or timed out: not a measured fight
            if " (" in c["loadout"] and "team of" in c["loadout"] and "team of 1" not in c["loadout"]:
                continue                                              # team fights are not one player's numbers
            weapon, q, armor, hp = kit(loadouts[lo_name], items)
            ph = per_hit(items[weapon], creatures[foe], q)
            if ph <= 0: continue
            secs = c.get("ttkMean") or (hp / max(0.1, c.get("dpsDealt", 1)))
            taken = c.get("damageTakenPct", 0) / 100 * hp
            if stars > 0:
                star_cells.append((lo_name, foe, stars, c["dpsDealt"] / ph, taken / max(1.0, secs)))
                star_fought.append({"lo": lo_name, "foe": foe, "stars": stars, "wins": c.get("wins", 0), "runs": decided,
                                    "ttk": c.get("ttkMean", 0)})
                continue
            cal[(lo_name, foe)] = {"eff": c["dpsDealt"] / ph, "dtps": taken / max(1.0, secs), "armor": armor, "hp": hp,
                                   "win": c.get("wins", 0) / decided, "ttk": c.get("ttkMean", 0), "runs": decided}

    # ---- star factors: stars do more than Valheim's health/damage scaling (a bigger stagger threshold means fewer
    #      staggers, longer fights). Per star level, the measured efficiency and pressure against the same build and
    #      creature at 0 stars (geometric mean); 1.0 when no starred calibration exists.
    eff_star = {1: 1.0, 2: 1.0}; dtps_star = {1: 1.0, 2: 1.0}
    for n in (1, 2):
        er = [e / cal[(lo, f)]["eff"] for lo, f, sn, e, d in star_cells if sn == n and (lo, f) in cal and cal[(lo, f)]["eff"] > 0]
        hit_ok = []
        for lo, f, sn, e, d in star_cells:
            if sn != n or (lo, f) not in cal or cal[(lo, f)]["dtps"] <= 0: continue
            hit = creature_hit(creatures[f]); arm = kit(loadouts[lo], items)[2]
            base = armor_after(hit * (1 + 0.5 * n), arm) / max(1e-6, armor_after(hit, arm)) if hit else 1 + 0.5 * n
            hit_ok.append(d / (cal[(lo, f)]["dtps"] * base))
        if er: eff_star[n] = math.exp(st.mean(math.log(max(1e-3, x)) for x in er))
        if hit_ok: dtps_star[n] = math.exp(st.mean(math.log(max(1e-3, x)) for x in hit_ok))
    if star_cells:
        print(f"star factors (from {len(star_cells)} starred fights): damage dealt x{eff_star[1]:.2f} / x{eff_star[2]:.2f}, "
              f"damage taken x{dtps_star[1]:.2f} / x{dtps_star[2]:.2f} beyond Valheim's own scaling (1 / 2 stars)")

    # ---- the win curve: P(win) = logistic(k * ln(time the player lasts / time to kill)), k fitted on the calibration
    def raw_ratio(v, foe):
        ttk = creatures[foe]["health"] / max(1e-6, v["eff"] * per_hit(items[kit(loadouts[lo], items)[0]], creatures[foe], kit(loadouts[lo], items)[1]))
        return ttk
    pts = []
    for (lo, foe), v in cal.items():
        ttk = creatures[foe]["health"] / max(1e-6, v["eff"] * per_hit(items[kit(loadouts[lo], items)[0]], creatures[foe], kit(loadouts[lo], items)[1]))
        lasts = v["hp"] / max(1e-6, v["dtps"])
        pts.append((math.log(max(1e-3, lasts / ttk)), v["win"], v["runs"]))
    best_k, best_err = 3.0, 1e9
    for k in [x / 4 for x in range(1, 41)]:
        err = sum(n * (1 / (1 + math.exp(-k * x)) - w) ** 2 for x, w, n in pts)
        if err < best_err: best_k, best_err = k, err

    # ---- per-pair anchor: what the formula cannot see (a creature's AoE that a style walks into, a build the AI
    #      out-paces) shows up as the fought win rate differing from the curve. Each fought pair keeps a logit offset
    #      that puts the curve on its measured win rate (shrunk towards 0 with few runs), carried to its other weapon
    #      levels and star counts. --no-anchor turns it off.
    def logit(x): return math.log(x / (1 - x))
    bias = {}
    if not a.no_anchor:
        for (lo, foe), v in cal.items():
            ttk = creatures[foe]["health"] / max(1e-6, v["eff"] * per_hit(items[kit(loadouts[lo], items)[0]], creatures[foe], kit(loadouts[lo], items)[1]))
            x = best_k * math.log(max(1e-3, (v["hp"] / max(1e-6, v["dtps"])) / ttk))
            wins = v["win"] * v["runs"]
            obs = (wins + 0.5) / (v["runs"] + 1)
            shrink = v["runs"] / (v["runs"] + 4)
            bias[(lo, foe)] = max(-8.0, min(8.0, shrink * (logit(obs) - max(-6.0, min(6.0, x)))))

    # ---- predictions: every calibrated build at every weapon level, 0-2 stars, vs every creature of its own biome;
    #      plus tier-ups (a build against the next biome's creatures, borrowed efficiency)
    biomes = sorted({lo["tier"] for lo in loadouts.values() if lo["name"].startswith("cal-")})
    order = ["meadows", "blackforest", "swamp", "mountain", "plains", "mistlands", "ashlands"]
    creatures_of = {}
    for (lo, foe) in cal:
        creatures_of.setdefault(loadouts[lo]["tier"], set()).add(foe)
    builds = sorted({lo for (lo, _) in cal})
    preds = []
    for lo in builds:
        tier = loadouts[lo]["tier"]
        effs = [v["eff"] for (l, _), v in cal.items() if l == lo]
        dtpss = [v["dtps"] for (l, _), v in cal.items() if l == lo]
        targets = [(f, False) for f in sorted(creatures_of.get(tier, []))]
        nxt = order[order.index(tier) + 1] if tier in order and order.index(tier) + 1 < len(order) else None
        if nxt:
            targets += [(f, True) for f in sorted(creatures_of.get(nxt, []))]        # tier-up: only creatures calibrated elsewhere
        weapon, q0, _, hp = kit(loadouts[lo], items)
        for foe, borrowed in targets:
            v = cal.get((lo, foe))
            eff = v["eff"] if v else st.median(effs)
            base_dtps = v["dtps"] if v else None
            if base_dtps is None:
                # another build of the next biome fought it: its pressure, rescaled to this build's armor
                other = [(cv, kit(loadouts[l2], items)[2]) for (l2, f2), cv in cal.items() if f2 == foe]
                hit = creature_hit(creatures[foe])
                if not other: continue
                ov, oarm = other[0]
                base_dtps = ov["dtps"] * (armor_after(hit, kit(loadouts[lo], items)[2]) / max(1e-6, armor_after(hit, oarm)) if hit else 1.0)
            for q in range(1, items[weapon].get("maxQuality", 4) + 1):
                _, _, armor_q, _ = kit(loadouts[lo], items, q)
                _, _, armor_cal, _ = kit(loadouts[lo], items)
                hit = creature_hit(creatures[foe])
                for stars in (0, 1, 2):
                    dmg = per_hit(items[weapon], creatures[foe], q)
                    dps = eff * dmg * (eff_star.get(stars, 1.0) if stars else 1.0)
                    if dps <= 0: continue
                    health = creatures[foe]["health"] * (1 + stars)
                    if a.opener and loadouts[lo].get("build", "").startswith("stealth"):
                        health = max(1.0, health - dmg * (items[weapon].get("backstab", 1) - 1))
                    ttk = health / dps
                    scale = 1.0
                    if hit:
                        h_star = hit * (1 + 0.5 * stars)
                        scale = armor_after(h_star, armor_q) / max(1e-6, armor_after(hit, armor_cal))
                    else:
                        scale = 1 + 0.5 * stars
                    dtps = base_dtps * scale * (dtps_star.get(stars, 1.0) if stars else 1.0)
                    lasts = hp / max(1e-6, dtps)
                    z = max(-6.0, min(6.0, best_k * math.log(max(1e-3, lasts / ttk))))    # never-hurt fights: finite
                    p = 1 / (1 + math.exp(-(z + bias.get((lo, foe), 0.0))))
                    preds.append({"build": lo, "tier": tier, "foe": foe, "stars": stars, "quality": q, "win": round(p, 2),
                                  "ttk": round(ttk, 1), "lasts": round(lasts, 1), "dps": round(dps, 1), "dtps": round(dtps, 2),
                                  "fought": bool(v) and q == q0 and stars == 0, "borrowed": not v, "tierUp": borrowed})

    # ---- per-creature star anchor: how much harder (or easier) a creature's stars really are than the shared star
    #      factors say. Each creature's fought star ladder (logit of the measured win rate minus the model's, shrunk with
    #      few runs; kill time as a ratio) moves every build's prediction for that creature and star count.
    if not a.no_anchor and star_fought:
        at = {(p["build"], p["foe"], p["stars"]): p for p in preds if not p["tierUp"] and p["quality"] == kit(loadouts[p["build"]], items)[1]}
        res, kr = {}, {}
        for sf in star_fought:
            p = at.get((sf["lo"], sf["foe"], sf["stars"]))
            if not p or sf["runs"] == 0: continue
            obs = (sf["wins"] + 0.5) / (sf["runs"] + 1)
            pw = min(0.995, max(0.005, p["win"]))
            res.setdefault((sf["foe"], sf["stars"]), []).append((sf["runs"] / (sf["runs"] + 4)) * (logit(obs) - logit(pw)))
            if sf["ttk"] > 0 and p["ttk"] > 0:
                kr.setdefault((sf["foe"], sf["stars"]), []).append(math.log(sf["ttk"] / p["ttk"]))
        star_bias = {k: max(-8.0, min(8.0, st.mean(v))) for k, v in res.items()}
        star_ttk = {k: math.exp(st.mean(v)) for k, v in kr.items()}
        for p in preds:
            key = (p["foe"], p["stars"])
            if key in star_bias:
                w = min(0.995, max(0.005, p["win"]))
                p["win"] = round(1 / (1 + math.exp(-(logit(w) + star_bias[key]))), 2)
            if key in star_ttk:
                p["ttk"] = round(p["ttk"] * star_ttk[key], 1)
        print(f"star anchors: {len(star_bias)} creature/star pairs (mean |shift| "
              f"{st.mean(abs(v) for v in star_bias.values()) if star_bias else 0:.2f} logit)")

    print(f"calibration pairs: {len(cal)}; win curve k = {best_k:.2f}; predictions: {len(preds)}")
    # fit check: the model at the fought points against what was measured
    fit = [(p, cal[(p["build"], p["foe"])]) for p in preds if p["fought"]]
    if fit:
        errs = [abs(p["win"] - v["win"]) for p, v in fit]
        print(f"fit at the fought points: mean |win error| {st.mean(errs):.0%}, worst {max(errs):.0%} "
              f"({len([e for e in errs if e > 0.25])} of {len(errs)} off by more than 25 points)")
        for p, v in sorted(fit, key=lambda x: -abs(x[0]["win"] - x[1]["win"]))[:5]:
            print(f"    worst: {p['build']} vs {p['foe']}: model {p['win']:.0%}, fought {v['win']:.0%} ({v['runs']} runs)")
    for p in preds:
        if p["stars"] == 0 and p["quality"] in (1, 4) and not p["tierUp"] and "Troll" in p["foe"]:
            print(f"  {p['build']:26} q{p['quality']} vs {p['foe']:16} {p['stars']}*  win {p['win']:4.0%}  kill {p['ttk']:5.1f}s  lasts {p['lasts']:5.1f}s")
    if a.out:
        json.dump({"k": best_k, "calibration": {f"{l}|{f}": v for (l, f), v in cal.items()}, "predictions": preds},
                  open(a.out, "w"), indent=1)
        print("written", a.out)

    if a.spot and a.spot_out:
        # random predicted combos nobody fought, 3 per fight (mixed lanes: each lane its own build, level, foe and stars)
        rnd = random.Random(a.seed)
        pool = [p for p in preds if not p["fought"] and not p["borrowed"] and "stealth" not in p["build"]]
        if a.spot_tiers:
            picks = []
            for t in a.spot_tiers.split(","):
                tp = [p for p in pool if p["tier"] == t]
                picks += rnd.sample(tp, min(a.spot, len(tp)))
        else:
            picks = rnd.sample(pool, min(a.spot, len(pool)))
        os.makedirs(a.spot_out, exist_ok=True)
        lo_dir = os.path.join(os.path.dirname(a.spot_out.rstrip("/")), "loadouts")
        manifest = []
        for g in range(0, len(picks), 3):
            lanes = []
            for p in picks[g:g + 3]:
                lo_name = f"{p['build']}-q{p['quality']}"
                lo = {**loadouts[p["build"]], "name": lo_name, "spotOf": p["build"],
                      "items": [{**it, "quality": p["quality"]} for it in loadouts[p["build"]]["items"]]}
                json.dump(lo, open(os.path.join(lo_dir, lo_name + ".json"), "w"), indent=2)
                label = f"{lo_name} vs {p['foe']} {p['stars']}*"
                lanes.append({"loadout": lo_name, "foes": {"prefab": p["foe"], "stars": p["stars"]}, "label": label})
                manifest.append({"label": label, "predictedWin": p["win"], "predictedTtk": p["ttk"]})
                print(f"  spot {label}: predicted win {p['win']:.0%}, kill {p['ttk']}s")
            name = f"spot-{g // 3:02d}"
            first = lanes[0]
            sc = {"name": name, "runs": 8, "timeout": 150, "bot": "fighter", "seed": 1, "timeScale": 3, "autoRuns": False,
                  "gear": {"loadout": first["loadout"]}, "foes": first["foes"], "lanes": lanes, "variants": ["vanilla"]}
            json.dump(sc, open(os.path.join(a.spot_out, name + ".json"), "w"), indent=2)
        json.dump(manifest, open(os.path.join(a.spot_out, "..", "spot-predictions.json"), "w"), indent=1)

if __name__ == "__main__":
    main()
