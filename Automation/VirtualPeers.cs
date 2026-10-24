using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// One end of an in-memory connection. Packages sent here arrive at the other end (after an optional delay, or dropped
/// at random), the way a network socket would deliver them. The game's own ZRpc reads and writes it.
/// </summary>
internal sealed class LoopSocket : ISocket
{
    private readonly Queue<(float At, byte[] Data)> _inbox = new();
    private readonly string _name;
    private LoopSocket? _other;
    private bool _closed;

    /// <summary>Delay (seconds) and loss (0..1) for packages this end sends.</summary>
    public float Latency;
    public float DropRate;

    /// <summary>What GetSendQueueSize reports. A large value stops the server streaming world data (ZDOs) to this end.</summary>
    public int ReportedQueueSize;

    private LoopSocket(string name) => _name = name;

    public static (LoopSocket Server, LoopSocket Client) Pair(string name)
    {
        var server = new LoopSocket("virtual:" + name);
        var client = new LoopSocket("virtual-server");
        server._other = client;
        client._other = server;
        return (server, client);
    }

    public bool IsConnected() => !_closed;

    public void Send(ZPackage pkg)
    {
        if (_closed || _other == null || _other._closed)
            return;
        if (DropRate > 0f && UnityEngine.Random.value < DropRate)
            return;
        // ZRpc reuses its package buffer, so copy the bytes now
        _other._inbox.Enqueue((Time.realtimeSinceStartup + Latency, pkg.GetArray()));
    }

    public ZPackage? Recv()
    {
        if (_inbox.Count == 0 || _inbox.Peek().At > Time.realtimeSinceStartup)
            return null;
        return new ZPackage(_inbox.Dequeue().Data);
    }

    public bool GotNewData() => _inbox.Count > 0 && _inbox.Peek().At <= Time.realtimeSinceStartup;
    public int GetSendQueueSize() => ReportedQueueSize;
    public int GetCurrentSendRate() => 0;
    public bool IsHost() => false;
    public void Dispose() => Close();

    public void Close()
    {
        _closed = true;
        if (_other != null)
            _other._closed = true;
    }

    public string GetEndPointString() => _name;
    public void GetAndResetStats(out int totalSent, out int totalRecv) { totalSent = 0; totalRecv = 0; }

    public void GetConnectionQuality(out float localQuality, out float remoteQuality, out int ping, out float outByteSec, out float inByteSec)
    {
        localQuality = remoteQuality = 1f;
        ping = (int)(Latency * 1000f);
        outByteSec = inByteSec = 0f;
    }

    public ISocket? Accept() => null;
    public int GetHostPort() => 0;
    public bool Flush() => true;
    public string GetHostName() => _name;
    public void VersionMatch() { }
}

/// <summary>A routed RPC the server sent to a virtual player.</summary>
internal sealed class PeerMessage
{
    public string Method = "";   // the name when known (an RPC a test sent or checked for), else "#hash"
    public int Hash;
    public long Sender;
    public ZDOID Target;
    public byte[] Parameters = Array.Empty<byte>();
    public float Time;
}

/// <summary>
/// A pretend second player connected to this game (the test world is the server). The server talks to it through the
/// game's own peer code: it joins the peer lists, owns objects, gets routed RPCs (kept in <see cref="Inbox"/> for checks)
/// and sends routed RPCs that the server handles exactly like ones from a real client, including forged sender ids.
/// Its character in the world is a remote-player puppet whose ZDO it owns.
/// What it cannot do: run its own copy of the mods (a second client's local state, UI and config) or real network timing.
/// </summary>
internal sealed class VirtualPeer
{
    public string Name = "";
    public long PlayerId;
    public long Uid;
    public ZNetPeer ServerPeer = null!;
    public LoopSocket ServerSocket = null!;
    public LoopSocket ClientSocket = null!;
    public ZRpc ClientRpc = null!;
    public ZDO? PuppetZdo;
    public readonly List<PeerMessage> Inbox = new();

    /// <summary>
    /// Answer like a second client: RPCs routed to this player for objects they own run through the same mod code here,
    /// with their character as the local player, and what that code sends back leaves through their connection.
    /// </summary>
    public bool Proxy = true;
    private long _msgCounter = 1;

