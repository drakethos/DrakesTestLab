using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Util;

/// <summary>
/// Test camera that circles the local player, for screenshots of the character from any side (front, side, back).
/// Suites drive it with "invoke": DrakesTestLab.Util.OrbitCamera.View(yaw, focus, dist, pitch) / .Off().
/// yaw is relative to where the player faces (0 = looking at the front), focus = height looked at, in metres.
/// </summary>
internal static class OrbitCamera
{
    private static bool _on;
    private static float _yaw, _focus = 1.0f, _dist = 2.4f, _pitch = 6f;

    public static bool View(float yaw, float focus, float dist, float pitch)
    {
        var player = Player.m_localPlayer;
        if (player == null)
            return false;
        _yaw = player.transform.eulerAngles.y + 180f + yaw;
        _focus = focus;
        _dist = dist;
        _pitch = pitch;
        _on = true;
        return true;
    }

    /// <summary>
    /// Photo stage: fixes the time of day (0.5 = noon) and removes trees, logs and rocks within <paramref name="radius"/>
    /// metres of the player so nothing stands between the orbit camera and the character. Test worlds only.
    /// Returns how many objects were removed.
    /// </summary>
    public static int Stage(float timeOfDay, float radius)
    {
        if (EnvMan.instance != null)
        {
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = Mathf.Clamp01(timeOfDay);
        }

        var player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null)
            return 0;
        // Same clear the arena build uses (every networked tree, log, bush and rock in range).
        Bootstrap.ArenaBuilder.ClearArena(player.transform.position, radius, 0f, true);
        return 1;
    }

    public static bool Off()
    {
        _on = false;
        return true;
    }

    internal static void Place(Transform camera)
    {
        var player = Player.m_localPlayer;
        if (!_on || player == null)
            return;
        var target = player.transform.position + Vector3.up * _focus;
        var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        camera.position = target - rotation * Vector3.forward * _dist;
        camera.rotation = rotation;
    }

    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    private static class GameCamera_LateUpdate_Patch
    {
        private static void Postfix(GameCamera __instance) => Place(__instance.transform);
    }
}
