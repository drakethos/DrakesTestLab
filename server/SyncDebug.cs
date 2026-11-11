using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DrakesTestLabServer;

/// <summary>
/// Looks inside the config sync that mods bundle (the community ServerSync library: DrakeModsLibs, LockSmith, RenameIt and others carry a
/// copy). <c>sync.info</c> reports each sync's name, whether it is locked and whether this process is the source of truth, and the trace
/// logs every time the library broadcasts a changed setting or receives one, so a test can tell "the server never sent it" from "the
/// client never applied it".
/// </summary>
internal static class SyncDebug
{
    private static bool _installed;

    /// <summary>The ConfigSync types: every loaded assembly that carries its own copy of ServerSync has one.</summary>
    private static List<Type> SyncTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => { try { return a.GetType("ServerSync.ConfigSync", false); } catch { return null; } })
            .Where(t => t != null).Select(t => t!).ToList();

    public static void Install(Harmony harmony)
    {
        if (_installed)
            return;
        _installed = true;

        var prefix = new HarmonyMethod(typeof(SyncDebug), nameof(Trace));
        var count = 0;
        foreach (var type in SyncTypes())
        {
            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.Name is "Broadcast" or "RPC_FromOtherClientConfigSync" or "RPC_FromServerConfigSync" && !m.IsAbstract && !m.ContainsGenericParameters)
                {
                    try { harmony.Patch(m, prefix: prefix); count++; }
                    catch (Exception ex) { LabServerPlugin.Log?.LogWarning($"Could not trace {type.Assembly.GetName().Name}.{m.Name}: {ex.Message}"); }
                }
            }
        }

        LabServerPlugin.Log?.LogInfo($"Config sync trace: {SyncTypes().Count} ServerSync copies, {count} methods traced.");
    }

    private static void Trace(MethodBase __originalMethod, object __instance, object[] __args)
    {
        try
        {
            var name = __instance != null ? (AccessTools.Property(__instance.GetType(), "Name")?.GetValue(__instance) as string ?? "?") : "?";
            var detail = __args == null ? "" : string.Join(", ", __args.Select(a => a is Array arr ? $"[{arr.Length} entries]" : a?.ToString() ?? "null"));
            LabServerPlugin.Log?.LogInfo($"[SyncTrace] {name}: {__originalMethod.Name}({detail})");
        }
        catch { /* never let the trace affect the game */ }
    }

    public static string Info()
    {
        var rows = new List<string>();
        foreach (var type in SyncTypes())
        {
            var list = (AccessTools.Field(type, "configSyncs")?.GetValue(null) as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
            var isServer = AccessTools.Field(type, "isServer")?.GetValue(null);
            var processing = AccessTools.Field(type, "ProcessingServerUpdate")?.GetValue(null);
            foreach (var sync in list)
            {
                string Get(string n) => (AccessTools.Property(type, n)?.GetValue(sync) ?? AccessTools.Field(type, n)?.GetValue(sync))?.ToString() ?? "?";
                rows.Add($"{Get("Name")}: locked={Get("IsLocked")} sourceOfTruth={Get("IsSourceOfTruth")} initialSyncDone={Get("InitialSyncDone")} isServer={isServer} processingServerUpdate={processing}");
            }
        }

        return rows.Count == 0 ? "no config sync found" : string.Join("; ", rows);
    }
}
