using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Cases;
using UnityEngine;
using UnityEngine.UIElements;

namespace DrakesTestLab.Automation;

/// <summary>
/// Records what you do in game as test steps (craft, place, press E, inventory clicks, drop, chest moves, typing, console, UI clicks,
/// setting changes, playing as someone) and saves them as a test you can edit and run. The steps are the same ones a hand-written
/// test uses; objects you build while recording get tags (chest1, door1...) so later steps can find them again.
/// </summary>
internal static class Recorder
{
    public static bool Recording { get; private set; }
    public static readonly List<Dictionary<string, object?>> Steps = new();
    public static readonly List<Dictionary<string, object?>> Checks = new();
    public static Dictionary<string, object?>? Setup { get; private set; }
    public static string LastMessage = "";

    private static readonly Dictionary<ZDOID, string> Tags = new();
    private static readonly Dictionary<string, int> TagCounts = new();
    private static Vector3 _pose;
    private static float _poseYaw;
    private static bool _havePose;
    private static int _quiet;
    private static float _nextUiScan;
    private static readonly HashSet<VisualElement> HookedRoots = new();
    private static readonly List<(ConfigFile file, EventHandler<SettingChangedEventArgs> handler)> ConfigHandlers = new();

    /// <summary>True while recording and not inside something that must not be recorded (a test run, a case being applied).</summary>
    /// <summary>Paused: nothing is recorded until you resume (walk somewhere, set something up off the record).</summary>
    public static bool Paused { get; private set; }

    public static void TogglePause()
    {
        if (!Recording)
            return;
        Paused = !Paused;
        // after a pause the next action starts from wherever you are then
        _havePose = false;
        LastMessage = Paused ? "Paused: nothing is recorded until you resume." : "Recording again.";
    }

    public static bool Active => Recording && !Paused && _quiet == 0 && (AllowDuringTests || !TestRunner.Running) && !CaseRunner.Running && Player.m_localPlayer != null;

    /// <summary>The lab's own tests record their steps on purpose (normally a running test is never recorded).</summary>
    public static bool AllowDuringTests;

    /// <summary>Wrap code whose effects should not be recorded (the recorder's own actions, a case applying).</summary>
    public static IDisposable Quiet() => new QuietScope();

    private sealed class QuietScope : IDisposable
    {
        public QuietScope() => _quiet++;
        public void Dispose() => _quiet--;
    }

    // ---------------------------------------------------------------- start / stop

    /// <summary>Start recording. With a setup, the test starts from what the game looks like now (a saved case).</summary>
    public static void Start(bool captureSetup)
    {
        Steps.Clear();
        Checks.Clear();
        Tags.Clear();
        TagCounts.Clear();
        _havePose = false;
        Setup = null;
        if (captureSetup && Player.m_localPlayer != null)
        {
            // the start position too, so recorded moves land where you made them
            Setup = CaseCapture.Capture(new CaptureOptions { Position = true }, "recorded setup", "");
            Setup.Remove("name");
        }

        HookConfigs();
        Paused = false;
        Recording = true;
        LastMessage = "Recording. Play the scenario, add checks, then stop and save.";
        TestLabPlugin.Log?.LogInfo("Recorder: started.");
    }

    public static void Stop()
    {
        Recording = false;
        Paused = false;
        UnhookConfigs();
        LastMessage = $"Stopped: {Steps.Count} steps, {Checks.Count} checks. Save it as a test below.";
        TestLabPlugin.Log?.LogInfo($"Recorder: stopped with {Steps.Count} steps.");
    }

    public static void Clear()
    {
        Steps.Clear();
        Checks.Clear();
        Setup = null;
        LastMessage = "";
    }

    // ---------------------------------------------------------------- recording

    public static void Add(Dictionary<string, object?> step)
    {
        if (!Active)
            return;
        // the same thing twice in a row (a double click, a re-sent request) is one step
        if (Steps.Count > 0 && J.Describe(Steps[Steps.Count - 1]) == J.Describe(step))
            return;
        Steps.Add(step);
    }

    /// <summary>Before an action that depends on where you stand: a teleport (with your facing) when you moved since the last one.</summary>
    public static void Pose()
    {
        var p = Player.m_localPlayer;
        if (!Active || p == null)
            return;
        var yaw = p.transform.eulerAngles.y;
        if (_havePose && Vector3.Distance(p.transform.position, _pose) < 1.5f && Mathf.Abs(Mathf.DeltaAngle(yaw, _poseYaw)) < 20f)
            return;
        _pose = p.transform.position;
        _poseYaw = yaw;
        _havePose = true;
        Add(new Dictionary<string, object?> { ["do"] = "teleport", ["x"] = Round(_pose.x), ["z"] = Round(_pose.z), ["yaw"] = Round(yaw) });
    }

    /// <summary>The name a step uses for an object: its tag if it was made while recording, else its prefab (the nearest one is found).</summary>
    public static string NameFor(GameObject go)
    {
        var view = go.GetComponentInParent<ZNetView>();
        if (view != null && view.IsValid() && Tags.TryGetValue(view.GetZDO().m_uid, out var tag))
            return tag;
        return Utils.GetPrefabName(view != null ? view.gameObject : go);
    }

