using System.Collections.Generic;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI.Toolbox;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>
/// The clipboard window (right click while holding the clipboard): an editor for what you were looking at when it opened (its
/// values, editable in place, and its raw ZDO data), copy / paste of an object's values, and the copies kept on the clipboard.
/// While it is open the game takes no input and the cursor is free (ToolboxInputPatches).
/// </summary>
internal static class ClipboardMenu
{
    private const int WindowId = 0x7E5A;

    public static bool IsOpen { get; private set; }
    public static Target? Target { get; private set; }

    private static Rect _window = new(0f, 0f, 600f, 680f);
    private static Vector2 _scroll;
    private static Vector2 _notesScroll;
    private static bool _placed;
    private static bool _showRaw;
    private static string _rawFilter = "";
    private static string _status = "";
    private static bool _statusBad;

    // what the user typed and has not set yet, by field id / raw key; the live value shows for everything else
    private static readonly Dictionary<string, string> Edits = new();

    public static void Open(Target? target)
    {
        Target = target;
        IsOpen = true;
        _scroll = Vector2.zero;
        Edits.Clear();
        _status = "";
        Clipboard.Load();
    }

    public static void Close()
    {
        IsOpen = false;
        Edits.Clear();
    }

    public static void Toggle(Target? target)
    {
        if (IsOpen) Close();
        else Open(target);
    }

    private static void Status(string? error, string ok)
    {
        _statusBad = error != null;
        _status = error ?? ok;
        if (error == null)
            Clipboard.Say(ok);
    }

