using System;

namespace DrakesTestLab.Bootstrap;

internal static class CharacterSaveLoader
{
    // Enum values differ between the pre-1.0 CI stubs and the live game (a compiled-in FileSource.Local
    // pointed at the cloud folder), so resolve by name at runtime.
    internal static readonly FileHelpers.FileSource LocalSource =
        (FileHelpers.FileSource)Enum.Parse(typeof(FileHelpers.FileSource), "Local");

    /// <summary>
    /// Loads a character through the public <see cref="PlayerProfile.Load"/>. Valheim 1.0 made
    /// LoadPlayerFromDisk private, so calling it directly throws MethodAccessException in game.
    /// </summary>
    public static PlayerProfile? LoadByFileBaseName(string saveBaseName)
    {
        if (string.IsNullOrWhiteSpace(saveBaseName))
            return null;

        try
        {
            var profile = new PlayerProfile(saveBaseName, LocalSource);
            return profile.Load() ? profile : null;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Character load failed for '{saveBaseName}': {ex.Message}");
            return null;
        }
    }
}
