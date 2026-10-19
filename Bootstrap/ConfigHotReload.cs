using System;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;

namespace DrakesTestLab.Bootstrap;

internal sealed class ConfigHotReload : IDisposable
{
    private FileSystemWatcher? _watcher;
    private DateTime _lastReload = DateTime.MinValue;

    public void Start()
    {
        if (!TestLabConfig.Instance.HotReloadEnabled.Value)
            return;

        var configPath = TestLabPlugin.Instance.Config.ConfigFilePath;
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            return;

        var dir = Path.GetDirectoryName(configPath)!;
        var file = Path.GetFileName(configPath);

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watcher.Changed += OnConfigChanged;
        _watcher.EnableRaisingEvents = true;
        TestLabPlugin.Log?.LogInfo($"Hot reload watching {configPath}");
    }

    private void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        var debounceMs = TestLabConfig.Instance.HotReloadDebounceMs.Value;
        var now = DateTime.UtcNow;
        if ((now - _lastReload).TotalMilliseconds < debounceMs)
            return;

        _lastReload = now;
        ReloadNow();
    }

    public static void ReloadNow()
    {
        try
        {
            var reloaded = 0;
            var failed = 0;

            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info.Instance is not BaseUnityPlugin plugin)
                    continue;

                try
                {
                    plugin.Config.Reload();
                    reloaded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    TestLabPlugin.Log?.LogWarning(
                        $"Config reload failed for {info.Metadata.GUID}: {ex.Message}");
                }
            }

            TestLabConfig.Bind(TestLabPlugin.Instance.Config);
            CreatureHostilityState.Enabled = !TestLabConfig.Instance.CreaturesHostile.Value;

            if (TestLabConfig.Instance.WarmTierChanged())
            {
                TestLabPlugin.Log?.LogWarning(
                    "Save/Bootstrap settings changed — use Reset (hard restart) for full effect.");
            }
            else
            {
                TestLabConfig.Instance.SyncWarmTierBaseline();
            }

            TestControlsApplier.Apply();

            var suffix = failed > 0 ? $", {failed} failed" : string.Empty;
            TestLabPlugin.Log?.LogInfo(
                $"Reloaded configs for {reloaded} plugin(s){suffix}. Player spawn items were not re-applied.");
        }
        catch (Exception ex)
        {
            TestLabPlugin.Log?.LogError($"Config reload failed: {ex}");
        }
    }

    public void Dispose()
    {
        if (_watcher == null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }
}
