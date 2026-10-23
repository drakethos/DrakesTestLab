# DrakesTestLab

## 0.2.0

**Automated tests.** Suites of JSON tests that set up the world, act like a player, check the result and write a report, run from F9 > Tests, step by step, or unattended from the command line.

- **Suites:** one folder per mod (`config/DrakesTestLab/suites/<Suite>/`) with required mods, a config baseline, shared setup and one file per test. Ships a self-test suite, a commented `_TEMPLATE` and a function library (`_lib/valheim.json`).
- **Acting like a player:** `craft` (the game's crafting code, real owner; `by` crafts as someone else), `place` (builds with the hammer: build menu, materials unless free, you as creator), `give`, `drop`, `interact`, `itemClick` (inventory clicks with Shift/Ctrl held), `holdKey` / `pressKey` (keys the game and mods read as real), `lookAt` (aims the crosshair), `click` / `toggle` / `type` (Unity UI and UI Toolkit windows, fields found by their title), `playAs` (Alice, Bob, any name), `setLanguage`, `relog` (log out and load the same world back: persistence), plus teleport, face, wait/until, spawn, console, case, invoke, setConfig (this test only), screenshot, checkpoint.
- **Checks:** items and custom data, owner, objects, chests, saved values, windows, UI text, enabled buttons, hover and crosshair text, on-screen messages (including Libs message cards), unlock pop-ups, log lines, no errors per mod, config, skills, stats, ward access, test hooks. Checks can also sit between steps (`check` step, `expect` on hook steps).
- **Test hooks:** mods expose functions to tests with their own `[TestHook("name")]` attribute (found by name, no dependency) or `DrakesTestLab.TestHooks.Register`.
- **Reuse:** step blocks and functions with parameters and their own asserts (`{ "do": "craftAndCheck", "prefab": "Hammer" }` or `"craftAndCheck(\"Hammer\")"`), `include` for shared baselines and libraries, case files.
- **Results:** `results/latest.html` (every test, check, step log and screenshot), `latest.json` / `latest.md`, saved after every test. Screenshots Off / Failures / Always (proof); older runs cleaned up (`KeepRuns`). A requested suite that is missing fails instead of passing silently.
- **Tests tab:** run all / suite / test, re-run failed, stop, step mode, delay, screenshot mode, details per test, test hooks found, UI picker (hover a button, copy its name).
- **Unattended:** `[Automation] RunOnLaunch`, or command-line `-testlab-run <suites>` with `-testlab-quit` / `-testlab-stay` / `-testlab-attended` (manual suites) / `-testlab-delay` / `-testlab-screenshots`; exits 0 (passed) or 1. The repository's `tools/Run-TestLabSuites.ps1` launches a run through Steam and reads the results (not part of the mod).
- **Quiet switches** (F9 > Lab > Noise, `[Rules]`, per test case): hide unlock pop-ups and raven tutorials (still recorded for checks), turn auto-discovery off.
- **AI assistants:** the repository's `ai/` folder has an authoring guide, a Claude Code skill, a Cursor rule and a chat prompt for writing suites and test hooks.
- Config reload no longer warns about mods that never wrote a config file.

Known: saving results when the game is closed in the middle of a run is in place but not yet seen working.

## 0.1.1

- Removed the "hard reset" that relaunched valheim.exe (it started another process and could come back without your mods). Reset now always returns to the main menu inside the running game and loads a fresh world.
- Removed the `ValheimExePath`, `SaveBeforeRestart` and `ReloadInProcess` config options that went with it.
- The Cases and Mods tabs copy folder and log paths to the clipboard instead of opening them. The mod no longer starts any program.

## 0.1.0

First release.

- Fast boot into a throw-away test world and a brand-new clean character, recreated on every launch (`EphemeralFresh`; `EphemeralReuse` and `Persistent` also available)
- Pre-generated Valheim 1.0 world template, so there is no world generation wait; re-bake it from the Session tab
- Test arena: levels and clears the spawn area (standing stones kept), builds a roofed house with every crafting station, and puts the cauldron, mead ketill and cooking spit over lit fire pits
- Toolbox (F9): World, Player, Lab, Cases, Wards, Spawn, Inspect, Mods, Log and Session tabs; F10 info overlay
- Super stations, recipe cost multiplier, carry weight, vitals, speed and jump, fall damage (auto-safe), vanilla no-build zones (vanilla / off / scaled), discovery by biome, boss progression, fire fuel
- Crafting helper beside the crafting window and build menu (station level, cost, give materials)
- Share the test world with friends: `[Server]` config and Session tab (open to friends, public listing, crossplay, password), applied automatically because the game logs straight in
- No Jotunn or other library dependency; guests joining a shared test world do not need the mod
- In-process reset (back to the menu inside the running game) so mods stay loaded; test cases no longer rebuild the house on top of itself
- Test cases can reset the test state first (default on), so they never stack
- Ward test bench with fake owners, on/off, permitted players and ready-made scenarios
- JSON test cases, applied from the Cases tab or automatically on load; eight ship with the mod (ward-amy-owner, play-as-amy, builder-sandbox, bronze-age-start, boss-progress-all, vanilla-baseline, smithing-test, combat-dummies)
- Fixed for Valheim 1.0: save paths, world folder format, profile loading, console command binding, enum/stub mismatches
