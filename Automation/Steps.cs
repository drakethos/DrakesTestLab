using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Cases;
using DrakesTestLab.Util;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLab.Automation;

internal sealed class StepFailed : Exception
{
    public StepFailed(string message) : base(message)
    {
    }
}

/// <summary>The built-in step names. Kept apart from <see cref="Steps"/> so the suite loader can read them without game types.</summary>
internal static class StepNames
{
    public static readonly string[] All =
    {
        "teleport", "face", "wait", "spawn", "give", "craft", "clearInventory", "console", "playAs", "interact", "click", "toggle",
        "type", "pressKey", "closeUi", "invoke", "hook", "check", "checkpoint", "screenshot", "log", "case", "setConfig", "reset", "relog", "destroy", "holdKey", "releaseKeys", "itemClick", "lookAt", "setLanguage", "drop", "place", "use"
    };
}

/// <summary>
/// Runs one test step. Instant steps do their work inside <see cref="Execute"/>; steps that take time return a coroutine.
/// A failure throws <see cref="StepFailed"/> with a message for the report.
/// </summary>
internal static class Steps
{
    public static string[] Names => StepNames.All;

    private static Player P => Player.m_localPlayer ?? throw new StepFailed("no player");

    /// <summary>
    /// A check in the middle of the steps: recorded with the test's checks. A failed check fails the test but the steps go on,
    /// unless "stop": true. "screenshot": true saves a picture right after it.
    /// </summary>
    private static IEnumerator? CheckNow(Dictionary<string, object?> step, RunContext ctx)
    {
        var r = Asserts.Evaluate(step, ctx);
        if (step.Has("label"))
            r.Description = step.Str("label") + ": " + r.Description;
        ctx.Result.Asserts.Add(r);
        ctx.Log($"  check {(r.Passed ? "ok" : "FAILED")}: {r.Description} -> {r.Detail}");
        if (step.Bool("screenshot"))
            TestRunner.Screenshot(ctx, step.Str("label", "check"));
        if (!r.Passed && step.Bool("stop"))
            throw new StepFailed($"{r.Description} -> {r.Detail}");
        return null;
    }

    /// <summary>
    /// Crafts through the game's own crafting code, so the item gets a real crafter (owner) like a player made it.
    /// "by": craft as someone else (Alice, Bob, Name:123), then switch back. "free": false = needs the materials
    /// (and the station level); default true = no cost. "count" crafts several times.
    /// </summary>
    private static void Craft(Dictionary<string, object?> step)
    {
        var prefab = Need(step, "prefab");
        var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
        var drop = go != null ? go.GetComponent<ItemDrop>() : null;
        if (drop == null)
            throw new StepFailed($"'{prefab}' is not an item");
        var recipe = ObjectDB.instance!.GetRecipe(drop.m_itemData) ?? throw new StepFailed($"'{prefab}' has no recipe");
        var gui = InventoryGui.instance ?? throw new StepFailed("no inventory window yet");

        var by = step.Str("by");
        var previous = WardTools.Me();
        if (by.Length > 0)
            WardTools.PlayAs(WardTools.Parse(by), applyLook: false);

        var player = P;
        var noCost = AccessTools.Field(typeof(Player), "m_noPlacementCost");
        var oldNoCost = noCost?.GetValue(player);
        var doCrafting = AccessTools.Method(typeof(InventoryGui), "DoCrafting") ?? throw new StepFailed("InventoryGui.DoCrafting not found");
        try
        {
            if (step.Bool("free", true))
                noCost?.SetValue(player, true);
            AccessTools.Field(typeof(InventoryGui), "m_craftRecipe")?.SetValue(gui, recipe);
            AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem")?.SetValue(gui, null);
            AccessTools.Field(typeof(InventoryGui), "m_craftVariant")?.SetValue(gui, 0);
            var times = Mathf.Max(1, (int)step.Num("count", 1));
            var before = player.GetInventory().CountItems(drop.m_itemData.m_shared.m_name);
            for (var i = 0; i < times; i++)
                doCrafting.Invoke(gui, new object[] { player });
            if (player.GetInventory().CountItems(drop.m_itemData.m_shared.m_name) <= before)
                throw new StepFailed($"crafting {prefab} made nothing (materials, station or a full inventory?)");
        }
        finally
        {
            noCost?.SetValue(player, oldNoCost);
            AccessTools.Field(typeof(InventoryGui), "m_craftRecipe")?.SetValue(gui, null);
            if (by.Length > 0)
                WardTools.PlayAs(previous, applyLook: false);
        }
    }

