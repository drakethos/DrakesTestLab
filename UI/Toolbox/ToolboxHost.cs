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
    private Rect _window = new(30f, 30f, 860f, 680f);
    private bool _resizing;
    private static readonly string[] Groups = { "Play", "Scenario", "Tests", "Tools" };

    /// <summary>Small coloured labels in the window header (recording, a test running, who you play as).</summary>
    public static readonly List<System.Func<(string text, Color color)?>> Chips = new();
    private Vector2 _scroll;
    private int _tab;
    private GUISkin? _skin;

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
        _tabs.Add(new RecordTab());
        _tabs.Add(spawn);
        _tabs.Add(new InspectTab(spawn));
        _tabs.Add(new ModsTab());
        _tabs.Add(new LogTab());
        _tabs.Add(new SessionTab());
        LogCapture.Start();

        Chips.Add(() => Automation.Recorder.Recording ? ($"REC  {Automation.Recorder.Steps.Count} steps", Ui.Danger) : null);
        Chips.Add(() => Automation.TestRunner.Running ? ("Testing", Ui.Accent) : null);
        Chips.Add(() => WardTools.IsSwitched ? ("Playing as " + WardTools.Me().Name, Ui.Good) : null);
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
        Automation.Recorder.Tick();
        LabBar.Tick();
        foreach (var tab in _tabs)
            tab.Tick();
    }

    private void OnGUI()
    {
        var cfg = TestLabConfig.Instance;
        if (!IsOpen && !cfg.ShowOverlay.Value && !CraftHelper.ShouldShow() && !Automation.UiPicker.Active && !Automation.TestRunner.Running && !Automation.Recorder.Recording)
            return;

        EnsureSkin();
        var oldSkin = GUI.skin;
        var oldMatrix = GUI.matrix;
        GUI.skin = _skin;
        var s = Mathf.Clamp(cfg.UiScale.Value, 0.6f, 2.5f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        if (CraftHelper.ShouldShow())
            CraftHelper.Draw(s);

        if (Automation.TestRunner.Running && !IsOpen && !LabBar.Visible)
        {
            var keys = $"{cfg.StopTestsKey.Value} stop" + (Automation.TestRunner.StepMode ? $" / {cfg.NextStepKey.Value} next step" : "");
            GUI.Box(new Rect(Screen.width / s / 2f - 330f, 30f, 660f, 44f), GUIContent.none);
            GUI.Label(new Rect(Screen.width / s / 2f - 322f, 33f, 644f, 40f),
                "<color=#f0b45a><b>Testing</b></color> " + Automation.TestRunner.Status + "  <color=#999999>(" + keys + ")</color>");
        }

        Automation.UiPicker.DrawOverlay();

        LabBar.Draw(s);

        if (cfg.ShowOverlay.Value)
            GUI.Label(new Rect(10f, 4f, 1400f, 24f), "<color=#f0b45a>" + InspectTab.StatusLine() + "</color>");

        if (IsOpen)
        {
            _window.width = Mathf.Clamp(_window.width, 560f, Screen.width / s - 10f);
            _window.height = Mathf.Clamp(_window.height, 360f, Screen.height / s - 10f);
            _window = GUI.Window(WindowId, _window, DrawWindow, GUIContent.none);
        }

        GUI.matrix = oldMatrix;
        GUI.skin = oldSkin;
    }

    private void DrawWindow(int id)
    {
        const float headerH = 40f, sideW = 150f, footerH = 22f;

        // header: title, status chips, close
        GUILayout.BeginArea(new Rect(14f, 8f, _window.width - 28f, headerH - 8f));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Drakes Test Lab", Ui.TitleStyle, GUILayout.ExpandWidth(false));
        GUILayout.Space(10f);
        foreach (var chip in Chips)
        {
            if (chip() is not { } c)
                continue;
            GUILayout.Label($"<color={Ui.Html(c.color)}>{c.text}</color>", Ui.Chip, GUILayout.ExpandWidth(false), GUILayout.Height(20f));
            GUILayout.Space(4f);
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(60f)))
            Set(false);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // sidebar: tabs grouped Play / Scenario / Tests / Tools
        GUILayout.BeginArea(new Rect(0f, headerH, sideW, _window.height - headerH), Ui.SideStyle);
        foreach (var group in Groups)
        {
            GUILayout.Label(group.ToUpperInvariant(), Ui.NavGroup);
            for (var i = 0; i < _tabs.Count; i++)
            {
                if (_tabs[i].Group != group)
                    continue;
                if (GUILayout.Button(_tabs[i].Title + _tabs[i].Badge, i == _tab ? Ui.NavSelected : Ui.NavItem))
                {
                    _tab = i;
                    _scroll = Vector2.zero;
                }
            }
        }
        GUILayout.EndArea();

        // the tab, as a column of cards
        GUILayout.BeginArea(new Rect(sideW + 10f, headerH, _window.width - sideW - 16f, _window.height - headerH - footerH));
        _scroll = GUILayout.BeginScrollView(_scroll);
        Ui.BeginTab();
        try
        {
            _tabs[_tab].Draw();
        }
        finally
        {
            Ui.EndTab();
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.Label(new Rect(sideW + 12f, _window.height - footerH, _window.width - sideW - 40f, footerH), InspectTab.StatusLine(), Ui.HintStyle);

        // resize from the bottom-right corner
        var grip = new Rect(_window.width - 18f, _window.height - 18f, 18f, 18f);
        GUI.Label(grip, $"<color={Ui.Html(Ui.Muted)}>//</color>");
        var e = Event.current;
        if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
        {
            _resizing = true;
            e.Use();
        }
        else if (_resizing && e.type == EventType.MouseDrag)
        {
            _window.width += e.delta.x;
            _window.height += e.delta.y;
            e.Use();
        }
        else if (e.rawType == EventType.MouseUp)
        {
            _resizing = false;
        }

        if (!_resizing)
            GUI.DragWindow(new Rect(0f, 0f, 10000f, headerH));
    }

    private void EnsureSkin()
    {
        if (_skin != null)
            return;

        _skin = Ui.MakeSkin(GUI.skin);
    }
}
