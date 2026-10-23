using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>Selects world/character and starts local game via FejdStartup APIs.</summary>
internal static class FejdStartupBridge
{
    /// <summary>Re-scanning every save on disk is slow; poll at most this often (frames) unless forced.</summary>
    private const int RefreshIntervalFrames = 30;

    static readonly FieldInfo? WorldsField = AccessTools.Field(typeof(FejdStartup), "m_worlds");
    static readonly FieldInfo? ProfilesField = AccessTools.Field(typeof(FejdStartup), "m_profiles");
    static readonly FieldInfo? ProfileIndexField = AccessTools.Field(typeof(FejdStartup), "m_profileIndex");
    static readonly FieldInfo? WorldField = AccessTools.Field(typeof(FejdStartup), "m_world");
    static readonly FieldInfo? StartingWorldField = AccessTools.Field(typeof(FejdStartup), "m_startingWorld");
    static readonly MethodInfo? RefreshWorldSelection = AccessTools.Method(typeof(FejdStartup), "RefreshWorldSelection");
    static readonly MethodInfo? UpdateWorldList = AccessTools.Method(typeof(FejdStartup), "UpdateWorldList", new[] { typeof(bool) });
    static readonly MethodInfo? SetSelectedWorld = AccessTools.Method(typeof(FejdStartup), "SetSelectedWorld", new[] { typeof(int), typeof(bool) });
    static readonly MethodInfo? SetSelectedProfile = AccessTools.Method(typeof(FejdStartup), "SetSelectedProfile", new[] { typeof(string) });
    static readonly MethodInfo? OnCharacterStart = AccessTools.Method(typeof(FejdStartup), "OnCharacterStart");
    static readonly MethodInfo? OnWorldStart = AccessTools.Method(typeof(FejdStartup), "OnWorldStart");

    // CI compiles against pre-1.0 stubs that lack these 1.0 members, so reach them by reflection.
    static readonly FieldInfo? WorldFileNameField = AccessTools.Field(typeof(World), "m_worldName");
    static readonly MethodInfo? InvalidateSaveType = AccessTools.Method(typeof(SaveSystem), "InvalidateCache", new[] { typeof(SaveDataType) });

    static int _lastBootstrapDiagFrame = -999;
    static int _lastRefreshFrame = -999;
    static List<PlayerProfile> _profiles = new();

    public static bool IsObjectDbReady() => ObjectDB.instance != null;

    /// <summary>The game scans worlds_local/characters_local once and caches the result, including parsed world
    /// headers keyed by path. After we replace files on disk those caches must be dropped.</summary>
    public static void InvalidateSaveCaches()
    {
        try
        {
            InvalidateSaveType?.Invoke(null, new[] { Enum.Parse(typeof(SaveDataType), "World") });
            InvalidateSaveType?.Invoke(null, new[] { Enum.Parse(typeof(SaveDataType), "Character") });
            SaveSystem.ClearWorldListCache(false);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Save cache invalidation skipped: {ex.Message}");
        }

        _lastRefreshFrame = -999;
    }

    public static bool IsBootstrapReady(FejdStartup fejd, string worldSaveName, string characterSaveName)
    {
        if (fejd == null || !IsObjectDbReady())
            return false;

        if (!EnsureSaveFilesExist(worldSaveName, characterSaveName))
            return false;

        if (!DeployedSaveValidator.IsCharacterLoadable(characterSaveName))
        {
            LogBootstrapBlocker(characterSaveName, "character save exists but PlayerProfile.Load failed");
            return false;
        }

        RefreshSaveLists(fejd);

        var worldReady = FindWorldInSaveList(fejd, worldSaveName) != null;
        var profileReady = FindProfile(_profiles, characterSaveName) != null;

        if (!worldReady)
            LogBootstrapBlocker(worldSaveName, $"world not in menu list ({DescribeWorldNames(fejd)})");
        else if (!profileReady)
            LogBootstrapBlocker(characterSaveName, $"character not in profile list ({DescribeProfileNames(_profiles)})");

        return worldReady && profileReady;
    }

