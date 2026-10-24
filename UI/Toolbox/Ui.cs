using System;
using System.Collections.Generic;
using UnityEngine;

namespace DrakesTestLab.UI.Toolbox;

/// <summary>
/// The toolbox look and small IMGUI helpers shared by the tabs. A tab is a column of cards: every <see cref="Header"/> starts a new
/// card (the previous one closes by itself), so a tab only writes headers, rows and hints.
/// </summary>
internal static class Ui
{
    // palette
    public static readonly Color Bg = Hex(0x15171c);
    public static readonly Color Side = Hex(0x101216);
    public static readonly Color Card = Hex(0x1d2027);
    public static readonly Color Raised = Hex(0x292d36);
    public static readonly Color RaisedHover = Hex(0x343945);
    public static readonly Color Accent = Hex(0xf0b45a);
    public static readonly Color AccentDim = Hex(0x8a6a3a);
    public static readonly Color Text = Hex(0xe8e6e1);
    public static readonly Color Muted = Hex(0x8f939c);
    public static readonly Color Good = Hex(0x7fd18b);
    public static readonly Color Danger = Hex(0xe0675c);

    public static GUIStyle CardStyle = null!;
    public static GUIStyle CardTitle = null!;
    public static GUIStyle HintStyle = null!;
    public static GUIStyle NavItem = null!;
    public static GUIStyle NavSelected = null!;
    public static GUIStyle NavGroup = null!;
    public static GUIStyle Primary = null!;
    public static GUIStyle Danger_ = null!;
    public static GUIStyle Chip = null!;
    public static GUIStyle SideStyle = null!;
    public static GUIStyle TitleStyle = null!;

    private static bool _cardOpen;

