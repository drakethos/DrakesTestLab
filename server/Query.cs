using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLabServer;

/// <summary>
/// The server's own view of things, for a test to assert on: who is connected (and as what), the ZDOs in the world (how many, who owns
/// them, what is stored in them) and the routed RPC traffic the server has seen. Read-only; every answer is a short value that a step
/// can compare with <c>expect</c>, <c>contains</c> or <c>min</c> / <c>max</c>.
///
/// A ZDO is picked with <c>prefab</c> plus where: <c>"near": "me"</c> (around Player 1), or <c>x</c> / <c>z</c>, within <c>radius</c> metres
/// (default 30). Without a place the whole world is searched. When several match, the nearest to that place is used.
/// </summary>
internal static class Query
{
    public static bool Handles(string cmd) =>
        cmd.StartsWith("znet.", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("zdo.", StringComparison.OrdinalIgnoreCase)
        || cmd.StartsWith("world.", StringComparison.OrdinalIgnoreCase) || cmd.StartsWith("rpc.", StringComparison.OrdinalIgnoreCase)
        || cmd.Equals("server.info", StringComparison.OrdinalIgnoreCase);

    public static (bool, string) Run(long sender, string cmd, Dictionary<string, object?> a)
    {
        switch (cmd.ToLowerInvariant())
        {
            case "server.info":
                return (true, $"version={Version.GetVersionString()} peers={Peers().Count} zdos={ZDOMan.instance.NrOfObjects()} time={ZNet.instance.GetTimeSeconds():0}");

            case "znet.count": return (true, Peers().Count.ToString(CultureInfo.InvariantCulture));
            case "znet.peers": return (true, string.Join("; ", Peers().Select(Describe)));
            case "znet.peer":
            {
                var peer = PeerOf(sender, a);
                return (true, Describe(peer));
            }

            case "world.zdocount": return (true, ZDOMan.instance.NrOfObjects().ToString(CultureInfo.InvariantCulture));
            case "world.time": return (true, ZNet.instance.GetTimeSeconds().ToString("0", CultureInfo.InvariantCulture));

            case "zdo.count":
                return (true, Select(sender, a).Count.ToString(CultureInfo.InvariantCulture));
            case "zdo.owner":
                return (true, OwnerName(Nearest(sender, a).GetOwner()));
            case "zdo.describe":
            {
                var z = Nearest(sender, a);
                var p = z.GetPosition();
                return (true, $"owner={OwnerName(z.GetOwner())} pos={p.x:0},{p.y:0},{p.z:0} persistent={z.Persistent} id={z.m_uid}");
            }

            case "zdo.value":
            {
                var z = Nearest(sender, a);
                var key = Str(a, "key");
                if (key.Length == 0)
                    throw new InvalidOperationException("zdo.value needs the ZDO key to read (\"key\")");
                switch (Str(a, "type").ToLowerInvariant())
                {
                    case "int": return (true, z.GetInt(key, 0).ToString(CultureInfo.InvariantCulture));
                    case "long": return (true, z.GetLong(key, 0L).ToString(CultureInfo.InvariantCulture));
                    case "float": return (true, z.GetFloat(key, 0f).ToString(CultureInfo.InvariantCulture));
                    case "bool": return (true, z.GetBool(key, false).ToString());
                    case "string":
                    case "": return (true, z.GetString(key, ""));
                    default: throw new InvalidOperationException("type is string, int, long, float or bool");
                }
            }

            case "rpc.reset":
                RpcLog.Reset();
                return (true, "cleared");
            case "rpc.count":
                return (true, RpcLog.Count(Str(a, "method"), WhoId(sender, a)).ToString(CultureInfo.InvariantCulture));
            case "rpc.last":
                return (true, RpcLog.Last(Str(a, "method")));

            default:
                return (false, $"unknown query '{cmd}' (server.info, znet.count/peers/peer, world.zdocount/time, zdo.count/owner/describe/value, rpc.reset/count/last)");
        }
    }

    // ------------------------------------------------------------------ peers

    private static string Str(Dictionary<string, object?> a, string key) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

    private static float Num(Dictionary<string, object?> a, string key, float def) =>
        a.TryGetValue(key, out var v) && v != null && float.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;

    private static List<ZNetPeer> Peers()
    {
        var all = AccessTools.Field(typeof(ZNet), "m_peers")?.GetValue(ZNet.instance) as IEnumerable;
        var list = new List<ZNetPeer>();
        if (all != null)
            foreach (var o in all)
                if (o is ZNetPeer p && p.IsReady())
                    list.Add(p);
        return list;
    }

    private static string Describe(ZNetPeer p)
    {
        var host = p.m_socket.GetHostName();
        var pos = p.GetRefPos();
        return $"name={p.m_playerName} uid={p.m_uid} host={host} admin={ZNet.instance.IsAdmin(host)} role={Roles.RoleOf(p.m_uid).Trim()} pos={pos.x:0},{pos.y:0},{pos.z:0}";
    }

    private static ZNetPeer PeerOf(long sender, Dictionary<string, object?> a)
    {
        var who = Str(a, "who");
        if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase))
            return ZNet.instance.GetPeer(sender) ?? throw new InvalidOperationException("the sender is no longer connected");
        return ZNet.instance.GetPeerByPlayerName(who) ?? throw new InvalidOperationException($"no connected player named '{who}'");
    }

    /// <summary>The peer id of "me" or a named player; 0 when no "who" is given.</summary>
    private static long WhoId(long sender, Dictionary<string, object?> a) =>
        Str(a, "who").Length == 0 && Str(a, "from").Length == 0 ? 0L : PeerOf(sender, new Dictionary<string, object?>(a, StringComparer.OrdinalIgnoreCase) { ["who"] = Str(a, "who").Length > 0 ? Str(a, "who") : Str(a, "from") }).m_uid;

    private static string OwnerName(long owner)
    {
        if (owner == 0L)
            return "none";
        if (owner == ZDOMan.GetSessionID())
            return "server";
        return ZNet.instance.GetPeer(owner)?.m_playerName ?? owner.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ ZDOs

    private static List<ZDO> Select(long sender, Dictionary<string, object?> a)
    {
        var prefab = Str(a, "prefab");
        if (prefab.Length == 0)
            throw new InvalidOperationException("a ZDO query needs \"prefab\"");

        var all = new List<ZDO>();
        var index = 0;
        for (var guard = 0; guard < 4000; guard++)
            if (ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, all, ref index))
                break;

        var origin = Origin(sender, a);
        if (origin == null)
            return all;
        var radius = Num(a, "radius", 30f);
        return all.Where(z => Flat(z.GetPosition(), origin.Value) <= radius).ToList();
    }

    private static ZDO Nearest(long sender, Dictionary<string, object?> a)
    {
        var found = Select(sender, a);
        if (found.Count == 0)
            throw new InvalidOperationException($"no ZDO of '{Str(a, "prefab")}' found there");
        var origin = Origin(sender, a);
        return origin == null ? found[0] : found.OrderBy(z => Flat(z.GetPosition(), origin.Value)).First();
    }

    private static Vector3? Origin(long sender, Dictionary<string, object?> a)
    {
        var near = Str(a, "near");
        if (near.Length > 0)
            return PeerOf(sender, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["who"] = near }).GetRefPos();
        if (a.ContainsKey("x") && a.ContainsKey("z"))
            return new Vector3(Num(a, "x", 0f), 0f, Num(a, "z", 0f));
        return null;
    }

    private static float Flat(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}

