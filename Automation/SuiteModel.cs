using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;
using DrakesTestLab.Bootstrap;
using DrakesTestLab.Cases;

namespace DrakesTestLab.Automation;

internal sealed class ModRequirement
{
    public string Guid = "";
    public string MinVersion = "";
}

internal sealed class ConfigOverride
{
    public string Guid = "";
    public string Section = "";
    public string Key = "";
    public string Value = "";
}

/// <summary>One automated test: optional setup (the test-case format), steps the runner performs, and asserts it checks.</summary>
internal sealed class TestDef
{
    public string Name = "";
    public string Description = "";
    public string File = "";
    public TestCase? Setup;
    public List<Dictionary<string, object?>> Steps = new();
    public List<Dictionary<string, object?>> Asserts = new();
    public bool ManualOnly;
    public float TimeoutSeconds = -1f;
    /// <summary>A negative test: it passes only when it fails (and, with <see cref="ExpectFailureContains"/>, for that reason).</summary>
    public bool ExpectFailure;
    public string ExpectFailureContains = "";
    public string LoadError = "";
}

/// <summary>
/// Reusable steps, called from a test with { "do": "use", "block": "name", "args": { ... } }. {{param}} in the block's
/// strings is replaced by the argument; "params" gives defaults (null = optional: empty text, and a key set to only
/// "{{param}}" is left out).
/// </summary>
internal sealed class BlockDef
{
    public string Name = "";
    public string Source = "";
    public Dictionary<string, object?> Params = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Param names in the order written: positional args of a call like "craftAndCheck(\"Hammer\", 2)" map onto them.</summary>
    public List<string> ParamOrder = new();
    public List<object?> Steps = new();
    /// <summary>A function's checks: added after its steps as check steps labelled with the function name.</summary>
    public List<object?> Asserts = new();
}

/// <summary>A folder of tests for one mod: which mods it needs, config overrides for them, shared setup and step blocks.</summary>
internal sealed class SuiteDef
{
    public string Name = "";
    public string Description = "";
    public string Folder = "";
    public List<ModRequirement> RequireMods = new();
    public List<ConfigOverride> ConfigOverrides = new();
    public TestCase? Setup;
    public bool ResetBetweenTests = true;
    public Dictionary<string, BlockDef> Blocks = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Includes = new();
    public List<TestDef> Tests = new();
    public string LoadError = "";
}

internal static class J
{
    public static string Str(this Dictionary<string, object?> d, string key, string def = "") =>
        d.TryGetValue(key, out var v) && v != null ? (v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture)) : def;

    public static bool Has(this Dictionary<string, object?> d, string key) => d.TryGetValue(key, out var v) && v != null;

    public static float Num(this Dictionary<string, object?> d, string key, float def = 0f)
    {
        if (!d.TryGetValue(key, out var v) || v == null)
            return def;
        if (v is double dbl)
            return (float)dbl;
        if (v is bool b)
            return b ? 1f : 0f;
        return float.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
    }

    public static bool Bool(this Dictionary<string, object?> d, string key, bool def = false)
    {
        if (!d.TryGetValue(key, out var v) || v == null)
            return def;
        if (v is bool b)
            return b;
        if (v is double dbl)
            return dbl != 0d;
        var s = Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim().ToLowerInvariant();
        return s is "true" or "yes" or "1" or "on";
    }

    public static Dictionary<string, object?>? Obj(this Dictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) ? v as Dictionary<string, object?> : null;

    public static List<object?> Arr(this Dictionary<string, object?> d, string key) =>
        d.TryGetValue(key, out var v) && v is List<object?> l ? l : new List<object?>();

    /// <summary>The first value present among several aliases ("do" / "action", "check" / "assert").</summary>
    public static string First(this Dictionary<string, object?> d, params string[] keys)
    {
        foreach (var k in keys)
        {
            var s = d.Str(k);
            if (s.Length > 0)
                return s;
        }

        return "";
    }

    public static string Describe(Dictionary<string, object?> d)
    {
        var parts = new List<string>();
        foreach (var kv in d)
            parts.Add($"{kv.Key}={Value(kv.Value)}");
        return string.Join(", ", parts);
    }

    public static string Value(object? v) => v switch
    {
        null => "null",
        string s => s,
        bool b => b ? "true" : "false",
        double n => n.ToString(CultureInfo.InvariantCulture),
        Dictionary<string, object?> d => "{" + Describe(d) + "}",
        List<object?> l => "[" + string.Join(", ", l.ConvertAll(Value)) + "]",
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };
}

