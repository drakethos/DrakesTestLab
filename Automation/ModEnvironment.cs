using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace DrakesTestLab.Automation;

/// <summary>Checks that a suite's mods are loaded, and applies / restores config overrides on those mods' live configs.</summary>
internal static class ModEnvironment
{
    private sealed class Saved
    {
        public ConfigEntryBase Entry = null!;
        public string OldValue = "";
    }

    private static readonly List<Saved> Applied = new();

    /// <summary>Values changed while a test runs (setConfig steps); put back when that test ends.</summary>
    private static readonly List<Saved> TestApplied = new();

    /// <summary>True while a test runs: <see cref="Set"/> changes then last only for that test.</summary>
    public static bool TestScope;

    /// <summary>Null when every required mod is loaded at a new enough version, otherwise the reason to skip.</summary>
    public static string? MissingMods(SuiteDef suite)
    {
        foreach (var req in suite.RequireMods)
        {
            if (req.Guid.Length == 0)
                continue;
            if (!Chainloader.PluginInfos.TryGetValue(req.Guid, out var info))
                return $"needs mod {req.Guid} (not loaded)";

            if (req.MinVersion.Length > 0 && TryVersion(req.MinVersion, out var min) && info.Metadata.Version.CompareTo(min) < 0)
                return $"needs {info.Metadata.Name} {req.MinVersion}+ (loaded {info.Metadata.Version})";
        }

        return null;
    }

    private static bool TryVersion(string text, out System.Version version)
    {
        var core = text.Split('-', '+')[0];
        return System.Version.TryParse(core, out version!);
    }

    public static ConfigEntryBase? FindEntry(string guid, string section, string key)
    {
        if (!Chainloader.PluginInfos.TryGetValue(guid, out var info) || info.Instance == null)
            return null;

        var config = info.Instance.Config;
        var def = new ConfigDefinition(section, key);
        return config.ContainsKey(def) ? config[def] : null;
    }

    /// <summary>Sets a config value and remembers the old one so <see cref="RestoreAll"/> can put it back.</summary>
    public static string Set(string guid, string section, string key, string value)
    {
        var entry = FindEntry(guid, section, key);
        if (entry == null)
            return $"config [{section}] {key} not found in {guid}";

        if (!Applied.Exists(s => ReferenceEquals(s.Entry, entry)))
            Applied.Add(new Saved { Entry = entry, OldValue = entry.GetSerializedValue() });
        if (TestScope && !TestApplied.Exists(s => ReferenceEquals(s.Entry, entry)))
            TestApplied.Add(new Saved { Entry = entry, OldValue = entry.GetSerializedValue() });
        entry.SetSerializedValue(value);
        return "";
    }

    /// <summary>Back to the suite's values after a test that changed config.</summary>
    public static void RestoreTest()
    {
        Restore(TestApplied);
        TestApplied.Clear();
    }

    private static void Restore(List<Saved> list)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            try
            {
                list[i].Entry.SetSerializedValue(list[i].OldValue);
            }
            catch (Exception ex)
            {
                TestLabPlugin.Log?.LogWarning($"Restoring config {list[i].Entry.Definition} failed: {ex.Message}");
            }
        }
    }

    public static void RestoreAll()
    {
        RestoreTest();
        Restore(Applied);
        Applied.Clear();
    }
}
