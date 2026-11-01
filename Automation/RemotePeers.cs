using System;
using System.Collections;
using System.Collections.Generic;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Cases;
using DrakesTestLab.UI.Toolbox;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// The client half of Player 1 / Player 2 on a dedicated server (the server half is DrakesTestLabServer's Roles).
/// Player 1 runs the test cases. A step with <c>"on": "player2"</c> is not run here: it is sent through the server to the
/// real second client, which runs it with the same step engine and sends the answer back. Player 2 runs no tests of its
/// own; it only does what it is told. Both ride on the game's routed RPC, and the server decides who is who.
/// </summary>
internal static class RemotePeers
{
    public const string Player1 = "Player1";
    public const string Player2 = "Player2";

    /// <summary>The server's answer when no Player 2 is connected (the same text the server mod sends).</summary>
    private const string NoPlayer2 = "no Player 2 connected";

    /// <summary>The role the server gave this client ("" until it answers, or when it refused).</summary>
    public static string Role { get; private set; } = "";

    /// <summary>Why the server refused the role, when it did.</summary>
    public static string RoleMessage { get; private set; } = "";

    /// <summary>True once the server has answered the hello (granted or refused).</summary>
    public static bool Answered { get; private set; }

    private static readonly Dictionary<string, (bool Ok, string Detail)> Results = new();

    public static bool IsRemote(Dictionary<string, object?> step) =>
        step.Str("on").Equals("player2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Called when a client joins a server: listen for the server's role answer, commands and results.</summary>
    public static void Register()
    {
        var rpc = ZRoutedRpc.instance;
        if (rpc == null)
            return;

        Role = "";
        RoleMessage = "";
        Answered = false;
        Results.Clear();
        rpc.Register<string, string>("DTL_Role", OnRole);
        rpc.Register<string, string>("DTL_Run", OnRun);
        rpc.Register<string, bool, string>("DTL_Result", OnResult);
    }

    /// <summary>Ask the server for a role. The answer arrives as <see cref="Answered"/> / <see cref="Role"/>.</summary>
    public static void Hello(string role)
    {
        Role = "";
        RoleMessage = "";
        Answered = false;
        ZRoutedRpc.instance?.InvokeRoutedRPC("DTL_Hello", role);
    }

    // Commands and answers are only trusted from the server: a client cannot address another one past it.
    private static bool FromServer(long sender) => ZNet.instance?.GetServerPeer()?.m_uid == sender;

    private static void OnRole(long sender, string role, string message)
    {
        if (!FromServer(sender))
            return;

        Role = role ?? "";
        RoleMessage = message ?? "";
        Answered = true;
        TestLabPlugin.Log?.LogInfo(Role.Length > 0 ? $"Server role: you are {Role}." : $"Server refused the role: {RoleMessage}");
    }

    private static void OnResult(long sender, string id, bool ok, string detail)
    {
        if (FromServer(sender))
            Results[id] = (ok, detail ?? "");
    }

    // ------------------------------------------------------------------ Player 1: send a step to Player 2

    /// <summary>Runs a step on Player 2: sends it through the server and waits for the answer.
    /// Fails the step when there is no answer or Player 2 reports a failure.</summary>
    public static IEnumerator RunRemote(Dictionary<string, object?> step, RunContext ctx)
    {
        if (!ServerConnect.Active || ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.IsServer())
            throw new StepFailed("a step with \"on\": \"player2\" needs server connect mode ([Bootstrap] Target = Server) as Player 1.");

        // the hello is sent as the client spawns; the answer is a round trip away
        var roleWait = Time.realtimeSinceStartup + 10f;
        while (!Answered && Time.realtimeSinceStartup < roleWait)
            yield return null;
        if (Role != Player1)
            throw new StepFailed(Answered
                ? $"this client is {(Role.Length > 0 ? Role : "not assigned a role")} ({RoleMessage}); only {Player1} sends commands to {Player2}."
                : $"the server never answered the role request: is DrakesTestLabServer installed on the server?");

        var remote = new Dictionary<string, object?>(step);
        remote.Remove("on");
        var json = JsonWriter.Write(remote);
        var stepTimeout = Mathf.Max(1f, step.Num("timeout", 30f));
        var connectDeadline = Time.realtimeSinceStartup + step.Num("connectTimeout", 30f);

        while (true)
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            ZRoutedRpc.instance.InvokeRoutedRPC("DTL_Cmd", Player2, id, json);

            var deadline = Time.realtimeSinceStartup + stepTimeout + 5f;
            while (!Results.ContainsKey(id) && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!Results.TryGetValue(id, out var result))
                throw new StepFailed($"Player 2 did not answer within {stepTimeout:0} s.");
            Results.Remove(id);

            if (result.Ok)
            {
                ctx.LastResult = result.Detail;
                ctx.Log("player 2: " + (result.Detail.Length > 0 ? result.Detail : "ok"));
                yield break;
            }

            // Player 2 may still be starting: wait for it, up to connectTimeout
            if (result.Detail == NoPlayer2 && Time.realtimeSinceStartup < connectDeadline)
            {
                yield return new WaitForSecondsRealtime(1f);
                continue;
            }

            throw new StepFailed("Player 2: " + result.Detail);
        }
    }

