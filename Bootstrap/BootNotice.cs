using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// A line at the top of the screen while DrakesTestLab boots into its test world, so a long wait says why (baking a new world,
/// a first load) instead of showing an empty screen. Goes away once you are standing in the world.
/// </summary>
internal sealed class BootNotice : MonoBehaviour
{
    private static BootNotice? _instance;
    private static string _text = "";
    private static float _since;
    private static bool _sticky;

    /// <summary>Show a notice until the player is in the world (sticky: until replaced, e.g. a failure).</summary>
    public static void Show(string text, bool sticky = false)
    {
        _text = text;
        _since = Time.realtimeSinceStartup;
        _sticky = sticky;
        if (_instance == null)
        {
            var go = new GameObject("DrakesTestLab_BootNotice");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BootNotice>();
        }
    }

    public static void Clear() => _text = "";

    private GUIStyle? _style;

    private void Update()
    {
        // in the world and the loading screen is gone: done
        if (!_sticky && _text.Length > 0 && Player.m_localPlayer != null)
            Clear();
    }

    private void OnGUI()
    {
        if (_text.Length == 0)
            return;
        _style ??= new GUIStyle(GUI.skin.box) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(16, 16, 10, 10) };
        var text = _text;
        var waited = Time.realtimeSinceStartup - _since;
        if (!_sticky && waited > 30f)
            text += $"\nStill working ({waited:0} s). The first load of a world can take a few minutes; nothing is stuck.";
        var width = Mathf.Min(Screen.width - 40f, 900f);
        GUI.depth = -1000;
        GUI.Box(new Rect((Screen.width - width) / 2f, 24f, width, 90f), text, _style);
    }
}
