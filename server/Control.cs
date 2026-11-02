using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLabServer;

/// <summary>
/// Lets Player 1 change the server's conditions from a test: the admin list, the whitelist (permitted list), the blacklist
/// (banned list), kicking a player, a mod's setting, a mod's own server-side reset hooks, and a restart. A test step
/// <c>{ "do": "server", "cmd": "admin.add", "args": { "who": "me" } }</c> arrives here as <c>DTL_Ctl</c>; the answer goes
/// back as <c>DTL_CtlResult</c>. Off unless <c>Control.Enabled</c> is set, and only the client that holds the Player1 role may use it.
///
/// The game re-reads adminlist.txt, permittedlist.txt and bannedlist.txt from disk every 10 seconds, so editing those files by
/// hand has the same effect as these commands, live, without a restart.
/// </summary>
internal static class Control
{
    private const string Rpc = "DTL_Ctl";

    private sealed class Hook
    {
        public string Name = "";
        public MethodInfo Method = null!;
        public string Source = "";
    }

    private static Dictionary<string, Hook>? _hooks;

    public static void Register(ZRoutedRpc rpc)
    {
        rpc.Register<string, string, string>(Rpc, OnCtl);
        LabServerPlugin.Log?.LogInfo($"Server control {(LabConfig.ControlEnabled.Value ? "is ON" : "is off (Control.Enabled = false)")}: {Rpc}.");
    }

    private static void OnCtl(long sender, string id, string cmd, string argsJson)
    {
        bool ok;
        string detail;
        try
        {
            (ok, detail) = Run(sender, (cmd ?? "").Trim(), argsJson);
        }
        catch (Exception ex)
        {
            ok = false;
            detail = ex.InnerException?.Message ?? ex.Message;
        }

        if (detail.Length > 480)
            detail = detail.Substring(0, 480) + "...";
        LabServerPlugin.Log?.LogInfo($"Control {cmd}: {(ok ? "ok" : "FAILED")} {detail}");
        ZRoutedRpc.instance.InvokeRoutedRPC(sender, "DTL_CtlResult", id, ok, detail);
    }

    private static (bool, string) Run(long sender, string cmd, string argsJson)
    {
        if (!Roles.IsPlayer1(sender))
            return (false, $"only {Roles.Player1} may control the server");

        var a = Json.ParseObject(argsJson);

        // read-only questions about the server's own view (peers, ZDOs, RPC traffic) are allowed whenever Control.Queries is on
        if (Query.Handles(cmd))
            return LabConfig.QueriesEnabled.Value
                ? Query.Run(sender, cmd, a)
                : (false, "server queries are off: set Control.Queries = true in the server's com.drakemods.testlab.server.cfg and restart");

        if (!LabConfig.ControlEnabled.Value)
            return (false, "server control is off: set Control.Enabled = true in the server's com.drakemods.testlab.server.cfg and restart");
        switch (cmd.ToLowerInvariant())
        {
            case "admin.add": return (true, ListEdit("m_adminList", true, Who(sender, a)));
            case "admin.remove": return (true, ListEdit("m_adminList", false, Who(sender, a)));
            case "admin.list": return (true, string.Join(",", ListItems("m_adminList")));
            case "admin.is": return (true, ZNet.instance.IsAdmin(Who(sender, a)).ToString());

            case "permit.add": return (true, ListEdit("m_permittedList", true, Who(sender, a)));
            case "permit.remove": return (true, ListEdit("m_permittedList", false, Who(sender, a)));
            case "permit.list": return (true, string.Join(",", ListItems("m_permittedList")));
            case "permit.clear":
            {
                var items = ListItems("m_permittedList");
                foreach (var item in items)
                    ListEdit("m_permittedList", false, item);
                return (true, $"cleared {items.Count}");
            }

            case "ban.add": return (true, ListEdit("m_bannedList", true, Who(sender, a)));
            case "ban.remove": return (true, ListEdit("m_bannedList", false, Who(sender, a)));
            case "ban.list": return (true, string.Join(",", ListItems("m_bannedList")));
            case "ban.clear":
            {
                var items = ListItems("m_bannedList");
                foreach (var item in items)
                    ListEdit("m_bannedList", false, item);
                return (true, $"cleared {items.Count}");
            }

            case "kick":
            {
                var who = Str(a, "who");
                if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase))
                    return (false, "kick needs the name of another player");
                ZNet.instance.Kick(who);
                return (true, $"kicked {who}");
            }

