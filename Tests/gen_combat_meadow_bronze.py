#!/usr/bin/env python3
"""Combat Lab set: Meadows and bronze-age foes against gear at different levels (tiers and upgrade quality).

Writes Tests/combat/loadouts and Tests/combat/scenarios (prefix mb-). Run: python3 gen_combat_meadow_bronze.py [--runs N]
  smoke  = one scenario, to prove the loop end to end before the matrix
  matrix = every loadout against every foe

Meals per tier are below; skills come from the weapon level (TestLab config). Edit them here.
"""
import json, os, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "combat")
RUNS = int(sys.argv[sys.argv.index("--runs") + 1]) if "--runs" in sys.argv else 3


def write(sub, name, data):
    d = os.path.join(ROOT, sub)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")


# Real meals per tier, eaten fresh before every fight (the game's own food code gives health, stamina and regen).
# A profile is a list of up to 3 food prefabs; "balanced" is the loadout's default.
MEALS = {
    "meadows": {"balanced": ["CookedMeat", "NeckTailGrilled", "Honey"],
                "health":   ["CookedMeat", "NeckTailGrilled", "BoarJerky"],
                "stamina":  ["Honey", "Raspberry", "CookedMeat"]},
    "bronze":  {"balanced": ["DeerStew", "MinceMeatSauce", "QueensJam"],
                "health":   ["DeerStew", "MinceMeatSauce", "CookedDeerMeat"],
                "stamina":  ["QueensJam", "CarrotSoup", "DeerStew"]},
}


def loadout(name, tier, weapon, armor, shield, quality):
    """Skills are not written: the lab starts every skill at the tier's level ([Combat] TierSkills: meadows 1, bronze 15...),
    and moves the weapon skill and Blocking by [Combat] SkillPerQuality (5) per weapon level from 2."""
    items = [{"prefab": weapon, "quality": quality, "equip": True}]
    items += [{"prefab": a, "quality": quality, "equip": True} for a in armor]
    if shield:
        items.append({"prefab": shield, "quality": quality, "equip": True})
    write("loadouts", name, {"name": name, "tier": tier, "meals": MEALS[tier]["balanced"], "food": MEALS[tier], "items": items})
    return name


BRONZE_ARMOR = ["ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze"]
LEATHER_ARMOR = ["ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather"]

loadouts = []
# the same bronze kit upgraded 1..3 (q1 = +0, q2 = +1, q3 = +2): what a smith's levels are worth
for q in (1, 2, 3):
    loadouts.append(loadout(f"mb-bronze-sword-q{q}", "bronze", "SwordBronze", BRONZE_ARMOR, "ShieldBronzeBuckler", q))
# other bronze weapons at level 2, same kit
loadouts.append(loadout("mb-bronze-axe-q2", "bronze", "AxeBronze", BRONZE_ARMOR, "ShieldBronzeBuckler", 2))
loadouts.append(loadout("mb-bronze-mace-q2", "bronze", "MaceBronze", BRONZE_ARMOR, "ShieldBronzeBuckler", 2))
loadouts.append(loadout("mb-bronze-spear-q2", "bronze", "SpearBronze", BRONZE_ARMOR, "ShieldBronzeBuckler", 2))
# the step below: what a Meadows player has
loadouts.append(loadout("mb-meadows-club-leather", "meadows", "Club", LEATHER_ARMOR, "ShieldWood", 1))

FOES = [("Boar", 0), ("Neck", 0), ("Greyling", 0),                                   # Meadows
        ("Greydwarf", 0), ("Greydwarf_Elite", 0), ("Skeleton", 0), ("Troll", 0)]    # Black Forest (bronze age)


MEADOWS_SKITTISH = {"Boar", "Neck", "Greyling"}      # flee or ignore you in real Valheim: raw data needs them to fight


def scenario(lo, foe, stars, runs, **extra):
    name = f"mb-{lo[3:]}-vs-{foe}" + (f"-{stars}star" if stars else "") + extra.pop("suffix", "")
    data = {"name": name, "runs": runs, "timeout": 75, "bot": "trained", "seed": 1,
            "gear": {"loadout": lo}, "foes": {"prefab": foe, "stars": stars}, "variants": ["vanilla"]}
    if foe in MEADOWS_SKITTISH:
        data["fearless"] = True
        data["aggro"] = True
    data.update(extra)
    write("scenarios", name, data)
    return name