    public bool Connected => ServerPeer != null && ServerSocket.IsConnected() && ZNet.instance != null && ZNet.instance.GetPeers().Contains(ServerPeer);

    public GameObject? Puppet
    {
        get
        {
            if (PuppetZdo == null || ZNetScene.instance == null)
                return null;
            var view = ZNetScene.instance.FindInstance(PuppetZdo);
            return view != null ? view.gameObject : null;
        }
    }

    public Vector3 Position => PuppetZdo != null ? PuppetZdo.GetPosition() : ServerPeer.m_refPos;

    /// <summary>Sends a routed RPC from this player's client, as its ZRoutedRpc would. <paramref name="sender"/> forges the sender id.</summary>
    public void SendRoutedRpc(long targetPeer, ZDOID targetZdo, string method, object[] args, long? sender = null)
    {
        var data = new ZRoutedRpc.RoutedRPCData
        {
            m_msgID = Uid + _msgCounter++,
            m_senderPeerID = sender ?? Uid,
            m_targetPeerID = targetPeer,
            m_targetZDO = targetZdo,
            m_methodHash = method.GetStableHashCode()
        };
        ZRpc.Serialize(args, ref data.m_parameters);
        data.m_parameters.SetPos(0);
        var pkg = new ZPackage();
        data.Serialize(pkg);
        VirtualPeers.Remember(method);
        ClientRpc.Invoke("RoutedRPC", pkg);
    }

    public void OnRoutedRpc(ZRpc rpc, ZPackage pkg)
    {
        var data = new ZRoutedRpc.RoutedRPCData();
        data.Deserialize(pkg);
        Inbox.Add(new PeerMessage
        {
            Hash = data.m_methodHash,
            Method = VirtualPeers.NameOf(data.m_methodHash),
            Sender = data.m_senderPeerID,
            Target = data.m_targetZDO,
            Parameters = data.m_parameters.GetArray(),
            Time = UnityEngine.Time.realtimeSinceStartup
        });
        if (Proxy && data.m_senderPeerID != Uid && !data.m_targetZDO.IsNone())
            VirtualPeers.RunAsClient(this, data);
    }
}

/// <summary>Creates, pumps and removes virtual players. Everything goes away when a test ends.</summary>
internal static class VirtualPeers
{
    public static readonly List<VirtualPeer> All = new();
    private static readonly Dictionary<int, string> Names = new();
    private static Pump? _pump;

    private static readonly MethodInfo? OnNewConnection = AccessTools.Method(typeof(ZNet), "OnNewConnection");
    private static readonly MethodInfo? SendPlayerList = AccessTools.Method(typeof(ZNet), "SendPlayerList");
    private static readonly FieldInfo? PeerPlayerId = AccessTools.Field(typeof(ZNetPeer), "m_playerID");

    /// <summary>Set while a virtual player's client is running mod code (see <see cref="RunAsClient"/>).</summary>
    public static VirtualPeer? ActingAs;

    /// <summary>The object a proxied handler is running on, and whom the handler gave it to (ZDO.SetOwner patch below).</summary>
    public static ZDO? ProxiedZdo;
    public static long? HandedTo;

    /// <summary>
    /// What this player's client would do with an RPC for an object it owns: run the object's handler (the same mod code)
    /// as the owner, with their character as Player.m_localPlayer. Routed RPCs the handler sends go out through their
    /// connection (ZRoutedRpc patch below), so the server sees them come from this player. An approximation: there is one
    /// copy of each mod and its state, shared with this game.
    /// </summary>
    public static void RunAsClient(VirtualPeer vp, ZRoutedRpc.RoutedRPCData data)
    {
        var zdo = ZDOMan.instance?.GetZDO(data.m_targetZDO);
        if (zdo == null || zdo.GetOwner() != vp.Uid || ZNetScene.instance == null)
            return; // not theirs: their client would only run effects on its own copy
        var view = ZNetScene.instance.FindInstance(zdo);
        if (view == null)
            return;

        var local = ZDOMan.GetSessionID();
        var previousPlayer = Player.m_localPlayer;
        var puppet = vp.Puppet != null ? vp.Puppet.GetComponent<Player>() : null;
        try
        {
            ActingAs = vp;
            ProxiedZdo = zdo;
            HandedTo = null;
            if (puppet != null)
                Player.m_localPlayer = puppet;
            zdo.SetOwnerInternal(local); // IsOwner() is true for their client (no owner revision: nothing really changed hands)
            data.m_parameters.SetPos(0);
            view.HandleRoutedRPC(data);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Virtual player {vp.Name}: handling {NameOf(data.m_methodHash)} failed: {ex.InnerException?.Message ?? ex.Message}");
        }
        finally
        {
            Player.m_localPlayer = previousPlayer;
            ActingAs = null;
            ProxiedZdo = null;
            // back to them, then the hand-over their client made (if any) as a real ownership change
            zdo.SetOwnerInternal(vp.Uid);
            if (HandedTo is { } to && to != vp.Uid)
                zdo.SetOwner(to);
            HandedTo = null;
        }
    }

