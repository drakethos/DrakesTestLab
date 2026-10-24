using System;
using System.Collections.Generic;
using DrakesTestLab.Automation;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>A key with optional modifiers, written like "F7", "Shift+F7", "Ctrl+Alt+F7". Modifiers must match exactly.</summary>
internal readonly struct KeyCombo
{
    public readonly KeyCode Key;
    public readonly bool Shift, Ctrl, Alt;
    public readonly bool Valid;

    private KeyCombo(KeyCode key, bool shift, bool ctrl, bool alt)
    {
        Key = key;
        Shift = shift;
        Ctrl = ctrl;
        Alt = alt;
        Valid = key != KeyCode.None;
    }

    public static KeyCombo Parse(string text)
    {
        bool shift = false, ctrl = false, alt = false;
        var key = KeyCode.None;
        foreach (var raw in (text ?? "").Split('+'))
        {
            var part = raw.Trim();
            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
            else if (Enum.TryParse<KeyCode>(part, true, out var k)) key = k;
        }

        return new KeyCombo(key, shift, ctrl, alt);
    }

    public bool Pressed()
    {
        if (!Valid || !Input.GetKeyDown(Key))
            return false;
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        var alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        return shift == Shift && ctrl == Ctrl && alt == Alt;
    }
}

/// <summary>
/// The light bar at the top of the screen while you record a test or answer a manual check, so the big lab window is only needed
/// at the start and the end. Every button has a key (shown on it); the buttons can also be clicked whenever the cursor is free
/// (inventory open). F7 is the lab's action key: next step in step mode, Pass at a manual check, check what you look at while recording.
/// </summary>
internal static class LabBar
{
    private static readonly List<(string label, string key, Action action, bool danger)> Buttons = new();

    public static bool Visible => !ToolboxHost.IsOpen && (Recorder.Recording || TestRunner.CheckpointPending || (TestRunner.Running && TestRunner.WaitingForNext));

    /// <summary>Every frame: the bar's keys (the lab window being closed, as the bar only shows then).</summary>
    public static void Tick()
    {
        if (ToolboxHost.IsOpen)
            return;
        var cfg = TestLabConfig.Instance;

        if (TestRunner.CheckpointPending)
        {
            if (KeyCombo.Parse(cfg.ManualPassKey.Value).Pressed()) TestRunner.AnswerCheckpoint(true);
            else if (KeyCombo.Parse(cfg.ManualFailKey.Value).Pressed()) TestRunner.AnswerCheckpoint(false);
            return;
        }

        if (TestRunner.Running && TestRunner.WaitingForNext)
        {
            if (KeyCombo.Parse(cfg.NextStepKey.Value).Pressed()) TestRunner.Next();
            return;
        }

        if (!Recorder.Recording)
            return;
        if (KeyCombo.Parse(cfg.RecorderCheckKey.Value).Pressed()) Recorder.CheckLookedAt();
        else if (KeyCombo.Parse(cfg.RecorderInventoryKey.Value).Pressed()) Recorder.CheckInventory();
        else if (KeyCombo.Parse(cfg.RecorderScreenshotKey.Value).Pressed()) { Recorder.Screenshot(); Recorder.LastMessage = "Screenshot step added."; }
        else if (KeyCombo.Parse(cfg.RecorderPauseKey.Value).Pressed()) Recorder.TogglePause();
    }

    public static void Draw(float scale)
    {
        if (!Visible)
            return;
        var cfg = TestLabConfig.Instance;
        Buttons.Clear();
        string title, detail;

        if (TestRunner.CheckpointPending)
        {
            title = $"<color={Ui.Html(Ui.Accent)}><b>CHECK</b></color>  {TestRunner.CheckpointMessage}";
            detail = "Look, then answer:";
            Buttons.Add(("Pass", cfg.ManualPassKey.Value, () => TestRunner.AnswerCheckpoint(true), false));
            Buttons.Add(("Fail", cfg.ManualFailKey.Value, () => TestRunner.AnswerCheckpoint(false), true));
        }
        else if (TestRunner.Running && TestRunner.WaitingForNext)
        {
            title = $"<color={Ui.Html(Ui.Accent)}><b>STEP MODE</b></color>  {TestRunner.Status}";
            detail = "Waiting for you before the next step.";
            Buttons.Add(("Next step", cfg.NextStepKey.Value, TestRunner.Next, false));
            Buttons.Add(("Stop tests", cfg.StopTestsKey.Value, TestRunner.Stop, true));
        }
        else
        {
            var state = Recorder.Paused ? $"<color={Ui.Html(Ui.Muted)}><b>PAUSED</b></color>" : $"<color={Ui.Html(Ui.Danger)}><b>● REC</b></color>";
            title = $"{state}  {Recorder.Steps.Count} steps, {Recorder.Checks.Count} checks";
            detail = Recorder.LastMessage.Length > 0 ? Recorder.LastMessage : "Play the scenario; every action becomes a step.";
            Buttons.Add(("Check what I look at", cfg.RecorderCheckKey.Value, Recorder.CheckLookedAt, false));
            Buttons.Add(("Check my inventory", cfg.RecorderInventoryKey.Value, Recorder.CheckInventory, false));
            Buttons.Add(("Screenshot", cfg.RecorderScreenshotKey.Value, Recorder.Screenshot, false));
            Buttons.Add((Recorder.Paused ? "Resume" : "Pause", cfg.RecorderPauseKey.Value, Recorder.TogglePause, false));
            Buttons.Add(("Stop & save", cfg.MenuKey.Value, () => { Recorder.Stop(); ToolboxHost.ShowTab("Record"); }, true));
        }

        const float width = 760f, height = 92f;
        var rect = new Rect(Screen.width / scale / 2f - width / 2f, 14f, width, height);
        GUILayout.BeginArea(rect, Ui.CardStyle);
        GUILayout.Label(title);
        GUILayout.Label(detail, Ui.HintStyle);
        GUILayout.BeginHorizontal();
        // the buttons are clickable whenever the cursor is free (inventory open); the keys always work
        foreach (var (label, key, action, danger) in Buttons)
        {
            var text = $"{label}  <color={Ui.Html(Ui.Muted)}>{key}</color>";
            if (danger ? GUILayout.Button(text, Ui.Danger_) : GUILayout.Button(text))
                action();
        }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
}
