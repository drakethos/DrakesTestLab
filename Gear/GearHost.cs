using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DrakesTestLab.Automation;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI.Toolbox;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>
/// Runs the lab gear every frame: finds what the crosshair is on while the clipboard is held or the glasses are worn, reads the
/// clipboard's clicks (left = copy its values, shift + left = paste the newest copy onto it, right = the clipboard editor), and
/// draws the card by the crosshair and the window. The two items do different jobs: the glasses READ (the full readout of
/// anything in sight, hands free), the clipboard WRITES (edit, copy, paste, spawn copies); holding it alone shows only a short tag.
/// </summary>
internal sealed class GearHost : MonoBehaviour
{
    public static GearHost? Instance { get; private set; }

    /// <summary>What the crosshair is on right now (null when nothing, or the gear is not in use).</summary>
    public static Target? Current { get; private set; }

    private static readonly MethodInfo? TakeInputMethod = AccessTools.Method(typeof(Player), "TakeInput");

    private GUISkin? _skin;
    private float _nextRefresh;

    private const float ClipboardRange = 12f;

    private void Awake()
    {
        Instance = this;
        RegisterHooks();
    }

    private static bool Active => LabGear.Enabled && (Clipboard.Held || Clipboard.Worn);

    private void Update()
    {
        if (!LabGear.Enabled)
            return;
        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + 0.2f;
            Clipboard.Refresh();
        }

        if (ClipboardMenu.IsOpen && (!Clipboard.Held || ToolboxHost.IsOpen || (Menu.instance != null && Menu.IsVisible())))
            ClipboardMenu.Close();

        if (!Active || Player.m_localPlayer == null)
        {
            Current = null;
            return;
        }

        if (!ClipboardMenu.IsOpen)
            Current = Readout.Find(Clipboard.Worn ? Mathf.Max(5f, TestLabConfig.Instance.GlassesRange.Value) : ClipboardRange);