# smoke: proves setup, fight, record, reset, report
scenario("mb-bronze-sword-q1", "Boar", 0, 3)
for lo in loadouts:
    for foe, stars in FOES:
        if lo == "mb-bronze-sword-q1" and foe == "Boar":
            scenario(lo, foe, stars, RUNS)     # the smoke scenario above is overwritten with the full count; use --runs to override
            continue
        scenario(lo, foe, stars, RUNS)
# how many can they fight at once: climb the count until the loadout mostly loses, for each food profile
for lo in ("mb-bronze-sword-q2", "mb-bronze-sword-q3"):
    for foe in ("Skeleton", "Greydwarf"):
        scenario(lo, foe, 0, 3, suffix="-capacity", food=["balanced", "health", "stamina"],
                 calibrate={"from": 1, "to": 12, "step": 1, "stopBelow": 0.2, "capacityAt": 0.5}, timeout=90)

# primary vs secondary: a two-hander (atgeir), only primary, only secondary, a secondary every 3rd swing, one per 4 seconds
loadouts.append(loadout("mb-bronze-atgeir-q2", "bronze", "AtgeirBronze", BRONZE_ARMOR, None, 2))
for foe in ("Greydwarf", "Skeleton", "Troll"):
    scenario("mb-bronze-atgeir-q2", foe, 0, 3, suffix="-attacks", attack=["primary", "secondary", "mix:every3", "mix:4s"])
# same idea for the sword, which has a secondary too
for foe in ("Greydwarf", "Skeleton"):
    scenario("mb-bronze-sword-q2", foe, 0, 3, suffix="-attacks", attack=["primary", "secondary", "mix"])

# raw-data variations for the giggles: no stagger, no knockback, double damage
scenario("mb-bronze-sword-q2", "Greydwarf", 0, 3, suffix="-nostagger", noStagger="all", noKnockback=True)
scenario("mb-bronze-sword-q2", "Troll", 0, 3, suffix="-foedmg-half", foeDamage=0.5)

# unsticker test: both damage multipliers are 0, so no damage ever changes hands; after 5 quiet seconds everybody should be moved, twice, then the fight ends as stalled
scenario("mb-bronze-sword-q2", "Greydwarf", 0, 1, suffix="-unstick-test", stall=5, unsticks=2, timeout=60, gearDamage=0, foeDamage=0)

print("loadouts:", len(loadouts), "foes:", len(FOES), "scenarios:", len(loadouts) * len(FOES))

# clock check: the same fight at normal speed and at 2x. Win rate and damage taken should match within noise; only the wall time differs.
scenario("mb-bronze-sword-q2", "Greydwarf", 0, 10, suffix="-clock1x", timeScale=1)
scenario("mb-bronze-sword-q2", "Greydwarf", 0, 10, suffix="-clock2x", timeScale=2)

# a tougher clock check: Troll at q2 is a coin flip, so a clock that bends the fight shows in the win rate
for x in (1, 2, 3):
    scenario("mb-bronze-sword-q2", "Troll", 0, 10, suffix=f"-clock{x}x-troll", timeScale=x)

# bigger clock check: 30 runs each at 1x and 3x (normal rendering), on two fights that are neither always won nor always lost
for foe in ("Troll", "Greydwarf_Elite"):
    for x in (1, 3):
        scenario("mb-bronze-sword-q2", foe, 0, 30, suffix=f"-big{x}x", timeScale=x)

# Validation of the speed-ups (scientific control). Fixed run counts (autoRuns off): stopping early biases a comparison.
# ctl = 1x, one fight at a time. Candidates: 3 lanes at 2x and at 3x. Each must match the control within noise:
# win rate (Fisher exact), time to kill and time to die (Mann-Whitney), damage taken. 40 runs per arm, fighter bot.
# Greydwarf_Elite is the sensitive one (quick swings: 3x at 40 fps made it easier before).
for foe in ("Troll", "Greydwarf_Elite"):
    scenario("mb-bronze-sword-q2", foe, 0, 40, suffix="-v-ctl", timeScale=1, lanes=1, bot="fighter", autoRuns=False)
    scenario("mb-bronze-sword-q2", foe, 0, 40, suffix="-v-2x-lanes3", timeScale=2, lanes=3, bot="fighter", autoRuns=False)
    scenario("mb-bronze-sword-q2", foe, 0, 40, suffix="-v-3x-lanes3", timeScale=3, lanes=3, bot="fighter", autoRuns=False)
    # round 2: which part skews it? lanes alone, lanes without the zone fence, speed alone
    scenario("mb-bronze-sword-q2", foe, 0, 30, suffix="-v-1x-lanes3", timeScale=1, lanes=3, bot="fighter", autoRuns=False)
    scenario("mb-bronze-sword-q2", foe, 0, 30, suffix="-v-1x-lanes3-nofence", timeScale=1, lanes=3, laneKeep=False, bot="fighter", autoRuns=False)
    scenario("mb-bronze-sword-q2", foe, 0, 30, suffix="-v-2x", timeScale=2, lanes=1, bot="fighter", autoRuns=False)

