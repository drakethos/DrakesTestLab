# DrakesTestLab

## Unreleased

- **Icon.** Same brass knot as the other mods: DrakeMods, Test Lab, the health mead, the portrait, and "Valheim Mod QA tools". Version stays 0.5.1 until the next release.

## 0.5.1

- README: documents the 0.5 timing, load-time, frame-time and memory reports. No code change.

## 0.5.0

- **Timestamps everywhere.** Every log line in a test is stamped `[+3.2s]` from the start of that test. The reports (JSON, Markdown, HTML) show when each test started, how long its setup and each step took, how long the whole run took, how long the game took to reach the run, and how long the world took to be ready. The HTML report has a Timing section: slowest tests, slowest steps, mod load times.
- **Mod load times.** The report lists how long each mod took to load. Install the new optional preloader patcher (it ships in the package as `BepInEx/patchers/DrakesTestLab/DrakesTestLab.Preload.dll`) and every mod is timed, the very first included; without it only the mods that load after DrakesTestLab are listed individually, and the report says so. The last mod is timed to the end of chainloading, not to a frame later.
- **Frame times.** Every frame is recorded while tests run. Each test and step reports average fps, 1% low fps, the 95th and 99th percentile frame time, the worst frame and the number of hitches (frames over 50 ms). The HTML report lists the roughest tests and the steps with the worst single frames.
- **Memory.** Each test and step reports managed heap (start, end, peak), total memory, an allocation figure (churn) and GC counts per generation; the HTML report lists heap growth and the tests that allocate the most.
- **Object census (optional).** `Automation/CountObjects` counts every Unity object before and after each test and names the types that grew, for leak hunting. Off by default because it costs about a second per test.
- `Run-TestLabSuites.ps1` prints seconds per test, the run's time split, the roughest tests by fps, the biggest heap growers, the current memory and the slowest mod loads.
- Faster start-up: the settings are saved once instead of once each, and the optional Libs message-card hook is found without scanning every loaded type (about 1.1 s down to 0.5 s).
- Meta tests M-38 (timing), M-39 (frame times) and M-40 (memory).

## 0.4.6

- **The authoring guide and chat prompt now ship inside the mod** and are copied to `BepInEx/config/DrakesTestLab/docs/` at startup, so an AI assistant that cannot open GitHub pages can read them from disk. The first time, the lab logs where they are and the `raw.githubusercontent.com` links to give an assistant that can open links but not files, and writes the same note to `docs/READ-ME.txt`.

## 0.4.5

- **`uiHover` and `uiRelease` steps.** Rest the game's own virtual mouse (written into Unity's Input System: no operating-system mouse moves, nobody has to be at the PC) on a piece of UI, so tooltips and hover states open exactly as for a player. Pick the target by `icon` (an item prefab: the slot that draws its icon), `text`, `name`, or `x`/`y` (`fx`/`fy` as fractions of the screen). `until` waits for a text and tries every matching slot in turn (the hotbar and the inventory can draw the same icon); `nth`, `where`, `wait` and `hold` tune it. The step log says what the pointer is over.
- **`screenText` check.** Reads every text the UI shows right now (TextMeshPro, Text and UI Toolkit, rich text removed) and tests it with `contains`, `notContains` or `regex`; `under` limits it to objects whose path contains that text. With `uiHover` it reads any mod's tooltip, including UIs that draw their own (SeneaL UI).
- **Remote steps rewrite tags inside a hook's `args`.** A step tagged `"on": "player2"` now also turns `args.target` / `args.to` into prefab plus position, so a hook that takes its object from `args` finds it on Player 2.
- New meta tests M-37 (hover + read a tooltip) and N-13 (a hover with nothing to hover fails).
- **Fixed: `relog` lost its tagged objects.** After a relog the lab looked for each tagged object the moment the player was in, before the world had loaded it, so a saved chest was reported gone (Meta test M-28 failed on 0.4.3 and 0.4.4). It now looks for up to 8 s, and a miss logs where the object stood and how far the nearest one is.

