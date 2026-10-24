# Writing DrakesTestLab tests (guide for AI assistants and people)

Give this file to an AI assistant (Claude, Cursor, ChatGPT...) together with the mod you want to test, or read it yourself.
It is everything needed to write test suites and test hooks for a Valheim mod with DrakesTestLab.

## What DrakesTestLab does

A BepInEx mod for Valheim. On launch it loads a reset test world with a fresh character, then runs **suites** of JSON
tests: it sets up state, performs steps (spawn, craft, give, play as another player, interact, click UI, call mod code),
checks results, and writes a report with screenshots. It can run unattended from the command line and exit with a code.

## Where things live

```
BepInEx/config/DrakesTestLab/
  suites/<SuiteName>/suite.json      the suite: required mods, config baseline, shared setup
  suites/<SuiteName>/<test>.json     one file per test (alphabetical order unless suite.json lists "tests")
  suites/<SuiteName>/_blocks/*.json  reusable step blocks for that suite (subfolders are never read as tests)
  suites/<SuiteName>/_functions/*.json  the same, for functions
  suites/_lib/valheim.json           shipped function library (craftAndCheck, equipAndCheck, spawnPiece, as)
  suites/_Shared/*.json              anything several suites "include": blocks, config baseline, setup
  suites/_TEMPLATE/                  every step and check with comments (folders starting with _ are ignored)
  cases/<name>.json                  reusable starting states ("test cases"); a suite or test can apply them
  results/latest.html                the report (open in a browser): checks, step log, screenshots
  results/latest.json, latest.md     the same for scripts
  results/run-<stamp>/*.png          screenshots of that run
```

Keep the suite source in the mod's own repository (`Tests/suites/<Suite>/`, never packaged into the mod) and copy it into
the profile's `config/DrakesTestLab/suites/` to run. (DrakeMods builds do that copy automatically after each local deploy.)

## suite.json

```json
{
  "name": "MyMod",
  "description": "What this suite covers.",
  "requireMods": [ { "guid": "com.me.mymod", "minVersion": "1.0.0" } ],
  "configOverrides": [
    { "guid": "com.me.mymod", "section": "General", "key": "LockToOwner", "value": "true" }
  ],
  "setup": { "timeOfDay": 0.5, "freezeTime": 1, "god": 1, "noBuild": "off", "hideUnlockPopups": 1 },
  "resetBetweenTests": true
}
```

