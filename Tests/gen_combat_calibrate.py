#!/usr/bin/env python3
"""Combat Lab calibration set: the fights the difficulty model learns from (the rest is calculated, then spot-checked).

Per biome, the logical builds of that tier fight every ground creature at 0 stars:
  melee builds (sword+shield, axe+shield, mace+shield, spear+shield, two-hander) side by side in lanes against the same
  creature (setups rotate between lanes every fight, each build gets its own row);
  the stealth build (knife + light armor, "rogue" bot) in its own fight, the creature spawning unaware so it can open
  with a sneak backstab, then dodging and hitting and running.
Big creatures (trolls, abominations, golems, bosses later) also get:
  a skill sweep (twig / fighter / veteran players in lanes, sword build), and
  a team climb (1-4 players together until a team wins half its fights).

Clock 3x (validated: 3 lanes at 3x match one-at-a-time 1x on win rate, kill time and time to die; damage taken
runs ~10% low). Every build: q2 gear of the tier, three real meals of the biome, the tier's skills ([Combat] TierSkills). Runs stop by
themselves once the numbers are settled (cap per build: --runs, default 8).

Writes Tests/combat/loadouts/cal-<biome>-<build>.json and Tests/combat/scenarios/cal-<biome>-<creature>-<kind>.json.
Run a biome with -testlab-combat "cal-blackforest-*". Names are checked at start (a wrong prefab skips that scenario).
"""
import json, os, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "combat")
RUNS = int(sys.argv[sys.argv.index("--runs") + 1]) if "--runs" in sys.argv else 8
ONLY = sys.argv[sys.argv.index("--biome") + 1] if "--biome" in sys.argv else None


def write(sub, name, data):
    d = os.path.join(ROOT, sub)
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")


# Per biome: meals, armor sets, melee builds (name, weapon, shield or None, armor set), stealth build, creatures, big ones.
BIOMES = {
    "blackforest": {
        "meals": ["DeerStew", "MinceMeatSauce", "QueensJam"],
        "armor": {"heavy": ["ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze"],
                  "light": ["ArmorTrollLeatherChest", "ArmorTrollLeatherLegs", "HelmetTrollLeather", "CapeTrollHide"]},
        "melee": [("sword", "SwordBronze", "ShieldBronzeBuckler", "heavy"), ("axe", "AxeBronze", "ShieldBronzeBuckler", "heavy"),
                  ("mace", "MaceBronze", "ShieldBronzeBuckler", "heavy"), ("spear", "SpearBronze", "ShieldBronzeBuckler", "heavy"),
                  ("atgeir", "AtgeirBronze", None, "heavy")],
        "stealth": ("knife", "KnifeCopper", "light"),
        "creatures": ["Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman", "Skeleton", "Ghost", "Troll"],
        "big": ["Troll"],
    },
    "swamp": {
        "meals": ["Sausages", "TurnipStew", "BlackSoup"],
        "armor": {"heavy": ["ArmorIronChest", "ArmorIronLegs", "HelmetIron"],
                  "light": ["ArmorRootChest", "ArmorRootLegs", "HelmetRoot"]},
        "melee": [("sword", "SwordIron", "ShieldBanded", "heavy"), ("axe", "AxeIron", "ShieldBanded", "heavy"),
                  ("mace", "MaceIron", "ShieldBanded", "heavy"), ("spear", "SpearElderbark", "ShieldBanded", "heavy"),
                  ("atgeir", "AtgeirIron", None, "heavy"), ("battleaxe", "Battleaxe", None, "heavy")],
        "stealth": ("knife", "KnifeChitin", "light"),
        "creatures": ["Draugr", "Draugr_Elite", "Blob", "BlobElite", "Leech", "Wraith", "Surtling", "Abomination"],
        "big": ["Abomination"],
    },
    "mountain": {
        "meals": ["SerpentStew", "WolfMeatSkewer", "Eyescream"],
        "armor": {"heavy": ["ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake"],
                  "light": ["ArmorFenringChest", "ArmorFenringLegs", "HelmetFenring"]},
        "melee": [("sword", "SwordSilver", "ShieldSilver", "heavy"), ("mace", "MaceSilver", "ShieldSilver", "heavy"),
                  ("spear", "SpearWolfFang", "ShieldSilver", "heavy"), ("battleaxe", "Battleaxe", None, "heavy")],
        "stealth": ("knife", "KnifeSilver", "light"),
        "creatures": ["Wolf", "Ulv", "Fenring", "Fenring_Cultist", "StoneGolem"],
        "big": ["StoneGolem"],
    },
    "plains": {
        "meals": ["LoxPie", "FishWraps", "BloodPudding"],
        "armor": {"heavy": ["ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded"],
                  "light": ["ArmorFenringChest", "ArmorFenringLegs", "HelmetFenring"]},
        "melee": [("sword", "SwordBlackmetal", "ShieldBlackmetal", "heavy"), ("axe", "AxeBlackMetal", "ShieldBlackmetal", "heavy"),
                  ("mace", "MaceNeedle", "ShieldBlackmetal", "heavy"), ("atgeir", "AtgeirBlackmetal", None, "heavy")],
        "stealth": ("knife", "KnifeBlackMetal", "light"),
        "creatures": ["Goblin", "GoblinBrute", "GoblinShaman", "Lox", "BlobTar"],
        "big": ["GoblinBrute", "Lox"],
    },
    "mistlands": {
        "meals": ["MeatPlatter", "HoneyGlazedChicken", "MushroomOmelette"],
        "armor": {"heavy": ["ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace"],
                  "light": ["ArmorFenringChest", "ArmorFenringLegs", "HelmetFenring"]},
        "melee": [("sword", "SwordMistwalker", "ShieldCarapace", "heavy"), ("axe", "AxeJotunBane", "ShieldCarapace", "heavy"),
                  ("spear", "SpearCarapace", "ShieldCarapace", "heavy"), ("atgeir", "AtgeirHimminAfl", None, "heavy")],
        "stealth": ("knife", "KnifeSkollAndHati", "light"),
        "creatures": ["Seeker", "SeekerBrute", "Tick", "Dverger"],
        "big": ["SeekerBrute"],
    },
    "ashlands": {
        "meals": ["PiquantPie", "MashedMeat", "ScorchingMedley"],
        "armor": {"heavy": ["ArmorFlametalChest", "ArmorFlametalLegs", "HelmetFlametal"],
                  "light": ["ArmorAshlandsMediumChest", "ArmorAshlandsMediumlegs", "HelmetAshlandsMediumHood"]},
        "melee": [("sword", "SwordNiedhogg", "ShieldFlametal", "heavy"), ("axe", "AxeBerzerkr", None, "heavy"),   # Berserkir: dual axes, no shield
                  ("mace", "MaceEldner", "ShieldFlametal", "heavy"), ("spear", "SpearSplitner", "ShieldFlametal", "heavy")],
        "stealth": ("knife", "KnifeSkollAndHati", "light"),       # no flametal knife exists
        "creatures": ["Charred_Melee", "Morgen", "Asksvin", "BlobLava"],
        "big": ["Morgen"],
    },
}
SKITTISH = {"Lox", "Boar", "Neck", "Deer"}


