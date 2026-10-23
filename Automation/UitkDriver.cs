using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.UIElements;

namespace DrakesTestLab.Automation;

/// <summary>
/// The same find / click / read / type operations for UI Toolkit windows (UIDocument panels), which newer mod UIs use.
/// Unity UI (uGUI, which the game and Jotunn use) is handled by <see cref="UiDriver"/>; this covers the rest.
/// </summary>
internal static class UitkDriver
{
    private static IEnumerable<VisualElement> All()
    {
        // topmost window first (a dialog over a window wins, like the player's click would)
        var docs = UnityEngine.Object.FindObjectsOfType<UIDocument>();
        Array.Sort(docs, (a, b) => Order(b).CompareTo(Order(a)));
        foreach (var doc in docs)
        {
            if (doc == null || !doc.isActiveAndEnabled)
                continue;
            var root = doc.rootVisualElement;
            if (root == null)
                continue;
            foreach (var ve in root.Query<VisualElement>().ToList())
                yield return ve;
        }
    }

    private static float Order(UIDocument doc) =>
        (doc.panelSettings != null ? doc.panelSettings.sortingOrder : 0f) * 1000f + doc.sortingOrder;

    public static bool Shown(VisualElement ve)
    {
        if (ve.panel == null)
            return false;
        for (var cur = ve; cur != null; cur = cur.parent)
        {
            var s = cur.resolvedStyle;
            if (s.display == DisplayStyle.None || s.visibility == Visibility.Hidden || s.opacity <= 0.01f)
                return false;
        }

        return true;
    }

    private static string TextOf(VisualElement ve) => ve switch
    {
        TextField f => f.value ?? "",
        TextElement t => t.text ?? "",
        _ => ""
    };

    /// <summary>The title a form shows above or before a field: a Label in the field's own row or the row above it.</summary>
    private static string TitleOf(VisualElement field)
    {
        for (var cur = field.parent; cur != null && cur.parent != null; cur = cur.parent)
        {
            var row = cur.parent;
            var idx = row.IndexOf(cur);
            for (var i = idx - 1; i >= 0 && i >= idx - 2; i--)
            {
                var label = row[i] as Label ?? row[i].Q<Label>();
                if (label != null && !string.IsNullOrEmpty(label.text))
                    return label.text;
            }

            if (cur.parent.parent == null || row.childCount > 3)
                break;
        }

        return field is TextField tf ? tf.label ?? "" : "";
    }

    /// <summary>Match order: element name, exact text, text contains. Only shown elements count.</summary>
    public static T? Find<T>(string query) where T : VisualElement
    {
        var q = query.Trim();
        T? byText = null, byContains = null;
        foreach (var ve in All())
        {
            if (ve is not T typed || !Shown(ve))
                continue;
            if (string.Equals(ve.name, q, StringComparison.OrdinalIgnoreCase))
                return typed;
            // fields are found by the title shown with them ("Name", "Description")
            if (ve is TextField && string.Equals(TitleOf(ve).Trim(), q, StringComparison.OrdinalIgnoreCase))
                return typed;
            var text = TextOf(ve);
            if (byText == null && text.Equals(q, StringComparison.OrdinalIgnoreCase))
                byText = typed;
            if (byContains == null && text.Length > 0 && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                byContains = typed;
        }

        return byText ?? byContains;
    }

    public static bool Click(string query)
    {
        var button = Find<Button>(query);
        if (button == null)
        {
            var toggle = Find<Toggle>(query);
            if (toggle == null)
                return false;
            toggle.value = !toggle.value;
            return true;
        }

        // invoke the button's click handlers directly (synthetic pointer events need a focused panel)
        var clickable = button.clickable;
        if (clickable != null)
        {
            if (AccessTools.Field(typeof(Clickable), "clicked")?.GetValue(clickable) is Action clicked)
                clicked.Invoke();
            if (AccessTools.Field(typeof(Clickable), "clickedWithEventInfo")?.GetValue(clickable) is Action<EventBase> withInfo)
                withInfo.Invoke(null!);
        }

        return true;
    }

    private static T? FirstShown<T>() where T : VisualElement
    {
        foreach (var ve in All())
        {
            if (ve is T typed && Shown(ve))
                return typed;
        }

        return null;
    }

    public static bool SetToggle(string query, bool value)
    {
        var toggle = Find<Toggle>(query);
        if (toggle == null)
            return false;
        toggle.value = value;
        return true;
    }

    public static bool Type(string query, string text)
    {
        // "*" = the text field of the topmost window (e.g. a name prompt that just opened)
        var field = query.Trim() == "*" ? FirstShown<TextField>() : Find<TextField>(query);
        if (field == null)
            return false;
        // like typing or pasting: the field's own character limit applies
        if (field.maxLength > 0 && text.Length > field.maxLength)
            text = text.Substring(0, field.maxLength);
        field.Focus();
        field.value = text;
        return true;
    }

    /// <summary>Every shown UI Toolkit text (for reports).</summary>
    public static IEnumerable<string> ShownTexts()
    {
        foreach (var doc in UnityEngine.Object.FindObjectsOfType<UIDocument>())
        {
            if (doc == null || !doc.isActiveAndEnabled || doc.rootVisualElement == null)
                continue;
            foreach (var ve in doc.rootVisualElement.Query<TextElement>().ToList())
            {
                if (!string.IsNullOrWhiteSpace(ve.text) && Shown(ve))
                    yield return $"uitk:{doc.gameObject.name}/{ve.name} = '{ve.text.Replace("\n", " ")}'";
            }
        }
    }

    public static bool IsVisible(string query) => Find<VisualElement>(query) != null;

    /// <summary>A shown element whose name or whole text is exactly the query (no "contains" match: "Shop" is not "Workshop").</summary>
    public static bool IsVisibleExact(string query)
    {
        var q = query.Trim();
        foreach (var ve in All())
        {
            if (Shown(ve) && (string.Equals(ve.name, q, StringComparison.OrdinalIgnoreCase) || string.Equals(TextOf(ve).Trim(), q, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public static string? Text(string query)
    {
        var ve = Find<VisualElement>(query);
        if (ve == null)
            return null;
        var own = TextOf(ve);
        if (own.Length > 0)
            return own;
        var child = ve.Q<TextElement>();
        return child != null ? child.text : "";
    }

    public static bool? Enabled(string query)
    {
        var ve = Find<VisualElement>(query);
        return ve == null ? null : ve.enabledInHierarchy;
    }
}