    public static void Remember(string method) => Names[method.GetStableHashCode()] = method;
    public static string NameOf(int hash) => Names.TryGetValue(hash, out var n) ? n : "#" + hash;

    public static VirtualPeer? Find(string name) =>
        All.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Connects a virtual player like a client that passed the handshake (ZNet's own peer setup), with a puppet character at <paramref name="pos"/>.</summary>
    public static VirtualPeer Join(string name, Vector3 pos, bool puppet, float latency, float drop, bool streamWorld, bool proxy = true)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
            throw new StepFailed("virtual players need this game to be the server (the test world is)");
        if (OnNewConnection == null)
            throw new StepFailed("ZNet.OnNewConnection not found (game update?)");
        if (Find(name) is { } existing)
            Leave(existing);

        var who = WardTools.Parse(name);
        var (serverSocket, clientSocket) = LoopSocket.Pair(who.Name);
        serverSocket.Latency = latency;
        clientSocket.Latency = latency;
        serverSocket.DropRate = drop;
        clientSocket.DropRate = drop;
        serverSocket.ReportedQueueSize = streamWorld ? 0 : 100000;

        var peer = new ZNetPeer(serverSocket, false);
        OnNewConnection.Invoke(ZNet.instance, new object[] { peer });

        var vp = new VirtualPeer
        {
            Name = who.Name,
            PlayerId = who.Id,
            // stable per name, far from real session ids
            Uid = 7_000_000_000_000L + (who.Name.GetStableHashCode() & 0x7FFFFFFF),
            ServerPeer = peer,
            ServerSocket = serverSocket,
            ClientSocket = clientSocket,
            Proxy = proxy
        };

        // what ZNet.RPC_PeerInfo sets for a client that passed the version/password checks
        peer.m_uid = vp.Uid;
        peer.m_playerName = who.Name;
        PeerPlayerId?.SetValue(peer, who.Id); // newer games only
        peer.m_refPos = pos;

        vp.ClientRpc = new ZRpc(clientSocket);
        vp.ClientRpc.Register<ZPackage>("RoutedRPC", vp.OnRoutedRpc);

        // ZDOMan: the peer counts as connected (its objects are not abandoned) and nearby ownership follows it like a real player
        ZDOMan.instance.AddPeer(peer);
        ZRoutedRpc.instance.AddPeer(peer);
        All.Add(vp);
        EnsurePump();

        if (puppet)
            vp.PuppetZdo = SpawnPuppet(vp, who, pos);
        try
        {
            SendPlayerList?.Invoke(ZNet.instance, null);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Virtual player {who.Name}: player list not sent ({ex.InnerException?.Message ?? ex.Message})");
        }

        TestLabPlugin.Log?.LogInfo($"Virtual player {who.Name} joined (peer {vp.Uid}, player {who.Id}).");
        return vp;
    }

