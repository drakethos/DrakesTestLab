using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DrakesTestLab.Bootstrap;

/// <summary>
/// Fast boot skips the Start Game screen, so the "Open / Public / Crossplay / Password" choices that screen normally holds are
/// filled in from the config here, right before the world is started. That is what lets friends join a test world you logged
/// straight into.
/// </summary>
internal static class ServerSettings
{
    public const string FallbackPassword = "testlab";

    /// <summary>Last decision, shown in the Session tab.</summary>
    public static string LastApplied { get; private set; } = "Not applied yet.";

    private static T? Get<T>(FejdStartup fejd, string field) where T : class =>
        AccessTools.Field(typeof(FejdStartup), field)?.GetValue(fejd) as T;

    private static bool EulaAccepted() =>
        AccessTools.Method(typeof(PlatformPrefs), "GetBool", new[] { typeof(string) })?.Invoke(null, new object[] { "EulaAccepted" }) is true;

    public static void ApplyBeforeStart(FejdStartup fejd)
    {
        var cfg = TestLabConfig.Instance;
        var open = Get<Toggle>(fejd, "m_openServerToggle");
        var pub = Get<Toggle>(fejd, "m_publicServerToggle");
        var cross = Get<Toggle>(fejd, "m_crossplayServerToggle");
        var pass = Get<object>(fejd, "m_serverPassword");

        if (!cfg.OpenToFriends.Value)
        {
            open?.SetIsOnWithoutNotify(false);
            pub?.SetIsOnWithoutNotify(false);
            LastApplied = "Single player: friends cannot join (set [Server] OpenToFriends = true).";
            return;
        }

        if (!EulaAccepted())
        {
            // Accepting the game's server terms is the player's decision; do it once through the normal Start Game screen.
            open?.SetIsOnWithoutNotify(false);
            LastApplied = "Not opened: accept the server terms once (main menu > Start Game > tick Open server), then relaunch.";
            TestLabPlugin.Log?.LogWarning(LastApplied);
            return;
        }

        var password = cfg.ServerPassword.Value ?? "";
        var listed = cfg.PublicListing.Value;
        if (listed && password.Length < 5)
        {
            password = FallbackPassword;
            TestLabPlugin.Log?.LogWarning($"A public server needs a password of 5+ characters; using '{FallbackPassword}'.");
        }

        // SetIsOnWithoutNotify: the normal click handler would pop up the terms window and start a PlayFab login
        open?.SetIsOnWithoutNotify(true);
        pub?.SetIsOnWithoutNotify(listed);
        cross?.SetIsOnWithoutNotify(cfg.Crossplay.Value);
        if (pass != null)
            AccessTools.Property(pass.GetType(), "text")?.SetValue(pass, password);

        LastApplied = $"Open to friends{(listed ? ", listed publicly" : "")}{(cfg.Crossplay.Value ? ", crossplay" : "")}; password {(password.Length == 0 ? "none" : "'" + password + "'")}.";
        TestLabPlugin.Log?.LogInfo("Server settings applied: " + LastApplied);
    }

    public static List<string> OnlinePlayers()
    {
        var names = new List<string>();
        if (ZNet.instance == null)
            return names;

        foreach (var p in ZNet.instance.GetPlayerList())
            names.Add(p.m_name);
        return names;
    }

    /// <summary>Plugin list a joining player has to match (the game itself does not enforce mods, but mismatches break things).</summary>
    public static string ModList()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Valheim {Application.version}");
        foreach (var info in Chainloader.PluginInfos.Values)
            sb.AppendLine($"{info.Metadata.Name} {info.Metadata.Version}");
        return sb.ToString();
    }
}
