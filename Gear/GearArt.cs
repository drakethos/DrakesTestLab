using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace DrakesTestLab.Gear;

/// <summary>
/// Art for the lab gear: embedded in this DLL (Gear/Art, logical names "Gear/&lt;file&gt;"). A file of the same name in the
/// dev folder (config/DrakesTestLab/dev/Gear) wins, so the look can be iterated with <see cref="LabGear.Reload"/> without a rebuild.
/// </summary>
internal static class GearArt
{
    public static string DevFolder => Path.Combine(BepInEx.Paths.ConfigPath, "DrakesTestLab", "dev", "Gear");

    public static bool HasDevFile(string name) => File.Exists(Path.Combine(DevFolder, name));

    public static string? ReadDevText(string name)
    {
        var path = Path.Combine(DevFolder, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static byte[] ReadBytes(string name)
    {
        var dev = Path.Combine(DevFolder, name);
        if (File.Exists(dev))
            return File.ReadAllBytes(dev);
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Gear/" + name)
                           ?? throw new FileNotFoundException("embedded resource Gear/" + name);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    // ImageConversion.LoadImage lives in a module this project doesn't reference (it needs netstandard 2.1): call it by reflection.
    private static readonly MethodInfo LoadImageMethod =
        Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) })
        ?? throw new MissingMethodException("UnityEngine.ImageConversion.LoadImage");

    public static Texture2D LoadTexture(string name, bool linear = false, bool mips = true)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mips, linear) { name = "Gear_" + name };
        var loaded = LoadImageMethod.Invoke(null, new object[] { texture, ReadBytes(name), false });
        if (loaded is not true)
            throw new InvalidDataException("could not decode Gear/" + name);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        return texture;
    }

    public static Sprite LoadIcon(string name)
    {
        var icon = LoadTexture(name, linear: false, mips: false);
        return Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height), new Vector2(0.5f, 0.5f), 100f);
    }
}