## 0.4.4

- **`click` on UI Toolkit windows:** `"nth"` picks one of several elements with the same label (the up arrow on every row of a list); text is matched without rich text tags (a button drawn as a colored square plus a name is found by the name); and a disabled button now fails the step instead of being clicked, as a player could not click it.
- **`regex` compare field:** `hook` steps and checks can test the whole answer against a pattern, for readings like `40x34/12` where a range is part of the claim.
- **Cases can bring virtual second players:** a case's `peers` list (`name`, `dx`, `dz`, `proxy`) joins them when the case is applied, and the case's commands run after they have arrived.
- **Other mods can add a toolbox tab:** `DrakesTestLab.TestLabTabs.Register(title, group, draw)`, called by reflection, so a mod needs no reference to the lab.
- **Docs:** `ai/CHAT-PROMPT.md` now gives raw.githubusercontent.com links to the authoring guide, for assistants that can open links but not GitHub's `/blob/` pages or attachments.

## 0.4.3

- **`face` and `lookAt` can tilt the view.** New optional fields: `up` / `down` / `dy` (metres above or below the aim point), `left` / `right` (metres sideways), `pitch` (degrees, + looks up), `yawOffset` (degrees, + turns right) and, with `x`/`z`, an absolute `y` aim height. The view tilts like the mouse, third-person camera included (before, `face` only turned the player and always levelled the view). `lookAt` with a tilt holds the view a few frames instead of waiting for the crosshair to hover the target, so `{ "do": "lookAt", "target": "tree", "up": 1.5 }` aims 1.5 m up the trunk.
- A plain `face` (target, `x`/`z`, `dx`/`dz`, no tilt field) behaves exactly as before: existing tests are unchanged.
- New hook `testlab.cameraPitch` (`pitch`, `tolerance`): reads the real camera's angle, to prove a tilt. New meta test M-36; the Meta suite now requires 0.4.3.

## 0.4.2

Server control, server queries and three-way asserts. Everything here needs DrakesTestLabServer 0.1.1 with `[Control] Enabled = true` in its config.

- **New step `server`.** Player 1 sends a command to the dedicated server: `admin.add/remove/list/is`, `permit.*` (whitelist), `ban.*` (blacklist), `kick`, `players`, `config.get/set` (any BepInEx setting, with an optional `"force": true` that flips the value first so a setting set to its current value still syncs), `hook.call/list` (test hooks of mods on the server) and `server.quit`. `"who": "me"` means Player 1. Only Player 1 may control the server; Player 2 is refused.
- **New step `serverConfig`.** Sets a setting on the server and waits until Player 1 (and Player 2) report the new value, saying how long each took. A setting that never arrives fails the step with the value each client still has.
- **New step `agree`.** Reads one value from Player 1, the server and Player 2 and requires them to match (`"expect"` or `"equal": true`). Reads: `config`, a server query (`"cmd"`), or a hook.
- **`read` step** gets `config`, `count` and `owner` reads, as seen by that client.
- **Server queries:** `server.info`, `sync.info`, `znet.count/peers/peer`, `world.zdocount/time`, `zdo.count/owner/describe/value`, `rpc.count/last/reset`.
- **`setconfig` with `"asPlayer": true`** changes the value a player's client actually uses (the value the server last sent) instead of its own local value.
- **Config checks read the effective value** (what the mod really uses), not the local file value; `"local": true` reads the file value.
- Test suites `DrakesTestLab-Server`, `-ServerAsserts` and `-ConfigSync` need 0.4.2. Verified with two real clients on a dedicated server: all pass.
- Known: Jotunn "admin only" settings (for example DrakesServerTeams) are sent when a client connects or its admin status changes, not when the server later changes the value. The server still enforces them. ServerSync settings (LockSmith, Renameit) sync live.

