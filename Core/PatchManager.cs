// File: L.A.T.E/Core/PatchManager.cs
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace LATE.Core
{
    internal static class PatchManager
    {
        private static Harmony? _harmonyInstance;
        private static ManualLogSource? _logger;
        private static readonly List<Hook> _hooks = new List<Hook>();

        // Tuple arrays describing hooks to apply via MonoMod
        // Format: (TargetType, TargetMethod, HookType, HookMethod)
        private static readonly (Type TargetType, string TargetMethod, Type? HookType, string HookMethod)[] _monoModHooks =
        {
            (typeof(RunManager), "ChangeLevel", typeof(LATE.Patches.CoreGame.RunManagerPatches), nameof(LATE.Patches.CoreGame.RunManagerPatches.RunManager_ChangeLevelHook)),
            (typeof(PlayerAvatar), "Spawn", typeof(LATE.Patches.Player.PlayerAvatarPatches), nameof(LATE.Patches.Player.PlayerAvatarPatches.PlayerAvatar_SpawnHook)),
            (typeof(PlayerAvatar), "Start", typeof(LATE.Patches.Player.PlayerAvatarPatches), nameof(LATE.Patches.Player.PlayerAvatarPatches.PlayerAvatar_StartHook)),
        };

        // Tuple arrays describing explicit Harmony patches
        // Format: (TargetType, TargetMethod, PatchType, HookMethod, ArgTypes, IsPostfix)
        private static readonly (Type TargetType, string TargetMethod, Type? PatchType, string HookMethod, Type[]? Args, bool Postfix)[] _explicitHarmonyPatches =
        {
            (typeof(NetworkManager), nameof(NetworkManager.OnPlayerEnteredRoom),
                typeof(LATE.Patches.Player.NetworkManagerPatches), nameof(LATE.Patches.Player.NetworkManagerPatches.NetworkManager_OnPlayerEnteredRoom_Postfix),
                new[] { typeof(Photon.Realtime.Player) }, true),
            (typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom),
                typeof(LATE.Patches.Player.NetworkManagerPatches), nameof(LATE.Patches.Player.NetworkManagerPatches.NetworkManager_OnPlayerLeftRoom_Postfix),
                new[] { typeof(Photon.Realtime.Player) }, true),
        };

        /// <summary>
        /// Initializes the patching system by storing the provided Harmony instance and logger,
        /// then applying MonoMod hooks and Harmony patches.
        /// </summary>
        internal static void InitializeAndApplyPatches(Harmony harmonyInstance, ManualLogSource logger)
        {
            _harmonyInstance = harmonyInstance ?? throw new ArgumentNullException(nameof(harmonyInstance));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            ApplyMonoModHooks();
            ApplyHarmonyPatches();
        }

        #region MonoMod Hooking

        private static void ApplyMonoModHooks()
        {
            _logger!.LogInfo("[PatchManager] Applying MonoMod hooks…");
            foreach (var (targetType, targetMethod, hookType, hookMethod) in _monoModHooks)
            {
                if (hookType == null)
                {
                    _logger.LogWarning($"[MonoMod] Skipping hook for {targetType.Name}.{targetMethod} because its hook type was not resolved (this might be okay if it's an old, removed hook definition).");
                    continue;
                }
                TryApplyMonoModHook(targetType, targetMethod, hookType, hookMethod);
            }
            _logger.LogInfo("[PatchManager] MonoMod hook application finished.");
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
                _logger!.LogError($"[MonoMod] Hook method not found: {hookType.FullName}.{hookMethodName} (is it public and static?)");
                return;
            }

            try
            {
                Hook hook = new Hook(targetMI, hookMI);
                _hooks.Add(hook);
                _logger!.LogDebug($"[MonoMod] Successfully hooked {targetType.Name}.{targetMethodName} with {hookType.Name}.{hookMethodName}");
            }
            catch (Exception ex)
            {
                _logger!.LogError($"[MonoMod] Exception while hooking {targetType.Name}.{targetMethodName}: {ex}");
            }
        }

        #endregion

        #region Harmony Patching

        private static void ApplyHarmonyPatches()
        {
            _logger!.LogInfo("[PatchManager] Applying Harmony attribute-driven patches…");
            try
            {
                // Core Game Systems
                _harmonyInstance!.PatchAll(typeof(LATE.Patches.CoreGame.RunManagerPatches));
                _harmonyInstance.PatchAll(typeof(LATE.Patches.CoreGame.GameDirectorPatches));
                _harmonyInstance.PatchAll(typeof(LATE.Patches.CoreGame.NetworkConnectPatches));

                // Player Systems
                _harmonyInstance.PatchAll(typeof(LATE.Patches.Player.PlayerAvatarPatches));
                _harmonyInstance.PatchAll(typeof(LATE.Patches.Player.PlayerControllerPatches));
                _harmonyInstance.PatchAll(typeof(LATE.Patches.Player.NetworkManagerPatches));

                // Object Systems
                _harmonyInstance.PatchAll(typeof(LATE.Patches.Objects.PhysGrabObjectPatches));
                _harmonyInstance.PatchAll(typeof(LATE.Patches.Objects.PhysGrabHingePatches));

                // Enemy Systems
                _harmonyInstance.PatchAll(typeof(LATE.Patches.Enemies.EnemyVisionPatches));

                // UI Systems
                _harmonyInstance.PatchAll(typeof(LATE.Patches.UI.TruckScreenTextPatches));

                _logger.LogInfo("[PatchManager] Attribute-driven patching complete.");
                _logger.LogInfo("[PatchManager] Applying explicit Harmony patches…");
                foreach (var (targetType, targetMethod, patchType, patchMethodName, args, postfix) in _explicitHarmonyPatches)
                {
                    if (patchType == null)
                    {
                        _logger.LogWarning($"[Harmony] Skipping explicit patch for {targetType.Name}.{targetMethod} because its patch type was not resolved (this might be okay if it's an old or removed patch definition).");
                        continue;
                    }
                    TryApplyHarmonyPatch(targetType, targetMethod, patchType, patchMethodName, args, postfix);
                }
                _logger.LogInfo("[PatchManager] Explicit Harmony patch application finished.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[PatchManager] Failed to apply Harmony patches: {ex}");
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
                _logger!.LogError($"[Harmony] Patch method not found: {patchType.FullName}.{patchMethodName} (is it public and static?)");
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

        #endregion

        #region Reflection Helpers

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
                _logger?.LogError($"[Reflection] Error finding method {type.Name}.{methodName} with specified arguments: {ex.Message}");
                return null;
            }
        }

        #endregion
    }
}