    /// <summary>
    /// "materialsFor": an item (its recipe) or a building piece: gives what it costs, times "count", at "quality" (the
    /// upgrade cost for quality 2+). With a craft step "free": false this makes a real-cost craft.
    /// </summary>
    private static void GiveMaterials(Dictionary<string, object?> step)
    {
        var name = Need(step, "materialsFor");
        var times = Mathf.Max(1, (int)step.Num("count", 1));
        var quality = Mathf.Max(1, (int)step.Num("quality", 1));
        Piece.Requirement[]? requirements = null;
        var item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
        var drop = item != null ? item.GetComponent<ItemDrop>() : null;
        if (drop != null)
            requirements = (ObjectDB.instance!.GetRecipe(drop.m_itemData) ?? throw new StepFailed($"'{name}' has no recipe")).m_resources;
        else
        {
            var go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            var piece = go != null ? go.GetComponent<Piece>() : null;
            requirements = piece != null ? piece.m_resources : throw new StepFailed($"'{name}' is neither an item with a recipe nor a building piece");
        }

        foreach (var req in requirements)
        {
            if (req.m_resItem == null)
                continue;
            var amount = req.GetAmount(quality) * times;
            if (amount > 0 && ItemGiver.Give(req.m_resItem.gameObject.name, amount) == 0)
                throw new StepFailed($"could not give {amount} {req.m_resItem.gameObject.name} for {name}");
        }
    }

    /// <summary>
    /// Persistence check: save, log out to the main menu and load the same world and character back in (no process restart,
    /// nothing reset). Tagged objects are found again afterwards (same prefab at the same spot). "timeout" default 180 s.
    /// </summary>
    private static IEnumerator Relog(Dictionary<string, object?> step, RunContext ctx)
    {
        // remember tagged objects by prefab + position: the old instances are destroyed with the scene
        var marks = new List<(string tag, string prefab, Vector3 pos, bool spawned)>();
        foreach (var kv in ctx.Tags)
        {
            if (kv.Value != null)
                marks.Add((kv.Key, Utils.GetPrefabName(kv.Value), kv.Value.transform.position, ctx.Spawned.Contains(kv.Value)));
        }

        var timeout = step.Num("timeout", 180f);
        var started = Time.realtimeSinceStartup;
        if (!SessionRelog.Begin())
            throw new StepFailed("no world is running to log out of");
        ctx.Log("  logging out...");

        try
        {
            while (Player.m_localPlayer != null)
            {
                if (Time.realtimeSinceStartup - started > timeout)
                    throw new StepFailed("the game did not log out");
                yield return null;
            }

            ctx.Log("  at the main menu, loading the same world...");
            var stable = 0f;
            while (stable < 2f)
            {
                if (Time.realtimeSinceStartup - started > timeout)
                    throw new StepFailed($"the world did not load again within {timeout:0} s");
                var p = Player.m_localPlayer;
                stable = p != null && !p.InIntro() && !p.IsTeleporting() && ZNetScene.instance != null ? stable + Time.unscaledDeltaTime : 0f;
                yield return null;
            }
        }
        finally
        {
            SessionRelog.Finished();
        }

        // the reloaded world's objects replace the old tags
        ctx.Spawned.RemoveAll(go => go == null);
        foreach (var (tag, prefab, pos, spawned) in marks)
        {
            GameObject? best = null;
            var bestD = 1.5f;
            foreach (var v in Targets.AllViews())
            {
                if (!Utils.GetPrefabName(v.gameObject).Equals(prefab, StringComparison.OrdinalIgnoreCase))
                    continue;
                var d = Vector3.Distance(v.transform.position, pos);
                if (d < bestD)
                {
                    best = v.gameObject;
                    bestD = d;
                }
            }

            if (best == null)
            {
                ctx.Tags.Remove(tag);
                ctx.Log($"  '{tag}' ({prefab}) is gone after the relog");
                continue;
            }

            ctx.Tags[tag] = best;
            if (spawned)
                ctx.Spawned.Add(best);
        }

        ctx.Log($"  back in after {Time.realtimeSinceStartup - started:0} s");
    }

