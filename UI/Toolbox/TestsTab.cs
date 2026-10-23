using System.Collections.Generic;
using DrakesTestLab.Automation;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>Automated tests: suites per mod, run / step / stop, live status, results and the UI picker.</summary>
internal sealed class TestsTab : ToolboxTab
{
    private bool _loaded;
    private readonly HashSet<string> _expanded = new();
    private string _showDetails = "";

    public override string Title => "Tests";

    public override string Badge
    {
        get
        {
            if (TestRunner.Running)
                return " (running)";
            var failed = TestRunner.LastRun.FindAll(r => r.Status == TestStatus.Failed).Count;
            return failed > 0 ? $" ({failed} failed)" : "";
        }
    }

    public override void Draw()
    {
        if (!_loaded)
        {
            TestRunner.Reload();
            _loaded = true;
        }

        var cfg = TestLabConfig.Instance;

        // ---- run controls
        GUILayout.Label($"<b>{TestRunner.Status}</b>");
        if (TestRunner.CheckpointPending)
        {
            GUILayout.Label($"<color=#f0b45a><b>Check:</b> {TestRunner.CheckpointMessage}</color>");
            Ui.Row(("Pass", () => TestRunner.AnswerCheckpoint(true)), ("Fail", () => TestRunner.AnswerCheckpoint(false)));
        }

        GUILayout.BeginHorizontal();
        GUI.enabled = !TestRunner.Running;
        if (GUILayout.Button("Run all")) RunAndClose(() => TestRunner.RunAll());
        if (GUILayout.Button("Re-run failed")) RunAndClose(TestRunner.RunFailed);
        if (GUILayout.Button("Reload suites")) TestRunner.Reload();
        GUI.enabled = TestRunner.Running;
        if (GUILayout.Button("Stop")) TestRunner.Stop();
        GUI.enabled = TestRunner.Running && TestRunner.WaitingForNext;
        if (GUILayout.Button("Next step")) TestRunner.Next();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        TestRunner.StepMode = GUILayout.Toggle(TestRunner.StepMode, $"Step mode: wait for Next step ({cfg.NextStepKey.Value}) before every step");
        var delay = Ui.Slider("Delay between steps", cfg.StepDelay.Value, 0f, 3f, "0.00s");
        if (!Mathf.Approximately(delay, cfg.StepDelay.Value))
            cfg.StepDelay.Value = delay;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Screenshots:", GUILayout.Width(90f));
        foreach (var mode in new[] { "Off", "Failures", "Always" })
        {
            var on = GUILayout.Toggle(cfg.Screenshots.Value == mode, mode == "Always" ? "Always (proof)" : mode);
            if (on && cfg.Screenshots.Value != mode)
                cfg.Screenshots.Value = mode;
        }

        GUILayout.EndHorizontal();
        GUILayout.Label($"<color=#999999>Tests run with the toolbox closed so the game takes input; {cfg.StopTestsKey.Value} stops them. Unattended runs: [Automation] RunOnLaunch in the config.</color>");

        // ---- suites
        Ui.Header($"Suites ({TestRunner.Suites.Count})");
        GUILayout.Label($"<color=#999999>{SuiteLoader.Folder}</color>");
        Ui.Row(("Copy suites folder path", () => Ui.Copy(SuiteLoader.Folder)),
            ("Copy results folder path", () => Ui.Copy(SuiteLoader.ResultsFolder)),
            ("Documentation", () => Application.OpenURL("https://github.com/drakethos/DrakesTestLab/wiki/Automation")));

        foreach (var suite in TestRunner.Suites)
        {
            var gate = suite.LoadError.Length > 0 ? suite.LoadError : ModEnvironment.MissingMods(suite);
            var open = _expanded.Contains(suite.Name);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(open ? "-" : "+", GUILayout.Width(26)))
            {
                if (open) _expanded.Remove(suite.Name);
                else _expanded.Add(suite.Name);
            }

            GUILayout.Label($"<b>{suite.Name}</b>  {SuiteCounts(suite)}" + (gate != null ? $"  <color=#ff9060>({gate})</color>" : ""));
            GUI.enabled = !TestRunner.Running && gate == null;
            if (GUILayout.Button("Run suite", GUILayout.Width(100)))
                RunAndClose(() => TestRunner.RunSuite(suite));
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (!open)
                continue;

            if (suite.Description.Length > 0)
                GUILayout.Label($"<color=#999999>{suite.Description}</color>");

            foreach (var test in suite.Tests)
            {
                var key = TestRunner.Key(suite.Name, test.Name);
                TestRunner.Latest.TryGetValue(key, out var result);
                GUILayout.BeginHorizontal();
                GUILayout.Space(30);
                GUILayout.Label($"{StatusIcon(result)} {test.Name}" + (test.ManualOnly ? " <color=#999999>(manual)</color>" : ""));
                GUI.enabled = !TestRunner.Running && gate == null;
                if (GUILayout.Button("Run", GUILayout.Width(60)))
                    RunAndClose(() => TestRunner.RunTest(suite, test));
                GUI.enabled = result != null;
                if (GUILayout.Button(_showDetails == key ? "Hide" : "Details", GUILayout.Width(70)))
                    _showDetails = _showDetails == key ? "" : key;
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                if (test.Description.Length > 0)
                    GUILayout.Label($"      <color=#999999>{test.Description}</color>");
                if (test.LoadError.Length > 0)
                    GUILayout.Label($"      <color=#ff7070>Could not read: {test.LoadError}</color>");
                if (result != null && result.Reason.Length > 0)
                    GUILayout.Label($"      <color=#ffb070>{result.Reason}</color>");
                if (_showDetails == key && result != null)
                    DrawDetails(result);
            }
        }

