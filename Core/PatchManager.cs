// File: L.A.T.E/Core/PatchManager.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using Photon.Realtime; // For Player type in explicit patch definition
// Required for patch target types
using LATE.Patches.CoreGame; // Will be added as patches are moved
// For now, assume old patch classes might be in LATE or LATE.Patches
// We will update these as we move patches.

namespace LATE.Core;

/// <summary>
/// Manages the application of all Harmony and MonoMod patches for the plugin.
/// </summary>
internal static class PatchManager
{
    private static Harmony? _harmonyInstance;
    private static ManualLogSource? _logger;
    private static readonly List<Hook> _hooks = new List<Hook>(); // Keeps MonoMod hooks alive

    // MonoMod hook definitions
    // These will be updated as patch methods are moved to their new classes.
    private static readonly (Type TargetType, string TargetMethod, Type HookType, string HookMethod)[] _monoModHooks =
    {
        // Example for RunManager, others will follow this pattern
        (typeof(RunManager), "ChangeLevel", typeof(LATE.Patches.CoreGame.RunManagerPatches), nameof(LATE.Patches.CoreGame.RunManagerPatches.RunManager_ChangeLevelHook)),
        (typeof(PlayerAvatar), "Spawn", typeof(LATE.Patches.Player.PlayerAvatarPatches), nameof(LATE.Patches.Player.PlayerAvatarPatches.PlayerAvatar_SpawnHook)), // Placeholder, will be LATE.Patches.Player...
        (typeof(PlayerAvatar), "Start", typeof(LATE.Patches.Player.PlayerAvatarPatches), nameof(LATE.Patches.Player.PlayerAvatarPatches.PlayerAvatar_StartHook)), // Placeholder
    };

