using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI.Toolbox;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>
/// The clipboard window (right click while holding the clipboard): what you were looking at when it opened, with actions, and the
/// notes with theirs. While it is open the game takes no input and the cursor is free (ToolboxInputPatches).
/// </summary>
internal static class ClipboardMenu
{
    private const int WindowId = 0x7E5A;

    public static bool IsOpen { get; private set; }
    public static Target? Target { get; private set; }

    private static Rect _window = new(0f, 0f, 560f, 600f);
    private static Vector2 _scroll;
    private static bool _placed;

    public static void Open(Target? target)
    {
        Target = target;
        IsOpen = true;
        _scroll = Vector2.zero;
        Clipboard.Load();
    }

    public static void Close() => IsOpen = false;

    public static void Toggle(Target? target)
    {
        if (IsOpen) Close();
        else Open(target);
    }

    public static void Draw(float scale)
    {
        if (!IsOpen)
            return;
        var w = Screen.width / scale;
        var h = Screen.height / scale;
        _window.width = Mathf.Min(560f, w - 20f);
        _window.height = Mathf.Min(620f, h - 20f);
        if (!_placed)
        {
            _window.x = (w - _window.width) / 2f;
            _window.y = (h - _window.height) / 2f;
            _placed = true;
        }
        var oldBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 1f, 1f, Mathf.Clamp(TestLabConfig.Instance.UiOpacity.Value, 0.3f, 1f));
        _window = GUI.Window(WindowId, _window, DrawWindow, GUIContent.none);
        GUI.backgroundColor = oldBg;
    }

    private static void DrawWindow(int id)
    {
        GUI.backgroundColor = new Color(1f, 1f, 1f, Mathf.Clamp(TestLabConfig.Instance.UiOpacity.Value, 0.3f, 1f));
        GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
        GUILayout.BeginHorizontal(Ui.SideStyle);
        GUILayout.Label("<b>Lab Clipboard</b>", Ui.TitleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{Clipboard.Notes.Count} note(s)", Ui.HintStyle);
        if (GUILayout.Button("Close", GUILayout.Width(64f)))
            Close();
        GUILayout.EndHorizontal();

        GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 8, 8) });
        Ui.BeginTab();
        DrawTarget();
        DrawNotes();
        Ui.EndTab();
        GUILayout.EndVertical();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 30f));
    }

    private static void DrawTarget()
    {
        var t = Target;
        if (t == null || !t.Alive)
        {
            Ui.Header("Looking at");
            Ui.Hint("Nothing in reach. Close, aim at something and right click again.");
            return;
        }

        Ui.Header($"Looking at: {Readout.StripTags(t.Title)}");
        GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{t.Kind}</color>   <b>{t.Prefab}</b>");
        foreach (var line in Readout.Lines(t, Clipboard.Worn))
        {
            if (line.Key == "Prefab")
                continue;
            GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{line.Key}</color>   {line.Value}");
        }

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        if (Ui.PrimaryButton("Note it"))
            Clipboard.Add(t);
        if (GUILayout.Button("Copy id"))
        {
            Ui.Copy(t.Prefab);
            Clipboard.Say($"Copied {t.Prefab}");
        }
        if (t.Kind != "Terrain" && GUILayout.Button(t.Kind == "Item" ? "Give me one" : "Spawn another"))
            Clipboard.Spawn(new Note { Prefab = t.Prefab, Kind = t.Kind });
        if (t.Kind != "Terrain" && GUILayout.Button("Spawn tab"))
        {
            ToolboxHost.ShowTab("Spawn");
            SelectInSpawnTab(t.Prefab);
            Close();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Go there"))
        {
            Clipboard.Teleport(t);
            Close();
        }
        var wear = t.Kind != "Terrain" ? t.Root.GetComponent<WearNTear>() : null;
        var creature = t.Kind is "Creature" or "Player" ? t.Root.GetComponent<Character>() : null;
        if (wear != null || creature != null)
        {
            if (GUILayout.Button(wear != null ? "Repair" : "Heal"))
                Clipboard.Repair(t);
            if (GUILayout.Button("Hurt 50%"))
                Clipboard.Hurt(t, 0.5f);
        }
        if (t.Kind != "Terrain" && t.Kind != "Player" && Ui.DangerButton("Destroy"))
        {
            Clipboard.Destroy(t);
            Target = null;
        }
        GUILayout.EndHorizontal();
    }

    private static void DrawNotes()
    {
        Ui.Header("Notes");
        if (Clipboard.Notes.Count == 0)
        {
            Ui.Hint("Empty. Hold the clipboard and left click on anything to note it down.");
            return;
        }

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(180f), GUILayout.MaxHeight(330f));
        Note? remove = null;
        foreach (var note in Clipboard.Notes)
        {
            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(0, 0, 2, 4) });
            GUILayout.BeginHorizontal();
            GUILayout.Label(note.Kind, Ui.Chip, GUILayout.Width(66f));
            GUILayout.Label($"<b>{note.Name}</b>  <color={Ui.Html(Ui.Muted)}>{note.Prefab}</color>");
            GUILayout.FlexibleSpace();
            GUILayout.Label(note.When, Ui.HintStyle, GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            if (note.Info.Length > 0)
                GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{note.Info}   {note.Pos.x:0},{note.Pos.y:0},{note.Pos.z:0}</color>");
            GUILayout.BeginHorizontal();
            if (note.Kind != "Terrain" && GUILayout.Button(note.Kind == "Item" ? "Give" : "Spawn", GUILayout.Width(64f)))
                Clipboard.Spawn(note);
            if (note.Kind != "Terrain" && GUILayout.Button("Spawn tab", GUILayout.Width(84f)))
            {
                ToolboxHost.ShowTab("Spawn");
                SelectInSpawnTab(note.Prefab);
                Close();
            }
            if (GUILayout.Button("Go there", GUILayout.Width(72f)))
            {
                Clipboard.GoTo(note);
                Close();
            }
            if (GUILayout.Button("Copy id", GUILayout.Width(64f)))
            {
                Ui.Copy(note.Prefab);
                Clipboard.Say($"Copied {note.Prefab}");
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("x", GUILayout.Width(26f)))
                remove = note;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        GUILayout.EndScrollView();
        if (remove != null)
            Clipboard.Remove(remove);

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy all"))
        {
            Ui.Copy(Clipboard.AsText());
            Clipboard.Say($"Copied {Clipboard.Notes.Count} note(s)");
        }
        if (GUILayout.Button("Save as text"))
            Clipboard.SaveText();
        GUILayout.FlexibleSpace();
        if (Ui.DangerButton("Clear all"))
            Clipboard.Clear();
        GUILayout.EndHorizontal();
        Ui.Hint($"Notes are kept in {Clipboard.FilePath}");
    }

    private static void SelectInSpawnTab(string prefab)
    {
        var spawn = ToolboxHost.Instance != null ? ToolboxHost.Instance.FindTab<SpawnTab>() : null;
        spawn?.Select(prefab);
    }
}
