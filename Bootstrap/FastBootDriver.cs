using System.Collections;
using UnityEngine;

namespace DrakesTestLab.Bootstrap;

/// <summary>Frame-by-frame fast boot on FejdStartup — assumes DeployBeforeLoad already wrote saves to disk.</summary>
internal sealed class FastBootDriver : MonoBehaviour
{
    private const float PrimaryTimeoutSeconds = 45f;
    private const float FallbackTimeoutSeconds = 20f;

    private FejdStartup? _fejd;
    private bool _started;

    public static void Attach(FejdStartup fejd)
    {
        if (fejd == null || fejd.GetComponent<FastBootDriver>() != null)
            return;

        var driver = fejd.gameObject.AddComponent<FastBootDriver>();
        driver._fejd = fejd;
    }

    private void Start() => StartCoroutine(BootRoutine());

    private IEnumerator BootRoutine()
    {
        if (_fejd == null || !TestLabConfig.Instance.BootstrapEnabled.Value)
            yield break;

        // Let FejdStartup.Start() run SetupObjectDB before we touch saves or start the world.
        yield return null;

        MenuSuppressor.Suppress(_fejd);
        FejdStartupBridge.RefreshSaveLists(_fejd, force: true);

        var target = SaveModeResolver.Resolve();
        var triedDefaultFallback = SaveModeResolver.UsesDefaultSaveNames(target);

        yield return Poll(target, PrimaryTimeoutSeconds);

        if (!_started && !triedDefaultFallback)
        {
            TestLabPlugin.Log?.LogWarning(
                $"FastBoot failed for custom names (world={target.WorldName}, char={target.CharacterName}). " +
                $"Redeploying defaults ({PresetDeployer.DefaultEphemeralWorldName}/{PresetDeployer.DefaultEphemeralCharacterName}) and retrying.");

            target = SaveModeResolver.CreateDefaultFallbackTarget(target);
            PresetDeployer.DeployBeforeLoad(target);
            FejdStartupBridge.RefreshSaveLists(_fejd, force: true);

            yield return Poll(target, FallbackTimeoutSeconds);
        }

        if (!_started)
            TestLabPlugin.Log?.LogWarning("FastBoot gave up — check BepInEx log for 'FastBoot waiting on' messages.");
    }

    private IEnumerator Poll(ActiveSaveTarget target, float timeoutSeconds)
    {
        var deadline = Time.realtimeSinceStartup + timeoutSeconds;

        while (!_started && Time.realtimeSinceStartup < deadline)
        {
            MenuSuppressor.Suppress(_fejd!);

            if (FejdStartupBridge.IsObjectDbReady()
                && FejdStartupBridge.TryFastStart(_fejd!, target.WorldName, target.CharacterName))
            {
                _started = true;
                yield break;
            }

            yield return null;
        }
    }
}