    public static IEnumerator? Execute(Dictionary<string, object?> step, RunContext ctx)
    {
        var action = step.First("do", "action", "step").Trim();
        switch (action.ToLowerInvariant())
        {
            case "teleport": return Teleport(step, ctx);
            case "face": Face(step, ctx); return null;
            case "wait": return Wait(step, ctx);
            case "spawn": Spawn(step, ctx); return null;
            case "give":
                if (step.Has("materialsFor"))
                {
                    GiveMaterials(step);
                    return null;
                }

                // "crafter": "me" / "Alice" / "Name:123" = crafted by that player; left out = unowned (like loot or console spawns)
                var crafter = step.Str("crafter");
                var maker = crafter.Length > 0 ? WardTools.Parse(crafter) : null;
                if (ItemGiver.Give(Need(step, "prefab"), Mathf.Max(1, (int)step.Num("count", 1)), Mathf.Max(1, (int)step.Num("quality", 1)), step.Bool("equip"),
                        maker?.Id ?? 0L, maker?.Name ?? "") == 0)
                    throw new StepFailed($"could not give '{step.Str("prefab")}'");
                return null;
            case "craft": Craft(step); return null;
            case "clearinventory":
                P.UnequipAllItems();
                P.GetInventory().RemoveAll();
                return null;
            case "console": GameRefs.RunConsoleCommand(Need(step, "command")); return null;
            case "playas":
            {
                var who = step.First("who", "name");
                if (who.Length == 0 || who.Equals("me", StringComparison.OrdinalIgnoreCase))
                    WardTools.RestoreOriginal();
                else if (!WardTools.PlayAs(WardTools.Parse(who), step.Bool("look", true)))
                    throw new StepFailed("play-as failed (no player yet?)");
                return null;
            }
            case "interact": Interact(step, ctx); return null;
            case "click": Ok(UiDriver.Click(step.First("button", "target", "name"))); return null;
            case "toggle": Ok(UiDriver.SetToggle(step.First("toggle", "target", "name"), step.Bool("value", true))); return null;
            case "type": Ok(UiDriver.Type(step.First("field", "target", "name"), step.Str("text"))); return null;
            case "presskey": return PressKey(step);
            case "closeui":
                if (InventoryGui.instance != null && InventoryGui.IsVisible()) InventoryGui.instance.Hide();
                if (TextInput.instance != null && TextInput.IsVisible()) TextInput.instance.Hide();
                // a test that pressed Esc may have opened the game menu
                if (Menu.instance != null && Menu.IsVisible()) AccessTools.Method(typeof(Menu), "Hide")?.Invoke(Menu.instance, null);
                return null;
            case "invoke": ctx.LastResult = Invoke(step); ctx.Log($"  -> {Convert.ToString(ctx.LastResult, CultureInfo.InvariantCulture) ?? "null"}"); return null;
            case "hook":
            {
                var args = step.Obj("args") ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                ctx.LastResult = TestHooks.Call(Need(step, "name"), args, ctx);
                ctx.Log($"  -> {Convert.ToString(ctx.LastResult, CultureInfo.InvariantCulture) ?? "null"}");
                var tag = step.First("as", "tag");
                if (tag.Length > 0 && ctx.LastResult is GameObject made)
                {
                    ctx.Tags[tag] = made;
                    if (!step.Bool("keep"))
                        ctx.Spawned.Add(made);
                }

                // "expect" / "contains" / "notContains" on the step: also record the answer as a check (like a check step)
                if (step.Has("expect") || step.Has("contains") || step.Has("notContains"))
                {
                    var text = Convert.ToString(ctx.LastResult, CultureInfo.InvariantCulture) ?? "null";
                    var r = new AssertResult
                    {
                        Description = (step.Has("label") ? step.Str("label") + ": " : "") + J.Describe(step),
                        Passed = Asserts.Matches(text, step),
                        Detail = $"hook returned '{text}'"
                    };
                    ctx.Result.Asserts.Add(r);
                    ctx.Log($"  check {(r.Passed ? "ok" : "FAILED")}: {r.Detail}");
                    if (!r.Passed && step.Bool("stop"))
                        throw new StepFailed($"{r.Description} -> {r.Detail}");
                }

                return null;
            }
            case "checkpoint": return Checkpoint(step, ctx);
            case "check": return CheckNow(step, ctx);
            case "screenshot": TestRunner.Screenshot(ctx, step.Str("name", "shot")); return null;
            case "log": ctx.Log("  " + step.Str("message")); return null;
            case "case": return ApplyCase(step);
            case "setconfig":
            {
                // guid defaults to the suite's first required mod; "values": { "Section/Key": value, ... } sets several at once
                var guid = step.Str("guid");
                if (guid.Length == 0 && ctx.Suite.RequireMods.Count > 0)
                    guid = ctx.Suite.RequireMods[0].Guid;
                if (guid.Length == 0)
                    throw new StepFailed("setConfig needs a guid (or a suite with requireMods)");
                var many = step.Obj("values");
                if (many != null)
                {
                    foreach (var kv in many)
                    {
                        var slash = kv.Key.LastIndexOf('/');
                        if (slash <= 0)
                            throw new StepFailed($"'{kv.Key}': use \"Section/Key\"");
                        Ok(ModEnvironment.Set(guid, kv.Key.Substring(0, slash), kv.Key.Substring(slash + 1), J.Value(kv.Value)));
                    }
                }
                else
                {
                    Ok(ModEnvironment.Set(guid, Need(step, "section"), Need(step, "key"), step.Str("value")));
                }

                return null;
            }
            case "reset": StateReset.Run(); return null;
            case "relog": return Relog(step, ctx);
            case "holdkey": UiSteps.HoldKey(step); return null;
            case "releasekeys": HeldKeys.ReleaseAll(); return null;
            case "itemclick": return UiSteps.ItemClick(step, ctx);
            case "lookat": return UiSteps.LookAt(step, ctx);
            case "setlanguage": LanguageSwitch.Set(Need(step, "language")); return null;
            case "drop": UiSteps.Drop(step, ctx); return null;
            case "place": return UiSteps.Place(step, ctx);
            case "destroy":
            {
                var go = Targets.Find(ctx, Need(step, "target")) ?? throw new StepFailed($"nothing named '{step.Str("target")}' to destroy");
                ZNetScene.instance?.Destroy(go);
                return null;
            }
            case "use":
                // blocks are replaced by their steps when the suite loads (SuiteLoader / BlockExpander)
                throw new StepFailed($"block '{step.Str("block")}' was not expanded; reload the suites");
            default:
                throw new StepFailed($"unknown step '{action}' (known: {string.Join(", ", Names)})");
        }
    }

