using System.Collections.Generic;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

internal sealed class WorldTab : ToolboxTab
{
    private float _tod = 0.5f;
    /// <summary>Game speed requested by this tab (also reset by the state reset).</summary>
    public static float TimeScale = 1f;
    private string _keyText = "";

    public override string Title => "World";

    public override string Group => "Play";

    public static string Clock(float fraction)
    {
        var minutes = Mathf.RoundToInt(fraction * 24f * 60f) % (24 * 60);
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    public override void Draw()
    {
        var env = EnvMan.instance;
        if (env == null)
        {
            GUILayout.Label("No world loaded yet.");
            return;
        }

        Ui.Header("Time of day");
        var frac = env.GetDayFraction();
        GUILayout.Label($"Day {env.GetDay()}   ~{Clock(frac)}   env: {env.GetCurrentEnvironment().m_name}");

        var frozen = env.m_debugTimeOfDay;
        var shown = frozen ? _tod : frac;
        var next = Ui.Slider("Time", shown, 0f, 1f, "0.000");
        if (!Mathf.Approximately(next, shown))
            SetTime(env, next);

        var wantFrozen = GUILayout.Toggle(frozen, "Freeze time at slider value");
        if (wantFrozen != frozen)
        {
            if (wantFrozen)
                SetTime(env, frac);
            else
                env.m_debugTimeOfDay = false;
        }

        Ui.Row(
            ("Midnight", () => SetTime(env, 0.0f)),
            ("Dawn", () => SetTime(env, 0.22f)),
            ("Morning", () => SetTime(env, 0.32f)),
            ("Noon", () => SetTime(env, 0.5f)),
            ("Dusk", () => SetTime(env, 0.76f)));
        Ui.Row(
            ("+1 hour", () => GameRefs.RunConsoleCommand($"skiptime {env.m_dayLengthSec / 24}")),
            ("+6 hours", () => GameRefs.RunConsoleCommand($"skiptime {env.m_dayLengthSec / 4}")),
            ("+1 day", () => GameRefs.RunConsoleCommand($"skiptime {env.m_dayLengthSec}")),
            ("Sleep to morning", () => GameRefs.RunConsoleCommand("sleep")));

        TimeScale = Ui.Slider("Game speed", TimeScale, 0f, 5f, "0.0x");
        Ui.Row(("Pause", () => TimeScale = 0f), ("1x", () => TimeScale = 1f), ("3x", () => TimeScale = 3f));
        if (!Mathf.Approximately(Time.timeScale, TimeScale))
            Time.timeScale = TimeScale;

        Ui.Header("Weather");
        var names = new List<string>();
        foreach (var e in env.m_environments)
            names.Add(e.m_name);

        Ui.Grid(names, 3, n => env.m_debugEnv = n, env.m_debugEnv);
        Ui.Row(
            ("Automatic weather", () => env.m_debugEnv = ""),
            ("Calm wind", () => GameRefs.RunConsoleCommand("wind 0 0")),
            ("Storm wind", () => GameRefs.RunConsoleCommand("wind 90 1")));

        Ui.Header("Random events");
        var events = new List<string>();
        if (RandEventSystem.instance != null)
        {
            foreach (var e in RandEventSystem.instance.m_events)
                events.Add(e.m_name);
        }

        Ui.Grid(events, 3, n => GameRefs.RunConsoleCommand("event " + n));
        Ui.Row(("Stop event", () => GameRefs.RunConsoleCommand("stopevent")));

        Ui.Header("World keys & map");
        Ui.Row(
            ("Unlock all pieces", () => GameRefs.RunConsoleCommand("setkey AllPiecesUnlocked")),
            ("Unlock all recipes", () => GameRefs.RunConsoleCommand("setkey AllRecipesUnlocked")));
        _keyText = Ui.Field("Key", _keyText);
        Ui.Row(
            ("Set key", () => GameRefs.RunConsoleCommand("setkey " + _keyText)),
            ("Remove key", () => GameRefs.RunConsoleCommand("removekey " + _keyText)),
            ("List keys", () => GameRefs.RunConsoleCommand("listkeys")),
            ("Reset keys", () => GameRefs.RunConsoleCommand("resetkeys")));
        Ui.Row(
            ("Explore map", () => GameRefs.RunConsoleCommand("exploremap")),
            ("Reset map", () => GameRefs.RunConsoleCommand("resetmap")));
    }

    private void SetTime(EnvMan env, float value)
    {
        _tod = Mathf.Clamp01(value);
        env.m_debugTimeOfDay = true;
        env.m_debugTime = _tod;
    }
}
