---
name: drakestestlab-tests
description: Write DrakesTestLab test suites (JSON) and test hooks (C#) for a Valheim BepInEx mod, run them unattended, and read the results. Use when asked to test a Valheim mod, add regression tests, write test cases from a test sheet, or add [TestHook] methods.
---

# DrakesTestLab tests

Read `TESTLAB-AUTHORING.md` (next to this file, or in the DrakesTestLab repository under `ai/`) before writing anything:
it has the suite and test format, every step and check, how to write test hooks, and the workflow.

Workflow:

1. Read the mod's config bindings, permission/ownership rules and public API. List every behaviour and setting.
2. If the user has a test sheet (spreadsheet / CSV), map each row id to a test; keep the id at the start of the test name.
3. Write `Tests/suites/<Mod>/suite.json` with a `configOverrides` baseline for every setting the tests use.
4. Add a `Testing/<Mod>TestHooks.cs` file with the mod's own `TestHookAttribute` class (no DrakesTestLab reference): action hooks call the real apply code, read hooks return strings/bools.
5. Write one JSON file per test. Craft owned items with the `craft` step (`by` for another player), use `playAs` for other players, `setConfig` for per-test settings, `case` for reusable setups, functions (`_functions/`, called by name like a step or as `"name(\"arg\")"`, with their own `asserts`) for steps several tests repeat, the shipped `_lib/valheim.json` library (`craftAndCheck`, `equipAndCheck`, `spawnPiece`, `as`), and `include` to share a baseline or functions between suites.
6. Put what needs a person into `<Mod>-Manual` with `checkpoint` steps, and write a coverage table (Auto / Manual / Gap).
7. Build the mod, copy the suites into the profile's `BepInEx/config/DrakesTestLab/suites/`, run
   `tools/Run-TestLabSuites.ps1 -Suites <Mod> -Screenshots Always`, then read `results/latest.json` and fix.

Never start or stop the game when the user has it open; the script refuses if Valheim is running.
