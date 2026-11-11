using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DrakesTestLabServer;

/// <summary>Puts the shipped lab world in the server's save folder before the server loads it.</summary>
internal static class LabWorld
{
    private const string DefaultWorldName = "Dedicated";

    public static void Prepare(string pluginLocation)
    {
        try
        {
            Run(pluginLocation);
        }
        catch (Exception ex)
        {
            // a failure must never stop the server: it just starts with whatever world it already has
            LabServerPlugin.Log?.LogError($"Preparing the lab world failed, the server keeps its own world: {ex}");
        }
    }

    private static void Run(string pluginLocation)
    {
        var mode = LabConfig.Mode.Value;
        if (mode == WorldMode.Off)
        {
            LabServerPlugin.Log?.LogInfo("World mode is Off: the server's world is left alone.");
            return;
        }

        var worldName = LabConfig.WorldName.Value.Trim();
        if (worldName.Length == 0)
            worldName = CommandLineValue("-world") ?? DefaultWorldName;

        var saveRoot = CommandLineValue("-savedir") ?? Application.persistentDataPath;
        var worldsRoot = Path.Combine(saveRoot, "worlds_local");
        var template = Path.Combine(Path.GetDirectoryName(pluginLocation) ?? "", "Assets", "LabWorld");
        if (Directory.GetFiles(template, "_main.*.fwl2").Length == 0)
        {
            LabServerPlugin.Log?.LogError($"The lab world is missing from {template}: reinstall the mod. The server keeps its own world.");
            return;
        }

        if (mode == WorldMode.CreateIfMissing && WorldExists(worldsRoot, worldName))
        {
            LabServerPlugin.Log?.LogInfo($"World '{worldName}' already exists and the mode is CreateIfMissing: left alone.");
            return;
        }

        Directory.CreateDirectory(worldsRoot);
        var backupRoot = Path.Combine(saveRoot, "testlab_world_backups");
        BackUpAndRemove(worldsRoot, backupRoot, worldName);
        CopyWorld(template, Path.Combine(worldsRoot, worldName));
        Prune(backupRoot, LabConfig.KeepBackups.Value);

        LabServerPlugin.Log?.LogInfo($"Fresh lab world '{worldName}' is in place ({worldsRoot}); the server will load it now.");
    }

    private static bool WorldExists(string worldsRoot, string name)
    {
        if (File.Exists(Path.Combine(worldsRoot, name + ".fwl")) && File.Exists(Path.Combine(worldsRoot, name + ".db")))
            return true;
        var dir = Path.Combine(worldsRoot, name);
        return Directory.Exists(dir) && Directory.GetFiles(dir, "_main.*.fwl2").Length > 0;
    }

    /// <summary>Moves the current world (folder layout and legacy .fwl/.db) into the backup folder.</summary>
    private static void BackUpAndRemove(string worldsRoot, string backupRoot, string name)
    {
        var keep = LabConfig.KeepBackups.Value > 0;
        var dest = Path.Combine(backupRoot, $"{name}_{DateTime.Now:yyyyMMdd-HHmmss}");

        var dir = Path.Combine(worldsRoot, name);
        if (Directory.Exists(dir))
        {
            if (keep)
            {
                Directory.CreateDirectory(backupRoot);
                Directory.Move(dir, dest);
            }
            else
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        foreach (var ext in new[] { ".fwl", ".db", ".fwl.old", ".db.old", ".db.bak" })
        {
            var file = Path.Combine(worldsRoot, name + ext);
            if (!File.Exists(file))
                continue;
            if (keep)
            {
                Directory.CreateDirectory(dest);
                File.Move(file, Path.Combine(dest, Path.GetFileName(file)));
            }
            else
            {
                File.Delete(file);
            }
        }
    }

    private static void CopyWorld(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        var manifest = Path.Combine(source, "_manifest.txt");
        var only = File.Exists(manifest) ? new HashSet<string>(File.ReadAllLines(manifest)) : null;
        foreach (var file in Directory.GetFiles(source))
        {
            var leaf = Path.GetFileName(file);
            if (leaf == "_manifest.txt" || (only != null && !only.Contains(leaf)))
                continue;
            File.Copy(file, Path.Combine(dest, leaf), overwrite: true);
        }
    }

    private static void Prune(string backupRoot, int keep)
    {
        if (!Directory.Exists(backupRoot))
            return;
        var old = new DirectoryInfo(backupRoot).GetDirectories().OrderByDescending(d => d.CreationTimeUtc).Skip(Math.Max(keep, 0));
        foreach (var d in old)
        {
            try { d.Delete(recursive: true); }
            catch (Exception ex) { LabServerPlugin.Log?.LogWarning($"Could not remove old backup {d.FullName}: {ex.Message}"); }
        }
    }

    private static string? CommandLineValue(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1].Trim('"');
        }

        return null;
    }
}
