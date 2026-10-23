using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>Keys a test holds down (holdKey) until released or the test ends. Read by every keyboard check the game and mods use.</summary>
internal static class HeldKeys
{
    private static readonly HashSet<KeyCode> Held = new();
    private static readonly Dictionary<KeyCode, int> DownFrame = new();

    public static bool Any => Held.Count > 0;

    /// <summary>"Shift", "Ctrl", "Alt" mean the left one; anything else is a Unity KeyCode name.</summary>
    public static KeyCode Parse(string name) => name.Trim().ToLowerInvariant() switch
    {
        "shift" => KeyCode.LeftShift,
        "ctrl" or "control" => KeyCode.LeftControl,
        "alt" => KeyCode.LeftAlt,
        _ => Enum.TryParse<KeyCode>(name.Trim(), true, out var k) ? k : throw new StepFailed($"unknown key '{name}'")
    };

    public static void Hold(KeyCode key)
    {
        if (Held.Add(key))
            DownFrame[key] = Time.frameCount + 1;
    }

    public static void Release(KeyCode key)
    {
        Held.Remove(key);
        DownFrame.Remove(key);
    }

    public static void ReleaseAll()
    {
        Held.Clear();
        DownFrame.Clear();
    }

    public static bool IsHeld(KeyCode key)
    {
        if (Held.Contains(key))
            return true;
        // a held Shift/Ctrl/Alt counts for either side
        return key switch
        {
            KeyCode.RightShift => Held.Contains(KeyCode.LeftShift),
            KeyCode.RightControl => Held.Contains(KeyCode.LeftControl),
            KeyCode.RightAlt => Held.Contains(KeyCode.LeftAlt),
            _ => false
        };
    }

    public static bool IsDown(KeyCode key) => DownFrame.TryGetValue(key, out var f) && f == Time.frameCount;
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKey), typeof(KeyCode))]
internal static class Input_GetKey_Patch
{
    private static void Postfix(KeyCode key, ref bool __result)
    {
        if (!__result && (HeldKeys.IsHeld(key) || InputInjector.KeyHeld(key)))
            __result = true;
    }
}

[HarmonyPatch(typeof(Input), nameof(Input.GetKeyDown), typeof(KeyCode))]
internal static class Input_GetKeyDown_Patch
{
    private static void Postfix(KeyCode key, ref bool __result)
    {
        if (!__result && (HeldKeys.IsDown(key) || InputInjector.KeyDown(key)))
            __result = true;
    }
}

/// <summary>
/// Mods that check their menu modifier through a small helper of their own (inlined Input.GetKey calls are not always
/// patchable): optional targets by name, no reference to those mods.
/// </summary>
[HarmonyPatch]
internal static class ModKeyHelper_Patch
{
    private static readonly (string type, string method)[] Targets = { ("DrakeModsLibs.Input.MenuKeyBinding", "IsTokenHeld") };

    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var (type, method) in Targets)
        {
            var m = AccessTools.TypeByName(type) is { } t ? AccessTools.Method(t, method, new[] { typeof(string) }) : null;
            if (m != null)
                yield return m;
        }
    }

    private static bool Prepare() => TargetMethods().Any();

    private static void Postfix(string token, ref bool __result)
    {
        if (__result || !HeldKeys.Any)
            return;
        try
        {
            __result = HeldKeys.IsHeld(HeldKeys.Parse(token));
        }
        catch (StepFailed)
        {
            // not a key name
        }
    }
}

/// <summary>The language a test switched to, put back when the test ends.</summary>
internal static class LanguageSwitch
{
    private static string? _original;

    public static void Set(string language)
    {
        var loc = Localization.instance ?? throw new StepFailed("no localization yet");
        _original ??= loc.GetSelectedLanguage();
        loc.SetLanguage(language);
    }

    public static void Restore()
    {
        if (_original == null || Localization.instance == null)
            return;
        Localization.instance.SetLanguage(_original);
        _original = null;
    }
}

internal static class UiSteps
{
    private static Player P => Player.m_localPlayer ?? throw new StepFailed("no player");

    /// <summary>holdKey: key ("Shift", "LeftControl", "F1"...), on (default true). Held keys are released when the test ends.</summary>
    public static void HoldKey(Dictionary<string, object?> step)
    {
        foreach (var name in step.First("key", "keys").Split('+', ','))
        {
            if (name.Trim().Length == 0)
                continue;
            var key = HeldKeys.Parse(name);
            if (step.Bool("on", true))
                HeldKeys.Hold(key);
            else
                HeldKeys.Release(key);
        }
    }

