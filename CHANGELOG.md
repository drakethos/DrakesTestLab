# DrakesTestLab

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
