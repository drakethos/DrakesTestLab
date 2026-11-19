using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>One value of an object the clipboard can show and change: its current text and how to set it from text.</summary>
internal sealed class EditField
{
    public string Id = "";
    public string Label = "";
    public Func<string> Get = () => "";
    /// <summary>Sets the value from text; returns null when it worked, else why not.</summary>
    public Func<string, string?>? Set;
    public string Hint = "";

    public bool ReadOnly => Set == null;
}

/// <summary>One raw value in an object's ZDO (the networked save data every component reads its state from).</summary>
internal sealed class RawValue
{
    public char Type;      // f float, i int, l long, s string, v vector3, q quaternion, b byte array
    public int Hash;
    public string Name = "";
    public string Text = "";

    /// <summary>The key a copied value is stored under: type and hash, so it pastes back into the same slot.</summary>
    public string Key => Type + ":" + Hash.ToString(CultureInfo.InvariantCulture);
    public bool Editable => Type is 'f' or 'i' or 'l' or 's' or 'v';
}

/// <summary>
/// What the clipboard edits: the curated fields of the object under the crosshair (health, level, tamed, name, stack, quality,
/// durability, sign text, fuel, door, ward, picked, position, rotation) and the object's raw ZDO data. Copy takes every raw value
/// of an object; Paste writes them onto another one, then pokes the components that only read their state at start
/// (a creature's level and tamed flag, an item's data) so the change shows at once. Everything claims ownership first.
/// </summary>
internal static class ObjectEditor
{
    // ---- names for ZDO hashes: every ZDOVars.s_xxx field is "xxx".GetStableHashCode(), plus the names the lab reads itself ----

    private static Dictionary<int, string>? _names;

    private static readonly string[] ExtraNames =
    {
        "health", "maxHealth", "level", "tamed", "TamedName", "text", "fuel", "state", "enabled", "picked", "items", "itemData",
        "creator", "creatorName", "permitted", "item", "variant", "quality", "stack", "durability", "crafterName", "crafterID",
        "spawntime", "seed", "author", "authorDisplayName", "support", "haveTarget", "alert", "huntplayer", "patrol", "patrolPoint",
        "spawnpoint", "lastTime", "plantTime", "pickedTime", "addedDefaultItems", "inUse", "noise", "aggravated", "sleeping",
        "tag", "tagauthor", "TCData", "sex", "bakeTimer", "fermentStart", "content", "queued", "accTime", "StartTime", "LastAttack"
    };

    public static string NameOf(int hash)
    {
        if (_names == null)
        {
            _names = new Dictionary<int, string>();
            foreach (var n in ExtraNames)
                _names[n.GetStableHashCode()] = n;
            var vars = AccessTools.TypeByName("ZDOVars");
            if (vars != null)
            {
                foreach (var f in vars.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (f.FieldType != typeof(int) || !f.Name.StartsWith("s_", StringComparison.Ordinal))
                        continue;
                    try
                    {
                        if (f.GetValue(null) is int h && !_names.ContainsKey(h))
                            _names[h] = f.Name.Substring(2);
                    }
                    catch
                    {
                        // a static that throws on read is just a missing name
                    }
                }
            }
        }
        return _names.TryGetValue(hash, out var name) ? name : "#" + hash.ToString(CultureInfo.InvariantCulture);
    }

    private static int H(string name) => name.GetStableHashCode();

    private static ZDO? Zdo(Target t) => t.View != null && t.View.IsValid() ? t.View.GetZDO() : null;

    /// <summary>Takes ownership so the writes stick and sync (the owner's copy is the one that is sent).</summary>
    private static ZDO? Own(Target t)
    {
        var zdo = Zdo(t);
        if (zdo == null)
            return null;
        if (!t.View!.IsOwner())
            t.View.ClaimOwnership();
        return zdo;
    }

    // ---- curated fields ----

    public static List<EditField> Fields(Target t)
    {
        var list = new List<EditField>();
        if (!t.Alive || t.Kind == "Terrain")
            return list;
        void Try(Action add)
        {
            try { add(); }
            catch (Exception ex) { TestLabPlugin.Log?.LogDebug($"Clipboard editor: a field was skipped ({ex.Message})."); }
        }

        Try(() => CharacterFields(t, list));
        Try(() => StructureFields(t, list));
        Try(() => NaturalFields(t, list));
        Try(() => ItemFields(t, list));
        Try(() => SignFields(t, list));
        Try(() => FireFields(t, list));
        Try(() => DoorFields(t, list));
        Try(() => WardFields(t, list));
        Try(() => PickableFields(t, list));
        Try(() => TransformFields(t, list));
        return list;
    }

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool TryFloat(string s, out float v) => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryInt(string s, out int v) => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static bool TryBool(string s, out bool v)
    {
        var x = s.Trim().ToLowerInvariant();
        v = x is "1" or "true" or "yes" or "on" or "y";
        return v || x is "0" or "false" or "no" or "off" or "n";
    }