/// <summary>Finds and parses suites in BepInEx/config/DrakesTestLab/suites/&lt;Suite&gt;/ (folders starting with _ are ignored).</summary>
internal static class SuiteLoader
{
    public static string Folder => Path.Combine(Paths.ConfigPath, "DrakesTestLab", "suites");
    public static string ResultsFolder => Path.Combine(Paths.ConfigPath, "DrakesTestLab", "results");

    public static List<SuiteDef> LoadAll()
    {
        var suites = new List<SuiteDef>();
        try
        {
            if (!Directory.Exists(Folder))
                return suites;

            var dirs = Directory.GetDirectories(Folder);
            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                if (Path.GetFileName(dir).StartsWith("_", StringComparison.Ordinal))
                    continue;
                suites.Add(LoadSuite(dir));
            }
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Listing suites failed: {ex.Message}");
        }

        return suites;
    }

    private static Dictionary<string, object?> ReadObject(string path) =>
        MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object?>
        ?? throw new FormatException("the file must contain one JSON object { ... }");

    /// <summary>Layers setups in order (includes first, the suite's own last): a later file wins field by field.</summary>
    private static TestCase? ReadSetup(List<Dictionary<string, object?>> setups, string where)
    {
        if (setups.Count == 0)
            return null;

        var warnings = new List<string>();
        var c = new TestCase();
        foreach (var setup in setups)
            CaseMapper.Populate(c, setup, warnings, "setup.");
        foreach (var w in warnings)
            TestLabPlugin.Log?.LogWarning($"{where}: {w}");
        if (c.reset < 0)
            c.reset = 0; // the runner resets between tests itself; setups layer on top of each other
        return c;
    }

    private static TestCase? ReadSetup(Dictionary<string, object?>? setup, string where) =>
        setup == null ? null : ReadSetup(new List<Dictionary<string, object?>> { setup }, where);

    private static SuiteDef LoadSuite(string dir)
    {
        var suite = new SuiteDef { Name = Path.GetFileName(dir), Folder = dir };
        var suiteFile = Path.Combine(dir, "suite.json");
        var order = new List<string>();
        try
        {
            Dictionary<string, object?>? d = null;
            var setups = new List<Dictionary<string, object?>>();
            if (File.Exists(suiteFile))
            {
                d = ReadObject(suiteFile);
                suite.Name = d.Str("name", suite.Name);
                suite.Description = d.Str("description");
                suite.ResetBetweenTests = d.Bool("resetBetweenTests", true);

                // included files first, so the suite's own values win
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(suiteFile) };
                foreach (var inc in IncludeList(d))
                    ApplyInclude(suite, suite.Blocks, inc, dir, setups, seen, 0);

                ReadModsAndOverrides(suite, d);
                var own = d.Obj("setup");
                if (own != null)
                    setups.Add(own);
                foreach (var t in d.Arr("tests"))
                {
                    if (t is string s)
                        order.Add(s);
                }
            }

            suite.Setup = ReadSetup(setups, $"Suite '{suite.Name}'");
            foreach (var sub in new[] { "_blocks", "_functions" })
            {
                var blockFolder = Path.Combine(dir, sub);
                if (Directory.Exists(blockFolder))
                    LoadBlockFolder(suite.Blocks, blockFolder);
            }

            if (d != null)
                AddBlocks(suite.Blocks, d, suiteFile);
        }
        catch (Exception ex)
        {
            suite.LoadError = $"suite.json: {ex.Message}";
        }

        var files = new List<string>(Directory.GetFiles(dir, "*.json"));
        files.RemoveAll(f => Path.GetFileName(f).Equals("suite.json", StringComparison.OrdinalIgnoreCase));
        files.Sort(StringComparer.OrdinalIgnoreCase);
        if (order.Count > 0)
        {
            var ordered = new List<string>();
            foreach (var name in order)
            {
                var match = files.Find(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    ordered.Add(match);
            }

            foreach (var f in files)
            {
                if (!ordered.Contains(f))
                    ordered.Add(f);
            }

            files = ordered;
        }

        foreach (var file in files)
            suite.Tests.Add(LoadTest(file, suite));
        return suite;
    }

    // ------------------------------------------------------------------ include

    private static List<string> IncludeList(Dictionary<string, object?> d)
    {
        var list = new List<string>();
        if (d.TryGetValue("include", out var v) && v is string one)
        {
            if (one.Length > 0)
                list.Add(one);
            return list;
        }

        foreach (var x in d.Arr("include"))
        {
            if (x is string s && s.Length > 0)
                list.Add(s);
        }

        return list;
    }

    /// <summary>A path relative to the including file's folder, else to the suites folder (e.g. "_Shared/mymods.json").</summary>
    private static string? ResolveInclude(string path, string fromFolder)
    {
        var full = Path.GetFullPath(Path.Combine(fromFolder, path));
        if (File.Exists(full) || Directory.Exists(full))
            return full;
        full = Path.GetFullPath(Path.Combine(Folder, path));
        return File.Exists(full) || Directory.Exists(full) ? full : null;
    }

    /// <summary>
    /// An included file may hold "include", "requireMods", "configOverrides", "setup", "blocks" and "functions"; an included
    /// folder is read like a _blocks folder. Each file is read once, so loops and diamonds are harmless. A test's own include
    /// (suite == null) only takes blocks and functions.
    /// </summary>
    private static void ApplyInclude(SuiteDef? suite, Dictionary<string, BlockDef> blocks, string path, string fromFolder,
        List<Dictionary<string, object?>>? setups, HashSet<string> seen, int depth)
    {
        if (depth > 10)
            throw new FormatException($"includes nest deeper than 10 levels at '{path}'");
        var full = ResolveInclude(path, fromFolder) ?? throw new FileNotFoundException($"include '{path}' not found (looked next to the including file and in the suites folder)");
        if (!seen.Add(full))
            return;
        suite?.Includes.Add(full);
        if (Directory.Exists(full))
        {
            LoadBlockFolder(blocks, full);
            return;
        }

        Dictionary<string, object?> d;
        try
        {
            d = ReadObject(full);
        }
        catch (Exception ex)
        {
            throw new FormatException($"include '{path}': {ex.Message}");
        }

        foreach (var inc in IncludeList(d))
            ApplyInclude(suite, blocks, inc, Path.GetDirectoryName(full)!, setups, seen, depth + 1);
        if (suite != null)
        {
            ReadModsAndOverrides(suite, d);
            var setup = d.Obj("setup");
            if (setup != null)
                setups?.Add(setup);
        }

        AddBlocks(blocks, d, full);
    }

    /// <summary>Adds requireMods and configOverrides; one already there for the same mod (or the same setting) is replaced.</summary>
    private static void ReadModsAndOverrides(SuiteDef suite, Dictionary<string, object?> d)
    {
        foreach (var m in d.Arr("requireMods"))
        {
            var req = m switch
            {
                Dictionary<string, object?> md => new ModRequirement { Guid = md.Str("guid"), MinVersion = md.Str("minVersion") },
                string guid => new ModRequirement { Guid = guid },
                _ => null
            };
            if (req == null)
                continue;
            suite.RequireMods.RemoveAll(r => r.Guid.Equals(req.Guid, StringComparison.OrdinalIgnoreCase));
            suite.RequireMods.Add(req);
        }

        foreach (var o in d.Arr("configOverrides"))
        {
            if (o is not Dictionary<string, object?> od)
                continue;
            var co = new ConfigOverride { Guid = od.Str("guid"), Section = od.Str("section"), Key = od.Str("key"), Value = od.Str("value") };
            var i = suite.ConfigOverrides.FindIndex(x => x.Guid.Equals(co.Guid, StringComparison.OrdinalIgnoreCase)
                                                          && x.Section.Equals(co.Section, StringComparison.OrdinalIgnoreCase)
                                                          && x.Key.Equals(co.Key, StringComparison.OrdinalIgnoreCase));
            if (i >= 0)
                suite.ConfigOverrides[i] = co;
            else
                suite.ConfigOverrides.Add(co);
        }
    }

    // ------------------------------------------------------------------ blocks and functions

    /// <summary>
    /// Each file in a _blocks / _functions folder is { "blocks": {...}, "functions": {...} }, one block or function
    /// { "params": ..., "steps"/"actions": [...], "asserts": [...] } named after the file, or a bare [ steps ] array named after the file.
    /// </summary>
    private static void LoadBlockFolder(Dictionary<string, BlockDef> blocks, string folder)
    {
        var files = Directory.GetFiles(folder, "*.json");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            object? parsed;
            try
            {
                parsed = MiniJson.Parse(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                throw new FormatException($"block file {Path.GetFileName(file)}: {ex.Message}");
            }

            if (parsed is Dictionary<string, object?> d && (d.Has("blocks") || d.Has("functions")))
                AddBlocks(blocks, d, file);
            else
                AddBlock(blocks, Path.GetFileNameWithoutExtension(file), parsed, file);
        }
    }

    /// <summary>The "blocks" and "functions" maps of a suite, include or test file (the same thing under two names).</summary>
    private static void AddBlocks(Dictionary<string, BlockDef> blocks, Dictionary<string, object?> file, string source)
    {
        foreach (var key in new[] { "blocks", "functions" })
        {
            var map = file.Obj(key);
            if (map == null)
                continue;
            foreach (var kv in map)
                AddBlock(blocks, kv.Key, kv.Value, source);
        }
    }

    private static void AddBlock(Dictionary<string, BlockDef> blocks, string name, object? value, string source)
    {
        var block = new BlockDef { Name = name, Source = source };
        switch (value)
        {
            case List<object?> steps:
                block.Steps = steps;
                break;
            case Dictionary<string, object?> d when d.Has("steps") || d.Has("actions") || d.Has("asserts"):
                block.Steps = d.Has("steps") ? d.Arr("steps") : d.Arr("actions");
                block.Asserts = d.Arr("asserts");
                ReadParams(block, d.TryGetValue("params", out var ps) ? ps : null);
                break;
            default:
                throw new FormatException($"'{name}' in {Path.GetFileName(source)} must be [ steps ] or {{ \"params\": ..., \"steps\": [...], \"asserts\": [...] }}");
        }

        if (Array.Exists(StepNames.All, n => n.Equals(name, StringComparison.OrdinalIgnoreCase)))
            TestLabPlugin.Log?.LogWarning($"'{name}' in {Path.GetFileName(source)} has a built-in step's name: call it with \"use\", the plain step name runs the built-in step");
        blocks[name] = block; // later sources win: includes, then _blocks/ and _functions/, then the file's own map
    }

    /// <summary>
    /// "params": { "prefab": "Hammer", "label": null } (defaults; a param left out is required), or
    /// "params": [ "prefab", { "count": 1 } ] (a plain name is required, an object gives a default). The order is the
    /// order of positional arguments.
    /// </summary>
    private static void ReadParams(BlockDef block, object? ps)
    {
        switch (ps)
        {
            case Dictionary<string, object?> map:
                foreach (var p in map)
                {
                    block.Params[p.Key] = p.Value;
                    block.ParamOrder.Add(p.Key);
                }

                break;
            case List<object?> list:
                foreach (var entry in list)
                {
                    if (entry is string required)
                        block.ParamOrder.Add(required);
                    else if (entry is Dictionary<string, object?> withDefault)
                    {
                        foreach (var p in withDefault)
                        {
                            block.Params[p.Key] = p.Value;
                            block.ParamOrder.Add(p.Key);
                        }
                    }
                }

                break;
        }
    }

    private static TestDef LoadTest(string file, SuiteDef suite)
    {
        var test = new TestDef { Name = Path.GetFileNameWithoutExtension(file), File = file };
        try
        {
            var d = ReadObject(file);
            test.Name = d.Str("name", test.Name);
            test.Description = d.Str("description");
            test.ManualOnly = d.Bool("manualOnly");
            test.TimeoutSeconds = d.Num("timeout", -1f);
            // "expectFailure": true, or the text the failure reason must contain
            if (d.TryGetValue("expectFailure", out var ef) && ef != null)
            {
                test.ExpectFailure = ef is not bool b || b;
                test.ExpectFailureContains = ef is string text ? text : "";
            }
            test.Setup = ReadSetup(d.Obj("setup"), $"Test '{suite.Name}/{test.Name}'");

            // a test may bring its own blocks / functions, or include a library, on top of the suite's
            var blocks = suite.Blocks;
            var includes = IncludeList(d);
            if (includes.Count > 0 || d.Has("blocks") || d.Has("functions"))
            {
                blocks = new Dictionary<string, BlockDef>(suite.Blocks, StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(file) };
                foreach (var inc in includes)
                    ApplyInclude(null, blocks, inc, Path.GetDirectoryName(file)!, null, seen, 0);
                AddBlocks(blocks, d, file);
            }

            test.Steps = BlockExpander.Expand(d.Arr("steps"), blocks);

            foreach (var a in d.Arr("asserts"))
            {
                if (a is Dictionary<string, object?> ad)
                    test.Asserts.Add(ad);
            }
        }
        catch (Exception ex)
        {
            test.LoadError = ex.Message;
        }

        return test;
    }
}

