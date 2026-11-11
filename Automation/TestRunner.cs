using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Cases;
using DrakesTestLab.UI.Toolbox;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// Runs suites and tests: gate on required mods, apply config overrides, reset, set up, perform the steps, check the
/// asserts, record results, clean up. Driven from the Tests tab or, with [Automation] RunOnLaunch, by itself.
/// </summary>
internal static class TestRunner
{
    private static bool _ranOnLaunch;
    private static bool? _quitOverride;
    private static bool _hotPolling;
    private static readonly List<string> _missing = new();

    public static List<SuiteDef> Suites { get; private set; } = new();
    public static readonly Dictionary<string, TestResult> Latest = new(StringComparer.OrdinalIgnoreCase);
    public static List<TestResult> LastRun { get; private set; } = new();

    public static bool Running { get; private set; }
    public static bool StopRequested { get; private set; }
    public static bool StepMode { get; set; }
    public static bool WaitingForNext { get; private set; }
    public static string Status { get; private set; } = "Idle.";
    public static string LastSummary { get; private set; } = "";
    public static string LastResultsFile { get; private set; } = "";

    /// <summary>Folder for this run's screenshots: results/run-&lt;stamp&gt;.</summary>
    public static string RunFolder { get; private set; } = "";
    private static string _runStamp = "";

    /// <summary>Wall-clock start of the run, the realtime clock then (= seconds since the game started), and how long the world took to be ready.</summary>
    public static DateTime RunStartedAt { get; private set; }
    public static float RunStartRealtime { get; private set; }
    public static float RunWorldReadySeconds { get; private set; }

    /// <summary>Every test the current run will do, so results written mid-run (or on quit) can list the rest.</summary>
    private static List<(string Suite, string Test)> _planned = new();
    private static TestResult? _current;
    private static bool _quitHooked;

    /// <summary>Off, Failures or Always: the -testlab-screenshots switch, else [Automation] Screenshots.</summary>
    public static string ScreenshotMode
    {
        get
        {
            var mode = _screenshotOverride ?? TestLabConfig.Instance.Screenshots.Value;
            return mode.Equals("always", StringComparison.OrdinalIgnoreCase) || mode.Equals("proof", StringComparison.OrdinalIgnoreCase) ? "Always"
                : mode.Equals("off", StringComparison.OrdinalIgnoreCase) || mode.Equals("none", StringComparison.OrdinalIgnoreCase) ? "Off"
                : "Failures";
        }
    }

    private static string? _screenshotOverride;

    public static bool CheckpointPending { get; private set; }
    public static string CheckpointMessage { get; private set; } = "";
    public static bool CheckpointAnswer { get; private set; }

    public static string Key(string suite, string test) => suite + "/" + test;

