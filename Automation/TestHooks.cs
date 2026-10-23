using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using DrakesTestLab.Automation;
using UnityEngine;

namespace DrakesTestLab;

/// <summary>
/// Test hooks: functions other mods expose so test JSON can call them ("do": "hook" / "check": "hook").
/// Mods do NOT need to reference DrakesTestLab. Two ways in:
/// <list type="number">
/// <item>Put an attribute named <c>TestHookAttribute</c> (declare your own copy, any namespace) on a static method:
/// <c>[TestHook("renameit.spawnPaper")] static GameObject SpawnPaper(string text, string owner, Vector3 position)</c>.
/// DrakesTestLab finds it by the attribute's name.</item>
/// <item>Or call <see cref="Register"/> (directly, or by reflection for a soft dependency).</item>
/// </list>
/// Parameters are filled by name from the JSON "args": strings, numbers, bools and enums are converted; a
/// <c>GameObject</c> parameter takes a tag or prefab name; a <c>Vector3</c> takes x/z or dx/dz; a <c>Player</c> gets the
/// local player; a <c>Dictionary&lt;string, object&gt;</c> gets all args. The return value becomes "lastResult"; a returned
/// GameObject can be tagged with "as". Throw to fail the step.
/// </summary>
public static class TestHooks
{
    private sealed class Hook
    {
        public string Name = "";
        public string Source = "";
        public MethodInfo? Method;
        public Func<Dictionary<string, object?>, object?>? Func;
    }

    private static readonly Dictionary<string, Hook> Hooks = new(StringComparer.OrdinalIgnoreCase);
    private static bool _scanned;

    /// <summary>Registers a hook from code. Args arrive as parsed JSON (string, double, bool, lists, dictionaries).</summary>
    public static void Register(string name, Func<Dictionary<string, object?>, object?> hook, string source = "")
    {
        if (string.IsNullOrWhiteSpace(name) || hook == null)
            return;
        Hooks[name] = new Hook { Name = name, Func = hook, Source = source.Length > 0 ? source : hook.Method.DeclaringType?.Assembly.GetName().Name ?? "" };
    }

    public static void Unregister(string name) => Hooks.Remove(name);

    /// <summary>All known hooks as "name (mod)".</summary>
    public static List<string> Describe()
    {
        Scan();
        return Hooks.Values.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).Select(h => $"{h.Name} ({h.Source})").ToList();
    }

    /// <summary>Looks through every loaded plugin's assembly for static methods carrying an attribute named TestHookAttribute.</summary>
    internal static void Scan(bool force = false)
    {
        if (_scanned && !force)
            return;
        _scanned = true;

        var seen = new HashSet<Assembly>();
        foreach (var info in Chainloader.PluginInfos.Values)
        {
            var asm = info.Instance != null ? info.Instance.GetType().Assembly : null;
            if (asm == null || !seen.Add(asm))
                continue;

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                MethodInfo[] methods;
                try
                {
                    methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var m in methods)
                {
                    var name = HookName(m);
                    if (name == null)
                        continue;
                    if (name.Length == 0)
                        name = $"{info.Metadata.GUID}.{m.Name}";
                    if (!Hooks.ContainsKey(name))
                        Hooks[name] = new Hook { Name = name, Method = m, Source = info.Metadata.Name };
                }
            }
        }

        if (Hooks.Count > 0)
            TestLabPlugin.Log?.LogInfo($"Test hooks found: {string.Join(", ", Hooks.Keys)}");
    }

    /// <summary>null = not a hook; "" = hook without an explicit name.</summary>
    private static string? HookName(MethodInfo m)
    {
        IList<CustomAttributeData> attrs;
        try
        {
            attrs = m.GetCustomAttributesData();
        }
        catch
        {
            return null;
        }

        foreach (var a in attrs)
        {
            if (a.AttributeType.Name != "TestHookAttribute" && a.AttributeType.Name != "TestHook")
                continue;
            foreach (var arg in a.ConstructorArguments)
                if (arg.Value is string s) return s;
            foreach (var arg in a.NamedArguments)
                if (arg.TypedValue.Value is string s) return s;
            return "";
        }

        return null;
    }

    internal static bool Exists(string name)
    {
        Scan();
        return Hooks.ContainsKey(name);
    }

    internal static object? Call(string name, Dictionary<string, object?> args, RunContext ctx)
    {
        Scan();
        if (!Hooks.TryGetValue(name, out var hook))
            throw new StepFailed($"no test hook named '{name}' (known: {string.Join(", ", Hooks.Keys)})");

        try
        {
            if (hook.Func != null)
                return hook.Func(args);

            var m = hook.Method!;
            var ps = m.GetParameters();
            var values = new object?[ps.Length];
            for (var i = 0; i < ps.Length; i++)
                values[i] = Bind(ps[i], args, ctx);
            return m.Invoke(null, values);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new StepFailed($"hook '{name}' failed: {ex.InnerException.Message}");
        }
        catch (StepFailed)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new StepFailed($"hook '{name}' failed: {ex.Message}");
        }
    }

    private static object? Bind(ParameterInfo p, Dictionary<string, object?> args, RunContext ctx)
    {
        var t = p.ParameterType;
        var name = p.Name ?? "";

        if (t == typeof(Dictionary<string, object?>) || t == typeof(Dictionary<string, object>) || t == typeof(IDictionary<string, object>))
            return args;
        if (t == typeof(Player))
            return Player.m_localPlayer;
        if (t == typeof(Vector3))
            return Targets.PointFrom(args, Player.m_localPlayer != null ? Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 3f : Vector3.zero);

        args.TryGetValue(name, out var raw);
        if (t == typeof(GameObject))
        {
            var target = raw as string ?? "";
            if (target.Length == 0)
                return p.HasDefaultValue ? null : throw new StepFailed($"hook needs '{name}' (a tag or prefab name)");
            return Targets.Find(ctx, target) ?? throw new StepFailed($"'{target}' not found for hook parameter '{name}'");
        }

        if (raw == null)
        {
            if (p.HasDefaultValue)
                return p.DefaultValue;
            if (!t.IsValueType)
                return null;
            throw new StepFailed($"hook needs argument '{name}'");
        }

        if (t == typeof(string))
            return raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
        if (t.IsEnum)
            return Enum.Parse(t, Convert.ToString(raw, CultureInfo.InvariantCulture)!, true);
        if (t == typeof(bool))
            return raw is bool b ? b : Convert.ToString(raw, CultureInfo.InvariantCulture)?.ToLowerInvariant() is "true" or "1" or "yes";
        return Convert.ChangeType(raw, t, CultureInfo.InvariantCulture);
    }

    /// <summary>"Did it work?" for a hook used as a check without expect: true, or anything that is not false / null / "".</summary>
    internal static bool Truthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0 && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
        _ => true
    };
}