    private static string B(bool v) => v ? "yes" : "no";

    private static void CharacterFields(Target t, List<EditField> list)
    {
        var c = t.Root.GetComponent<Character>();
        if (c == null)
            return;
        list.Add(new EditField
        {
            Id = "health", Label = "Health", Get = () => F(c.GetHealth()), Hint = $"0 - {F(c.GetMaxHealth())}",
            Set = s => { if (!TryFloat(s, out var v)) return "not a number"; Own(t); c.SetHealth(Mathf.Max(0f, v)); return null; }
        });
        list.Add(new EditField
        {
            Id = "maxHealth", Label = "Max health", Get = () => F(c.GetMaxHealth()),
            Set = s => { if (!TryFloat(s, out var v) || v <= 0f) return "a number above 0"; Own(t); c.SetMaxHealth(v); return null; }
        });
        if (c is Player)
            return;
        list.Add(new EditField
        {
            Id = "level", Label = "Level (stars + 1)", Get = () => c.GetLevel().ToString(CultureInfo.InvariantCulture), Hint = "1 = no star",
            Set = s => { if (!TryInt(s, out var v) || v < 1) return "a whole number, 1 or more"; Own(t); c.SetLevel(v); return null; }
        });
        if (t.Root.GetComponent<Tameable>() != null || c.IsTamed())
        {
            list.Add(new EditField
            {
                Id = "tamed", Label = "Tamed", Get = () => B(c.IsTamed()), Hint = "yes / no",
                Set = s => { if (!TryBool(s, out var v)) return "yes or no"; Own(t); c.SetTamed(v); return null; }
            });
            list.Add(new EditField
            {
                Id = "tamedName", Label = "Name", Get = () => Zdo(t)?.GetString(H("TamedName"), "") ?? "",
                Set = s => { var z = Own(t); if (z == null) return "no ZDO"; z.Set(H("TamedName"), s); return null; }
            });
        }
    }

    private static void StructureFields(Target t, List<EditField> list)
    {
        var wear = t.Root.GetComponent<WearNTear>();
        if (wear == null)
            return;
        list.Add(new EditField
        {
            Id = "health", Label = "Health", Get = () => F(Zdo(t)?.GetFloat(H("health"), wear.m_health) ?? wear.m_health), Hint = $"max {F(wear.m_health)}",
            Set = s =>
            {
                if (!TryFloat(s, out var v)) return "not a number";
                var z = Own(t);
                if (z == null) return "no ZDO";
                if (v <= 0f) { ZNetScene.instance?.Destroy(t.Root); return null; }
                z.Set(H("health"), Mathf.Min(v, wear.m_health));
                return null;
            }
        });
    }

    private static void NaturalFields(Target t, List<EditField> list)
    {
        if (t.Root.GetComponent<WearNTear>() != null || t.Root.GetComponent<Character>() != null)
            return;
        float max;
        if (t.Root.GetComponent<TreeBase>() is { } tree) max = tree.m_health;
        else if (t.Root.GetComponent<Destructible>() is { } d) max = d.m_health;
        else if (t.Root.GetComponent<TreeLog>() is { } log) max = log.m_health;
        else return;
        list.Add(new EditField
        {
            Id = "health", Label = "Health", Get = () => F(Zdo(t)?.GetFloat(H("health"), max) ?? max), Hint = $"max {F(max)}",
            Set = s => { if (!TryFloat(s, out var v)) return "not a number"; var z = Own(t); if (z == null) return "no ZDO"; z.Set(H("health"), Mathf.Max(0f, v)); return null; }
        });
    }

    private static readonly MethodInfo? ItemDropSave = AccessTools.Method(typeof(ItemDrop), "Save");

    /// <summary>After m_itemData changed: write it to the ZDO (ItemDrop.Save is private in the live game).</summary>
    private static void SaveItem(ItemDrop drop)
    {
        if (ItemDropSave != null)
            ItemDropSave.Invoke(drop, null);
        else if (drop.GetComponent<ZNetView>() is { } v && v.IsValid())
            ItemDrop.SaveToZDO(drop.m_itemData, v.GetZDO());
    }