/// <summary>
/// Replaces calls to blocks and functions with their steps when a test loads, so the runner, the step log and the report see
/// ordinary steps (each tagged "block": "name"). A call is { "do": "use", "block": "name", "args": {...} },
/// { "do": "name", ...args } (built-in step names win), or the string "name(arg1, arg2)" with positional args in "params"
/// order. A function's asserts follow its steps as check steps labelled with its name. Any step may carry "if": a false, 0,
/// empty or null value (usually "{{someParam}}") drops it. A list where a step is expected (a "{{steps}}" param) is spliced in.
/// </summary>
internal static class BlockExpander
{
    private const int MaxDepth = 10;
    private static readonly Regex Placeholder = new(@"\{\{\s*([A-Za-z0-9_.\-]+)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex CallText = new(@"^\s*([A-Za-z_][A-Za-z0-9_.\-]*)\s*\((.*)\)\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly HashSet<string> UseKeys = new(StringComparer.OrdinalIgnoreCase) { "do", "action", "step", "block", "args" };
    private static readonly HashSet<string> BuiltIn = new(StepNames.All, StringComparer.OrdinalIgnoreCase);

    public static List<Dictionary<string, object?>> Expand(List<object?> steps, Dictionary<string, BlockDef> blocks)
    {
        var output = new List<Dictionary<string, object?>>();
        ExpandInto(steps, blocks, output, "", 0);
        return output;
    }

    private static void ExpandInto(List<object?> steps, Dictionary<string, BlockDef> blocks, List<Dictionary<string, object?>> output, string chain, int depth)
    {
        foreach (var s in steps)
        {
            switch (s)
            {
                case List<object?> spliced:
                    ExpandInto(spliced, blocks, output, chain, depth);
                    continue;
                case string text:
                    Call(ParseCall(text, blocks, out var callArgs), callArgs, blocks, output, chain, depth);
                    continue;
                case not Dictionary<string, object?>:
                    continue;
            }

            var step = (Dictionary<string, object?>)s;
            if (step.TryGetValue("if", out var cond))
            {
                if (!Truthy(cond))
                    continue;
                step = new Dictionary<string, object?>(step, StringComparer.OrdinalIgnoreCase);
                step.Remove("if");
            }

            var action = step.First("do", "action", "step");
            string name;
            if (action.Equals("use", StringComparison.OrdinalIgnoreCase))
            {
                name = step.Str("block");
                if (name.Length == 0)
                    throw new FormatException("a \"use\" step needs \"block\": \"<name>\"");
            }
            else if (action.Length > 0 && !BuiltIn.Contains(action))
            {
                name = action;
            }
            else
            {
                if (chain.Length > 0 && !step.ContainsKey("block"))
                    step["block"] = chain; // inside a block the step is a fresh copy made by Substitute
                output.Add(step);
                continue;
            }

            // "args", then any other key written straight on the call
            var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var given = step.Obj("args");
            if (given != null)
            {
                foreach (var kv in given)
                    args[kv.Key] = kv.Value;
            }

            foreach (var kv in step)
            {
                if (!UseKeys.Contains(kv.Key))
                    args[kv.Key] = kv.Value;
            }

            Call(name, args, blocks, output, chain, depth);
        }
    }

    private static void Call(string name, Dictionary<string, object?> given, Dictionary<string, BlockDef> blocks, List<Dictionary<string, object?>> output, string chain, int depth)
    {
        if (!blocks.TryGetValue(name, out var block))
            throw new FormatException($"unknown step, block or function '{name}' (blocks and functions: {(blocks.Count == 0 ? "none" : string.Join(", ", blocks.Keys))})");
        if (depth >= MaxDepth)
            throw new FormatException($"blocks nest more than {MaxDepth} deep ({chain} > {name}); does a block use itself?");

        var args = new Dictionary<string, object?>(block.Params, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in given)
            args[kv.Key] = kv.Value;

        var body = new List<object?>(block.Steps.Count + block.Asserts.Count);
        foreach (var bs in block.Steps)
            body.Add(Substitute(bs, args, name));
        foreach (var a in block.Asserts)
        {
            if (Substitute(a, args, name) is not Dictionary<string, object?> check)
                continue;
            // reported as "<function>: <label>" so a failure names the function
            var label = check.Str("label");
            check["label"] = label.Length > 0 ? $"{name}: {label}" : name;
            check.Remove("do");
            var asStep = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["do"] = "check" };
            foreach (var kv in check)
                asStep[kv.Key] = kv.Value;
            body.Add(asStep);
        }

        ExpandInto(body, blocks, output, chain.Length > 0 ? chain + ">" + name : name, depth + 1);
    }

