using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace DrakesTestLabServer;

/// <summary>
/// Server-only half of DrakesTestLab. It runs on a dedicated server only and never boots a client: before the server
/// loads its world, it replaces it with the shipped lab world (a levelled arena with every crafting station), so every
/// server start is a clean lab. On a normal game client it logs one line and does nothing.
/// </summary>
[BepInPlugin(GUID, ModName, Version)]
public class LabServerPlugin : BaseUnityPlugin
{
    public const string GUID = "com.drakemods.testlab.server";
    public const string ModName = "DrakesTestLabServer";
    public const string Version = "0.1.0";

    internal static ManualLogSource? Log { get; private set; }
    internal static LabServerPlugin? Instance { get; private set; }

    // used by the server.quit control command (through Invoke, so it runs after the answer is sent)
    private void QuitNow() => Application.Quit();

    private void Awake()
    {
        Log = Logger;
        Instance = this;

        // batchmode = the dedicated server. Anything else is a client that should never run this.
        if (!Application.isBatchMode)
        {
            Logger.LogInfo($"{ModName} is a server-only mod: not a dedicated server, so it stays inactive.");
            return;
        }

        LabConfig.Bind(Config);
        Logger.LogInfo($"{ModName} {Version}: dedicated server detected.");

        // Awake runs before the server loads its world, so the lab world can be put in place first.
        LabWorld.Prepare(Info.Location);

        // Player 1 / Player 2 roles and the command relay between them (see Roles.cs).
        new Harmony(GUID).PatchAll(typeof(LabServerPlugin).Assembly);
    }
}
