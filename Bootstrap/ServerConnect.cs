using System.Collections;
using DrakesTestLab.Automation;
using DrakesTestLab.UI.Toolbox;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Server connect mode ([Bootstrap] Target = Server): instead of building a local test world, join the dedicated test
/// server, which owns the world. If the connection cannot be made the run fails with a failed assert, so an unattended
/// run exits with a failure rather than hanging at a menu.
/// </summary>
internal static class ServerConnect
{
    public static bool Active => TestLabConfig.Instance != null && TestLabConfig.Instance.Target.Value == BootTarget.Server;

    /// <summary>[Connect] Role = Player2: this client runs no tests and only does what Player 1 sends.</summary>
    public static bool IsPlayer2 =>
        TestLabConfig.Instance != null && TestLabConfig.Instance.ConnectRole.Value.Trim().Equals(RemotePeers.Player2, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>True once this client is in the server's world.</summary>
    public static bool Connected { get; private set; }

    private static bool _started;

    public static void Begin(FejdStartup fejd)
    {
        // once per game start: the menu scene is loaded again after a failed or ended connection
        if (_started || ToolboxHost.Instance == null)
            return;

        _started = true;
        ToolboxHost.Instance.StartCoroutine(Connect(fejd));
    }

    // Runs on the toolbox host: it survives the scene change from the menu into the world, the menu does not.
    private static IEnumerator Connect(FejdStartup fejd)
    {
        var cfg = TestLabConfig.Instance;
        var address = cfg.ConnectAddress.Value.Trim();
        var character = cfg.ConnectCharacter.Value.Trim();
        if (character.Length == 0)
            character = PresetDeployer.DefaultEphemeralCharacterName;

        // a clean character on every launch, same as the local lab
        ValheimPaths.DeleteCharacter(character);
        if (!CharacterFactory.CreateFresh(character, cfg.CharacterDisplayName.Value))
        {
            TestRunner.ReportConnectFailure($"Could not create the test character '{character}'.");
            yield break;
        }

        FejdStartupBridge.InvalidateSaveCaches();
        yield return null;

        var deadline = Time.realtimeSinceStartup + cfg.ConnectTimeout.Value;
        var joining = false;
        while (!joining && Time.realtimeSinceStartup < deadline)
        {
            joining = FejdStartupBridge.TryJoinServer(fejd, character, address, cfg.ConnectPassword.Value);
            if (!joining)
                yield return null;
        }

        if (!joining)
        {
            TestRunner.ReportConnectFailure($"The menu never became ready to join {address} within {cfg.ConnectTimeout.Value:0}s.");
            yield break;
        }

        while (Time.realtimeSinceStartup < deadline)
        {
            if (Player.m_localPlayer != null && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                Connected = true;
                TestLabPlugin.Log?.LogInfo($"ServerConnect: connected to {address} and spawned.");

                // ask the server for this client's role: it hands Player1 and Player2 to one client each
                var wanted = IsPlayer2 ? RemotePeers.Player2 : RemotePeers.Player1;
                RemotePeers.Hello(wanted);
                var helloDeadline = Time.realtimeSinceStartup + 10f;
                while (!RemotePeers.Answered && Time.realtimeSinceStartup < helloDeadline)
                    yield return null;

                if (!RemotePeers.Answered)
                    TestLabPlugin.Log?.LogWarning("ServerConnect: the server did not answer the role request (is DrakesTestLabServer on the server?). Steps with \"on\": \"player2\" will fail.");
                else if (RemotePeers.Role.Length == 0)
                    TestRunner.ReportConnectFailure($"The server refused the role {wanted}: {RemotePeers.RoleMessage}.");
                else
                    TestLabPlugin.Log?.LogInfo($"ServerConnect: role {RemotePeers.Role}.");
                yield break;
            }

            var status = ZNet.GetConnectionStatus();
            if (status >= ZNet.ConnectionStatus.ErrorVersion)
            {
                TestRunner.ReportConnectFailure($"The server at {address} refused or lost the connection: {status}.");
                yield break;
            }

            yield return null;
        }

        TestRunner.ReportConnectFailure(
            $"Could not connect to the server at {address} within {cfg.ConnectTimeout.Value:0}s (status: {ZNet.GetConnectionStatus()}).");
    }
}
