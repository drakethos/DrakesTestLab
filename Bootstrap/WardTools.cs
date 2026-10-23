using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Ward (guard stone) test bench. A ward stores its owner (creator id + name), enabled flag and permitted list in its ZDO;
/// everything here edits those directly, so any ownership/permission scenario can be staged without a second player.
/// </summary>
internal static class WardTools
{
    public sealed class Identity
    {
        public string Name = "";
        public long Id;

        // optional look, applied when you play as this identity (Model: 0 male, 1 female; -1 = leave)
        public int Model = -1;
        public string Hair = "";
        public string Beard = "";
        public Vector3? HairColor;
        public Vector3? SkinColor;

        public override string ToString() => $"{Name} ({Id})";
    }

    public static readonly Identity Alice = new()
    {
        Name = "Alice", Id = 100001, Model = 1, Hair = "Hair12", Beard = "", HairColor = new Vector3(0.75f, 0.28f, 0.1f), SkinColor = new Vector3(0.95f, 0.8f, 0.7f)
    };

    public static readonly Identity Amy = new()
    {
        Name = "Amy", Id = 100004, Model = 1, Hair = "Hair6", Beard = "", HairColor = new Vector3(0.25f, 0.15f, 0.1f), SkinColor = new Vector3(0.9f, 0.7f, 0.6f)
    };

    public static readonly Identity Bob = new()
    {
        Name = "Bob", Id = 100002, Model = 0, Hair = "Hair3", Beard = "Beard5", HairColor = new Vector3(0.3f, 0.2f, 0.1f), SkinColor = new Vector3(0.8f, 0.6f, 0.45f)
    };

    public static readonly Identity Carol = new()
    {
        Name = "Carol", Id = 100003, Model = 1, Hair = "Hair9", Beard = "", HairColor = new Vector3(0.9f, 0.8f, 0.4f), SkinColor = new Vector3(0.6f, 0.45f, 0.35f)
    };

    private static Identity? _original;
    private static Identity? _currentOverride;

    /// <summary>The player object is gone after a reload; forget the identity captured from the old one.</summary>
    public static void ForgetOriginal()
    {
        _original = null;
        _currentOverride = null;
    }

    /// <summary>Who the local player really is (name, id and look at the time of the first switch).</summary>
    public static Identity? Original => _original;
    public static bool IsSwitched => _currentOverride != null;

    private static void CaptureOriginal()
    {
        var p = Player.m_localPlayer;
        if (_original != null || p == null)
            return;

        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        _original = new Identity
        {
            Name = profile != null ? profile.GetName() : p.GetPlayerName(),
            Id = profile != null ? profile.GetPlayerID() : p.GetPlayerID(),
            Model = p.GetPlayerModel(),
            HairColor = p.GetHairColor()
        };
    }

    /// <summary>
    /// Becomes another player for ward purposes: rewrites the player id and name the ward checks read, and optionally the look.
    /// Everything that compares against GetPlayerID (ward owner, permitted list) then sees you as that player.
    /// </summary>
    public static bool PlayAs(Identity who, bool applyLook = true)
    {
        var p = Player.m_localPlayer;
        var view = p != null ? AccessTools.Field(typeof(Player), "m_nview")?.GetValue(p) as ZNetView : null;
        var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (p == null || zdo == null)
            return false;

        CaptureOriginal();

        // The game has TWO notions of "me": the player object (ZDO player id; ward permitted list) and the character profile
        // (Piece.IsCreator: ward owner, private chests, locks). Both must change or access ends up half and half.
        zdo.Set(Key("s_playerID"), who.Id);
        zdo.Set(Key("s_playerName"), who.Name);
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        if (profile != null)
        {
            AccessTools.Field(typeof(PlayerProfile), "m_playerID")?.SetValue(profile, who.Id);
            profile.SetName(who.Name);
        }

        _currentOverride = ReferenceEquals(who, _original) ? null : who;
        if (applyLook)
            ApplyLook(p, who);
        TestLabPlugin.Log?.LogInfo($"Now playing as {who}.");
        return true;
    }

    public static void RestoreOriginal()
    {
        var p = Player.m_localPlayer;
        if (_original == null || p == null)
            return;

        PlayAs(_original, true);
        _currentOverride = null;
    }

    /// <summary>1.0 takes the item's name hash, the pre-1.0 CI stubs take the name: bind by name and pass what fits.</summary>
    private static void CallVisItem(VisEquipment vis, string method, string itemName)
    {
        var m = AccessTools.Method(typeof(VisEquipment), method);
        if (m == null)
            return;

        var param = m.GetParameters()[0].ParameterType;
        m.Invoke(vis, new object[] { param == typeof(string) ? itemName : (object)(itemName.Length == 0 ? 0 : itemName.GetStableHashCode()) });
    }