    /// <summary>A remote player's character: the Player prefab on a ZDO this peer owns, with its player id and name.</summary>
    private static ZDO SpawnPuppet(VirtualPeer vp, WardTools.Identity who, Vector3 pos)
    {
        if (ZoneSystem.instance != null)
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        var hash = "Player".GetStableHashCode();
        var prefab = ZNetScene.instance.GetPrefab(hash) ?? throw new StepFailed("no Player prefab");
        var zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
        // what ZNetView.Awake would set for the owner (CreateNewZDO leaves the prefab empty: the game deletes such ZDOs)
        var view = prefab.GetComponent<ZNetView>();
        zdo.SetPrefab(hash);
        zdo.Persistent = view.m_persistent;
        zdo.SetType(view.m_type);
        zdo.SetDistant(view.m_distant);
        zdo.Set(ZDOVars.s_playerID, vp.PlayerId);
        zdo.Set(ZDOVars.s_playerName, vp.Name);
        // the look a remote player's VisEquipment reads (model, hair, colours) from the roster entry
        if (who.Model >= 0)
            zdo.Set("ModelIndex".GetStableHashCode(), who.Model);
        if (who.Hair.Length > 0)
            zdo.Set("HairItem".GetStableHashCode(), who.Hair.GetStableHashCode());
        if (who.Beard.Length > 0)
            zdo.Set("BeardItem".GetStableHashCode(), who.Beard.GetStableHashCode());
        if (who.HairColor is { } hairColor)
            zdo.Set("HairColor".GetStableHashCode(), hairColor);
        if (who.SkinColor is { } skinColor)
            zdo.Set("SkinColor".GetStableHashCode(), skinColor);
        zdo.SetOwner(vp.Uid);
        vp.ServerPeer.m_characterID = zdo.m_uid;
        return zdo;
    }

    public static void Move(VirtualPeer vp, Vector3 pos)
    {
        if (ZoneSystem.instance != null)
            pos.y = ZoneSystem.instance.GetGroundHeight(pos);
        vp.ServerPeer.m_refPos = pos;
        if (vp.PuppetZdo != null)
        {
            vp.PuppetZdo.SetPosition(pos);
            var go = vp.Puppet;
            if (go != null)
                go.transform.position = pos;
        }
    }

    /// <summary>Disconnects through ZNet.Disconnect (what a real client leaving triggers); the puppet is left to the game's cleanup unless <paramref name="removePuppet"/>.</summary>
    public static void Leave(VirtualPeer vp, bool removePuppet = true)
    {
        All.Remove(vp);
        try
        {
            if (ZNet.instance != null && ZNet.instance.GetPeers().Contains(vp.ServerPeer))
                ZNet.instance.Disconnect(vp.ServerPeer);
            else
                vp.ServerSocket.Close();
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Virtual player {vp.Name} disconnect: {ex.Message}");
        }

        if (removePuppet && vp.PuppetZdo != null && ZDOMan.instance != null && ZDOMan.instance.GetZDO(vp.PuppetZdo.m_uid) != null)
        {
            var go = vp.Puppet;
            vp.PuppetZdo.SetOwner(ZDOMan.GetSessionID());
            if (go != null && ZNetScene.instance != null)
                ZNetScene.instance.Destroy(go);
            else
                ZDOMan.instance.DestroyZDO(vp.PuppetZdo);
        }
    }

    public static void LeaveAll()
    {
        foreach (var vp in All.ToArray())
            Leave(vp);
    }

    private static void EnsurePump()
    {
        if (_pump != null)
            return;
        var go = new GameObject("DrakesTestLab_VirtualPeers");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _pump = go.AddComponent<Pump>();
    }

    /// <summary>Runs each virtual client's ZRpc every frame: delivers what the server sent and answers its pings.</summary>
    private sealed class Pump : MonoBehaviour
    {
        private void Update()
        {
            for (var i = All.Count - 1; i >= 0; i--)
            {
                var vp = All[i];
                if (vp.ClientRpc.Update(Time.deltaTime) == ZRpc.ErrorCode.Disconnected)
                {
                    TestLabPlugin.Log?.LogInfo($"Virtual player {vp.Name} was disconnected by the server.");
                    All.RemoveAt(i);
                }
            }
        }
    }

    // ------------------------------------------------------------------ argument helpers

    /// <summary>
    /// Turns JSON args into the typed values the RPC handler expects. "types" names them (int, uint, long, float, double,
    /// bool, string, byte, zdoid); without it whole numbers are int (long when too big) and "@Name" is that player's id (long).
    /// </summary>
    public static object[] Args(List<object?> raw, List<object?> types, RunContext ctx)
    {
        var result = new object[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            var type = i < types.Count ? (Convert.ToString(types[i], CultureInfo.InvariantCulture) ?? "").ToLowerInvariant() : "";
            result[i] = ConvertArg(raw[i], type, ctx);
        }

        return result;
    }