    /// <summary>
    /// itemClick: click an inventory slot like a player. prefab (+ index / match on the shown name), button = right (default) or left,
    /// mods = "Shift" / "Ctrl+Alt" held during the click. Opens the inventory first.
    /// </summary>
    public static IEnumerator ItemClick(Dictionary<string, object?> step, RunContext ctx)
    {
        var gui = InventoryGui.instance ?? throw new StepFailed("no inventory window");
        if (!InventoryGui.IsVisible())
        {
            gui.Show(null);
            yield return null;
            yield return null;
        }

        var item = FindItem(step);
        var grid = gui.m_playerGrid ?? throw new StepFailed("no player grid");
        var mods = step.Str("mods").Split('+', ',').Where(s => s.Trim().Length > 0).Select(HeldKeys.Parse).ToList();
        foreach (var k in mods)
            HeldKeys.Hold(k);
        yield return null; // the key is down from the next frame on

        try
        {
            var right = !step.Str("button", "right").Equals("left", StringComparison.OrdinalIgnoreCase);
            var field = AccessTools.Field(typeof(InventoryGrid), right ? "m_onRightClick" : "m_onSelected");
            if (field?.GetValue(grid) is not Delegate handler)
                throw new StepFailed($"the inventory grid has no {(right ? "right-click" : "click")} handler");
            var ps = handler.Method.GetParameters();
            var args = new object?[ps.Length];
            for (var i = 0; i < ps.Length; i++)
            {
                var t = ps[i].ParameterType;
                args[i] = t == typeof(InventoryGrid) ? grid
                    : t == typeof(ItemDrop.ItemData) ? item
                    : t == typeof(Vector2i) ? item.m_gridPos
                    : t.IsEnum ? Enum.ToObject(t, 0)
                    : null;
            }

            // closures from patches (like RenameIt's) take (grid, item, pos); vanilla's left-click also takes a mode
            handler.DynamicInvoke(args.Length == ps.Length ? args : null);
            ctx.Log($"  {(right ? "right" : "left")}-clicked {item.m_shared.m_name}{(mods.Count > 0 ? " with " + string.Join("+", mods) : "")}");
        }
        finally
        {
            foreach (var k in mods)
                HeldKeys.Release(k);
        }

        yield return null;
    }

    /// <summary>
    /// place: build a piece the way a player does with a build tool (default the Hammer, equipped for you): the piece must be in the
    /// tool's build menu and enabled; free = false (default true) needs the materials (and the station nearby) and uses them up.
    /// prefab, dz (default 2.5), dx, yaw (degrees, default facing you), tool, as, keep. Fails like the game would refuse.
    /// </summary>
    public static IEnumerator Place(Dictionary<string, object?> step, RunContext ctx)
    {
        var p = P;
        var prefab = Need(step, "prefab");
        var toolName = step.Str("tool", "Hammer");
        var inv = p.GetInventory();
        var tool = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
        {
            Util.ItemGiver.Give(toolName, 1);
            tool = inv.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new StepFailed($"no build tool '{toolName}'");
        }

        if (!p.IsItemEquiped(tool))
            p.EquipItem(tool, false);
        yield return null;

        var table = tool.m_shared.m_buildPieces ?? throw new StepFailed($"'{toolName}' is not a build tool");
        var pieceGo = table.m_pieces.FirstOrDefault(g => g != null && g.name.Equals(prefab, StringComparison.OrdinalIgnoreCase))
                      ?? throw new StepFailed($"'{prefab}' is not in the {toolName} build menu");
        var piece = pieceGo.GetComponent<Piece>() ?? throw new StepFailed($"'{prefab}' is not a piece");
        if (!piece.m_enabled)
            throw new StepFailed($"'{prefab}' is in the menu but switched off (not placeable)");

        var free = step.Bool("free", true);
        if (!free && !HaveBuildRequirements(p, piece))
            throw new StepFailed($"missing requirements to build '{prefab}' (materials or crafting station)");

        var forward = p.transform.forward;
        var right = p.transform.right;
        var pos = p.transform.position + forward * step.Num("dz", 2.5f) + right * step.Num("dx", 0f);
        if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(pos, out var ground))
            pos.y = ground;
        var yaw = step.Has("yaw") ? step.Num("yaw") : p.transform.eulerAngles.y + 180f;
        var rot = Quaternion.Euler(0f, yaw, 0f);

