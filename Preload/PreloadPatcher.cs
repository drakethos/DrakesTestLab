using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx;
using HarmonyLib;
using Mono.Cecil;

namespace DrakesTestLab.Preload
{
    /// <summary>
    /// BepInEx preloader patcher (it patches no game DLL). Its only job is to time every plugin's load, including the
    /// ones that load before DrakesTestLab: it hooks BaseUnityPlugin's constructor in Finish(), after all other patchers
    /// ran, and leaves the list in AppDomain data for DrakesTestLab to read. No effect on gameplay, no files written.
    /// </summary>
    public static class PreloadPatcher
    {
        public const string Key = "DrakesTestLab.ModLoad";           // List<string> of "ticks|name|guid"
        public const string BeforeKey = "DrakesTestLab.BeforeSeconds"; // double: process start -> first plugin
        public const string EndKey = "DrakesTestLab.ChainloaderDoneTicks"; // long: Stopwatch ticks when BepInEx finished loading plugins

        public static IEnumerable<string> TargetDLLs { get { yield break; } }

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        public static void Initialize()
        {
        }

        public static void Finish()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(Key, new List<string>());
                new Harmony("drakemods.DrakesTestLab.Preload").PatchAll(typeof(PreloadPatcher).Assembly);
            }
            catch (Exception ex)
            {
                // timing is optional: never break startup
                AppDomain.CurrentDomain.SetData(Key, null);
                Console.WriteLine("[DrakesTestLab.Preload] could not hook plugin loading: " + ex.Message);
            }
        }
    }

    /// <summary>Marks the moment the last plugin's Awake is done, so the last plugin is not charged for the engine work that follows.</summary>
    [HarmonyPatch(typeof(BepInEx.Bootstrap.Chainloader), nameof(BepInEx.Bootstrap.Chainloader.Start))]
    internal static class ChainloaderDonePatch
    {
        private static void Postfix()
        {
            try
            {
                AppDomain.CurrentDomain.SetData(PreloadPatcher.EndKey, Stopwatch.GetTimestamp());
            }
            catch
            {
            }
        }
    }

    [HarmonyPatch(typeof(BaseUnityPlugin), MethodType.Constructor)]
    internal static class PluginCtorPatch
    {
        private static void Prefix(BaseUnityPlugin __instance)
        {
            try
            {
                var list = AppDomain.CurrentDomain.GetData(PreloadPatcher.Key) as List<string>;
                if (list == null)
                    return;
                if (list.Count == 0)
                {
                    try
                    {
                        AppDomain.CurrentDomain.SetData(PreloadPatcher.BeforeKey,
                            (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds);
                    }
                    catch
                    {
                    }
                }

                var attr = (BepInPlugin)Attribute.GetCustomAttribute(__instance.GetType(), typeof(BepInPlugin));
                var name = (attr != null ? attr.Name : __instance.GetType().Name).Replace('|', '/');
                var guid = attr != null ? attr.GUID : "";
                list.Add(Stopwatch.GetTimestamp() + "|" + name + "|" + guid);
            }
            catch
            {
            }
        }
    }
}
