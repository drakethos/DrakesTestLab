using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// Steps and checks for virtual players (<see cref="VirtualPeers"/>): another "client" connected to the test world that a
/// test controls. Steps: peerJoin, peerLeave, peerMove, peerRpc, peerOwn, peerSet, peerClear, peerMirror, peerPose, peerEquip.
/// Checks: peerReceived, peerConnected, zdoOwner, peerAnim. Chat (<see cref="ChatSteps"/>): chat, peerChat, peerEmote,
/// peerPing, chatClear; checks chatSeen, peerHeard, emote.
/// </summary>
internal static class PeerSteps
{
    public static readonly string[] StepNames = { "peerJoin", "peerLeave", "peerMove", "peerRpc", "peerOwn", "peerSet", "peerClear", "peerMirror", "peerPose", "peerEquip" };
    public static readonly string[] CheckNames = { "peerReceived", "peerConnected", "zdoOwner", "peerAnim" };

    private static Player P => Player.m_localPlayer ?? throw new StepFailed("no player");

    public static bool Handles(string action) =>
        StepNames.Any(n => n.Equals(action, StringComparison.OrdinalIgnoreCase)) || ChatSteps.StepNames.Any(n => n.Equals(action, StringComparison.OrdinalIgnoreCase))
        || PvpSteps.StepNames.Any(n => n.Equals(action, StringComparison.OrdinalIgnoreCase));

