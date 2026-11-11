using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DrakesTestLab.Cases;
using DrakesTestLab.Util;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>One line on the clipboard: what was looked at, where, and the state it was in.</summary>
internal sealed class Note
{
    public string Prefab = "";
    public string Name = "";
    public string Kind = "";
    public string Info = "";
    public Vector3 Pos;
    public string Zdo = "";
    public string When = "";

    public Dictionary<string, object?> ToJson() => new()
    {
        ["prefab"] = Prefab, ["name"] = Name, ["kind"] = Kind, ["info"] = Info,
        ["x"] = Pos.x, ["y"] = Pos.y, ["z"] = Pos.z, ["zdo"] = Zdo, ["when"] = When
    };

    public static Note? FromJson(Dictionary<string, object?> d)
    {
        string S(string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
        float F(string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v, CultureInfo.InvariantCulture) : 0f;
        var prefab = S("prefab");
        if (prefab.Length == 0)
            return null;
        return new Note { Prefab = prefab, Name = S("name"), Kind = S("kind"), Info = S("info"), Pos = new Vector3(F("x"), F("y"), F("z")), Zdo = S("zdo"), When = S("when") };
    }

    public string Line => $"{Kind,-8} {Prefab,-28} {Name,-24} {Pos.x:0.#},{Pos.y:0.#},{Pos.z:0.#}  {Info}";
}

/// <summary>
/// The clipboard's notes and the things it can do to what you look at. Notes persist in config/DrakesTestLab/clipboard.json across
/// sessions. Held / Worn say whether the local player holds the clipboard / wears the glasses (refreshed by GearHost).
/// </summary>
internal static class Clipboard
{
    public static readonly List<Note> Notes = new();
    public static string LastMessage = "";

    public static bool Held { get; private set; }
    public static bool Worn { get; private set; }

    public static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "DrakesTestLab", "clipboard.json");
    public static string TextPath => Path.Combine(BepInEx.Paths.ConfigPath, "DrakesTestLab", "clipboard.txt");

    private static readonly System.Reflection.FieldInfo? RightItemField = AccessTools.Field(typeof(Humanoid), "m_rightItem");
    private static readonly System.Reflection.FieldInfo? UtilityItemField = AccessTools.Field(typeof(Humanoid), "m_utilityItem");
    private static bool _loaded;

