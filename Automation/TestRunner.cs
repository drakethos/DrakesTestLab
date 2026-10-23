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
        TestHooks.Register("testlab.inventoryCount", _ => Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory().NrOfItems() : -1, "DrakesTestLab");
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
        ToolboxHost.ShowTab("Tests");
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

    /// <summary>[Automation] RunOnLaunch: once per game start, after the world and arena are ready.</summary>
    public static void MaybeRunOnLaunch()
    {
        // command line wins over the config:  -testlab-run all|SuiteA,SuiteB  [-testlab-stay] [-testlab-delay 0.5]
        var cli = CommandLine.Value("-testlab-run");
        var what = cli ?? TestLabConfig.Instance.RunOnLaunch.Value.Trim();
        if (_ranOnLaunch || what.Length == 0)
            return;

        if (CommandLine.Has("-testlab-stay"))
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
        List<SuiteDef> chosen;
        if (what.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            chosen = Suites;
        }
        else
        {
            var names = new HashSet<string>(what.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
            chosen = Suites.FindAll(s => names.Contains(s.Name.Trim()) || names.Contains(Path.GetFileName(s.Folder)));
            // a suite that was asked for but is not there must not pass silently
            foreach (var name in names)
            {
                if (!chosen.Exists(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(s.Folder).Equals(name, StringComparison.OrdinalIgnoreCase)))
                    _missing.Add(name);
            }
        }

        TestLabPlugin.Log?.LogInfo($"Automation: running {chosen.Count} suite(s) on launch ({what}).");
        // -testlab-attended: a person is at the keyboard (manual suites): checkpoints ask, manual-only tests run, the game stays open
        var attended = CommandLine.Has("-testlab-attended");
        Start(Plan(chosen, null), unattended: !attended);
    }

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

                yield return RunOne(suite, test, unattended);
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

    private static IEnumerator RunOne(SuiteDef suite, TestDef test, bool unattended)
    {
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
        Status = $"{suite.Name} / {test.Name}: setting up";

        if (suite.ResetBetweenTests)
        {
            StateReset.Run();
            yield return null;
        }

        if (suite.Setup != null)
            yield return CaseRunner.ApplyCase(suite.Setup, suite.Name + " setup", quick: true);
        if (test.Setup != null)
            yield return CaseRunner.ApplyCase(test.Setup, test.Name + " setup", quick: true);

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

            if (failure.Length == 0 && delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
        }

        // a step that waits stops quietly at the deadline; a timeout during the last step must still fail the test
        if (failure.Length == 0 && ctx.TimedOut)
            failure = $"timed out after {timeout:0} s";

        if (failure.Length == 0)
        {
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

        foreach (var go in ctx.Spawned)
        {
            if (go != null && ZNetScene.instance != null)
                ZNetScene.instance.Destroy(go);
        }

        ModEnvironment.TestScope = false;
        ModEnvironment.RestoreTest();
        HeldKeys.ReleaseAll();
        LanguageSwitch.Restore();

        result.Seconds = Time.realtimeSinceStartup - started;
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

    public static void Screenshot(RunContext ctx, string name)
    {
        try
        {
            var folder = RunFolder.Length > 0 ? RunFolder : SuiteLoader.ResultsFolder;
            Directory.CreateDirectory(folder);
            var fileName = $"{Safe(ctx.Suite.Name)}_{Safe(ctx.Test.Name)}_{ctx.Result.Screenshots.Count + 1:00}_{Safe(name)}.png";
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
        sb.Append($"  \"passed\": {passed},\n  \"failed\": {failed},\n  \"skipped\": {skipped},\n");
        sb.Append("  \"tests\": [\n");
        for (var i = 0; i < LastRun.Count; i++)
        {
            var r = LastRun[i];
            sb.Append("    {\n");
            sb.Append($"      \"suite\": \"{Esc(r.Suite)}\",\n      \"test\": \"{Esc(r.Test)}\",\n");
            sb.Append($"      \"status\": \"{r.Status}\",\n      \"seconds\": {r.Seconds.ToString("0.00", CultureInfo.InvariantCulture)},\n");
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
        sb.AppendLine("| Suite | Test | Result | Time | Reason |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var r in LastRun)
            sb.AppendLine($"| {r.Suite} | {r.Test} | {r.Status} | {r.Seconds:0.0}s | {r.Reason.Replace("|", "/")} |");
        return sb.ToString();
    }
}
