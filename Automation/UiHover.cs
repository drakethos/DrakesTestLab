using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace DrakesTestLab.Automation;

/// <summary>
/// Rests the game's own (virtual) mouse on a piece of UI and reads back everything the UI shows. No operating-system mouse
/// moves and no one needs to be at the screen: the position is written into Unity's Input System, so the game's UI
/// raycasts, tooltips and hover states react exactly as they do for a player.
/// </summary>
internal static class UiHover
{
    private static readonly Type? MouseType = AccessTools.TypeByName("UnityEngine.InputSystem.Mouse");
    private static readonly Type? InputStateType = AccessTools.TypeByName("UnityEngine.InputSystem.LowLevel.InputState");

    private static Vector2? _held;
    private static HoverKeeper? _keeper;

    /// <summary>Where the virtual mouse rests now (null = not driven by the lab).</summary>
    public static Vector2? Held => _held;

    // ------------------------------------------------------------------ the virtual mouse

    /// <summary>Writes a screen position (pixels from the bottom left) into the Input System's mouse. False when it cannot.</summary>
    public static bool SetMouse(Vector2 screenPos)
    {
        try
        {
            if (MouseType == null || InputStateType == null)
            {
                LastError = $"the Input System types are missing (Mouse: {MouseType != null}, InputState: {InputStateType != null})";
                return false;
            }

            var mouse = MouseType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (mouse == null)
            {
                LastError = "Mouse.current is null (no mouse device registered)";
                return false;
            }

            // the position control: a property on older versions, found through the Pointer base type on others
            var control = AccessTools.Property(MouseType, "position")?.GetValue(mouse)
                          ?? AccessTools.Property(MouseType.BaseType, "position")?.GetValue(mouse);
            if (control == null)
            {
                LastError = "the mouse has no 'position' control";
                return false;
            }

            var change = InputStateType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Change" && m.IsGenericMethodDefinition && m.GetParameters().Length == 4
                                     && m.GetParameters()[1].ParameterType.IsGenericParameter);
            if (change == null)
            {
                LastError = "InputState.Change<T>(control, value, ...) was not found: " +
                            string.Join("; ", InputStateType.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Change")
                                .Select(m => m.ToString()));
                return false;
            }

            var ps = change.GetParameters();
            change.MakeGenericMethod(typeof(Vector2)).Invoke(null, new object?[]
            {
                control, screenPos, Enum.ToObject(ps[2].ParameterType, 0), Activator.CreateInstance(ps[3].ParameterType)
            });
            return true;
        }
        catch (Exception ex)
        {
            LastError = (ex.InnerException ?? ex).GetType().Name + ": " + (ex.InnerException ?? ex).Message;
            return false;
        }
    }

    /// <summary>Why the last <see cref="SetMouse"/> failed.</summary>
    public static string LastError = "";

    // keeps the position set every frame while a hover is held, so a following step (screenshot, check) sees the tooltip
    private sealed class HoverKeeper : MonoBehaviour
    {
        private void Update()
        {
            if (_held.HasValue)
                SetMouse(_held.Value);
        }
    }

    private static void EnsureKeeper()
    {
        if (_keeper != null)
            return;
        var go = new GameObject("TestLabHoverKeeper");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _keeper = go.AddComponent<HoverKeeper>();
    }

    /// <summary>uiRelease: stop driving the mouse and park it off the UI.</summary>
    public static void Release()
    {
        _held = null;
        SetMouse(new Vector2(-1000f, -1000f));
    }

    // ------------------------------------------------------------------ uiHover

    /// <summary>
    /// uiHover: rest the mouse on a piece of UI and wait for its tooltip. Pick the target with one of:
    /// icon (item prefab: the slot that draws that item's icon), text (a shown label containing it), name (object name or path
    /// part), or x / y (screen pixels from the bottom left; fx / fy are fractions of the screen). Optional: nth (which match,
    /// default 0), where (only matches whose path contains this), wait (seconds, default 1; tooltips open after about half a
    /// second), hold (default true: keep the mouse there for the next steps until uiRelease).
    /// </summary>
    public static IEnumerator Hover(Dictionary<string, object?> step, RunContext ctx)
    {
        var points = new List<(Vector2 Point, string Name)>();
        if (step.Has("x") || step.Has("y"))
        {
            var p = new Vector2(step.Num("x", 0f), step.Num("y", 0f));
            points.Add((p, $"({p.x:0},{p.y:0})"));
        }
        else if (step.Has("fx") || step.Has("fy"))
        {
            var p = new Vector2(step.Num("fx", 0.5f) * Screen.width, step.Num("fy", 0.5f) * Screen.height);
            points.Add((p, $"({p.x:0},{p.y:0})"));
        }
        else
        {
            var matches = FindTargets(step, out var how);
            var nth = (int)step.Num("nth", 0f);
            if (matches.Count <= nth)
                throw new StepFailed($"uiHover: nothing shown matches {how} (index {nth}); {matches.Count} found. " +
                                     "Is the window open? Candidates: " + string.Join(" | ", matches.Take(5).Select(m => PathOf(m))));
            // with "until" every match from nth on is tried in turn (the hotbar and the inventory can draw the same icon)
            for (var i = nth; i < matches.Count && (i == nth || step.Has("until")); i++)
                points.Add((ScreenCenter(matches[i]), PathOf(matches[i])));
            if (matches.Count > 1)
                ctx.Log($"  {matches.Count} matches for {how}; starting at #{nth}");
        }

        var until = step.Str("until");
        var wait = step.Num("wait", 1f);
        var shown = false;
        EnsureKeeper();
        foreach (var (point, name) in points)
        {
            if (point.x < 0 || point.y < 0 || point.x > Screen.width || point.y > Screen.height)
                throw new StepFailed($"uiHover: {name} is off the screen at ({point.x:0},{point.y:0}) of {Screen.width}x{Screen.height}");
            if (!SetMouse(point))
                throw new StepFailed("uiHover: the Input System mouse could not be moved: " + LastError);

            _held = point;
            yield return null;
            yield return null;
            var end = Time.realtimeSinceStartup + wait;
            while (Time.realtimeSinceStartup < end && !ctx.TimedOut)
                yield return null;

            ctx.Log($"  mouse at ({point.x:0},{point.y:0}) on {name}; the pointer is over {TopHit(point)}");
            if (until.Length == 0 || ShownTexts().Any(t => t.IndexOf(until, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                shown = true;
                break;
            }
        }

        if (until.Length > 0 && !shown && step.Bool("require", true))
            throw new StepFailed($"uiHover: '{until}' did not appear after hovering {points.Count} place(s)");
        if (!step.Bool("hold", true))
            Release();
    }

    private static string TopHit(Vector2 point)
    {
        try
        {
            var es = EventSystem.current;
            if (es == null)
                return "(no event system)";
            var results = new List<RaycastResult>();
            es.RaycastAll(new PointerEventData(es) { position = point }, results);
            return results.Count == 0 ? "nothing" : PathOf(results[0].gameObject.transform);
        }
        catch (Exception ex)
        {
            return "(raycast failed: " + ex.Message + ")";
        }
    }

    // ------------------------------------------------------------------ finding the thing to hover

    private static List<RectTransform> FindTargets(Dictionary<string, object?> step, out string how)
    {
        var where = step.Str("where");
        var all = new List<RectTransform>();
        if (step.Has("icon") || step.Has("item"))
        {
            var prefab = step.First("icon", "item");
            how = $"the icon of '{prefab}'";
            var sprite = IconOf(prefab);
            if (sprite == null)
                throw new StepFailed($"uiHover: no item '{prefab}' (or it has no icon)");
            foreach (var img in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
                if (img != null && img.sprite == sprite && IsShown(img))
                    all.Add(img.rectTransform);
        }
        else if (step.Has("text"))
        {
            var needle = step.Str("text");
            how = $"the text '{needle}'";
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                if (t != null && IsShown(t) && Plain(t.text).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    all.Add(t.rectTransform);
        }
        else if (step.Has("name") || step.Has("target"))
        {
            var q = step.First("name", "target");
            how = $"the UI object '{q}'";
            foreach (var g in Resources.FindObjectsOfTypeAll<Graphic>())
            {
                if (g == null || !IsShown(g))
                    continue;
                if (g.name.Equals(q, StringComparison.OrdinalIgnoreCase) || PathOf(g.transform).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    all.Add(g.rectTransform);
            }
        }
        else
        {
            throw new StepFailed("uiHover needs icon, text, name, or x / y");
        }

        if (where.Length > 0)
            all = all.Where(r => PathOf(r).IndexOf(where, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        // a stable order: on-screen position, left to right then top to bottom
        return all.Where(r => OnScreen(ScreenCenter(r))).OrderBy(r => PathOf(r)).ThenBy(r => ScreenCenter(r).x).ToList();
    }

    private static Sprite? IconOf(string prefab)
    {
        var go = ObjectDB.instance?.GetItemPrefab(prefab);
        return go != null ? go.GetComponent<ItemDrop>()?.m_itemData?.GetIcon() : null;
    }

    private static bool OnScreen(Vector2 p) => p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;

    private static Vector2 ScreenCenter(RectTransform rt)
    {
        var canvas = rt.GetComponentInParent<Canvas>();
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
    }

    private static string PathOf(RectTransform rt) => PathOf(rt.transform);

    private static string PathOf(Transform t) => UiDriver.PathOf(t);

    private static bool IsShown(Graphic g) => g.gameObject.activeInHierarchy && g.enabled && g.canvas != null
        && g.canvasRenderer != null && !g.canvasRenderer.cull && g.color.a > 0.01f && GroupAlpha(g.transform) > 0.01f;

    private static float GroupAlpha(Transform t)
    {
        var a = 1f;
        foreach (var g in t.GetComponentsInParent<CanvasGroup>(false))
        {
            a *= g.alpha;
            if (g.ignoreParentGroups)
                break;
        }

        return a;
    }

    // ------------------------------------------------------------------ everything the UI shows

    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    private static string Plain(string? s) => string.IsNullOrEmpty(s) ? "" : Tags.Replace(s, "").Replace("​", "").Trim();

    /// <summary>Every shown text on screen (TextMeshPro, legacy Text and UI Toolkit), one per line, rich text removed.
    /// <paramref name="under"/> limits it to objects whose path contains that text.</summary>
    public static List<string> ShownTexts(string? under = null)
    {
        var list = new List<string>();
        foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (t == null || !IsShown(t))
                continue;
            if (!string.IsNullOrEmpty(under) && PathOf(t.transform).IndexOf(under, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var p = Plain(t.text);
            if (p.Length > 0)
                list.Add(p);
        }

        foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (t == null || !IsShown(t))
                continue;
            if (!string.IsNullOrEmpty(under) && PathOf(t.transform).IndexOf(under, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var p = Plain(t.text);
            if (p.Length > 0)
                list.Add(p);
        }

        foreach (var doc in UnityEngine.Object.FindObjectsOfType<UIDocument>())
        {
            if (doc == null || !doc.isActiveAndEnabled || doc.rootVisualElement == null)
                continue;
            if (!string.IsNullOrEmpty(under) && PathOf(doc.transform).IndexOf(under, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            foreach (var ve in doc.rootVisualElement.Query<TextElement>().ToList())
            {
                if (UitkDriver.Shown(ve))
                {
                    var p = Plain(ve.text);
                    if (p.Length > 0)
                        list.Add(p);
                }
            }
        }

        return list;
    }
}