    private static object ConvertArg(object? v, string type, RunContext ctx)
    {
        if (v is string s && s.StartsWith("@", StringComparison.Ordinal))
        {
            var id = WardTools.Parse(s.Substring(1)).Id;
            v = (double)id;
            if (type.Length == 0)
                type = "long";
        }

        var inv = CultureInfo.InvariantCulture;
        switch (type)
        {
            case "int": return Convert.ToInt32(v, inv);
            case "uint": return Convert.ToUInt32(v, inv);
            case "long": return Convert.ToInt64(v, inv);
            case "float": return Convert.ToSingle(v, inv);
            case "double": return Convert.ToDouble(v, inv);
            case "bool": return Convert.ToBoolean(v, inv);
            case "byte": return Convert.ToByte(v, inv);
            case "string": return Convert.ToString(v, inv) ?? "";
            case "zdoid":
            {
                var go = Targets.Find(ctx, Convert.ToString(v, inv) ?? "") ?? throw new StepFailed($"no object '{v}' for a zdoid argument");
                return go.GetComponent<ZNetView>()?.GetZDO()?.m_uid ?? throw new StepFailed($"'{v}' has no ZDO");
            }
        }

        switch (v)
        {
            case bool b: return b;
            case string str: return str;
            case double d when Math.Abs(d % 1) < 1e-9:
                return d is >= int.MinValue and <= int.MaxValue ? (object)(int)d : (long)d;
            case double d: return (float)d;
            case null: throw new StepFailed("null argument: give a value");
            default: return v;
        }
    }

    /// <summary>Reads a message's parameters back as the given types (for checks).</summary>
    public static List<object?> Read(PeerMessage m, List<object?> types)
    {
        var pkg = new ZPackage(m.Parameters);
        var values = new List<object?>();
        foreach (var t in types)
        {
            switch ((Convert.ToString(t, CultureInfo.InvariantCulture) ?? "").ToLowerInvariant())
            {
                case "int": values.Add(pkg.ReadInt()); break;
                case "uint": values.Add(pkg.ReadUInt()); break;
                case "long": values.Add(pkg.ReadLong()); break;
                case "float": values.Add(pkg.ReadSingle()); break;
                case "double": values.Add(pkg.ReadDouble()); break;
                case "bool": values.Add(pkg.ReadBool()); break;
                case "byte": values.Add(pkg.ReadByte()); break;
                case "string": values.Add(pkg.ReadString()); break;
                case "zdoid": values.Add(pkg.ReadZDOID().ToString()); break;
                default: throw new InvalidOperationException($"unknown type '{t}'");
            }
        }

        return values;
    }
}

/// <summary>Virtual players are not on the world's permitted list; without this the server kicks them within 5 s.</summary>
[HarmonyPatch(typeof(ZNet), "ListContainsId")]
internal static class ZNet_ListContainsId_VirtualPeers
{
    private static readonly FieldInfo? Permitted = AccessTools.Field(typeof(ZNet), "m_permittedList");

    private static void Postfix(ZNet __instance, object list, string idString, ref bool __result)
    {
        if (!__result && idString != null && idString.StartsWith("virtual:", StringComparison.Ordinal) && ReferenceEquals(list, Permitted?.GetValue(__instance)))
            __result = true;
    }
}

/// <summary>While a virtual player's client is running mod code, its routed RPCs go out through that player's connection.</summary>
[HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), typeof(long), typeof(ZDOID), typeof(string), typeof(object[]))]
internal static class ZRoutedRpc_Invoke_VirtualPeers
{
    private static bool Prefix(long targetPeerID, ZDOID targetZDO, string methodName, object[] parameters)
    {
        var vp = VirtualPeers.ActingAs;
        if (vp == null)
            return true;
        vp.SendRoutedRpc(targetPeerID, targetZDO, methodName, parameters ?? Array.Empty<object>());
        return false;
    }
}

/// <summary>Notes whom a proxied handler hands its object to (e.g. Container.RPC_RequestOpen gives the chest to the opener).</summary>
[HarmonyPatch(typeof(ZDO), nameof(ZDO.SetOwner))]
internal static class ZDO_SetOwner_VirtualPeers
{
    private static void Prefix(ZDO __instance, long uid)
    {
        if (VirtualPeers.ActingAs != null && ReferenceEquals(__instance, VirtualPeers.ProxiedZdo))
            VirtualPeers.HandedTo = uid;
    }
}