- `configOverrides` = the baseline every test starts from. Set **every** setting the tests depend on, so a player's own config cannot change results. Restored after the suite.
- `setup` uses the test-case format (items, skills, time, god mode, rules, `case`...). `resetBetweenTests` resets inventory, skills, discovery, boss keys, wards, identity and rules before each test.
- `blocks` / `functions` = reusable steps (see [Blocks](#blocks-reusable-steps) and [Functions](#functions)). `include` pulls in other files (see [Include](#include-share-between-suites)).

## Blocks (reusable steps)

When several tests repeat the same steps, make them a block and call it with a `use` step:

```json
// in suite.json, or one file per block in _blocks/<name>.json ({ "params": ..., "steps": ... } or a bare [ steps ] array)
"blocks": {
  "writePage": {
    "params": { "desc": null, "blanks": 1 },          // defaults; a param not listed here is required
    "steps": [
      { "do": "give", "prefab": "Drakes_PieceOfPaper", "count": "{{blanks}}" },
      { "do": "hook", "name": "mymod.edit", "args": { "op": "name", "text": "{{text}}" }, "expect": true, "stop": true },
      { "if": "{{desc}}", "do": "hook", "name": "mymod.edit", "args": { "op": "desc", "text": "{{desc}}" }, "expect": true }
    ]
  }
}
```

```json
{ "do": "use", "block": "writePage", "args": { "text": "Hello", "desc": "Page text" } }
{ "do": "use", "block": "writePage", "text": "Hello" }      // args can also go straight on the step
```

- `{{name}}` is replaced in every string of the block. A string that is **only** `"{{name}}"` takes the value as it is (a number, `true`, an object), so `"count": "{{blanks}}"` stays a number.
- A param that defaults to `null` is optional: inside text it becomes empty, and a key whose value is only `"{{name}}"` is left out (handy for an optional `label` or `by`).
- `"if"` on any step: false, 0, `""` or null drops the step. Use it with an optional param (`"if": "{{desc}}"`).
- Blocks can `use` other blocks and pass their own params on (`"args": { "who": "{{who}}" }`), up to 10 deep.
- Blocks are expanded when the suite loads: the report shows the real steps, each tagged `block=<name>`. A wrong block name or a missing required param makes that test fail with "could not read the test" and says which.
- Same name in several places: `suite.json` beats `_blocks/`, which beats included files.

## Functions

A function is a block with its own checks, called like a built-in step. Define it under `"functions"` (suite.json, an
included file, or `_functions/<name>.json`; `"blocks"` and `"functions"` are the same map, so either name works anywhere):

```json
"functions": {
  "craftAndCheck": {
    "params": [ "prefab", { "count": 1 } ],     // call order: a plain name is required, { name: default } is optional
    "actions": [
      { "do": "give", "materialsFor": "{{prefab}}", "count": "{{count}}" },
      { "do": "craft", "prefab": "{{prefab}}", "count": "{{count}}", "free": false }
    ],
    "asserts": [ { "check": "hasItem", "prefab": "{{prefab}}", "count": "{{count}}", "label": "crafted {{prefab}}" } ]
  }
}
```

Call it any of these ways:

```json
"craftAndCheck(\"Hammer\", 2)"                          // a string step: JSON arguments in params order
{ "do": "craftAndCheck", "prefab": "Hammer", "count": 2 }
{ "do": "use", "block": "craftAndCheck", "args": { "prefab": "Hammer" } }
```

- `actions` (or `steps`) run first; each of `asserts` then runs as a `check` step labelled `<function>: <label>`, so a failure names the function.
- A built-in step name always wins: a function called `give` can only be reached with `use`.
- A param whose value is a list of steps can be spliced in with a step that is just `"{{steps}}"`; that is how `as` works: `{ "do": "as", "who": "Bob", "steps": [ ... ] }` runs the steps as Bob and switches back.
- A test file can have its own `functions` / `blocks` and its own `include` (only blocks and functions are taken from it).

**Shipped library** `suites/_lib/valheim.json` (include it with `"include": "_lib/valheim.json"`):
`craftAndCheck(prefab, count)` real-cost craft + check, `equipAndCheck(prefab)`, `spawnPiece(prefab, owner, tag, dz)` +
owner check, `as(who, steps)`.

## Include (share between suites)

```json
{ "name": "MyMod-Manual", "include": [ "../MyMod/_shared/baseline.json", "../MyMod/_blocks" ] }
```

- `include` is a path or a list of paths, relative to the file that includes it, else to the `suites/` folder (`"_Shared/drakemods.json"`).
- An included **file** may hold `requireMods`, `configOverrides`, `setup`, `blocks` and its own `include`. An included **folder** is read like `_blocks/`.
- The suite's own values win: a `configOverrides` entry for the same mod/section/key replaces the included one, `setup` is layered field by field on top of the included setups, `requireMods` for the same guid replaces the included one.
- Each file is read once per suite, so include loops and two suites including the same file are fine.
- Typical use: a suite and its `-Manual` suite share one baseline; suites for related mods share blocks from `suites/_Shared/` (folders starting with `_` are never run as suites).

## A test

```json
{
  "name": "TC-202 owner lock",
  "description": "Sheet: TC-202. Others cannot rename my item.",
  "steps": [
    { "do": "craft", "prefab": "AxeStone" },
    { "do": "playAs", "who": "Bob" },
    { "do": "hook", "name": "mymod.canRename", "args": { "prefab": "AxeStone" }, "expect": false, "label": "Bob cannot rename it" },
    { "do": "playAs", "who": "me" }
  ],
  "asserts": [ { "check": "noErrors", "source": "MyMod" } ]
}
```

- Steps run in order; a step that cannot be done fails the test.
- Checks can go anywhere: a `check` step, or `expect` / `contains` / `notContains` on a `hook` step. A failed check fails the test but the steps continue (add `"stop": true` to stop).
- `asserts` run at the end.
- `manualOnly: true` + a `checkpoint` step = a test that asks a person Pass / Fail (skipped in unattended runs).
- `expectFailure: true` (or a text the failure reason must contain) = a negative test: it passes only when it fails. Use it to prove a check catches the problem. Examples: the shipped `DrakesTestLab-Meta` suite.

### Steps

| Step | Fields | Does |
|---|---|---|
| `craft` | `prefab`, `count`, `by` (craft as Alice/Bob/...), `free` (default true; false = needs materials), `station` (tag of a placed station to craft at; needed for real-cost station recipes; roof/fire waived) | Crafts through the game's crafting code: the item gets a **real crafter/owner** |
| `give` | `prefab`, `count`, `quality`, `equip`, `crafter`; or `materialsFor` (+ `count`, `quality`) | Adds items. Without `crafter` they are unowned (like loot). `materialsFor` gives an item recipe's or a building piece's cost (pair with `craft` `"free": false`) |
| `spawn` | `prefab`, `dx`/`dz` or `x`/`z`, `owner`, `as` (tag), `count`, `level`, `yaw`, `keep`; wards: `enabled`, `permitted`, `radius` | Places pieces (with owner), creatures, wards |
| `playAs` | `who`: `me`, `Alice`, `Amy`, `Bob`, `Carol`, any name, `Name:12345` | Become another player (player id + name change; ownership and wards follow) |
| `setConfig` | `values`: `{ "Section/Key": value }` (guid defaults to the suite's first required mod) or `guid`,`section`,`key`,`value` | Changes config **for this test only** |
| `case` | `name` | Applies a case file from `cases/` (reuse complex starting states) |
| `use` | `block`, `args` (or the args straight on the step) | Runs a block's or function's steps in place ([Blocks](#blocks-reusable-steps)); a function can also be called by its own name |
| `hook` | `name`, `args`, `as`, `expect`/`contains`/`notContains`, `label`, `stop` | Calls a test hook the mod exposes |
| `check` | `check` + that check's fields, `label`, `stop`, `screenshot` | A check in the middle of the steps |
| `interact` | `target`, `alt`, `hold` | Press E on an object; the game's answer becomes `lastResult` |
| `click` / `toggle` / `type` | `button` / `name`,`value` / `field`,`text`,`submit` (`field: "*"` = the prompt that just opened) | Drive Unity UI and UI Toolkit windows by name or label |
| `pressKey` | `button` (Use, Inventory, Jump...) or `key`, `frames` | Fake key press the game reads as real |
| `teleport`, `face`, `wait` | position or `target` (teleport: `yaw` to face a way); `seconds` or `until` + `timeout` | Movement and timing |
| `invoke` | `method` (`Namespace.Type.Member`), `args` | Calls a static method / reads a static property; result = `lastResult` |
| `relog` | `timeout` | Persistence: log out and load the same world and character back (no restart); tags re-found |
| `store` / `take` | `target`, `prefab`, `count`, `index`/`match` | Put an inventory stack into a chest / take items out of one |
| `clearInventory`, `closeUi`, `destroy`, `reset` | | Housekeeping |
| `screenshot`, `log`, `checkpoint` | `name` / `message` / `message` | Proof and notes; checkpoint asks a person |

### Checks

`hasItem` (prefab, count, quality, data, crafter: `me`/`Alice`/`none`), `noItem`, `equipped`, `globalKey`, `objectNear`, `owner` (piece creator), `zdo`,
`uiVisible` (text "contains" match; `"exact": true` for whole labels, use it with `expect: false`), `uiText`, `uiEnabled`, `message` (on-screen messages, `kind` Center/TopLeft/Unlock/Tutorial), `unlockShown`,
`hoverText`, `containerHas`, `logContains`, `noErrors` (`source`), `config`, `skill`, `stat`, `position`, `guardianPower`,
`wardAccess`, `lastResult`, `hook`. Compare fields: `expect` (equals, numbers as numbers, case-insensitive), `contains`,
`notContains`, `min`, `max`.

## Test hooks (the mod side)

Most mod rules cannot be reached from JSON alone. A **test hook** is a static method the mod marks so tests can call it.
**The mod needs no reference to DrakesTestLab**: declare your own attribute class named `TestHookAttribute`; DrakesTestLab
finds it by name. Without DrakesTestLab installed the hooks are never called and cost nothing.

```csharp
[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestHookAttribute : Attribute
{
    public TestHookAttribute(string name) => Name = name;
    public string Name { get; }
}

internal static class MyModTestHooks
{
    // args arrive as parsed JSON: string, double, bool, List<object>, Dictionary<string, object>
    [TestHook("mymod.canRename")]
    static bool CanRename(Dictionary<string, object?> args)
    {
        var item = FindItem(args);                    // e.g. by "prefab" in the player's inventory
        return MyPermissions.CanRename(item, Player.m_localPlayer);   // call the REAL rule, not a copy
    }
}
```

Parameters can also be bound by name: `string`/numbers/`bool`/enums from the arg of the same name, `GameObject` from a
tag or prefab, `Vector3` from `x`/`z` or `dx`/`dz`, `Player` = local player, optional parameters use their default.
Return values become `lastResult`; throw an exception to fail the step with your message.

Good hooks:

- **Call the mod's real code paths** (the same permission check and apply method the UI uses). A hook that re-implements the rule tests nothing.
- Pick targets simply: `prefab` + `index` or `match` (display name contains) for inventory items; the nearest object for world pieces.
- Split them into **actions** (`edit`, `share`, `unlock`, returning true/false like the UI would allow) and **reads** (`name`, `owner`, `denial`, returning strings/bools to compare).
- Return short, stable strings (enum names, "a,b") rather than objects.
- If the UI caps or validates input, apply the same cap in the action hook.

## How to write a suite for a mod (workflow)

1. **Read the mod**: its config file (every section and key), its permission/ownership rules, its public API, what it patches. List each behaviour and setting.
2. **List the cases**: one test per behaviour or setting combination. Keep test names starting with an id (`TC-201 ...`) if there is a test sheet, and put the sheet id in `description`.
3. **Baseline**: write `suite.json` with `configOverrides` for every setting the tests depend on.
4. **Hooks**: add the smallest set of hooks that reach the rules (actions + reads). Build the mod.
5. **Tests**: owned items are **crafted** (`craft`, `by` for someone else), never given with a fake owner. Other players = `playAs`. Per-test settings = `setConfig` (auto-restored). Reusable starting states = `case` files. Steps that repeat across tests = functions (`_functions/`, called by name, with their own `asserts`); generic ones are already in `_lib/valheim.json`; a baseline shared with the manual suite = `include`.
6. **Manual suite**: what needs eyes or real hands (window layout, real key presses, language, relaunch, a second real client, other mods) goes into a `<Suite>-Manual` suite with `checkpoint` steps.
7. **Coverage**: write a table mapping every requirement to Auto / Manual / Gap.
8. **Run** and fix: unattended run, read `results/latest.json`, fix tests or report real bugs.

## Running

In game: **F9 > Tests** (Run all / suite / one test, step mode, delay, screenshots Off / Failures / Always).

Unattended (Windows, Steam running, game closed):

```powershell
.\tools\Run-TestLabSuites.ps1 -Suites "MyMod" -Screenshots Always
```

Exit code: 0 all passed, 1 a test failed, 2 could not start, 3 timed out / no results. Or start the game with
`-testlab-run MyMod -testlab-quit -testlab-screenshots always`.

Results: `BepInEx/config/DrakesTestLab/results/latest.html` (report with screenshots), `latest.json` (for scripts).

## Things that trip people up

- In single player the host is an admin. If the mod lets admins bypass rules, turn that off in the baseline or every denial test passes for the wrong reason.
- `playAs` changes who you are for ownership and wards, but there is still only one game client: a second real player's view, server sync and network timing need a manual test.
- `give` without `crafter` = unowned item. Use `craft` when ownership matters.
- Settings the mod only reads at startup or registration (recipes, prefab sizes) may not change live; check them, and mark live-toggle behaviour as manual if needed.
- Copy the suite folder **with** its `_blocks/` / `_functions/` / `_shared/` subfolders (and any `suites/_Shared/` it includes) into the profile. `_lib/` ships with DrakesTestLab.
- Check the mod's own errors with `{ "check": "noErrors", "source": "<BepInEx log source of the mod>" }` in every test.

## Recorded tests and saved cases

Players can record a test in game (F9 > Tests > Record) and save the current game as a case (F9 > Cases). Those files use the same
steps and case fields as hand-written ones (`pieces`, `configs`, item `crafter` / `data`), so treat them as a first draft: rename
tags, drop accidental steps, turn recorded checks into the ones the test is really about.