    private static void ItemFields(Target t, List<EditField> list)
    {
        var drop = t.Root.GetComponent<ItemDrop>();
        if (drop == null)
            return;
        var d = drop.m_itemData;
        list.Add(new EditField
        {
            Id = "stack", Label = "Stack", Get = () => d.m_stack.ToString(CultureInfo.InvariantCulture), Hint = $"1 - {d.m_shared.m_maxStackSize}",
            Set = s =>
            {
                if (!TryInt(s, out var v) || v < 1) return "a whole number, 1 or more";
                Own(t);
                d.m_stack = Mathf.Min(v, Mathf.Max(1, d.m_shared.m_maxStackSize));
                SaveItem(drop);
                return null;
            }
        });
        if (d.m_shared.m_maxQuality > 1)
            list.Add(new EditField
            {
                Id = "quality", Label = "Quality", Get = () => d.m_quality.ToString(CultureInfo.InvariantCulture), Hint = $"1 - {d.m_shared.m_maxQuality}",
                Set = s =>
                {
                    if (!TryInt(s, out var v) || v < 1) return "a whole number, 1 or more";
                    Own(t);
                    d.m_quality = Mathf.Clamp(v, 1, d.m_shared.m_maxQuality);
                    SaveItem(drop);
                    return null;
                }
            });
        if (d.m_shared.m_useDurability)
            list.Add(new EditField
            {
                Id = "durability", Label = "Durability", Get = () => F(d.m_durability), Hint = $"0 - {F(d.GetMaxDurability())}",
                Set = s =>
                {
                    if (!TryFloat(s, out var v)) return "not a number";
                    Own(t);
                    d.m_durability = Mathf.Clamp(v, 0f, d.GetMaxDurability());
                    SaveItem(drop);
                    return null;
                }
            });
        if (d.m_shared.m_variants > 1)
            list.Add(new EditField
            {
                Id = "variant", Label = "Variant", Get = () => d.m_variant.ToString(CultureInfo.InvariantCulture), Hint = $"0 - {d.m_shared.m_variants - 1}",
                Set = s =>
                {
                    if (!TryInt(s, out var v) || v < 0) return "a whole number, 0 or more";
                    Own(t);
                    d.m_variant = Mathf.Clamp(v, 0, d.m_shared.m_variants - 1);
                    SaveItem(drop);
                    return null;
                }
            });
        list.Add(new EditField
        {
            Id = "crafterName", Label = "Crafted by", Get = () => d.m_crafterName ?? "",
            Set = s => { Own(t); d.m_crafterName = s; SaveItem(drop); return null; }
        });
    }

    private static void SignFields(Target t, List<EditField> list)
    {
        var sign = t.Root.GetComponent<Sign>();
        if (sign == null)
            return;
        list.Add(new EditField
        {
            Id = "text", Label = "Text", Get = () => sign.GetText() ?? "",
            Set = s => { Own(t); sign.SetText(s); return null; }
        });
    }

    private static void FireFields(Target t, List<EditField> list)
    {
        var fire = t.Root.GetComponent<Fireplace>();
        if (fire == null)
            return;
        list.Add(new EditField
        {
            Id = "fuel", Label = "Fuel", Get = () => F(Zdo(t)?.GetFloat(H("fuel"), 0f) ?? 0f), Hint = $"0 - {F(fire.m_maxFuel)}",
            Set = s => { if (!TryFloat(s, out var v)) return "not a number"; var z = Own(t); if (z == null) return "no ZDO"; z.Set(H("fuel"), Mathf.Clamp(v, 0f, fire.m_maxFuel)); return null; }
        });
    }

    private static void DoorFields(Target t, List<EditField> list)
    {
        if (t.Root.GetComponent<Door>() == null)
            return;
        list.Add(new EditField
        {
            Id = "state", Label = "Door", Get = () => (Zdo(t)?.GetInt(H("state"), 0) ?? 0).ToString(CultureInfo.InvariantCulture), Hint = "0 closed, 1 open out, -1 open in",
            Set = s => { if (!TryInt(s, out var v)) return "-1, 0 or 1"; var z = Own(t); if (z == null) return "no ZDO"; z.Set(H("state"), Mathf.Clamp(v, -1, 1)); return null; }
        });
    }

    private static void WardFields(Target t, List<EditField> list)
    {
        if (t.Root.GetComponent<PrivateArea>() == null)
            return;
        list.Add(new EditField
        {
            Id = "enabled", Label = "Ward active", Get = () => B(Zdo(t)?.GetBool(H("enabled"), false) ?? false), Hint = "yes / no",
            Set = s => { if (!TryBool(s, out var v)) return "yes or no"; var z = Own(t); if (z == null) return "no ZDO"; z.Set(H("enabled"), v); return null; }
        });
    }

