using System;

namespace DrakesTestLab.Bootstrap;

internal sealed class ActiveSaveTarget
{
    public SaveModeKind Mode { get; set; }
    public string WorldName { get; set; } = "";
    public string CharacterName { get; set; } = "";
    public string TemplateWorldFile { get; set; } = "";
    public string TemplateCharacterFile { get; set; } = "";
    public bool CopyTemplatesEveryLaunch { get; set; }
    public bool CopyTemplatesIfMissing { get; set; }
}

internal static class SaveModeResolver
{
    public static ActiveSaveTarget Resolve()
    {
        var cfg = TestLabConfig.Instance;
        var mode = cfg.SaveMode.Value;
        var templateWorld = SaveNameResolver.ResolveTemplateName(
            cfg.TemplateWorldFile.Value,
            PresetDeployer.DefaultTemplateWorldFile);
        var templateCharacter = SaveNameResolver.ResolveTemplateName(
            cfg.TemplateCharacterFile.Value,
            PresetDeployer.DefaultTemplateCharacterFile);

        switch (mode)
        {
            case SaveModeKind.Persistent:
                return new ActiveSaveTarget
                {
                    Mode = mode,
                    WorldName = SaveNameResolver.ResolveActiveName(
                        cfg.PersistentWorldName.Value,
                        PresetDeployer.DefaultEphemeralWorldName),
                    CharacterName = SaveNameResolver.ResolveActiveName(
                        cfg.PersistentCharacterName.Value,
                        "BjornPersistent"),
                    TemplateWorldFile = templateWorld,
                    TemplateCharacterFile = templateCharacter,
                    CopyTemplatesEveryLaunch = false,
                    CopyTemplatesIfMissing = true
                };
            case SaveModeKind.EphemeralReuse:
                return new ActiveSaveTarget
                {
                    Mode = mode,
                    WorldName = SaveNameResolver.ResolveActiveName(
                        cfg.EphemeralWorldName.Value,
                        PresetDeployer.DefaultEphemeralWorldName),
                    CharacterName = SaveNameResolver.ResolveActiveName(
                        cfg.EphemeralCharacterName.Value,
                        PresetDeployer.DefaultEphemeralCharacterName),
                    TemplateWorldFile = templateWorld,
                    TemplateCharacterFile = templateCharacter,
                    CopyTemplatesEveryLaunch = false,
                    CopyTemplatesIfMissing = true
                };
            default:
                return new ActiveSaveTarget
                {
                    Mode = SaveModeKind.EphemeralFresh,
                    WorldName = SaveNameResolver.ResolveActiveName(
                        cfg.EphemeralWorldName.Value,
                        PresetDeployer.DefaultEphemeralWorldName),
                    CharacterName = SaveNameResolver.ResolveActiveName(
                        cfg.EphemeralCharacterName.Value,
                        PresetDeployer.DefaultEphemeralCharacterName),
                    TemplateWorldFile = templateWorld,
                    TemplateCharacterFile = templateCharacter,
                    CopyTemplatesEveryLaunch = true,
                    CopyTemplatesIfMissing = true
                };
        }
    }

    public static ActiveSaveTarget CreateDefaultFallbackTarget(ActiveSaveTarget source) =>
        new()
        {
            Mode = source.Mode,
            WorldName = PresetDeployer.DefaultEphemeralWorldName,
            CharacterName = PresetDeployer.DefaultEphemeralCharacterName,
            TemplateWorldFile = source.TemplateWorldFile,
            TemplateCharacterFile = source.TemplateCharacterFile,
            CopyTemplatesEveryLaunch = source.CopyTemplatesEveryLaunch,
            CopyTemplatesIfMissing = source.CopyTemplatesIfMissing
        };

    public static bool UsesDefaultSaveNames(ActiveSaveTarget target) =>
        string.Equals(target.WorldName, PresetDeployer.DefaultEphemeralWorldName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(target.CharacterName, PresetDeployer.DefaultEphemeralCharacterName, StringComparison.OrdinalIgnoreCase);
}