def loadout(biome, build, weapon, shield, armor_set, b):
    name = f"cal-{biome}-{build}"
    items = [{"prefab": weapon, "quality": 2, "equip": True}]
    items += [{"prefab": a, "quality": 2, "equip": True} for a in b["armor"][armor_set]]
    if shield:
        items.append({"prefab": shield, "quality": 2, "equip": True})
    write("loadouts", name, {"name": name, "tier": biome, "build": build, "meals": b["meals"], "items": items})
    return name


def scenario(name, biome, lo, creature, bot="fighter", **extra):
    data = {"name": name, "runs": RUNS, "timeout": 150, "bot": bot, "seed": 1, "biome": biome, "timeScale": 3,
            "gear": {"loadout": lo}, "foes": {"prefab": creature, "stars": 0}, "variants": ["vanilla"]}
    if creature in SKITTISH:
        data["fearless"] = True
        data["aggro"] = True
    data.update(extra)
    write("scenarios", name, data)


total = 0
for biome, b in BIOMES.items():
    if ONLY and biome != ONLY:
        continue
    melee = [(build, loadout(biome, build, w, sh, arm, b)) for build, w, sh, arm in b["melee"]]
    sb, sw, sarm = b["stealth"]
    stealth = loadout(biome, "stealth-" + sb, sw, None, sarm, b)
    groups = [melee[i:i + 3] for i in range(0, len(melee), 3)]           # 3 builds per fight (3 lanes)
    for c in b["creatures"]:
        for gi, g in enumerate(groups):
            lanes = [{"loadout": lo, "label": f"{biome} {build}"} for build, lo in g]
            scenario(f"cal-{biome}-{c}-melee{'abc'[gi]}", biome, g[0][1], c, lanes=lanes, autoRuns=True)
            total += 1
        # stealth: the creature starts unaware and facing away; the rogue sneaks up and backstabs, then dodges
        scenario(f"cal-{biome}-{c}-stealth", biome, stealth, c, bot="rogue", opener="sneak", autoRuns=True)
        total += 1
    # stars ladder: stars do more than Valheim's health/damage scaling (fewer staggers, longer fights); these fights
    # let the model learn per-star factors. Sword at 1 and 2 stars, a second build at 2 stars; big creatures: team climb.
    second = melee[-1]
    for c in b["creatures"]:
        if c in b["big"]:
            continue
        lanes = [{"loadout": melee[0][1], "foes": {"prefab": c, "stars": 1}, "label": f"{biome} {melee[0][0]}"},
                 {"loadout": melee[0][1], "foes": {"prefab": c, "stars": 2}, "label": f"{biome} {melee[0][0]}"},
                 {"loadout": second[1], "foes": {"prefab": c, "stars": 2}, "label": f"{biome} {second[0]}"}]
        scenario(f"cal-{biome}-{c}-stars", biome, melee[0][1], c, lanes=lanes, autoRuns=True)
        total += 1
    sword = melee[0][1]
    for c in b["big"]:
        lanes = [{"loadout": sword, "bot": bot, "label": f"{biome} sword ({bot})"} for bot in ("twig", "fighter", "veteran")]
        scenario(f"cal-{biome}-{c}-skill", biome, sword, c, lanes=lanes, autoRuns=True)
        scenario(f"cal-{biome}-{c}-team", biome, sword, c, difficulty=True, autoRuns=True)
        total += 2
print(f"calibration: {total} scenarios" + (f" ({ONLY})" if ONLY else ""))
