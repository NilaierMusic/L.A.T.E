using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using Photon.Realtime;
using UnityEngine;

namespace L.A.T.E
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    internal sealed class LateJoinEntry : BaseUnityPlugin
    {
        // Static properties to hold the BepInEx logger and Harmony instance.
        internal static ManualLogSource Log { get; private set; } = null!;
        internal static Harmony HarmonyInstance { get; private set; } = null!;

        // Private cached fields.
        private static readonly List<Hook> _hooks = new List<Hook>(); // Keep hooks alive.
        private static MonoBehaviour? _coroutineRunner;

        // Public property to lazily initialize the CoroutineRunner.
        internal static MonoBehaviour? CoroutineRunner
        {
            get => _coroutineRunner ??= Utilities.FindCoroutineRunner();
        }

        // MonoMod hook definitions.
        private static readonly (Type TargetType, string TargetMethod, Type HookType, string HookMethod)[] _monoModHooks =
        {
            (typeof(RunManager), "ChangeLevel", typeof(Patches), nameof(Patches.RunManager_ChangeLevelHook)),
            (typeof(PlayerAvatar), "Spawn", typeof(Patches), nameof(Patches.PlayerAvatar_SpawnHook)),
            (typeof(PlayerAvatar), "Start", typeof(Patches), nameof(Patches.PlayerAvatar_StartHook))
        };

        // Explicit Harmony patches definitions.
        private static readonly (Type TargetType, string TargetMethod, Type PatchType, string PatchMethod, Type[]? Args, bool Postfix)[] _explicitHarmonyPatches =
        {
            (typeof(NetworkManager), nameof(NetworkManager.OnPlayerEnteredRoom), typeof(Patches), nameof(Patches.NetworkManager_OnPlayerEnteredRoom_Postfix), new[] { typeof(Player) }, true),
            (typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom), typeof(Patches), nameof(Patches.NetworkManager_OnPlayerLeftRoom_Postfix), new[] { typeof(Player) }, true)
        };

        // Unity Awake – called when the plugin loads.
        private void Awake()
        {
            Log = Logger; // Bind the BepInEx logger.
            HarmonyInstance = new Harmony(PluginInfo.PLUGIN_GUID);

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} is loading…");

            GameVersion detectedVersion = GameVersionSupport.DetectVersion();
            Log.LogInfo($"Detected Game Version: {detectedVersion}");
            if (detectedVersion == GameVersion.Unknown)
            {
                Log.LogError("Failed to determine game version. Mod features related to Steam lobby management might not work correctly.");
                // Depending on how critical this is, you might want to disable the mod or parts of it.
            }

            ConfigManager.Initialize(Config); // Load configuration.

            ApplyMonoModHooks();
            ApplyHarmonyPatches();

            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} finished loading!");
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
                HarmonyInstance.PatchAll(typeof(Patches));
                HarmonyInstance.PatchAll(typeof(NetworkConnect_Patches));
                HarmonyInstance.PatchAll(typeof(TruckScreenText_ChatBoxState_EarlyLock_Patches));

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
        internal static void ClearCoroutineRunnerCache()
        {
            Log.LogDebug("Clearing cached CoroutineRunner.");
            _coroutineRunner = null;
        }
    }
}