## 0.4.1

- **Fixed: a failed check on Player 2 was reported as a pass.** A `check` step, or a `hook` step with `expect`, records a soft assertion. On Player 2 each command runs in its own context that nothing read, so a failed check there came back to Player 1 as "ok". A failed check on Player 2 now fails the command, and Player 1's step shows the reason. If you wrote tests with `"on": "player2"` on 0.4.0, run them again: checks that ran on Player 2 may have passed when they should not have.
- The `DrakesTestLab-Player2` suite now requires 0.4.1.
- Verified with two real clients on a dedicated server (a Game Pass client as Player 1 and a Steam client as Player 2): the lab's own Player 2 suite, LockSmith (a stranger is refused a private chest and gets in once it is public) and DrakesServerTeams (two real players join different teams and each reads the other's team) all pass.

## 0.4.0

Server mode and a real Player 2. The lab can now test against a dedicated server with two real players. The server half is a separate package, **DrakesTestLabServer** (server only), released on its own `server-v...` tags.

- **Server connect mode.** `[Bootstrap] Target = Server` joins the dedicated test server instead of building a local world. The server owns the world. Set the server's public `IP:port` in `[Connect] Address` and its password in `[Connect] Password`.
- **Joins through PlayFab on every platform.** The game's own join-by-IP picks Steam on a Steam client, which a crossplay (PlayFab-only) server never answers (error 5003). The lab looks the server up on PlayFab by its public IP and joins it the way the game's `-joincode` option does. A Game Pass / Xbox client only speaks PlayFab, so the server runs with crossplay on.
- **A failed connect fails cleanly.** If the server cannot be reached the lab writes a failed result (`ServerConnect/Connect`, assert "connected to the test server"), shows the reason, and closes the game (`[Connect] QuitOnFailure`, default on; off brings the main menu back). An unattended `-testlab-run` exits with code 1.
- **Player 1 and Player 2.** `[Connect] Role` is `Player1` (default, runs the test cases) or `Player2` (runs no tests, only does what Player 1 sends). The server gives each role to one client.
- **`"on": "player2"`.** Any step in a test case can be marked `"on": "player2"` to run on the real second client, through the server. A tagged object named in such a step is sent as its prefab plus Player 1's position, so Player 2 finds the same object. New `ping` step: checks Player 2 is connected and names it.
- **`zdoOwner` check:** `"expect": "other"` passes when the owner is some player who is not Player 1 (a real Player 2 has the same character name as Player 1).
- **New suite `DrakesTestLab-Player2`:** Player 2 answers a ping, runs steps on its own character, opens Player 1's chest (the chest opens and changes owner) and is refused a private chest.
- Needs the same game version and the same mods on the server and on both clients; the server refuses a client without them ("incompatible version" in the game).
- Design and test order: `docs/MULTIPLAYER.md`.

## 0.3.2

- **Fixed:** the shipped test world went missing when the mod was installed through Gale (or r2modman). Mod managers flatten every file that is not under a `plugins/` folder, so `Assets/Presets/templates/...` arrived as loose files and the mod reported "The shipped test world is missing". The package now keeps the DLL and `Assets/` under `plugins/`, which installs to the same `plugins/DrakeMods-DrakesTestLab/Assets/...` path as before.
- The release build now simulates Gale's install rules and fails if the world would not land with its folders intact.

## 0.3.1

- New screenshots of the 0.3 features in the README, the Hexium page and the wiki: a recording in progress, the recorded steps, a saved case, Player 2 in the world, the manual-check bar.
- `screenshot` step: `"clean": true` leaves the lab's Testing banner out of the picture. Meta test M-35 takes the feature pictures.

## 0.3.0

Record tests by playing them, save the game as a case, a cleaner lab window, and a second player without a second game.

