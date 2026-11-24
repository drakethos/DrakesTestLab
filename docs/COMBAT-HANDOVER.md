# Combat Lab: handover for the next developer or AI assistant

Read this before changing anything under `Combat/`, `UI/Toolbox/CombatTab.cs`, `Tests/combat/` or `tools/combat_*.py`.
The user-facing manual is [COMBAT.md](COMBAT.md); this file is how it works, why, what is proven, and what is open.
The repo rules in the workshop `CLAUDE.md` (queue every Valheim launch, never change versions unasked, verify API refs,
commit and release only on the user's go) apply to everything here.

## 1. What it is for

A Valheim balance simulator that stays scientifically honest. The user's standing requirements:
- **Real combat**: the game's own AI, attack, block, parry, stagger, damage and food code. No formula-only shortcuts.
- **Human-like bots**, at named skill levels, recorded in every result.
- **Speed-ups only when validated** against normal play (1x, one fight at a time).
- **Avoid thousands of combinations**: calibrate a few hundred fights, model the rest, spot-check the model.
- **A shipped whole-game difficulty database**, with a clear rebuild warning (it takes hours).
- Live play: the user can fight monsters, Bjornita or Bjorn by hand with the same recording.

## 2. Code map

| File | Role |
|---|---|
| `Combat/CombatRunner.cs` | The coroutine. `RunAll` loops scenarios > variants > foods > attacks > counts; four paths: mixed lanes, team climbs, plain runs (with lanes), calibration counts. `Fight` sets one fight up (reset, loadout, lanes, PvP opponent, teammates, countdown, meals), runs it on `WaitForFixedUpdate`, records, resets. Also death patches (`CheckDeath`, `OnDeath`), screenshots, tally, player-count scaling choice, report writing. |
| `Combat/CombatData.cs` | Loadout and scenario models and their JSON readers (MiniJson). `LiveScenarios` / `LiveLoadouts` hold in-memory ones (the Combat tab). `SaveLoadout`. |
| `Combat/CombatBot.cs` | `BotProfile` (twig, trained, fighter, veteran, rogue, you) and `CombatBot`, which drives a `Player` through `SetControls` each physics step. Reads foe attacks, parry timing, block/dodge choice, stamina management, combos, strafing, stealth opener, learning which attacks cannot be blocked (`NoteHit`, `AvoidBlocking`). Real input is held off for driven players; `Manual` profiles are never driven. |
| `Combat/CombatLanes.cs` | Extra bot players (lanes, teammates, the PvP opponent): spawned from the `Player` prefab, owned locally, stepped through `Player.FixedUpdate` with `m_localPlayer` swapped. Patches: foe targeting locks, the `Game.GetPlayerDifficulty` override, sleeping foes woken, arena terrain protected. |
| `Combat/CombatRecorder.cs` | Harmony on `Character.RPC_Damage` (every hit: raw, final, types, blocked, parried, backstab) and `Character.ApplyDamage` (damage-over-time ticks, which bypass RPC_Damage), `Humanoid.BlockAttack`, `StartAttack`, `ConsumeItem`. Dormant unless `Active`. |
| `Combat/CombatArena.cs` | Picks a flat spot ~110 m from the start, clears it, rings it with unbreakable stakes; containment and unsticking. |
| `Combat/CombatPrep.cs` | Real meals (`EatFood`, then `UpdateFood`) and skills (loadout skills, or tier base plus quality steps). Mutes skill-up messages during runs. |
| `Combat/CombatFoes.cs`, `CombatRules.cs` | Per-foe setup (hostile, aggro, fearless) and the raw-data switches (no stagger, no knockback, damage multipliers). |
| `Combat/CombatClock.cs`, `CombatLean.cs` | Faster game clock (fixed delta kept in step), lean rendering. |
| `Combat/CombatStats.cs`, `CombatVerdict.cs`, `CombatReport.cs`, `CombatRecords.cs`, `CombatBaseline.cs` | Summaries (Wilson intervals, settled-check), bands and verdicts, the HTML report, records, baseline merge. |
| `Combat/CombatDifficultyDb.cs` | Rows from `"difficulty": true` scenarios merged into `combat/difficulty-db.json`; `Save` keeps the shipped `model` block. |
| `Combat/CombatGameData.cs` | `-testlab-combat-dump`: weapons, armour, creatures, foods to `combat/gamedata.json` (the model's inputs). |
| `Combat/CombatLive.cs` | Live fights: builds an in-memory scenario from the Combat tab (`bot: "you"`, 1x, countdown 3, no stall check), captures "my current gear" and gives it back when the session ends. |
| `UI/Toolbox/CombatTab.cs` | The Combat tab (Scenario group). Hook `testlab.combatTabPreset` sets it up for pictures. |
| `Tests/gen_combat_*.py` | Generators for loadouts and scenarios (`gen_combat_game.py`: the `game-*` climbs; `gen_combat_calibrate.py`: `cal-*`). |
| `tools/combat_predict.py`, `combat_build_db.py`, `combat_spotcheck.py`, `combat_validate.py` | The model, the DB builder, the spot-check scorer, the speed-up validation statistics. |
| `Tests/combat/` | Source of the shipped loadouts and scenarios (linked into the package by the csproj), plus `results-samples/` (raw calibration results the DB was built from) and `spot-predictions.json`. |

Shipping: the csproj links `Tests/combat/loadouts/*.json` and the `cal-*`, `game-*`, `pvp-*`, `example-*`, `bronze-*`,
`iron-*` scenarios into `Assets/Presets/templates/combat/`; `EnsureShippedCombat` copies missing files into the config.
`docs-shot-*` and `mb-*`/`spot-*` scenarios are dev-only and not shipped.

## 3. Things that were wrong once (do not undo these)

| Problem | Fix | Where |
|---|---|---|
| Lanes made every monster a group monster: `Game.GetPlayerDifficulty` counts all players within 200 m, lane bots included | During fights the lab returns the real count: 1 per lane, the team size in team fights, or the scenario's `playerScaling` | `CombatLanes_Game_GetPlayerDifficulty`, `CombatRunner.ScalingPlayers` |
| Pickaxe damage on creature attacks (Troll, Golem, Morgen, Lava Blob) dug craters until fights stalled | Terrain operations inside the arena are blocked during fights | `CombatLanes_TerrainComp_InternalDoOperation` |
| Sleeping foes (Morgen) never woke: lane players make no noise | Foes are woken at fight start and kept awake | `CombatLanes_MonsterAI_UpdateSleep` |
| A loadout that failed to equip counted as a loss | The fight is voided (nothing recorded) | `Fight`, `GearReady` |
| Poison and burning ticks were not recorded (they call `ApplyDamage`, not `RPC_Damage`) | Second hook on `ApplyDamage` with a depth counter so RPC hits are not counted twice | `CombatRecorder_ApplyDamage` |
| Stalled or timed-out fights counted as losses | They are missing data everywhere (summaries, model, DB) | `CombatStats`, `tools/combat_*.py` |
| A death could arrive late (death animation) and kill the player after the fight | `CheckDeath` is skipped for the gear side during runs | `CombatRunner_Character_CheckDeath` |
| Feeding lanes earlier than the local player left them a point down (food decays) | Everyone eats at the same moment, just before the fight | `Fight` |
| Meals gave Mountain players 99 HP | Meals are 2 health foods + 1 stamina food per biome | `Tests/gen_combat_*.py` |

## 4. What is proven

- **3 lanes at 3x** match 1x single fights on Greydwarf Elite and Troll for win rate, kill time, time to die (Fisher and
  Mann-Whitney tests, fixed counts, ABBA order; `tools/combat_validate.py`). Damage taken reads ~3% low in lanes and ~11%
  low at 3x (p < 0.05). Validated at ~40 fps; faster clocks take bigger steps on slower machines.
- **The model** (`P(win) = logistic(2.75 * ln(lasts / ttk))` with a per-pair anchor at 0 stars and per-creature star
  anchors) held 34 of 36 fresh spot-checks; mean kill-time bias +10%. Misses: Wolf 2-star with a q4 battleaxe, Seeker
  2-star with an atgeir (both model-only cells).
- **The team model** `win(N) = sig(logit(p1) + k_team * ln(N^2 / ((1 + 0.3 (N-1)) (1 + 0.04 (N-1)))))`, `k_team ~ 0.45`,
  fitted on 0-star climbs only, predicted all seven 1-star climbs (">4").
- **Bot learning** (roll instead of block) triggers rarely and changes results within noise.
- **Goblin Brute**: the twig bot beats it more often than the veteran, because nonstop swinging keeps it staggered. Real
  game behaviour, not a bug.
- **Stealth**: one-shot backstabs on Asksvin with high Sneak and a knife (x6 backstab) are real.

## 5. How to work on it

- **Launch only through the queue.** The dev helpers in `~/.local/share/drakes-combat/` do it for you:
  `COMBAT="example-troll-solo" RUNS=0 TIMEOUT=900 queue.sh "<job name>" <tag>`
  queues `combat-run.sh`: build, `verify_valheim_api_refs.py`, copy the DLL and `Tests/combat/` into the clean profile
  `drakeCombat`, run, keep `logs/game-<tag>.log`. Results: the profile's `BepInEx/config/DrakesTestLab/combat/results/latest.json`.
- **Use the clean profile `drakeCombat` for balance numbers**: `drakeTest` has other mods (OmniConfig modlets) that skew stats.
- **Run the fewest fights that prove a change.** A full DB rebuild is hours.
- **Rebuild the DB**: `-testlab-combat "cal-*"` (3 lanes at 3x), merge results into
  `Tests/combat/results-samples/cal-<biome>-1.json` (`merge.py`), `-testlab-combat-dump` for game data, then
  `python3 tools/combat_predict.py` and `python3 tools/combat_build_db.py --teams <team-climb results>`, which writes
  `Assets/Presets/templates/combat/difficulty-db.json`. Spot-check with `tools/combat_spotcheck.py` against fresh fights.
- **Diagnose a stall**: `"trace": true` logs each bot's view every 2 s; the run log line per fight lists blocks,
  parries, punishes, lapses, whiffs, unsticks and opener notes.
- **Any new game member you call** must pass `Verify-ValheimApiRefs` (builds compile against stubs; protected members
  compile and throw in game).
- **New public behaviour** gets a line in `docs/COMBAT.md`, the wiki pages (`Combat-Lab`, `Combat-Scenarios`,
  `Difficulty-Database`) and the CHANGELOG.

## 6. Open work

1. Ranged builds (bows, crossbows, staffs) for the bot; they matter for several biomes' real meta.
2. Bosses (a boss arena, summoning, phases).
3. Extended mode: sweep gear quality, skill and bot skill for full difficulty curves per creature.
4. Creature level mods (CLLC) for stars above 2 and infusions: soft reference, dormant when not installed.
5. More team-climb data at 2 stars, so the team model is checked beyond 1 star.
6. The report could show the difficulty database's row next to each matching cell.
7. Live fights: a tally overlay with per-fight damage numbers, and a "best of N" stop rule.
