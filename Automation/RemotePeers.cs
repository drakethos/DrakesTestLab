using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        rpc.Register<string, bool, string>("DTL_CtlResult", OnCtlResult);
        CtlResults.Clear();
    }

    private static readonly Dictionary<string, (bool Ok, string Detail)> CtlResults = new();

    private static void OnCtlResult(long sender, string id, bool ok, string detail)
    {
        if (FromServer(sender))
            CtlResults[id] = (ok, detail ?? "");
    }

    /// <summary>
    /// The <c>serverConfig</c> step: change a mod's setting on the server and prove it reached the clients. It sets the value on the
    /// server, then waits until Player 1's own copy of the setting has it (and Player 2's, unless <c>"player2": false</c>). The step's
    /// result says how long each client took, so a slow or missing sync shows up as a number or a failure.
    /// <code>{ "do": "serverConfig", "guid": "com.drakemods.locksmith", "section": "02 Features", "key": "EnableChests", "value": "false" }</code>
    /// </summary>
    public static IEnumerator RunServerConfig(Dictionary<string, object?> step, RunContext ctx)
    {
        var guid = step.Str("guid");
        var section = step.Str("section");
        var key = step.Str("key");
        var value = step.Str("value");
        if (guid.Length == 0 || section.Length == 0 || key.Length == 0 || !step.Has("value"))
            throw new StepFailed("serverConfig needs guid, section, key and value");

        var timeout = Mathf.Max(1f, step.Num("timeout", 20f));
        // "force" (default on): make the server's sync send the value even when the server already had it, so a client holding a local edit is reset
        var args = new Dictionary<string, object?> { ["guid"] = guid, ["section"] = section, ["key"] = key, ["value"] = value, ["force"] = step.Bool("force", true) };
        var set = RunServer(new Dictionary<string, object?> { ["cmd"] = "config.set", ["args"] = args, ["timeout"] = timeout }, ctx);
        while (set.MoveNext())
            yield return set.Current;

        var serverValue = Convert.ToString(ctx.LastResult, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var started = Time.realtimeSinceStartup;
        var deadline = started + timeout;

        // Player 1: its own copy of the setting
        float? p1 = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            var entry = ModEnvironment.FindEntry(guid, section, key);
            if (entry != null && string.Equals(ModEnvironment.EffectiveValue(entry), serverValue, StringComparison.OrdinalIgnoreCase))
            {
                p1 = Time.realtimeSinceStartup - started;
                break;
            }

            yield return null;
        }

        if (p1 == null)
        {
            var found = ModEnvironment.FindEntry(guid, section, key);
            var have = found != null ? ModEnvironment.EffectiveValue(found) : "(no such setting on this client)";
            throw new StepFailed($"Player 1 still has '{have}' for [{section}] {key} {timeout:0} s after the server set '{serverValue}': the setting did not sync");
        }

        // Player 2: ask it to read its own copy until it matches
        float? p2 = null;
        if (step.Bool("player2", true))
        {
            var remote = new Dictionary<string, object?>
            {
                ["on"] = "player2", ["do"] = "check", ["check"] = "config", ["guid"] = guid, ["section"] = section, ["key"] = key,
                ["equals"] = serverValue, ["timeout"] = 10f, ["connectTimeout"] = 30f
            };
            string? failure = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                var routine = RunRemote(remote, ctx);
                failure = null;
                while (true)
                {
                    bool more;
                    try { more = routine.MoveNext(); }
                    catch (StepFailed ex) { failure = ex.Message; break; }
                    if (!more)
                        break;
                    yield return routine.Current;
                }

                if (failure == null)
                {
                    p2 = Time.realtimeSinceStartup - started;
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (p2 == null)
                throw new StepFailed($"Player 2 did not get '{serverValue}' for [{section}] {key} within {timeout:0} s ({failure})");
        }

        ctx.LastResult = $"{serverValue}; player1 {p1.Value * 1000f:0} ms" + (p2 != null ? $"; player2 {p2.Value * 1000f:0} ms" : "");
        ctx.Log($"  serverConfig [{section}] {key} = {serverValue}: {ctx.LastResult}");
    }

    // Runs a coroutine to its end and reports the failure message (null = it passed). Lets a caller yield around it instead of inside a try.
    private static IEnumerator Capture(IEnumerator? routine, Action<string?> done)
    {
        if (routine == null)
        {
            done(null);
            yield break;
        }

        string? failure = null;
        while (true)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (StepFailed ex) { failure = ex.Message; break; }
            catch (Exception ex) { failure = ex.InnerException?.Message ?? ex.Message; break; }
            if (!more)
                break;
            yield return routine.Current;
        }

        done(failure);
    }

    /// <summary>
    /// The <c>agree</c> step, a three-way assert: read the same thing from Player 1, the server and Player 2 and require the answers to
    /// match, or to match <c>expect</c> (also <c>contains</c>, <c>min</c>, <c>max</c>). It polls until they do, so a sync that takes a
    /// moment still passes, and fails with all three answers when they never agree.
    /// <code>
    /// { "do": "agree", "timeout": 15, "expect": "blue", "reads": [
    ///     { "party": "player1", "hook": "teams.teamOf", "args": { "player": "Bjornita" } },
    ///     { "party": "server",  "cmd": "zdo.value", "args": { "prefab": "Player", "near": "me", "key": "team" } },
    ///     { "party": "player2", "hook": "teams.myTeam" } ] }
    /// </code>
    /// A read is a <c>hook</c> (with <c>args</c>), a <c>config</c> ({ guid, section, key }: that client's own copy of a setting) or, for the
    /// server, a <c>cmd</c> (with <c>args</c>). Leave a party out to compare only the others.
    /// </summary>
    public static IEnumerator RunAgree(Dictionary<string, object?> step, RunContext ctx)
    {
        var reads = new List<(string Party, Dictionary<string, object?> Read)>();
        foreach (var item in step.Arr("reads"))
        {
            if (item is not Dictionary<string, object?> r)
                continue;
            var party = r.Str("party").Trim().ToLowerInvariant();
            if (party != "player1" && party != "player2" && party != "server")
                throw new StepFailed($"agree: a read's party is player1, server or player2 (got '{party}')");
            reads.Add((party, r));
        }

        if (reads.Count < 2)
            throw new StepFailed("agree needs at least two reads (for example player1, server and player2)");

        var timeout = Mathf.Max(1f, step.Num("timeout", 15f));
        var started = Time.realtimeSinceStartup;
        var deadline = started + timeout;
        var values = new string[reads.Count];
        var problems = new string?[reads.Count];

        do
        {
            for (var i = 0; i < reads.Count; i++)
            {
                var (party, read) = reads[i];
                string? failure = null;
                ctx.LastResult = null;

                IEnumerator? routine;
                try
                {
                    routine = StartRead(party, read, step, ctx);
                }
                catch (StepFailed ex)
                {
                    failure = ex.Message;
                    routine = null;
                }

                var capture = Capture(routine, f => failure = f ?? failure);
                while (capture.MoveNext())
                    yield return capture.Current;

                problems[i] = failure;
                values[i] = failure == null ? (Convert.ToString(ctx.LastResult, System.Globalization.CultureInfo.InvariantCulture) ?? "null").Trim() : "(" + failure + ")";
            }

            if (problems.All(p => p == null) && Agree(values, step))
            {
                ctx.LastResult = string.Join("; ", reads.Select((r, i) => $"{r.Party}={values[i]}")) + $" (agreed after {(Time.realtimeSinceStartup - started) * 1000f:0} ms)";
                ctx.Log("  agree: " + ctx.LastResult);
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }
        while (Time.realtimeSinceStartup < deadline);

        var want = step.Has("expect") ? $" (expected '{step.Str("expect")}')" : step.Has("contains") ? $" (expected to contain '{step.Str("contains")}')" : "";
        throw new StepFailed($"{(step.Has("label") ? step.Str("label") + ": " : "")}not in agreement after {timeout:0} s{want}: " +
                             string.Join("; ", reads.Select((r, i) => $"{r.Party}={values[i]}")));
    }

    // does one party's read: Player 1 reads here, Player 2 is asked, the server is asked
    private static IEnumerator? StartRead(string party, Dictionary<string, object?> read, Dictionary<string, object?> step, RunContext ctx)
    {
        var timeout = Mathf.Max(1f, step.Num("readTimeout", 10f));
        if (read.Has("cmd"))
        {
            if (party != "server")
                throw new StepFailed("agree: a \"cmd\" read is for the server");
            return RunServer(new Dictionary<string, object?> { ["cmd"] = read.Str("cmd"), ["args"] = read.Obj("args") ?? new Dictionary<string, object?>(), ["timeout"] = timeout }, ctx);
        }

        Dictionary<string, object?> sub;
        if (read.Has("hook"))
            sub = new Dictionary<string, object?> { ["do"] = "hook", ["name"] = read.Str("hook"), ["args"] = read.Obj("args") ?? new Dictionary<string, object?>() };
        else if (read.Obj("config") != null)
            sub = new Dictionary<string, object?> { ["do"] = "read", ["config"] = read.Obj("config") };
        else if (read.Obj("count") != null)
            sub = new Dictionary<string, object?> { ["do"] = "read", ["count"] = read.Obj("count") };
        else if (read.Obj("owner") != null)
            sub = new Dictionary<string, object?> { ["do"] = "read", ["owner"] = read.Obj("owner") };
        else
            throw new StepFailed($"agree: a read for {party} needs a \"hook\", \"config\", \"count\" or \"owner\"" + (party == "server" ? " (or a \"cmd\")" : ""));

        if (party == "server")
            throw new StepFailed("agree: the server is read with a \"cmd\" (for example config.get or zdo.value)");
        if (party == "player2")
        {
            sub["on"] = "player2";
            sub["timeout"] = timeout;
            sub["connectTimeout"] = 5f;
            return RunRemote(sub, ctx);
        }

        return Steps.Execute(sub, ctx);
    }

    private static bool Agree(string[] values, Dictionary<string, object?> step)
    {
        var hasMatcher = step.Has("expect") || step.Has("contains") || step.Has("notContains") || step.Has("min") || step.Has("max");
        if (hasMatcher && !values.All(v => Asserts.Matches(v, step)))
            return false;

        // no expected value, or "equal": true: every answer must also equal the first (numbers by value)
        if (hasMatcher && !step.Bool("equal"))
            return true;
        var wanted = new Dictionary<string, object?> { ["equals"] = values[0] };
        return values.Skip(1).All(v => Asserts.Matches(v, wanted));
    }

    /// <summary>The <c>server</c> step: ask the server to change a condition (admin / whitelist / blacklist, kick, a mod's setting,
    /// a mod's own server-side hook, a restart). Needs Control.Enabled on the server and this client as Player 1. The answer is
    /// the step's result; "expect" / "contains" / "notContains" on the step fail it when the answer does not match.</summary>
    public static IEnumerator RunServer(Dictionary<string, object?> step, RunContext ctx)
    {
        if (!ServerConnect.Active || ZRoutedRpc.instance == null || ZNet.instance == null || ZNet.instance.IsServer())
            throw new StepFailed("a server step needs server connect mode ([Bootstrap] Target = Server) as Player 1.");

        var roleWait = Time.realtimeSinceStartup + 10f;
        while (!Answered && Time.realtimeSinceStartup < roleWait)
            yield return null;
        if (Role != Player1)
            throw new StepFailed(Answered
                ? $"this client is {(Role.Length > 0 ? Role : "not assigned a role")}; only {Player1} controls the server."
                : "the server never answered the role request: is DrakesTestLabServer installed on the server?");

        var cmd = step.Str("cmd").Trim();
        if (cmd.Length == 0)
            throw new StepFailed("a server step needs \"cmd\" (for example admin.add, permit.list, config.set, hook.call, kick, players)");

        var id = Guid.NewGuid().ToString("N").Substring(0, 8);
        var json = JsonWriter.Write(step.Obj("args") ?? new Dictionary<string, object?>());
        ZRoutedRpc.instance.InvokeRoutedRPC("DTL_Ctl", id, cmd, json);

        var timeout = Mathf.Max(1f, step.Num("timeout", 20f));
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!CtlResults.ContainsKey(id) && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!CtlResults.TryGetValue(id, out var result))
            throw new StepFailed($"the server did not answer '{cmd}' within {timeout:0} s.");
        CtlResults.Remove(id);

        ctx.LastResult = result.Detail;
        ctx.Log($"  server {cmd}: {(result.Ok ? "ok" : "FAILED")} {result.Detail}");
        if (!result.Ok)
            throw new StepFailed($"server {cmd}: {result.Detail}");

        // expect / contains / notContains are hard here: a server step that is not what the test says stops the test
        if ((step.Has("expect") || step.Has("contains") || step.Has("notContains")) && !Asserts.Matches(result.Detail, step))
            throw new StepFailed($"server {cmd} returned '{result.Detail}'" + (step.Has("label") ? " (" + step.Str("label") + ")" : ""));
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

    /// <summary>If <paramref name="holder"/>[key] is one of Player 1's tags, replace it by the object's prefab name and
    /// put the spot Player 1 has it at ("around") on the step, so Player 2 finds the same object.</summary>
    private static void RewriteTag(Dictionary<string, object?> holder, string key, RunContext ctx, Dictionary<string, object?> step)
    {
        if (holder.TryGetValue(key, out var value) && value is string tag
            && ctx.Tags.TryGetValue(tag, out var tagged) && tagged != null)
        {
            var at = tagged.transform.position;
            holder[key] = Utils.GetPrefabName(tagged);
            step["around"] = new List<object?> { at.x, at.y, at.z };
        }
    }

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

        // Player 2 has none of my tags: name the object by prefab and say where I have it ("around"), so its nearest
        // object of that prefab to that spot is the same one
        foreach (var key in new[] { "target", "to" })
            RewriteTag(remote, key, ctx, remote);

        // a hook step carries its object in "args" ({"do": "hook", "name": "...", "args": {"target": "chest"}}): same rewrite there,
        // on a copy so Player 1's own step keeps its tag
        if (remote.TryGetValue("args", out var rawArgs) && rawArgs is Dictionary<string, object?> hookArgs)
        {
            var argsCopy = new Dictionary<string, object?>(hookArgs, StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "target", "to" })
                RewriteTag(argsCopy, key, ctx, remote);
            remote["args"] = argsCopy;
        }

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

        if (step.TryGetValue("around", out var around) && around is List<object?> xyz && xyz.Count == 3)
            ctx.AroundHint = new Vector3(Convert.ToSingle(xyz[0], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(xyz[1], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(xyz[2], System.Globalization.CultureInfo.InvariantCulture));

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

        // A check, or a hook with "expect", records a soft assert in this context. Here nothing else ever reads it, so a
        // failed one would be reported back as "ok": it fails the command instead and Player 1's step shows the reason.
        if (ok)
        {
            var failed = ctx.Result.Asserts.Find(a => !a.Passed);
            if (failed != null)
            {
                ok = false;
                detail = failed.Description + " -> " + failed.Detail;
            }
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
