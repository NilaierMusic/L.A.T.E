// File: L.A.T.E/Core/LatePlugin.cs
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine; // For MonoBehaviour
using LATE.Config;
using LATE.Managers.GameState;
using LATE.DataModels;

// Removed using MonoMod.RuntimeDetour;
// Removed using Photon.Realtime;
// Removed using System.Collections.Generic;
// Removed using System.Reflection;
// These are now primarily handled by Core.PatchManager

namespace LATE.Core;

[BepInPlugin(LATE.PluginInfo.PLUGIN_GUID, LATE.PluginInfo.PLUGIN_NAME, LATE.PluginInfo.PLUGIN_VERSION)]
internal sealed class LatePlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    // HarmonyInstance is now initialized and passed to PatchManager
    // internal static Harmony HarmonyInstance { get; private set; } = null!; // Moved conceptually to PatchManager

    // _hooks list moved to Core.PatchManager
    // _monoModHooks definitions moved to Core.PatchManager
    // _explicitHarmonyPatches definitions moved to Core.PatchManager

    internal static MonoBehaviour? CoroutineRunner => CoroutineHelper.CoroutineRunner;

    private void Awake()
    {
        Log = Logger;
        // HarmonyInstance is created here but primarily used by PatchManager
        var harmony = new Harmony(LATE.PluginInfo.PLUGIN_GUID);

        Log.LogInfo($"{LATE.PluginInfo.PLUGIN_NAME} v{LATE.PluginInfo.PLUGIN_VERSION} is loading…");

        GameVersion detectedVersion = GameVersionSupport.DetectVersion();
        Log.LogInfo($"Detected Game Version: {detectedVersion}");
        if (detectedVersion == GameVersion.Unknown)
        {
            Log.LogError("Failed to determine game version. Mod features related to Steam lobby management might not work correctly.");
        }

        ConfigManager.Initialize(Config);

        // Delegate patch application to the new PatchManager
        PatchManager.InitializeAndApplyPatches(harmony, Log);

        Log.LogInfo($"{LATE.PluginInfo.PLUGIN_NAME} finished loading!");
    }

    // ApplyMonoModHooks, TryApplyMonoModHook, ApplyHarmonyPatches, TryApplyHarmonyPatch, FindMethod
    // have all been moved to Core.PatchManager.

    internal static void ClearCoroutineRunnerCache()
    {
        CoroutineHelper.ClearCoroutineRunnerCache();
    }
}