    private static void ApplyLook(Player p, Identity who)
    {
        try
        {
            if (who.Model >= 0)
                p.SetPlayerModel(who.Model);
            if (who.SkinColor is { } skin)
                p.SetSkinColor(skin);
            if (who.HairColor is { } hair)
                p.SetHairColor(hair);

            var vis = p.GetComponent<VisEquipment>();
            if (vis == null)
                return;

            if (who.Hair.Length > 0)
                CallVisItem(vis, "SetHairItem", who.Hair);
            if (who.Beard.Length > 0 || who.Model == 1)
                CallVisItem(vis, "SetBeardItem", who.Beard);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Applying look for {who.Name} failed: {ex.Message}");
        }
    }

    private static readonly FieldInfo? NviewField = AccessTools.Field(typeof(PrivateArea), "m_nview");
    private static readonly FieldInfo? PieceCreatorField = AccessTools.Field(typeof(Piece), "m_creator");
    private static readonly MethodInfo? UpdateStatus = AccessTools.Method(typeof(PrivateArea), "UpdateStatus");

    private static int Key(string field) =>
        AccessTools.Field(typeof(ZDOVars), field)?.GetValue(null) is int i ? i : 0;

    /// <summary>The identity you currently are (changes when you play as someone else).</summary>
    public static Identity Me()
    {
        var p = Player.m_localPlayer;
        var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
        return new Identity
        {
            Name = profile != null ? profile.GetName() : (p != null ? p.GetPlayerName() : "me"),
            Id = profile != null ? profile.GetPlayerID() : (p != null ? p.GetPlayerID() : 0)
        };
    }