    public static void Draw(float scale)
    {
        if (!IsOpen)
            return;
        var w = Screen.width / scale;
        var h = Screen.height / scale;
        _window.width = Mathf.Min(600f, w - 20f);
        _window.height = Mathf.Min(700f, h - 20f);
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
        GUILayout.Label($"{Clipboard.Notes.Count} cop{(Clipboard.Notes.Count == 1 ? "y" : "ies")}", Ui.HintStyle);
        if (GUILayout.Button("Close", GUILayout.Width(64f)))
            Close();
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 8, 8) });
        Ui.BeginTab();
        DrawTarget();
        DrawNotes();
        Ui.EndTab();
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 30f));
    }

    // ---- the object: editable values, raw data, actions ----

    private static void DrawTarget()
    {
        var t = Target;
        if (t == null || !t.Alive)
        {
            Ui.Header("Editing");
            Ui.Hint("Nothing in reach. Close, aim at something and right click again.");
            return;
        }

        Ui.Header($"Editing: {Readout.StripTags(t.Title)}");
        GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{t.Kind}</color>   <b>{t.Prefab}</b>   <color={Ui.Html(Ui.Muted)}>{t.Distance:0.0} m</color>");

        if (t.Kind == "Terrain")
        {
            Ui.Hint("Bare ground has no values to edit. Note it to keep its place, or go there.");
            GUILayout.BeginHorizontal();
            if (Ui.PrimaryButton("Note it"))
                Clipboard.Add(t);
            if (GUILayout.Button("Go there"))
            {
                Clipboard.Teleport(t);
                Close();
            }
            GUILayout.EndHorizontal();
            return;
        }

        DrawFields(t);
        DrawActions(t);
        DrawRaw(t);
        if (_status.Length > 0)
            GUILayout.Label($"<color={Ui.Html(_statusBad ? Ui.Danger : Ui.Good)}>{_status}</color>");
    }

    private static void DrawFields(Target t)
    {
        var fields = ObjectEditor.Fields(t);
        if (fields.Count == 0)
        {
            Ui.Hint("No values the clipboard knows how to edit here; the raw data below can still be changed.");
            return;
        }
        var pending = false;
        foreach (var f in fields)
        {
            var live = SafeGet(f);
            var has = Edits.TryGetValue("f:" + f.Id, out var typed);
            GUILayout.BeginHorizontal();
            GUILayout.Label(f.Label, GUILayout.Width(130f));
            if (f.ReadOnly)
            {
                GUILayout.Label(live);
            }
            else
            {
                var shown = has ? typed! : live;
                var now = GUILayout.TextField(shown, GUILayout.MinWidth(160f));
                if (now != shown)
                    Edits["f:" + f.Id] = now;
                var changed = Edits.TryGetValue("f:" + f.Id, out typed) && typed != live;
                pending |= changed;
                GUI.enabled = changed;
                if (GUILayout.Button("Set", GUILayout.Width(44f)))
                    Apply(t, f, typed!);
                GUI.enabled = true;
            }
            GUILayout.Label(f.Hint, Ui.HintStyle, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        GUI.enabled = pending;
        if (Ui.PrimaryButton("Set all changed"))
            foreach (var f in fields)
                if (!f.ReadOnly && Edits.TryGetValue("f:" + f.Id, out var typed) && typed != SafeGet(f))
                    Apply(t, f, typed);
        if (GUILayout.Button("Undo edits"))
            Edits.Clear();
        GUI.enabled = true;
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private static string SafeGet(EditField f)
    {
        try { return f.Get() ?? ""; }
        catch { return "?"; }
    }

    private static void Apply(Target t, EditField f, string value)
    {
        string? error;
        try { error = f.Set!(value); }
        catch (System.Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; }
        if (error == null)
            Edits.Remove("f:" + f.Id);
        Status(error != null ? $"{f.Label}: {error}" : null, $"{f.Label} of {t.Prefab} set to {value}");
    }

    private static void DrawActions(Target t)
    {
        GUILayout.Space(6f);
        var latest = Clipboard.LatestCopy();
        GUILayout.BeginHorizontal();
        if (Ui.PrimaryButton("Copy values"))
            Clipboard.Add(t);
        GUI.enabled = latest != null && t.Kind != "Player";
        if (GUILayout.Button(latest != null ? $"Paste from {Trim(latest.Name, 18)}" : "Paste (nothing copied)"))
        {
            var n = Clipboard.Paste(latest!, t);
            Status(n == 0 ? Clipboard.LastMessage : null, Clipboard.LastMessage);
            Edits.Clear();
        }
        GUI.enabled = true;
        if (GUILayout.Button(t.Kind == "Item" ? "Give me one" : "Spawn another"))
            Clipboard.Spawn(new Note { Prefab = t.Prefab, Kind = t.Kind });
        if (GUILayout.Button("Copy id"))
        {
            Ui.Copy(t.Prefab);
            Clipboard.Say($"Copied {t.Prefab}");
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Go there"))
        {
            Clipboard.Teleport(t);
            Close();
        }
        if (GUILayout.Button("Spawn tab"))
        {
            ToolboxHost.ShowTab("Spawn");
            SelectInSpawnTab(t.Prefab);
            Close();
        }
        var wear = t.Root.GetComponent<WearNTear>();
        var creature = t.Kind is "Creature" or "Player" ? t.Root.GetComponent<Character>() : null;
        if (wear != null || creature != null)
        {
            if (GUILayout.Button(wear != null ? "Repair" : "Heal"))
                Clipboard.Repair(t);
            if (GUILayout.Button("Hurt 50%"))
                Clipboard.Hurt(t, 0.5f);
        }
        if (t.Kind != "Player" && Ui.DangerButton("Destroy"))
        {
            Clipboard.Destroy(t);
            Target = null;
        }
        GUILayout.EndHorizontal();
    }

    private static void DrawRaw(Target t)
    {
        GUILayout.Space(4f);
        var raw = ObjectEditor.Raw(t);
        GUILayout.BeginHorizontal();
        _showRaw = GUILayout.Toggle(_showRaw, $" Raw data ({raw.Count} ZDO values)");
        if (_showRaw)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("filter", Ui.HintStyle);
            _rawFilter = GUILayout.TextField(_rawFilter, GUILayout.Width(120f));
        }
        GUILayout.EndHorizontal();
        if (!_showRaw)
            return;
        if (raw.Count == 0)
        {
            Ui.Hint(t.View == null ? "Not a networked object: it has no ZDO." : "The ZDO has no extra values.");
            return;
        }
        foreach (var r in raw)
        {
            if (_rawFilter.Length > 0 && r.Name.IndexOf(_rawFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{r.Type}</color> {r.Name}", GUILayout.Width(170f));
            if (!r.Editable)
            {
                GUILayout.Label(r.Type == 'b' ? $"<color={Ui.Html(Ui.Muted)}>{r.Text.Length * 3 / 4} bytes (copy / paste only)</color>" : $"<color={Ui.Html(Ui.Muted)}>quaternion</color>");
            }
            else
            {
                var has = Edits.TryGetValue(r.Key, out var typed);
                var shown = has ? typed! : r.Text;
                var now = GUILayout.TextField(shown, GUILayout.MinWidth(180f));
                if (now != shown)
                    Edits[r.Key] = now;
                var changed = Edits.TryGetValue(r.Key, out typed) && typed != r.Text;
                GUI.enabled = changed;
                if (GUILayout.Button("Set", GUILayout.Width(44f)))
                {
                    var error = ObjectEditor.SetRaw(t, r.Key, typed!);
                    if (error == null)
                        Edits.Remove(r.Key);
                    Status(error != null ? $"{r.Name}: {error}" : null, $"{r.Name} of {t.Prefab} set to {typed}");
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
        }
        Ui.Hint("Raw values are what the game saves and syncs. Components read most of them every frame; a few (level, tamed) only at start.");
    }

    // ---- the copies kept on the clipboard ----

    private static void DrawNotes()
    {
        Ui.Header("Copies");
        if (Clipboard.Notes.Count == 0)
        {
            Ui.Hint("Empty. Hold the clipboard and left click on anything to copy its values; shift + left click pastes the newest copy.");
            return;
        }

        _notesScroll = GUILayout.BeginScrollView(_notesScroll, GUILayout.MinHeight(160f), GUILayout.MaxHeight(300f));
        Note? remove = null;
        var target = Target != null && Target.Alive && Target.Kind is not ("Terrain" or "Player") ? Target : null;
        foreach (var note in Clipboard.Notes)
        {
            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(0, 0, 2, 4) });
            GUILayout.BeginHorizontal();
            GUILayout.Label(note.Kind, Ui.Chip, GUILayout.Width(66f));
            GUILayout.Label($"<b>{note.Name}</b>  <color={Ui.Html(Ui.Muted)}>{note.Prefab}</color>");
            GUILayout.FlexibleSpace();
            GUILayout.Label(note.Values.Count > 0 ? $"{note.Values.Count} values" : "", Ui.HintStyle, GUILayout.Width(66f));
            GUILayout.Label(note.When, Ui.HintStyle, GUILayout.Width(60f));
            GUILayout.EndHorizontal();
            if (note.Info.Length > 0)
                GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{note.Info}   {note.Pos.x:0},{note.Pos.y:0},{note.Pos.z:0}</color>");
            GUILayout.BeginHorizontal();
            if (target != null && note.Values.Count > 0 && GUILayout.Button("Paste here", GUILayout.Width(80f)))
            {
                var n = Clipboard.Paste(note, target);
                Status(n == 0 ? Clipboard.LastMessage : null, Clipboard.LastMessage);
                Edits.Clear();
            }
            if (note.Kind != "Terrain" && GUILayout.Button(note.Values.Count > 0 ? "Spawn copy" : note.Kind == "Item" ? "Give" : "Spawn", GUILayout.Width(84f)))
            {
                if (note.Values.Count > 0) Clipboard.SpawnCopy(note);
                else Clipboard.Spawn(note);
            }
            if (GUILayout.Button("Go there", GUILayout.Width(72f)))
            {
                Clipboard.GoTo(note);
                Close();
            }
            if (GUILayout.Button("Copy text", GUILayout.Width(76f)))
            {
                Ui.Copy(note.Detail());
                Clipboard.Say($"Copied {note.Name} as text");
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
        if (GUILayout.Button("Copy all as text"))
        {
            Ui.Copy(Clipboard.AsText());
            Clipboard.Say($"Copied {Clipboard.Notes.Count} cop{(Clipboard.Notes.Count == 1 ? "y" : "ies")} as text");
        }
        if (GUILayout.Button("Save as text"))
            Clipboard.SaveText();
        GUILayout.FlexibleSpace();
        if (Ui.DangerButton("Clear all"))
            Clipboard.Clear();
        GUILayout.EndHorizontal();
        Ui.Hint($"Copies are kept in {Clipboard.FilePath}");
    }

    private static string Trim(string s, int max) => s.Length > max ? s.Substring(0, max - 1) + "…" : s;

    private static void SelectInSpawnTab(string prefab)
    {
        var spawn = ToolboxHost.Instance != null ? ToolboxHost.Instance.FindTab<SpawnTab>() : null;
        spawn?.Select(prefab);
    }
}