    private static void PickableFields(Target t, List<EditField> list)
    {
        if (t.Root.GetComponent<Pickable>() == null)
            return;
        list.Add(new EditField
        {
            Id = "picked", Label = "Picked", Get = () => B(Zdo(t)?.GetBool(H("picked"), false) ?? false), Hint = "yes / no",
            Set = s => { if (!TryBool(s, out var v)) return "yes or no"; var z = Own(t); if (z == null) return "no ZDO"; z.Set(H("picked"), v); return null; }
        });
    }

    private static void TransformFields(Target t, List<EditField> list)
    {
        if (t.Kind == "Player")
            return;
        list.Add(new EditField
        {
            Id = "position", Label = "Position", Hint = "x, y, z",
            Get = () => { var p = t.Root.transform.position; return $"{F(p.x)}, {F(p.y)}, {F(p.z)}"; },
            Set = s =>
            {
                if (!TryVec(s, out var p)) return "three numbers: x, y, z";
                Move(t, p, t.Root.transform.rotation);
                return null;
            }
        });
        list.Add(new EditField
        {
            Id = "rotation", Label = "Rotation (yaw)", Hint = "degrees",
            Get = () => F(t.Root.transform.eulerAngles.y),
            Set = s =>
            {
                if (!TryFloat(s, out var yaw)) return "not a number";
                var e = t.Root.transform.eulerAngles;
                Move(t, t.Root.transform.position, Quaternion.Euler(e.x, yaw, e.z));
                return null;
            }
        });
    }

