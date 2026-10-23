using System;

namespace DrakesTestLab.Automation;

/// <summary>Reads DrakesTestLab's own command-line switches (-testlab-run, -testlab-stay, -testlab-quit, -testlab-delay).</summary>
internal static class CommandLine
{
    private static string[]? _args;

    private static string[] Args => _args ??= Environment.GetCommandLineArgs();

    public static bool Has(string name)
    {
        foreach (var a in Args)
        {
            if (a.Equals(name, StringComparison.OrdinalIgnoreCase) || a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>"-name value" or "-name=value"; null when the switch is absent.</summary>
    public static string? Value(string name)
    {
        var args = Args;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i].Substring(name.Length + 1).Trim('"');
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1].Trim('"') : "all";
        }

        return null;
    }
}