        if (!Clipboard.Held)
            return;
        if (ClipboardMenu.IsOpen)
        {
            if (Input.GetMouseButtonDown(1))
                ClipboardMenu.Close();
            return;
        }
        if (!TakeInput())
            return;
        if (Input.GetMouseButtonDown(0) && Current != null)
        {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                var copy = Clipboard.LatestCopy();
                if (copy == null) Clipboard.Say("Nothing copied yet: left click on something first");
                else Clipboard.Paste(copy, Current);
            }
            else
            {
                Clipboard.Add(Current);
            }
        }
        else if (Input.GetMouseButtonDown(1))
        {
            ClipboardMenu.Open(Current);
        }
    }

    private static bool TakeInput()
    {
        var player = Player.m_localPlayer;
        if (player == null || TakeInputMethod == null)
            return false;
        try { return TakeInputMethod.Invoke(player, null) is true; }
        catch { return false; }
    }

    private void OnGUI()
    {
        if (!Active || ToolboxHost.IsOpen || (Util.OrbitCamera.Active && !ClipboardMenu.IsOpen))
            return;
        var cfg = TestLabConfig.Instance;
        _skin ??= Ui.MakeSkin(GUI.skin);
        var oldSkin = GUI.skin;
        var oldMatrix = GUI.matrix;
        GUI.skin = _skin;
        var s = Mathf.Clamp(cfg.UiScale.Value, 0.6f, 2.5f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        if (ClipboardMenu.IsOpen)
            ClipboardMenu.Draw(s);
        else if (Current != null && Current.Alive)
            DrawCard(s);

        GUI.matrix = oldMatrix;
        GUI.skin = oldSkin;
    }

    /// <summary>
    /// The card to the right of the crosshair. Glasses: the full readout. Clipboard alone: a short tag (name, prefab) and what
    /// its buttons do there, since its job is editing, not reading.
    /// </summary>
    private static void DrawCard(float scale)
    {
        var t = Current!;
        var full = Clipboard.Worn;
        var width = full ? 380f : 280f;
        var x = Screen.width / scale / 2f + 36f;
        var y = Screen.height / scale / 2f - 16f;
        var oldBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 1f, 1f, 0.9f);
        GUILayout.BeginArea(new Rect(x, y, width, Screen.height / scale - y - 8f));
        GUILayout.BeginVertical(Ui.CardStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"<color={Ui.Html(Ui.Accent)}><b>{t.Title}</b></color>", Ui.TitleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(t.Kind, Ui.Chip);
        GUILayout.EndHorizontal();
        if (full)
        {
            foreach (var line in Readout.Lines(t, true))
                GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{line.Key}</color>   {line.Value}");
        }
        else
        {
            GUILayout.Label($"<color={Ui.Html(Ui.Muted)}>{t.Prefab}</color>", Ui.HintStyle);
        }
        if (Clipboard.Held)
        {
            var copy = t.Kind is "Terrain" or "Player" ? null : Clipboard.LatestCopy();
            var m = Ui.Html(Ui.Muted);
            var hint = t.Kind == "Terrain" ? $"<color={m}>LMB</color> note the spot" : $"<color={m}>LMB</color> copy values";
            if (copy != null) hint += $"   <color={m}>Shift+LMB</color> paste {copy.Name}";
            hint += $"   <color={m}>RMB</color> edit";
            GUILayout.Label(hint, Ui.HintStyle);
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
        GUI.backgroundColor = oldBg;
    }

    // ---- test hooks: suites drive the gear with "hook" steps ----

    private static void RegisterHooks()
    {
        const string source = "DrakesTestLab";
        TestHooks.Register("testlab.gear.give", _ => { LabGear.GiveAll(); return true; }, source);
        TestHooks.Register("testlab.gear.equip", _ => LabGear.EquipAll(), source);
        TestHooks.Register("testlab.gear.held", _ => { Clipboard.Refresh(); return Clipboard.Held; }, source);
        TestHooks.Register("testlab.gear.worn", _ => { Clipboard.Refresh(); return Clipboard.Worn; }, source);
        TestHooks.Register("testlab.gear.target", _ => { Look(); return Current?.Prefab ?? ""; }, source);
        TestHooks.Register("testlab.gear.title", _ => { Look(); return Current != null ? Readout.StripTags(Current.Title) : ""; }, source);
        // the readout of what the crosshair is on, as "Key: value | Key: value" ({"full": false} for the clipboard's short version)
        TestHooks.Register("testlab.gear.readout", args =>
        {
            Look();
            if (Current == null)
                return "";
            var full = !args.TryGetValue("full", out var f) || f is not bool b || b;
            var parts = new List<string>();
            foreach (var line in Readout.Lines(Current, full))
                parts.Add($"{line.Key}: {Readout.StripTags(line.Value)}");
            return string.Join(" | ", parts);
        }, source);
        TestHooks.Register("testlab.gear.note", _ =>
        {
            Look();
            if (Current == null)
                throw new InvalidOperationException("nothing under the crosshair to note");
            Clipboard.Add(Current);
            return Clipboard.Notes.Count;
        }, source);
        TestHooks.Register("testlab.gear.notes", _ => { Clipboard.Load(); return Clipboard.Notes.Count; }, source);
        // the clipboard editor: {"field": "health"} reads, {"field": "health", "value": "10"} sets (fields of what the crosshair is on)
        TestHooks.Register("testlab.gear.fields", _ =>
        {
            Look();
            if (Current == null)
                return "";
            var parts = new List<string>();
            foreach (var f in ObjectEditor.Fields(Current))
                parts.Add($"{f.Id}={f.Get()}");
            return string.Join(" | ", parts);
        }, source);
        TestHooks.Register("testlab.gear.set", args =>
        {
            Look();
            if (Current == null)
                throw new InvalidOperationException("nothing under the crosshair to edit");
            var field = args.TryGetValue("field", out var f) ? Convert.ToString(f, CultureInfo.InvariantCulture) ?? "" : "";
            var value = args.TryGetValue("value", out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
            var error = ObjectEditor.SetField(Current, field, value);
            if (error != null)
                throw new InvalidOperationException(error);
            foreach (var x in ObjectEditor.Fields(Current))
                if (x.Id == field) return x.Get();
            return value;
        }, source);
        TestHooks.Register("testlab.gear.copy", _ =>
        {
            Look();
            if (Current == null)
                throw new InvalidOperationException("nothing under the crosshair to copy");
            return Clipboard.Add(Current).Values.Count;
        }, source);
        TestHooks.Register("testlab.gear.paste", _ =>
        {
            Look();
            if (Current == null)
                throw new InvalidOperationException("nothing under the crosshair to paste onto");
            var copy = Clipboard.LatestCopy() ?? throw new InvalidOperationException("nothing copied");
            return Clipboard.Paste(copy, Current);
        }, source);
        TestHooks.Register("testlab.gear.lastNote", _ => { Clipboard.Load(); return Clipboard.Notes.Count > 0 ? Clipboard.Notes[0].Line : ""; }, source);
        TestHooks.Register("testlab.gear.clear", _ => { Clipboard.Clear(); return true; }, source);
        TestHooks.Register("testlab.gear.menu", args =>
        {
            var open = !args.TryGetValue("open", out var o) || o is not bool b || b;
            if (open) { Look(); ClipboardMenu.Open(Current); }
            else ClipboardMenu.Close();
            return ClipboardMenu.IsOpen;
        }, source);
        TestHooks.Register("testlab.gear.reload", _ => LabGear.Reload(), source);
        TestHooks.Register("testlab.gear.describe", _ => LabGear.Describe(), source);
        TestHooks.Register("testlab.gear.diagnoseScale", _ => LabGear.DiagnoseScale(), source);
        TestHooks.Register("testlab.gear.materials", _ => LabGear.DumpMaterials(), source);
        TestHooks.Register("testlab.gear.probe", args => LabGear.Probe(args.TryGetValue("clear", out var c) && c is true), source);
        TestHooks.Register("testlab.gear.probeGlasses", args => LabGear.ProbeGlasses(args.TryGetValue("clear", out var c) && c is true), source);
        // place a model by hand for tuning: {"item": "clipboard"|"glasses", "pos": [x,y,z], "rot": [x,y,z], "scale": 1}
        TestHooks.Register("testlab.gear.fit", args =>
        {
            var item = args.TryGetValue("item", out var i) ? Convert.ToString(i, CultureInfo.InvariantCulture) ?? "" : "";
            var fit = item.StartsWith("g", StringComparison.OrdinalIgnoreCase) ? LabGear.GlassesFit : LabGear.ClipboardFit;
            if (args.TryGetValue("pos", out var p) && p is List<object?> pl && pl.Count >= 3) fit.Pos = ToVec(pl);
            if (args.TryGetValue("rot", out var r) && r is List<object?> rl && rl.Count >= 3) fit.Rot = ToVec(rl);
            if (args.TryGetValue("scale", out var sc) && sc != null) fit.Scale = Convert.ToSingle(sc, CultureInfo.InvariantCulture);
            return LabGear.Reload();
        }, source);
    }

    private static Vector3 ToVec(List<object?> l) => new(Convert.ToSingle(l[0], CultureInfo.InvariantCulture), Convert.ToSingle(l[1], CultureInfo.InvariantCulture), Convert.ToSingle(l[2], CultureInfo.InvariantCulture));

    /// <summary>Hooks run between frames: look now, whether or not the gear is equipped.</summary>
    private static void Look()
    {
        Clipboard.Refresh();
        Current = Readout.Find(Mathf.Max(5f, TestLabConfig.Instance.GlassesRange.Value));
    }
}
