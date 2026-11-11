using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace DrakesTestLabServer;

/// <summary>
/// Who is Player 1 and who is Player 2, and the relay between them. Lab clients say hello with the role they want; the
/// server hands each role to one client. Player 1 sends commands to the server, which forwards them to Player 2 and sends
/// the answer back. Clients never address each other directly, so the server is always the one that decides.
/// Everything rides on the game's own routed RPC (no extra port or backend).
/// </summary>
internal static class Roles
{
    public const string Player1 = "Player1";
    public const string Player2 = "Player2";

    private static readonly Dictionary<string, long> Holders = new();

    public static void Register()
    {
        var rpc = ZRoutedRpc.instance;
        if (rpc == null)
        {
            LabServerPlugin.Log?.LogWarning("Role relay not registered: no routed RPC yet.");
            return;
        }

        Holders.Clear();
        rpc.Register<string>("DTL_Hello", OnHello);
        rpc.Register<string, string, string>("DTL_Cmd", OnCmd);
        rpc.Register<string, bool, string>("DTL_ResultUp", OnResult);
        LabServerPlugin.Log?.LogInfo("Role relay ready: DTL_Hello, DTL_Cmd, DTL_ResultUp.");
    }

    private static bool Connected(long id) => ZNet.instance != null && ZNet.instance.GetPeer(id) != null;

    private static string NameOf(long id) => ZNet.instance?.GetPeer(id)?.m_playerName ?? id.ToString();

    private static bool Holds(string role, long id) =>
        Holders.TryGetValue(role, out var holder) && holder == id && Connected(holder);

    /// <summary>True when this peer is the connected client that holds the Player1 role.</summary>
    public static bool IsPlayer1(long id) => Holds(Player1, id);

    /// <summary>" Player1" / " Player2" for a peer that holds a role, else an empty string (for the player list).</summary>
    public static string RoleOf(long id) =>
        Holds(Player1, id) ? " " + Player1 : Holds(Player2, id) ? " " + Player2 : "";

    private static void Tell(long peer, string role, string message) =>
        ZRoutedRpc.instance.InvokeRoutedRPC(peer, "DTL_Role", role, message);

    private static void OnHello(long sender, string wanted)
    {
        wanted = (wanted ?? "").Trim();
        if (wanted != Player1 && wanted != Player2)
        {
            LabServerPlugin.Log?.LogWarning($"Hello from {NameOf(sender)}: unknown role '{wanted}'.");
            Tell(sender, "", $"unknown role '{wanted}' (use {Player1} or {Player2})");
            return;
        }

        if (Holders.TryGetValue(wanted, out var holder) && holder != sender && Connected(holder))
        {
            LabServerPlugin.Log?.LogWarning($"Hello from {NameOf(sender)}: {wanted} is already taken by {NameOf(holder)}.");
            Tell(sender, "", $"{wanted} is already taken by {NameOf(holder)}");
            return;
        }

        // one role per client, and a role left by a client that has gone is free again
        foreach (var stale in Holders.Where(kv => (kv.Value == sender && kv.Key != wanted) || !Connected(kv.Value)).Select(kv => kv.Key).ToList())
            Holders.Remove(stale);

        Holders[wanted] = sender;
        LabServerPlugin.Log?.LogInfo($"{NameOf(sender)} is {wanted}.");
        Tell(sender, wanted, "");
    }

    private static void OnCmd(long sender, string targetRole, string id, string stepJson)
    {
        if (!Holds(Player1, sender))
        {
            Result(sender, id, false, $"only {Player1} may send commands to {Player2}");
            return;
        }

        if (targetRole != Player2 || !Holders.TryGetValue(Player2, out var target) || !Connected(target))
        {
            Result(sender, id, false, NoPlayer2);
            return;
        }

        LabServerPlugin.Log?.LogInfo($"Command {id} {Player1} -> {Player2}: {Trim(stepJson)}");
        ZRoutedRpc.instance.InvokeRoutedRPC(target, "DTL_Run", id, stepJson);
    }

    private static void OnResult(long sender, string id, bool ok, string detail)
    {
        if (!Holds(Player2, sender))
            return;

        if (!Holders.TryGetValue(Player1, out var p1) || !Connected(p1))
        {
            LabServerPlugin.Log?.LogWarning($"Result {id} from {Player2} dropped: {Player1} is gone.");
            return;
        }

        LabServerPlugin.Log?.LogInfo($"Result {id} {Player2} -> {Player1}: {(ok ? "ok" : "FAILED")} {Trim(detail)}");
        Result(p1, id, ok, detail);
    }

    /// <summary>The text Player 1 gets when no Player 2 is connected (the client retries on exactly this text).</summary>
    public const string NoPlayer2 = "no Player 2 connected";

    private static void Result(long peer, string id, bool ok, string detail) =>
        ZRoutedRpc.instance.InvokeRoutedRPC(peer, "DTL_Result", id, ok, detail);

    private static string Trim(string s) => s != null && s.Length > 160 ? s.Substring(0, 160) + "..." : s ?? "";
}

[HarmonyPatch(typeof(ZNet), "Awake")]
internal static class ZNet_Awake_Roles_Patch
{
    // the routed RPC is created in ZNet.Awake, so the handlers can only be registered after it
    private static void Postfix(ZNet __instance)
    {
        if (!__instance.IsServer())
            return;

        Roles.Register();
        if (ZRoutedRpc.instance != null)
            Control.Register(ZRoutedRpc.instance);
    }
}
