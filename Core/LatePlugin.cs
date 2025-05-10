// File: L.A.T.E/Core/LatePlugin.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using Photon.Realtime;
using UnityEngine;
using LATE.Config; // Updated
using LATE.Managers.GameState; // Updated
using LATE.DataModels; // Added for GameVersion
// LATE.Utilities will be added when GameUtilities is moved/created

namespace LATE.Core; // File-scoped namespace

[BepInPlugin(LATE.PluginInfo.PLUGIN_GUID, LATE.PluginInfo.PLUGIN_NAME, LATE.PluginInfo.PLUGIN_VERSION)] // Using LATE.PluginInfo
internal sealed class LatePlugin : BaseUnityPlugin
{
    // Static properties to hold the BepInEx logger and Harmony instance.
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static Harmony HarmonyInstance { get; private set; } = null!;

    // Private cached fields.
    private static readonly List<Hook> _hooks = new List<Hook>(); // Keep hooks alive.
    // _coroutineRunner will be moved to CoroutineHelper

    // Public property to lazily initialize the CoroutineRunner.
    // This will be moved to CoroutineHelper, and accessed via CoroutineHelper.CoroutineRunner
    internal static MonoBehaviour? CoroutineRunner
    {
        get => CoroutineHelper.CoroutineRunner; // Will change to this
    }

    // MonoMod hook definitions.
    // These class names (Patches, NetworkConnect_Patches, etc.) will be updated when those files are refactored
    private static readonly (Type TargetType, string TargetMethod, Type HookType, string HookMethod)[] _monoModHooks =
    {
        (typeof(RunManager), "ChangeLevel", typeof(LATE.Patches), nameof(LATE.Patches.RunManager_ChangeLevelHook)),
        (typeof(PlayerAvatar), "Spawn", typeof(LATE.Patches), nameof(LATE.Patches.PlayerAvatar_SpawnHook)),
        (typeof(PlayerAvatar), "Start", typeof(LATE.Patches), nameof(LATE.Patches.PlayerAvatar_StartHook))
    };

