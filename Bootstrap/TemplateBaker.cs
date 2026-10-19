using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Makes the shipped world template from a brand-new generated world, so it has no leftovers.
/// Request a bake, launch: the world is generated blank, saved untouched, copied into the template folder, and the game quits.
/// </summary>
internal static class TemplateBaker
{
    public const string BakeSeed = "TestWorld";

    private static string ConfigDir => Path.Combine(Paths.ConfigPath, "DrakesTestLab");
    private static string FlagPath => Path.Combine(ConfigDir, "bake.flag");

    public static bool Pending => File.Exists(FlagPath);

    public static void Request()
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(FlagPath, "Delete this file to cancel. Next launch generates a blank world and bakes it as the template.");
    }

    private static void Clear()
    {
        try
        {
            File.Delete(FlagPath);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Could not remove bake flag: {ex.Message}");
        }
    }

    /// <summary>Writes a new empty world (just the metadata) the way the game's "new world" button does.</summary>
    public static bool CreateFreshWorld(string name, string seed)
    {
        try
        {
            ValheimPaths.DeleteWorld(name);

            var world = new World(name, seed);
            SetField(world, "m_fileSource", Enum.Parse(typeof(FileHelpers.FileSource), "Local"));
            SetField(world, "m_needsDB", false);

            AccessTools.Method(typeof(SaveSystem), "SetSaveNumber", new[] { typeof(uint) })?.Invoke(null, new object[] { 0u });
            var save = AccessTools.Method(typeof(World), "SaveWorldFWLData", new[] { typeof(DateTime) })
                       ?? AccessTools.Method(typeof(World), "SaveWorldMetaData", new[] { typeof(DateTime) });
            if (save == null)
            {
                TestLabPlugin.Log?.LogError("Neither SaveWorldFWLData nor SaveWorldMetaData exists; cannot create a blank world.");
                return false;
            }

            save.Invoke(world, new object[] { DateTime.Now });
            FejdStartupBridge.InvalidateSaveCaches();
            TestLabPlugin.Log?.LogInfo($"Created blank world '{name}' (seed '{seed}').");
            return true;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogError($"Blank world creation failed: {ex}");
            return false;
        }
    }

    public static IEnumerator Bake(string worldName, string templateName)
    {
        TestLabPlugin.Log?.LogInfo("Bake: waiting for the blank world to finish loading...");

        Player? last = null;
        var stable = 0f;
        var waited = 0f;
        while (waited < 300f)
        {
            var cur = Player.m_localPlayer;
            if (cur != null && ZNetScene.instance != null && ZNet.instance != null && !cur.InIntro() && !cur.IsTeleporting())
            {
                if (ReferenceEquals(cur, last)) stable += Time.unscaledDeltaTime;
                else { last = cur; stable = 0f; }
                if (stable >= 8f)
                    break;
            }
            else
            {
                last = null;
                stable = 0f;
            }

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (ZNet.instance == null)
        {
            TestLabPlugin.Log?.LogError("Bake: world never became ready; aborting.");
            yield break;
        }

        ZNet.instance.Save(true);
        for (var i = 0; i < 600 && ZNet.instance != null && ZNet.instance.IsSaving(); i++)
            yield return null;
        yield return new WaitForSeconds(3f);

        var source = Path.Combine(ValheimPaths.WorldsFolder, worldName);
        if (!Directory.Exists(source) || Directory.GetFiles(source, "_main.*.fwl2").Length == 0)
        {
            TestLabPlugin.Log?.LogError($"Bake: no saved world folder at {source}; template NOT updated.");
            yield break;
        }

        var dest = Path.Combine(ValheimPaths.TemplatesFolder, templateName);
        var archive = Path.Combine(ConfigDir, "baked", templateName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        CopyFresh(source, dest);
        CopyFresh(source, archive);

        // lists exactly the files that belong to this template, so stale files from older bakes are never deployed
        var manifest = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.GetFiles(source))
            manifest.Add(Path.GetFileName(file));
        File.WriteAllLines(Path.Combine(dest, "_manifest.txt"), manifest.ToArray());
        File.WriteAllLines(Path.Combine(archive, "_manifest.txt"), manifest.ToArray());

        Clear();
        TestLabPlugin.Log?.LogInfo($"Bake: blank world saved as template -> {dest} (copy in {archive}). Quitting.");
        yield return new WaitForSeconds(1f);
        Application.Quit();
    }

    private static void CopyFresh(string source, string dest)
    {
        if (Directory.Exists(dest))
            Directory.Delete(dest, true);

        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
    }

    private static void SetField(object target, string name, object value)
    {
        AccessTools.Field(target.GetType(), name)?.SetValue(target, value);
    }
}