    private static void LogBootstrapBlocker(string saveName, string reason)
    {
        var frame = Time.frameCount;
        if (frame - _lastBootstrapDiagFrame < 120)
            return;

        _lastBootstrapDiagFrame = frame;
        TestLabPlugin.Log?.LogWarning($"FastBoot waiting on '{saveName}': {reason}");
    }

    public static bool TryFastStart(FejdStartup fejd, string worldSaveName, string characterSaveName)
    {
        try
        {
            if (fejd == null)
                return false;

            if (!IsBootstrapReady(fejd, worldSaveName, characterSaveName))
                return false;

            MenuSuppressor.Suppress(fejd);

            var world = FindWorldInSaveList(fejd, worldSaveName);
            if (world == null)
            {
                TestLabPlugin.Log?.LogWarning(
                    $"FastBoot: world save '{worldSaveName}' not in menu list (internal names: {DescribeWorldNames(fejd)}).");
                return false;
            }

            var worldIndex = FindWorldIndex(fejd, world);
            if (worldIndex < 0 || !InvokeFejd(fejd, SetSelectedWorld, worldIndex, true))
            {
                TestLabPlugin.Log?.LogWarning(
                    $"FastBoot: could not select world index for '{worldSaveName}' (index={worldIndex}).");
                return false;
            }

            var profile = FindProfile(_profiles, characterSaveName);
            if (profile == null)
            {
                TestLabPlugin.Log?.LogWarning(
                    $"FastBoot: character save '{characterSaveName}' not found (profiles: {DescribeProfileNames(_profiles)}).");
                return false;
            }

            ProfilesField?.SetValue(fejd, _profiles);
            var profileIndex = _profiles.IndexOf(profile);
            if (profileIndex >= 0)
                ProfileIndexField?.SetValue(fejd, profileIndex);

            InvokeFejd(fejd, SetSelectedProfile, profile.GetFilename());

            // Vanilla path after picking character: OnCharacterStart -> ShowStartGame -> RefreshWorldSelection.
            if (!InvokeFejd(fejd, OnCharacterStart))
            {
                TestLabPlugin.Log?.LogWarning("FastBoot: OnCharacterStart unavailable.");
                return false;
            }

            MenuSuppressor.Suppress(fejd);
            InvokeFejd(fejd, RefreshWorldSelection);

            if (WorldField?.GetValue(fejd) is not World selectedWorld)
            {
                TestLabPlugin.Log?.LogWarning("FastBoot: world selection did not stick after RefreshWorldSelection.");
                return false;
            }

            // Vanilla start path - direct ZNet/TransitionToMainScene skipped player/equipment init.
            StartingWorldField?.SetValue(fejd, false);
            ServerSettings.ApplyBeforeStart(fejd);
            InvokeFejd(fejd, OnWorldStart);

            if (IsStartingWorld(fejd))
            {
                TestLabPlugin.Log?.LogInfo(
                    $"FastBoot started world={selectedWorld.m_name} (file={worldSaveName}) " +
                    $"char={profile.GetName()}<{profile.GetFilename()}> (file={characterSaveName})");
                return true;
            }

            TestLabPlugin.Log?.LogWarning(
                $"FastBoot failed to arm load (world={selectedWorld.m_name}, dataError={selectedWorld.m_dataError}).");
            return false;
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogError($"FastBoot failed: {ex}");
            return false;
        }
    }

    /// <summary>Re-reads world and character lists. Throttled unless <paramref name="force"/>.</summary>
    public static void RefreshSaveLists(FejdStartup fejd, bool force = false)
    {
        var frame = Time.frameCount;
        if (!force && frame - _lastRefreshFrame < RefreshIntervalFrames)
            return;

        _lastRefreshFrame = frame;
        InvalidateSaveCaches();
        _lastRefreshFrame = frame;

        try
        {
            WorldsField?.SetValue(fejd, SaveSystem.GetWorldList());
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"SaveSystem.GetWorldList failed: {ex.Message}");
            WorldsField?.SetValue(fejd, null);
            InvokeFejd(fejd, UpdateWorldList, true);
        }

