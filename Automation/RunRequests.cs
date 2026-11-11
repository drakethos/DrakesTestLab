using System;
using System.Collections;
using System.IO;
using BepInEx;
using DrakesTestLab.Bootstrap;
using UnityEngine;

namespace DrakesTestLab.Automation;

/// <summary>
/// Hot mode: after the launch run the game stays open and watches <c>config/DrakesTestLab/requests</c> for one-line
/// text files dropped in by a test script (Run-HotSwap.ps1 in the dev tree):
/// <list type="bullet">
/// <item><c>reset.txt</c>: fresh world and character in this same game (SessionReset); other requests wait until the
/// new world is loaded.</item>
/// <item><c>run.txt</c>: rescan suites and test hooks (picks up a mod that was reloaded), then run "failed" or a
/// suite/test list like -testlab-run.</item>
/// <item><c>quit.txt</c>: close the game (exit code 1 if the last run had a failure).</item>
/// </list>
/// TestLab writes <c>state.txt</c> = busy | ready so the script knows when to send the next request.
/// Off unless [Automation] HotMode is true or -testlab-hot is on the command line. It only reads files: it never
/// loads code, needs no other plugin, and never starts a program.
/// </summary>
internal static class RunRequests
{
    public static string Folder => Path.Combine(Paths.ConfigPath, "DrakesTestLab", "requests");
    static string RunFile => Path.Combine(Folder, "run.txt");
    static string ResetFile => Path.Combine(Folder, "reset.txt");
    static string QuitFile => Path.Combine(Folder, "quit.txt");
    static string StateFile => Path.Combine(Folder, "state.txt");

    static string _state = "";

    public static bool Enabled => CommandLine.Has("-testlab-hot") || TestLabConfig.Instance.HotMode.Value;

    public static IEnumerator Poll()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            // stale requests from an earlier session must not fire
            foreach (var f in new[] { RunFile, ResetFile, QuitFile })
                if (File.Exists(f)) File.Delete(f);
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogWarning($"Hot mode: cannot use {Folder}: {ex.Message}");
            yield break;
        }

        TestLabPlugin.Log?.LogInfo($"Hot mode: watching {Folder} for run.txt / reset.txt / quit.txt");
        var wait = new WaitForSecondsRealtime(1f);
        while (true)
        {
            yield return wait;
            if (TestRunner.Running || !WorldReady())
            {
                SetState("busy");
                continue;
            }

            if (TryTake(QuitFile, out _))
            {
                SetState("busy");
                var failed = TestRunner.LastRun.Exists(r => r.Status == TestStatus.Failed);
                TestLabPlugin.Log?.LogInfo("Hot mode: quit requested.");
                yield return new WaitForSecondsRealtime(0.5f);
                Application.Quit(failed ? 1 : 0);
                yield break;
            }

            if (TryTake(ResetFile, out _))
            {
                SetState("busy");
                TestLabPlugin.Log?.LogInfo("Hot mode: reset requested (fresh world).");
                yield return ResetAndWait();
                continue;
            }

            if (TryTake(RunFile, out var what))
            {
                SetState("busy");
                TestLabPlugin.Log?.LogInfo($"Hot mode: run requested ({what}).");
                TestRunner.RunRequested(what.Length == 0 ? "failed" : what);
                continue;
            }

            SetState("ready");
        }
    }

    /// <summary>Log out to the menu; TestLab's fast boot loads a fresh world and character. Done when a NEW player is in the world.</summary>
    static IEnumerator ResetAndWait()
    {
        var before = Player.m_localPlayer;
        SessionReset.Execute();
        var waited = 0f;
        var stable = 0f;
        var nextNote = 10f;
        while (waited < 240f && stable < 2f)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
            var p = Player.m_localPlayer;
            if (p != null && p != before && WorldReady())
                stable += Time.unscaledDeltaTime;
            else
                stable = 0f;
            if (waited >= nextNote)
            {
                nextNote += 10f;
                TestLabPlugin.Log?.LogInfo($"Hot mode: reset waiting {waited:0}s (player={(p != null ? (p == before ? "old" : "new") : "none")}, " +
                                           $"intro={(p != null && p.InIntro())}, teleporting={(p != null && p.IsTeleporting())}, " +
                                           $"zns={(ZNetScene.instance != null)}, arenaBusy={ArenaBuilder.Busy}, game={(Game.instance != null)})");
            }
        }

        TestLabPlugin.Log?.LogInfo(stable >= 2f ? "Hot mode: fresh world ready." : "Hot mode: reset did not finish within 240 s.");
    }

    static bool WorldReady()
    {
        var p = Player.m_localPlayer;
        return p != null && !p.InIntro() && !p.IsTeleporting() && ZNetScene.instance != null && !ArenaBuilder.Busy;
    }

    static void SetState(string state)
    {
        if (state == _state)
            return;
        try
        {
            File.WriteAllText(StateFile, state);
            _state = state;
        }
        catch
        {
            // next poll tries again
        }
    }

    static bool TryTake(string path, out string text)
    {
        text = "";
        try
        {
            if (!File.Exists(path))
                return false;
            text = File.ReadAllText(path).Trim();
            File.Delete(path);
            return true;
        }
        catch
        {
            return false; // still being written; next poll
        }
    }
}