        var before = new HashSet<int>(Targets.AllViews().Select(v => v.GetInstanceID()));
        var placeMethod = AccessTools.Method(typeof(Player), "PlacePiece") ?? throw new StepFailed("Player.PlacePiece not found");
        var ps = placeMethod.GetParameters();
        var args = new object[ps.Length];
        args[0] = piece;
        args[1] = pos;
        args[2] = rot;
        for (var i = 3; i < ps.Length; i++)
            args[i] = ps[i].ParameterType == typeof(bool) ? false : (ps[i].HasDefaultValue ? ps[i].DefaultValue! : null!);
        placeMethod.Invoke(p, args);
        if (!free)
            AccessTools.Method(typeof(Player), "ConsumeResources")?.Invoke(p, new object[] { piece.m_resources, 0, -1, 1 });
        yield return null;

        GameObject? made = null;
        var bestD = 1.5f;
        foreach (var v in Targets.AllViews())
        {
            if (before.Contains(v.GetInstanceID()) || !Utils.GetPrefabName(v.gameObject).Equals(prefab, StringComparison.OrdinalIgnoreCase))
                continue;
            var d = Vector3.Distance(v.transform.position, pos);
            if (d < bestD)
            {
                made = v.gameObject;
                bestD = d;
            }
        }

        if (made == null)
            throw new StepFailed($"placing '{prefab}' did not create it");
        var tag = step.First("as", "tag");
        if (tag.Length > 0)
            ctx.Tags[tag] = made;
        if (!step.Bool("keep"))
            ctx.Spawned.Add(made);
        // like a player putting the hammer away: build mode has its own camera and no hover
        if (!step.Bool("holdTool") && p.IsItemEquiped(tool))
            p.UnequipItem(tool, false);
        ctx.Log($"  built {prefab}{(free ? " (free)" : "")}");
    }

    private static bool HaveBuildRequirements(Player p, Piece piece)
    {
        var mode = AccessTools.Inner(typeof(Player), "RequirementMode");
        var have = mode != null ? AccessTools.Method(typeof(Player), "HaveRequirements", new[] { typeof(Piece), mode }) : null;
        if (have == null)
            return true;
        return have.Invoke(p, new[] { piece, Enum.Parse(mode!, "CanBuild") }) is true;
    }

    /// <summary>drop: put an inventory stack on the ground dz (default 1.5) m in front, tagged "as" (removed when the test ends).</summary>
    public static void Drop(Dictionary<string, object?> step, RunContext ctx)
    {
        var p = P;
        var item = FindItem(step);
        p.UnequipItem(item, false);
        p.GetInventory().RemoveItem(item);
        var pos = p.transform.position + p.transform.forward * step.Num("dz", 1.5f) + Vector3.up * 0.2f;
        var drop = ItemDrop.DropItem(item, item.m_stack, pos, Quaternion.identity) ?? throw new StepFailed("could not drop the item");
        if (drop.GetComponent<Rigidbody>() is { } body)
            body.velocity = Vector3.zero;
        drop.m_autoPickup = false; // a test's dropped item stays on the ground (you stand right next to it)
        var tag = step.First("as", "tag");
        if (tag.Length > 0)
            ctx.Tags[tag] = drop.gameObject;
        if (!step.Bool("keep"))
            ctx.Spawned.Add(drop.gameObject);
    }

    private static ItemDrop.ItemData FindItem(Dictionary<string, object?> step)
    {
        var prefab = Need(step, "prefab");
        var match = step.Str("match");
        var list = P.GetInventory().GetAllItems()
            .Where(i => i?.m_shared != null && ((i.m_dropPrefab != null && i.m_dropPrefab.name.Equals(prefab, StringComparison.OrdinalIgnoreCase))
                                                || i.m_shared.m_name.Equals(prefab, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(i => i.m_gridPos.y).ThenBy(i => i.m_gridPos.x).ToList();
        if (match.Length > 0)
            list = list.Where(i => Localization.instance.Localize(i.m_shared.m_name).IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0
                                   || (i.m_customData != null && i.m_customData.Values.Any(v => v.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0))).ToList();
        var index = (int)step.Num("index", 0);
        if (list.Count <= index)
            throw new StepFailed($"no inventory stack '{prefab}'{(match.Length > 0 ? $" matching '{match}'" : "")} (index {index})");
        return list[index];
    }

    /// <summary>
    /// lookAt: turn the view onto a target (tag or prefab) and wait until the game itself hovers it, like a player aiming the
    /// crosshair. Walks up to `distance` (default 2 m) in front of it first. timeout (default 3 s).
    /// </summary>
    public static IEnumerator LookAt(Dictionary<string, object?> step, RunContext ctx)
    {
        var name = step.First("target", "to");
        var target = Targets.Find(ctx, name) ?? throw new StepFailed($"lookAt target '{name}' not found");
        var p = P;
        var center = Center(target);

        // stand "distance" metres from the object's own position (what the game's use-distance checks measure), aim at its middle.
        // When something is in the way from that side (a wall), walk round and try the other sides.
        var distance = step.Num("distance", 2f);
        var anchor = target.transform.position;
        var flat = p.transform.position - anchor;
        flat.y = 0f;
        var startDir = flat.sqrMagnitude > 0.01f ? flat.normalized : -p.transform.forward;
        var inRange = flat.magnitude <= distance + 0.5f && flat.magnitude >= 0.6f;
        var perSide = step.Num("timeout", 3f) / 4f;
        GameObject? hover = null;
        for (var side = 0; side < 4; side++)
        {
            if (side > 0 || !inRange)
            {
                var dir = Quaternion.Euler(0f, 90f * side, 0f) * startDir;
                MoveTo(p, anchor + dir * distance);
                yield return null;
            }

            var deadline = Time.realtimeSinceStartup + Mathf.Max(0.75f, perSide);
            var steady = 0;
            while (Time.realtimeSinceStartup <= deadline)
            {
                // aim from the camera, which sits above and behind the player (and settles for a few frames after a move)
                var cam = GameCamera.instance != null ? GameCamera.instance.transform.position : p.GetEyePoint();
                Aim(p, (center - cam).normalized);
                yield return null;

                hover = p.GetHoverObject();
                if (hover != null && (hover == target || hover.transform.IsChildOf(target.transform) || target.transform.IsChildOf(hover.transform)))
                {
                    // done once the hover holds for a few frames: the camera has settled and the HUD shows the crosshair text
                    if (++steady >= 4)
                    {
                        ctx.Log($"  looking at {target.name}");
                        yield break;
                    }
                    continue;
                }
                steady = 0;

                // things without a hover (walls, floors) only need to be in the crosshair's line of sight
                if (CameraHits(target))
                {
                    ctx.Log($"  aiming at {target.name}");
                    yield break;
                }
            }
        }

        throw new StepFailed($"could not aim at '{name}' from any side (hovering {(hover != null ? hover.name : "nothing")})");
    }

    /// <summary>Puts the player on whatever floor is under that spot (terrain, a stone or wood floor), not at the old height.</summary>
    private static void MoveTo(Player p, Vector3 spot)
    {
        spot.y = p.transform.position.y;
        var floorMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small");
        if (Physics.Raycast(spot + Vector3.up * 4f, Vector3.down, out var floor, 12f, floorMask, QueryTriggerInteraction.Ignore))
            spot.y = floor.point.y;
        else if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(spot, out var ground))
            spot.y = Mathf.Max(spot.y, ground);
        p.transform.position = spot;
        if (p.GetComponent<Rigidbody>() is { } body)
        {
            body.position = spot;
            body.velocity = Vector3.zero;
        }
    }

    private static readonly System.Reflection.FieldInfo? LookYaw = AccessTools.Field(typeof(Player), "m_lookYaw");
    private static readonly System.Reflection.FieldInfo? LookPitch = AccessTools.Field(typeof(Player), "m_lookPitch");

    /// <summary>
    /// Point the player's view along <paramref name="dir"/> the way the mouse does: Valheim rebuilds the look every frame from its
    /// stored yaw + pitch, so those are set and then a zero mouse move applies them (eye, look direction, camera).
    /// </summary>
    public static void Aim(Player p, Vector3 dir)
    {
        var flat = new Vector3(dir.x, 0f, dir.z);
        if (flat.sqrMagnitude > 0.0001f)
        {
            var yaw = Quaternion.LookRotation(flat.normalized);
            p.transform.rotation = yaw;
            LookYaw?.SetValue(p, yaw);
        }

        var pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, -89f, 89f);
        LookPitch?.SetValue(p, pitch);
        p.SetMouseLook(Vector2.zero);
    }

    private static bool CameraHits(GameObject target)
    {
        var cam = GameCamera.instance != null ? GameCamera.instance.transform : null;
        if (cam == null)
            return false;
        var hits = Physics.RaycastAll(cam.position, cam.forward, 12f);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); // RaycastAll is not ordered
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.GetComponentInParent<Player>() != null)
                continue;
            var t = hit.collider.transform;
            return t == target.transform || t.IsChildOf(target.transform);
        }

        return false;
    }

    private static Vector3 Center(GameObject go)
    {
        var col = go.GetComponentInChildren<Collider>();
        return col != null ? col.bounds.center : go.transform.position + Vector3.up * 0.3f;
    }

    private static IEnumerable<string> HoverToolkitTexts(Hud hud)
    {
        // panels the HUD itself owns, plus any UI document whose object is named like a hover panel
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var roots = new List<UnityEngine.UIElements.VisualElement>();
        foreach (var f in typeof(Hud).GetFields(all))
        {
            var value = f.GetValue(hud);
            if (value is UnityEngine.UIElements.UIDocument doc && doc != null && doc.rootVisualElement != null)
                roots.Add(doc.rootVisualElement);
            else if (value is UnityEngine.UIElements.VisualElement ve)
                roots.Add(ve);
        }
        foreach (var doc in UnityEngine.Object.FindObjectsOfType<UnityEngine.UIElements.UIDocument>())
        {
            if (doc != null && doc.isActiveAndEnabled && doc.rootVisualElement != null && doc.gameObject.name.IndexOf("hover", StringComparison.OrdinalIgnoreCase) >= 0)
                roots.Add(doc.rootVisualElement);
        }

        var seen = new HashSet<UnityEngine.UIElements.TextElement>();
        foreach (var root in roots)
        {
            foreach (var t in UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.TextElement>(root).ToList())
            {
                if (seen.Add(t) && !string.IsNullOrWhiteSpace(t.text) && UitkDriver.Shown(t))
                    yield return t.text;
            }
        }
    }

    /// <summary>For a report when the crosshair reads empty: the texts shown on the HUD and where they sit.</summary>
    public static string HudTextsForReport()
    {
        var all = UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>()
            .Where(t => t != null && t.isActiveAndEnabled && !string.IsNullOrWhiteSpace(t.text))
            .Select(t =>
            {
                var path = t.name;
                for (var cur = t.transform.parent; cur != null; cur = cur.parent)
                    path = cur.name + "/" + path;
                return $"{path} = '{t.text.Replace("\n", " ")}'";
            })
            .Where(line => line.IndexOf("IngameGui", StringComparison.Ordinal) >= 0 && line.IndexOf("HotKeyBar", StringComparison.Ordinal) < 0)
            .Concat(UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>()
                .Where(t => t != null && t.isActiveAndEnabled && !string.IsNullOrWhiteSpace(t.text))
                .Select(t => $"text:{t.transform.parent?.name}/{t.name} = '{t.text.Replace("\n", " ")}'"))
            .Concat(UitkDriver.ShownTexts())
            .Take(16)
            .ToList();
        return all.Count == 0 ? " (no texts on screen)" : " (texts on screen: " + string.Join("; ", all) + ")";
    }

    /// <summary>What the crosshair shows right now (the game's hover name text), rich text stripped.</summary>
    public static string CrosshairText()
    {
        var hud = Hud.instance;
        if (hud == null)
            return "";
        var field = AccessTools.Field(typeof(Hud), "m_hoverName");
        var raw = field?.GetValue(hud) switch
        {
            TMPro.TMP_Text t when t.isActiveAndEnabled => t.text,
            UnityEngine.UI.Text t when t.isActiveAndEnabled => t.text,
            _ => ""
        } ?? "";
        // Valheim 1.0 shows pieces (workbench "Use [E]") in a newer hover panel: read every shown text under a "hover" object
        if (raw.Trim().Length == 0)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var t in hud.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (t == null || !t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text) || ReferenceEquals(t, field?.GetValue(hud)))
                    continue;
                for (var cur = t.transform; cur != null && cur != hud.transform; cur = cur.parent)
                {
                    if (cur.name.IndexOf("hover", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    parts.Add(t.text);
                    break;
                }
            }
            raw = string.Join("\n", parts);
        }

        // ...or a UI Toolkit hover panel that replaces the game's (DrakeModsLibs' "drakes_world_hover"): a document named like "hover"
        if (raw.Trim().Length == 0)
            raw = string.Join("\n", HoverToolkitTexts(hud));
        var sb = new System.Text.StringBuilder();
        var inTag = false;
        foreach (var ch in raw)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string Need(Dictionary<string, object?> step, string key)
    {
        var v = step.Str(key);
        if (v.Length == 0)
            throw new StepFailed($"needs \"{key}\"");
        return v;
    }
}
