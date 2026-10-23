using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DrakesTestLab.Automation;

/// <summary>Finds and drives the game's UI (Unity UI buttons, toggles, text fields) by name, label text or hierarchy path.</summary>
internal static class UiDriver
{
    public static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (var cur = t; cur != null; cur = cur.parent)
            parts.Insert(0, cur.name);
        return string.Join("/", parts);
    }

    public static string LabelOf(Component c)
    {
        var tmp = c.GetComponentInChildren<TMP_Text>();
        if (tmp != null && !string.IsNullOrEmpty(tmp.text))
            return tmp.text;
        var legacy = c.GetComponentInChildren<Text>();
        return legacy != null ? legacy.text : "";
    }

    /// <summary>Match order: exact object name, exact label, path ending, label contains.</summary>
    private static T? Find<T>(string query) where T : Component
    {
        var q = query.Trim();
        var all = UnityEngine.Object.FindObjectsOfType<T>();
        T? byLabel = null, byPath = null, byContains = null;
        foreach (var c in all)
        {
            if (c == null || !c.gameObject.activeInHierarchy)
                continue;
            if (c.name.Equals(q, StringComparison.OrdinalIgnoreCase))
                return c;

            var label = Clean(LabelOf(c));
            if (byLabel == null && label.Equals(q, StringComparison.OrdinalIgnoreCase))
                byLabel = c;
            if (byPath == null && q.Contains("/") && PathOf(c.transform).EndsWith(q, StringComparison.OrdinalIgnoreCase))
                byPath = c;
            if (byContains == null && label.Length > 0 && label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                byContains = c;
        }

        return byLabel ?? byPath ?? byContains;
    }

    private static string Clean(string s)
    {
        // strip rich-text tags like <color=...> so labels compare as shown
        var sb = new StringBuilder();
        var inTag = false;
        foreach (var ch in s)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    public static string Click(string query)
    {
        var button = Find<Button>(query);
        if (button != null)
        {
            if (!button.interactable)
                return $"button '{query}' is not clickable right now";
            button.onClick.Invoke();
            return "";
        }

        var toggle = Find<Toggle>(query);
        if (toggle != null)
        {
            toggle.isOn = !toggle.isOn;
            return "";
        }

        if (UitkDriver.Click(query))
            return "";

        return $"no visible button or toggle matching '{query}' (Unity UI or UI Toolkit)";
    }

    public static string SetToggle(string query, bool value)
    {
        var toggle = Find<Toggle>(query);
        if (toggle == null)
            return UitkDriver.SetToggle(query, value) ? "" : $"no visible toggle matching '{query}'";
        toggle.isOn = value;
        return "";
    }

    public static string Type(string query, string text)
    {
        var tmp = Find<TMP_InputField>(query);
        if (tmp != null)
        {
            tmp.text = text;
            tmp.onEndEdit?.Invoke(text);
            return "";
        }

        var legacy = Find<InputField>(query);
        if (legacy != null)
        {
            legacy.text = text;
            legacy.onEndEdit?.Invoke(text);
            return "";
        }

        if (UitkDriver.Type(query, text))
            return "";

        return $"no visible text field matching '{query}'";
    }

    /// <summary>Special names for the game's main windows, otherwise any active object with that name or label.</summary>
    public static bool IsVisible(string name)
    {
        switch (name.Trim().ToLowerInvariant())
        {
            case "inventorygui":
            case "inventory":
                return InventoryGui.IsVisible();
            case "menu":
                return Menu.IsVisible();
            case "textinput":
                return TextInput.IsVisible();
            case "console":
                return global::Console.IsVisible();
            case "minimap":
            case "map":
                return Minimap.IsOpen();
            case "storegui":
            case "store":
                return StoreGui.IsVisible();
        }

        var go = GameObject.Find(name);
        if (go != null && go.activeInHierarchy)
            return true;
        return Find<Button>(name) != null || FindText(name) != null || UitkDriver.IsVisible(name);
    }

    /// <summary>Only a label or element that is exactly this text (or named so) counts: for "this tab is NOT there" checks.</summary>
    public static bool IsVisibleExact(string name)
    {
        var q = name.Trim();
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
        {
            if (t != null && t.gameObject.activeInHierarchy && string.Equals(t.text.Trim(), q, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return UitkDriver.IsVisibleExact(q);
    }

    /// <summary>Button / toggle / field clickable right now? null = not found.</summary>
    public static bool? IsEnabled(string query)
    {
        var selectable = Find<Selectable>(query);
        if (selectable != null)
            return selectable.interactable && selectable.IsActive();
        return UitkDriver.Enabled(query);
    }

    public static TMP_Text? FindText(string query)
    {
        var q = query.Trim();
        TMP_Text? contains = null;
        foreach (var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
        {
            if (t == null || !t.gameObject.activeInHierarchy)
                continue;
            if (t.name.Equals(q, StringComparison.OrdinalIgnoreCase) || PathOf(t.transform).EndsWith(q, StringComparison.OrdinalIgnoreCase))
                return t;
            if (contains == null && t.text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                contains = t;
        }

        return contains;
    }

    public static string TextOf(string query)
    {
        var t = FindText(query);
        if (t != null)
            return Clean(t.text);

        var go = GameObject.Find(query);
        if (go != null)
        {
            var any = go.GetComponentInChildren<TMP_Text>();
            if (any != null)
                return Clean(any.text);
        }

        return UitkDriver.Text(query) ?? "";
    }
}

/// <summary>Hover the game's UI and see what a test step should call it. F8 (configurable) copies the best name.</summary>
internal static class UiPicker
{
    public static bool Active;
    public static readonly List<string> Hits = new();
    private static string _best = "";

    public static void Update()
    {
        if (!Active)
            return;

        Hits.Clear();
        _best = "";
        var es = EventSystem.current;
        if (es == null)
            return;

        var data = new PointerEventData(es) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        es.RaycastAll(data, results);
        foreach (var r in results)
        {
            if (r.gameObject == null)
                continue;
            var button = r.gameObject.GetComponentInParent<Button>();
            var target = button != null ? button.transform : r.gameObject.transform;
            var label = button != null ? UiDriver.LabelOf(button) : "";
            var line = $"{target.name}{(label.Length > 0 ? $"  [label: {label}]" : "")}  path: {UiDriver.PathOf(target)}";
            if (!Hits.Contains(line))
                Hits.Add(line);
            if (_best.Length == 0)
                _best = target.name;
            if (Hits.Count >= 4)
                break;
        }

        var key = TestLabPlugin_Keys.PickerKey;
        if (key != KeyCode.None && Input.GetKeyDown(key) && _best.Length > 0)
        {
            GUIUtility.systemCopyBuffer = _best;
            TestLabPlugin.Log?.LogInfo($"UI picker copied '{_best}'.");
        }
    }

    public static void DrawOverlay()
    {
        if (!Active)
            return;

        var text = Hits.Count == 0
            ? "UI picker: hover a game window or button"
            : "UI picker (" + TestLabPlugin_Keys.PickerKey + " copies the first name):\n" + string.Join("\n", Hits);
        var pos = Input.mousePosition;
        var rect = new Rect(pos.x + 18f, Screen.height - pos.y + 12f, 620f, 22f + 18f * Hits.Count);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, rect.height), text);
    }
}

internal static class TestLabPlugin_Keys
{
    public static KeyCode Parse(string name) =>
        Enum.TryParse<KeyCode>(name ?? "", true, out var k) ? k : KeyCode.None;

    public static KeyCode PickerKey => Parse(Bootstrap.TestLabConfig.Instance.PickerKey.Value);
}
