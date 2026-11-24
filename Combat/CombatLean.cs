using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Optional "lean" rendering for fights: the picture is not needed to measure a fight, and drawing it is a big share of the frame
/// on a small GPU. While on, cameras draw nothing, vsync and the frame cap are off and shadows are off; all of it is put back after.
/// The on-screen overlay is a screen-space canvas and keeps drawing. Nothing about the game's simulation changes.
/// </summary>
internal static class CombatLean
{
    private static bool _on;
    private static int _vsync, _target, _pixelLights;
    private static ShadowQuality _shadows;
    private static Camera[] _cameras = new Camera[0];
    private static int[] _masks = new int[0];

    public static void Begin()
    {
        if (_on) return;
        _on = true;
        _vsync = QualitySettings.vSyncCount;
        _target = Application.targetFrameRate;
        _shadows = QualitySettings.shadows;
        _pixelLights = QualitySettings.pixelLightCount;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.pixelLightCount = 0;
        _cameras = Camera.allCameras;
        _masks = new int[_cameras.Length];
        for (var i = 0; i < _cameras.Length; i++)
        {
            _masks[i] = _cameras[i].cullingMask;
            _cameras[i].cullingMask = 0;
        }
    }

    public static void End()
    {
        if (!_on) return;
        _on = false;
        QualitySettings.vSyncCount = _vsync;
        Application.targetFrameRate = _target;
        QualitySettings.shadows = _shadows;
        QualitySettings.pixelLightCount = _pixelLights;
        for (var i = 0; i < _cameras.Length; i++)
            if (_cameras[i] != null) _cameras[i].cullingMask = _masks[i];
    }
}
