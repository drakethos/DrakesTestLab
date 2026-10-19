namespace DrakesTestLab.Bootstrap;

internal static class DeployedSaveValidator
{
    public static bool IsCharacterLoadable(string saveBaseName) =>
        CharacterSaveLoader.LoadByFileBaseName(saveBaseName) != null;

    public static bool AreSaveFilesPresent(string worldName, string characterName)
    {
        if (string.IsNullOrWhiteSpace(worldName) || string.IsNullOrWhiteSpace(characterName))
            return false;

        return System.IO.File.Exists(System.IO.Path.Combine(ValheimPaths.WorldsFolder, worldName + ".fwl"))
               && System.IO.File.Exists(System.IO.Path.Combine(ValheimPaths.WorldsFolder, worldName + ".db"))
               && System.IO.File.Exists(System.IO.Path.Combine(ValheimPaths.CharactersFolder, characterName + ".fch"));
    }
}
