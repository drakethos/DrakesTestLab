using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>Small IMGUI helpers shared by the toolbox tabs.</summary>
internal static class Ui
{
    public static void Header(string text)
    {
        GUILayout.Space(8);
        GUILayout.Label($"<b><color=#f0b45a>{text}</color></b>");
    }

    public static float Slider(string label, float value, float min, float max, string format = "0.00")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {value.ToString(format)}", GUILayout.Width(190));
        value = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.EndHorizontal();
        return value;
    }

    public static string Field(string label, string value, float labelWidth = 90)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        value = GUILayout.TextField(value);
        GUILayout.EndHorizontal();
        return value;
    }

    public static void Row(params (string label, Action action)[] buttons)
    {
        GUILayout.BeginHorizontal();
        foreach (var (label, action) in buttons)
        {
            if (GUILayout.Button(label))
                action();
        }

        GUILayout.EndHorizontal();
    }

    /// <summary>Wrapped grid of buttons; the selected entry is highlighted.</summary>
    public static void Grid(IList<string> items, int columns, Action<string> onClick, string? selected = null)
    {
        for (var i = 0; i < items.Count; i += columns)
        {
            GUILayout.BeginHorizontal();
            for (var c = 0; c < columns; c++)
            {
                if (i + c >= items.Count)
                {
                    GUILayout.FlexibleSpace();
                    continue;
                }

                var name = items[i + c];
                var old = GUI.backgroundColor;
                if (name == selected)
                    GUI.backgroundColor = new Color(0.95f, 0.65f, 0.25f);
                if (GUILayout.Button(name))
                    onClick(name);
                GUI.backgroundColor = old;
            }

            GUILayout.EndHorizontal();
        }
    }

    public static void Copy(string text)
    {
        GUIUtility.systemCopyBuffer = text;
    }
}
