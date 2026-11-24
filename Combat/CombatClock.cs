using UnityEngine;

namespace DrakesTestLab.Combat;

/// <summary>
/// Holds the game clock at the combat speed. Valheim puts Time.timeScale back to 1 every frame (its pause handling), so a one-off
/// assignment does nothing; this sets it again after the game's own update. Scale 0 = hands off.
/// </summary>
internal sealed class CombatClock : MonoBehaviour
{
    public static float Scale;
    private static CombatClock? _instance;

    public static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("DrakesTestLab.CombatClock");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CombatClock>();
    }

    public static void Stop()
    {
        Scale = 0f;
        Time.timeScale = 1f;
        if (_instance != null) Destroy(_instance.gameObject);
        _instance = null;
    }

    private void LateUpdate()
    {
        if (Scale > 0f) Time.timeScale = Scale;
    }
}