# Fight style check: the "fighter" and "veteran" bots read the foe's swing (parry just before the hit, else block or roll),
# hit back in short combos, back off for stamina and punish a stagger. Watch it; the log shows blocks, parries and punishes.
for foe in ("Troll", "Greydwarf_Elite"):
    for bot in ("fighter", "veteran"):
        scenario("mb-bronze-sword-q2", foe, 0, 6, suffix=f"-style-{bot}", bot=bot, timeScale=1, lanes=3)

# ---- Mixed lanes: one fight, a different setup in each lane (setups rotate between lanes every fight, each gets its own row)
loadout("mb-bronze-sword-q4", "bronze", "SwordBronze", BRONZE_ARMOR, "ShieldBronzeBuckler", 4)   # for the q1-q4 sweep only
scenario("mb-bronze-sword-q2", "Greydwarf_Elite", 0, 10, suffix="-mix-quality", bot="fighter",
         lanes=[{"loadout": f"mb-bronze-sword-q{q}", "label": f"bronze sword q{q}"} for q in (1, 2, 3, 4)])
scenario("mb-bronze-sword-q2", "Troll", 0, 10, suffix="-mix-stars", bot="fighter",
         lanes=[{"foes": {"prefab": "Troll", "stars": st}, "label": "bronze q2"} for st in (0, 1, 2)])
scenario("mb-bronze-sword-q2", "Greydwarf", 0, 10, suffix="-mix-count", bot="fighter",
         lanes=[{"foes": {"prefab": "Greydwarf", "count": c}, "label": "bronze q2"} for c in (1, 2, 3)])

# ---- PvP: you (Bjorn) against Bjornita, a bot player with her own set and fighting style
def pvp(name, mine, hers, my_bot="fighter", her_bot="fighter", runs=10):
    data = {"name": name, "runs": runs, "timeout": 240, "bot": my_bot, "seed": 1, "gear": {"loadout": mine},
            "pvp": {"loadout": hers, "bot": her_bot, "name": "Bjornita"}, "variants": ["vanilla"]}
    write("scenarios", name, data)

pvp("pvp-bronze-sword-vs-same", "mb-bronze-sword-q2", "mb-bronze-sword-q2")              # mirror match: should be about 50/50
pvp("pvp-bronze-sword-vs-mace", "mb-bronze-sword-q2", "mb-bronze-mace-q2")
pvp("pvp-bronze-sword-vs-spear", "mb-bronze-sword-q2", "mb-bronze-spear-q2")
pvp("pvp-fighter-vs-veteran", "mb-bronze-sword-q2", "mb-bronze-sword-q2", "fighter", "veteran")

# ---- Team fights: if one player loses, how many does it take? Team size climbs 1..4 until a team wins half its fights.
for st in (0, 1, 2):
    scenario("mb-bronze-sword-q2", "Troll", st, 10, suffix="-team", bot="fighter",
             team={"from": 1, "to": 4, "needWin": 0.5})

# Lanes diagnosis: which part of lanes makes fights harder? Greydwarf_Elite (cheap, and the kill time moved most).
# ctl = one fight at a time; lanes3 = as before; lanes3-nolock = foes pick targets by their own AI;
# zoneonly = the small lane zone and spawn distance, but no other fighters.
for arm, extra in (("ctl", {"lanes": 1}), ("lanes3", {"lanes": 3}), ("lanes3-nolock", {"lanes": 3, "laneLock": False}),
                   ("zoneonly", {"lanes": 3, "zoneOnly": True})):
    scenario("mb-bronze-sword-q2", "Greydwarf_Elite", 0, 12, suffix=f"-d-v-{arm}", timeScale=1, bot="fighter", autoRuns=False, **extra)
