using System;
using System.IO;
using System.Text;

namespace DrakesTestLab.Bootstrap;

internal static class PresetDeployer
{
    internal const string DefaultEphemeralWorldName = "TestLab";
    internal const string DefaultEphemeralCharacterName = "TestBjorn";
    internal const string DefaultTemplateWorldFile = "TestLab";
    internal const string DefaultTemplateCharacterFile = "MasterBjorn";
    internal const string TemplateWorldInternalMaster = "TestLabMaster";
    internal const string TemplateCharacterInternalMaster = "BjornMaster";

    public static void DeployForCurrentMode() => DeployBeforeLoad(SaveModeResolver.Resolve());

    /// <summary>Purge stale saves, copy templates to disk — must finish before fast boot loads the world.</summary>
    public static void DeployBeforeLoad(ActiveSaveTarget target)
    {
        try
        {
            Deploy(target);
        }
        catch (Exception ex)
        {
            // never break the main menu: report it and let the boot loop retry, or give up with the menu back
            TestLabPlugin.Log?.LogError($"Copying the test world and character failed: {ex}");
        }
    }

    private static void Deploy(ActiveSaveTarget target)
    {
        var templates = ValheimPaths.TemplatesFolder;
        if (!Directory.Exists(templates) || !HasWorldFolder(Path.Combine(templates, target.TemplateWorldFile)))
        {
            // some installs nest the package one folder deeper: look for the shipped world anywhere in the mod's folder
            var found = FindTemplatesFolder(target.TemplateWorldFile);
            if (found != null && !string.Equals(found, templates, StringComparison.OrdinalIgnoreCase))
            {
                TestLabPlugin.Log?.LogWarning($"Templates were not in {templates}; using {found}.");
                templates = found;
            }
        }

        if (!Directory.Exists(templates))
        {
            TestLabPlugin.Log?.LogError($"The shipped test world is missing: no templates folder at {templates}. Reinstall DrakesTestLab.");
            return;
        }

        // "Keep" toggles let you carry the world and/or the character over a restart instead of resetting them.
        var cfgKeep = TestLabConfig.Instance;
        // a relog (persistence test) keeps both: the logout just saved them
        var relog = SessionRelog.Active;
        var resetWorld = target.CopyTemplatesEveryLaunch && !cfgKeep.KeepWorld.Value && !relog;
        var resetCharacter = target.CopyTemplatesEveryLaunch && !cfgKeep.KeepCharacter.Value && !relog;
        if (resetWorld)
            ValheimPaths.DeleteWorld(target.WorldName);
        if (resetCharacter)
            ValheimPaths.DeleteCharacter(target.CharacterName);

        // Valheim 1.0 migrates a played world into worlds_local/<Name>/ and retires the .fwl/.db pair, so
        // "dest file exists" is not a valid check for reuse/persistent modes - ask the world as a whole.
        var worldFolderTemplate = Path.Combine(templates, target.TemplateWorldFile);
        if (TemplateBaker.Pending)
        {
            TestLabPlugin.Log?.LogWarning("Baking a new template world (bake.flag is set): the game generates a brand-new world, which takes several minutes.");
            BootNotice.Show("DrakesTestLab is baking a new template world (requested with bake.flag).\nThe game generates it from scratch, which takes several minutes, then quits.", sticky: true);
            TemplateBaker.CreateFreshWorld(target.WorldName, TemplateBaker.BakeSeed);
        }
        else if ((resetWorld || !ValheimPaths.WorldExists(target.WorldName)) && HasWorldFolder(worldFolderTemplate))
        {
            // Valheim 1.0 world folder (_main.N.fwl2/db2/chunks + minimap caches): copied as-is, so the game loads it
            // directly instead of regenerating locations and the minimap from the legacy .fwl/.db.
            var dest = Path.Combine(ValheimPaths.WorldsFolder, target.WorldName);
            BootNotice.Show("DrakesTestLab: loading the test world...");
            CopyDirectory(worldFolderTemplate, dest);
            if (ValheimPaths.WorldExists(target.WorldName))
                TestLabPlugin.Log?.LogInfo($"Deployed world folder template {target.TemplateWorldFile} -> {dest}");
            else
                TestLabPlugin.Log?.LogError(
                    $"Copied the world template but the game will not find it: {worldFolderTemplate} [{Describe(worldFolderTemplate)}] -> {dest} [{Describe(dest)}]");
        }
        else if (resetWorld || !ValheimPaths.WorldExists(target.WorldName))
        {
            // only an old .fwl/.db template: the game rebuilds locations and the map on the first load, which is slow
            TestLabPlugin.Log?.LogWarning($"No world folder template at {worldFolderTemplate}; using the old .fwl/.db template. The first load rebuilds the world and can take minutes.");
            BootNotice.Show("DrakesTestLab: building the test world from an old template.\nThe first load can take a few minutes; later launches are quick.");
            CopyTemplateWithFallback(
                templates,
                target.TemplateWorldFile,
                DefaultTemplateWorldFile,
                ".fwl",
                ValheimPaths.WorldsFolder,
                target.WorldName,
                target);
            CopyTemplateWithFallback(
                templates,
                target.TemplateWorldFile,
                DefaultTemplateWorldFile,
                ".db",
                ValheimPaths.WorldsFolder,
                target.WorldName,
                target);
        }
        if (TestLabConfig.Instance.FreshCharacter.Value)
        {
            if (resetCharacter || !ValheimPaths.CharacterExists(target.CharacterName))
                CharacterFactory.CreateFresh(target.CharacterName, TestLabConfig.Instance.CharacterDisplayName.Value);
        }
        else if (resetCharacter || !ValheimPaths.CharacterExists(target.CharacterName))
        {
            CopyTemplateWithFallback(
                templates,
                target.TemplateCharacterFile,
                DefaultTemplateCharacterFile,
                ".fch",
                ValheimPaths.CharactersFolder,
                target.CharacterName,
                target);
        }

        // The game scans the save folders once and caches them; tell it the folders changed under it.
        FejdStartupBridge.InvalidateSaveCaches();

        TestLabPlugin.Log?.LogInfo(
            $"Pre-load deploy complete (before world entry): mode={target.Mode} " +
            $"world={target.TemplateWorldFile}->{target.WorldName} " +
            $"char={target.TemplateCharacterFile}->{target.CharacterName} templates={templates}");
    }