    /// <summary>Give an object made while recording a tag (chest1, chest2...) so later steps can point at it.</summary>
    public static string Tag(GameObject go)
    {
        var view = go.GetComponentInParent<ZNetView>();
        var prefab = Utils.GetPrefabName(view != null ? view.gameObject : go);
        var stem = Stem(prefab);
        TagCounts.TryGetValue(stem, out var n);
        TagCounts[stem] = ++n;
        var tag = stem + n;
        if (view != null && view.IsValid())
            Tags[view.GetZDO().m_uid] = tag;
        return tag;
    }

    private static string Stem(string prefab)
    {
        var p = prefab.ToLowerInvariant();
        foreach (var word in new[] { "chest", "door", "gate", "sign", "stand", "ward", "bench", "forge", "case", "rack", "box", "bed", "fire", "portal" })
        {
            if (p.Contains(word))
                return word;
        }

        var cut = prefab.LastIndexOf('_');
        return (cut >= 0 && cut < prefab.Length - 1 ? prefab.Substring(cut + 1) : prefab).ToLowerInvariant();
    }

    public static float Round(float v) => Mathf.Round(v * 100f) / 100f;

    // ---------------------------------------------------------------- checks

    /// <summary>Checks that what you carry now is there when the test runs (prefab, count, who made it).</summary>
    public static void CheckInventory()
    {
        var p = Player.m_localPlayer;
        if (p == null)
            return;
        var totals = new Dictionary<string, (int count, string crafter)>();
        foreach (var item in p.GetInventory().GetAllItems())
        {
            if (item.m_dropPrefab == null)
                continue;
            var key = item.m_dropPrefab.name;
            totals.TryGetValue(key, out var t);
            var crafter = item.m_crafterID != 0 ? CaseCapture.NameOf(item.m_crafterID, item.m_crafterName) : t.crafter;
            totals[key] = (t.count + item.m_stack, crafter);
        }

        foreach (var kv in totals)
        {
            var check = new Dictionary<string, object?> { ["check"] = "hasItem", ["prefab"] = kv.Key, ["count"] = kv.Value.count };
            if (!string.IsNullOrEmpty(kv.Value.crafter))
                check["crafter"] = kv.Value.crafter;
            check["label"] = $"carrying {kv.Value.count} {kv.Key}";
            AddCheck(check);
        }

        LastMessage = $"Added {totals.Count} inventory checks.";
    }

    /// <summary>Checks the thing under the crosshair: its hover text, who built it, what a chest holds.</summary>
    public static void CheckLookedAt()
    {
        var p = Player.m_localPlayer;
        var go = p != null ? p.GetHoverObject() : null;
        if (go == null)
        {
            LastMessage = "Look at something first (the crosshair must be on it).";
            return;
        }

        var name = NameFor(go);
        var added = 0;
        var hover = go.GetComponentInParent<Hoverable>();
        var text = hover != null ? Localization.instance.Localize(hover.GetHoverText() ?? "") : "";
        var first = StripTags(text.Split('\n')[0]).Trim();
        if (first.Length > 0)
        {
            AddCheck(new Dictionary<string, object?> { ["check"] = "hoverText", ["target"] = name, ["contains"] = first, ["label"] = $"{name} shows '{first}'" });
            added++;
        }

        if (go.GetComponentInParent<Piece>() is { } piece && piece.GetCreator() != 0)
        {
            var owner = CaseCapture.NameOf(piece.GetCreator(), "");
            AddCheck(new Dictionary<string, object?> { ["check"] = "owner", ["target"] = name, ["expect"] = owner, ["label"] = $"{name} built by {owner}" });
            added++;
        }

        if (go.GetComponentInParent<Container>() is { } container && container.GetInventory() is { } inv)
        {
            foreach (var group in inv.GetAllItems().Where(i => i.m_dropPrefab != null).GroupBy(i => i.m_dropPrefab.name))
            {
                var count = group.Sum(i => i.m_stack);
                AddCheck(new Dictionary<string, object?> { ["check"] = "containerHas", ["target"] = name, ["prefab"] = group.Key, ["count"] = count, ["label"] = $"{name} holds {count} {group.Key}" });
                added++;
            }
        }

        LastMessage = added > 0 ? $"Added {added} checks for {name}." : $"Nothing to check on {name}.";
    }

    public static void CheckText(string text)
    {
        if (text.Trim().Length == 0)
            return;
        AddCheck(new Dictionary<string, object?> { ["check"] = "uiVisible", ["name"] = text.Trim(), ["label"] = $"'{text.Trim()}' is on screen" });
        LastMessage = $"Added a check for '{text.Trim()}' on screen.";
    }

    /// <summary>A check where it happens: as a step (it runs at this point of the test).</summary>
    private static void AddCheck(Dictionary<string, object?> check)
    {
        var step = new Dictionary<string, object?> { ["do"] = "check" };
        foreach (var kv in check)
            step[kv.Key] = kv.Value;
        Steps.Add(step);
        Checks.Add(check);
    }