    private static void Move(Target t, Vector3 pos, Quaternion rot)
    {
        var zdo = Own(t);
        var body = t.Root.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = pos;
            body.rotation = rot;
            if (!body.isKinematic)
                body.velocity = Vector3.zero;
        }
        t.Root.transform.SetPositionAndRotation(pos, rot);
        if (zdo != null)
        {
            zdo.SetPosition(pos);
            zdo.SetRotation(rot);
        }
    }

    private static bool TryVec(string s, out Vector3 v)
    {
        v = default;
        var parts = s.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !TryFloat(parts[0], out var x) || !TryFloat(parts[1], out var y) || !TryFloat(parts[2], out var z))
            return false;
        v = new Vector3(x, y, z);
        return true;
    }

    /// <summary>Sets a curated field by its id (or label) from text. Null when it worked, else why not.</summary>
    public static string? SetField(Target t, string idOrLabel, string value)
    {
        foreach (var f in Fields(t))
        {
            if (!string.Equals(f.Id, idOrLabel, StringComparison.OrdinalIgnoreCase) && !string.Equals(f.Label, idOrLabel, StringComparison.OrdinalIgnoreCase))
                continue;
            if (f.Set == null)
                return $"{f.Label} is read only";
            try { return f.Set(value); }
            catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message}"; }
        }
        return $"no field '{idOrLabel}' on {t.Prefab}";
    }

    // ---- raw ZDO data ----

    public static List<RawValue> Raw(Target t)
    {
        var list = new List<RawValue>();
        var zdo = Zdo(t);
        if (zdo == null)
            return list;
        ZDOExtraData.GetData(zdo.m_uid, out var floats, out var vecs, out var quats, out var ints, out var longs, out var strings, out var bytes, out _);
        void Add(char type, int hash, string text) => list.Add(new RawValue { Type = type, Hash = hash, Name = NameOf(hash), Text = text });
        if (floats != null) foreach (var kv in floats) Add('f', kv.Key, F(kv.Value));
        if (ints != null) foreach (var kv in ints) Add('i', kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
        if (longs != null) foreach (var kv in longs) Add('l', kv.Key, kv.Value.ToString(CultureInfo.InvariantCulture));
        if (strings != null) foreach (var kv in strings) Add('s', kv.Key, kv.Value ?? "");
        if (vecs != null) foreach (var kv in vecs) Add('v', kv.Key, $"{F(kv.Value.x)}, {F(kv.Value.y)}, {F(kv.Value.z)}");
        if (quats != null) foreach (var kv in quats) Add('q', kv.Key, Convert.ToBase64String(QuatBytes(kv.Value)));
        if (bytes != null) foreach (var kv in bytes) Add('b', kv.Key, kv.Value != null ? Convert.ToBase64String(kv.Value) : "");
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private static byte[] QuatBytes(Quaternion q)
    {
        var b = new byte[16];
        Buffer.BlockCopy(new[] { q.x, q.y, q.z, q.w }, 0, b, 0, 16);
        return b;
    }

    private static Quaternion BytesQuat(byte[] b)
    {
        var f = new float[4];
        Buffer.BlockCopy(b, 0, f, 0, 16);
        return new Quaternion(f[0], f[1], f[2], f[3]);
    }

    /// <summary>Writes one raw value (by its "type:hash" key) from text. Null when it worked, else why not.</summary>
    public static string? SetRaw(Target t, string key, string text)
    {
        var zdo = Own(t);
        if (zdo == null)
            return "no ZDO";
        if (key.Length < 3 || key[1] != ':' || !int.TryParse(key.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash))
            return $"bad key {key}";
        try
        {
            switch (key[0])
            {
                case 'f': if (!TryFloat(text, out var f)) return "not a number"; zdo.Set(hash, f); break;
                case 'i': if (!TryInt(text, out var i)) return "not a whole number"; zdo.Set(hash, i); break;
                case 'l': if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return "not a whole number"; zdo.Set(hash, l); break;
                case 's': zdo.Set(hash, text); break;
                case 'v': if (!TryVec(text, out var v)) return "three numbers: x, y, z"; zdo.Set(hash, v); break;
                case 'q': zdo.Set(hash, BytesQuat(Convert.FromBase64String(text))); break;
                case 'b': zdo.Set(hash, Convert.FromBase64String(text)); break;
                default: return $"unknown type {key[0]}";
            }
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
        return null;
    }

    // ---- copy and paste ----

    // never pasted: they belong to the object they were read from, not to the state being copied
    private static readonly HashSet<string> NotPasted = new(StringComparer.Ordinal) { "spawntime", "seed", "spawnpoint", "patrolPoint", "lastTime" };

    /// <summary>Every raw value of the object, by "type:hash" key: what a note carries and Paste writes back.</summary>
    public static Dictionary<string, string> Copy(Target t)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in Raw(t))
            values[raw.Key] = raw.Text;
        return values;
    }

    /// <summary>Writes copied values onto the object and refreshes the components that cache their state. Returns how many were written.</summary>
    public static int Paste(Target t, Dictionary<string, string> values, out string? error)
    {
        error = null;
        if (!t.Alive || t.Kind is "Terrain" or "Player")
        {
            error = t.Kind == "Player" ? "not onto a player" : "nothing to paste onto";
            return 0;
        }
        if (Own(t) == null)
        {
            error = "the object has no ZDO";
            return 0;
        }
        var n = 0;
        foreach (var kv in values)
        {
            if (kv.Key.Length > 2 && int.TryParse(kv.Key.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash) && NotPasted.Contains(NameOf(hash)))
                continue;
            var why = SetRaw(t, kv.Key, kv.Value);
            if (why == null) n++;
            else error ??= $"{kv.Key}: {why}";
        }
        Refresh(t, values);
        return n;
    }

    /// <summary>Components that read the ZDO only at start: push the pasted state into them.</summary>
    private static void Refresh(Target t, Dictionary<string, string> values)
    {
        try
        {
            var zdo = Zdo(t);
            if (zdo == null)
                return;
            if (t.Root.GetComponent<Character>() is { } c && c is not Player)
            {
                if (values.ContainsKey("i:" + H("level")))
                    c.SetLevel(Mathf.Max(1, zdo.GetInt(H("level"), c.GetLevel())));
                if (values.ContainsKey("i:" + H("tamed")))
                    c.SetTamed(zdo.GetBool(H("tamed"), c.IsTamed()));
            }
            if (t.Root.GetComponent<ItemDrop>() is { } drop)
                drop.Load();
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogDebug($"Clipboard paste: refresh skipped ({ex.Message}).");
        }
    }

    /// <summary>
    /// A new copy of a noted object in front of the player, with the note's values pasted onto it (a chest with the same contents,
    /// a creature with the same level and health, an item with the same quality and durability). Returns the new object.
    /// </summary>
    public static GameObject? SpawnCopy(Note note)
    {
        var player = Player.m_localPlayer;
        var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(note.Prefab) : null;
        if (player == null || prefab == null)
            return null;
        var pos = player.transform.position + player.transform.forward * 2.5f + Vector3.up * (note.Kind == "Item" ? 1f : 0.2f);
        if (note.Kind != "Item" && ZoneSystem.instance != null)
            pos.y = ZoneSystem.instance.GetSolidHeight(pos);
        var rot = Quaternion.Euler(0f, player.transform.eulerAngles.y + 180f, 0f);
        var go = UnityEngine.Object.Instantiate(prefab, pos, rot);
        if (note.Values.Count > 0)
        {
            var t = Readout.FromObject(go, player);
            Paste(t, note.Values, out _);
        }
        return go;
    }
}
