#!/usr/bin/env python3
"""Writes the shipped Combat Lab baseline set: loadouts and scenarios for bronze and iron weapons.

Run:  python3 gen_combat_baselines.py   ->  Tests/combat/loadouts/*.json, Tests/combat/scenarios/*.json
Copy Tests/combat into BepInEx/config/DrakesTestLab/combat, run `Run-TestLabSuites -Combat all`, then copy the resulting
combat/results/latest.json to combat/baselines/<name>.json. Later runs are compared against it automatically.

The numbers (health, stamina, skill levels, foes) are first guesses for what a player of that tier has. Edit them here.
"""
import json, os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "combat")

TIERS = {
    "bronze": {
        "maxHealth": 125, "maxStamina": 100, "skill": 30,
        "armor": ["ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze"],
        "shield": "ShieldBronzeBuckler",
        "one_hand": ["SwordBronze", "AxeBronze", "MaceBronze", "SpearBronze"],
        "two_hand": ["AtgeirBronze"],
        "foes": [("Greydwarf_Elite", 0), ("Troll", 0), ("Draugr_Elite", 0), ("Wolf", 0)],
    },
    "iron": {
        "maxHealth": 150, "maxStamina": 110, "skill": 45,
        "armor": ["ArmorIronChest", "ArmorIronLegs", "HelmetIron"],
        "shield": "ShieldIronSquare",
        "one_hand": ["SwordIron", "AxeIron", "MaceIron"],
        "two_hand": ["AtgeirIron", "SledgeIron"],
        "foes": [("Draugr_Elite", 0), ("Wolf", 0), ("Fenring", 0), ("Troll", 0)],
    },
}

SKILL = {"Sword": "Swords", "Axe": "Axes", "Mace": "Clubs", "Spear": "Spears", "Atgeir": "Polearms", "Sledge": "Clubs"}


def skill_for(prefab):
    for k, v in SKILL.items():
        if prefab.startswith(k):
            return v
    return "Swords"


def write(sub, name, data):
    d = os.path.join(ROOT, sub)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")


for tier, t in TIERS.items():
    loadouts = []
    for w in t["one_hand"] + t["two_hand"]:
        name = f"{tier}-{w}" + ("" if w in t["two_hand"] else "-shield")
        items = [{"prefab": w, "equip": True}] + [{"prefab": a, "equip": True} for a in t["armor"]]
        if w in t["one_hand"]:
            items.append({"prefab": t["shield"], "equip": True})
        write("loadouts", name, {
            "name": name, "tier": tier, "maxHealth": t["maxHealth"], "maxStamina": t["maxStamina"],
            "skills": {skill_for(w): t["skill"], "Blocking": t["skill"]}, "items": items})
        loadouts.append(name)
    for lo in loadouts:
        for foe, stars in t["foes"]:
            name = f"{lo}-vs-{foe}" + (f"-{stars}star" if stars else "")
            write("scenarios", name, {
                "name": name, "runs": 30, "timeout": 75, "bot": "trained", "seed": 1,
                "gear": {"loadout": lo},
                "foes": {"prefab": foe, "stars": stars},
                "variants": ["vanilla"]})
print("loadouts:", len(os.listdir(os.path.join(ROOT, "loadouts"))), "scenarios:", len(os.listdir(os.path.join(ROOT, "scenarios"))))