    private static bool HasWorldFolder(string dir) =>
        Directory.Exists(dir) && Directory.GetFiles(dir, "_main.*.fwl2").Length > 0;

    /// <summary>The templates folder that holds the shipped world, searched for under the mod's own folder.</summary>
    private static string? FindTemplatesFolder(string worldTemplate)
    {
        try
        {
            var pluginDir = Path.GetDirectoryName(TestLabPlugin.Instance?.Info.Location);
            if (string.IsNullOrEmpty(pluginDir) || !Directory.Exists(pluginDir))
                return null;
            foreach (var dir in Directory.GetDirectories(pluginDir, worldTemplate, SearchOption.AllDirectories))
            {
                if (HasWorldFolder(dir))
                    return Path.GetDirectoryName(dir);
            }
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Looking for the world template failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>The file names in a folder, for a report.</summary>
    internal static string Describe(string dir)
    {
        if (!Directory.Exists(dir))
            return "missing";
        var names = Directory.GetFiles(dir);
        return names.Length == 0 ? "empty" : string.Join(", ", Array.ConvertAll(names, Path.GetFileName));
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        var manifestPath = Path.Combine(source, "_manifest.txt");
        var only = File.Exists(manifestPath) ? new System.Collections.Generic.HashSet<string>(File.ReadAllLines(manifestPath)) : null;
        foreach (var file in Directory.GetFiles(source))
        {
            var leaf = Path.GetFileName(file);
            if (leaf == "_manifest.txt" || (only != null && !only.Contains(leaf)))
                continue;

            File.Copy(file, Path.Combine(dest, leaf), overwrite: true);
        }
    }

    private static void CopyTemplateWithFallback(
        string templatesDir,
        string templateBase,
        string fallbackTemplateBase,
        string extension,
        string destDir,
        string destBase,
        ActiveSaveTarget target)
    {
        var resolvedTemplate = ResolveTemplateSource(templatesDir, templateBase, fallbackTemplateBase, extension);
        if (resolvedTemplate == null)
        {
            TestLabPlugin.Log?.LogError(
                $"Template missing: {templateBase}{extension} and fallback {fallbackTemplateBase}{extension}");
            return;
        }

        var source = Path.Combine(templatesDir, resolvedTemplate + extension);
        var dest = Path.Combine(destDir, destBase + extension);

        if (File.Exists(dest) && !target.CopyTemplatesEveryLaunch)
            return;

        Directory.CreateDirectory(destDir);
        File.Copy(source, dest, overwrite: true);
        TestLabPlugin.Log?.LogInfo($"Deployed template {resolvedTemplate}{extension} -> {dest}");

        if (extension == ".fwl")
        {
            CopiedSaveRenamer.TryPatchDeployedWorld(dest, destBase);
            return;
        }

        if (extension != ".fch")
            return;

        if (!TryPatchAndValidateCharacter(dest, source, destBase, target.WorldName))
        {
            TestLabPlugin.Log?.LogError(
                $"Character save '{destBase}' still invalid after rename patch. Fast boot may fail until cfg uses default names " +
                $"(TestLab / {DefaultEphemeralCharacterName}) or templates are re-exported.");
        }
    }

    private static bool TryPatchAndValidateCharacter(string dest, string source, string destBase, string worldName)
    {
        if (PatchCharacterSave(dest, destBase, worldName) && DeployedSaveValidator.IsCharacterLoadable(destBase))
            return true;

        TestLabPlugin.Log?.LogWarning(
            $"Character save '{destBase}' failed validation after rename patch — restoring clean template copy.");

        File.Copy(source, dest, overwrite: true);
        return PatchCharacterSave(dest, destBase, worldName)
               && DeployedSaveValidator.IsCharacterLoadable(destBase);
    }

    private static bool PatchCharacterSave(string fchPath, string destBase, string worldName)
    {
        CopiedSaveRenamer.TryPatchDeployedCharacter(fchPath, destBase, worldName);

        var data = File.ReadAllBytes(fchPath);
        if (!CopiedSaveRenamer.TryReadCharacterInternalName(data, out var internalName))
        {
            TestLabPlugin.Log?.LogWarning($"Character save '{destBase}' has no internal name field after patch.");
            return false;
        }

        if (!string.Equals(internalName, destBase, StringComparison.Ordinal))
        {
            TestLabPlugin.Log?.LogWarning(
                $"Character save '{destBase}' internal name is '{internalName}' (expected '{destBase}').");
            return false;
        }

        if (CopiedSaveRenamer.ContainsUtf8Text(data, TemplateWorldInternalMaster))
        {
            TestLabPlugin.Log?.LogWarning(
                $"Character save '{destBase}' still references template world '{TemplateWorldInternalMaster}' (expected '{worldName}').");
            return false;
        }

        return true;
    }

    private static string? ResolveTemplateSource(
        string templatesDir,
        string templateBase,
        string fallbackTemplateBase,
        string extension)
    {
        var primary = Path.Combine(templatesDir, templateBase + extension);
        if (File.Exists(primary))
            return templateBase;

        if (string.Equals(templateBase, fallbackTemplateBase, System.StringComparison.OrdinalIgnoreCase))
            return null;

        var fallback = Path.Combine(templatesDir, fallbackTemplateBase + extension);
        if (!File.Exists(fallback))
            return null;

        TestLabPlugin.Log?.LogWarning(
            $"Template {templateBase}{extension} missing — using fallback {fallbackTemplateBase}{extension}");
        return fallbackTemplateBase;
    }
}
