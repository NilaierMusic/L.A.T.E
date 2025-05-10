// File: L.A.T.E/Core/PatchManager.cs
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using Photon.Realtime;
using System.Reflection;

namespace LATE.Core;

internal static class PatchManager
{
    private static Harmony? _harmonyInstance;
    private static ManualLogSource? _logger;
    private static readonly List<Hook> _hooks = new List<Hook>();

    private static readonly Type? _oldPatchesClassType = Type.GetType("LATE.Patches, LATE");

    private static readonly (Type TargetType, string TargetMethod, Type? HookType, string HookMethod)[] _monoModHooks =
    {
        (typeof(RunManager), "ChangeLevel", typeof(LATE.Patches.CoreGame.RunManagerPatches), nameof(LATE.Patches.CoreGame.RunManagerPatches.RunManager_ChangeLevelHook)),
        (typeof(PlayerAvatar), "Spawn", typeof(LATE.Patches.Player.PlayerAvatarPatches), nameof(LATE.Patches.Player.PlayerAvatarPatches.PlayerAvatar_SpawnHook)),
        (typeof(PlayerAvatar), "Start", typeof(LATE.Patches.Player.PlayerAvatarPatches), nameof(LATE.Patches.Player.PlayerAvatarPatches.PlayerAvatar_StartHook)),
    };

    private static readonly (Type TargetType, string TargetMethod, Type? PatchType, string HookMethod, Type[]? Args, bool Postfix)[] _explicitHarmonyPatches =
    {
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerEnteredRoom), typeof(LATE.Patches.Player.NetworkManagerPatches), nameof(LATE.Patches.Player.NetworkManagerPatches.NetworkManager_OnPlayerEnteredRoom_Postfix), new[] { typeof(Player) }, true), // Updated
        (typeof(NetworkManager), nameof(NetworkManager.OnPlayerLeftRoom), typeof(LATE.Patches.Player.NetworkManagerPatches), nameof(LATE.Patches.Player.NetworkManagerPatches.NetworkManager_OnPlayerLeftRoom_Postfix), new[] { typeof(Player) }, true), // Updated
    };

    internal static void InitializeAndApplyPatches(Harmony harmonyInstance, ManualLogSource logger)
    {
        _harmonyInstance = harmonyInstance ?? throw new ArgumentNullException(nameof(harmonyInstance));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_oldPatchesClassType == null)
        {
            // This warning is fine if we successfully migrate everything from LATE.Patches
            _logger.LogInfo("[PatchManager] Old 'LATE.Patches' class not found. This is expected if all patches are migrated.");
        }
        else
        {
            _logger.LogInfo($"[PatchManager] Found old 'LATE.Patches' class type: {_oldPatchesClassType.FullName}. Will attempt to use it if explicit patches still point there.");
        }

        ApplyMonoModHooks();
        ApplyHarmonyPatches();
    }

    private static void ApplyMonoModHooks()
    {
        _logger!.LogInfo("[PatchManager] Applying MonoMod hooks…");
        foreach (var (targetType, targetMethod, hookType, hookMethod) in _monoModHooks)
        {
            // hookType should always be non-null now as we are migrating
            if (hookType == null)
            {
                _logger!.LogWarning($"[MonoMod] Skipping hook for {targetType.Name}.{targetMethod} because its hook type was not resolved. This should not happen post-migration.");
                continue;
            }
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
            _logger!.LogError($"[MonoMod] Hook method not found: {hookType.FullName}.{hookMethodName} (is it public static?)");
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
            _harmonyInstance!.PatchAll(typeof(LATE.Patches.CoreGame.RunManagerPatches));
            _harmonyInstance!.PatchAll(typeof(LATE.Patches.CoreGame.GameDirectorPatches));
            _harmonyInstance!.PatchAll(typeof(LATE.Patches.CoreGame.NetworkConnectPatches));
            _harmonyInstance!.PatchAll(typeof(LATE.Patches.Player.PlayerAvatarPatches));
            _harmonyInstance!.PatchAll(typeof(LATE.Patches.Player.NetworkManagerPatches)); // Added this line

            // Remaining attribute patches from old monolithic class
            PatchOldClassIfExists("LATE.Patches, LATE", "old LATE.Patches (monolithic attribute patches)");
            // Explicitly named old patch classes
            PatchOldClassIfExists("LATE.TruckScreenText_ChatBoxState_EarlyLock_Patches, LATE", "old LATE.TruckScreenText_ChatBoxState_EarlyLock_Patches");

            // Apply explicit patches (should now be fully migrated or pointing to new classes)
            foreach (var (targetType, targetMethod, patchType, patchMethodName, args, postfix) in _explicitHarmonyPatches)
            {
                // patchType should always be non-null now
                if (patchType == null)
                {
                    _logger!.LogWarning($"[Harmony] Skipping explicit patch for {targetType.Name}.{targetMethod} because its patch type was not resolved. This should not happen post-migration.");
                    continue;
                }
                TryApplyHarmonyPatch(targetType, targetMethod, patchType, patchMethodName, args, postfix);
            }
            _logger!.LogInfo("[PatchManager] Harmony patch application process finished.");
        }
        catch (Exception ex)
        {
            _logger!.LogError($"[PatchManager] Failed to apply Harmony patches: {ex}");
        }
    }

    private static void PatchOldClassIfExists(string typeNameWithAssembly, string description)
    {
        Type? oldType = Type.GetType(typeNameWithAssembly);
        if (oldType != null)
        {
            _logger!.LogInfo($"[PatchManager] Applying Harmony attribute patches from {description} (Type: {oldType.FullName}). This is temporary and should be removed once all patches are migrated.");
            _harmonyInstance!.PatchAll(oldType);
        }
        else
        {
            _logger!.LogInfo($"[PatchManager] Could not find type '{typeNameWithAssembly}' for temporary patching of {description}. If all patches from it are migrated, this is expected.");
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
            _logger!.LogError($"[Harmony] Patch method not found: {patchType.FullName}.{patchMethodName} (is it public static?)");
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