    private static string Need(Dictionary<string, object?> step, string key)
    {
        var v = step.Str(key);
        if (v.Length == 0)
            throw new StepFailed($"step is missing '{key}'");
        return v;
    }

    private static void Ok(string error)
    {
        if (error.Length > 0)
            throw new StepFailed(error);
    }

    private static IEnumerator Teleport(Dictionary<string, object?> step, RunContext ctx)
    {
        var p = P;
        Vector3 pos;
        var target = step.First("to", "target");
        if (target.Length > 0)
        {
            var go = Targets.Find(ctx, target, 2000f) ?? throw new StepFailed($"teleport target '{target}' not found");
            var away = step.Num("distance", 2f);
            pos = go.transform.position - go.transform.forward * away;
        }
        else
        {
            pos = Targets.PointFrom(step, new Vector3(float.NaN, 0, 0));
            if (float.IsNaN(pos.x))
                throw new StepFailed("teleport needs x/z, dx/dz or to");
        }

        pos.y = Mathf.Max(pos.y, ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(pos) : pos.y) + 0.3f;
        if (Vector3.Distance(p.transform.position, pos) < 120f)
        {
            // short hops: move directly, no loading screen or teleport cooldown
            p.transform.position = pos;
            var body = p.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = pos;
                body.velocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            yield return null;
            yield break;
        }

        p.TeleportTo(pos, p.transform.rotation, true);
        yield return null;
        while (!ctx.TimedOut && Player.m_localPlayer != null && Player.m_localPlayer.IsTeleporting())
            yield return null;
    }