    static TestRunner()
    {
        // DrakesTestLab's own hooks, as an example and for the self-test suite
        TestHooks.Register("testlab.echo", args => args.TryGetValue("value", out var v) ? v : null, "DrakesTestLab");
        // is this key held as the game sees it (Input.GetKey, which holdKey / pressKey drive)? key: a Unity KeyCode name
        TestHooks.Register("testlab.keyHeld", args => args.TryGetValue("key", out var k) && Enum.TryParse<KeyCode>(Convert.ToString(k), true, out var code) && Input.GetKey(code), "DrakesTestLab");
        // is the real camera tilted to `pitch` degrees (+ up) within `tolerance` (default 3)? Reads the camera itself, not the player's stored pitch.
        TestHooks.Register("testlab.cameraPitch", args =>
        {
            if (GameCamera.instance == null)
                return "no camera";
            var actual = Mathf.Asin(Mathf.Clamp(GameCamera.instance.transform.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            var want = args.TryGetValue("pitch", out var w) ? Convert.ToSingle(w, System.Globalization.CultureInfo.InvariantCulture) : 0f;
            var tol = args.TryGetValue("tolerance", out var t) ? Convert.ToSingle(t, System.Globalization.CultureInfo.InvariantCulture) : 3f;
            return (Mathf.Abs(actual - want) <= tol ? "near " : "off ") + actual.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        }, "DrakesTestLab");
        TestHooks.Register("testlab.inventoryCount", _ => Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory().NrOfItems() : -1, "DrakesTestLab");

        // timing, for the meta suite: are the running test's log lines stamped, were its finished steps timed,
        // did the run record its start, and did the mod load timer see the mods (all of them with the preload patcher)
        TestHooks.Register("testlab.logStamped", _ => _current != null && _current.StepLog.Count > 0 && _current.StepLog.TrueForAll(l => l.StartsWith("[+", StringComparison.Ordinal)), "DrakesTestLab");
        TestHooks.Register("testlab.stepsTimed", _ => _current != null ? _current.StepTimes.Count : -1, "DrakesTestLab");
        TestHooks.Register("testlab.runTimed", _ => RunStartedAt != default && _current != null && _current.StartedAt >= RunStartedAt && _current.StartRealtime > 0f, "DrakesTestLab");
        // names of the game's prefabs that contain some text, for finding what to spawn: {"contains": "log", "max": 40}
        TestHooks.Register("testlab.prefabsLike", args =>
        {
            var scene = ZNetScene.instance;
            if (scene == null)
                return "no world";
            var text = args.TryGetValue("contains", out var c) ? Convert.ToString(c, System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";
            var max = args.TryGetValue("max", out var m) ? Convert.ToInt32(m, System.Globalization.CultureInfo.InvariantCulture) : 40;
            var names = new List<string>();
            var prefabs = HarmonyLib.AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs")?.GetValue(scene) as System.Collections.IDictionary;
            if (prefabs != null)
                foreach (System.Collections.DictionaryEntry e in prefabs)
                    if (e.Value is GameObject go && go.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                        names.Add(go.name);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Count > max)
                names = names.GetRange(0, max);
            return string.Join(", ", names);
        }, "DrakesTestLab");
        QuietZone.RegisterHooks();
        TestHooks.Register("testlab.framesSampled", _ => _current != null ? FrameProfiler.Count - _current.FrameMark : -1, "DrakesTestLab");
        TestHooks.Register("testlab.frameAvgFps", _ => _current != null ? FrameProfiler.Compute(_current.FrameMark, FrameProfiler.Count).AvgFps : -1f, "DrakesTestLab");
        TestHooks.Register("testlab.stepFramesRecorded", _ => _current != null && _current.StepFrames.Count > 0 && _current.StepFrames[0].Frames > 0, "DrakesTestLab");
        TestHooks.Register("testlab.memorySampled", _ => _current != null ? MemoryProfiler.Count - _current.FrameMark : -1, "DrakesTestLab");
        TestHooks.Register("testlab.heapMb", _ => MemoryProfiler.CurrentMonoMb, "DrakesTestLab");
        TestHooks.Register("testlab.totalMb", _ => MemoryProfiler.CurrentTotalMb, "DrakesTestLab");
        TestHooks.Register("testlab.stepMemoryRecorded", _ => _current != null && _current.StepMem.Count > 0 && _current.StepMem[0].Valid, "DrakesTestLab");
        TestHooks.Register("testlab.objectCount", _ => ObjectCensus.Take().Total, "DrakesTestLab");
        TestHooks.Register("testlab.modLoadCount", _ => { ModLoadTimer.Close(); return ModLoadTimer.All.Count; }, "DrakesTestLab");
        TestHooks.Register("testlab.modLoadSane", _ =>
        {
            ModLoadTimer.Close();
            foreach (var m in ModLoadTimer.All)
                if (m.Seconds < 0f || m.Seconds > 600f)
                    return false;
            // with the patcher every mod is listed, DrakesTestLab itself included
            return !ModLoadTimer.FromPatcher || ModLoadTimer.All.Any(m => m.Guid == TestLabPlugin.GUID);
        }, "DrakesTestLab");

        // the lab's own features, for its meta suite: save the current game as a case (name, radius), the recorder, the toolbox
        TestHooks.Register("testlab.saveCase", args =>
        {
            var name = args.TryGetValue("name", out var n) ? Convert.ToString(n) ?? "case" : "case";
            var o = new Cases.CaptureOptions();
            if (args.TryGetValue("radius", out var r) && float.TryParse(Convert.ToString(r, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var radius))
                o.Radius = radius;
            // "ui": true saves through the Cases tab, so it shows the name and its "Saved ..." message
            if (args.TryGetValue("ui", out var ui) && TestHooks.Truthy(ui) && UI.Toolbox.CasesTab.Current is { } tab)
                return tab.SaveNamed(name, args.TryGetValue("description", out var d) ? Convert.ToString(d) ?? "" : "");
            return Cases.CaseCapture.Save(Cases.CaseCapture.Capture(o, name, "saved by a test"), name);
        }, "DrakesTestLab");
        TestHooks.Register("testlab.caseText", args => System.IO.File.ReadAllText(System.IO.Path.Combine(Cases.CaseRunner.Folder, Convert.ToString(args["name"]) + ".json")), "DrakesTestLab");
        TestHooks.Register("testlab.recordStart", args =>
        {
            Recorder.AllowDuringTests = true;
            Recorder.Start(args.TryGetValue("setup", out var setup) && TestHooks.Truthy(setup));
            return true;
        }, "DrakesTestLab");
        TestHooks.Register("testlab.recordStop", _ =>
        {
            Recorder.Stop();
            Recorder.AllowDuringTests = false;
            return Recorder.Steps.Count;
        }, "DrakesTestLab");
        TestHooks.Register("testlab.recordCheckInventory", _ =>
        {
            Recorder.CheckInventory();
            return Recorder.Checks.Count;
        }, "DrakesTestLab");
        TestHooks.Register("testlab.recordedSteps", _ => string.Join(" | ", Recorder.Steps.ConvertAll(J.Describe)), "DrakesTestLab");
        TestHooks.Register("testlab.recordSave", args =>
            Recorder.Save(Convert.ToString(args["suite"]) ?? "Recorded", Convert.ToString(args["name"]) ?? "recorded", Recorder.BuildTest(Convert.ToString(args["name"]) ?? "recorded", "", true)), "DrakesTestLab");
        TestHooks.Register("testlab.showTab", args =>
        {
            UI.Toolbox.ToolboxHost.ShowTab(Convert.ToString(args["tab"]) ?? "Tests");
            return true;
        }, "DrakesTestLab");
        // pictures of the manual-check bar (normally only in attended runs): show it with a question, or clear it (clear: true)
        TestHooks.Register("testlab.showCheckpoint", args =>
        {
            if (args.TryGetValue("clear", out var clear) && TestHooks.Truthy(clear))
                AnswerCheckpoint(true);
            else
                AskCheckpoint(Convert.ToString(args.TryGetValue("message", out var m) ? m : "Does it look right?") ?? "");
            return true;
        }, "DrakesTestLab");
        TestHooks.Register("testlab.closeToolbox", _ =>
        {
            UI.Toolbox.ToolboxHost.Set(false);
            return true;
        }, "DrakesTestLab");
    }

    /// <summary>Closing the game mid-run still writes the results so far; the tests it never reached are marked Skipped.</summary>
    private static void OnQuitting()
    {
        if (!Running)
            return;
        if (_current != null && !LastRun.Contains(_current))
        {
            _current.Status = TestStatus.Skipped;
            _current.Reason = "game closed during this test";
            LastRun.Add(_current);
        }

        var done = new HashSet<string>(LastRun.ConvertAll(r => Key(r.Suite, r.Test)), StringComparer.OrdinalIgnoreCase);
        foreach (var (suite, test) in _planned)
        {
            if (!done.Contains(Key(suite, test)))
                LastRun.Add(new TestResult { Suite = suite, Test = test, Status = TestStatus.Skipped, Reason = "game closed before this test ran" });
        }

        WriteResults(complete: false);
        Running = false;
    }

    public static void Reload()
    {
        EnsureShippedSuites();
        Suites = SuiteLoader.LoadAll();
        TestHooks.Scan(force: true);
    }

    /// <summary>Copies shipped suites into the config folder file by file, never overwriting your edits.</summary>
    public static void EnsureShippedSuites()
    {
        try
        {
            var shipped = Path.Combine(ValheimPaths.TemplatesFolder, "suites");
            if (!Directory.Exists(shipped))
                return;

            CopyMissing(shipped, SuiteLoader.Folder);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Copying shipped suites failed: {ex.Message}");
        }
    }

    /// <summary>Copies files (and subfolders such as _blocks) that are not there yet.</summary>
    private static void CopyMissing(string from, string to)
    {
        foreach (var dir in Directory.GetDirectories(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(dir));
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(dir))
            {
                var target = Path.Combine(dest, Path.GetFileName(file));
                if (!File.Exists(target))
                    File.Copy(file, target);
            }

            CopyMissing(dir, dest);
        }
    }

    public static void Next() => WaitingForNext = false;

    public static void Stop()
    {
        if (Running)
            StopRequested = true;
        WaitingForNext = false;
        CheckpointPending = false;
    }

    public static void AskCheckpoint(string message)
    {
        CheckpointMessage = message;
        CheckpointAnswer = false;
        CheckpointPending = true;
        // the game stays in view: the question and Pass / Fail are on the lab bar (F7 / Shift+F7)
        ToolboxHost.Set(false);
    }

    public static void AnswerCheckpoint(bool pass)
    {
        CheckpointAnswer = pass;
        CheckpointPending = false;
    }

    // ------------------------------------------------------------------ entry points

    public static void RunAll(bool unattended = false) => Start(Plan(Suites, null), unattended);

    public static void RunSuite(SuiteDef suite) => Start(Plan(new List<SuiteDef> { suite }, null), false);

    public static void RunTest(SuiteDef suite, TestDef test) =>
        Start(new List<(SuiteDef, List<TestDef>)> { (suite, new List<TestDef> { test }) }, false);

    public static void RunFailed()
    {
        var plan = new List<(SuiteDef, List<TestDef>)>();
        foreach (var suite in Suites)
        {
            var tests = suite.Tests.FindAll(t => Latest.TryGetValue(Key(suite.Name, t.Name), out var r) && r.Status == TestStatus.Failed);
            if (tests.Count > 0)
                plan.Add((suite, tests));
        }

        Start(plan, false);
    }

    /// <summary>Server connect mode could not reach the server: record it as a failed test with a failed assert, write the
    /// results, and (for an unattended run) quit with a failure exit code instead of waiting at a menu.</summary>
    public static void ReportConnectFailure(string reason)
    {
        TestLabPlugin.Log?.LogError("ServerConnect failed: " + reason);
        var result = new TestResult { Suite = "ServerConnect", Test = "Connect", Status = TestStatus.Failed, Reason = reason };
        result.Asserts.Add(new AssertResult { Description = "connected to the test server", Passed = false, Detail = reason });
        LastRun = new List<TestResult> { result };
        _planned = new List<(string, string)> { (result.Suite, result.Test) };
        _runStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        WriteResults(complete: true);
        Status = "Failed: " + reason;
        var cfg = TestLabConfig.Instance;
        var unattended = CommandLine.Value("-testlab-run") != null || CommandLine.Has("-testlab-quit");
        var quit = !CommandLine.Has("-testlab-stay") && (unattended || cfg.ConnectQuitOnFailure.Value);
        var delay = unattended ? 1f : Mathf.Max(1f, cfg.ConnectQuitDelay.Value);

        BootNotice.Show(
            "DrakesTestLab could not connect to the test server.\n" + reason +
            (quit ? $"\nThe game closes in {delay:0} seconds (the result is saved)." : ""),
            sticky: true);

        // the menu was hidden for the connect: bring it back so there is always a way out
        if (FejdStartup.instance != null)
        {
            try { MenuSuppressor.Restore(FejdStartup.instance); }
            catch (Exception ex) { TestLabPlugin.Log?.LogWarning($"Could not restore the main menu: {ex.Message}"); }
        }

        if (quit && ToolboxHost.Instance != null)
            ToolboxHost.Instance.StartCoroutine(QuitAfter(delay));
    }

    private static IEnumerator QuitAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        TestLabPlugin.Log?.LogInfo("ServerConnect: closing the game after the failed connect.");
        Application.Quit(1);
    }

    /// <summary>[Automation] RunOnLaunch: once per game start, after the world and arena are ready.</summary>
    public static void MaybeRunOnLaunch()
    {
        // command line wins over the config:  -testlab-run all|SuiteA,SuiteB  [-testlab-stay] [-testlab-delay 0.5]
        var cli = CommandLine.Value("-testlab-run");
        var what = cli ?? TestLabConfig.Instance.RunOnLaunch.Value.Trim();

        // hot mode ([Automation] HotMode or -testlab-hot; off by default): stay open and take run / reset / quit
        // requests from config/DrakesTestLab/requests. Started once per game, with or without a launch run.
        if (!_hotPolling && RunRequests.Enabled && ToolboxHost.Instance != null)
        {
            _hotPolling = true;
            _quitOverride = false;
            ToolboxHost.Instance.StartCoroutine(RunRequests.Poll());
        }

        if (_ranOnLaunch || what.Length == 0)
            return;

        if (CommandLine.Has("-testlab-stay") || _hotPolling)
            _quitOverride = false;
        else if (cli != null || CommandLine.Has("-testlab-quit"))
            _quitOverride = true;
        var shots = CommandLine.Value("-testlab-screenshots");
        if (shots != null)
            _screenshotOverride = shots == "all" ? "Always" : shots;
        var delay = CommandLine.Value("-testlab-delay");
        if (delay != null && float.TryParse(delay, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            TestLabConfig.Instance.StepDelay.Value = d;

        _ranOnLaunch = true;
        Reload();
        var plan = BuildPlan(what);

        TestLabPlugin.Log?.LogInfo($"Automation: running {plan.Sum(p => p.Item2.Count)} test(s) from {plan.Count} suite(s) on launch ({what}).");
        // -testlab-attended: a person is at the keyboard (manual suites): checkpoints ask, manual-only tests run, the game stays open
        var attended = CommandLine.Has("-testlab-attended");
        Start(plan, unattended: !attended);
    }

    /// <summary>
    /// Hot mode: rescan suites and test hooks (a hot-swapped mod's new hooks win), then run "failed" (the tests that
    /// failed last time) or a list like the -testlab-run one. Unattended, so results are written the same way.
    /// </summary>
    internal static void RunRequested(string what)
    {
        Reload();
        List<(SuiteDef, List<TestDef>)> plan;
        if (what.Equals("failed", StringComparison.OrdinalIgnoreCase))
        {
            plan = new List<(SuiteDef, List<TestDef>)>();
            foreach (var suite in Suites)
            {
                var tests = suite.Tests.FindAll(t => Latest.TryGetValue(Key(suite.Name, t.Name), out var r) && r.Status == TestStatus.Failed);
                if (tests.Count > 0)
                    plan.Add((suite, tests));
            }
        }
        else
        {
            plan = BuildPlan(what);
        }

        TestLabPlugin.Log?.LogInfo($"Automation: rerunning {plan.Sum(p => p.Item2.Count)} test(s) from {plan.Count} suite(s) ({what}).");
        Start(plan, unattended: true);
    }

    /// <summary>"all", or suites and single tests: "SuiteA,SuiteB/N-01,SuiteB/N-03" (a test matches by the start of its name or file name).</summary>
    private static List<(SuiteDef, List<TestDef>)> BuildPlan(string what)
    {
        var plan = new List<(SuiteDef, List<TestDef>)>();
        if (what.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            plan = Plan(Suites, null);
        }
        else
        {
            var wanted = new Dictionary<SuiteDef, List<string>?>();
            foreach (var entry in what.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0))
            {
                var slash = entry.IndexOf('/');
                var suiteName = slash > 0 ? entry.Substring(0, slash) : entry;
                var suite = Suites.Find(s => s.Name.Equals(suiteName, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(s.Folder).Equals(suiteName, StringComparison.OrdinalIgnoreCase));
                // a suite or test that was asked for but is not there must not pass silently
                if (suite == null)
                {
                    _missing.Add(entry);
                    continue;
                }

                if (slash <= 0)
                {
                    wanted[suite] = null; // the whole suite
                    continue;
                }

                var prefix = entry.Substring(slash + 1);
                if (!suite.Tests.Exists(t => TestMatches(t, prefix)))
                    _missing.Add(entry);
                else if (!wanted.TryGetValue(suite, out var list) || list != null)
                    (wanted[suite] = list ?? new List<string>()).Add(prefix);
            }

            foreach (var kv in wanted)
                plan.Add((kv.Key, kv.Value == null ? new List<TestDef>(kv.Key.Tests) : kv.Key.Tests.FindAll(t => kv.Value.Exists(p => TestMatches(t, p)))));
        }

        return plan;
    }

    private static bool TestMatches(TestDef t, string prefix) =>
        t.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(t.File).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static List<(SuiteDef, List<TestDef>)> Plan(List<SuiteDef> suites, Func<TestDef, bool>? filter)
    {
        var plan = new List<(SuiteDef, List<TestDef>)>();
        foreach (var s in suites)
            plan.Add((s, filter == null ? new List<TestDef>(s.Tests) : s.Tests.FindAll(t => filter(t))));
        return plan;
    }

    private static void Start(List<(SuiteDef suite, List<TestDef> tests)> plan, bool unattended)
    {
        if (Running || ToolboxHost.Instance == null)
            return;
        ToolboxHost.Instance.StartCoroutine(Run(plan, unattended));
    }

    // ------------------------------------------------------------------ the run

    private static IEnumerator Run(List<(SuiteDef suite, List<TestDef> tests)> plan, bool unattended)
    {
        Running = true;
        StopRequested = false;
        LastRun = new List<TestResult>();
        _current = null;
        _planned = new List<(string, string)>();
        foreach (var (s, ts) in plan)
            foreach (var t in ts)
                _planned.Add((s.Name, t.Name));
        if (!_quitHooked)
        {
            Application.quitting += OnQuitting;
            _quitHooked = true;
        }

        _runStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        RunStartedAt = DateTime.Now;
        RunStartRealtime = Time.realtimeSinceStartup;
        RunWorldReadySeconds = 0f;
        RunFolder = Path.Combine(SuiteLoader.ResultsFolder, "run-" + _runStamp);
        PruneOldRuns();
        Status = "Waiting for the world to be ready...";
        if (unattended)
            ToolboxHost.Set(false);

        // the world, the arena and any auto-load case must be finished first
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

        RunWorldReadySeconds = Time.realtimeSinceStartup - RunStartRealtime;
        ModLoadTimer.Close();
        // zones around the arena that were not loaded when it was built still hold their trees
        var clearedAgain = ArenaBuilder.ClearAgain();
        if (clearedAgain > 0)
            TestLabPlugin.Log?.LogInfo($"Cleared {clearedAgain} more natural objects around the arena once the world had loaded.");
        // bushes, ferns and pebbles are clutter, not objects: the clear above cannot reach them
        DrakesTestLab.Bootstrap.Naturals.HideClutter();

        foreach (var name in _missing)
            Record(new TestResult { Suite = name, Test = "(suite)", Status = TestStatus.Failed, Reason = $"suite '{name}' was requested but is not in {SuiteLoader.Folder}" });
        _missing.Clear();

        foreach (var (suite, tests) in plan)
        {
            if (StopRequested)
                break;

            var skip = suite.LoadError.Length > 0 ? suite.LoadError : ModEnvironment.MissingMods(suite);
            if (skip != null)
            {
                foreach (var t in tests)
                    Record(new TestResult { Suite = suite.Name, Test = t.Name, Status = TestStatus.Skipped, Reason = skip });
                continue;
            }

            foreach (var o in suite.ConfigOverrides)
            {
                var err = ModEnvironment.Set(o.Guid, o.Section, o.Key, o.Value);
                if (err.Length > 0)
                    TestLabPlugin.Log?.LogWarning($"Suite '{suite.Name}': {err}");
            }

            foreach (var test in tests)
            {
                if (StopRequested)
                    break;

                if (test.LoadError.Length > 0)
                {
                    Record(new TestResult { Suite = suite.Name, Test = test.Name, Status = TestStatus.Failed, Reason = "could not read the test: " + test.LoadError });
                    continue;
                }

                if (unattended && test.ManualOnly)
                {
                    Record(new TestResult { Suite = suite.Name, Test = test.Name, Status = TestStatus.Skipped, Reason = "manual-only test" });
                    continue;
                }

                // drive the test by hand: an exception thrown inside a nested coroutine would end this whole run silently
                // (the game sits there with nothing running), so a crash fails this one test and the run goes on
                var routine = RunOne(suite, test, unattended);
                while (true)
                {
                    bool more;
                    try
                    {
                        more = routine.MoveNext();
                    }
                    catch (Exception ex)
                    {
                        RecordRunnerCrash(suite, test, ex);
                        break;
                    }

                    if (!more)
                        break;
                    yield return routine.Current;
                }
            }

            ModEnvironment.RestoreAll();
        }

        ModEnvironment.RestoreAll();
        WaitingForNext = false;
        CheckpointPending = false;
        WriteResults(complete: !StopRequested);
        _current = null;
        Running = false;
        Status = StopRequested ? "Stopped. " + LastSummary : "Done. " + LastSummary;
        TestLabPlugin.Log?.LogInfo("Automation: " + Status);

        if (unattended && (_quitOverride ?? TestLabConfig.Instance.QuitWhenDone.Value))
        {
            var failed = LastRun.Exists(r => r.Status == TestStatus.Failed);
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(failed ? 1 : 0);
        }
    }

    /// <summary>Where RunOne is, so a crash says in which part of the test it happened.</summary>
    private static string _stage = "";

    private static void RecordRunnerCrash(SuiteDef suite, TestDef test, Exception ex)
    {
        TestLabPlugin.Log?.LogError($"The runner crashed in {suite.Name}/{test.Name} ({_stage}): {ex}");
        try
        {
            ModEnvironment.TestScope = false;
            ModEnvironment.RestoreTest();
            HeldKeys.ReleaseAll();
            VirtualPeers.LeaveAll();
        }
        catch (Exception cleanup)
        {
            TestLabPlugin.Log?.LogWarning($"Clean-up after the crash failed too: {cleanup.Message}");
        }

        var r = _current != null && _current.Suite == suite.Name && _current.Test == test.Name
            ? _current
            : new TestResult { Suite = suite.Name, Test = test.Name };
        r.Status = TestStatus.Failed;
        r.Reason = $"the lab's runner crashed while {_stage}: {ex.GetType().Name}: {ex.Message} (full trace in LogOutput.log)";
        r.EndedAt = DateTime.Now;
        Record(r);
    }

    private static IEnumerator RunOne(SuiteDef suite, TestDef test, bool unattended)
    {
        _stage = "starting";
        var result = new TestResult { Suite = suite.Name, Test = test.Name, Status = TestStatus.Running };
        Latest[Key(suite.Name, test.Name)] = result;
        _current = result;
        var timeout = test.TimeoutSeconds > 0 ? test.TimeoutSeconds : TestLabConfig.Instance.TestTimeout.Value;
        var ctx = new RunContext
        {
            Suite = suite,
            Test = test,
            Result = result,
            StartedAt = DateTime.Now,
            Deadline = Time.realtimeSinceStartup + timeout,
            Unattended = unattended
        };
        var started = Time.realtimeSinceStartup;
        result.StartedAt = DateTime.Now;
        result.StartRealtime = started;
        // the census takes a noticeable moment: do it before the frame mark so it is not counted as a hitch of the test
        if (TestLabConfig.Instance.CountObjects.Value)
            result.CensusBefore = ObjectCensus.Take();
        result.FrameMark = FrameProfiler.Mark();
        Status = $"{suite.Name} / {test.Name}: setting up";

        _stage = "resetting";
        if (suite.ResetBetweenTests)
        {
            StateReset.Run();
            yield return null;
        }

        // settings a setup changes (case "configs") last only for this test, like setConfig
        _stage = "applying the setup";
        ModEnvironment.TestScope = true;
        if (suite.Setup != null)
            yield return CaseRunner.ApplyCase(suite.Setup, suite.Name + " setup", quick: true);
        if (test.Setup != null)
            yield return CaseRunner.ApplyCase(test.Setup, test.Name + " setup", quick: true);

        _stage = "clearing the area";
        var clearSpec = test.ClearArea ?? suite.ClearArea;
        if (clearSpec != null && clearSpec.Enabled)
            ctx.Log($"clearArea: removed {clearSpec.Apply()} natural objects");

        _stage = "running the steps";
        result.SetupSeconds = Time.realtimeSinceStartup - started;
        string failure = "";
        var delay = TestLabConfig.Instance.StepDelay.Value;
        ModEnvironment.TestScope = true;
        for (var i = 0; i < test.Steps.Count && failure.Length == 0; i++)
        {
            if (StopRequested) { failure = "stopped"; break; }
            if (ctx.TimedOut) { failure = $"timed out after {timeout:0} s"; break; }

            var step = test.Steps[i];
            var label = $"step {i + 1}/{test.Steps.Count}: {J.Describe(step)}";
            Status = $"{suite.Name} / {test.Name}: {label}";

            if (StepMode && !unattended)
            {
                WaitingForNext = true;
                while (WaitingForNext && !StopRequested)
                    yield return null;
            }

            ctx.Log(label);
            var stepStart = Time.realtimeSinceStartup;
            var stepFrameMark = FrameProfiler.Mark();
            IEnumerator? routine = null;
            try
            {
                routine = Steps.Execute(step, ctx);
            }
            catch (StepFailed ex)
            {
                failure = $"{label} failed: {ex.Message}";
            }
            catch (Exception ex)
            {
                failure = $"{label} crashed: {ex.InnerException?.Message ?? ex.Message}";
            }

            if (routine != null)
            {
                // run the step's coroutine by hand so its errors can be caught
                while (true)
                {
                    bool more;
                    try
                    {
                        more = routine.MoveNext();
                    }
                    catch (StepFailed ex)
                    {
                        failure = $"{label} failed: {ex.Message}";
                        break;
                    }
                    catch (Exception ex)
                    {
                        failure = $"{label} crashed: {ex.InnerException?.Message ?? ex.Message}";
                        break;
                    }

                    if (!more)
                        break;
                    yield return routine.Current;
                }
            }

            // the step's own time, before the configured delay between steps
            result.StepTimes.Add((label, Time.realtimeSinceStartup - stepStart));
            result.StepFrames.Add(FrameProfiler.Compute(stepFrameMark, FrameProfiler.Mark()));
            result.StepMem.Add(MemoryProfiler.Compute(stepFrameMark, FrameProfiler.Mark()));

            if (failure.Length == 0 && delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
        }

        // a step that waits stops quietly at the deadline; a timeout during the last step must still fail the test
        if (failure.Length == 0 && ctx.TimedOut)
            failure = $"timed out after {timeout:0} s";

        if (failure.Length == 0)
        {
            _stage = "checking the results";
            Status = $"{suite.Name} / {test.Name}: checking";
            foreach (var a in test.Asserts)
                result.Asserts.Add(Asserts.Evaluate(a, ctx));
        }

        var anyFailed = failure.Length > 0 || result.Asserts.Exists(r => !r.Passed);
        result.Reason = failure.Length > 0 ? failure : anyFailed ? FirstFailedAssert(result) : "";
        if (test.ExpectFailure && failure != "stopped")
        {
            // a negative test (does the runner catch this?): failing the expected way is a pass
            var expected = test.ExpectFailureContains;
            var asExpected = anyFailed && (expected.Length == 0 || result.Reason.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0);
            result.Reason = asExpected ? "failed as expected: " + result.Reason
                : anyFailed ? $"failed, but not with '{expected}': {result.Reason}"
                : "expected this test to fail, but it passed";
            anyFailed = !asExpected;
        }
        result.Status = anyFailed ? TestStatus.Failed : TestStatus.Passed;
        var mode = ScreenshotMode;
        if (mode == "Always" || (anyFailed && mode == "Failures"))
        {
            // taken before cleanup so the picture shows the test's end state; the file is written at the end of the frame
            Screenshot(ctx, anyFailed ? "failure" : "proof");
            yield return null;
            yield return null;
        }

        _stage = "cleaning up";
        foreach (var go in ctx.Spawned)
        {
            if (go != null && ZNetScene.instance != null)
                ZNetScene.instance.Destroy(go);
        }

        VirtualPeers.LeaveAll();
        ModEnvironment.TestScope = false;
        ModEnvironment.RestoreTest();
        HeldKeys.ReleaseAll();
        LanguageSwitch.Restore();

        _stage = "writing the result";
        result.Seconds = Time.realtimeSinceStartup - started;
        result.EndedAt = DateTime.Now;
        result.Frames = FrameProfiler.Compute(result.FrameMark, FrameProfiler.Mark());
        result.Mem = MemoryProfiler.Compute(result.FrameMark, FrameProfiler.Mark());
        if (result.CensusBefore != null)
            result.CensusAfter = ObjectCensus.Take();
        Record(result);
        TestLabPlugin.Log?.LogInfo($"Test {suite.Name}/{test.Name}: {result.Status}{(result.Reason.Length > 0 ? " - " + result.Reason : "")}");
    }

    private static string FirstFailedAssert(TestResult r)
    {
        var a = r.Asserts.Find(x => !x.Passed);
        return a == null ? "" : $"{a.Description} -> {a.Detail}";
    }

    /// <summary>Records a finished test and rewrites the results files, so a run cut short keeps what it did.</summary>
    private static void Record(TestResult r)
    {
        LastRun.Add(r);
        Latest[Key(r.Suite, r.Test)] = r;
        if (r == _current)
            _current = null;
        WriteResults(complete: false);
    }

    /// <summary>A "clean" screenshot step hides the Testing banner until this frame (the picture is taken at the end of a frame).</summary>
    public static int CleanShotUntilFrame = -1;

    public static void Screenshot(RunContext ctx, string name)
    {
        try
        {
            var folder = RunFolder.Length > 0 ? RunFolder : SuiteLoader.ResultsFolder;
            Directory.CreateDirectory(folder);
            // test names and labels can be long sentences: keep the file name well under the Windows path limit
            var fileName = $"{Safe(ctx.Suite.Name)}_{Cut(Safe(ctx.Test.Name), 40)}_{ctx.Result.Screenshots.Count + 1:00}_{Cut(Safe(name), 60)}.png";
            var file = Path.Combine(folder, fileName);
            ScreenCapture.CaptureScreenshot(file);
            ctx.Result.Screenshot = file;
            ctx.Result.Screenshots.Add(Path.GetFileName(folder) + "/" + fileName);
            ctx.Log($"  screenshot: {file}");
        }
        catch (Exception ex)
        {
            ctx.Log($"  screenshot failed: {ex.Message}");
        }
    }

    /// <summary>Screenshots add up (~2 MB each): keep the newest [Automation] KeepRuns run folders and their reports.</summary>
    private static void PruneOldRuns()
    {
        var keep = TestLabConfig.Instance.KeepRuns.Value;
        if (keep <= 0 || !Directory.Exists(SuiteLoader.ResultsFolder))
            return;
        try
        {
            var runs = new List<string>(Directory.GetDirectories(SuiteLoader.ResultsFolder, "run-*"));
            runs.Sort(StringComparer.Ordinal);
            for (var i = 0; i < runs.Count - (keep - 1); i++)
            {
                var stamp = Path.GetFileName(runs[i]).Substring(4);
                Directory.Delete(runs[i], true);
                foreach (var old in new[] { $"report-{stamp}.html", $"results-{stamp}.json" })
                {
                    var f = Path.Combine(SuiteLoader.ResultsFolder, old);
                    if (File.Exists(f))
                        File.Delete(f);
                }
            }
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Cleaning old test runs failed: {ex.Message}");
        }
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max).TrimEnd('_');

    private static string Safe(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    // ------------------------------------------------------------------ results files

    /// <summary>
    /// latest.* and this run's results-/report- files. Written after every test with complete = false (the JSON says
    /// "complete": false and leaves out the tests not run yet), and once more at the end.
    /// </summary>
    private static void WriteResults(bool complete)
    {
        var passed = LastRun.FindAll(r => r.Status == TestStatus.Passed).Count;
        var failed = LastRun.FindAll(r => r.Status == TestStatus.Failed).Count;
        var skipped = LastRun.FindAll(r => r.Status == TestStatus.Skipped).Count;
        LastSummary = $"{passed} passed, {failed} failed, {skipped} skipped.";

        try
        {
            Directory.CreateDirectory(SuiteLoader.ResultsFolder);
            var stamp = _runStamp.Length > 0 ? _runStamp : DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var json = ToJson(passed, failed, skipped, complete);
            var file = Path.Combine(SuiteLoader.ResultsFolder, $"results-{stamp}.json");
            File.WriteAllText(file, json);
            File.WriteAllText(Path.Combine(SuiteLoader.ResultsFolder, "latest.json"), json);
            File.WriteAllText(Path.Combine(SuiteLoader.ResultsFolder, "latest.md"), ToMarkdown(passed, failed, skipped));
            var html = ResultsReport.Html(LastRun, passed, failed, skipped);
            File.WriteAllText(Path.Combine(SuiteLoader.ResultsFolder, "latest.html"), html);
            File.WriteAllText(Path.Combine(SuiteLoader.ResultsFolder, $"report-{stamp}.html"), html);
            LastResultsFile = file;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Writing test results failed: {ex.Message}");
        }
    }

    private static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static string ToJson(int passed, int failed, int skipped, bool complete)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"complete\": {(complete ? "true" : "false")},\n");
        sb.Append($"  \"planned\": {_planned.Count},\n");
        sb.Append($"  \"finished\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",\n");
        var inv = CultureInfo.InvariantCulture;
        var runTotal = RunStartedAt == default ? 0f : (float)(DateTime.Now - RunStartedAt).TotalSeconds;
        sb.Append($"  \"started\": \"{RunStartedAt:yyyy-MM-dd HH:mm:ss}\",\n");
        sb.Append($"  \"totalSeconds\": {runTotal.ToString("0.0", inv)},\n");
        sb.Append($"  \"gameStartToRunSeconds\": {RunStartRealtime.ToString("0.0", inv)},\n");
        sb.Append($"  \"worldReadySeconds\": {RunWorldReadySeconds.ToString("0.0", inv)},\n");
        sb.Append($"  \"memoryNowMb\": {{ \"mono\": {MemoryProfiler.CurrentMonoMb.ToString("0.0", inv)}, \"total\": {MemoryProfiler.CurrentTotalMb.ToString("0.0", inv)}, \"reserved\": {MemoryProfiler.CurrentReservedMb.ToString("0.0", inv)} }},\n");
        sb.Append($"  \"modLoadBeforeSeconds\": {ModLoadTimer.BeforeSeconds.ToString("0.00", inv)},\n");
        sb.Append($"  \"modLoad\": {ModLoadTimer.ToJson(Esc)},\n");
        sb.Append($"  \"passed\": {passed},\n  \"failed\": {failed},\n  \"skipped\": {skipped},\n");
        sb.Append("  \"tests\": [\n");
        for (var i = 0; i < LastRun.Count; i++)
        {
            var r = LastRun[i];
            sb.Append("    {\n");
            sb.Append($"      \"suite\": \"{Esc(r.Suite)}\",\n      \"test\": \"{Esc(r.Test)}\",\n");
            sb.Append($"      \"status\": \"{r.Status}\",\n      \"seconds\": {r.Seconds.ToString("0.00", CultureInfo.InvariantCulture)},\n");
            sb.Append($"      \"started\": \"{(r.StartedAt == default ? "" : r.StartedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture))}\",\n");
            sb.Append($"      \"ended\": \"{(r.EndedAt == default ? "" : r.EndedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture))}\",\n");
            sb.Append($"      \"setupSeconds\": {r.SetupSeconds.ToString("0.00", CultureInfo.InvariantCulture)},\n");
            sb.Append("      \"stepSeconds\": [");
            for (var k = 0; k < r.StepTimes.Count; k++)
                sb.Append(k == 0 ? "" : ", ").Append(r.StepTimes[k].Seconds.ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append("],\n");
            sb.Append($"      \"frames\": {r.Frames.ToJson()},\n");
            sb.Append($"      \"memory\": {r.Mem.ToJson()},\n");
            sb.Append($"      \"objects\": {MemoryProfiler.CensusJson(r.CensusBefore, r.CensusAfter, Esc)},\n");
            sb.Append("      \"stepMonoGrowthMb\": [");
            for (var k = 0; k < r.StepMem.Count; k++)
                sb.Append(k == 0 ? "" : ", ").Append(r.StepMem[k].MonoGrowthMb.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append("],\n");
            sb.Append("      \"stepWorstFrameMs\": [");
            for (var k = 0; k < r.StepFrames.Count; k++)
                sb.Append(k == 0 ? "" : ", ").Append(r.StepFrames[k].MaxMs.ToString("0.0", CultureInfo.InvariantCulture));
            sb.Append("],\n");
            sb.Append($"      \"reason\": \"{Esc(r.Reason)}\",\n      \"screenshot\": \"{Esc(r.Screenshot)}\",\n");
            sb.Append("      \"screenshots\": [");
            for (var k = 0; k < r.Screenshots.Count; k++)
                sb.Append(k == 0 ? "" : ", ").Append($"\"{Esc(r.Screenshots[k])}\"");
            sb.Append("],\n");
            sb.Append("      \"asserts\": [");
            for (var k = 0; k < r.Asserts.Count; k++)
            {
                var a = r.Asserts[k];
                sb.Append(k == 0 ? "\n" : ",\n");
                sb.Append($"        {{ \"check\": \"{Esc(a.Description)}\", \"passed\": {(a.Passed ? "true" : "false")}, \"detail\": \"{Esc(a.Detail)}\" }}");
            }

            sb.Append(r.Asserts.Count > 0 ? "\n      ],\n" : "],\n");
            sb.Append("      \"steps\": [");
            for (var k = 0; k < r.StepLog.Count; k++)
                sb.Append(k == 0 ? "\n" : ",\n").Append($"        \"{Esc(r.StepLog[k])}\"");
            sb.Append(r.StepLog.Count > 0 ? "\n      ]\n" : "]\n");
            sb.Append(i < LastRun.Count - 1 ? "    },\n" : "    }\n");
        }

        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    private static string ToMarkdown(int passed, int failed, int skipped)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# DrakesTestLab results {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"**{passed} passed, {failed} failed, {skipped} skipped**");
        sb.AppendLine();
        var total = RunStartedAt == default ? 0.0 : (DateTime.Now - RunStartedAt).TotalSeconds;
        sb.AppendLine($"Run started {RunStartedAt:HH:mm:ss}, {total:0.0}s so far. Game start to run start {RunStartRealtime:0.0}s, world ready after {RunWorldReadySeconds:0.0}s.");
        sb.AppendLine();
        sb.AppendLine("| Suite | Test | Result | Started | Time | Frames | Memory | Reason |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in LastRun)
            sb.AppendLine($"| {r.Suite} | {r.Test} | {r.Status} | {(r.StartedAt == default ? "" : r.StartedAt.ToString("HH:mm:ss"))} | {r.Seconds:0.0}s | {r.Frames.Summary()} | {r.Mem.Summary()} | {r.Reason.Replace("|", "/")} |");

        var mods = new List<ModLoadTimer.Entry>(ModLoadTimer.All);
        if (mods.Count > 0)
        {
            mods.Sort((a, b) => b.Seconds.CompareTo(a.Seconds));
            sb.AppendLine();
            sb.AppendLine(ModLoadTimer.FromPatcher
                ? $"## Mod load times (every mod; the first started {ModLoadTimer.BeforeSeconds:0.0}s after the game process did)"
                : $"## Mod load times (only mods loaded after DrakesTestLab; {ModLoadTimer.BeforeSeconds:0.0}s passed before them. Install DrakesTestLab.Preload for all)");
            sb.AppendLine();
            sb.AppendLine("| Mod | Load |");
            sb.AppendLine("|---|---|");
            foreach (var m in mods)
                sb.AppendLine($"| {m.Name} | {m.Seconds:0.00}s |");
        }

        return sb.ToString();
    }
}
