using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// Poses for a virtual player's character, so a two-client look check needs no second game.
///
/// On every other client a player's pose comes from two things their own client sends: the animator parameters
/// ZSyncAnimation writes into their character's ZDO (key = 438569 + Animator.StringToHash(name), only for the parameters
/// in its sync lists), and the "SetTrigger" routed RPC. Equipment comes from the item hashes VisEquipment writes into the
/// same ZDO. A virtual player has no client running, so nothing ever wrote those and her puppet stood idle.
///
///  - peerMirror: the puppet copies YOUR character's synced state every frame (animator values, equipment, mod data
///    kept on the player ZDO, facing) and replays your triggers through her connection. Aim a musket yourself and the
///    puppet shows exactly what another client would see of that aim, including what a mod forgot to sync.
///  - peerPose: set her animator values / triggers / animation speed directly.
///  - peerEquip: put items in her hands and armour slots.
///  - check peerAnim: what her animator is actually doing (a parameter, a state name, or the clip playing).
/// </summary>
internal static class PeerPose
{
    /// <summary>The salt ZSyncAnimation adds to an animator parameter hash to get its ZDO key.</summary>
    public const int ZdoSalt = 438569;

    public static int Key(string param) => ZdoSalt + ZSyncAnimation.GetHash(param);

    private sealed class Mirror
    {
        public string Face = "same";   // same = your facing, me = turned towards you, or a yaw in degrees
        public bool Equipment = true;
    }

    private static readonly Dictionary<VirtualPeer, Mirror> Mirrors = new();

    // the puppet keeps its own identity and look; everything else on your player ZDO is mirrored
    private static readonly HashSet<int> KeepOwn = new()
    {
        ZDOVars.s_playerID, ZDOVars.s_playerName, ZDOVars.s_modelIndex, ZDOVars.s_hairItem, ZDOVars.s_beardItem,
        ZDOVars.s_hairColor, ZDOVars.s_skinColor
    };

    private static readonly HashSet<int> EquipmentKeys = new()
    {
        ZDOVars.s_rightItem, ZDOVars.s_rightItemQuality, ZDOVars.s_leftItem, ZDOVars.s_leftItemVariant, ZDOVars.s_leftItemQuality,
        ZDOVars.s_rightBackItem, ZDOVars.s_rightBackItemQuality, ZDOVars.s_leftBackItem, ZDOVars.s_leftBackItemVariant,
        ZDOVars.s_leftBackItemQuality, ZDOVars.s_chestItem, ZDOVars.s_legItem, ZDOVars.s_helmetItem, ZDOVars.s_shoulderItem,
        ZDOVars.s_shoulderItemVariant, ZDOVars.s_shoulderItemQuality, ZDOVars.s_utilityItem, ZDOVars.s_trinketItem
    };

    public static bool IsMirroring(VirtualPeer vp) => Mirrors.ContainsKey(vp);

    public static void StartMirror(VirtualPeer vp, string face, bool equipment)
    {
        if (vp.PuppetZdo == null)
            throw new StepFailed($"{vp.Name} has no character (peerJoin with \"puppet\": true)");
        Mirrors[vp] = new Mirror { Face = face.Length > 0 ? face : "same", Equipment = equipment };
        Tick();
    }

    public static void StopMirror(VirtualPeer vp) => Mirrors.Remove(vp);

    public static void Forget(VirtualPeer vp) => Mirrors.Remove(vp);

    /// <summary>Called every frame by the virtual players' pump (after the game has written this frame's state).</summary>
    public static void Tick()
    {
        if (Mirrors.Count == 0)
            return;
        var me = Player.m_localPlayer;
        var view = me != null ? me.GetComponent<ZNetView>() : null;
        var src = view != null && view.IsValid() ? view.GetZDO() : null;
        if (src == null)
            return;
        foreach (var kv in Mirrors.ToList())
        {
            var vp = kv.Key;
            var dst = vp.PuppetZdo;
            if (!VirtualPeers.All.Contains(vp) || dst == null || ZDOMan.instance == null || ZDOMan.instance.GetZDO(dst.m_uid) == null)
            {
                Mirrors.Remove(vp);
                continue;
            }
            try
            {
                Copy(src, dst, kv.Value.Equipment);
                dst.SetRotation(Facing(kv.Value.Face, me!, dst.GetPosition()));
            }
            catch (Exception ex)
            {
                TestLabPlugin.Log?.LogWarning($"Mirror to {vp.Name} stopped: {ex.Message}");
                Mirrors.Remove(vp);
            }
        }
    }