    /// <summary>"craftAndCheck(\"Hammer\", 2)": JSON values in brackets, mapped onto the params in order.</summary>
    private static string ParseCall(string text, Dictionary<string, BlockDef> blocks, out Dictionary<string, object?> args)
    {
        var m = CallText.Match(text);
        if (!m.Success)
            throw new FormatException($"step \"{text}\" is not a call: write name(arg, ...) or a {{ \"do\": ... }} object");
        var name = m.Groups[1].Value;
        if (!blocks.TryGetValue(name, out var block))
            throw new FormatException($"unknown function '{name}' in \"{text}\" (blocks and functions: {(blocks.Count == 0 ? "none" : string.Join(", ", blocks.Keys))})");

        List<object?> values;
        try
        {
            values = m.Groups[2].Value.Trim().Length == 0 ? new List<object?>() : MiniJson.Parse("[" + m.Groups[2].Value + "]") as List<object?> ?? new List<object?>();
        }
        catch (Exception ex)
        {
            throw new FormatException($"arguments of \"{text}\": {ex.Message} (strings need \"quotes\")");
        }

        if (values.Count > block.ParamOrder.Count)
            throw new FormatException($"\"{text}\": '{name}' takes {block.ParamOrder.Count} argument(s) ({string.Join(", ", block.ParamOrder)}), got {values.Count}");
        args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++)
            args[block.ParamOrder[i]] = values[i];
        return name;
    }

    /// <summary>Deep copy with {{param}} filled in. A string that is only "{{param}}" takes the argument as it is (number, true, object).</summary>
    private static object? Substitute(object? value, Dictionary<string, object?> args, string block)
    {
        switch (value)
        {
            case string s:
            {
                var whole = Placeholder.Match(s);
                if (whole.Success && whole.Index == 0 && whole.Length == s.Length)
                    return Arg(args, whole.Groups[1].Value, block);
                return Placeholder.Replace(s, m =>
                {
                    var v = Arg(args, m.Groups[1].Value, block);
                    return v == null ? "" : J.Value(v);
                });
            }
            case Dictionary<string, object?> d:
            {
                var copy = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in d)
                {
                    // "label": "{{label}}" with a null (left out) param drops the key, so optional params leave no trace
                    var v = Substitute(kv.Value, args, block);
                    if (v != null || kv.Value == null || kv.Key.Equals("if", StringComparison.OrdinalIgnoreCase))
                        copy[kv.Key] = v;
                }

                return copy;
            }
            case List<object?> l:
            {
                var copy = new List<object?>(l.Count);
                foreach (var x in l)
                    copy.Add(Substitute(x, args, block));
                return copy;
            }
            default:
                return value;
        }
    }

    private static object? Arg(Dictionary<string, object?> args, string key, string block) =>
        args.TryGetValue(key, out var v) ? v : throw new FormatException($"'{block}' needs \"{key}\" (pass it as an argument or give it a default in \"params\")");

    private static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0d,
        string s => s.Trim().ToLowerInvariant() is not ("" or "false" or "0" or "no" or "off"),
        _ => true
    };
}