    public static Color Hex(int rgb) => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);

    public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    // ---------------------------------------------------------------- skin

    /// <summary>Builds the toolbox skin once: flat dark panels with rounded corners, one warm accent.</summary>
    public static GUISkin MakeSkin(GUISkin from)
    {
        var skin = UnityEngine.Object.Instantiate(from);
        skin.window.normal.background = skin.window.onNormal.background = Rounded(Bg, 6);
        skin.window.border = new RectOffset(6, 6, 6, 6);
        skin.window.padding = new RectOffset(0, 0, 0, 0);
        skin.window.normal.textColor = skin.window.onNormal.textColor = Text;

        skin.label.richText = true;
        skin.label.fontSize = 13;
        skin.label.wordWrap = true;
        skin.label.normal.textColor = Text;

        Button(skin.button, Raised, RaisedHover, Text);
        skin.button.fontSize = 12;
        skin.button.richText = true;

        skin.textField.normal.background = skin.textField.focused.background = skin.textField.hover.background = Rounded(Hex(0x0f1014), 4);
        skin.textField.border = new RectOffset(4, 4, 4, 4);
        skin.textField.padding = new RectOffset(6, 6, 4, 4);
        skin.textField.normal.textColor = skin.textField.focused.textColor = skin.textField.hover.textColor = Text;
        skin.textField.fontSize = 13;
        skin.textArea.normal.background = skin.textArea.focused.background = skin.textField.normal.background;
        skin.textArea.normal.textColor = skin.textArea.focused.textColor = Text;

        skin.toggle.fontSize = 13;
        skin.toggle.richText = true;
        skin.toggle.wordWrap = false;
        // a clear checkbox: dark box when off, accent box with a tick when on (the left 20 px of the texture; the rest is clear)
        skin.toggle.normal.background = skin.toggle.focused.background = Checkbox(Hex(0x0f1014), Hex(0x4a4f5c), false);
        skin.toggle.hover.background = skin.toggle.active.background = Checkbox(Hex(0x0f1014), Accent, false);
        skin.toggle.onNormal.background = skin.toggle.onFocused.background = Checkbox(Accent, Accent, true);
        skin.toggle.onHover.background = skin.toggle.onActive.background = Checkbox(Hex(0xf6c77c), Hex(0xf6c77c), true);
        skin.toggle.border = new RectOffset(20, 2, 0, 0);
        skin.toggle.overflow = new RectOffset(0, 0, 0, 0);
        skin.toggle.padding = new RectOffset(22, 4, 1, 1);
        skin.toggle.margin = new RectOffset(2, 4, 3, 3);
        skin.toggle.fixedHeight = 18;
        skin.toggle.normal.textColor = skin.toggle.onNormal.textColor = skin.toggle.hover.textColor = skin.toggle.onHover.textColor =
            skin.toggle.active.textColor = skin.toggle.onActive.textColor = skin.toggle.focused.textColor = skin.toggle.onFocused.textColor = Text;

        skin.verticalScrollbar.normal.background = MakeTex(Hex(0x14161b));
        skin.verticalScrollbarThumb.normal.background = Rounded(Raised, 3);
        skin.horizontalSlider.normal.background = Rounded(Hex(0x0f1014), 3);
        skin.horizontalSliderThumb.normal.background = skin.horizontalSliderThumb.hover.background = skin.horizontalSliderThumb.active.background = Rounded(Accent, 5);

        CardStyle = new GUIStyle { normal = { background = Rounded(Card, 6) }, border = new RectOffset(6, 6, 6, 6), padding = new RectOffset(12, 12, 8, 10), margin = new RectOffset(0, 4, 0, 8) };
        CardTitle = new GUIStyle(skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Accent }, margin = new RectOffset(0, 0, 0, 4), wordWrap = false };
        HintStyle = new GUIStyle(skin.label) { fontSize = 12, normal = { textColor = Muted } };
        SideStyle = new GUIStyle { normal = { background = MakeTex(Side) }, padding = new RectOffset(8, 8, 10, 10) };
        TitleStyle = new GUIStyle(skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Text }, wordWrap = false };
        NavGroup = new GUIStyle(skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, normal = { textColor = Muted }, margin = new RectOffset(4, 0, 10, 2), wordWrap = false };
        NavItem = new GUIStyle(skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 13, padding = new RectOffset(10, 6, 5, 5), margin = new RectOffset(0, 0, 1, 1) };
        Button(NavItem, Side, Raised, Text);
        NavSelected = new GUIStyle(NavItem) { fontStyle = FontStyle.Bold };
        Button(NavSelected, Raised, Raised, Accent);
        Primary = new GUIStyle(skin.button) { fontStyle = FontStyle.Bold };
        Button(Primary, Accent, Hex(0xf6c77c), Hex(0x1a1408));
        Danger_ = new GUIStyle(skin.button);
        Button(Danger_, Hex(0x5a2a26), Hex(0x733630), Text);
        Chip = new GUIStyle(skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 8, 2, 2), wordWrap = false, normal = { background = Rounded(Raised, 8) } };
        Chip.border = new RectOffset(8, 8, 8, 8);
        return skin;
    }

    private static void Button(GUIStyle style, Color normal, Color hover, Color text)
    {
        style.normal.background = style.onNormal.background = Rounded(normal, 4);
        style.hover.background = style.onHover.background = Rounded(hover, 4);
        style.active.background = style.onActive.background = Rounded(Color.Lerp(hover, Color.white, 0.08f), 4);
        style.focused.background = style.normal.background;
        style.border = new RectOffset(4, 4, 4, 4);
        style.normal.textColor = style.onNormal.textColor = style.hover.textColor = style.onHover.textColor = style.active.textColor = style.focused.textColor = text;
    }

    public static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    /// <summary>A 16 px checkbox on the left of a 24x18 texture (the rest clear), with a tick when on.</summary>
    private static Texture2D Checkbox(Color fill, Color edge, bool tick)
    {
        const int w = 24, h = 18;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var clear = new Color(0f, 0f, 0f, 0f);
        var dark = Hex(0x1a1408);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = clear;
                if (x >= 1 && x <= 16 && y >= 1 && y <= 16)
                {
                    var onEdge = x == 1 || x == 16 || y == 1 || y == 16;
                    var corner = (x == 1 || x == 16) && (y == 1 || y == 16);
                    c = corner ? clear : onEdge ? edge : fill;
                    // the tick: a short stroke down-right, then a long one up-right (y grows upwards in a texture)
                    if (tick)
                    {
                        var shortStroke = x >= 4 && x <= 7 && Mathf.Abs((y - 5) - (7 - x) * 1f) <= 1f;
                        var longStroke = x >= 7 && x <= 13 && Mathf.Abs((y - 5) - (x - 7) * 1.2f) <= 1f;
                        if (shortStroke || longStroke)
                            c = dark;
                    }
                }

                t.SetPixel(x, y, c);
            }
        }

        t.Apply();
        return t;
    }

    /// <summary>A small rounded-rectangle texture, used 9-sliced (the style's border equals the radius).</summary>
    private static Texture2D Rounded(Color c, int radius)
    {
        var size = radius * 2 + 2;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - radius));
                var dy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - radius));
                var a = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * a));
            }
        }

        t.Apply();
        return t;
    }

    // ---------------------------------------------------------------- cards

    /// <summary>Called by the host before a tab draws.</summary>
    public static void BeginTab() => _cardOpen = false;

    /// <summary>Called by the host after a tab draws: closes the last card.</summary>
    public static void EndTab() => CloseCard();

    private static void CloseCard()
    {
        if (!_cardOpen)
            return;
        GUILayout.EndVertical();
        _cardOpen = false;
    }

    /// <summary>Starts a card with this title (closing the one before).</summary>
    public static void Header(string text)
    {
        CloseCard();
        GUILayout.BeginVertical(CardStyle);
        _cardOpen = true;
        GUILayout.Label(text.ToUpperInvariant(), CardTitle);
    }

    /// <summary>A short muted explanation under a control.</summary>
    public static void Hint(string text) => GUILayout.Label(text, HintStyle);

    /// <summary>A line of state, coloured: good (green), off (muted) or a warning.</summary>
    public static void State(string text, bool good) => GUILayout.Label($"<color={Html(good ? Good : Muted)}>{text}</color>");

    // ---------------------------------------------------------------- controls

    public static float Slider(string label, float value, float min, float max, string format = "0.00")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: <b>{value.ToString(format)}</b>", GUILayout.Width(200));
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

    public static bool Toggle(bool value, string label, string? hint = null)
    {
        var next = GUILayout.Toggle(value, " " + label);
        if (hint != null)
            Hint(hint);
        return next;
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

    public static bool PrimaryButton(string label, params GUILayoutOption[] options) => GUILayout.Button(label, Primary, options);

    public static bool DangerButton(string label, params GUILayoutOption[] options) => GUILayout.Button(label, Danger_, options);

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
                if (GUILayout.Button(name, name == selected ? Primary : GUI.skin.button))
                    onClick(name);
            }

            GUILayout.EndHorizontal();
        }
    }

    public static void Copy(string text)
    {
        GUIUtility.systemCopyBuffer = text;
    }
}