    private static void Copy(ZDO src, ZDO dst, bool equipment)
    {
        ZDOExtraData.GetData(src.m_uid, out var floats, out var vecs, out var quats, out var ints, out var longs, out var strings, out _, out _);
        bool Skip(int h) => KeepOwn.Contains(h) || (!equipment && EquipmentKeys.Contains(h));
        if (floats != null) foreach (var p in floats) if (!Skip(p.Key)) dst.Set(p.Key, p.Value);
        if (ints != null) foreach (var p in ints) if (!Skip(p.Key)) dst.Set(p.Key, p.Value);
        if (longs != null) foreach (var p in longs) if (!Skip(p.Key)) dst.Set(p.Key, p.Value);
        if (strings != null) foreach (var p in strings) if (!Skip(p.Key)) dst.Set(p.Key, p.Value);
        if (vecs != null) foreach (var p in vecs) if (!Skip(p.Key)) dst.Set(p.Key, p.Value);
        if (quats != null) foreach (var p in quats) if (!Skip(p.Key)) dst.Set(p.Key, p.Value);
    }

    private static Quaternion Facing(string face, Player me, Vector3 at)
    {
        if (face.Equals("me", StringComparison.OrdinalIgnoreCase) || face.Equals("host", StringComparison.OrdinalIgnoreCase))
        {
            var to = me.transform.position - at;
            to.y = 0f;
            return to.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(to) : me.transform.rotation;
        }
        if (float.TryParse(face, NumberStyles.Float, CultureInfo.InvariantCulture, out var yaw))
            return Quaternion.Euler(0f, yaw, 0f);
        return me.transform.rotation;
    }

    /// <summary>Your triggers, replayed by every mirroring puppet through its own connection (a routed RPC to everybody, like a client's).</summary>
    internal static void OnTrigger(ZSyncAnimation sync, string name)
    {
        if (Mirrors.Count == 0 || VirtualPeers.ActingAs != null)
            return;
        var me = Player.m_localPlayer;
        if (me == null || sync.gameObject != me.gameObject)
            return;
        foreach (var vp in Mirrors.Keys.ToList())
            if (vp.PuppetZdo != null)
                vp.SendRoutedRpc(0L, vp.PuppetZdo.m_uid, "SetTrigger", new object[] { name });
    }

    // ---- peerPose ----

    /// <summary>
    /// "bool"/"float"/"int": { "param": value } written the way the owner's ZSyncAnimation writes them; "trigger": a name or a
    /// list (sent as SetTrigger from her connection); "speed": animation speed; "clear": true drops what peerPose set first.
    /// Returns a short description for the log.
    /// </summary>
    public static string Pose(VirtualPeer vp, Dictionary<string, object?> step)
    {
        var zdo = vp.PuppetZdo ?? throw new StepFailed($"{vp.Name} has no character (peerJoin with \"puppet\": true)");
        var done = new List<string>();
        var inv = CultureInfo.InvariantCulture;
        if (step.Bool("clear"))
        {
            if (vp.PoseKeys.Count > 0)
            {
                foreach (var key in vp.PoseKeys)
                {
                    ZDOExtraData.RemoveInt(zdo.m_uid, key);
                    ZDOExtraData.RemoveFloat(zdo.m_uid, key);
                }
                done.Add($"cleared {vp.PoseKeys.Count}");
                vp.PoseKeys.Clear();
            }
        }
        foreach (var kv in step.Obj("bool") ?? new Dictionary<string, object?>())
        {
            var k = Key(kv.Key);
            zdo.Set(k, Convert.ToBoolean(kv.Value, inv) ? 1 : 0);
            vp.PoseKeys.Add(k);
            done.Add($"{kv.Key}={J.Value(kv.Value)}");
        }
        foreach (var kv in step.Obj("float") ?? new Dictionary<string, object?>())
        {
            var k = Key(kv.Key);
            zdo.Set(k, Convert.ToSingle(kv.Value, inv));
            vp.PoseKeys.Add(k);
            done.Add($"{kv.Key}={J.Value(kv.Value)}");
        }
        foreach (var kv in step.Obj("int") ?? new Dictionary<string, object?>())
        {
            var k = Key(kv.Key);
            zdo.Set(k, Convert.ToInt32(kv.Value, inv));
            vp.PoseKeys.Add(k);
            done.Add($"{kv.Key}={J.Value(kv.Value)}");
        }
        if (step.Has("speed"))
        {
            zdo.Set(ZDOVars.s_animationSpeed, step.Num("speed", 1f));
            done.Add($"speed={step.Num("speed", 1f)}");
        }
        var triggers = new List<string>();
        if (step.TryGetValue("trigger", out var t) && t is string one && one.Length > 0) triggers.Add(one);
        foreach (var x in step.Arr("triggers")) if (x is string s && s.Length > 0) triggers.Add(s);
        if (t is List<object?> many) foreach (var x in many) if (x is string s && s.Length > 0) triggers.Add(s);
        foreach (var trig in triggers)
        {
            vp.SendRoutedRpc(0L, zdo.m_uid, "SetTrigger", new object[] { trig });
            done.Add($"trigger {trig}");
        }
        return done.Count > 0 ? string.Join(", ", done) : "nothing to set";
    }

    // ---- peerEquip ----

    private static readonly (string Slot, int Key)[] Slots =
    {
        ("right", ZDOVars.s_rightItem), ("left", ZDOVars.s_leftItem), ("rightBack", ZDOVars.s_rightBackItem), ("leftBack", ZDOVars.s_leftBackItem),
        ("chest", ZDOVars.s_chestItem), ("legs", ZDOVars.s_legItem), ("helmet", ZDOVars.s_helmetItem), ("shoulder", ZDOVars.s_shoulderItem),
        ("utility", ZDOVars.s_utilityItem), ("trinket", ZDOVars.s_trinketItem)
    };