    public static IEnumerator? Execute(string action, Dictionary<string, object?> step, RunContext ctx)
    {
        switch (action.ToLowerInvariant())
        {
            case "peerjoin":
            {
                // "name": Bjornita (default: [Automation] Player2) / Amy / Bob / any name (same ids as playAs); puppet at dx/dz (default 3 m to your right)
                var name = step.First("name", "peer", "who");
                if (name.Length == 0)
                    name = TestLabConfig.Instance.Player2.Value;
                var pos = Targets.PointFrom(step, P.transform.position + P.transform.right * 3f);
                // "proxy": false = only record what she is sent, never answer (her client stays silent)
                var vp = VirtualPeers.Join(name, pos, step.Bool("puppet", true), step.Num("latency", 0f), step.Num("drop", 0f), step.Bool("streamWorld"), step.Bool("proxy", true));
                var tag = step.First("as", "tag");
                return WaitForPuppet(vp, tag.Length > 0 ? tag : vp.Name, ctx);
            }
            case "peerleave":
            {
                var name = step.First("name", "peer");
                if (name == "*" || (name.Length == 0 && VirtualPeers.All.Count == 1))
                    VirtualPeers.LeaveAll();
                else
                    VirtualPeers.Leave(Need(name), step.Bool("removePuppet", true));
                return Frames(2);
            }
            case "peermove":
            {
                var vp = Need(step.First("peer", "name"));
                var target = step.First("to", "target");
                var pos = target.Length > 0
                    ? (Targets.Find(ctx, target) ?? throw new StepFailed($"'{target}' not found")).transform.position
                    : Targets.PointFrom(step, vp.Position);
                VirtualPeers.Move(vp, pos);
                return Frames(2);
            }
            case "peerrpc":
                return SendRpc(step, ctx);
            case "peerown":
            {
                // give a world object to this player (as if their client had claimed it), or back: "peer": "me"
                var zdo = Zdo(ctx, step.Str("target"));
                zdo.SetOwner(PeerUid(step.First("peer", "name")));
                return Frames(1);
            }
            case "peerset":
                PeerSet(step, ctx);
                return Frames(1);
            case "peermirror":
            {
                // her character copies yours every frame (pose, equipment, facing) until "on": false; "face": same / me / degrees
                var vp = Need(step.First("peer", "name"));
                if (step.Bool("on", true))
                {
                    PeerPose.StartMirror(vp, step.Str("face", "same"), step.Bool("equipment", true));
                    ctx.Log($"  {vp.Name} mirrors you (face {step.Str("face", "same")})");
                }
                else
                {
                    PeerPose.StopMirror(vp);
                    ctx.Log($"  {vp.Name} stopped mirroring");
                }
                return Frames((int)step.Num("frames", 3f));
            }
            case "peerpose":
            {
                var vp = Need(step.First("peer", "name"));
                ctx.Log($"  {vp.Name} pose: {PeerPose.Pose(vp, step)}");
                return Frames((int)step.Num("frames", 3f));
            }
            case "chat":
            {
                // you type into chat and press Enter: "/s hi", "/w hi", "/wave", a mod's command, or plain text
                var text = step.First("text", "say");
                ChatSteps.Type(text);
                ctx.Log($"  you: {text}");
                return Frames((int)step.Num("frames", 5f));
            }
            case "peerchat":
            {
                var vp = Need(step.First("peer", "name"));
                ctx.Log($"  {vp.Name}: {ChatSteps.PeerType(vp, step.First("text", "say"))}");
                return Frames((int)step.Num("frames", 5f));
            }
            case "peeremote":
            {
                var vp = Need(step.First("peer", "name"));
                ctx.Log($"  {vp.Name}: {ChatSteps.Emote(vp, step.First("emote", "name"), step.Bool("stop"))}");
                return Frames((int)step.Num("frames", 5f));
            }
            case "peerping":
            {
                var vp = Need(step.First("peer", "name"));
                var target = step.First("to", "target");
                var at = target.Length > 0
                    ? (Targets.Find(ctx, target) ?? throw new StepFailed($"'{target}' not found")).transform.position
                    : Targets.PointFrom(step, P.transform.position);
                ctx.Log($"  {vp.Name}: {ChatSteps.Ping(vp, at)}");
                return Frames((int)step.Num("frames", 5f));
            }
            case "pvp":
                ctx.Log($"  {PvpSteps.SetMine(step.Bool("on", true))}");
                return Frames(3);
            case "peerpvp":
            {
                var vp = Need(step.First("peer", "name"));
                ctx.Log($"  {PvpSteps.SetHers(vp, step.Bool("on", true))}");
                return Frames(3);
            }
            case "attack":
                // you swing at "target": a tag, an object, or a virtual player's name (default: player 2)
                return PvpSteps.Attack(step, ctx, AttackTarget(step.First("target", "peer"), ctx));
            case "peerattack":
            {
                var vp = Need(step.First("peer", "name"));
                var t = step.First("target", "at");
                var target = t.Length == 0 || t.Equals("me", StringComparison.OrdinalIgnoreCase) ? P.gameObject : AttackTarget(t, ctx);
                ctx.Log($"  {vp.Name} {PvpSteps.PeerAttack(vp, step, target)}");
                return Wait(step.Num("wait", 0.3f) + vp.ClientSocket.Latency * 2f, ctx);
            }
            case "peerhealth":
            {
                var vp = Need(step.First("peer", "name"));
                ctx.Log($"  {PvpSteps.SetHealth(vp, step)}");
                return Frames(3);
            }
            case "chatclear":
            {
                ChatSteps.Log.Clear();
                foreach (var vp in VirtualPeers.All)
                    vp.Inbox.Clear();
                return null;
            }
            case "peerequip":
            {
                var vp = Need(step.First("peer", "name"));
                ctx.Log($"  {vp.Name} equips: {PeerPose.Equip(vp, step)}");
                return Frames((int)step.Num("frames", 3f));
            }
            case "peerclear":
            {
                var name = step.First("peer", "name");
                foreach (var vp in VirtualPeers.All)
                {
                    if (name.Length == 0 || vp.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        vp.Inbox.Clear();
                }

                return null;
            }
        }

        throw new StepFailed($"unknown peer step '{action}'");
    }

    private static GameObject AttackTarget(string name, RunContext ctx)
    {
        if (name.Length == 0)
            name = TestLabConfig.Instance.Player2.Value;
        if (VirtualPeers.Find(name) is { } vp)
            return vp.Puppet ?? throw new StepFailed($"{vp.Name}'s character is not in the world");
        return Targets.Find(ctx, name) ?? throw new StepFailed($"'{name}' not found");
    }

    private static IEnumerator Wait(float seconds, RunContext ctx)
    {
        var until = Time.realtimeSinceStartup + seconds;
        yield return null;
        while (Time.realtimeSinceStartup < until && !ctx.TimedOut)
            yield return null;
    }

    private static IEnumerator Frames(int n)
    {
        for (var i = 0; i < n; i++)
            yield return null;
    }

    private static IEnumerator WaitForPuppet(VirtualPeer vp, string tag, RunContext ctx)
    {
        if (vp.PuppetZdo == null)
            yield break;
        var until = Time.realtimeSinceStartup + 5f;
        while (vp.Puppet == null && Time.realtimeSinceStartup < until && !ctx.TimedOut)
            yield return null;
        var go = vp.Puppet ?? throw new StepFailed($"{vp.Name}'s character did not appear in the world");
        ctx.Tags[tag] = go;
        ctx.Log($"  {vp.Name} joined as peer {vp.Uid} (player {vp.PlayerId})");
    }

    /// <summary>
    /// A routed RPC from the virtual player's client. "target": an object (sent to its owner, as a real client does) or
    /// "to": server / everybody / a player name. "args" + "types"; "sender" forges the sender id (host, me, a player name, a number).
    /// </summary>
    private static IEnumerator SendRpc(Dictionary<string, object?> step, RunContext ctx)
    {
        var vp = Need(step.First("peer", "name"));
        var method = step.Str("method");
        if (method.Length == 0)
            throw new StepFailed("peerRpc needs \"method\"");

        var zdoId = ZDOID.None;
        long targetPeer;
        var target = step.Str("target");
        if (target.Length > 0)
        {
            var zdo = Zdo(ctx, target);
            zdoId = zdo.m_uid;
            targetPeer = zdo.GetOwner();
            if (targetPeer == vp.Uid)
                throw new StepFailed($"{vp.Name} owns '{target}': their own client would run {method} itself, nothing reaches the server (peerOwn it to someone else first)");
            if (targetPeer == 0L)
                targetPeer = ZDOMan.GetSessionID();
        }
        else
        {
            var to = step.Str("to", "server");
            targetPeer = to.Equals("everybody", StringComparison.OrdinalIgnoreCase) ? 0L : PeerUid(to);
        }

        long? sender = step.Has("sender") ? PeerUid(step.Str("sender")) : null;
        var args = VirtualPeers.Args(step.Arr("args"), step.Arr("types"), ctx);
        vp.SendRoutedRpc(targetPeer, zdoId, method, args, sender);
        ctx.Log($"  {vp.Name} -> {method}({string.Join(", ", args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)))}){(sender != null ? $" as sender {sender}" : "")}");

        // the server reads it on its next update; give replies time to come back
        var until = Time.realtimeSinceStartup + step.Num("wait", 0.2f) + vp.ClientSocket.Latency * 2f;
        yield return null;
        while (Time.realtimeSinceStartup < until && !ctx.TimedOut)
            yield return null;
    }

    /// <summary>The player writes a value on an object's ZDO, as their client would after owning it (they take ownership unless "keepOwner").</summary>
    private static void PeerSet(Dictionary<string, object?> step, RunContext ctx)
    {
        var vp = Need(step.First("peer", "name"));
        var zdo = Zdo(ctx, step.Str("target"));
        var key = step.Str("key");
        if (key.Length == 0)
            throw new StepFailed("peerSet needs \"key\"");
        if (!step.Bool("keepOwner"))
            zdo.SetOwner(vp.Uid);

        var hash = key.GetStableHashCode();
        var raw = step.TryGetValue("value", out var v) ? v : null;
        var type = step.Str("type").ToLowerInvariant();
        if (type.Length == 0)
            type = raw switch { bool => "bool", double d when Math.Abs(d % 1) < 1e-9 => "int", double => "float", _ => "string" };
        var inv = CultureInfo.InvariantCulture;
        switch (type)
        {
            case "int": zdo.Set(hash, Convert.ToInt32(raw, inv)); break;
            case "long": zdo.Set(hash, Convert.ToInt64(raw, inv)); break;
            case "float": zdo.Set(hash, Convert.ToSingle(raw, inv)); break;
            case "bool": zdo.Set(hash, Convert.ToBoolean(raw, inv)); break;
            default: zdo.Set(hash, Convert.ToString(raw, inv) ?? ""); break;
        }

        ctx.Log($"  {vp.Name} set {key} = {J.Value(raw)} on {step.Str("target")}");
    }

    // ------------------------------------------------------------------ checks

    public static bool HandlesCheck(string kind) =>
        CheckNames.Any(n => n.Equals(kind, StringComparison.OrdinalIgnoreCase)) || ChatSteps.CheckNames.Any(n => n.Equals(kind, StringComparison.OrdinalIgnoreCase))
        || PvpSteps.CheckNames.Any(n => n.Equals(kind, StringComparison.OrdinalIgnoreCase));

    public static (bool, string) Check(string kind, Dictionary<string, object?> a, RunContext ctx)
    {
        switch (kind.ToLowerInvariant())
        {
            case "pvp":
                return PvpSteps.PvpCheck(a);
            case "health":
                return PvpSteps.HealthCheck(a);
            case "chatseen":
                return ChatSteps.Seen(a);
            case "peerheard":
                return ChatSteps.Heard(Need(a.First("peer", "name")), a);
            case "emote":
                return ChatSteps.EmoteCheck(a);
            case "peeranim":
                return PeerPose.Check(Need(a.First("peer", "name")), a);
            case "peerconnected":
            {
                var name = a.First("peer", "name");
                var vp = VirtualPeers.Find(name);
                var connected = vp != null && vp.Connected;
                var want = a.Bool("expect", true);
                return (connected == want, connected ? $"{name} is connected" : $"{name} is not connected");
            }
            case "zdoowner":
            {
                var zdo = Zdo(ctx, a.Str("target"));
                var owner = zdo.GetOwner();
                var want = a.Str("expect", "me");
                // "other": some player who is not me (a real Player 2 has the same character name as I do)
                if (want.Equals("other", StringComparison.OrdinalIgnoreCase))
                    return (owner != 0L && owner != PeerUid("me"), $"owner is {Describe(owner)}, expected another player");
                var wantUid = want.Equals("none", StringComparison.OrdinalIgnoreCase) ? 0L : PeerUid(want);
                return (owner == wantUid, $"owner is {Describe(owner)}, expected {want}");
            }
            case "peerreceived":
            {
                // "method", optional "target" (object), "args" + "types" ("*" = any), "count" (at least, default 1), "max"
                var vp = Need(a.First("peer", "name"));
                var method = a.Str("method");
                VirtualPeers.Remember(method);
                var hash = method.GetStableHashCode();
                ZDOID? zdoId = a.Str("target").Length > 0 ? Zdo(ctx, a.Str("target")).m_uid : null;
                var types = a.Arr("types");
                var expected = a.Arr("args");
                var matches = 0;
                var seen = new List<string>();
                foreach (var m in vp.Inbox)
                {
                    if (m.Hash != hash || (zdoId != null && m.Target != zdoId.Value))
                        continue;
                    var values = types.Count > 0 ? VirtualPeers.Read(m, types) : new List<object?>();
                    seen.Add(values.Count > 0 ? "(" + string.Join(", ", values.Select(J.Value)) + ")" : "()");
                    if (ArgsMatch(values, expected))
                        matches++;
                }

                // "count" = at least (default 1, or 0 when only "max" is given: "max": 0 = must not have arrived)
                var min = (int)a.Num("count", a.Has("max") ? 0 : 1);
                var max = a.Has("max") ? (int)a.Num("max") : int.MaxValue;
                var others = vp.Inbox.Where(m => m.Hash != hash).Select(m => m.Method).Distinct().ToList();
                var detail = $"{matches} matching {method}{(seen.Count > 0 ? " " + string.Join(" ", seen) : "")}" +
                             $" (need {(max == int.MaxValue ? $"{min}+" : $"{min}..{max}")}){(others.Count > 0 ? "; also got " + string.Join(", ", others) : "")}";
                return (matches >= min && matches <= max, detail);
            }
        }

        throw new InvalidOperationException($"unknown peer check '{kind}'");
    }

    private static bool ArgsMatch(List<object?> values, List<object?> expected)
    {
        for (var i = 0; i < expected.Count && i < values.Count; i++)
        {
            var want = expected[i];
            if (want is string s && s == "*")
                continue;
            if (want is string at && at.StartsWith("@", StringComparison.Ordinal))
                want = (double)WardTools.Parse(at.Substring(1)).Id;
            var got = values[i];
            var same = want switch
            {
                double d => got != null && Math.Abs(Convert.ToDouble(got, CultureInfo.InvariantCulture) - d) < 1e-6,
                bool b => got is bool gb && gb == b,
                _ => string.Equals(Convert.ToString(got, CultureInfo.InvariantCulture), Convert.ToString(want, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            };
            if (!same)
                return false;
        }

        return true;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A connected virtual player; no name = [Automation] Player2 (Bjornita).</summary>
    private static VirtualPeer Need(string name)
    {
        if (name.Length == 0)
            name = TestLabConfig.Instance.Player2.Value;
        return VirtualPeers.Find(name) ?? throw new StepFailed($"no virtual player '{name}' (peerJoin first)");
    }

    /// <summary>A peer id: me / host / server = this game, a virtual player's name, or a number.</summary>
    private static long PeerUid(string who)
    {
        if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase) || who.Equals("host", StringComparison.OrdinalIgnoreCase)
            || who.Equals("server", StringComparison.OrdinalIgnoreCase))
            return ZDOMan.GetSessionID();
        var vp = VirtualPeers.Find(who);
        if (vp != null)
            return vp.Uid;
        if (long.TryParse(who, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return n;
        throw new StepFailed($"'{who}' is not me, host, a virtual player or a peer id");
    }

    private static string Describe(long uid)
    {
        if (uid == 0L)
            return "nobody";
        if (uid == ZDOMan.GetSessionID())
            return "me";
        var vp = VirtualPeers.All.Find(p => p.Uid == uid);
        return vp != null ? vp.Name : $"peer {uid}";
    }

    private static ZDO Zdo(RunContext ctx, string target)
    {
        if (target.Length == 0)
            throw new StepFailed("needs \"target\"");
        var go = Targets.Find(ctx, target) ?? throw new StepFailed($"'{target}' not found");
        var view = go.GetComponentInParent<ZNetView>() ?? throw new StepFailed($"'{target}' is not a networked object");
        return view.GetZDO() ?? throw new StepFailed($"'{target}' has no ZDO");
    }
}
