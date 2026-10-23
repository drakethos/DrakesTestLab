using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Util;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>Checks one assert against the running game. Never throws: problems come back as a failed result with a reason.</summary>
internal static class Asserts
{
    public static readonly string[] Names =
    {
        "hasItem", "noItem", "equipped", "globalKey", "objectNear", "owner", "zdo", "uiVisible", "uiText", "logContains",
        "noErrors", "config", "skill", "stat", "position", "guardianPower", "wardAccess", "lastResult", "hook", "message", "unlockShown", "hoverText", "containerHas", "uiEnabled", "hovering", "crosshair"
    };

    public static AssertResult Evaluate(Dictionary<string, object?> a, RunContext ctx)
    {
        var kind = a.First("check", "assert", "is");
        var r = new AssertResult { Description = $"{kind}: {J.Describe(Without(a, "check", "assert", "is"))}" };
        try
        {
            (r.Passed, r.Detail) = Check(kind.ToLowerInvariant(), a, ctx);
        }
        catch (Exception ex)
        {
            r.Passed = false;
            r.Detail = ex.Message;
        }

        return r;
    }

    private static Dictionary<string, object?> Without(Dictionary<string, object?> d, params string[] keys)
    {
        var copy = new Dictionary<string, object?>(d, StringComparer.OrdinalIgnoreCase);
        foreach (var k in keys) copy.Remove(k);
        return copy;
    }

    private static Player P => Player.m_localPlayer ?? throw new InvalidOperationException("no player");

