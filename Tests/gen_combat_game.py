#!/usr/bin/env python3
"""Combat Lab: the game difficulty baseline set.

A mid player of each biome (q2 weapon, armor and shield of that tier, three real meals of that biome, the tier's skills
from [Combat] TierSkills, the "fighter" bot) against every ground creature of the biome at 0, 1 and 2 stars. Each
scenario is a "difficulty" run: solo first, then 2, 3, 4 players together until a team wins at least half its fights.
Results go into combat/difficulty-db.json (biome / creature / stars).

Writes Tests/combat/loadouts/game-<biome>-mid.json and Tests/combat/scenarios/game-<biome>-<creature>-<n>star.json.
Run them all with -testlab-combat "game-*" (or one biome: "game-swamp-*"). The whole game takes hours, even sped up.

Prefab names are checked when a run starts: a scenario naming an item, meal or creature the game does not have is
skipped with an error in the log, never fought with the wrong thing.

Not here yet: flying creatures (a melee bot cannot fight them fairly: Deathsquito, Hatchling, Gjall, Volture, Bat,
FallenValkyrie) and bosses (altars, arenas and phases of their own).
"""
import json, os, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "combat")
RUNS = int(sys.argv[sys.argv.index("--runs") + 1]) if "--runs" in sys.argv else 10   # cap per team size (auto-stop ends sooner)


def write(sub, name, data):
    d = os.path.join(ROOT, sub)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")


# biome: (weapon, armor, shield, meals, creatures). Creatures that flee or ignore you get "fearless"/"aggro" so they fight.
BIOMES = {
    "meadows":     ("Club", ["ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather"], "ShieldWood",
                    ["CookedMeat", "NeckTailGrilled", "Honey"],
                    ["Boar", "Neck", "Greyling"]),
    "blackforest": ("SwordBronze", ["ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze"], "ShieldBronzeBuckler",
                    ["DeerStew", "MinceMeatSauce", "QueensJam"],
                    ["Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman", "Skeleton", "Ghost", "Troll"]),
    "swamp":       ("SwordIron", ["ArmorIronChest", "ArmorIronLegs", "HelmetIron"], "ShieldBanded",
                    ["Sausages", "TurnipStew", "BlackSoup"],
                    ["Draugr", "Draugr_Elite", "Blob", "BlobElite", "Leech", "Wraith", "Surtling", "Abomination"]),
    "mountain":    ("SwordSilver", ["ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake"], "ShieldSilver",
                    ["OnionSoup", "Eyescream", "WolfJerky"],
                    ["Wolf", "Ulv", "Fenring", "Fenring_Cultist", "StoneGolem"]),
    "plains":      ("SwordBlackmetal", ["ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded"], "ShieldBlackmetal",
                    ["LoxPie", "FishWraps", "BloodPudding"],
                    ["Goblin", "GoblinBrute", "GoblinShaman", "Lox", "BlobTar"]),
    "mistlands":   ("SwordMistwalker", ["ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace"], "ShieldCarapace",
                    ["MeatPlatter", "HoneyGlazedChicken", "MushroomOmelette"],
                    ["Seeker", "SeekerBrute", "Tick", "Dverger"]),
    "ashlands":    ("SwordNiedhogg", ["ArmorFlametalChest", "ArmorFlametalLegs", "HelmetFlametal"], "ShieldFlametal",
                    ["PiquantPie", "SpicyMarmalade", "ScorchingMedley"],
                    ["Charred_Melee", "Morgen", "Asksvin", "BlobLava"]),
}
SKITTISH = {"Boar", "Neck", "Lox", "Deer"}       # flee or ignore you in real Valheim: the raw numbers need them to fight

count = 0
for biome, (weapon, armor, shield, meals, creatures) in BIOMES.items():
    lo = f"game-{biome}-mid"
    items = [{"prefab": weapon, "quality": 2, "equip": True}] + [{"prefab": a, "quality": 2, "equip": True} for a in armor]
    if shield:
        items.append({"prefab": shield, "quality": 2, "equip": True})
    write("loadouts", lo, {"name": lo, "tier": biome, "meals": meals, "items": items})
    for c in creatures:
        for stars in (0, 1, 2):
            name = f"game-{biome}-{c}-{stars}star"
            data = {"name": name, "runs": RUNS, "timeout": 120, "bot": "fighter", "seed": 1, "biome": biome,
                    "difficulty": True, "gear": {"loadout": lo}, "foes": {"prefab": c, "stars": stars}, "variants": ["vanilla"]}
            if c in SKITTISH:
                data["fearless"] = True
                data["aggro"] = True
            write("scenarios", name, data)
            count += 1
print(f"game baseline: {len(BIOMES)} biomes, {count} scenarios (each a 1-4 player climb)")
