using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Joins the test server through PlayFab (crossplay), on any platform. The game's own join-by-IP picks the platform's
/// backend: on a Steam client that is Steam, which a crossplay (PlayFab only) server never answers, so the join times out
/// (error 5003). This looks the server up on PlayFab by its public IP and joins it as a PlayFab user, which is what the
/// game's <c>-joincode</c> launch option does. Everything is reflected: these types differ between game versions and the
/// release build compiles against older game DLLs.
/// </summary>
internal static class PlayFabJoin
{
    private static readonly Type? ManagerType = AccessTools.TypeByName("PlayFabManager");
    private static readonly Type? MatchmakingType = AccessTools.TypeByName("ZPlayFabMatchmaking");
    private static readonly Type? DedicatedType = AccessTools.TypeByName("ServerJoinDataDedicated");
    private static readonly Type? PlayFabUserType = AccessTools.TypeByName("ServerJoinDataPlayFabUser");
    private static readonly Type? JoinType = AccessTools.TypeByName("ServerJoinData");
    private static readonly Type? EndPointType = AccessTools.TypeByName("NetworkingUtils.IPEndPoint");
    private static readonly FieldInfo? JoinServerField = AccessTools.Field(typeof(FejdStartup), "m_joinServer");

    private static FejdStartup? _fejd;
    private static bool _started;

    /// <summary>Why the lookup failed (empty while it has not).</summary>
    public static string Failure { get; private set; } = "";

    /// <summary>False on a game version that does not have the PlayFab types: the caller falls back to the plain join.</summary>
    public static bool Available =>
        ManagerType != null && MatchmakingType != null && DedicatedType != null && PlayFabUserType != null
        && JoinType != null && EndPointType != null && JoinServerField != null
        && AccessTools.Method(MatchmakingType, "FindHostByIp") != null;

    /// <summary>PlayFab login finishes on its own shortly after start; the lookup needs it.</summary>
    public static bool LoggedIn
    {
        get
        {
            if (ManagerType == null)
                return false;
            return (AccessTools.Property(ManagerType, "IsLoggedIn")?.GetValue(null) ?? AccessTools.Field(ManagerType, "IsLoggedIn")?.GetValue(null)) is true;
        }
    }

    /// <summary>Starts the lookup; the join itself happens when PlayFab answers. Safe to call again.</summary>
    public static void Begin(FejdStartup fejd, string address)
    {
        if (_started)
            return;
        _started = true;
        _fejd = fejd;
        Failure = "";

        try
        {
            // "host:port" -> the game's own address type; a literal IP is resolved straight away
            var dedicated = Activator.CreateInstance(DedicatedType!, address);
            var args = new object?[] { null };
            if (AccessTools.Method(DedicatedType, "TryGetIPAddress")?.Invoke(dedicated, args) is not true || args[0] == null)
            {
                Failure = $"'{address}' is not an IP address. Use the server's public IP and port (for example 50.5.111.150:2456).";
                return;
            }

            var port = AccessTools.Field(DedicatedType, "m_port").GetValue(dedicated);
            var endPoint = Activator.CreateInstance(EndPointType!, args[0], port);

            var find = AccessTools.Method(MatchmakingType, "FindHostByIp");
            var parameters = find.GetParameters();
            var found = MakeDelegate(parameters[1].ParameterType, nameof(OnFound));
            var failed = MakeDelegate(parameters[2].ParameterType, nameof(OnFailed));
            find.Invoke(null, new object?[] { endPoint, found, failed, false });
            TestLabPlugin.Log?.LogInfo($"ServerConnect: looking up {address} on PlayFab.");
        }
        catch (Exception ex)
        {
            Failure = "PlayFab lookup failed to start: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static void OnFound(object serverData)
    {
        try
        {
            var id = AccessTools.Field(serverData.GetType(), "remotePlayerId")?.GetValue(serverData) as string;
            if (string.IsNullOrEmpty(id) || _fejd == null)
            {
                Failure = "PlayFab found the server but gave no player id to join.";
                return;
            }

            var user = Activator.CreateInstance(PlayFabUserType!, id);
            JoinServerField!.SetValue(_fejd, Activator.CreateInstance(JoinType!, user));
            TestLabPlugin.Log?.LogInfo($"ServerConnect: PlayFab found the server ({id}); joining it through PlayFab.");
            _fejd.JoinServer();
        }
        catch (Exception ex)
        {
            Failure = "Joining through PlayFab failed: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static void OnFailed(object reason)
    {
        Failure = $"PlayFab could not find a server at that address ({reason}). Check the address is the server's public IP and port, and that the server is running with crossplay.";
    }

    // the game's callback types are delegates over game types, so each is built to call one of the object handlers above
    private static Delegate MakeDelegate(Type delegateType, string handler)
    {
        var parameter = Expression.Parameter(delegateType.GetMethod("Invoke")!.GetParameters()[0].ParameterType, "x");
        var call = Expression.Call(
            typeof(PlayFabJoin).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)!,
            Expression.Convert(parameter, typeof(object)));
        return Expression.Lambda(delegateType, call, parameter).Compile();
    }
}