    private static (bool, string) Check(string kind, Dictionary<string, object?> a, RunContext ctx)
    {
        switch (kind)
        {
            case "hasitem":
            {
                var (count, detail) = CountItems(a);
                var min = (int)a.Num("count", 1);
                return (count >= min, $"found {count} (need {min}){detail}");
            }
            case "noitem":
            {
                var (count, _) = CountItems(a);
                return (count == 0, $"found {count}");
            }
            case "equipped":
            {
                var prefab = a.Str("prefab");
                foreach (var item in P.GetInventory().GetEquippedItems())
                {
                    if (item.m_dropPrefab != null && item.m_dropPrefab.name.Equals(prefab, StringComparison.OrdinalIgnoreCase))
                        return (true, "equipped");
                }

                return (false, "not equipped");
            }
            case "globalkey":
            {
                var key = a.Str("key");
                var has = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key);
                var want = a.Bool("expect", true);
                return (has == want, has ? "key is set" : "key is not set");
            }
            case "objectnear":
            {
                var target = a.First("target", "prefab");
                var around = Targets.PointFrom(a, P.transform.position);
                var radius = a.Num("radius", 10f);
                int count;
                if (ctx.Tags.TryGetValue(target, out var tagged))
                    count = tagged != null && Vector3.Distance(tagged.transform.position, around) <= radius ? 1 : 0;
                else
                    count = Targets.Count(target, around, radius);

                if (a.Has("count"))
                    return (count == (int)a.Num("count"), $"found {count} within {radius} m");
                var want = a.Bool("expect", true);
                return ((count > 0) == want, $"found {count} within {radius} m");
            }
            case "owner":
            {
                var go = Targets.Find(ctx, a.Str("target")) ?? throw new InvalidOperationException($"'{a.Str("target")}' not found");
                var piece = go.GetComponent<Piece>() ?? throw new InvalidOperationException("not a building piece");
                var want = WardTools.Parse(a.Str("expect", "me"));
                return (piece.GetCreator() == want.Id, $"creator id {piece.GetCreator()}, expected {want}");
            }
            case "zdo":
            {
                var go = Targets.Find(ctx, a.Str("target")) ?? throw new InvalidOperationException($"'{a.Str("target")}' not found");
                var zdo = go.GetComponent<ZNetView>()?.GetZDO() ?? throw new InvalidOperationException("no saved data on that object");
                var key = a.Str("key");
                var actual = ReadZdo(zdo, key);
                if (actual.Length == 0 && a.Has("default"))
                    actual = a.Str("default");
                return (Matches(actual, a), $"{key} = '{actual}'");
            }
            case "uivisible":
            {
                var visible = a.Bool("exact", false) ? UiDriver.IsVisibleExact(a.First("name", "target")) : UiDriver.IsVisible(a.First("name", "target"));
                var want = a.Bool("expect", true);
                return (visible == want, visible ? "visible" : "not visible");
            }
            case "uitext":
            {
                var text = UiDriver.TextOf(a.First("name", "target"));
                return (Matches(text, a), $"text is '{text}'");
            }
            case "logcontains":
            {
                var found = FindLog(ctx, a.Str("source"), a.Str("text"), errorsOnly: false);
                var want = a.Bool("expect", true);
                return ((found != null) == want, found ?? "no matching log line since the test started");
            }
            case "noerrors":
            {
                var found = FindLog(ctx, a.Str("source"), "", errorsOnly: true);
                return (found == null, found ?? "no errors since the test started");
            }
            case "config":
            {
                var entry = ModEnvironment.FindEntry(a.Str("guid"), a.Str("section"), a.Str("key"))
                            ?? throw new InvalidOperationException("config entry not found");
                var value = entry.GetSerializedValue();
                return (Matches(value, a), $"value is '{value}'");
            }
            case "skill":
            {
                var name = a.Str("name");
                if (!Enum.TryParse<Skills.SkillType>(name, true, out var type))
                    throw new InvalidOperationException($"unknown skill '{name}'");
                var level = P.GetSkills().GetSkillLevel(type);
                return (InRange(level, a), $"{name} is {level:0.#}");
            }
            case "stat":
            {
                var p = P;
                var name = a.Str("name").ToLowerInvariant();
                float value = name switch
                {
                    "health" => p.GetHealth(),
                    "maxhealth" => p.GetMaxHealth(),
                    "stamina" => p.GetStamina(),
                    "maxstamina" => p.GetMaxStamina(),
                    "eitr" => p.GetEitr(),
                    "weight" => p.GetInventory().GetTotalWeight(),
                    "carryweight" => p.GetMaxCarryWeight(),
                    _ => throw new InvalidOperationException($"unknown stat '{name}' (health, maxHealth, stamina, maxStamina, eitr, weight, carryWeight)")
                };
                return (InRange(value, a), $"{name} is {value:0.##}");
            }
            case "position":
            {
                Vector3 at;
                var target = a.Str("target");
                if (target.Length > 0)
                    at = (Targets.Find(ctx, target) ?? throw new InvalidOperationException($"'{target}' not found")).transform.position;
                else
                    at = new Vector3(a.Num("x"), 0f, a.Num("z"));
                var pos = P.transform.position;
                var d = Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(at.x, at.z));
                var radius = a.Num("radius", 3f);
                return (d <= radius, $"{d:0.0} m away (allowed {radius})");
            }
            case "guardianpower":
            {
                var power = P.GetGuardianPowerName() ?? "";
                return (Matches(power, a), $"power is '{power}'");
            }
            case "wardaccess":
            {
                var access = WardTools.HaveAccessHere();
                var want = a.Bool("expect", true);
                return (access == want, access ? "access allowed" : "access blocked");
            }
            case "lastresult":
            {
                var text = Convert.ToString(ctx.LastResult, CultureInfo.InvariantCulture) ?? "null";
                return (Matches(text, a), $"result is '{text}'");
            }
            case "message":
            {
                var found = MessageLog.Find(ctx.StartedAt, a.Str("contains", a.Str("text")), a.Str("kind"));
                var want = a.Bool("expect", true);
                return ((found != null) == want, found != null ? $"[{found.Kind}] {found.Text}" : "no matching message since the test started");
            }
            case "unlockshown":
            {
                var found = MessageLog.Find(ctx.StartedAt, a.Str("contains", a.Str("text")), "Unlock");
                var want = a.Bool("expect", true);
                return ((found != null) == want, found != null ? found.Text : "no matching unlock pop-up since the test started");
            }
            case "hovertext":
            {
                var go = Targets.Find(ctx, a.Str("target")) ?? throw new InvalidOperationException($"'{a.Str("target")}' not found");
                var hover = go.GetComponentInChildren<Hoverable>() ?? go.GetComponentInParent<Hoverable>()
                            ?? throw new InvalidOperationException("that object has no hover text");
                var raw = hover.GetHoverText() ?? "";
                var text = Localization.instance != null ? Localization.instance.Localize(raw) : raw;
                return (Matches(text, a), $"hover text '{text.Replace("\n", " | ")}'");
            }
            case "containerhas":
            {
                var go = Targets.Find(ctx, a.Str("target")) ?? throw new InvalidOperationException($"'{a.Str("target")}' not found");
                var container = go.GetComponentInChildren<Container>() ?? throw new InvalidOperationException("not a container");
                var prefab = a.Str("prefab");
                var count = 0;
                foreach (var item in container.GetInventory().GetAllItems())
                {
                    var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
                    if (prefab.Length == 0 || name.Equals(prefab, StringComparison.OrdinalIgnoreCase))
                        count += item.m_stack;
                }

                if (a.Has("count"))
                    return (count >= (int)a.Num("count"), $"contains {count}");
                var want = a.Bool("expect", true);
                return ((count > 0) == want, $"contains {count}");
            }
            case "hovering":
            {
                var target = a.First("target", "name");
                var want = a.Bool("expect", true);
                var hover = Player.m_localPlayer != null ? Player.m_localPlayer.GetHoverObject() : null;
                var go = target.Length > 0 ? Targets.Find(ctx, target) : null;
                var on = hover != null && (go == null ? target.Length == 0 : hover == go || hover.transform.IsChildOf(go.transform) || go.transform.IsChildOf(hover.transform));
                return (on == want, hover != null ? $"hovering {hover.name}" : "hovering nothing");
            }
            case "crosshair":
            {
                var text = UiSteps.CrosshairText();
                return (Matches(text, a), $"crosshair shows '{text.Replace("\n", " | ")}'" + (text.Length == 0 ? UiSteps.HudTextsForReport() : ""));
            }
            case "uienabled":
            {
                var enabled = UiDriver.IsEnabled(a.First("name", "target"))
                              ?? throw new InvalidOperationException($"no visible element '{a.First("name", "target")}'");
                var want = a.Bool("expect", true);
                return (enabled == want, enabled ? "enabled" : "disabled");
            }
            case "hook":
            {
                var args = a.Obj("args") ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var value = TestHooks.Call(a.Str("name"), args, ctx);
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
                var ok = a.Has("expect") || a.Has("equals") || a.Has("contains") || a.Has("min") || a.Has("max")
                    ? Matches(text, a)
                    : TestHooks.Truthy(value);
                return (ok, $"hook returned '{text}'");
            }
            default:
                throw new InvalidOperationException($"unknown check '{kind}' (known: {string.Join(", ", Names)})");
        }
    }

    private static (int count, string detail) CountItems(Dictionary<string, object?> a)
    {
        var prefab = a.Str("prefab");
        var quality = (int)a.Num("quality", -1);
        var data = a.Obj("data");
        // "crafter": "me" / "Alice" / "Name:123" = made by that player; "none" = unowned (given, looted)
        var crafter = a.Str("crafter");
        var maker = crafter.Length > 0 && !crafter.Equals("none", StringComparison.OrdinalIgnoreCase) ? WardTools.Parse(crafter) : null;
        var count = 0;
        foreach (var item in P.GetInventory().GetAllItems())
        {
            var name = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
            if (!name.Equals(prefab, StringComparison.OrdinalIgnoreCase))
                continue;
            if (quality > 0 && item.m_quality != quality)
                continue;
            if (data != null && !DataMatches(item, data))
                continue;
            if (crafter.Length > 0 && (maker == null ? item.m_crafterID != 0 : item.m_crafterID != maker.Id))
                continue;
            count += item.m_stack;
        }

        var detail = (data != null ? " with matching custom data" : "") + (crafter.Length > 0 ? $" crafted by {crafter}" : "");
        return (count, detail);
    }

    private static bool DataMatches(ItemDrop.ItemData item, Dictionary<string, object?> data)
    {
        foreach (var kv in data)
        {
            if (!item.m_customData.TryGetValue(kv.Key, out var value))
                return false;
            var want = Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? "";
            if (want != "*" && !value.Equals(want, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string ReadZdo(ZDO zdo, string key)
    {
        var s = zdo.GetString(key, "\u0001");
        if (s != "\u0001")
            return s;
        var l = zdo.GetLong(key, long.MinValue);
        if (l != long.MinValue)
            return l.ToString(CultureInfo.InvariantCulture);
        var i = zdo.GetInt(key, int.MinValue);
        if (i != int.MinValue)
            return i.ToString(CultureInfo.InvariantCulture);
        var f = zdo.GetFloat(key, float.NaN);
        if (!float.IsNaN(f))
            return f.ToString(CultureInfo.InvariantCulture);
        return "";
    }

    private static string? FindLog(RunContext ctx, string source, string text, bool errorsOnly)
    {
        var capture = LogCapture.Instance;
        capture.Drain();
        foreach (var e in capture.Entries)
        {
            if (e.Time < ctx.StartedAt)
                continue;
            if (errorsOnly && (e.Level & (LogLevel.Error | LogLevel.Fatal)) == 0)
                continue;
            if (source.Length > 0 && !e.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                continue;
            if (text.Length > 0 && e.Message.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            var msg = e.Message.Length > 200 ? e.Message.Substring(0, 200) + "..." : e.Message;
            return $"[{e.Level}:{e.Source}] {msg}";
        }

        return null;
    }

    /// <summary>expect (equals; numbers compared as numbers, true/false as booleans), contains, min, max.</summary>
    public static bool Matches(string actual, Dictionary<string, object?> a)
    {
        if (a.Has("notContains"))
        {
            if (actual.IndexOf(a.Str("notContains"), StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (!a.Has("contains") && !a.Has("expect") && !a.Has("equals"))
                return true;
        }

        if (a.Has("contains"))
            return actual.IndexOf(a.Str("contains"), StringComparison.OrdinalIgnoreCase) >= 0;

        if (a.Has("min") || a.Has("max"))
            return float.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && InRange(n, a);

        if (!a.Has("expect") && !a.Has("equals"))
            return actual.Length > 0;

        var want = a.Has("expect") ? a.Str("expect") : a.Str("equals");
        if (float.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return Mathf.Abs(x - y) <= 0.001f + Mathf.Abs(y) * 0.0001f;
        return actual.Trim().Equals(want.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool InRange(float value, Dictionary<string, object?> a)
    {
        if (a.Has("min") && value < a.Num("min"))
            return false;
        if (a.Has("max") && value > a.Num("max"))
            return false;
        if (a.Has("expect") && !a.Has("min") && !a.Has("max"))
            return Mathf.Abs(value - a.Num("expect")) <= 0.01f;
        return true;
    }
}
