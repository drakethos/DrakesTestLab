using System;
using System.Collections.Generic;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.UI;
using DrakesTestLab.Util;
using HarmonyLib;

namespace DrakesTestLab.Commands;

/// <summary>
/// The testlab.* console commands. They are handled by a small interceptor on Terminal.TryRunCommand instead of registering
/// real console commands, so the mod does not need Jotunn (or any other library) and guests do not need the mod either.
/// </summary>
internal static class TestLabConsole
{
    private sealed class Entry
    {
        public string Help = "";
        public Action<string[]> Run = _ => { };
    }

    private static readonly Dictionary<string, Entry> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["testlab.help"] = new Entry { Help = "List the testlab commands.", Run = _ => PrintHelp() },
        ["testlab.reloadcfg"] = new Entry { Help = "Reload all loaded mod configs from disk (does not re-grant starting items).", Run = _ => ConfigHotReload.ReloadNow() },
        ["testlab.menu"] = new Entry { Help = "Toggle the test toolbox (same as F9).", Run = _ => TestControlPanel.Toggle() },
        ["testlab.panel"] = new Entry { Help = "Toggle the test toolbox (same as F9).", Run = _ => TestControlPanel.Toggle() },
        ["testlab.close"] = new Entry { Help = "Close the test toolbox.", Run = _ => TestControlPanel.Hide() },
        ["testlab.reset"] = new Entry { Help = "Reset: back to the menu and straight into a fresh world and character.", Run = _ => HardRestart.Execute() },
        ["testlab.savemode"] = new Entry { Help = "Print the active save mode.", Run = _ => PrintSaveMode() },
        ["testlab.clearpersistent"] = new Entry { Help = "Delete persistent test saves.", Run = _ => ClearPersistent() },
        ["testlab.clearephemeral"] = new Entry { Help = "Delete ephemeral test saves.", Run = _ => ClearEphemeral() },
        ["testlab.dev"] = new Entry { Help = "Toggle devcommands auto-enable.", Run = _ => TestControlsApplier.ToggleDevCommands() },
        ["testlab.hostile"] = new Entry { Help = "Toggle creature hostility.", Run = _ => TestControlsApplier.ToggleHostile() }
    };

    /// <summary>Handles "testlab.xxx" lines. Returns true when the line was ours.</summary>
    public static bool TryHandle(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Commands.TryGetValue(parts[0], out var entry))
            return false;

        try
        {
            entry.Run(parts);
        }
        catch (Exception ex)
        {
            GameRefs.LogToConsole($"DrakesTestLab: {parts[0]} failed: {ex.Message}");
        }

        return true;
    }

    private static void PrintHelp()
    {
        foreach (var kv in Commands)
            GameRefs.LogToConsole($"{kv.Key} - {kv.Value.Help}");
    }

    private static void PrintSaveMode()
    {
        var t = SaveModeResolver.Resolve();
        GameRefs.LogToConsole(
            $"DrakesTestLab Mode={t.Mode} world={t.WorldName} char={t.CharacterName} " +
            $"templates={t.TemplateWorldFile}/{t.TemplateCharacterFile}");
    }

    private static void ClearPersistent()
    {
        var target = SaveModeResolver.Resolve();
        if (target.Mode != SaveModeKind.Persistent)
        {
            GameRefs.LogToConsole("DrakesTestLab: not in Persistent mode - nothing cleared.");
            return;
        }

        ValheimPaths.DeleteSaveSet(target.WorldName, target.CharacterName);
        GameRefs.LogToConsole($"DrakesTestLab: persistent saves cleared ({target.WorldName} / {target.CharacterName}).");
    }

    private static void ClearEphemeral()
    {
        var target = SaveModeResolver.Resolve();
        ValheimPaths.DeleteSaveSet(target.WorldName, target.CharacterName);
        GameRefs.LogToConsole($"DrakesTestLab: ephemeral saves cleared ({target.WorldName} / {target.CharacterName}).");
    }
}

[HarmonyPatch(typeof(Terminal), "TryRunCommand")]
internal static class Terminal_TryRunCommand_Patch
{
    private static bool Prefix(string text) => !TestLabConsole.TryHandle(text);
}
