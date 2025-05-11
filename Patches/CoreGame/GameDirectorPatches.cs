// File: L.A.T.E/Patches/CoreGame/GameDirectorPatches.cs
using HarmonyLib;

using Photon.Pun;

using UnityEngine;                // Coroutine

using LATE.Config;                // ConfigManager
using LATE.Core;                  // LatePlugin.Log, CoroutineHelper
using LATE.Managers.GameState;    // GameVersionSupport
using LATE.Utilities;             // GameUtilities, PhotonUtilities

namespace LATE.Patches.CoreGame;

/// <summary>Harmony patches for <see cref="GameDirector"/>.</summary>
[HarmonyPatch]
internal static class GameDirectorPatches
{
    private const string LogPrefix = "[GameDirectorPatches.SetStart_Postfix]";

    /* ------------------------------------------------------------------------------------- */
    /*  SetStart POSTFIX                                                                    */
    /* ------------------------------------------------------------------------------------- */

    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.SetStart))]
    [HarmonyPostfix]
    private static void GameDirector_SetStart_Postfix(GameDirector __instance)
    {
        if (!PhotonUtilities.IsRealMasterClient()) return;

        bool shouldOpenLobby = RunManagerPatches.GetShouldOpenLobbyAfterGen();
        bool initialPhaseDone = RunManagerPatches.GetInitialPublicListingPhaseComplete();
        bool keepPublicCfg = ConfigManager.KeepPublicLobbyListed.Value;
        bool inRoom = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null;
        bool isModLogicActive = GameUtilities.IsModLogicActive();

        if (!isModLogicActive)
            LatePlugin.Log.LogInfo($"{LogPrefix} Mod logic inactive for this scene. Proceeding with standard logic.");

        LatePlugin.Log.LogInfo(
            $"{LogPrefix} State → Start | ShouldOpenLobby: {shouldOpenLobby} | " +
            $"KeepPublicCfg: {keepPublicCfg} | InitialPhaseComplete: {initialPhaseDone}");

        if (shouldOpenLobby)
            ExecuteOpenLobbyPath(inRoom, keepPublicCfg, initialPhaseDone);
        else
            ExecuteKeepClosedPath(inRoom);

        // Reset flag for next level transition
        RunManagerPatches.SetShouldOpenLobbyAfterGen(false);
        LatePlugin.Log.LogDebug($"{LogPrefix} Reset _shouldOpenLobbyAfterGen flag for next cycle.");
    }

    /* ------------------------------------------------------------------------------------- */
    /*  Helpers                                                                              */
    /* ------------------------------------------------------------------------------------- */

    private static void ExecuteOpenLobbyPath(bool inRoom, bool keepPublicCfg, bool initialPhaseDone)
    {
        bool makeVisible = keepPublicCfg || !initialPhaseDone;
        LatePlugin.Log.LogInfo($"{LogPrefix} Decided to OPEN lobby (Visible: {makeVisible}).");

        if (inRoom)
        {
            PhotonNetwork.CurrentRoom.IsOpen = true;
            PhotonNetwork.CurrentRoom.IsVisible = makeVisible;
            LatePlugin.Log.LogDebug($"{LogPrefix} Photon room IsOpen=true, IsVisible={makeVisible}.");
        }
        else
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} Cannot open/visibilise Photon room: not in room.");
        }

        GameVersionSupport.UnlockSteamLobby(makeVisible);
        LatePlugin.Log.LogDebug($"{LogPrefix} Steam lobby unlock attempted (public: {makeVisible}).");

        FinishNormalLogic("[L.A.T.E.] Normal lobby 'open/visibility set' sequence completed successfully.",
                          "[L.A.T.E. Failsafe] Disarmed by successful normal logic.");
    }

    private static void ExecuteKeepClosedPath(bool inRoom)
    {
        LatePlugin.Log.LogInfo($"{LogPrefix} Flag FALSE → keeping lobby CLOSED/hidden.");

        if (inRoom)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
            LatePlugin.Log.LogDebug($"{LogPrefix} Photon room IsOpen=false, IsVisible=false.");
        }

        GameVersionSupport.LockSteamLobby();
        LatePlugin.Log.LogDebug($"{LogPrefix} Steam lobby locked.");

        FinishNormalLogic("[L.A.T.E.] Normal lobby 'keep closed/hidden' sequence completed.",
                          "[L.A.T.E. Failsafe] Disarmed by 'keep closed' logic.");
    }

    private static void FinishNormalLogic(string successMsg, string disarmMsg)
    {
        RunManagerPatches.SetNormalUnlockLogicExecuted(true);
        LatePlugin.Log.LogInfo(successMsg);

        Coroutine? failsafe = RunManagerPatches.GetLobbyUnlockFailsafeCoroutine();
        if (failsafe != null && CoroutineHelper.CoroutineRunner != null)
        {
            CoroutineHelper.CoroutineRunner.StopCoroutine(failsafe);
            RunManagerPatches.SetLobbyUnlockFailsafeCoroutine(null);
            LatePlugin.Log.LogDebug(disarmMsg);
        }
    }
}