    // ------------------------------------------------------------------ Player 2: run what the server sends

    private static void OnRun(long sender, string id, string stepJson)
    {
        if (!FromServer(sender))
            return;

        if (Role != Player2)
        {
            Reply(id, false, $"this client is {(Role.Length > 0 ? Role : "not assigned a role")}, not {Player2}");
            return;
        }

        if (ToolboxHost.Instance == null)
        {
            Reply(id, false, "the lab is not ready");
            return;
        }

        ToolboxHost.Instance.StartCoroutine(Execute(id, stepJson));
    }

    private static IEnumerator Execute(string id, string stepJson)
    {
        if (MiniJson.Parse(stepJson) is not Dictionary<string, object?> step)
        {
            Reply(id, false, "the step was not valid JSON");
            yield break;
        }

        var ok = true;
        var detail = "";
        IEnumerator? routine = null;
        var ctx = new RunContext
        {
            Suite = new SuiteDef { Name = "Player2" },
            Test = new TestDef { Name = "remote" },
            Result = new TestResult { Suite = "Player2", Test = "remote" },
            StartedAt = DateTime.Now,
            Deadline = Time.realtimeSinceStartup + Mathf.Max(1f, step.Num("timeout", 30f)) + 60f,
            Unattended = true
        };

        try
        {
            if (step.First("do", "action", "step").Equals("ping", StringComparison.OrdinalIgnoreCase))
                detail = "pong from " + (Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "(not spawned)");
            else
                routine = Steps.Execute(step, ctx);
        }
        catch (StepFailed ex)
        {
            ok = false;
            detail = ex.Message;
        }
        catch (Exception ex)
        {
            ok = false;
            detail = "crashed: " + (ex.InnerException?.Message ?? ex.Message);
        }

        // run the step's coroutine by hand so its errors can be caught and sent back
        while (ok && routine != null)
        {
            bool more;
            try
            {
                more = routine.MoveNext();
            }
            catch (StepFailed ex)
            {
                ok = false;
                detail = ex.Message;
                break;
            }
            catch (Exception ex)
            {
                ok = false;
                detail = "crashed: " + (ex.InnerException?.Message ?? ex.Message);
                break;
            }

            if (!more)
                break;
            yield return routine.Current;
        }

        if (ok && detail.Length == 0 && ctx.LastResult != null)
            detail = ctx.LastResult.ToString() ?? "";
        Reply(id, ok, detail);
    }

    private static void Reply(string id, bool ok, string detail)
    {
        if (detail.Length > 500)
            detail = detail.Substring(0, 500) + "...";
        ZRoutedRpc.instance?.InvokeRoutedRPC("DTL_ResultUp", id, ok, detail);
    }
}