    public static void Screenshot() => Steps.Add(new Dictionary<string, object?> { ["do"] = "screenshot", ["name"] = "step" + (Steps.Count + 1) });

    public static void Note(string text)
    {
        if (text.Trim().Length > 0)
            Steps.Add(new Dictionary<string, object?> { ["do"] = "log", ["message"] = text.Trim() });
    }

    private static string StripTags(string s)
    {
        var sb = new System.Text.StringBuilder();
        var inTag = false;
        foreach (var ch in s)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }

        return sb.ToString();
    }

    // ---------------------------------------------------------------- settings

    private static void HookConfigs()
    {
        UnhookConfigs();
        foreach (var info in Chainloader.PluginInfos.Values)
        {
            if (info.Instance == null || info.Metadata.GUID == TestLabPlugin.GUID)
                continue;
            var guid = info.Metadata.GUID;
            EventHandler<SettingChangedEventArgs> handler = (_, e) => SettingChanged(guid, e);
            info.Instance.Config.SettingChanged += handler;
            ConfigHandlers.Add((info.Instance.Config, handler));
        }
    }

    private static void UnhookConfigs()
    {
        foreach (var (file, handler) in ConfigHandlers)
            file.SettingChanged -= handler;
        ConfigHandlers.Clear();
    }

    private static void SettingChanged(string guid, SettingChangedEventArgs e)
    {
        if (!Active)
            return;
        var key = e.ChangedSetting.Definition.Section + "/" + e.ChangedSetting.Definition.Key;
        var value = e.ChangedSetting.GetSerializedValue();
        // a setting dragged through many values is one change: update the last setConfig of this mod in place
        if (Steps.Count > 0 && Steps[Steps.Count - 1] is { } last && Equals(last["do"], "setConfig") && Equals(last["guid"], guid)
            && last["values"] is Dictionary<string, object?> values)
        {
            values[key] = value;
            return;
        }

        Add(new Dictionary<string, object?> { ["do"] = "setConfig", ["guid"] = guid, ["values"] = new Dictionary<string, object?> { [key] = value } });
    }

    // ---------------------------------------------------------------- UI Toolkit clicks

    /// <summary>Called every frame: hooks clicks on UI Toolkit windows (mod menus) so pressing a button records a click step.</summary>
    public static void Tick()
    {
        if (!Recording || Time.realtimeSinceStartup < _nextUiScan)
            return;
        _nextUiScan = Time.realtimeSinceStartup + 0.5f;
        foreach (var doc in UnityEngine.Object.FindObjectsOfType<UIDocument>())
        {
            var root = doc != null ? doc.rootVisualElement : null;
            if (root == null || !HookedRoots.Add(root))
                continue;
            root.RegisterCallback<ClickEvent>(OnUiClick, TrickleDown.TrickleDown);
        }
    }

    private static void OnUiClick(ClickEvent e)
    {
        if (!Active || e.target is not VisualElement ve)
            return;
        // the clicked thing's own text, or the nearest text up the tree (a switch row, a card)
        for (var cur = ve; cur != null; cur = cur.parent)
        {
            var text = cur switch
            {
                Button b => b.text,
                Toggle t => t.label,
                TextElement t => t.text,
                _ => cur.Q<Label>()?.text ?? ""
            };
            if (!string.IsNullOrWhiteSpace(text))
            {
                Add(new Dictionary<string, object?> { ["do"] = "click", ["button"] = text.Trim() });
                return;
            }
        }
    }

    // ---------------------------------------------------------------- saving

    /// <summary>The recording as a test: its setup (the game when you pressed Record), the steps, and a no-errors check.</summary>
    public static Dictionary<string, object?> BuildTest(string name, string description, bool includeSetup)
    {
        var test = new Dictionary<string, object?> { ["name"] = name };
        if (description.Length > 0)
            test["description"] = description;
        if (includeSetup && Setup != null)
            test["setup"] = Setup;
        test["steps"] = Steps.Select(s => (object?)s).ToList();
        test["asserts"] = new List<object?> { new Dictionary<string, object?> { ["check"] = "noErrors" } };
        return test;
    }

    /// <summary>Save into a suite folder (made with a suite.json when it is new). Returns the file written.</summary>
    public static string Save(string suite, string fileName, Dictionary<string, object?> test)
    {
        var folder = Path.Combine(SuiteLoader.Folder, CaseCapture.Sanitize(suite));
        Directory.CreateDirectory(folder);
        var suiteFile = Path.Combine(folder, "suite.json");
        if (!File.Exists(suiteFile))
        {
            var meta = new Dictionary<string, object?>
            {
                ["name"] = CaseCapture.Sanitize(suite),
                ["description"] = "Recorded with the DrakesTestLab recorder.",
                ["requireMods"] = new List<object?> { new Dictionary<string, object?> { ["guid"] = TestLabPlugin.GUID, ["minVersion"] = TestLabPlugin.Version } },
                ["resetBetweenTests"] = true
            };
            File.WriteAllText(suiteFile, JsonWriter.Write(meta));
        }

        var path = Path.Combine(folder, CaseCapture.Sanitize(fileName) + ".json");
        File.WriteAllText(path, JsonWriter.Write(test));
        return path;
    }
}
