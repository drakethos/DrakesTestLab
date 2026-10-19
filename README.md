# DrakesTestLab

A **QA bench for Valheim modders**. Launch the game and it skips the menus and drops you into a throw-away test world as a brand-new character, standing in a flat, cleared arena next to a house that holds every crafting station. Press **F9** for a toolbox that bends the game's rules, so you can test your mod in seconds instead of minutes. Quit, relaunch, and everything is back to a clean slate.

Part of the **DrakeMods** collection. **Full documentation: [the wiki](https://github.com/drakethos/DrakesTestLab/wiki)**: every toolbox tab, the test case format with every field, and every config key. It is a developer tool: you do not need it to play, and it is not meant for a normal save.

> **Your real worlds and characters are never touched.** The test world is called `TestLab` and the character `TestBjorn`. Both are deleted and recreated on every launch.

> ## First release (0.1.0)
> Back up anything you care about, and if something misbehaves please [open an issue](https://github.com/drakethos/DrakesTestLab/issues) with your `BepInEx/LogOutput.log`.

![The test arena: the roofed house with every crafting station, fire pits under the cauldron, ketill and spit](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/arena-house.jpg)

---

## What it can do

- **Instant start:** straight into the world, no menu, no intro cinematic.
- **Fresh every time:** a ready-made world is copied in on each launch (no world generation wait) and a clean character is created (no gear, no skills).
- **Test arena:** the area around spawn is cleared and levelled (the spawn standing stones stay), with a roofed house containing a workbench, forge, stonecutter, artisan table, black forge, galdr table, chest, bed and hearth, plus a cauldron, mead ketill and cooking spit standing over lit fire pits.
- **Super stations:** one switch makes every crafting station level N and ignores the roof and fire rules. A small helper next to the crafting window and build menu has station level `+` / `-`, a recipe **cost multiplier** (free crafting), and a **Give materials** button for the selected recipe or piece.
- **Rules you can bend:** carry weight, health / stamina / eitr, run speed, jump height, fall damage (never hurt by a height you can jump), the vanilla "mystical force" no-build zones (keep, shrink, grow or remove them), recipe and piece cost.
- **Discovery:** unlock every item, recipe and station, or only up to a chosen biome, or forget everything again.
- **Bosses:** mark bosses defeated with their world key, guardian power and trophy, or hook the trophy into the nearest boss stone.
- **Ward test bench:** spawn a ward and set its owner, on/off and permitted players, or **play as** Alice, Bob or Carol (or any name) to see exactly what a stranger, an owner and a permitted player can do. Ready-made scenarios: stranger's ward, I'm permitted, ward off, overlapping wards.
- **Share the world:** because the game logs straight in, the open / public / password choices come from the config (`[Server]`) and the Session tab, so friends can join the test world through Steam.
- **Fires:** one button refuels every fire and fuel-driven cooking station, or keeps them topped up.

![Cauldron and mead ketill standing over lit fire pits, cooking spit alongside](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/fire-stations.jpg)

![Crafting window with the Test helper: station level, cost multiplier, give materials](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/craft-helper.png)

---

## The toolbox (F9)

| Tab | What is in it |
|---|---|
| **World** | time-of-day slider and freeze, weather, random events, world keys, map, game speed |
| **Player** | god / ghost / fly / no build cost, infinite stamina, kits, skills, teleport |
| **Lab** | station level, cost multiplier, carry weight, vitals, speed and jump, discovery, arena, no-build zones, fall damage, fire fuel, bosses |
| **Cases** | JSON test cases (below) |
| **Wards** | the ward test bench and play-as |
| **Spawn** | creatures, items and pieces by search; give to inventory; kill / tame / clear |
| **Inspect** | what you are looking at (prefab, components, health, ZDO), copy prefab name, destroy it |
| **Mods** | every loaded plugin with version and GUID, reload all mod configs |
| **Log** | the BepInEx log live, filtered by mod, errors first, click a line to copy it |
| **Session** | reset world and character, quit, console, startup options |

![Lab tab: station level, cost, carry weight, vitals, discovery, arena](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/toolbox-lab.png)

![World tab: time of day, weather](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/toolbox-world.png)

![Spawn tab: search creatures, items and pieces](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/toolbox-spawn.png)

![Wards tab: play as Alice, Bob or Carol, spawn and configure test wards](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/ward-bench.png)

![Inspect tab: what you are looking at](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/toolbox-inspect.png)

**F10** toggles a small overlay with FPS, position, biome, time and health. Hotkeys and UI scale are in the config.

---

## JSON test cases

A test case is a small JSON file that sets the game up exactly how you want it: time and weather, the arena, rules, vitals, discovery, items (with quality, optionally equipped), skills, bosses, wards, who you play as, creatures to spawn, and any console commands. Drop it in `BepInEx/config/DrakesTestLab/cases/`, apply it from **F9 > Cases**, or set `[Case] OnLoad = <name>` in `com.drakemods.testlab.cfg` to apply it on every launch.

```json
{
  "name": "smithing-test",
  "arena": "build",
  "stationLevel": 7,
  "costMultiplier": 0,
  "carryWeight": 9999,
  "discover": "tier:3",
  "items": [
    { "prefab": "Hammer" },
    { "prefab": "Iron", "count": 200 },
    { "prefab": "SwordBronze", "quality": 2, "equip": true }
  ],
  "wards": [ { "owner": "Alice", "enabled": true, "dz": 12 } ]
}
```

![Cases tab: apply a test case or auto-load one](https://raw.githubusercontent.com/drakethos/DrakesTestLab/main/docs/images/cases-tab.png)

Eight example cases ship with the mod and are copied into that folder on first run, with a field reference (`README.md`). The complete format is on the wiki: [Test cases](https://github.com/drakethos/DrakesTestLab/wiki/Test-Cases). Every field is optional, so a case only changes what it names.

---

## Configuration

Every key, its default and what it does: [Configuration on the wiki](https://github.com/drakethos/DrakesTestLab/wiki/Configuration).

`BepInEx/config/com.drakemods.testlab.cfg` (changes hot-reload):

- **Save mode:** `EphemeralFresh` (default, new world and character every launch), `EphemeralReuse`, or `Persistent`
- **Fresh character:** on by default; turn off to use the bundled template character instead
- **Arena:** `Spawn` (level and clear around spawn, keep the standing stones) or `FindFlat`, radius, keep-radius, house offset, build the house on or off
- **Rules:** no-build zones (`Vanilla` / `Off` / `Scaled`), fall damage, auto-safe falls
- **Dev:** toolbox and overlay hotkeys, UI scale

---

## Good to know

- Cheats and dev commands are switched on for you in the test world; that is the point of the tool.
- **Reset** (Session tab, or logging out) relaunches the game so you always return to a clean world.
- The shipped world is a pre-generated world folder with seed `TestWorld`. To regenerate it, use **Session > Re-bake blank world template**: the game generates a blank world, saves it as the template and quits.
- Requires only **BepInEx Pack for Valheim**: no Jotunn, no other library, no other DrakeMods mod. It changes nothing that is synced over the network, so **friends who join your test world do not need the mod installed** (they do need the same Valheim version and any mods that change the game world).

---

## Building from source

Open `DrakesTestLab.sln`, restore NuGet, copy `environment.props.example` to `environment.props`, build. Releases are built by the GitHub workflow and published to Hexium.