    /// <summary>{ "right": "prefab", "left": "", "chest": ..., "legs", "helmet", "shoulder", "utility", "trinket", "rightBack", "leftBack" }: "" or null empties a slot.</summary>
    public static string Equip(VirtualPeer vp, Dictionary<string, object?> step)
    {
        var zdo = vp.PuppetZdo ?? throw new StepFailed($"{vp.Name} has no character (peerJoin with \"puppet\": true)");
        var done = new List<string>();
        foreach (var (slot, key) in Slots)
        {
            if (!step.ContainsKey(slot))
                continue;
            var prefab = step.Str(slot);
            if (prefab.Length > 0 && (ObjectDB.instance == null || ObjectDB.instance.GetItemPrefab(prefab) == null))
                throw new StepFailed($"peerEquip: no item prefab '{prefab}' for {slot}");
            zdo.Set(key, prefab.Length > 0 ? prefab.GetStableHashCode() : 0);
            done.Add($"{slot}={(prefab.Length > 0 ? prefab : "(empty)")}");
        }
        if (step.Has("quality"))
            zdo.Set(ZDOVars.s_rightItemQuality, (int)step.Num("quality", 1f));
        return done.Count > 0 ? string.Join(", ", done) : "no slots named";
    }

    // ---- check peerAnim ----

    /// <summary>
    /// "param": an animator parameter with "expect" (bool / number); "state": a state name on any layer (or "layer");
    /// "clip": text the name of a playing clip contains. "expect": false inverts state/clip.
    /// </summary>
    public static (bool, string) Check(VirtualPeer vp, Dictionary<string, object?> a)
    {
        var go = vp.Puppet ?? throw new StepFailed($"{vp.Name}'s character is not in the world");
        var anim = go.GetComponentInChildren<Animator>() ?? throw new StepFailed($"{vp.Name}'s character has no Animator");
        var layers = a.Has("layer") ? new[] { (int)a.Num("layer") } : Enumerable.Range(0, anim.layerCount).ToArray();
        var clips = layers.SelectMany(l => anim.GetCurrentAnimatorClipInfo(l).Select(c => c.clip != null ? c.clip.name : "")).Where(n => n.Length > 0).Distinct().ToList();
        var playing = clips.Count > 0 ? "playing " + string.Join(", ", clips) : "no clip playing";

        var param = a.Str("param");
        if (param.Length > 0)
        {
            var p = anim.parameters.FirstOrDefault(x => x.name == param) ?? throw new StepFailed($"{vp.Name}'s animator has no parameter '{param}'");
            switch (p.type)
            {
                case AnimatorControllerParameterType.Bool:
                {
                    var v = anim.GetBool(param);
                    return (v == a.Bool("expect", true), $"{param} = {v} ({playing})");
                }
                case AnimatorControllerParameterType.Float:
                {
                    var v = anim.GetFloat(param);
                    var want = a.Num("expect");
                    var ok = a.Has("min") || a.Has("max")
                        ? (!a.Has("min") || v >= a.Num("min")) && (!a.Has("max") || v <= a.Num("max"))
                        : Mathf.Abs(v - want) <= a.Num("tolerance", 0.05f);
                    return (ok, $"{param} = {v:0.###} ({playing})");
                }
                case AnimatorControllerParameterType.Int:
                {
                    var v = anim.GetInteger(param);
                    return (v == (int)a.Num("expect"), $"{param} = {v} ({playing})");
                }
                default:
                    throw new StepFailed($"'{param}' is a trigger: check the state or clip it leads to instead");
            }
        }

        var want2 = a.Bool("expect", true);
        var state = a.Str("state");
        if (state.Length > 0)
        {
            var hit = layers.Any(l => anim.GetCurrentAnimatorStateInfo(l).IsName(state) || anim.GetNextAnimatorStateInfo(l).IsName(state));
            return (hit == want2, $"state {state} {(hit ? "active" : "not active")} ({playing})");
        }
        var clip = a.Str("clip");
        if (clip.Length > 0)
        {
            var hit = clips.Any(n => n.IndexOf(clip, StringComparison.OrdinalIgnoreCase) >= 0);
            return (hit == want2, playing);
        }
        throw new StepFailed("peerAnim needs \"param\", \"state\" or \"clip\"");
    }
}

/// <summary>Your character's triggers reach a mirroring puppet (the local player's own SetTrigger lands here on this game).</summary>
[HarmonyPatch(typeof(ZSyncAnimation), "RPC_SetTrigger")]
internal static class PeerPose_SetTrigger_Patch
{
    private static void Postfix(ZSyncAnimation __instance, string name)
    {
        try { PeerPose.OnTrigger(__instance, name); }
        catch (Exception ex) { TestLabPlugin.Log?.LogDebug($"Mirror trigger skipped: {ex.Message}"); }
    }
}