- **Record tests:** F9 > Tests > Record turns what you do into test steps (crafting, building, pressing E, inventory clicks, dropping, chest moves, typing, console, menu clicks, setting changes, play-as) and saves them as a test, starting from a saved setup of the game as it was. Add checks while you play: what you look at (F7), your inventory (Shift+F7), screenshots (Ctrl+F7), pause (Alt+F7).
- **Save as case:** F9 > Cases saves the current game as a case: dials, modes, who you play as, inventory (with crafter and item data), skills, wards, the buildings around you (chests with contents, signs with text) and the mod settings you changed. New case fields `pieces` and `configs`; items gain `crafter` and `data`.
- **New toolbox layout:** tabs grouped down the side (Play, Scenario, Tests, Tools), cards instead of long pages, status chips (REC, Testing, Playing as), clearer checkboxes, a resizable window. Lab is now Rules.
- **Lab bar:** while recording, at a manual check or in step mode, a small bar at the top of the screen has the buttons and keys; the big window can stay closed. With the window open (F9) the bar moves to the bottom and its buttons can be clicked (Pause, Stop & save). Checkpoints no longer open the toolbox.
- **Window:** an Opacity slider in the header (`[Dev] UiOpacity`) to see the game through it; scrolling the window no longer zooms the camera.
- **Virtual players:** a second client in the test world without a second game. `peerJoin` connects Bjornita (test player 2, the new `[Automation] Player2`) or anyone through the game's own peer code, with a character she owns. `peerRpc` sends RPCs as her client (forged player ids and sender ids too), `peerReceived` checks what the server sent her, `zdoOwner` / `peerOwn` / `peerSet` / `peerMove` / `peerLeave` cover ownership, object data and leaving. Her client answers RPCs for objects she owns with the same mod code (proxy), so owner round trips complete. F9 > Wards > Player 2 connects her by hand.
- **Bjornita** (id 100005, female) joins the test roster: play-as, wards, cases and the Wards tab.
- `-Suites` / `-testlab-run` also take single tests: `"Suite/TestPrefix"` (start of the test name or file name), for quick checks.
- New steps and options: `take`, `teleport` `yaw`, `interact` `asPlayer`. Settings a test's setup changes now last only for that test. Cases move you to a near spot instantly and wait for a far teleport to land.

## 0.2.2

- `click` also works on UI Toolkit elements that are neither buttons nor toggles (DrakeModsLibs switches, cards): it sends a real click to the matching text.

## 0.2.1

- **Meta suite:** `DrakesTestLab-Meta` ships with the mod: about 40 tests that exercise every step and check against plain Valheim, readable as worked examples.
- **Negative tests:** `expectFailure` on a test (true, or the text the failure must contain) passes only when the test fails. Used to prove checks catch problems.
- **New:** `store` step (put a stack into a chest), `crafter` on `hasItem`, `submit` on `type` (confirms the game's text prompt), `station` on `craft` (real-cost craft at a placed station), `exact` on `uiVisible`, `type` `"field": "*"` for the game's own prompt, `testlab.keyHeld` hook, `launchConfig` in suites (settings read only at game start, applied by the run script).
- **Boot:** the shipped test world is checked after it is copied, found even when a mod manager nests the package one folder deeper, and copied again once if it went missing. The "world not found" warning is no longer logged every frame (every few seconds, with what the folders contain). An on-screen line says what is happening (loading the test world; baking a new template takes minutes; an old .fwl/.db template rebuilds on first load), and if the world cannot be loaded the main menu comes back with the reason instead of an empty screen.
- **Fixed:** a test that ran past its `timeout` during its last step was reported as passed. `lookAt` stood at your old height (below a raised floor, "too far" to use the piece) and measured from the piece's middle instead of its position; it now stands on the floor there, waits for a steady hover, and walks round to another side when a wall is in the way. `crosshair` also reads UI Toolkit hover panels (DrakeModsLibs' world hover). Windows are matched topmost first.

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
