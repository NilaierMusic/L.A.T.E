// File: L.A.T.E/Patches/Enemies/EnemyVisionPatches.cs
using HarmonyLib;
using UnityEngine; // For Collider
using LATE.Core;    // For LatePlugin.Log
using LATE.Utilities; // For GameUtilities, ReflectionCache

namespace LATE.Patches.Enemies; // File-scoped namespace

/// <summary>
/// Contains Harmony patches for the EnemyVision and related classes
/// to prevent issues with missing dictionary keys, often occurring with late-joiners.
/// </summary>
[HarmonyPatch]
internal static class EnemyVisionPatches
{
    /// <summary>
    /// Harmony Prefix for EnemyVision.VisionTrigger.
    /// Ensures the playerID exists in the vision dictionaries before the original method proceeds.
    /// If missing, attempts to add the player via PlayerAdded as a fallback.
    /// </summary>
    [HarmonyPatch(typeof(EnemyVision), nameof(EnemyVision.VisionTrigger))]
    [HarmonyPrefix]
    static bool EnemyVision_VisionTrigger_Prefix(EnemyVision __instance, int playerID, PlayerAvatar player)
    {
        // Early exit if critical components are missing
        if (__instance == null || player == null)
        {
            LatePlugin.Log.LogWarning("[EnemyVisionPatches.VisionTrigger_Prefix] Instance or player is null. Letting original method run.");
            return true; // Let original run maybe handle null?
        }

        bool needToAdd = false;
        string missingDict = "";

        // Check if keys exist
        if (!__instance.VisionsTriggered.ContainsKey(playerID))
        {
            needToAdd = true;
            missingDict = "VisionsTriggered";
        }
        else if (!__instance.VisionTriggered.ContainsKey(playerID)) // Check the second dictionary too
        {
            needToAdd = true;
            missingDict = "VisionTriggered";
        }

        if (needToAdd)
        {
            string enemyName = __instance.gameObject?.name ?? "UnknownEnemy";
            string playerName = player.photonView?.Owner?.NickName ?? $"ViewID {playerID}";
            LatePlugin.Log.LogWarning(
                $"[EnemyVisionPatches.VisionTrigger_Prefix] Key {playerID} ({playerName}) missing in '{missingDict}' for enemy '{enemyName}'. Attempting recovery."
            );

            try
            {
                // Attempt to add the player to the dictionaries
                __instance.PlayerAdded(playerID);

                // Re-check if the key was successfully added
                if (!__instance.VisionsTriggered.ContainsKey(playerID) || !__instance.VisionTriggered.ContainsKey(playerID))
                {
                    LatePlugin.Log.LogError(
                        $"[EnemyVisionPatches.VisionTrigger_Prefix] FAILED to add key {playerID} via PlayerAdded for enemy '{enemyName}'. Skipping original trigger logic to prevent crash."
                    );
                    return false; // Prevent original method execution
                }
                else
                {
                    LatePlugin.Log.LogInfo(
                        $"[EnemyVisionPatches.VisionTrigger_Prefix] Successfully recovered key {playerID} for enemy '{enemyName}'."
                    );
                }
            }
            catch (System.Exception ex)
            {
                LatePlugin.Log.LogError(
                    $"[EnemyVisionPatches.VisionTrigger_Prefix] Exception during PlayerAdded recovery for key {playerID} on enemy '{enemyName}': {ex}"
                );
                return false; // Prevent original method execution on error
            }
        }
        return true; // Proceed with original method
    }

    /// <summary>
    /// Harmony Prefix for EnemyTriggerAttack.OnTriggerStay.
    /// Ensures the player's ViewID exists in the associated EnemyVision's dictionary
    /// before the original method accesses it.
    /// </summary>
    [HarmonyPatch(typeof(EnemyTriggerAttack), "OnTriggerStay")] // OnTriggerStay is typically private or protected
    [HarmonyPrefix]
    static bool EnemyTriggerAttack_OnTriggerStay_Prefix(EnemyTriggerAttack __instance, Collider other)
    {
        if (__instance == null || other == null || __instance.Enemy == null)
        {
            return true; // Minimal checks, let original handle if these are null
        }

        // Use GameUtilities to get the EnemyVision instance, which handles reflection and fallbacks.
        EnemyVision? visionInstance = GameUtilities.GetEnemyVision(__instance.Enemy);
        if (visionInstance == null)
        {
            // GameUtilities.GetEnemyVision already logs if it fails.
            return true; // Cannot proceed with check/fix, let original run.
        }

        // Check if the collider belongs to a player
        PlayerTrigger component = other.GetComponent<PlayerTrigger>();
        if (component != null && component.PlayerAvatar != null && component.PlayerAvatar.photonView != null)
        {
            PlayerAvatar playerAvatar = component.PlayerAvatar;
            int viewID = playerAvatar.photonView.ViewID;

            // The critical check using the resolved visionInstance
            if (!visionInstance.VisionTriggered.ContainsKey(viewID))
            {
                string enemyName = __instance.gameObject?.name ?? "UnknownEnemyTrigger";
                string playerName = playerAvatar.photonView?.Owner?.NickName ?? $"ViewID {viewID}";
                LatePlugin.Log.LogWarning(
                    $"[EnemyVisionPatches.OnTriggerStay_Prefix] Key {viewID} ({playerName}) missing in 'VisionTriggered' for enemy '{enemyName}'. Attempting recovery."
                );

                try
                {
                    // Attempt recovery by calling PlayerAdded on the vision instance
                    visionInstance.PlayerAdded(viewID);

                    // Re-check if the key was successfully added
                    if (!visionInstance.VisionTriggered.ContainsKey(viewID))
                    {
                        LatePlugin.Log.LogError(
                            $"[EnemyVisionPatches.OnTriggerStay_Prefix] FAILED to add key {viewID} via PlayerAdded for enemy '{enemyName}'."
                        );
                        // Consider returning false here if missing key leads to inevitable crash in original.
                        // For now, let's assume original method might have its own checks or it's not always fatal.
                    }
                    else
                    {
                        LatePlugin.Log.LogInfo(
                            $"[EnemyVisionPatches.OnTriggerStay_Prefix] Successfully recovered key {viewID} for enemy '{enemyName}'."
                        );
                    }
                }
                catch (System.Exception ex)
                {
                    LatePlugin.Log.LogError(
                        $"[EnemyVisionPatches.OnTriggerStay_Prefix] Exception during PlayerAdded recovery for key {viewID} on enemy '{enemyName}': {ex}"
                    );
                }
            }
        }
        return true; // Let the original OnTriggerStay logic run
    }
}