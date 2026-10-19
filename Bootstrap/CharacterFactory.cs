using System;

namespace DrakesTestLab.Bootstrap;

/// <summary>Builds a clean slate character through the game's own profile API (no template, no gear, no skills).</summary>
internal static class CharacterFactory
{
    public static bool CreateFresh(string fileBaseName, string displayName)
    {
        try
        {
            var profile = new PlayerProfile(fileBaseName, CharacterSaveLoader.LocalSource);
            profile.SetName(string.IsNullOrWhiteSpace(displayName) ? "Bjorn" : displayName.Trim());

            // No SavePlayerData: with no stored player blob the game gives a brand-new character its default items on spawn.
            if (!profile.Save())
            {
                TestLabPlugin.Log?.LogError($"Fresh character '{fileBaseName}' could not be saved.");
                return false;
            }

            TestLabPlugin.Log?.LogInfo($"Created fresh character '{fileBaseName}' (display name '{profile.GetName()}').");
            return true;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogError($"Fresh character creation failed: {ex}");
            return false;
        }
    }
}