            case "players": return (true, Players());

            case "config.get": return (true, ConfigEntryOf(a).GetSerializedValue());
            case "config.set":
            {
                var entry = ConfigEntryOf(a);
                entry.SetSerializedValue(Str(a, "value"));
                return (true, entry.GetSerializedValue());
            }

            case "hook.call": return (true, CallHook(Str(a, "name"), a.TryGetValue("args", out var h) ? h as Dictionary<string, object?> : null));
            case "hook.list": return (true, string.Join(",", Hooks().Keys.OrderBy(k => k)));

            case "server.quit":
                // the container's restart policy (or a supervisor) starts the server again with a fresh lab world
                LabServerPlugin.Instance?.Invoke("QuitNow", 1.5f);
                return (true, "quitting in 1.5 s");

            default:
                return (false, $"unknown server command '{cmd}' (admin.*, permit.*, ban.*, kick, players, config.get/set, hook.call/list, server.quit)");
        }
    }

    // ------------------------------------------------------------------ who / lists

    private static string Str(Dictionary<string, object?> a, string key) =>
        a.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";

    /// <summary>"me" (or nothing) = the player who sent the command; a player's name = that connected player; anything else is
    /// taken as an id as it appears in the lists (for example Steam_7656... or Xbox_...).</summary>
    private static string Who(long sender, Dictionary<string, object?> a)
    {
        var who = Str(a, "who");
        if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase))
            return ZNet.instance.GetPeer(sender)?.m_socket.GetHostName() ?? throw new InvalidOperationException("the sender is no longer connected");

        var peer = ZNet.instance.GetPeerByPlayerName(who);
        return peer != null ? peer.m_socket.GetHostName() : who;
    }

    private static object ListObject(string field) =>
        AccessTools.Field(typeof(ZNet), field)?.GetValue(ZNet.instance) ?? throw new InvalidOperationException($"the game has no list '{field}'");

    private static List<string> ListItems(string field) =>
        new((List<string>)ListObject(field).GetType().GetMethod("GetList")!.Invoke(ListObject(field), null)!);

    private static string ListEdit(string field, bool add, string id)
    {
        var list = ListObject(field);
        // the game writes the list file itself (and re-reads it every 10 s), so a change shows in the file as well
        list.GetType().GetMethod(add ? "Add" : "Remove", new[] { typeof(string) })!.Invoke(list, new object[] { id });
        var present = (bool)list.GetType().GetMethod("Contains", new[] { typeof(string) })!.Invoke(list, new object[] { id })!;
        return $"{(add ? "added" : "removed")} {id} (now {(present ? "in" : "not in")} {field})";
    }

    private static string Players()
    {
        var peers = AccessTools.Field(typeof(ZNet), "m_peers")?.GetValue(ZNet.instance) as IEnumerable;
        var rows = new List<string>();
        if (peers != null)
        {
            foreach (var o in peers)
            {
                if (o is not ZNetPeer p || !p.IsReady())
                    continue;
                var host = p.m_socket.GetHostName();
                rows.Add($"{p.m_playerName}[{host}]{(ZNet.instance.IsAdmin(host) ? " admin" : "")}{Roles.RoleOf(p.m_uid)}");
            }
        }

        return string.Join("; ", rows);
    }

    // ------------------------------------------------------------------ config

    private static ConfigEntryBase ConfigEntryOf(Dictionary<string, object?> a)
    {
        var guid = Str(a, "guid");
        var info = Chainloader.PluginInfos.Values.FirstOrDefault(p => p.Metadata.GUID.Equals(guid, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"no plugin with guid '{guid}' is loaded");
        var def = new ConfigDefinition(Str(a, "section"), Str(a, "key"));
        var cfg = info.Instance.Config;
        if (!cfg.ContainsKey(def))
            throw new InvalidOperationException($"{guid} has no setting [{def.Section}] {def.Key}");
        return cfg[def];
    }

    // ------------------------------------------------------------------ the mods' own server-side hooks

    private static Dictionary<string, Hook> Hooks()
    {
        if (_hooks != null)
            return _hooks;

        _hooks = new Dictionary<string, Hook>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<Assembly>();
        foreach (var info in Chainloader.PluginInfos.Values)
        {
            var asm = info.Instance?.GetType().Assembly;
            if (asm == null || !seen.Add(asm))
                continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (var type in types)
            {
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                catch { continue; }

                foreach (var m in methods)
                {
                    var name = HookName(m);
                    if (!string.IsNullOrEmpty(name) && !_hooks.ContainsKey(name!))
                        _hooks[name!] = new Hook { Name = name!, Method = m, Source = info.Metadata.Name };
                }
            }
        }

        return _hooks;
    }

    // a method carrying an attribute named TestHookAttribute (the same convention DrakesTestLab's client uses)
    private static string? HookName(MethodInfo m)
    {
        IList<CustomAttributeData> attrs;
        try { attrs = m.GetCustomAttributesData(); }
        catch { return null; }

        foreach (var a in attrs)
        {
            if (a.AttributeType.Name != "TestHookAttribute" && a.AttributeType.Name != "TestHook")
                continue;
            foreach (var arg in a.ConstructorArguments)
                if (arg.Value is string s) return s;
            foreach (var arg in a.NamedArguments)
                if (arg.TypedValue.Value is string s) return s;
        }

        return null;
    }

    private static string CallHook(string name, Dictionary<string, object?>? args)
    {
        if (!Hooks().TryGetValue(name, out var hook))
            throw new InvalidOperationException($"no test hook named '{name}' on the server (is that mod built as Debug? known: {string.Join(", ", Hooks().Keys.OrderBy(k => k))})");

        args ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var ps = hook.Method.GetParameters();
        var values = new object?[ps.Length];
        for (var i = 0; i < ps.Length; i++)
        {
            var t = ps[i].ParameterType;
            if (t == typeof(Dictionary<string, object?>) || t == typeof(Dictionary<string, object>) || t == typeof(IDictionary<string, object>))
            {
                values[i] = args;
                continue;
            }

            args.TryGetValue(ps[i].Name ?? "", out var raw);
            if (raw == null)
            {
                if (ps[i].HasDefaultValue) { values[i] = ps[i].DefaultValue; continue; }
                if (!t.IsValueType) { values[i] = null; continue; }
                throw new InvalidOperationException($"hook '{name}' needs the argument '{ps[i].Name}'");
            }

            values[i] = t == typeof(string) ? Convert.ToString(raw, CultureInfo.InvariantCulture)
                : t == typeof(bool) ? (raw is bool b ? b : Convert.ToString(raw, CultureInfo.InvariantCulture)?.ToLowerInvariant() is "true" or "1" or "yes")
                : t.IsEnum ? Enum.Parse(t, Convert.ToString(raw, CultureInfo.InvariantCulture)!, true)
                : Convert.ChangeType(raw, t, CultureInfo.InvariantCulture);
        }

        try
        {
            return Convert.ToString(hook.Method.Invoke(null, values), CultureInfo.InvariantCulture) ?? "null";
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException($"hook '{name}' failed: {ex.InnerException.Message}");
        }
    }
}
