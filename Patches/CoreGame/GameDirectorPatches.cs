// File: L.A.T.E/Patches/CoreGame/GameDirectorPatches.cs
using HarmonyLib;
using Photon.Pun;
using UnityEngine; // Added: For Coroutine type
using LATE.Core; // For LatePlugin.Log, CoroutineHelper
using LATE.Managers.GameState; // For GameVersionSupport
using LATE.Utilities; // For GameUtilities (IsModLogicActive), PhotonUtilities (IsRealMasterClient)

namespace LATE.Patches.CoreGame; // File-scoped namespace

/// <summary>
/// Contains Harmony patches for the GameDirector class.
/// </summary>
[HarmonyPatch]
internal static class GameDirectorPatches
{
    /// <summary>
    /// Harmony Postfix for GameDirector.SetStart.
    /// Runs on the HOST after the game state is officially set to Start for the level.
    /// This serves as the final point to unlock the lobby if needed, and disarms the failsafe.
    /// </summary>
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.SetStart))]
    [HarmonyPostfix]
    static void GameDirector_SetStart_Postfix(GameDirector __instance)
    {
        // Only the MasterClient should perform this final step
        if (!PhotonUtilities.IsRealMasterClient())
        {
            return;
        }

        if (!GameUtilities.IsModLogicActive())
        {
            LatePlugin.Log.LogDebug(
                "[GameDirectorPatches.SetStart_Postfix] Mod logic is inactive. No L.A.T.E. lobby action."
            );
            return;
        }

        LatePlugin.Log.LogInfo(
            $"[GameDirectorPatches.SetStart_Postfix] GameDirector state set to Start. Checking if lobby should open (Flag: {RunManagerPatches.GetShouldOpenLobbyAfterGen()})."
        );

        if (RunManagerPatches.GetShouldOpenLobbyAfterGen())
        {
            LatePlugin.Log.LogInfo(
                "[GameDirectorPatches.SetStart_Postfix] Flag is TRUE. Opening Photon room and unlocking Steam lobby NOW."
            );

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                PhotonNetwork.CurrentRoom.IsOpen = true;
                LatePlugin.Log.LogDebug(
                    "[GameDirectorPatches.SetStart_Postfix] Photon Room IsOpen set to true."
                );
            }
            else
            {
                LatePlugin.Log.LogWarning(
                    "[GameDirectorPatches.SetStart_Postfix] Cannot open Photon room: Not in room or CurrentRoom is null."
                );
            }

            GameVersionSupport.UnlockSteamLobby(true);
            LatePlugin.Log.LogDebug(
                "[GameDirectorPatches.SetStart_Postfix] Steam lobby unlock attempted."
            );

            RunManagerPatches.SetNormalUnlockLogicExecuted(true);
            LatePlugin.Log.LogInfo("[L.A.T.E.] Normal lobby 'open' sequence completed successfully.");

            Coroutine? failsafeCoroutine = RunManagerPatches.GetLobbyUnlockFailsafeCoroutine();
            if (failsafeCoroutine != null && CoroutineHelper.CoroutineRunner != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(failsafeCoroutine); // StopCoroutine expects IEnumerator or Coroutine
                RunManagerPatches.SetLobbyUnlockFailsafeCoroutine(null);
                LatePlugin.Log.LogDebug("[L.A.T.E. Failsafe] Disarmed by successful normal 'open' logic.");
            }
        }
        else // _shouldOpenLobbyAfterGen was FALSE
        {
            LatePlugin.Log.LogInfo(
                "[GameDirectorPatches.SetStart_Postfix] Flag is FALSE. Lobby remains closed/locked."
            );

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.IsOpen)
            {
                LatePlugin.Log.LogWarning(
                    "[GameDirectorPatches.SetStart_Postfix] Sanity Check: Photon room was open. Closing."
                );
                PhotonNetwork.CurrentRoom.IsOpen = false;
            }

            GameVersionSupport.LockSteamLobby();
            LatePlugin.Log.LogDebug(
               "[GameDirectorPatches.SetStart_Postfix] Sanity Check: Steam lobby lock (re)attempted."
           );

            RunManagerPatches.SetNormalUnlockLogicExecuted(true);
            LatePlugin.Log.LogInfo("[L.A.T.E.] Normal lobby 'keep closed' sequence completed.");

            Coroutine? failsafeCoroutine = RunManagerPatches.GetLobbyUnlockFailsafeCoroutine();
            if (failsafeCoroutine != null && CoroutineHelper.CoroutineRunner != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(failsafeCoroutine); // StopCoroutine expects IEnumerator or Coroutine
                RunManagerPatches.SetLobbyUnlockFailsafeCoroutine(null);
                LatePlugin.Log.LogDebug("[L.A.T.E. Failsafe] Disarmed by 'keep closed' logic (should not have been active).");
            }
        }

        RunManagerPatches.SetShouldOpenLobbyAfterGen(false); // Reset for next level change
        LatePlugin.Log.LogDebug(
            $"[GameDirectorPatches.SetStart_Postfix] Resetting _shouldOpenLobbyAfterGen flag to false."
        );
    }
}