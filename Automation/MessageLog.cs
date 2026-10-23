using System;
using System.Collections.Generic;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using HarmonyLib;

namespace DrakesTestLab.Automation;

internal sealed class ShownMessage
{
    public DateTime Time;
    public string Kind = "";     // Center, TopLeft, Unlock, Tutorial
    public string Raw = "";      // as the code sent it ($msg_... tokens)
    public string Text = "";     // localized, as the player sees it
}

/// <summary>
/// Remembers every on-screen message (center / top-left messages, unlock pop-ups, raven tutorials) so tests can check them,
/// even when the toolbox hides the unlock pop-ups and tutorials as noise.
/// </summary>
internal static class MessageLog
{
    private const int Max = 400;
    public static readonly List<ShownMessage> Messages = new();

    public static void Add(string kind, string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return;

        string text;
        try
        {
            text = Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }
        catch
        {
            text = raw;
        }

        Messages.Add(new ShownMessage { Time = DateTime.Now, Kind = kind, Raw = raw, Text = text });
        if (Messages.Count > Max)
            Messages.RemoveRange(0, Messages.Count - Max);
    }

    public static ShownMessage? Find(DateTime since, string contains, string kind = "")
    {
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var m = Messages[i];
            if (m.Time < since)
                break;
            if (kind.Length > 0 && !m.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                continue;
            if (contains.Length == 0
                || m.Text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0
                || m.Raw.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                return m;
        }

        return null;
    }
}

[HarmonyPatch]
internal static class MessageHud_ShowMessage_Patch
{
    private static MethodBase? TargetMethod()
    {
        foreach (var m in typeof(MessageHud).GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            var ps = m.GetParameters();
            if (m.Name == "ShowMessage" && ps.Length >= 2 && ps[1].ParameterType == typeof(string))
                return m;
        }

        return null;
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(object[] __args)
    {
        if (__args.Length >= 2 && __args[1] is string text)
            MessageLog.Add(Convert.ToString(__args[0]) ?? "", text);
    }
}

/// <summary>
/// Mods that show their own message cards instead of the game's centre text are recorded too (kind "Card").
/// Optional targets: patched only when that mod is loaded; no reference to it.
/// </summary>
[HarmonyPatch]
internal static class ModMessageCard_Patch
{
    private static readonly string[] Targets = { "DrakeModsLibs.UI.DrakeMessage" };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var typeName in Targets)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null)
                continue;
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var ps = m.GetParameters();
                if (m.Name == "Show" && ps.Length >= 1 && ps[0].ParameterType == typeof(string))
                    yield return m;
            }
        }
    }

    private static bool Prepare()
    {
        foreach (var _ in TargetMethods())
            return true;
        return false;
    }

    private static void Prefix(object[] __args)
    {
        if (__args.Length >= 1 && __args[0] is string text)
            MessageLog.Add("Card", text);
    }
}

/// <summary>Records unlock pop-ups ("New crafting recipe"...) and hides them when the quiet switch or bulk discovery says so.</summary>
[HarmonyPatch(typeof(MessageHud), "QueueUnlockMsg")]
internal static class MessageHud_QueueUnlockMsg_Patch
{
    private static bool Prefix(object[] __args)
    {
        if (__args.Length >= 3)
            MessageLog.Add("Unlock", $"{__args[1]} {__args[2]}");
        return !(Discovery.Quiet || TestRules.HideUnlockPopups);
    }
}

[HarmonyPatch(typeof(Player), "ShowTutorial")]
internal static class Player_ShowTutorial_Patch
{
    private static bool Prefix(object[] __args)
    {
        if (__args.Length >= 1 && __args[0] is string name)
            MessageLog.Add("Tutorial", name);
        return !TestRules.HideTutorials;
    }
}

/// <summary>With auto-discovery off, picking items up no longer teaches materials and recipes (the toolbox can still discover).</summary>
[HarmonyPatch(typeof(Player), "AddKnownItem")]
internal static class Player_AddKnownItem_Patch
{
    private static bool Prefix() => TestRules.AutoDiscovery || Discovery.Quiet;
}