/// <summary>Counts the routed RPCs that pass through the server (every client-to-client and client-to-server message), by method.</summary>
internal static class RpcLog
{
    private struct Entry
    {
        public int Hash;
        public long Sender;
        public long Target;
        public string Name;
    }

    private static readonly List<Entry> Seen = new();
    private static readonly Dictionary<int, string> Names = new();

    public static void Reset() => Seen.Clear();

    public static void Record(int hash, long sender, long target)
    {
        Names.TryGetValue(hash, out var name);
        Seen.Add(new Entry { Hash = hash, Sender = sender, Target = target, Name = name ?? "" });
        if (Seen.Count > 5000)
            Seen.RemoveRange(0, 1000);
    }

    public static int Count(string method, long fromPeer)
    {
        var hash = Learn(method);
        return Seen.Count(e => e.Hash == hash && (fromPeer == 0L || e.Sender == fromPeer));
    }

    public static string Last(string method)
    {
        var hash = Learn(method);
        for (var i = Seen.Count - 1; i >= 0; i--)
        {
            if (Seen[i].Hash != hash)
                continue;
            var from = ZNet.instance.GetPeer(Seen[i].Sender)?.m_playerName ?? Seen[i].Sender.ToString(CultureInfo.InvariantCulture);
            var to = Seen[i].Target == 0L ? "everybody" : ZNet.instance.GetPeer(Seen[i].Target)?.m_playerName ?? Seen[i].Target.ToString(CultureInfo.InvariantCulture);
            return $"from={from} to={to}";
        }

        return "none";
    }

    private static int Learn(string method)
    {
        if (method.Length == 0)
            throw new InvalidOperationException("an rpc query needs \"method\" (the RPC name, for example RPC_RequestOpen)");
        var hash = StableHash(method);
        Names[hash] = method;
        return hash;
    }

    // the game's own string hash (so a name can be matched to the hash an RPC carries)
    private static int StableHash(string str)
    {
        var num = 5381;
        var num2 = num;
        for (var i = 0; i < str.Length && str[i] != '\0'; i += 2)
        {
            num = ((num << 5) + num) ^ str[i];
            if (i == str.Length - 1 || str[i + 1] == '\0')
                break;
            num2 = ((num2 << 5) + num2) ^ str[i + 1];
        }

        return num + num2 * 1566083941;
    }
}

[HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
internal static class ZRoutedRpc_RouteRPC_Patch
{
    // every routed RPC the server receives goes through RouteRPC; note who sent it and to whom
    private static void Prefix(ZRoutedRpc.RoutedRPCData rpcData)
    {
        try { RpcLog.Record(rpcData.m_methodHash, rpcData.m_senderPeerID, rpcData.m_targetPeerID); }
        catch { /* never let the log affect the game */ }
    }
}
