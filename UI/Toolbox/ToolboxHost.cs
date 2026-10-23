using System.Collections.Generic;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>IMGUI host for the test toolbox window and the always-on info overlay.</summary>
internal sealed class ToolboxHost : MonoBehaviour
{
    private const int WindowId = 0x7E57;

    private readonly List<ToolboxTab> _tabs = new();
    private Rect _window = new(30f, 30f, 600f, 700f);
    private Vector2 _scroll;
    private int _tab;
    private GUISkin? _skin;
    private Texture2D? _dark;

    public static ToolboxHost? Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    private void Awake()
    {
        Instance = this;
        var spawn = new SpawnTab();
        _tabs.Add(new WorldTab());
        _tabs.Add(new PlayerTab());
        _tabs.Add(new LabTab());
        _tabs.Add(new CasesTab());
        _tabs.Add(new WardsTab());
        _tabs.Add(new TestsTab());
        _tabs.Add(spawn);
        _tabs.Add(new InspectTab(spawn));
        _tabs.Add(new ModsTab());
        _tabs.Add(new LogTab());
        _tabs.Add(new SessionTab());
        LogCapture.Start();
    }

    public static void Toggle() => Set(!IsOpen);

    /// <summary>Opens the toolbox on the tab with this title.</summary>
    public static void ShowTab(string title)
    {
        if (Instance == null)
            return;

        var index = Instance._tabs.FindIndex(t => t.Title == title);
        if (index >= 0)
            Instance._tab = index;
        Set(true);
    }

    public static void Set(bool open)
    {
        if (IsOpen == open)
            return;

        IsOpen = open; // input blocking and the free cursor are handled by Patches/ToolboxInputPatches.cs
    }

    private void Update()
    {
        LogCapture.Instance.Drain();
        Automation.UiPicker.Update();
        foreach (var tab in _tabs)
            tab.Tick();
    }

    private void OnGUI()
    {
        var cfg = TestLabConfig.Instance;
        if (!IsOpen && !cfg.ShowOverlay.Value && !CraftHelper.ShouldShow() && !Automation.UiPicker.Active && !Automation.TestRunner.Running)
            return;

        EnsureSkin();
        var oldSkin = GUI.skin;
        var oldMatrix = GUI.matrix;
        GUI.skin = _skin;
        var s = Mathf.Clamp(cfg.UiScale.Value, 0.6f, 2.5f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        if (CraftHelper.ShouldShow())
            CraftHelper.Draw(s);

        if (Automation.TestRunner.Running && !IsOpen)
        {
            var keys = $"{cfg.StopTestsKey.Value} stop" + (Automation.TestRunner.StepMode ? $" / {cfg.NextStepKey.Value} next step" : "");
            GUI.Box(new Rect(Screen.width / s / 2f - 330f, 30f, 660f, 44f), GUIContent.none);
            GUI.Label(new Rect(Screen.width / s / 2f - 322f, 33f, 644f, 40f),
                "<color=#f0b45a><b>Testing</b></color> " + Automation.TestRunner.Status + "  <color=#999999>(" + keys + ")</color>");
        }

        Automation.UiPicker.DrawOverlay();

        if (cfg.ShowOverlay.Value)
            GUI.Label(new Rect(10f, 4f, 1400f, 24f), "<color=#f0b45a>" + InspectTab.StatusLine() + "</color>");

        if (IsOpen)
        {
            _window.width = Mathf.Min(_window.width, Screen.width / s - 10f);
            _window.height = Mathf.Min(_window.height, Screen.height / s - 10f);
            _window = GUILayout.Window(WindowId, _window, DrawWindow, "Drakes Test Lab", GUILayout.Width(_window.width), GUILayout.Height(_window.height));
        }

        GUI.matrix = oldMatrix;
        GUI.skin = oldSkin;
    }

    private void DrawWindow(int id)
    {
        var titles = new string[_tabs.Count];
        for (var i = 0; i < _tabs.Count; i++)
            titles[i] = _tabs[i].Title + _tabs[i].Badge;

        _tab = GUILayout.SelectionGrid(_tab, titles, 5);
        GUILayout.Space(4);

        _scroll = GUILayout.BeginScrollView(_scroll);
        _tabs[_tab].Draw();
        GUILayout.EndScrollView();

        GUILayout.Label(InspectTab.StatusLine());
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
    }

    private void EnsureSkin()
    {
        if (_skin != null)
            return;

        _dark = new Texture2D(1, 1);
        _dark.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.1f, 0.97f));
        _dark.Apply();

        _skin = Instantiate(GUI.skin);
        _skin.window.normal.background = _dark;
        _skin.window.onNormal.background = _dark;
        _skin.window.normal.textColor = Color.white;
        _skin.window.onNormal.textColor = Color.white;
        _skin.window.fontSize = 14;
        _skin.label.richText = true;
        _skin.label.fontSize = 13;
        _skin.label.wordWrap = true;
        _skin.button.fontSize = 12;
        _skin.button.richText = true;
        _skin.toggle.fontSize = 13;
        _skin.textField.fontSize = 13;
    }
}