    private static readonly MethodInfo? SetLookDir = AccessTools.Method(typeof(Character), "SetLookDir");

    /// <summary>Point the view (camera pitch included) along <paramref name="dir"/>.</summary>
    internal static void SetLook(Player p, Vector3 dir)
    {
        if (SetLookDir == null)
            return;
        var ps = SetLookDir.GetParameters();
        var args = new object[ps.Length];
        args[0] = dir;
        for (var i = 1; i < ps.Length; i++) args[i] = 0f;
        SetLookDir.Invoke(p, args);
    }

    private static void Face(Dictionary<string, object?> step, RunContext ctx)
    {
        var p = P;
        Vector3 at;
        var target = step.First("target", "to");
        if (target.Length > 0)
            at = (Targets.Find(ctx, target) ?? throw new StepFailed($"face target '{target}' not found")).transform.position;
        else
            at = Targets.PointFrom(step, p.transform.position + p.transform.forward);

        var dir = at - p.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return;
        dir.Normalize();
        UiSteps.Aim(p, dir);
    }

    private static IEnumerator Wait(Dictionary<string, object?> step, RunContext ctx)
    {
        var until = step.Obj("until");
        if (until == null)
        {
            var end = Time.realtimeSinceStartup + step.Num("seconds", step.Num("time", 1f));
            while (Time.realtimeSinceStartup < end && !ctx.TimedOut)
                yield return null;
            yield break;
        }

        var limit = Time.realtimeSinceStartup + step.Num("timeout", 10f);
        var last = new AssertResult();
        while (Time.realtimeSinceStartup < limit && !ctx.TimedOut)
        {
            last = Asserts.Evaluate(until, ctx);
            if (last.Passed)
                yield break;
            yield return null;
        }

        throw new StepFailed($"waited for '{last.Description}' but it never happened ({last.Detail})");
    }