        try
        {
            _profiles = SaveSystem.GetAllPlayerProfiles();
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"SaveSystem.GetAllPlayerProfiles failed: {ex.Message}");
            _profiles = new List<PlayerProfile>();
        }

        InvokeFejd(fejd, RefreshWorldSelection);
    }

    private static bool IsStartingWorld(FejdStartup fejd) =>
        StartingWorldField?.GetValue(fejd) is true;

    private static bool InvokeFejd(FejdStartup fejd, MethodInfo? method, params object[] args)
    {
        if (method == null)
            return false;

        method.Invoke(fejd, args);
        return true;
    }

    private static IList<World>? GetWorlds(FejdStartup fejd) =>
        WorldsField?.GetValue(fejd) as IList<World>;

    private static World? FindWorldInSaveList(FejdStartup fejd, string saveBaseName)
    {
        var worlds = GetWorlds(fejd);
        if (worlds == null)
            return null;

        foreach (var world in worlds)
        {
            if (WorldMatchesSaveName(world, saveBaseName))
                return world;
        }

        return null;
    }

    private static int FindWorldIndex(FejdStartup fejd, World target)
    {
        var worlds = GetWorlds(fejd);
        if (worlds == null)
            return -1;

        for (var i = 0; i < worlds.Count; i++)
        {
            if (ReferenceEquals(worlds[i], target))
                return i;
        }

        return -1;
    }

    /// <summary>1.0 worlds carry the file/folder name in <c>m_worldName</c>; <c>m_name</c> is the name stored inside the file.</summary>
    private static bool WorldMatchesSaveName(World world, string saveBaseName)
    {
        return string.Equals(GetWorldFileName(world), saveBaseName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(world.m_name, saveBaseName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWorldFileName(World world) =>
        WorldFileNameField?.GetValue(world) as string ?? world.m_name;

    private static PlayerProfile? FindProfile(List<PlayerProfile> profiles, string saveBaseName)
    {
        foreach (var profile in profiles)
        {
            if (string.Equals(profile.GetFilename(), saveBaseName, StringComparison.OrdinalIgnoreCase))
                return profile;
        }

        return null;
    }

    private static string DescribeWorldNames(FejdStartup fejd)
    {
        var worlds = GetWorlds(fejd);
        if (worlds == null || worlds.Count == 0)
            return "(none)";

        var parts = new List<string>();
        foreach (var world in worlds)
            parts.Add($"{world.m_name}@{GetWorldFileName(world)}");

        return string.Join(", ", parts);
    }

    private static string DescribeProfileNames(List<PlayerProfile> profiles)
    {
        if (profiles.Count == 0)
            return "(none)";

        var parts = new List<string>();
        foreach (var profile in profiles)
            parts.Add($"{profile.GetName()}<{profile.GetFilename()}>");

        return string.Join(", ", parts);
    }

    private static bool _redeployed;
    private static float _lastMissingLog = -999f;

    private static bool EnsureSaveFilesExist(string worldSaveName, string characterSaveName)
    {
        var worldOk = ValheimPaths.WorldExists(worldSaveName);
        var charOk = ValheimPaths.CharacterExists(characterSaveName);
        if ((!worldOk || !charOk) && !_redeployed)
        {
            // the copy at startup did not take (or something removed it): copy the shipped saves once more
            _redeployed = true;
            TestLabPlugin.Log?.LogWarning($"FastBoot: test {(worldOk ? "character" : "world")} missing; copying the shipped saves again.");
            PresetDeployer.DeployForCurrentMode();
            worldOk = ValheimPaths.WorldExists(worldSaveName);
            charOk = ValheimPaths.CharacterExists(characterSaveName);
        }

        if (worldOk && charOk)
            return true;

        // this runs every frame while FastBoot waits: say it every few seconds, not every frame
        if (Time.realtimeSinceStartup - _lastMissingLog >= 5f)
        {
            _lastMissingLog = Time.realtimeSinceStartup;
            var worldDir = Path.Combine(ValheimPaths.WorldsFolder, worldSaveName);
            TestLabPlugin.Log?.LogWarning(!worldOk
                ? $"FastBoot: world '{worldSaveName}' not found in {ValheimPaths.WorldsFolder} (folder {worldDir}: {PresetDeployer.Describe(worldDir)}). Shipped templates: {ValheimPaths.TemplatesFolder}."
                : $"FastBoot: character file missing: {Path.Combine(ValheimPaths.CharactersFolder, characterSaveName + ".fch")}");
        }

        return false;
    }
}
