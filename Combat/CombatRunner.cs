using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DrakesTestLab.Automation;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Cases;
using DrakesTestLab.Util;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Runs combat scenarios: for every variant, N fights of the gear side (you, driven by a bot) against the foes, with the
/// game's own AI and attack code. Each fight is recorded, summarized, judged against its band and written as
/// combat.json + combat.html. Separate from <see cref="TestRunner"/>; it shares the arena, spawn and reset tech.
/// </summary>
internal static class CombatRunner
{
    public static bool Running { get; private set; }
    public static bool Fighting { get; private set; }
    public static string Status { get; private set; } = "Idle.";
    public static string LastReport { get; private set; } = "";
    public static CombatResult? LastResult { get; private set; }

    /// <summary>One line for the on-screen banner: which fight, how long, both sides' health.</summary>
    public static string LiveLine { get; private set; } = "";
    public static string Scenario { get; private set; } = "";

    private static bool _stop;
    private static bool _gearDied;
    private static float _gearDiedAt;

    public static void Stop() => _stop = true;

    /// <summary>Radius of the cleared, flattened fighting ground around the start spot (foes spawn up to ~20 m out).</summary>
    public static float ArenaRadius => CombatArena.Radius;

    /// <summary>
    /// Copies the combat data shipped with the mod (loadouts, scenarios, the game difficulty baseline) into the config
    /// folder, file by file, never overwriting your edits.
    /// </summary>
    public static void EnsureShippedCombat()
    {
        try
        {
            var shipped = Path.Combine(ValheimPaths.TemplatesFolder, "combat");
            if (!Directory.Exists(shipped)) return;
            Directory.CreateDirectory(CombatData.Folder);
            TestRunner.CopyMissing(shipped, CombatData.Folder);
            foreach (var file in Directory.GetFiles(shipped))
            {
                var target = Path.Combine(CombatData.Folder, Path.GetFileName(file));
                if (!File.Exists(target)) File.Copy(file, target);
            }
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Copying shipped combat data failed: {ex.Message}");
        }
    }

    /// <summary>Starts the named scenarios (a list of file names in combat/scenarios) as one report.</summary>
    public static bool Start(IList<string> scenarios, bool quitWhenDone = false)
    {
        if (Running || TestRunner.Running || TestLabPlugin.Instance == null)
            return false;
        TestLabPlugin.Instance.StartCoroutine(RunAll(scenarios, quitWhenDone));
        return true;
    }

    /// <summary>Override for every scenario's run count (-testlab-combat-runs N); 0 = use the scenario's own.</summary>
    public static int RunsOverride;

    /// <summary>-testlab-combat-check: read every named scenario and check its prefab names, no arena and no fights.</summary>
    public static bool CheckOnly;

    /// <summary>-testlab-combat-dump: write the game's combat numbers (combat/gamedata.json) before the run (with check: instead of it).</summary>
    public static bool DumpGameData;

    /// <summary>Wins and losses of the scenario that is running (the Combat tab and the banner show it).</summary>
    public static string Tally { get; private set; } = "";

    private static string TallyOf(List<RunRecord> runs)
    {
        var won = runs.Count(r => r.Outcome == "win");
        var lost = runs.Count(r => r.Outcome == "loss");
        var last = runs.Count > 0 ? runs[runs.Count - 1] : null;
        var lastText = last == null ? "" : last.Outcome == "win" ? $", last: won in {last.TimeToKill:0.0} s" : last.Outcome == "loss" ? $", last: lost after {last.TimeToDie:0.0} s" : $", last: {last.Outcome}";
        return $"{won} won, {lost} lost of {runs.Count}{lastText}";
    }

    /// <summary>The player count the game's scaling sees, from the scenario's "playerScaling" (auto = <paramref name="auto"/>; 0 = the game counts).</summary>
    internal static int ScalingPlayers(ScenarioSpec sc, int auto)
    {
        var v = sc.PlayerScaling;
        if (v is "" or "auto") return auto;
        if (v is "vanilla" or "game" or "on") return 0;
        if (v is "off" or "none" or "solo") return 1;
        return float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? Mathf.Clamp(Mathf.RoundToInt(n), 1, 10) : auto;
    }

    private static float _closeToolboxAt;