    /// <summary>Re-reads what the local player holds and wears (the fields are protected in the live game: read by reflection).</summary>
    public static void Refresh()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            Held = Worn = false;
            return;
        }
        Held = Is(RightItemField?.GetValue(player) as ItemDrop.ItemData, LabGear.ClipboardPrefab);
        Worn = Is(UtilityItemField?.GetValue(player) as ItemDrop.ItemData, LabGear.GlassesPrefab);
    }

    private static bool Is(ItemDrop.ItemData? item, string prefab) =>
        item != null && item.m_dropPrefab != null && item.m_dropPrefab.name == prefab;

    // ---- notes ----

    public static Note Add(Target t)
    {
        Load();
        var note = new Note
        {
            Prefab = t.Prefab,
            Name = Readout.StripTags(t.Title),
            Kind = t.Kind,
            Info = Readout.Summary(t),
            Pos = t.Kind == "Terrain" ? t.Point : t.Root.transform.position,
            Zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO().m_uid.ToString() : "",
            When = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        };
        Notes.Insert(0, note);
        Save();
        Say($"Noted: {note.Name} ({note.Prefab})");
        return note;
    }

    public static void Remove(Note note)
    {
        Notes.Remove(note);
        Save();
    }

    public static void Clear()
    {
        Notes.Clear();
        Save();
    }

    public static string AsText()
    {
        var sb = new StringBuilder();
        foreach (var n in Notes)
            sb.AppendLine(n.Line);
        return sb.ToString();
    }

    public static void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        try
        {
            if (!File.Exists(FilePath))
                return;
            if (MiniJson.Parse(File.ReadAllText(FilePath)) is not List<object?> list)
                return;
            foreach (var raw in list)
                if (raw is Dictionary<string, object?> d && Note.FromJson(d) is { } note)
                    Notes.Add(note);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Clipboard: notes not read from {FilePath} ({ex.Message}).");
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var list = new List<object?>();
            foreach (var n in Notes)
                list.Add(n.ToJson());
            File.WriteAllText(FilePath, JsonWriter.Write(list));
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Clipboard: notes not saved ({ex.Message}).");
        }
    }

    public static string SaveText()
    {
        File.WriteAllText(TextPath, AsText());
        Say($"Saved {Notes.Count} note(s) to {TextPath}");
        return TextPath;
    }

    // ---- actions on a note ----

    public static void Spawn(Note note)
    {
        if (note.Kind == "Item")
        {
            ItemGiver.Give(note.Prefab, 1);
            Say($"Gave 1 x {note.Prefab}");
            return;
        }
        if (note.Kind == "Terrain")
            return;
        GameRefs.EnableCheats();
        GameRefs.RunConsoleCommand($"spawn {note.Prefab} 1 1");
        Say($"Spawned {note.Prefab}");
    }

    public static void Give(Note note)
    {
        ItemGiver.Give(note.Prefab, 1);
        Say($"Gave 1 x {note.Prefab}");
    }

    public static void GoTo(Note note)
    {
        var player = Player.m_localPlayer;
        if (player == null)
            return;
        var pos = note.Pos + Vector3.up * 0.5f;
        player.TeleportTo(pos, player.transform.rotation, false);
    }

    // ---- actions on what you look at ----

    public static void Destroy(Target t)
    {
        if (!t.Alive || t.Kind == "Terrain" || ZNetScene.instance == null)
            return;
        var name = t.Prefab;
        ZNetScene.instance.Destroy(t.Root);
        Say($"Destroyed {name}");
    }

    public static void Repair(Target t)
    {
        if (!t.Alive)
            return;
        var wear = t.Root.GetComponent<WearNTear>();
        if (wear != null)
        {
            wear.Repair();
            Say($"Repaired {t.Prefab}");
            return;
        }
        var c = t.Root.GetComponent<Character>();
        if (c != null)
        {
            c.SetHealth(c.GetMaxHealth());
            Say($"Healed {t.Prefab}");
        }
    }

    /// <summary>Takes a share of the object's health away (structures: through WearNTear; creatures: through SetHealth).</summary>
    public static void Hurt(Target t, float fraction)
    {
        if (!t.Alive)
            return;
        var wear = t.Root.GetComponent<WearNTear>();
        if (wear != null)
        {
            var zdo = t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;
            var cur = zdo != null ? zdo.GetFloat("health".GetStableHashCode(), wear.m_health) : wear.m_health;
            var hit = new HitData();
            hit.m_damage.m_damage = Mathf.Max(1f, cur * fraction);
            hit.m_point = t.Point;
            hit.m_toolTier = 100;
            wear.Damage(hit);
            Say($"Hit {t.Prefab} for {hit.m_damage.m_damage:0}");
            return;
        }
        var c = t.Root.GetComponent<Character>();
        if (c != null)
        {
            var hp = Mathf.Max(0f, c.GetHealth() * (1f - fraction));
            c.SetHealth(hp);
            Say($"{t.Prefab} set to {hp:0} HP");
        }
    }

    public static void Teleport(Target t)
    {
        var player = Player.m_localPlayer;
        if (player == null || !t.Alive)
            return;
        var pos = t.Kind == "Terrain" ? t.Point : t.Root.transform.position;
        player.TeleportTo(pos + Vector3.up * 0.5f, player.transform.rotation, false);
    }

    private static readonly System.Reflection.MethodInfo? ShowMessage = AccessTools.Method(typeof(MessageHud), "ShowMessage");

    /// <summary>A short message in the top left (MessageHud, called by name so a changed signature never throws).</summary>
    public static void Say(string text)
    {
        LastMessage = text;
        try
        {
            if (ShowMessage == null || MessageHud.instance == null)
                return;
            var ps = ShowMessage.GetParameters();
            var args = new object?[ps.Length];
            args[0] = MessageHud.MessageType.TopLeft;
            if (ps.Length > 1) args[1] = text;
            for (var i = 2; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
            ShowMessage.Invoke(MessageHud.instance, args);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogDebug($"Clipboard message not shown: {ex.Message}");
        }
    }
}