    /// <summary>"me", a known name (Alice/Bob/Carol), "Name" (stable fake id) or "Name:12345".</summary>
    public static Identity Parse(string spec)
    {
        var s = (spec ?? "").Trim();
        if (s.Length == 0 || s.Equals("me", StringComparison.OrdinalIgnoreCase))
            return Me();

        var colon = s.LastIndexOf(':');
        if (colon > 0 && long.TryParse(s.Substring(colon + 1), out var id))
            return new Identity { Name = s.Substring(0, colon), Id = id };

        foreach (var known in new[] { Alice, Amy, Bob, Carol })
        {
            if (known.Name.Equals(s, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        return new Identity { Name = s, Id = 200000 + Math.Abs(s.GetStableHashCode() % 100000) };
    }

    public static PrivateArea? Nearest(float range = 40f)
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return null;

        PrivateArea? best = null;
        var bestD = range;
        foreach (var ward in UnityEngine.Object.FindObjectsOfType<PrivateArea>())
        {
            var d = Vector3.Distance(ward.transform.position, p.transform.position);
            if (d < bestD)
            {
                best = ward;
                bestD = d;
            }
        }

        return best;
    }

    private static ZNetView? View(PrivateArea ward) => NviewField?.GetValue(ward) as ZNetView;

    private static ZDO? Zdo(PrivateArea ward)
    {
        var view = View(ward);
        if (view == null || !view.IsValid())
            return null;

        if (!view.IsOwner())
            view.ClaimOwnership();
        return view.GetZDO();
    }

    private static void Refresh(PrivateArea ward) => UpdateStatus?.Invoke(ward, null);

    /// <summary>Drops a ward in front of the player. Created switched OFF so scenarios start from a known state.</summary>
    public static PrivateArea? Spawn(Vector3 pos, Identity owner, bool enabled, IEnumerable<Identity>? permitted = null, float radius = -1f)
    {
        var prefab = ZNetScene.instance?.GetPrefab("guard_stone");
        if (prefab == null)
        {
            TestLabPlugin.Log?.LogWarning("guard_stone prefab not found.");
            return null;
        }

        if (ZoneSystem.instance != null)
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);

        var go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
        var ward = go.GetComponent<PrivateArea>();
        if (ward == null)
            return null;

        if (radius > 0f)
            ward.m_radius = radius;

        SetOwner(ward, owner);
        ClearPermitted(ward);
        foreach (var id in permitted ?? Array.Empty<Identity>())
            AddPermitted(ward, id);
        SetEnabled(ward, enabled);
        return ward;
    }

    public static void SetOwner(PrivateArea ward, Identity owner)
    {
        var zdo = Zdo(ward);
        if (zdo == null)
            return;

        zdo.Set(Key("s_creator"), owner.Id);
        zdo.Set(Key("s_creatorName"), owner.Name);
        // Piece caches the creator in a field when it wakes up, and the ward asks the piece for access checks
        var piece = ward.GetComponent<Piece>();
        if (piece != null)
            PieceCreatorField?.SetValue(piece, owner.Id);
        Refresh(ward);
    }

    /// <summary>Owner of any building piece (chest, door, station): the same creator id the ward and chest checks read.</summary>
    public static void SetPieceOwner(Piece piece, Identity owner)
    {
        var view = piece.GetComponent<ZNetView>();
        var zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (zdo != null)
        {
            if (!view!.IsOwner())
                view.ClaimOwnership();
            zdo.Set(Key("s_creator"), owner.Id);
            zdo.Set(Key("s_creatorName"), owner.Name);
        }

        PieceCreatorField?.SetValue(piece, owner.Id);
        var ward = piece.GetComponent<PrivateArea>();
        if (ward != null)
            Refresh(ward);
    }

    public static void SetEnabled(PrivateArea ward, bool enabled)
    {
        Zdo(ward)?.Set(Key("s_enabled"), enabled);
        Refresh(ward);
    }

    public static List<Identity> Permitted(PrivateArea ward)
    {
        var list = new List<Identity>();
        var zdo = View(ward)?.GetZDO();
        if (zdo == null)
            return list;

        var count = zdo.GetInt(Key("s_permitted"));
        for (var i = 0; i < count; i++)
        {
            var id = zdo.GetLong("pu_id" + i, 0L);
            if (id != 0L)
                list.Add(new Identity { Id = id, Name = zdo.GetString("pu_name" + i) });
        }

        return list;
    }

    private static void WritePermitted(PrivateArea ward, List<Identity> users)
    {
        var zdo = Zdo(ward);
        if (zdo == null)
            return;

        zdo.Set(Key("s_permitted"), users.Count);
        for (var i = 0; i < users.Count; i++)
        {
            zdo.Set("pu_id" + i, users[i].Id);
            zdo.Set("pu_name" + i, users[i].Name);
        }

        Refresh(ward);
    }

    public static void AddPermitted(PrivateArea ward, Identity who)
    {
        var list = Permitted(ward);
        if (!list.Exists(x => x.Id == who.Id))
        {
            list.Add(who);
            WritePermitted(ward, list);
        }
    }

    public static void RemovePermitted(PrivateArea ward, Identity who)
    {
        var list = Permitted(ward);
        if (list.RemoveAll(x => x.Id == who.Id) > 0)
            WritePermitted(ward, list);
    }

    public static void ClearPermitted(PrivateArea ward) => WritePermitted(ward, new List<Identity>());

    /// <summary>Back to a fresh ward you own: enabled, nobody else permitted.</summary>
    public static void Reset(PrivateArea ward)
    {
        SetOwner(ward, Me());
        ClearPermitted(ward);
        SetEnabled(ward, true);
    }

    public static string Describe(PrivateArea ward)
    {
        var zdo = View(ward)?.GetZDO();
        if (zdo == null)
            return "(ward not ready)";

        var me = Me();
        var ownerId = zdo.GetLong(Key("s_creator"), 0L);
        var ownerName = zdo.GetString(Key("s_creatorName"));
        var enabled = zdo.GetBool(Key("s_enabled"));
        var permitted = Permitted(ward);

        var sb = new StringBuilder();
        sb.AppendLine($"Owner: {ownerName} ({ownerId}){(ownerId == me.Id ? "  <color=#80ff80>= you</color>" : "")}");
        sb.AppendLine($"State: {(enabled ? "<color=#ff9060>ON (protecting)</color>" : "<color=#aaaaaa>OFF</color>")}   radius {ward.m_radius:0.#} m");
        sb.AppendLine("Permitted: " + (permitted.Count == 0 ? "nobody" : string.Join(", ", permitted)));
        var access = ownerId == me.Id || permitted.Exists(x => x.Id == me.Id);
        sb.Append(!enabled
            ? "Your access: ward is off - everyone can build and use things"
            : access
                ? "<color=#80ff80>Your access: YES (owner or permitted)</color>"
                : "<color=#ff8080>Your access: NO - building/interacting inside is blocked</color>");
        return sb.ToString();
    }

    /// <summary>The game's own access test at the player's position (no flash effect).</summary>
    public static bool HaveAccessHere()
    {
        var p = Player.m_localPlayer;
        return p == null || PrivateArea.CheckAccess(p.transform.position, 0f, false, false);
    }
}
