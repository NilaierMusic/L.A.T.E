// File: L.A.T.E/Patches/Enemies/EnemyVisionPatches.cs
using System;                        // Exception
using HarmonyLib;

using UnityEngine;                   // Collider

using LATE.Core;                     // LatePlugin.Log
using LATE.Utilities;                // GameUtilities, ReflectionCache

namespace LATE.Patches.Enemies;

/// <summary>
/// Harmony patches that harden EnemyVision / EnemyTriggerAttack against the
/// “missing dictionary key” crashes that late-joiners can trigger.
/// </summary>
[HarmonyPatch]
internal static class EnemyVisionPatches
{
    private const string LogPrefix = "[EnemyVisionPatches]";

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  EnemyVision.VisionTrigger PREFIX                                        */
    /*───────────────────────────────────────────────────────────────────────────*/

    [HarmonyPatch(typeof(EnemyVision), nameof(EnemyVision.VisionTrigger))]
    [HarmonyPrefix]
    private static bool VisionTrigger_Prefix(EnemyVision __instance, int playerID, PlayerAvatar player)
    {
        if (__instance == null || player == null)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} VisionTrigger: instance or player NULL – letting original run.");
            return true;
        }

        string? missingDict = GetMissingDict(__instance, playerID);
        if (missingDict == null) return true; // all good, let original run

        string enemyName = __instance.gameObject?.name ?? "UnknownEnemy";
        string playerName = player.photonView?.Owner?.NickName ?? $"ViewID {playerID}";

        LatePlugin.Log.LogWarning(
            $"{LogPrefix} VisionTrigger: key {playerID} ({playerName}) missing in '{missingDict}' for '{enemyName}'. Attempting recovery.");

        if (!TryAddPlayer(__instance, playerID, enemyName, "VisionTrigger")) return false;

        LatePlugin.Log.LogInfo($"{LogPrefix} VisionTrigger: successfully recovered key {playerID} for '{enemyName}'.");
        return true;
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  EnemyTriggerAttack.OnTriggerStay PREFIX                                 */
    /*───────────────────────────────────────────────────────────────────────────*/

    // Note: original method is usually private; Harmony accepts the string name
    [HarmonyPatch(typeof(EnemyTriggerAttack), "OnTriggerStay")]
    [HarmonyPrefix]
    private static bool TriggerAttack_OnTriggerStay_Prefix(EnemyTriggerAttack __instance, Collider other)
    {
        if (__instance?.Enemy == null || other == null) return true;

        EnemyVision? vision = GameUtilities.GetEnemyVision(__instance.Enemy);
        if (vision == null) return true;   // GameUtilities already logged failure

        if (other.GetComponent<PlayerTrigger>() is not { PlayerAvatar: { } avatar }) return true;
        if (avatar.photonView == null) return true;

        int viewID = avatar.photonView.ViewID;
        if (vision.VisionTriggered.ContainsKey(viewID)) return true;   // nothing to fix

        string enemyName = __instance.gameObject?.name ?? "UnknownEnemyTrigger";
        string playerName = avatar.photonView.Owner?.NickName ?? $"ViewID {viewID}";

        LatePlugin.Log.LogWarning(
            $"{LogPrefix} OnTriggerStay: key {viewID} ({playerName}) missing in 'VisionTriggered' for '{enemyName}'. Attempting recovery.");

        TryAddPlayer(vision, viewID, enemyName, "OnTriggerStay");
        return true; // still allow original OnTriggerStay to execute
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Helpers                                                                 */
    /*───────────────────────────────────────────────────────────────────────────*/

    // Returns the name of the first dict missing the key, or null if both present.
    private static string? GetMissingDict(EnemyVision ev, int id) =>
        !ev.VisionsTriggered.ContainsKey(id) ? "VisionsTriggered"
        : !ev.VisionTriggered.ContainsKey(id) ? "VisionTriggered"
        : null;

    private static bool TryAddPlayer(EnemyVision ev, int id, string enemyName, string context)
    {
        try
        {
            ev.PlayerAdded(id);

            if (ev.VisionsTriggered.ContainsKey(id) && ev.VisionTriggered.ContainsKey(id))
                return true;

            LatePlugin.Log.LogError(
                $"{LogPrefix} {context}: FAILED to add key {id} via PlayerAdded for enemy '{enemyName}'. Skipping original logic to prevent crash.");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError(
                $"{LogPrefix} {context}: Exception during PlayerAdded recovery for key {id} on '{enemyName}': {ex}");
        }

        // Returning false from prefix would stop original; here we indicate failure so caller can decide.
        return false;
    }
}