    private static void Spawn(Dictionary<string, object?> step, RunContext ctx)
    {
        var prefabName = Need(step, "prefab");
        var prefab = ZNetScene.instance?.GetPrefab(prefabName) ?? throw new StepFailed($"no prefab '{prefabName}'");
        var p = P;
        var count = Mathf.Max(1, (int)step.Num("count", 1));
        var basePos = Targets.PointFrom(step, p.transform.position + p.transform.forward * 3f);
        var owner = step.Str("owner");
        for (var i = 0; i < count; i++)
        {
            var pos = basePos + p.transform.right * (i * 2f);
            if (ZoneSystem.instance != null)
                pos.y = ZoneSystem.instance.GetGroundHeight(pos);

            GameObject go;
            if (prefab.GetComponent<PrivateArea>() != null)
            {
                var permitted = new List<WardTools.Identity>();
                foreach (var who in step.Arr("permitted"))
                    if (who is string s) permitted.Add(WardTools.Parse(s));
                var ward = WardTools.Spawn(pos, WardTools.Parse(owner.Length == 0 ? "me" : owner), step.Bool("enabled"), permitted, step.Num("radius", -1f));
                go = ward != null ? ward.gameObject : throw new StepFailed("ward spawn failed");
            }
            else
            {
                go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.Euler(0f, step.Num("yaw"), 0f));
                var piece = go.GetComponent<Piece>();
                if (piece != null)
                    WardTools.SetPieceOwner(piece, WardTools.Parse(owner.Length == 0 ? "me" : owner));
                var character = go.GetComponent<Character>();
                if (character != null && step.Num("level", 1) > 1)
                    character.SetLevel((int)step.Num("level", 1));
            }

            if (!step.Bool("keep"))
                ctx.Spawned.Add(go);
            var tag = step.First("as", "tag");
            if (tag.Length > 0)
                ctx.Tags[count == 1 ? tag : $"{tag}{i + 1}"] = go;
        }
    }

    private static void Interact(Dictionary<string, object?> step, RunContext ctx)
    {
        var target = step.First("target", "prefab");
        var go = Targets.Find(ctx, target) ?? throw new StepFailed($"nothing named '{target}' within reach");
        var interactable = go.GetComponentInChildren<Interactable>() ?? go.GetComponentInParent<Interactable>()
            ?? throw new StepFailed($"'{target}' cannot be interacted with");
        // the game's answer (true = it did something) becomes lastResult, e.g. false when a lock or ward refused
        ctx.LastResult = interactable.Interact(P, step.Bool("hold"), step.Bool("alt"));
        ctx.Log($"  -> {ctx.LastResult}");
    }

    private static IEnumerator PressKey(Dictionary<string, object?> step)
    {
        var button = step.Str("button");
        if (button.Length > 0)
            InputInjector.PressButton(button, (int)step.Num("frames", 0));
        else
        {
            var keyName = Need(step, "key");
            if (!Enum.TryParse<KeyCode>(keyName, true, out var key))
                throw new StepFailed($"unknown key '{keyName}' (use Unity KeyCode names: E, Tab, Escape, Alpha1...)");
            InputInjector.PressKey(key, (int)step.Num("frames", 0));
        }

        while (InputInjector.Busy)
            yield return null;
    }

    private static object? Invoke(Dictionary<string, object?> step)
    {
        var full = Need(step, "method");
        var dot = full.LastIndexOf('.');
        if (dot <= 0)
            throw new StepFailed("method must be 'Namespace.Type.Method'");
        var type = AccessTools.TypeByName(full.Substring(0, dot)) ?? throw new StepFailed($"type '{full.Substring(0, dot)}' not found");
        var name = full.Substring(dot + 1);
        var args = step.Arr("args");

        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (m.Name != name || m.GetParameters().Length != args.Count)
                continue;
            var ps = m.GetParameters();
            var values = new object?[ps.Length];
            for (var i = 0; i < ps.Length; i++)
                values[i] = ConvertArg(args[i], ps[i].ParameterType);
            return m.Invoke(null, values);
        }

        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (prop != null && args.Count == 0)
            return prop.GetValue(null, null);
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (field != null && args.Count == 0)
            return field.GetValue(null);

        throw new StepFailed($"no static method {name} with {args.Count} argument(s) on {type.FullName}");
    }

    private static object? ConvertArg(object? raw, Type t)
    {
        if (raw == null)
            return null;
        if (t == typeof(string))
            return Convert.ToString(raw, CultureInfo.InvariantCulture);
        if (t.IsEnum)
            return Enum.Parse(t, Convert.ToString(raw, CultureInfo.InvariantCulture)!, true);
        return Convert.ChangeType(raw, t, CultureInfo.InvariantCulture);
    }

    private static IEnumerator Checkpoint(Dictionary<string, object?> step, RunContext ctx)
    {
        var message = step.Str("message", "Does it look right?");
        if (ctx.Unattended)
        {
            ctx.Result.Asserts.Add(new AssertResult { Description = $"checkpoint: {message}", Passed = true, Detail = "skipped (unattended run)" });
            yield break;
        }

        TestRunner.AskCheckpoint(message);
        var askedAt = Time.realtimeSinceStartup;
        while (TestRunner.CheckpointPending && !TestRunner.StopRequested)
            yield return null;
        ctx.Deadline += Time.realtimeSinceStartup - askedAt; // a person answering does not use up the test's time
        ctx.Result.Asserts.Add(new AssertResult
        {
            Description = $"checkpoint: {message}",
            Passed = TestRunner.CheckpointAnswer,
            Detail = TestRunner.CheckpointAnswer ? "tester said pass" : "tester said fail"
        });
    }

    private static IEnumerator ApplyCase(Dictionary<string, object?> step)
    {
        var name = Need(step, "name");
        var c = CaseRunner.Load(name) ?? throw new StepFailed($"case '{name}' could not be read");
        if (c.reset < 0)
            c.reset = 0;
        return CaseRunner.ApplyCase(c, name, quick: true);
    }

    public static string CasePathHint => Path.Combine(CaseRunner.Folder, "<name>.json");
}
