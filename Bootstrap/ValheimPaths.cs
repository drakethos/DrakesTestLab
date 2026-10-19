using System;
using System.IO;

namespace DrakesTestLab.Bootstrap;

internal static class ValheimPaths
{
    // LocalLow is a sibling of Local, not a child ("Local\Low" is a different, wrong folder). Unity already knows
    // the real location: persistentDataPath = %USERPROFILE%\AppData\LocalLow\IronGate\Valheim.
    public static string ValheimAppDataRoot =>
        Path.GetFullPath(UnityEngine.Application.persistentDataPath);

    public static string WorldsFolder =>
        FirstExistingDirectory(
            Path.Combine(ValheimAppDataRoot, "worlds_local"),
            Path.Combine(ValheimAppDataRoot, "worlds"))
        ?? Path.Combine(ValheimAppDataRoot, "worlds_local");

    public static string CharactersFolder =>
        FirstExistingDirectory(
            Path.Combine(ValheimAppDataRoot, "characters_local"),
            Path.Combine(ValheimAppDataRoot, "characters"))
        ?? Path.Combine(ValheimAppDataRoot, "characters_local");

    public static string GetPluginAssetsPath()
    {
        var dir = Path.GetDirectoryName(TestLabPlugin.Instance?.Info.Location);
        return string.IsNullOrEmpty(dir) ? string.Empty : Path.Combine(dir, "Assets");
    }

    public static string TemplatesFolder => Path.Combine(GetPluginAssetsPath(), "Presets", "templates");

    private static string? FirstExistingDirectory(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>True when a playable world exists under this name, in either the legacy .fwl/.db layout
    /// or the Valheim 1.0 per-world folder (<c>worlds_local/Name/_main.N.fwl2</c>).</summary>
    public static bool WorldExists(string worldName)
    {
        var root = WorldsFolder;
        if (File.Exists(Path.Combine(root, worldName + ".fwl")) && File.Exists(Path.Combine(root, worldName + ".db")))
            return true;

        var dir = Path.Combine(root, worldName);
        return Directory.Exists(dir) && Directory.GetFiles(dir, "_main.*.fwl2").Length > 0;
    }

    public static bool CharacterExists(string characterName) =>
        File.Exists(Path.Combine(CharactersFolder, characterName + ".fch"));

    public static void DeleteSaveSet(string worldName, string characterName)
    {
        DeleteWorld(worldName);
        DeleteCharacter(characterName);
    }

    /// <summary>Removes every trace of a world: legacy files, the 1.0 world folder, minimap/tex caches and backups.
    /// A leftover folder would win over a freshly copied legacy template, so a reset must remove it.</summary>
    public static void DeleteWorld(string worldName)
    {
        var root = WorldsFolder;

        foreach (var ext in new[] { ".fwl", ".db", ".fwl.old", ".db.old", ".db.bak" })
            TryDelete(Path.Combine(root, worldName + ext));

        TryDeleteDirectory(Path.Combine(root, worldName));

        if (!Directory.Exists(root))
            return;

        // Name + '_' / '.' keeps "TestLab" from matching "TestLabMaster".
        foreach (var path in SafeEnumerate(root, worldName + "_*"))
        {
            var leaf = Path.GetFileName(path);
            if (leaf.StartsWith(worldName + "_backup_", StringComparison.OrdinalIgnoreCase)
                || leaf.EndsWith("TexCache", StringComparison.OrdinalIgnoreCase))
                TryDeletePath(path);
        }

        foreach (var path in SafeEnumerate(root, worldName + ".*"))
            TryDeletePath(path);
    }

    public static void DeleteCharacter(string characterName)
    {
        var root = CharactersFolder;

        foreach (var ext in new[] { ".fch", ".fch.old", ".fch.bak" })
            TryDelete(Path.Combine(root, characterName + ext));

        if (!Directory.Exists(root))
            return;

        foreach (var path in SafeEnumerate(root, characterName + "_backup_*"))
            TryDeletePath(path);
    }

    private static string[] SafeEnumerate(string root, string pattern)
    {
        try
        {
            return Directory.GetFileSystemEntries(root, pattern);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Could not list {root} ({pattern}): {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static void TryDeletePath(string path)
    {
        if (Directory.Exists(path))
            TryDeleteDirectory(path);
        else
            TryDelete(path);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Could not delete {path}: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Could not delete folder {path}: {ex.Message}");
        }
    }
}
