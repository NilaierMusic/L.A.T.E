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
using LATE.Config;
using LATE.Managers.GameState;
using LATE.DataModels;
// LATE.Patches, LATE.NetworkConnect_Patches etc will be updated as those files are refactored
// For now, assuming they might be in the root LATE namespace or a LATE.Patches namespace
// We will use fully qualified names for now if they are still in root LATE namespace to avoid ambiguity.

namespace LATE.Core; // File-scoped namespace

[BepInPlugin(LATE.PluginInfo.PLUGIN_GUID, LATE.PluginInfo.PLUGIN_NAME, LATE.PluginInfo.PLUGIN_VERSION)]
internal sealed class LatePlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static Harmony HarmonyInstance { get; private set; } = null!;

    // This list will be moved to a dedicated PatchManager in LATE.Core
    private static readonly List<Hook> _hooks = new List<Hook>();

    // This property now delegates to CoroutineHelper
    internal static MonoBehaviour? CoroutineRunner => CoroutineHelper.CoroutineRunner;

    // These definitions will be moved to a dedicated PatchManager in LATE.Core
    // For now, their types will need to be fully qualified if they are not yet in LATE.Patches namespace
    private static readonly (Type TargetType, string TargetMethod, Type HookType, string HookMethod)[] _monoModHooks =
    {
        // Assuming old LATE.Patches class for now
        (typeof(RunManager), "ChangeLevel", typeof(LATE.Patches), nameof(LATE.Patches.RunManager_ChangeLevelHook)),
        (typeof(PlayerAvatar), "Spawn", typeof(LATE.Patches), nameof(LATE.Patches.PlayerAvatar_SpawnHook)),
        (typeof(PlayerAvatar), "Start", typeof(LATE.Patches), nameof(LATE.Patches.PlayerAvatar_StartHook))
    };

    private static readonly (Type TargetType, string TargetMethod, Type PatchType, string PatchMethod, Type[]? Args, bool Postfix)[] _explicitHarmonyPatches =
    {
        // Assuming old LATE.Patches class for now
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerEnteredRoom), typeof(LATE.Patches), nameof(LATE.Patches.NetworkManager_OnPlayerEnteredRoom_Postfix), new[] { typeof(Player) }, true),
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom), typeof(LATE.Patches), nameof(LATE.Patches.NetworkManager_OnPlayerLeftRoom_Postfix), new[] { typeof(Player) }, true)
    };

    private void Awake()
    {
        Log = Logger;
        HarmonyInstance = new Harmony(LATE.PluginInfo.PLUGIN_GUID);

        Log.LogInfo($"{LATE.PluginInfo.PLUGIN_NAME} v{LATE.PluginInfo.PLUGIN_VERSION} is loading…");

        GameVersion detectedVersion = GameVersionSupport.DetectVersion();
        Log.LogInfo($"Detected Game Version: {detectedVersion}");
        if (detectedVersion == GameVersion.Unknown)
        {
            Log.LogError("Failed to determine game version. Mod features related to Steam lobby management might not work correctly.");
        }

        ConfigManager.Initialize(Config);

        // Patch application will be moved to a dedicated PatchManager class
        ApplyMonoModHooks();
        ApplyHarmonyPatches();

        Log.LogInfo($"{LATE.PluginInfo.PLUGIN_NAME} finished loading!");
    }

    // This method will be moved to Core.PatchManager
    private static void ApplyMonoModHooks()
    {
        Log.LogInfo("Applying MonoMod hooks…");
        foreach (var (targetType, targetMethod, hookType, hookMethod) in _monoModHooks)
        {
            TryApplyMonoModHook(targetType, targetMethod, hookType, hookMethod);
        }
        Log.LogInfo("MonoMod hook application finished.");
    }

    // This method will be moved to Core.PatchManager
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
            _hooks.Add(new Hook(targetMI, hookMI));
            Log.LogDebug($"[MonoMod] Hooked {targetType.Name}.{targetMethodName}");
        }
        catch (Exception ex)
        {
            Log.LogError($"[MonoMod] Exception while hooking {targetType.Name}.{targetMethodName}: {ex}");
        }
    }

    // This method will be moved to Core.PatchManager
    private static void ApplyHarmonyPatches()
    {
        Log.LogInfo("Applying Harmony patches…");
        try
        {
            // Assuming old LATE.Patches, LATE.NetworkConnect_Patches, LATE.TruckScreenText_ChatBoxState_EarlyLock_Patches class for now
            HarmonyInstance.PatchAll(typeof(LATE.Patches));
            HarmonyInstance.PatchAll(typeof(LATE.NetworkConnect_Patches));
            HarmonyInstance.PatchAll(typeof(LATE.TruckScreenText_ChatBoxState_EarlyLock_Patches));

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

    // This method will be moved to Core.PatchManager
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

    // This method will be moved to Core.PatchManager
    private static MethodInfo? FindMethod(Type type, string methodName, Type[]? arguments = null)
    {
        return arguments == null
            ? AccessTools.Method(type, methodName)
            : AccessTools.Method(type, methodName, arguments);
    }

    // This method is now handled by CoroutineHelper
    internal static void ClearCoroutineRunnerCache()
    {
        CoroutineHelper.ClearCoroutineRunnerCache();
    }
}