        // ---- last run
        if (TestRunner.LastSummary.Length > 0)
        {
            Ui.Header("Last run");
            GUILayout.Label(TestRunner.LastSummary);
            if (TestRunner.LastResultsFile.Length > 0)
            {
                var report = System.IO.Path.Combine(SuiteLoader.ResultsFolder, "latest.html");
                GUILayout.Label($"<color=#999999>Report (open in a browser): {report}</color>");
                Ui.Row(("Copy report path", () => Ui.Copy(report)),
                    ("Copy results file path", () => Ui.Copy(TestRunner.LastResultsFile)));
            }
        }

        // ---- picker
        Ui.Header("UI picker");
        GUILayout.Label("Turn it on, close the toolbox, hover any game button or window and press " +
                        $"{cfg.PickerKey.Value}: its name is copied for a \"click\" step or a \"uiVisible\" check.");
        UiPicker.Active = GUILayout.Toggle(UiPicker.Active, "UI picker on");

        Ui.Header("Test hooks from mods");
        var hooks = TestHooks.Describe();
        GUILayout.Label(hooks.Count == 0
            ? "<color=#999999>None yet. A mod adds one by putting a [TestHook(\"name\")] attribute (its own copy) on a static method, or by calling DrakesTestLab.TestHooks.Register.</color>"
            : "<color=#999999>" + string.Join(", ", hooks) + "</color>");

        Ui.Header("Step and check names");
        GUILayout.Label("<color=#999999>Steps: " + string.Join(", ", Steps.Names) + "</color>");
        GUILayout.Label("<color=#999999>Checks: " + string.Join(", ", Asserts.Names) + "</color>");
    }

    private static void RunAndClose(System.Action run)
    {
        ToolboxHost.Set(false);
        run();
    }

    private static string SuiteCounts(SuiteDef suite)
    {
        int pass = 0, fail = 0, skip = 0;
        foreach (var t in suite.Tests)
        {
            if (!TestRunner.Latest.TryGetValue(TestRunner.Key(suite.Name, t.Name), out var r))
                continue;
            if (r.Status == TestStatus.Passed) pass++;
            else if (r.Status == TestStatus.Failed) fail++;
            else if (r.Status == TestStatus.Skipped) skip++;
        }

        var parts = $"{suite.Tests.Count} test(s)";
        if (pass + fail + skip > 0)
            parts += $": <color=#80ff80>{pass} passed</color>, <color=#ff7070>{fail} failed</color>, {skip} skipped";
        return parts;
    }

    private static string StatusIcon(TestResult? r) => r?.Status switch
    {
        TestStatus.Passed => "<color=#80ff80>PASS</color>",
        TestStatus.Failed => "<color=#ff7070>FAIL</color>",
        TestStatus.Skipped => "<color=#aaaaaa>SKIP</color>",
        TestStatus.Running => "<color=#f0b45a>....</color>",
        _ => "<color=#666666>----</color>"
    };

    private static void DrawDetails(TestResult r)
    {
        GUILayout.Label($"      <b>{r.Status}</b> in {r.Seconds:0.0}s");
        foreach (var a in r.Asserts)
            GUILayout.Label($"      {(a.Passed ? "<color=#80ff80>ok</color>" : "<color=#ff7070>FAIL</color>")} {a.Description} <color=#999999>({a.Detail})</color>");
        foreach (var line in r.StepLog)
            GUILayout.Label($"      <color=#999999>{line}</color>");
        if (r.Screenshot.Length > 0)
            Ui.Row(("Copy screenshot path", () => Ui.Copy(r.Screenshot)));
    }
}