    // Explicit Harmony patches definitions.
    private static readonly (Type TargetType, string TargetMethod, Type PatchType, string PatchMethod, Type[]? Args, bool Postfix)[] _explicitHarmonyPatches =
    {
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerEnteredRoom), typeof(LATE.Patches), nameof(LATE.Patches.NetworkManager_OnPlayerEnteredRoom_Postfix), new[] { typeof(Player) }, true),
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom), typeof(LATE.Patches), nameof(LATE.Patches.NetworkManager_OnPlayerLeftRoom_Postfix), new[] { typeof(Player) }, true)
    };

    // Unity Awake – called when the plugin loads.
    private void Awake()
    {
        Log = Logger; // Bind the BepInEx logger.
        HarmonyInstance = new Harmony(LATE.PluginInfo.PLUGIN_GUID); // Using LATE.PluginInfo

        Log.LogInfo($"{LATE.PluginInfo.PLUGIN_NAME} v{LATE.PluginInfo.PLUGIN_VERSION} is loading…"); // Using LATE.PluginInfo

        DataModels.GameVersion detectedVersion = GameVersionSupport.DetectVersion(); // Using LATE.DataModels.GameVersion
        Log.LogInfo($"Detected Game Version: {detectedVersion}");
        if (detectedVersion == DataModels.GameVersion.Unknown) // Using LATE.DataModels.GameVersion
        {
            Log.LogError("Failed to determine game version. Mod features related to Steam lobby management might not work correctly.");
        }

        ConfigManager.Initialize(Config); // Load configuration.

        ApplyMonoModHooks();
        ApplyHarmonyPatches();

        Log.LogInfo($"{LATE.PluginInfo.PLUGIN_NAME} finished loading!"); // Using LATE.PluginInfo
    }

    // Apply MonoMod hooks.
    private static void ApplyMonoModHooks()
    {
        Log.LogInfo("Applying MonoMod hooks…");
        foreach (var (targetType, targetMethod, hookType, hookMethod) in _monoModHooks)
        {
            TryApplyMonoModHook(targetType, targetMethod, hookType, hookMethod);
        }
        Log.LogInfo("MonoMod hook application finished.");
    }

    // Attempt to apply a single MonoMod hook.
    private static void TryApplyMonoModHook(Type targetType, string targetMethodName, Type hookType, string hookMethodName)
    {
        MethodInfo? targetMI = FindMethod(targetType, targetMethodName);
        MethodInfo? hookMI = FindMethod(hookType, hookMethodName);

        if (targetMI == null)
        {
            Log.LogError($"[MonoMod] Target not found: {targetType.FullName}.{targetMethodName}");
            return;
        }

        if (hookMI == null)
        {
            Log.LogError($"[MonoMod] Hook not found: {hookType.FullName}.{hookMethodName}");
            return;
        }

        try
        {
            _hooks.Add(new Hook(targetMI, hookMI)); // Keep the hook alive.
            Log.LogDebug($"[MonoMod] Hooked {targetType.Name}.{targetMethodName}");
        }
        catch (Exception ex)
        {
            Log.LogError($"[MonoMod] Exception while hooking {targetType.Name}.{targetMethodName}: {ex}");
        }
    }

    // Apply all Harmony patches.
    private static void ApplyHarmonyPatches()
    {
        Log.LogInfo("Applying Harmony patches…");
        try
        {
            // Apply attribute-driven patches.
            // These types (Patches, NetworkConnect_Patches, etc.) will need their namespaces updated
            // once they are moved and refactored. For now, assuming they are still in LATE namespace.
            HarmonyInstance.PatchAll(typeof(LATE.Patches));
            HarmonyInstance.PatchAll(typeof(LATE.NetworkConnect_Patches));
            HarmonyInstance.PatchAll(typeof(LATE.TruckScreenText_ChatBoxState_EarlyLock_Patches));

            // Apply explicit patches (with parameters and postfix options).
            foreach (var (targetType, targetMethod, patchType, patchMethod, args, postfix) in _explicitHarmonyPatches)
            {
                TryApplyHarmonyPatch(targetType, targetMethod, patchType, patchMethod, args, postfix);
            }

            Log.LogInfo("Harmony patches applied successfully.");
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to apply Harmony patches: {ex}");
        }
    }

    // Attempt to apply a single Harmony patch.
    private static void TryApplyHarmonyPatch(Type targetType, string targetMethodName, Type patchType, string patchMethodName, Type[]? arguments, bool postfix)
    {
        MethodInfo? targetMI = FindMethod(targetType, targetMethodName, arguments);
        MethodInfo? patchMI = FindMethod(patchType, patchMethodName);

        if (targetMI == null)
        {
            Log.LogError($"[Harmony] Target not found: {targetType.FullName}.{targetMethodName}");
            return;
        }

        if (patchMI == null)
        {
            Log.LogError($"[Harmony] Patch not found: {patchType.FullName}.{patchMethodName}");
            return;
        }

        HarmonyMethod harmonyMethod = new HarmonyMethod(patchMI);
        if (postfix)
        {
            HarmonyInstance.Patch(targetMI, postfix: harmonyMethod);
        }
        else
        {
            HarmonyInstance.Patch(targetMI, prefix: harmonyMethod);
        }

        Log.LogDebug($"[Harmony] Patched {targetType.Name}.{targetMethodName} ({(postfix ? "postfix" : "prefix")})");
    }

    // Helper for finding a method with optional arguments.
    private static MethodInfo? FindMethod(Type type, string methodName, Type[]? arguments = null)
    {
        return arguments == null
            ? AccessTools.Method(type, methodName)
            : AccessTools.Method(type, methodName, arguments);
    }

    // Clears the cached CoroutineRunner reference.
    // This will be moved to CoroutineHelper
    internal static void ClearCoroutineRunnerCache()
    {
        CoroutineHelper.ClearCoroutineRunnerCache(); // Will change to this
    }
}