// File: L.A.T.E/Patches/CoreGame/GameDirectorPatches.cs
using HarmonyLib;
using LATE.Core; // For LatePlugin.Log, CoroutineHelper
using LATE.Managers.GameState; // For GameVersionSupport
using LATE.Utilities; // For GameUtilities (IsModLogicActive), PhotonUtilities (IsRealMasterClient)
using Photon.Pun;
using UnityEngine; // Added: For Coroutine type
using LATE.Config; // For ConfigManager

namespace LATE.Patches.CoreGame; // File-scoped namespace

/// <summary>
/// Contains Harmony patches for the GameDirector class.
/// </summary>
[HarmonyPatch]
internal static class GameDirectorPatches
{
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.SetStart))]
    [HarmonyPostfix]
    static void GameDirector_SetStart_Postfix(GameDirector __instance)
    {
        if (!PhotonUtilities.IsRealMasterClient())
        {
            return;
        }

        if (!GameUtilities.IsModLogicActive())
        {
            LatePlugin.Log.LogInfo(
                "[GameDirectorPatches.SetStart_Postfix] Mod logic is inactive for this scene. " +
                $"Flag _shouldOpenLobbyAfterGen is: {RunManagerPatches.GetShouldOpenLobbyAfterGen()}. Proceeding with standard logic."
            );
        }

        LatePlugin.Log.LogInfo(
            $"[GameDirectorPatches.SetStart_Postfix] GameDirector state set to Start. " +
            $"ShouldOpenLobbyFlag: {RunManagerPatches.GetShouldOpenLobbyAfterGen()}, " +
            $"KeepPublicListedCfg: {ConfigManager.KeepPublicLobbyListed.Value}, " +
            $"InitialPhaseComplete: {RunManagerPatches.GetInitialPublicListingPhaseComplete()}."
        );

        if (RunManagerPatches.GetShouldOpenLobbyAfterGen()) // If the current scene *allows* late joining
        {
            bool isCurrentlyPublicPhase = !RunManagerPatches.GetInitialPublicListingPhaseComplete();
            // Lobby is visible if KeepPublicLobbyListed is true OR if it's still in the initial public phase.
            bool makeVisibleAndPublic = ConfigManager.KeepPublicLobbyListed.Value || isCurrentlyPublicPhase;

            LatePlugin.Log.LogInfo(
                $"[GameDirectorPatches.SetStart_Postfix] Decided to make lobby Open. " +
                $"Effective Visibility/Public: {makeVisibleAndPublic} (Config: {ConfigManager.KeepPublicLobbyListed.Value}, InitialPhase: {isCurrentlyPublicPhase})"
            );

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                PhotonNetwork.CurrentRoom.IsOpen = true; // Always open if late joining is allowed for scene
                PhotonNetwork.CurrentRoom.IsVisible = makeVisibleAndPublic;
                LatePlugin.Log.LogDebug(
                    $"[GameDirectorPatches.SetStart_Postfix] Photon Room IsOpen set to true. IsVisible set to {makeVisibleAndPublic}."
                );
            }
            else
            {
                LatePlugin.Log.LogWarning(
                    "[GameDirectorPatches.SetStart_Postfix] Cannot open/show Photon room: Not in room or CurrentRoom is null."
                );
            }

            // Unlock Steam lobby. 'makeVisibleAndPublic' determines if it's public or private+joinable.
            GameVersionSupport.UnlockSteamLobby(makeVisibleAndPublic);
            LatePlugin.Log.LogDebug(
                $"[GameDirectorPatches.SetStart_Postfix] Steam lobby unlock attempted (public: {makeVisibleAndPublic})."
            );

            RunManagerPatches.SetNormalUnlockLogicExecuted(true);
            LatePlugin.Log.LogInfo($"[L.A.T.E.] Normal lobby 'open/visibility set' sequence completed successfully.");

            // Disarm failsafe
            Coroutine? failsafeCoroutine = RunManagerPatches.GetLobbyUnlockFailsafeCoroutine();
            if (failsafeCoroutine != null && CoroutineHelper.CoroutineRunner != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(failsafeCoroutine);
                RunManagerPatches.SetLobbyUnlockFailsafeCoroutine(null);
                LatePlugin.Log.LogDebug("[L.A.T.E. Failsafe] Disarmed by successful normal logic.");
            }
        }
        else // _shouldOpenLobbyAfterGen was FALSE (scene does not allow late joining)
        {
            LatePlugin.Log.LogInfo(
                "[GameDirectorPatches.SetStart_Postfix] Flag is FALSE. Ensuring lobby remains closed/locked/hidden."
            );

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                PhotonNetwork.CurrentRoom.IsOpen = false;
                PhotonNetwork.CurrentRoom.IsVisible = false;
                LatePlugin.Log.LogDebug(
                   "[GameDirectorPatches.SetStart_Postfix] Photon Room confirmed IsOpen=false and IsVisible=false."
               );
            }
            GameVersionSupport.LockSteamLobby();
            LatePlugin.Log.LogDebug(
               "[GameDirectorPatches.SetStart_Postfix] Steam lobby lock confirmed."
           );

            RunManagerPatches.SetNormalUnlockLogicExecuted(true);
            LatePlugin.Log.LogInfo("[L.A.T.E.] Normal lobby 'keep closed/hidden' sequence completed.");

            Coroutine? failsafeCoroutine = RunManagerPatches.GetLobbyUnlockFailsafeCoroutine();
            if (failsafeCoroutine != null && CoroutineHelper.CoroutineRunner != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(failsafeCoroutine);
                RunManagerPatches.SetLobbyUnlockFailsafeCoroutine(null);
                LatePlugin.Log.LogDebug("[L.A.T.E. Failsafe] Disarmed by 'keep closed' logic.");
            }
        }

        // Reset _shouldOpenLobbyAfterGen for the next level transition.
        // _normalUnlockLogicExecuted is reset at the start of ChangeLevelHook.
        RunManagerPatches.SetShouldOpenLobbyAfterGen(false);
        LatePlugin.Log.LogDebug(
            $"[GameDirectorPatches.SetStart_Postfix] Reset _shouldOpenLobbyAfterGen flag to false for next cycle."
        );
    }
}