    // Explicit Harmony patches definitions
    // These will also be updated.
    private static readonly (Type TargetType, string TargetMethod, Type PatchType, string PatchMethod, Type[]? Args, bool Postfix)[] _explicitHarmonyPatches =
    {
        // Placeholder, will be LATE.Patches.Player...
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerEnteredRoom), typeof(LATE.Patches.Player.NetworkManagerPatches), nameof(LATE.Patches.Player.NetworkManagerPatches.NetworkManager_OnPlayerEnteredRoom_Postfix), new[] { typeof(Player) }, true),
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom), typeof(LATE.Patches.Player.NetworkManagerPatches), nameof(LATE.Patches.Player.NetworkManagerPatches.NetworkManager_OnPlayerLeftRoom_Postfix), new[] { typeof(Player) }, true),
    };

    /// <summary>
    /// Initializes and applies all patches. Called once from LatePlugin.Awake().
    /// </summary>
    /// <param name="harmonyInstance">The Harmony instance to use for patching.</param>
    /// <param name="logger">The logger for logging patch progress and errors.</param>
    internal static void InitializeAndApplyPatches(Harmony harmonyInstance, ManualLogSource logger)
    {
        _harmonyInstance = harmonyInstance ?? throw new ArgumentNullException(nameof(harmonyInstance));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        ApplyMonoModHooks();
        ApplyHarmonyPatches(); // This will apply attribute patches from various new classes
    }

    private static void ApplyMonoModHooks()
    {
        _logger!.LogInfo("[PatchManager] Applying MonoMod hooks…");
        foreach (var (targetType, targetMethod, hookType, hookMethod) in _monoModHooks)
        {
            TryApplyMonoModHook(targetType, targetMethod, hookType, hookMethod);
        }
        _logger!.LogInfo("[PatchManager] MonoMod hook application finished.");
    }

    private static void TryApplyMonoModHook(Type targetType, string targetMethodName, Type hookType, string hookMethodName)
    {
        MethodInfo? targetMI = FindMethod(targetType, targetMethodName);
        MethodInfo? hookMI = FindMethod(hookType, hookMethodName);

        if (targetMI == null)
        {
            _logger!.LogError($"[MonoMod] Target method not found: {targetType.FullName}.{targetMethodName}");
            return;
        }
        if (hookMI == null)
        {
            _logger!.LogError($"[MonoMod] Hook method not found: {hookType.FullName}.{hookMethodName}");
            return;
        }

        try
        {
            _hooks.Add(new Hook(targetMI, hookMI));
            _logger!.LogDebug($"[MonoMod] Successfully hooked {targetType.Name}.{targetMethodName} with {hookType.Name}.{hookMethodName}");
        }
        catch (Exception ex)
        {
            _logger!.LogError($"[MonoMod] Exception while hooking {targetType.Name}.{targetMethodName}: {ex}");
        }
    }

    private static void ApplyHarmonyPatches()
    {
        _logger!.LogInfo("[PatchManager] Applying Harmony patches…");
        try
        {
            // Attribute-driven patches will be discovered from various assemblies/types.
            // As we create new patch files, we'll add PatchAll calls for their respective types/assemblies.
            // Example:
            _harmonyInstance!.PatchAll(typeof(LATE.Patches.CoreGame.RunManagerPatches));
            // _harmonyInstance.PatchAll(typeof(LATE.Patches.CoreGame.GameDirectorPatches)); // When created
            // _harmonyInstance.PatchAll(typeof(LATE.Patches.CoreGame.NetworkConnectPatches)); // When created
            // _harmonyInstance.PatchAll(typeof(LATE.Patches.Player.PlayerAvatarPatches)); // When created
            // ... and so on for all new patch classes.

            // For now, to keep the build green until all patches are moved,
            // we might need to temporarily patch the old LATE.Patches if it still has methods.
            // This will be removed once all methods are migrated.
            Type? oldPatchesType = Type.GetType("LATE.Patches, L.A.T.E"); // Attempt to find the old class
            if (oldPatchesType != null)
            {
                _logger!.LogWarning("[PatchManager] Temporarily patching old LATE.Patches class. This should be removed after full refactor.");
                _harmonyInstance!.PatchAll(oldPatchesType);
            }
            Type? oldNetworkConnectPatchesType = Type.GetType("LATE.NetworkConnect_Patches, L.A.T.E");
            if (oldNetworkConnectPatchesType != null)
            {
                _logger!.LogWarning("[PatchManager] Temporarily patching old LATE.NetworkConnect_Patches class.");
                _harmonyInstance!.PatchAll(oldNetworkConnectPatchesType);
            }
            Type? oldTruckScreenPatchesType = Type.GetType("LATE.TruckScreenText_ChatBoxState_EarlyLock_Patches, L.A.T.E");
            if (oldTruckScreenPatchesType != null)
            {
                _logger!.LogWarning("[PatchManager] Temporarily patching old LATE.TruckScreenText_ChatBoxState_EarlyLock_Patches class.");
                _harmonyInstance!.PatchAll(oldTruckScreenPatchesType);
            }


            // Apply explicit patches (with parameters and postfix options)
            foreach (var (targetType, targetMethod, patchType, patchMethod, args, postfix) in _explicitHarmonyPatches)
            {
                TryApplyHarmonyPatch(targetType, targetMethod, patchType, patchMethod, args, postfix);
            }
            _logger!.LogInfo("[PatchManager] Harmony patch application process finished.");
        }
        catch (Exception ex)
        {
            _logger!.LogError($"[PatchManager] Failed to apply Harmony patches: {ex}");
        }
    }

    private static void TryApplyHarmonyPatch(Type targetType, string targetMethodName, Type patchType, string patchMethodName, Type[]? arguments, bool postfix)
    {
        MethodInfo? targetMI = FindMethod(targetType, targetMethodName, arguments);
        MethodInfo? patchMI = FindMethod(patchType, patchMethodName);

        if (targetMI == null)
        {
            _logger!.LogError($"[Harmony] Target method not found: {targetType.FullName}.{targetMethodName}");
            return;
        }
        if (patchMI == null)
        {
            _logger!.LogError($"[Harmony] Patch method not found: {patchType.FullName}.{patchMethodName}");
            return;
        }

        HarmonyMethod harmonyMethod = new HarmonyMethod(patchMI);
        if (postfix)
        {
            _harmonyInstance!.Patch(targetMI, postfix: harmonyMethod);
        }
        else
        {
            _harmonyInstance!.Patch(targetMI, prefix: harmonyMethod);
        }
        _logger!.LogDebug($"[Harmony] Successfully patched {targetType.Name}.{targetMethodName} with {patchType.Name}.{patchMethodName} ({(postfix ? "postfix" : "prefix")})");
    }

    private static MethodInfo? FindMethod(Type type, string methodName, Type[]? arguments = null)
    {
        try
        {
            return arguments == null
                ? AccessTools.Method(type, methodName)
                : AccessTools.Method(type, methodName, arguments);
        }
        catch (Exception ex)
        {
            _logger?.LogError($"[Reflection] Error finding method {type.Name}.{methodName}: {ex.Message}");
            return null;
        }
    }
}