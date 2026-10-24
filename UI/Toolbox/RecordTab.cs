using System.Collections.Generic;
using System.IO;
using System.Linq;
using DrakesTestLab.Automation;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Cases;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>Record what you do as a test: start, play, add checks, stop, save into a suite.</summary>
internal sealed class RecordTab : ToolboxTab
{
    private bool _withSetup = true;
    private string _checkText = "";
    private string _note = "";
    private string _suite = "MyTests";
    private string _name = "recorded-test";
    private string _description = "";
    private string _saved = "";

    public override string Title => "Record";
    public override string Group => "Tests";
    public override string Badge => Recorder.Recording ? "  ●" : "";

    public override void Draw()
    {
        var cfg = TestLabConfig.Instance;

        Ui.Header("Recorder");
        if (!Recorder.Recording)
        {
            Ui.Hint("Press Record, close the lab and play the scenario: craft, build, press E, click menus, move things into chests. " +
                    "Every action becomes a test step. Add checks as you go, then stop and save it as a test.");
            _withSetup = Ui.Toggle(_withSetup, "Start from the game as it is now (saves a setup: dials, inventory, buildings, mod settings, where you stand)");
            GUILayout.BeginHorizontal();
            GUI.enabled = Player.m_localPlayer != null && !TestRunner.Running;
            if (Ui.PrimaryButton(Recorder.Steps.Count > 0 ? "Record again" : "Record", GUILayout.Width(140)))
            {
                Recorder.Start(_withSetup);
                ToolboxHost.Set(false);
            }
            GUI.enabled = true;
            if (Recorder.Steps.Count > 0 && GUILayout.Button("Clear", GUILayout.Width(80)))
                Recorder.Clear();
            GUILayout.EndHorizontal();
        }
        else
        {
            Ui.State(Recorder.Paused ? $"Paused: {Recorder.Steps.Count} steps so far (nothing is recorded until you resume)." : $"Recording: {Recorder.Steps.Count} steps so far.", !Recorder.Paused);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Recorder.Paused ? "Resume" : "Pause", GUILayout.Width(100)))
                Recorder.TogglePause();
            if (Ui.DangerButton("Stop", GUILayout.Width(100)))
                Recorder.Stop();
            if (GUILayout.Button("Back to the game", GUILayout.Width(150)))
                ToolboxHost.Set(false);
            GUILayout.EndHorizontal();
            Ui.Hint($"In the game, {cfg.RecorderCheckKey.Value} adds checks for what the crosshair is on.");
        }

        if (Recorder.LastMessage.Length > 0)
            Ui.Hint(Recorder.LastMessage);

        if (Recorder.Recording)
        {
            Ui.Header("Add checks");
            Ui.Row(
                ("Check my inventory", Recorder.CheckInventory),
                ("Check what I look at", Recorder.CheckLookedAt),
                ("Screenshot here", Recorder.Screenshot));
            GUILayout.BeginHorizontal();
            _checkText = GUILayout.TextField(_checkText);
            if (GUILayout.Button("Check this text is on screen", GUILayout.Width(210)))
            {
                Recorder.CheckText(_checkText);
                _checkText = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            _note = GUILayout.TextField(_note);
            if (GUILayout.Button("Add a note to the report", GUILayout.Width(210)))
            {
                Recorder.Note(_note);
                _note = "";
            }
            GUILayout.EndHorizontal();
        }

        if (Recorder.Steps.Count > 0)
        {
            Ui.Header($"Steps ({Recorder.Steps.Count})");
            Ui.Hint("Remove anything you did by accident. You can edit the saved file afterwards too.");
            for (var i = 0; i < Recorder.Steps.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{i + 1}.</color> {J.Describe(Recorder.Steps[i])}");
                if (GUILayout.Button("x", GUILayout.Width(26)))
                {
                    Recorder.Steps.RemoveAt(i);
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }
        }

        if (!Recorder.Recording && Recorder.Steps.Count > 0)
        {
            Ui.Header("Save as a test");
            var suites = Directory.Exists(SuiteLoader.Folder)
                ? Directory.GetDirectories(SuiteLoader.Folder).Select(Path.GetFileName).Where(n => n.Length > 0 && !n.StartsWith("_")).OrderBy(n => n).ToList()
                : new List<string>();
            if (suites.Count > 0)
            {
                Ui.Hint("Suite: pick one, or type a new name.");
                Ui.Grid(suites, 3, n => _suite = n, _suite);
            }
            _suite = Ui.Field("Suite", _suite);
            _name = Ui.Field("Test name", _name);
            _description = Ui.Field("Description", _description);
            GUILayout.BeginHorizontal();
            if (Ui.PrimaryButton("Save test", GUILayout.Width(120)))
            {
                var path = Recorder.Save(_suite, _name, Recorder.BuildTest(_name, _description, includeSetup: true));
                _saved = $"Saved {path}. It shows in the Tests tab.";
                TestRunner.Reload();
            }
            if (GUILayout.Button("Copy JSON", GUILayout.Width(110)))
            {
                Ui.Copy(JsonWriter.Write(Recorder.BuildTest(_name, _description, includeSetup: true)));
                _saved = "Copied the test to the clipboard.";
            }
            GUILayout.EndHorizontal();
            if (_saved.Length > 0)
                Ui.Hint(_saved);
        }
    }
}
