using System;
using System.Collections.Generic;
using DrakesTestLab.UI.Toolbox;

namespace DrakesTestLab;

/// <summary>
/// Lets another mod add its own tab to the toolbox (F9) without referencing this mod: find this type by name
/// (<c>DrakesTestLab.TestLabTabs</c>) and call <see cref="Register"/> by reflection. The draw callback runs inside the window with the
/// toolbox skin, so plain <c>GUILayout</c> buttons and labels look right. <see cref="Header"/> and <see cref="Hint"/> start a card and
/// add a grey note, like the built-in tabs do.
/// </summary>
public static class TestLabTabs
{
    internal sealed class Entry
    {
        public string Title = "";
        public string Group = "Tools";
        public Action Draw = () => { };
        public bool Added;
    }

    internal static readonly List<Entry> Registered = new();

    /// <summary>Add a tab. <paramref name="group"/> is Play, Scenario, Tests or Tools (anything else goes to Tools). A second call with the same title replaces the first.</summary>
    public static void Register(string title, string group, Action draw)
    {
        if (string.IsNullOrWhiteSpace(title) || draw == null)
            return;
        var existing = Registered.Find(e => e.Title == title);
        if (existing != null)
        {
            existing.Group = group;
            existing.Draw = draw;
            return;
        }
        Registered.Add(new Entry { Title = title, Group = group, Draw = draw });
    }

    /// <summary>Start a new card with this title.</summary>
    public static void Header(string text) => Ui.Header(text);

    /// <summary>A small grey note.</summary>
    public static void Hint(string text) => Ui.Hint(text);
}

/// <summary>A tab another mod registered through <see cref="TestLabTabs"/>.</summary>
internal sealed class ExternalTab : ToolboxTab
{
    private readonly TestLabTabs.Entry _entry;

    public ExternalTab(TestLabTabs.Entry entry) => _entry = entry;

    public override string Title => _entry.Title;

    public override string Group => _entry.Group is "Play" or "Scenario" or "Tests" or "Tools" ? _entry.Group : "Tools";

    public override void Draw()
    {
        try
        {
            _entry.Draw();
        }
        catch (Exception ex)
        {
            UnityEngine.GUILayout.Label($"<color=#ff8080>{_entry.Title} failed: {ex.Message}</color>");
        }
    }
}