    /// <summary>A scenario's screenshot: combat/results/shots/&lt;name&gt;.png, optionally with the toolbox open on a tab.</summary>
    private static void TakeShot(ShotSpec shot, int number)
    {
        try
        {
            if (shot.Tab.Length > 0) { UI.Toolbox.ToolboxHost.ShowTab(shot.Tab); _closeToolboxAt = Time.realtimeSinceStartup + 1.5f; }
            if (shot.Clean) TestRunner.CleanShotUntilFrame = Time.frameCount + 2;
            var dir = Path.Combine(CombatData.ResultsFolder, "shots");
            Directory.CreateDirectory(dir);
            var name = new string(shot.Name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
            var file = Path.Combine(dir, name + (number > 0 ? "-" + number : "") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            TestLabPlugin.Log?.LogInfo($"Combat: screenshot {file}");
        }
        catch (Exception ex) { TestLabPlugin.Log?.LogWarning("Combat: screenshot failed: " + ex.Message); }
    }

    /// <summary>Players the game's difficulty scaling counts for this fight (0 = the game counts them itself). See CombatLanes_Game_GetPlayerDifficulty.</summary>
    internal static int DifficultyPlayers;

    /// <summary>
    /// "all", or scenario names separated by commas. Used by -testlab-combat on the command line and the hot-mode
    /// combat.txt request, the same way -testlab-run takes suites. A name that is a folder-less set file in combat/sets
    /// is not supported yet; scenarios only.
    /// </summary>
    public static bool StartNamed(string what, bool quitWhenDone)
    {
        EnsureShippedCombat();
        // "all", names, or prefixes ending in * ("game-*", "game-swamp-*")
        var all = CombatData.ScenarioNames();
        var names = what.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
            ? all
            : what.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0)
                .SelectMany(n => n.EndsWith("*") ? all.Where(a => a.StartsWith(n.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)) : new[] { n })
                .ToList();      // repeats are kept on purpose: ABBA orders list a scenario twice
        if (names.Count == 0)
        {
            TestLabPlugin.Log?.LogError($"Combat: no scenarios found for '{what}' in {CombatData.Folder}");
            if (quitWhenDone) Application.Quit(3);
            return false;
        }
        return Start(names, quitWhenDone);
    }

    internal static void NoteGearDeath()
    {
        if (!_gearDied)
        {
            _gearDied = true;
            _gearDiedAt = CombatRecorder.Now;
        }
    }

    private static IEnumerator RunAll(IList<string> names, bool quitWhenDone)
    {
        Running = true;
        _stop = false;
        Tally = "";
        var result = new CombatResult { Title = "Combat Lab results" };
        var hostileBefore = CreatureHostilityState.Enabled;
        Status = "Waiting for the world to be ready...";

        // the world, the lab's own arena and any auto-load case must be finished first (same wait as the suites)
        var stable = 0f;
        var waited = 0f;
        while (waited < 180f && stable < 2f)
        {
            var p = Player.m_localPlayer;
            if (p != null && !p.InIntro() && !p.IsTeleporting() && !CaseRunner.Running && !ArenaBuilder.Busy && ZNetScene.instance != null)
                stable += Time.unscaledDeltaTime;
            else
                stable = 0f;
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        var player = Player.m_localPlayer;
        if (player == null)
        {
            TestLabPlugin.Log?.LogError("Combat: no player after waiting for the world; giving up.");
            Status = "No player.";
            Running = false;
            if (quitWhenDone) Application.Quit(3);
            yield break;
        }
        ArenaBuilder.ClearAgain();

        var homeRot = player.transform.rotation;
        var runStartedReal = Time.realtimeSinceStartup;
        var checkedOk = 0;
        var home = player.transform.position;
        try
        {
            // the combat arena: well away from the start, checked for stones and ruins, flat, cleared, ringed by unbreakable
            // stakes; nothing crosses it unless the sim says
            if (DumpGameData)
            {
                try { CombatGameData.Write(); }
                catch (Exception ex) { TestLabPlugin.Log?.LogError("Combat: game data dump failed: " + ex); }
            }
            Status = "Choosing and building the combat arena";
            var chosen = home;
            var arena = CheckOnly ? null : CombatArena.ChooseAndBuild(player, home, homeRot, v => chosen = v);
            while (arena != null && arena.MoveNext())
                yield return arena.Current;
            home = chosen;
            player = Player.m_localPlayer ?? player;
            result.Subtitle = string.Join(", ", names);
            Fill(result);
            foreach (var name in names)
            {
                if (_stop) break;
                if (TestLabConfig.Instance.CombatMaxRunMinutes.Value > 0f && Time.realtimeSinceStartup - runStartedReal > TestLabConfig.Instance.CombatMaxRunMinutes.Value * 60f)
                {
                    TestLabPlugin.Log?.LogWarning("Combat: the run reached [Combat] MaxRunMinutes; stopping and writing the report so far.");
                    _stop = true;
                    break;
                }
                ScenarioSpec scenario;
                Loadout loadout;
                try
                {
                    scenario = CombatData.ReadScenario(name);
                    loadout = CombatData.ReadLoadout(scenario.Loadout);
                }
                catch (Exception ex)
                {
                    TestLabPlugin.Log?.LogError($"Combat: scenario '{name}' could not be read: {ex.Message}");
                    Status = $"Scenario '{name}': {ex.Message}";
                    continue;
                }
                var missing = MissingPrefabs(scenario, loadout);
                if (missing.Count > 0)
                {
                    // a misspelled item, meal or creature would fight the wrong fight: skip it, loudly
                    TestLabPlugin.Log?.LogError($"Combat: scenario '{name}' skipped, unknown prefab(s): {string.Join(", ", missing)}");
                    continue;
                }
                if (CheckOnly) { checkedOk++; continue; }

                foreach (var variant in scenario.Variants)
                {
                    if (_stop) break;
                    ApplyVariant(variant);
                    foreach (var food in scenario.Foods)
                    {
                        foreach (var attack in scenario.Attacks)
                        {
                            if (_stop) break;
                            var counts = new List<int?>();
                            if (scenario.Counts != null)
                                for (var n = scenario.Counts.From; n <= scenario.Counts.To; n += scenario.Counts.Step) counts.Add(n);
                            else
                                counts.Add(null);
                            var label = LoadoutLabel(loadout, scenario, food, attack);
                            int capacity = 0, tried = 0;
                            var climbing = true;
                            foreach (var count in counts)
                            {
                                if (_stop) break;
                                if (scenario.Mix.Count > 0)
                                {
                                    // mixed lanes: each setup is its own row; setups rotate between the lanes every fight
                                    var mixLoadouts = scenario.Mix.Select(m => CombatData.ReadLoadout(m.Loadout)).ToList();
                                    var perMix = scenario.Mix.Select(_ => new List<RunRecord>()).ToList();
                                    var fights = RunsOverride > 0 ? RunsOverride : scenario.Runs;     // runs per setup = fights
                                    var n = scenario.Mix.Count;
                                    for (var f = 0; f < fights && !_stop; f++)
                                    {
                                        var plan = Enumerable.Range(0, n).Select(k =>
                                        {
                                            var m = (k + f) % n;
                                            return new ZonePlan { Loadout = mixLoadouts[m], Foes = scenario.Mix[m].Foes, Bot = scenario.Mix[m].Bot, Mix = m };
                                        }).ToList();
                                        Scenario = scenario.Name;
                                        Status = $"{scenario.Name} [{variant.Name}] fight {f + 1}/{fights} ({n} setups)";
                                        var recs = new List<RunRecord>();
                                        var fight = Fight(scenario, variant, plan[0].Loadout, food, attack, plan[0].Foes, f, home, homeRot, r => recs.Add(r), plan);
                                        while (true)
                                        {
                                            bool more;
                                            try { more = fight.MoveNext(); }
                                            catch (Exception ex)
                                            {
                                                TestLabPlugin.Log?.LogError($"Combat: fight {f + 1} of '{scenario.Name}' crashed: {ex}");
                                                more = false;
                                            }
                                            if (!more) break;
                                            yield return fight.Current;
                                        }
                                        foreach (var r in recs)
                                            if (r.Mix >= 0 && r.Mix < n) perMix[r.Mix].Add(r);
                                        if ((scenario.AutoRuns ?? TestLabConfig.Instance.CombatAutoRuns.Value) && perMix.All(pm => CombatStats.Settled(pm, out _)))
                                        {
                                            TestLabPlugin.Log?.LogInfo($"Combat: {scenario.Name}: every setup settled after {f + 1} fights; moving on.");
                                            break;
                                        }
                                    }
                                    ResetState(null, home, homeRot);
                                    for (var m = 0; m < n; m++)
                                    {
                                        var mixRuns = perMix[m];
                                        var sum = CombatStats.Summarize(scenario.Name, variant.Name, mixRuns);
                                        result.Cells.Add(new CombatCell
                                        {
                                            Loadout = scenario.Mix[m].Label, Foe = FoeLabelFor(scenario.Mix[m].Foes), Variant = variant.Name, Summary = sum, Band = scenario.Band,
                                            Verdict = CombatVerdict.Judge(sum, scenario.Band)
                                        });
                                        if (mixRuns.Count > 0) result.SampleRuns.Add(mixRuns.OrderBy(r => Math.Abs(r.Seconds - sum.Ttk.Median)).First());
                                    }
                                    SavePartial(result);
                                    break;
                                }
                                if (scenario.TeamTo > 1)
                                {
                                    // team fights: climb the team size until a team wins often enough
                                    var teamFoes = FoesFor(scenario, count);
                                    var needed = 0;
                                    var drow = scenario.Difficulty ? new DifficultyRow
                                    {
                                        Biome = scenario.Biome.Length > 0 ? scenario.Biome : loadout.Tier, Foe = teamFoes.Count > 0 ? teamFoes[0].Prefab : "?",
                                        Stars = teamFoes.Count > 0 ? teamFoes[0].Stars : 0, Loadout = loadout.Name, Bot = scenario.Bot, Scenario = scenario.Name,
                                        TeamTried = scenario.TeamTo, NeedWin = scenario.TeamNeedWin,
                                        Clock = scenario.TimeScale > 0f ? scenario.TimeScale : TestLabConfig.Instance.CombatTimeScale.Value
                                    } : null;
                                    for (var size = scenario.TeamFrom; size <= scenario.TeamTo && !_stop; size++)
                                    {
                                        var teamRuns = new List<RunRecord>();
                                        Tally = "";
                                        var cap = RunsOverride > 0 ? RunsOverride : scenario.Runs;
                                        for (var i = 0; i < cap && !_stop; i++)
                                        {
                                            Scenario = scenario.Name;
                                            Status = $"{scenario.Name} [{variant.Name}] team of {size}, fight {i + 1}/{cap}";
                                            var recs = new List<RunRecord>();
                                            var fight = Fight(scenario, variant, loadout, food, attack, teamFoes, i, home, homeRot, r => recs.Add(r), null, size);
                                            while (true)
                                            {
                                                bool more;
                                                try { more = fight.MoveNext(); }
                                                catch (Exception ex)
                                                {
                                                    TestLabPlugin.Log?.LogError($"Combat: team fight {i + 1} of '{scenario.Name}' crashed: {ex}");
                                                    more = false;
                                                }
                                                if (!more) break;
                                                yield return fight.Current;
                                            }
                                            teamRuns.AddRange(recs);
                                            Tally = TallyOf(teamRuns);
                                            if ((scenario.AutoRuns ?? TestLabConfig.Instance.CombatAutoRuns.Value) && CombatStats.Settled(teamRuns, out _)) break;
                                        }
                                        ResetState(null, home, homeRot);
                                        var tsum = CombatStats.Summarize(scenario.Name, variant.Name, teamRuns);
                                        result.Cells.Add(new CombatCell
                                        {
                                            Loadout = label + $" (team of {size})", Foe = FoeLabelFor(teamFoes), Variant = variant.Name, Summary = tsum, Band = scenario.Band,
                                            Verdict = CombatVerdict.Judge(tsum, scenario.Band)
                                        });
                                        if (teamRuns.Count > 0) result.SampleRuns.Add(teamRuns.OrderBy(r => Math.Abs(r.Seconds - tsum.Ttk.Median)).First());
                                        var fell = teamRuns.Count == 0 ? 0 : teamRuns.Average(r => (double)r.Deaths);
                                        TestLabPlugin.Log?.LogInfo($"Combat: {scenario.Name}: team of {size}: {tsum.Wins}/{tsum.Runs} wins, {fell:0.0} of {size} fell on average.");
                                        if (drow != null)
                                        {
                                            drow.Teams.Add((size, tsum.Wins, tsum.Runs, fell));
                                            if (size == 1)
                                            {
                                                drow.SoloRuns = tsum.Runs; drow.SoloWins = tsum.Wins; drow.SoloTtk = tsum.Ttk.Mean; drow.SoloDamagePct = tsum.DamageTakenPct.Mean;
                                                var deaths = teamRuns.Where(r => r.Outcome == "loss").Select(r => (double)r.TimeToDie).ToList();
                                                drow.SoloDeathTime = deaths.Count > 0 ? deaths.Average() : 0;
                                            }
                                        }
                                        SavePartial(result);
                                        if (tsum.WinRate >= scenario.TeamNeedWin) { needed = size; break; }
                                    }
                                    if (drow != null) { drow.PlayersNeeded = needed; CombatDifficultyDb.Record(drow); }
                                    result.Capacity.Add(new CapacityRow { Loadout = label + " (players needed)", Foe = FoeLabelFor(teamFoes), Variant = variant.Name, Max = needed, Tried = scenario.TeamTo, At = scenario.TeamNeedWin });
                                    TestLabPlugin.Log?.LogInfo($"Combat: {scenario.Name}: " + (needed > 0 ? $"needs {needed} player(s) to win at least {scenario.TeamNeedWin:P0}." : $"even {scenario.TeamTo} players do not win {scenario.TeamNeedWin:P0}."));
                                    break;
                                }
                                var runs = new List<RunRecord>();
                                Tally = "";
                                var runCount = RunsOverride > 0 ? RunsOverride : scenario.Runs;
                                var foes = FoesFor(scenario, count);
                                for (var i = 0; i < runCount && !_stop; i++)
                                {
                                    Scenario = scenario.Name;
                                    Status = $"{scenario.Name} [{variant.Name}{(food.Length > 0 ? ", " + food : "")}{(scenario.Attacks.Count > 1 ? ", " + attack.Label : "")}{(count != null ? ", x" + count : "")}] run {i + 1}/{runCount}";
                                    var recs = new List<RunRecord>();
                                    var fight = Fight(scenario, variant, loadout, food, attack, foes, i, home, homeRot, r => recs.Add(r));
                                    while (true)
                                    {
                                        bool more;
                                        try { more = fight.MoveNext(); }
                                        catch (Exception ex)
                                        {
                                            TestLabPlugin.Log?.LogError($"Combat: run {i + 1} of '{scenario.Name}' crashed: {ex}");
                                            more = false;
                                        }
                                        if (!more) break;
                                        yield return fight.Current;
                                    }
                                    runs.AddRange(recs);
                                    Tally = TallyOf(runs);
                                    if (recs.Count > 1) i += recs.Count - 1;      // lanes: one fight gave several runs
                                    // auto runs: stop once the numbers are pinned down (scenario "runs" is then the cap)
                                    if ((scenario.AutoRuns ?? TestLabConfig.Instance.CombatAutoRuns.Value) && CombatStats.Settled(runs, out var why))
                                    {
                                        TestLabPlugin.Log?.LogInfo($"Combat: {scenario.Name}: settled after {runs.Count} runs ({why}); moving on.");
                                        break;
                                    }
                                }
                                ResetState(null, home, homeRot);              // the last run of this set is done: reset, then move on

                                var summary = CombatStats.Summarize(scenario.Name, variant.Name, runs);
                                var cell = new CombatCell
                                {
                                    Loadout = label, Foe = scenario.Pvp != null ? $"{scenario.Pvp.Name} ({scenario.Pvp.Loadout}, {scenario.Pvp.Bot})" : FoeLabelFor(foes), Variant = variant.Name, Summary = summary, Band = scenario.Band,
                                    Verdict = CombatVerdict.Judge(summary, scenario.Band)
                                };
                                result.Cells.Add(cell);
                                if (runs.Count > 0) result.SampleRuns.Add(runs.OrderBy(r => Math.Abs(r.Seconds - summary.Ttk.Median)).First());
                                SavePartial(result);

                                // calibration: climb until the gear side mostly loses
                                if (scenario.Counts != null && count != null)
                                {
                                    tried = count.Value;
                                    if (climbing && summary.WinRate >= scenario.Counts.CapacityAt) capacity = count.Value;
                                    else climbing = false;
                                    if (summary.WinRate < scenario.Counts.StopBelow) break;
                                }
                            }
                            if (scenario.Counts != null)
                                result.Capacity.Add(new CapacityRow { Loadout = label, Foe = scenario.Foes.Count > 0 ? scenario.Foes[0].Label : "?", Variant = variant.Name, Max = capacity, Tried = tried, At = scenario.Counts.CapacityAt });
                        }
                    }
                    ModEnvironment.RestoreTest();
                    ModEnvironment.TestScope = false;
                }
            }
        }
        finally
        {
            ModEnvironment.RestoreTest();
            ModEnvironment.TestScope = false;
            CreatureHostilityState.Enabled = hostileBefore;
            Fighting = false;
            CombatRecorder.End();
            ResetState(null, home, homeRot);
            CombatClock.Stop();
            CombatLean.End();
            CombatLanes.Clear();
            CombatArena.Teardown();
            CombatLive.SessionEnded();
            Running = false;
        }

        if (CheckOnly)
        {
            TestLabPlugin.Log?.LogInfo($"Combat check: {checkedOk} of {names.Count} scenario(s) OK; any others are listed above as skipped.");
            if (quitWhenDone) { yield return new WaitForSecondsRealtime(0.5f); Application.Quit(checkedOk == names.Count ? 0 : 1); }
            yield break;
        }
        CombatDifficultyDb.Save();
        var flags = 0;
        if (result.Cells.Count > 0)
        {
            result.Baseline = result.Cells[0].Variant;
            CombatBaseline.AddTo(result);
            flags = result.Cells.Count(c => CombatVerdict.IsFlag(c.Verdict));
            LastResult = result;
            LastReport = Write(result, flags);
            Status = $"Done: {result.Cells.Count} cells, {flags} balance flag(s), report {LastReport}";
        }
        else
            Status = "Nothing ran.";
        TestLabPlugin.Log?.LogInfo("Combat: " + Status);
        // exit codes like -testlab-run: 0 fine, 1 a balance flag (a cell far off its band), 3 nothing ran
        if (quitWhenDone)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(result.Cells.Count == 0 ? 3 : flags > 0 ? 1 : 0);
        }
    }

    /// <summary>Every item, meal and creature the scenario names that this game does not have.</summary>
    private static List<string> MissingPrefabs(ScenarioSpec sc, Loadout main)
    {
        var missing = new List<string>();
        var loadouts = new List<Loadout> { main };
        try
        {
            foreach (var m in sc.Mix) if (m.Loadout != main.Name) loadouts.Add(CombatData.ReadLoadout(m.Loadout));
            if (sc.Pvp != null && sc.Pvp.Loadout != main.Name) loadouts.Add(CombatData.ReadLoadout(sc.Pvp.Loadout));
        }
        catch (Exception ex) { missing.Add("loadout: " + ex.Message); }
        foreach (var l in loadouts)
        {
            foreach (var it in l.Items)
                if (ObjectDB.instance?.GetItemPrefab(it.Prefab) == null) missing.Add($"item {it.Prefab} ({l.Name})");
            foreach (var meal in l.Meals.Concat(l.FoodMeals.Values.SelectMany(v => v)).Distinct())
                if (ObjectDB.instance?.GetItemPrefab(meal) == null) missing.Add($"meal {meal} ({l.Name})");
        }
        foreach (var f in sc.Foes.Concat(sc.Mix.SelectMany(m => m.Foes)))
            if (ZNetScene.instance?.GetPrefab(f.Prefab) == null) missing.Add($"creature {f.Prefab}");
        return missing.Distinct().ToList();
    }

    private static string LoadoutLabel(Loadout l, ScenarioSpec sc, string food, AttackSpec attack) =>
        l.Name + (food.Length > 0 ? $" [{food}]" : "") + (sc.Attacks.Count > 1 || attack.Mode != AttackSpec.Kind.Primary ? $" ({attack.Label})" : "");

    /// <summary>The scenario's foes, with a calibration count applied to the first foe type.</summary>
    private static List<FoeSpec> FoesFor(ScenarioSpec sc, int? count)
    {
        var list = sc.Foes.Select(f => new FoeSpec { Prefab = f.Prefab, Stars = f.Stars, Count = f.Count, Distance = f.Distance }).ToList();
        if (count != null && list.Count > 0) list[0].Count = count.Value;
        return list;
    }

    private static string FoeLabelFor(List<FoeSpec> foes) =>
        foes.Count == 0 ? "?" : string.Join(" + ", foes.Select(f => f.Label + (f.Count > 1 ? " x" + f.Count : "")));

    private static void ApplyVariant(VariantSpec v)
    {
        ModEnvironment.TestScope = true;
        foreach (var c in v.Changes)
        {
            var err = ModEnvironment.Set(c.Guid, c.Section, c.Key, c.Value);
            if (err.Length > 0)
                TestLabPlugin.Log?.LogWarning($"Combat variant '{v.Name}': {err}");
        }
    }

    /// <summary>Mixed lanes: what zone k fights with this time (rotated every fight).</summary>
    internal sealed class ZonePlan
    {
        public Loadout Loadout = null!;
        public List<FoeSpec> Foes = new();
        public string Bot = "";
        public int Mix;
    }

    private static IEnumerator Fight(ScenarioSpec sc, VariantSpec variant, Loadout loadout, string food, AttackSpec attack, List<FoeSpec> foeSpecs, int index, Vector3 home, Quaternion homeRot, Action<RunRecord> done, List<ZonePlan>? plan = null, int team = 1)
    {
        var player = Player.m_localPlayer;
        if (player == null) yield break;

        // ---- setup: clean state, creatures hostile, the loadout on, foes placed
        ResetState(null, home, homeRot);
        CreatureHostilityState.Enabled = false;       // creatures hostile: this is the point
        yield return null;
        // the gear must really be on before the fight: a fight in a half-equipped state measures punching, not the gear.
        // Equipping can take a moment (equip actions are queued), so wait for it, nudge it, and redo the whole setup if it never lands.
        for (var attemptSetup = 0; attemptSetup < 3; attemptSetup++)
        {
            ApplyLoadout(player, loadout, food);
            var equipWait = 0f;
            while (!GearReady(player, loadout) && equipWait < 3f)
            {
                yield return new WaitForSecondsRealtime(0.15f);
                equipWait += 0.15f;
                if (equipWait % 0.6f < 0.15f) EquipMissing(player, loadout);
            }
            if (GearReady(player, loadout)) break;
            TestLabPlugin.Log?.LogWarning($"Combat: '{loadout.Name}' did not fully equip (try {attemptSetup + 1}/3); resetting and trying again.");
            ResetState(null, home, homeRot);
            yield return null;
        }
        if (!GearReady(player, loadout))
        {
            // a fight with missing gear is not a measurement of the loadout: record nothing (it used to count as a loss)
            TestLabPlugin.Log?.LogError($"Combat: '{loadout.Name}' could not be equipped after 3 tries; run {index + 1} skipped, nothing recorded.");
            yield break;
        }
        // spike: extra bot players, each with the same loadout and its own foes (one fight, several runs)
        CombatLanes.Clear();
        var zones = sc.Lanes > 1 ? CombatLanes.Zones(CombatArena.Built ? CombatArena.Center : home, homeRot, sc.Lanes, CombatArena.Built ? CombatArena.Radius : 26f) : null;
        if (zones != null)
        {
            // lane 0 is you: your own zone, like every other lane
            player.transform.SetPositionAndRotation(zones[0].spot, zones[0].rot);
            Physics.SyncTransforms();
            for (var k = 1; k < zones.Count && !sc.ZoneOnly; k++)      // zoneOnly: the zone layout, no other lanes
            {
                var z = zones[k];
                var lp = CombatLanes.Spawn(k, z.spot, z.rot);
                var zp = plan != null && k < plan.Count ? plan[k] : null;
                CombatLanes.All.Add(new CombatLane
                {
                    Index = k, Player = lp, ZoneCenter = z.center, ZoneRadius = z.radius,
                    Loadout = zp?.Loadout ?? loadout, FoeSpecs = zp?.Foes ?? foeSpecs, Bot = zp?.Bot ?? sc.Bot, Mix = zp?.Mix ?? -1
                });
            }
            yield return null;                                            // let the new players wake up before equipping
            foreach (var lane in CombatLanes.All)
                CombatLanes.Equip(lane.Player, lane.Loadout!);
            var laneWait = 0f;
            while (CombatLanes.All.Any(l => !CombatLanes.GearReady(l.Player, l.Loadout!)) && laneWait < 3f)
            {
                yield return new WaitForSecondsRealtime(0.15f);
                laneWait += 0.15f;
                foreach (var lane in CombatLanes.All) CombatLanes.EquipMissing(lane.Player, lane.Loadout!);
            }
            var unready = CombatLanes.All.Where(l => !CombatLanes.GearReady(l.Player, l.Loadout!)).ToList();
            if (unready.Count > 0)
            {
                // a lane sitting out left its zone's foe idle and stalled the fight; a gear failure now voids the whole fight
                TestLabPlugin.Log?.LogError($"Combat lanes: lane(s) {string.Join(", ", unready.Select(l => l.Index + " (" + l.Loadout!.Name + ")"))} could not be equipped; run {index + 1} skipped, nothing recorded.");
                CombatLanes.Clear();
                ResetState(null, home, homeRot);
                yield break;
            }
            foreach (var lane in CombatLanes.All)
                CombatLanes.Fill(lane.Player, CombatPrep.MealsFor(lane.Loadout!, food), TestRules.MaxHealth, TestRules.MaxStamina);
        }

        // PvP: Bjornita (a bot player) is the foe, with her own loadout and bot
        CombatLane? opponent = null;
        if (sc.Pvp != null)
        {
            var oppLoadout = CombatData.ReadLoadout(sc.Pvp.Loadout);
            var fwd = homeRot * Vector3.forward;
            var who = WardTools.Known.Append(WardTools.Bjorn).FirstOrDefault(w => w.Name.Equals(sc.Pvp.Name, StringComparison.OrdinalIgnoreCase))
                      ?? new WardTools.Identity { Name = sc.Pvp.Name, Id = 100099 };
            var op = CombatLanes.Spawn(1, home + fwd * 7f, homeRot * Quaternion.Euler(0f, 180f, 0f), who);
            opponent = new CombatLane { Index = 1, Player = op, Opponent = true, Loadout = oppLoadout, Bot = sc.Pvp.Bot, ZoneCenter = home, ZoneRadius = CombatArena.Radius - 1.5f };
            CombatLanes.All.Add(opponent);
            yield return null;
            CombatLanes.Equip(op, oppLoadout);
            var oppWait = 0f;
            while (!CombatLanes.GearReady(op, oppLoadout) && oppWait < 3f)
            {
                yield return new WaitForSecondsRealtime(0.15f);
                oppWait += 0.15f;
                CombatLanes.EquipMissing(op, oppLoadout);
            }
            CombatLanes.Fill(op, CombatPrep.MealsFor(oppLoadout, food), TestRules.MaxHealth, TestRules.MaxStamina);
            player.SetPVP(true);
            op.SetPVP(true);
        }

        // team fight: teammates with the same loadout stand beside you and fight the same foes
        if (team > 1 && opponent == null && zones == null)
        {
            var right = homeRot * Vector3.right;
            var back = homeRot * Vector3.back;
            var spots = new[] { home + right * 2.5f, home - right * 2.5f, home + back * 2.5f };
            for (var k = 1; k < team && k <= spots.Length; k++)
            {
                var mp = CombatLanes.Spawn(k, spots[k - 1], homeRot, new WardTools.Identity { Name = "Ally " + k, Id = 900100 + k });
                CombatLanes.All.Add(new CombatLane { Index = k, Player = mp, Teammate = true, Loadout = loadout, Bot = sc.AllyBot.Length > 0 ? sc.AllyBot : sc.Bot, ZoneCenter = home, ZoneRadius = CombatArena.Radius - 1.5f });
            }
            yield return null;
            foreach (var mate in CombatLanes.All) CombatLanes.Equip(mate.Player, loadout);
            var mateWait = 0f;
            while (CombatLanes.All.Any(m => !CombatLanes.GearReady(m.Player, loadout)) && mateWait < 3f)
            {
                yield return new WaitForSecondsRealtime(0.15f);
                mateWait += 0.15f;
                foreach (var mate in CombatLanes.All) CombatLanes.EquipMissing(mate.Player, loadout);
            }
            foreach (var mate in CombatLanes.All) CombatLanes.Fill(mate.Player, CombatPrep.MealsFor(loadout, food), TestRules.MaxHealth, TestRules.MaxStamina);
        }

        yield return new WaitForSecondsRealtime(0.2f);
        var scale = sc.TimeScale > 0f ? sc.TimeScale : TestLabConfig.Instance.CombatTimeScale.Value;
        CombatClock.Ensure();
        CombatClock.Scale = Mathf.Clamp(scale, 1f, 4f);
        Time.timeScale = CombatClock.Scale;
        if (sc.Lean == 1 || (sc.Lean < 0 && TestLabConfig.Instance.CombatLean.Value)) CombatLean.Begin(); else CombatLean.End();
        var beatFrame = Time.frameCount;
        var startFrame = Time.frameCount;

        // live fights: a short countdown so you can get your hands on the keys (the foes are not out yet; she stands still)
        if (sc.Countdown > 0f)
        {
            for (var t = Mathf.CeilToInt(sc.Countdown); t > 0 && !_stop; t--)
            {
                LiveLine = $"<b>Fight starts in {t}...</b>";
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        // everyone eats at the same moment, just before the fight: food decays, so feeding lanes earlier left them a point down
        {
            var myMeals = CombatPrep.MealsFor(loadout, food);
            if (myMeals.Count > 0) CombatPrep.Feed(player, myMeals);
            foreach (var l in CombatLanes.All)
                CombatLanes.Fill(l.Player, CombatPrep.MealsFor(l.Loadout ?? loadout, food), TestRules.MaxHealth, TestRules.MaxStamina);
        }
        var foes = opponent != null ? new List<Character> { opponent.Player }
            : zones != null
            ? SpawnFoes(sc, foeSpecs, player, zones[0].spot, zones[0].rot, zones[0].radius * 0.9f)
            : SpawnFoes(sc, foeSpecs, player, home, homeRot);
        var localBot = plan != null && plan.Count > 0 ? plan[0].Bot : sc.Bot;
        var run = new RunRecord { Scenario = sc.Name, Variant = variant.Name, Bot = localBot, Index = index, Mix = plan != null && plan.Count > 0 ? plan[0].Mix : -1, GearMaxHealth = player.GetMaxHealth(), FoeMaxHealth = foes.Sum(f => f.GetMaxHealth()) };
        if (index == 0)
            TestLabPlugin.Log?.LogInfo($"Combat: {sc.Name} setup: skills {LastSkills}; meals {string.Join(", ", CombatPrep.MealsFor(loadout, food).DefaultIfEmpty("none (fixed numbers)"))}; max health {player.GetMaxHealth():0}, stamina {player.GetMaxStamina():0}; clock {CombatClock.Scale:0.#}x; lanes {sc.Lanes}" + (opponent != null ? $"; PvP vs {opponent.Player.GetPlayerName()} ({opponent.Loadout!.Name}, {opponent.Bot}), her max health {opponent.Player.GetMaxHealth():0}" : ""));
        CombatBot.Trace = sc.Trace;
        var bot = new CombatBot(player, BotProfile.Get(localBot), () => foes.Where(f => f != null).Cast<Character>(), sc.Seed * 1000 + index);
        bot.Attack = attack;
        _gearDied = false;
        // creature scaling by players nearby: a team fight counts its team, lanes count 1 each (solo fights side by side)
        DifficultyPlayers = ScalingPlayers(sc, team > 1 && opponent == null && zones == null ? 1 + CombatLanes.All.Count(m => m.Teammate) : 1);
        Fighting = true;
        CombatRecorder.Begin(run, new Character[] { player });
        CombatRules.Begin(sc);
        var localDead = false;
        if (team > 1 && opponent == null && zones == null)
        {
            run.TeamSize = 1 + CombatLanes.All.Count(m => m.Teammate);
            foreach (var f in foes) CombatLanes.TeamFoes.Add(f);
            var mates = CombatLanes.All.Where(m => m.Teammate).ToList();
            var me = player;
            CombatLanes.TeamAlive = () => (localDead ? Enumerable.Empty<Character>() : new Character[] { me }).Concat(mates.Where(m => !m.Done).Select(m => (Character)m.Player));
            CombatRecorder.AddLane(run, player, foes);
            // spread out: each player fights from its own side of the foe (2: opposite sides, 3: 120 degrees apart, 4: 90)
            var towardMe = -(homeRot * Vector3.forward);
            bot.FlankDir = towardMe;
            foreach (var mate in mates)
            {
                mate.Run = run;
                mate.BotDriver = new CombatBot(mate.Player, BotProfile.Get(sc.AllyBot.Length > 0 ? sc.AllyBot : sc.Bot), () => foes.Where(f => f != null).Cast<Character>(), sc.Seed * 1000 + index + mate.Index * 104729) { Attack = attack };
                mate.BotDriver.FlankDir = Quaternion.Euler(0f, mate.Index * 360f / run.TeamSize, 0f) * towardMe;
                CombatRecorder.AddLane(run, mate.Player, foes);
            }
        }
        else if (opponent != null)
        {
            var me = player;
            opponent.Bot = opponent.Bot.Length > 0 ? opponent.Bot : "fighter";
            opponent.Run = new RunRecord { Scenario = sc.Name, Variant = variant.Name, Bot = opponent.Bot, Lane = 1 };
            opponent.BotDriver = new CombatBot(opponent.Player, BotProfile.Get(opponent.Bot), () => new Character[] { me }, sc.Seed * 1000 + index + 4243) { Attack = attack };
        }
        else if (CombatLanes.All.Count > 0)
        {
            if (sc.LaneLock) foreach (var f in foes) CombatLanes.Lock(f, player);
            CombatRecorder.AddLane(run, player, foes);
            foreach (var lane in CombatLanes.All)
            {
                var lp = lane.Player;
                var lf = SpawnFoes(sc, lane.FoeSpecs ?? foeSpecs, lp, lp.transform.position, lp.transform.rotation, lane.ZoneRadius * 0.9f);
                lane.Foes = lf;
                if (sc.LaneLock) foreach (var f in lf) CombatLanes.Lock(f, lp);
                lane.Run = new RunRecord { Scenario = sc.Name, Variant = variant.Name, Bot = lane.Bot, Index = index + lane.Index, Lane = lane.Index, Mix = lane.Mix, GearMaxHealth = lp.GetMaxHealth(), FoeMaxHealth = lf.Sum(f => f.GetMaxHealth()) };
                lane.BotDriver = new CombatBot(lp, BotProfile.Get(lane.Bot), () => lf.Where(f => f != null).Cast<Character>(), sc.Seed * 1000 + index + lane.Index * 7919) { Attack = attack };
                CombatRecorder.AddLane(lane.Run, lp, lf);
            }
        }

        // ---- run: until a side is down (or at the scenario's target health), or the timeout; with lanes, until every lane is done
        var fixedWait = new WaitForFixedUpdate();
        var fightStartReal = Time.realtimeSinceStartup;
        var lastDamage = 0f;
        var lastProgress = 0f;
        var nextBeat = fightStartReal + 15f;
        var hardCap = Mathf.Max(10f, TestLabConfig.Instance.CombatMaxFightSeconds.Value);
        if (BotProfile.Get(localBot).Manual) hardCap = Mathf.Max(hardCap, sc.Timeout + 30f);   // a person may take their time
        var shotsTaken = new HashSet<ShotSpec>();
        var localDone = false;
        var localWall = 0f;
        while (true)
        {
            yield return fixedWait;
            var now = CombatRecorder.Now;
            var outOfTime = now >= sc.Timeout || _stop || Time.realtimeSinceStartup - fightStartReal >= hardCap;
            foreach (var shot in sc.Shots)
                if (!shotsTaken.Contains(shot) && now >= shot.At && (shot.Run == 0 || shot.Run == index + 1))
                {
                    shotsTaken.Add(shot);
                    TakeShot(shot, shot.Run == 0 ? index + 1 : 0);
                }
            if (_closeToolboxAt > 0f && Time.realtimeSinceStartup >= _closeToolboxAt) { UI.Toolbox.ToolboxHost.Set(false); _closeToolboxAt = 0f; }
            if (run.TeamSize > 1)
            {
                // team fight: everyone on the same foes; a fallen player steps out of the ring; lost when all have fallen
                if (!localDead && _gearDied) { localDead = true; run.Deaths++; bot.Release(); Retire(player); }
                if (!localDead) bot.Step();
                foreach (var mate in CombatLanes.All.Where(m => m.Teammate && !m.Done))
                {
                    if (mate.Died) { mate.Done = true; run.Deaths++; mate.BotDriver.Release(); Retire(mate.Player); continue; }
                    mate.BotDriver.Step();
                }
                CombatArena.Contain(foes.Where(f => f != null).Cast<Character>().Concat(CombatLanes.TeamAlive?.Invoke() ?? Enumerable.Empty<Character>()));
                LiveLine = $"{now:0}s   team {run.TeamSize - run.Deaths}/{run.TeamSize} standing   foes {Mathf.RoundToInt(foes.Where(f => f != null).Sum(f => Mathf.Max(0f, f.GetHealth())))}";
                var teamEnd = false;
                if (FoesDown(foes, sc.FoeEndHealth)) { run.Outcome = "win"; run.TimeToKill = now; teamEnd = true; }
                else if (run.Deaths >= run.TeamSize) { run.Outcome = "loss"; run.TimeToDie = now; teamEnd = true; }
                else if (outOfTime) teamEnd = true;
                if (teamEnd)
                {
                    run.Seconds = now;
                    localWall = Time.realtimeSinceStartup - fightStartReal;
                    localDone = true;
                    foreach (var mate in CombatLanes.All.Where(m => m.Teammate)) { mate.Done = true; mate.BotDriver.Release(); }
                    break;
                }
                if (Time.realtimeSinceStartup >= nextBeat)
                {
                    nextBeat += 15f;
                    TestLabPlugin.Log?.LogInfo($"Combat: {Status}  {LiveLine}   {(Time.frameCount - beatFrame) / 15f:0} fps");
                    beatFrame = Time.frameCount;
                }
                continue;
            }
            if (zones != null)
            {
                // strays: one sweep that knows every lane's fighters (a sweep per lane would remove the other lanes' foes)
                var everyone = new List<Character>(foes.Where(f => f != null)) { player };
                foreach (var lane in CombatLanes.All) { everyone.Add(lane.Player); everyone.AddRange(lane.Foes.Where(f => f != null)); }
                CombatArena.Keep(everyone, CombatArena.Center, CombatArena.Radius - 1.5f);
                CombatArena.Sweep(everyone);
            }
            foreach (var lane in CombatLanes.All)
            {
                if (lane.Opponent) { if (!localDone && !lane.Died) lane.BotDriver.Step(); continue; }   // PvP: she fights you
                if (lane.Done) continue;
                lane.BotDriver.Step();
                if (sc.LaneKeep) CombatArena.Keep(lane.Foes.Where(f => f != null).Cast<Character>().Append(lane.Player), lane.ZoneCenter, lane.ZoneRadius);
                if (lane.Died) { lane.Run.Outcome = "loss"; lane.Run.TimeToDie = lane.DiedAt; }
                else if (FoesDown(lane.Foes, sc.FoeEndHealth)) { lane.Run.Outcome = "win"; lane.Run.TimeToKill = now; }
                else if (!outOfTime) continue;
                lane.Done = true;
                lane.Run.Seconds = now;
                lane.Run.PrimarySwings = lane.BotDriver.Primary;
                lane.Run.SecondarySwings = lane.BotDriver.Secondary;
                lane.Run.Punishes = lane.BotDriver.Punishes;
                lane.BotDriver.Release();
                foreach (var f in lane.Foes) if (f != null) { CombatLanes.Unlock(f); UnityEngine.Object.Destroy(f.gameObject); }
                TestLabPlugin.Log?.LogInfo($"Combat: {sc.Name} run {index + 1} lane {lane.Index}: {lane.Run.Outcome} in {now:0.0}s, dealt {lane.Run.DamageDealt:0}, took {lane.Run.DamageTaken:0}, foe hp {lane.Run.FoeMaxHealth:0}, max hp {lane.Player.GetMaxHealth():0}, blocks {lane.Run.Blocks}, parries {lane.Run.Parries}, punishes {lane.Run.Punishes}, backstabs {lane.Run.Backstabs}, tried parry/block/roll {lane.BotDriver.ParryTries}/{lane.BotDriver.BlockTries}/{lane.BotDriver.DodgeTries}, lapses {lane.BotDriver.Lapses}, whiffs {lane.BotDriver.Whiffs}{(lane.BotDriver.AvoidedBlocks > 0 ? ", rolled instead of blocking x" + lane.BotDriver.AvoidedBlocks : "")}");
            }
            if (!localDone)
            {
                bot.Step();
                if (zones != null) { if (sc.LaneKeep) CombatArena.Keep(foes.Where(f => f != null).Cast<Character>().Append(player), zones[0].center, zones[0].radius); }
                else CombatArena.Contain(foes.Where(f => f != null).Cast<Character>().Append(player));
                LiveLine = $"{now:0}s   you {Mathf.Max(0, Mathf.RoundToInt(player.GetHealth()))}/{Mathf.RoundToInt(player.GetMaxHealth())}   foes {Mathf.RoundToInt(foes.Where(f => f != null).Sum(f => Mathf.Max(0f, f.GetHealth())))}";
                var gearFraction = player.GetMaxHealth() > 0f ? player.GetHealth() / player.GetMaxHealth() : 1f;
                if (_gearDied || (sc.GearEndHealth > 0f && gearFraction <= sc.GearEndHealth))
                {
                    run.Outcome = "loss";
                    run.TimeToDie = _gearDied ? _gearDiedAt : now;
                    localDone = true;
                }
                else if (opponent != null ? opponent.Died : FoesDown(foes, sc.FoeEndHealth))
                {
                    run.Outcome = "win";
                    run.TimeToKill = now;
                    localDone = true;
                }
                else
                {
                    // progress = damage actually changing hands (hits that do 0 damage do not count)
                    var totalDamage = run.DamageDealt + run.DamageTaken;
                    if (totalDamage > lastDamage + 0.01f) { lastDamage = totalDamage; lastProgress = now; }
                    if (sc.StallSeconds > 0f && now - lastProgress >= sc.StallSeconds)
                    {
                        var detail = string.Join(", ", foes.Where(f => f != null).Select(f => f.name.Replace("(Clone)", "") + " " + Mathf.RoundToInt(f.GetHealth()) + " hp @" + Mathf.RoundToInt((f.transform.position - player.transform.position).magnitude) + " m"));
                        if (run.Unsticks < sc.MaxUnsticks && CombatLanes.All.Count == 0)
                        {
                            // unsticker: nothing has been hit for a while, so somebody is stuck. New spot for everybody, then carry on.
                            var spot = CombatArena.Relocate(player, foes.Cast<Character>().ToList());
                            run.Unsticks++;
                            lastProgress = now;
                            TestLabPlugin.Log?.LogWarning($"Combat: '{sc.Name}' run {index + 1}: no damage for {sc.StallSeconds:0} s ({detail}); unstuck #{run.Unsticks}, " + (spot != null ? $"moved everybody to {spot.Value}" : "no clear spot found"));
                        }
                        else
                        {
                            run.Outcome = "stalled";
                            TestLabPlugin.Log?.LogWarning($"Combat: '{sc.Name}' run {index + 1} stalled for good after {run.Unsticks} unstick(s): {detail}.");
                            localDone = true;
                        }
                    }
                    if (outOfTime) localDone = true;
                }
                if (localDone)
                {
                    run.Seconds = now;
                    localWall = Time.realtimeSinceStartup - fightStartReal;
                    bot.Release();
                    if (CombatLanes.All.Count > 0 && opponent == null)
                        foreach (var f in foes) if (f != null) { CombatLanes.Unlock(f); UnityEngine.Object.Destroy(f.gameObject); }
                }
            }
            if (localDone && CombatLanes.All.All(l => l.Done || l.Opponent))
                break;
            if (Time.realtimeSinceStartup >= nextBeat)
            {
                nextBeat += 15f;
                TestLabPlugin.Log?.LogInfo($"Combat: {Status}  {LiveLine}   {(Time.frameCount - beatFrame) / 15f:0} fps");
                beatFrame = Time.frameCount;      // a steady log line: tells the launch script the game is alive
            }
        }
        var wall = Time.realtimeSinceStartup - fightStartReal;
        if (_closeToolboxAt > 0f) { UI.Toolbox.ToolboxHost.Set(false); _closeToolboxAt = 0f; }

        // ---- record
        if (run.TeamSize <= 1 && run.Outcome == "loss") run.Deaths = 1;
        run.PrimarySwings = bot.Primary;
        run.SecondarySwings = bot.Secondary;
        run.Punishes = bot.Punishes;
        TestLabPlugin.Log?.LogInfo($"Combat: {sc.Name} run {index + 1}: {run.Outcome} in {run.Seconds:0.0}s, dealt {run.DamageDealt:0}, took {run.DamageTaken:0}, wall {localWall:0.0}s, {(Time.frameCount - startFrame) / Mathf.Max(0.1f, wall):0} fps, clock {run.Seconds / Mathf.Max(0.01f, localWall):0.00}x of {CombatClock.Scale:0.#}x, foe hp {run.FoeMaxHealth:0}, max hp {run.GearMaxHealth:0}, blocks {run.Blocks}, parries {run.Parries}, punishes {run.Punishes}, backstabs {run.Backstabs}, swings {run.PrimarySwings + run.SecondarySwings}, tried parry/block/roll {bot.ParryTries}/{bot.BlockTries}/{bot.DodgeTries}, lapses {bot.Lapses}, whiffs {bot.Whiffs}{(bot.AvoidedBlocks > 0 ? ", rolled instead of blocking x" + bot.AvoidedBlocks : "")}{(run.Unsticks > 0 ? ", unstuck x" + run.Unsticks : "")}{(bot.OpenerNote.Length > 0 ? ", opener: " + bot.OpenerNote + "; " + run.FirstHit : "")}"
            + (run.TeamSize > 1 ? $", team of {run.TeamSize}, {run.Deaths} fell" : CombatLanes.All.Count > 0 ? $", {CombatLanes.All.Count + 1} lanes, fight wall {wall:0.0}s" : ""));
        CombatRules.End();
        CombatRecorder.End();
        Fighting = false;
        LiveLine = "";
        done(run);
        foreach (var lane in CombatLanes.All)
            if (!lane.Opponent && !lane.Teammate) done(lane.Run);
        if (opponent != null)
        {
            TestLabPlugin.Log?.LogInfo($"Combat: {sc.Name} run {index + 1} PvP: {opponent.Player.GetPlayerName()} blocks/parries tried {opponent.BotDriver.ParryTries}/{opponent.BotDriver.BlockTries}, swings {opponent.BotDriver.Primary + opponent.BotDriver.Secondary}, punishes {opponent.BotDriver.Punishes}");
            player.SetPVP(false);
        }

        // ---- reset: nothing of this fight is left for the next one
        CombatClock.Scale = 0f;
        Time.timeScale = 1f;
        CombatLean.End();
        bot.Release();
        CombatLanes.Clear();
        // real deaths (SkipDeathReload off): the game respawns the player; wait for a living one first
        var waited = 0f;
        while ((Player.m_localPlayer == null || Player.m_localPlayer.IsDead() || Player.m_localPlayer.IsTeleporting()) && waited < 60f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        ResetState(opponent != null ? null : foes, home, homeRot);
        yield return new WaitForSeconds(0.5f);
    }

    /// <summary>Team fight: a fallen player steps just outside the ring (still in view) and stops fighting.</summary>
    private static void Retire(Player p)
    {
        var off = p.transform.position - CombatArena.Center;
        off.y = 0f;
        if (off.sqrMagnitude < 0.01f) off = Vector3.forward;
        var pos = CombatArena.Center + off.normalized * (CombatArena.Radius + 5f);
        if (ZoneSystem.instance != null) pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        p.transform.position = pos;
        Physics.SyncTransforms();
    }

    /// <summary>True when every foe is dead, or (with a target above 0) their total health is at or below that fraction of their max.</summary>
    private static bool FoesDown(List<Character> foes, float endFraction)
    {
        if (foes.All(f => f == null || f.IsDead()))
            return true;
        if (endFraction <= 0f)
            return false;
        float hp = 0f, max = 0f;
        foreach (var f in foes)
        {
            if (f == null) continue;
            hp += Mathf.Max(0f, f.GetHealth());
            max += f.GetMaxHealth();
        }
        return max > 0f && hp / max <= endFraction;
    }

    /// <summary>
    /// Puts everything back after a fight, and before the next scenario: foes, loot and corpses removed, inventory, stats and
    /// modes reset (StateReset), the player healed and standing at the start spot. Called after every run, after the last run of
    /// every scenario, and when the whole run ends, so no state leaks from one fight to the next.
    /// </summary>
    /// <summary>The skills the last loadout set (for the log).</summary>
    internal static string LastSkills = "";

    private static readonly System.Reflection.FieldInfo? IsDeadField = AccessTools.Field(typeof(Character), "m_isDead");

    private static void ResetState(List<Character>? foes, Vector3 home, Quaternion homeRot)
    {
        if (foes != null)
            foreach (var f in foes)
                if (f != null) UnityEngine.Object.Destroy(f.gameObject);
        var player = Player.m_localPlayer;
        if (player == null)
            return;
        StateReset.Run();
        ClearLeftovers(home);
        IsDeadField?.SetValue(player, false);           // in case a death slipped through before the check was skipped
        player.SetHealth(player.GetMaxHealth());      // a "death" is recorded, never played out
        player.transform.SetPositionAndRotation(home, homeRot);
        Physics.SyncTransforms();
    }

    /// <summary>True when every item the loadout wants equipped is in the inventory and worn.</summary>
    private static bool GearReady(Player player, Loadout l)
    {
        var items = player.GetInventory().GetAllItems();
        foreach (var want in l.Items)
        {
            if (!want.Equip) continue;
            if (!items.Any(it => it.m_dropPrefab != null && it.m_dropPrefab.name == want.Prefab && it.m_equipped))
                return false;
        }
        return true;
    }

    private static void EquipMissing(Player player, Loadout l)
    {
        foreach (var it in player.GetInventory().GetAllItems().ToList())
            if (!it.m_equipped && l.Items.Any(x => x.Equip && x.Prefab == it.m_dropPrefab?.name))
                player.EquipItem(it, false);
    }

    internal static void ApplyLoadout(Player player, Loadout l, string food)
    {
        foreach (var item in l.Items)
        {
            if (ItemGiver.Give(item.Prefab, item.Count, item.Quality, item.Equip) == 0)
                TestLabPlugin.Log?.LogWarning($"Combat loadout '{l.Name}': could not give '{item.Prefab}'.");
        }
        LastSkills = CombatPrep.SetSkills(player, l);
        var meals = CombatPrep.MealsFor(l, food);
        if (meals.Count > 0)
        {
            // real meals: the game's own food code decides max health and stamina, so nothing is pinned
            TestRules.MaxHealth = 0f;
            TestRules.MaxStamina = 0f;
            CombatPrep.Feed(player, meals);
        }
        else
        {
            var hp = l.MaxHealth;
            var stam = l.MaxStamina;
            if (food.Length > 0)
            {
                if (l.Food.TryGetValue(food, out var f)) { if (f.health > 0f) hp = f.health; if (f.stamina > 0f) stam = f.stamina; }
                else TestLabPlugin.Log?.LogWarning($"Combat loadout '{l.Name}': no food profile '{food}'; using its default health and stamina.");
            }
            if (hp > 0f) StatSetter.SetMaxHealth(hp);
            if (stam > 0f) StatSetter.SetMaxStamina(stam);
            StatSetter.FillAll();
        }
        // equip anything flagged that Give could not equip (a second item of a stack, a shield next to a weapon)
        foreach (var it in player.GetInventory().GetAllItems().ToList())
            if (l.Items.Any(x => x.Equip && x.Prefab == it.m_dropPrefab?.name) && !it.m_equipped)
                player.EquipItem(it, false);
    }

    private static List<Character> SpawnFoes(ScenarioSpec sc, List<FoeSpec> specs, Player player, Vector3 home, Quaternion homeRot, float maxDist = 0f)
    {
        var list = new List<Character>();
        var total = specs.Sum(f => f.Count);
        var index = 0;
        foreach (var foe in specs)
        {
            var prefab = ZNetScene.instance?.GetPrefab(foe.Prefab);
            if (prefab == null) throw new InvalidOperationException($"no prefab '{foe.Prefab}'");
            for (var i = 0; i < foe.Count; i++, index++)
            {
                // an arc in front of the player: one foe straight ahead, many spread over up to 140 degrees, a bit further back in each row
                var angle = total == 1 ? 0f : Mathf.Lerp(-70f, 70f, index / (float)(total - 1));
                var reach = maxDist > 0f ? maxDist : CombatArena.Radius - 4f;   // lanes: inside the lane's own zone
                if (sc.Opener == "sneak") reach = Mathf.Min(reach, 3.5f);       // sneak opener: the sneak-up already worked; start just behind it
                var dist = Mathf.Min(foe.Distance + (index % 2) * 2f, reach);
                var dir = Quaternion.Euler(0f, angle, 0f) * (homeRot * Vector3.forward);
                var pos = home + dir * dist;
                if (ZoneSystem.instance != null) pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                // never inside a rock: try other bearings and distances
                for (var tries = 0; tries < 8 && CombatArena.IsBlocked(pos); tries++)
                {
                    var d2 = Quaternion.Euler(0f, UnityEngine.Random.Range(-90f, 90f), 0f) * (homeRot * Vector3.forward);
                    pos = home + d2 * UnityEngine.Random.Range(Mathf.Min(8f, reach * 0.6f), reach);
                    if (ZoneSystem.instance != null) pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                }
                // facing the player, or (sneak opener) facing away and unaware
                var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.LookRotation(sc.Opener == "sneak" ? dir : -dir));
                var c = go.GetComponent<Character>();
                if (c == null) { UnityEngine.Object.Destroy(go); continue; }
                if (foe.Stars > 0) c.SetLevel(foe.Stars + 1);   // level 1 is no star
                CombatFoes.Prepare(c, sc, player);
                list.Add(c);
            }
        }
        return list;
    }

    /// <summary>Loot, ragdolls and corpses from the last fight. Only things near the start spot, only drops and ragdolls.</summary>
    private static void ClearLeftovers(Vector3 home)
    {
        foreach (var d in UnityEngine.Object.FindObjectsOfType<ItemDrop>())
            if (d != null && (d.transform.position - home).sqrMagnitude < 60f * 60f) UnityEngine.Object.Destroy(d.gameObject);
        foreach (var r in UnityEngine.Object.FindObjectsOfType<Ragdoll>())
            if (r != null && (r.transform.position - home).sqrMagnitude < 60f * 60f) UnityEngine.Object.Destroy(r.gameObject);
    }

    private static void Fill(CombatResult r)
    {
        foreach (var p in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            r.Mods.Add((p.Metadata.Name, p.Metadata.Version.ToString()));
        r.Mods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>After every finished cell: the report so far (complete: false), so a crash or freeze mid-batch keeps what was run.</summary>
    private static void SavePartial(CombatResult r)
    {
        try { Write(r, r.Cells.Count(c => CombatVerdict.IsFlag(c.Verdict)), complete: false); }
        catch (Exception ex) { TestLabPlugin.Log?.LogWarning("Combat: could not save the partial report: " + ex.Message); }
    }

    private static string Write(CombatResult r, int flags, bool complete = true)
    {
        var dir = Path.Combine(CombatData.ResultsFolder, "run-" + r.When.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "combat.html"), CombatReport.Html(r));
        var json = ToJson(r);
        json["flags"] = flags;
        json["complete"] = complete;
        File.WriteAllText(Path.Combine(dir, "combat.json"), JsonWriter.Write(json));
        File.Copy(Path.Combine(dir, "combat.html"), Path.Combine(CombatData.ResultsFolder, "latest.html"), true);
        File.Copy(Path.Combine(dir, "combat.json"), Path.Combine(CombatData.ResultsFolder, "latest.json"), true);
        return Path.Combine(dir, "combat.html");
    }

    private static Dictionary<string, object?> ToJson(CombatResult r)
    {
        var cells = new List<object?>();
        foreach (var c in r.Cells)
        {
            var s = c.Summary;
            cells.Add(new Dictionary<string, object?>
            {
                ["loadout"] = c.Loadout, ["foe"] = c.Foe, ["variant"] = c.Variant, ["runs"] = s.Runs, ["wins"] = s.Wins, ["losses"] = s.Losses, ["timeouts"] = s.Timeouts,
                ["winRate"] = s.WinRate, ["winLow"] = s.WinLow, ["winHigh"] = s.WinHigh,
                ["ttkMean"] = s.Ttk.Mean, ["ttkMedian"] = s.Ttk.Median, ["ttkSd"] = s.Ttk.Sd, ["ttkP10"] = s.Ttk.P10, ["ttkP90"] = s.Ttk.P90,
                ["damageTakenPct"] = s.DamageTakenPct.Mean, ["dpsDealt"] = s.DpsDealt.Mean, ["blocks"] = s.Blocks,
                ["secondaryShare"] = s.SecondaryShare, ["unsticks"] = s.Unsticks, ["verdict"] = CombatVerdict.Key(c.Verdict)
            });
        }
        var mods = r.Mods.Select(m => (object?)new Dictionary<string, object?> { ["name"] = m.Name, ["version"] = m.Version }).ToList();
        return new Dictionary<string, object?>
        {
            ["when"] = r.When.ToString("s"), ["baseline"] = r.Baseline, ["levelMod"] = r.LevelModLoaded, ["cells"] = cells, ["mods"] = mods,
            ["capacity"] = r.Capacity.Select(c => (object?)new Dictionary<string, object?> { ["loadout"] = c.Loadout, ["foe"] = c.Foe, ["variant"] = c.Variant, ["max"] = c.Max, ["tried"] = c.Tried, ["at"] = c.At }).ToList()
        };
    }
}

/// <summary>
/// During a combat run the gear side's death is recorded, not played out (no death screen, tombstone or respawn between
/// fights). Does nothing outside a run.
/// </summary>
[HarmonyPatch]
internal static class CombatRunner_Player_OnDeath
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Player), "OnDeath");

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(Player __instance)
    {
        if (CombatLanes.Any && CombatLanes.IsLanePlayer(__instance))
        {
            CombatLanes.NoteDeath(__instance);
            return false;                              // a lane player's death is never played out
        }
        // during the whole run, not just the fight: a death can arrive late (the death animation calls OnDeath after a delay)
        if (!CombatRunner.Running || __instance != Player.m_localPlayer)
            return true;
        if (CombatRunner.Fighting) CombatRunner.NoteGearDeath();
        // option off: let the real death happen; the runner waits for the respawn after recording it
        return !TestLabConfig.Instance.CombatSkipDeath.Value;
    }
}

/// <summary>
/// Character.CheckDeath marks the character dead first, then calls OnDeath (or starts the death animation, which calls it
/// later). Skipping only OnDeath leaves the player flagged dead and lets the late call kill them for real after the fight.
/// So during a combat run the gear side's death is noted here and the check is skipped. Does nothing outside a run.
/// </summary>
[HarmonyPatch]
internal static class CombatRunner_Character_CheckDeath
{
    private static MethodBase? TargetMethod() => AccessTools.Method(typeof(Character), "CheckDeath");

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(Character __instance)
    {
        if (CombatLanes.Any && CombatLanes.IsLanePlayer(__instance))
        {
            if (__instance.GetHealth() > 0f) return true;
            CombatLanes.NoteDeath(__instance);
            return false;
        }
        if (!CombatRunner.Running || __instance != Player.m_localPlayer || !TestLabConfig.Instance.CombatSkipDeath.Value)
            return true;
        if (__instance.GetHealth() > 0f)
            return true;
        if (CombatRunner.Fighting) CombatRunner.NoteGearDeath();
        return false;
    }
}
