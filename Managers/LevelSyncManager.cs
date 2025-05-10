// File: L.A.T.E/Managers/LevelSyncManager.cs
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic; // Only for HostArenaPlatformSyncManager's PlayerList if it used it directly.
using UnityEngine;
using Object = UnityEngine.Object;
using LATE.Core;       // For LatePlugin.Log, CoroutineHelper
using LATE.Utilities;  // For ReflectionCache, PhotonUtilities, GameUtilities
// Potentially LATE.Config if any config checks were in these methods, but likely not.

namespace LATE.Managers; // File-scoped namespace

/// <summary>
/// Responsible for synchronizing level-specific states for late-joining players.
/// This includes the overall level state (name, completion, game over),
/// module connections, extraction point states, prop switch setups,
/// Arena states, and truck screen information.
/// </summary>
internal static class LevelSyncManager
{
    /// <summary>
    /// Synchronizes the core level state (current level, completion status, game over)
    /// and LevelGenerator state to a late-joining player.
    /// </summary>
    internal static void SyncLevelState(Player targetPlayer)
    {
        string targetPlayerNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        RunManager? runManager = RunManager.instance;
        if (runManager == null || runManager.levelCurrent == null)
        {
            LatePlugin.Log.LogError("[LevelSyncManager] Null RunManager/levelCurrent during SyncLevelState.");
            return;
        }
        if (ReflectionCache.RunManager_RunManagerPUNField == null || ReflectionCache.RunManagerPUN_PhotonViewField == null || ReflectionCache.RunManager_GameOverField == null)
        {
            LatePlugin.Log.LogError("[LevelSyncManager] Null reflection fields from ReflectionCache for level state.");
            return;
        }

        LevelGenerator? levelGen = LevelGenerator.Instance;
        try
        {
            object? runManagerPUNObj = ReflectionCache.RunManager_RunManagerPUNField.GetValue(runManager);
            RunManagerPUN? runManagerPUN = runManagerPUNObj as RunManagerPUN;
            if (runManagerPUN == null) throw new Exception("Reflected RunManagerPUN is null.");

            PhotonView? punPhotonView = ReflectionCache.RunManagerPUN_PhotonViewField.GetValue(runManagerPUN) as PhotonView;
            if (punPhotonView == null) throw new Exception("Reflected RunManagerPUN's PhotonView is null.");

            string levelName = runManager.levelCurrent.name;
            int levelsCompleted = runManager.levelsCompleted;
            bool gameOver = (bool)(ReflectionCache.RunManager_GameOverField.GetValue(runManager) ?? false);

            LatePlugin.Log.LogInfo($"[LevelSyncManager] Sending UpdateLevelRPC to {targetPlayerNickname}. Level:'{levelName}', Completed:{levelsCompleted}, GameOver:{gameOver}");
            punPhotonView.RPC("UpdateLevelRPC", targetPlayer, levelName, levelsCompleted, gameOver);
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[LevelSyncManager] Error sending UpdateLevelRPC: {ex}");
        }

        if (levelGen != null && levelGen.PhotonView != null)
        {
            try
            {
                LatePlugin.Log.LogInfo($"[LevelSyncManager] Sending GenerateDone RPC to {targetPlayerNickname}.");
                levelGen.PhotonView.RPC("GenerateDone", targetPlayer);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[LevelSyncManager] Error sending GenerateDone RPC: {ex}");
            }
        }
        else
        {
            LatePlugin.Log.LogWarning($"[LevelSyncManager] Skipped GenerateDone RPC (LevelGenerator or its PhotonView is null).");
        }
    }

    /// <summary>
    /// Synchronizes the connection states of all Modules in the scene to a late-joining player.
    /// </summary>
    internal static void SyncModuleConnectionStatesForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogInfo($"[LevelSyncManager][Module Sync] Starting Module connection sync for {nick}.");

        if (ReflectionCache.Module_SetupDoneField == null || ReflectionCache.Module_ConnectingTopField == null ||
            ReflectionCache.Module_ConnectingBottomField == null || ReflectionCache.Module_ConnectingRightField == null ||
            ReflectionCache.Module_ConnectingLeftField == null || ReflectionCache.Module_FirstField == null)
        {
            LatePlugin.Log.LogError("[LevelSyncManager][Module Sync] Critical reflection failure: Required Module fields not found in ReflectionCache. Aborting sync.");
            return;
        }

        Module[] allModules = Object.FindObjectsOfType<Module>(true);
        if (allModules == null || allModules.Length == 0)
        {
            LatePlugin.Log.LogWarning("[LevelSyncManager][Module Sync] Found 0 Module components. Skipping sync.");
            return;
        }
        int syncedCount = 0; int skippedCount = 0;
        foreach (Module module in allModules)
        {
            if (module == null) { skippedCount++; continue; }
            PhotonView? pv = PhotonUtilities.GetPhotonView(module);
            if (pv == null) { LatePlugin.Log.LogWarning($"[LevelSyncManager][Module Sync] Module '{module.gameObject?.name ?? "NULL_GO"}' missing PV. Skipping."); skippedCount++; continue; }
            try
            {
                bool setupDone = (bool)(ReflectionCache.Module_SetupDoneField.GetValue(module) ?? false);
                if (!setupDone) { skippedCount++; continue; }
                bool top = (bool)(ReflectionCache.Module_ConnectingTopField.GetValue(module) ?? false);
                bool bottom = (bool)(ReflectionCache.Module_ConnectingBottomField.GetValue(module) ?? false);
                bool right = (bool)(ReflectionCache.Module_ConnectingRightField.GetValue(module) ?? false);
                bool left = (bool)(ReflectionCache.Module_ConnectingLeftField.GetValue(module) ?? false);
                bool first = (bool)(ReflectionCache.Module_FirstField.GetValue(module) ?? false);
                pv.RPC("ModuleConnectionSetRPC", targetPlayer, top, bottom, right, left, first);
                syncedCount++;
            }
            catch (Exception ex) { LatePlugin.Log.LogError($"[LevelSyncManager][Module Sync] Error for module '{module.gameObject?.name ?? "NULL_GO"}' (ViewID: {pv.ViewID}): {ex}"); skippedCount++; }
        }
        LatePlugin.Log.LogInfo($"[LevelSyncManager][Module Sync] Finished for {nick}. Synced: {syncedCount}, Skipped: {skippedCount}.");
    }

    /// <summary>
    /// Synchronizes the state, goal, and surplus of all ExtractionPoints to a late-joining player.
    /// </summary>
    internal static void SyncExtractionPointsForPlayer(Player targetPlayer)
    {
        string targetPlayerNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogDebug($"[LevelSyncManager][EP Sync] Starting EP state/goal/surplus sync for {targetPlayerNickname}.");

        if (RoundDirector.instance == null || ReflectionCache.ExtractionPoint_CurrentStateField == null ||
            ReflectionCache.RoundDirector_ExtractionPointActiveField == null || ReflectionCache.RoundDirector_ExtractionPointSurplusField == null ||
            ReflectionCache.ExtractionPoint_HaulGoalFetchedField == null || ReflectionCache.RoundDirector_ExtractionPointCurrentField == null ||
            ReflectionCache.ExtractionPoint_IsShopField == null)
        {
            LatePlugin.Log.LogError("[LevelSyncManager][EP Sync] Critical instance or field missing from ReflectionCache for EP sync.");
            return;
        }
        ExtractionPoint[]? allExtractionPoints = Object.FindObjectsOfType<ExtractionPoint>(true);
        int hostSurplus = 0; bool isAnyEpActiveOnHost = false; ExtractionPoint? currentActiveEpOnHost = null;
        try
        {
            hostSurplus = (int)(ReflectionCache.RoundDirector_ExtractionPointSurplusField.GetValue(RoundDirector.instance) ?? 0);
            isAnyEpActiveOnHost = (bool)(ReflectionCache.RoundDirector_ExtractionPointActiveField.GetValue(RoundDirector.instance) ?? false);
            currentActiveEpOnHost = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint;
            if (isAnyEpActiveOnHost && currentActiveEpOnHost == null) isAnyEpActiveOnHost = false;
            else if (!isAnyEpActiveOnHost && currentActiveEpOnHost != null) currentActiveEpOnHost = null;
        }
        catch (Exception ex) { LatePlugin.Log.LogError($"[LevelSyncManager][EP Sync] Error reflecting RoundDirector state: {ex}. Aborting."); return; }

        LatePlugin.Log.LogInfo($"[LevelSyncManager][EP Sync] Host EP Active: {isAnyEpActiveOnHost}. Current EP: '{currentActiveEpOnHost?.name ?? "None"}'. Surplus: {hostSurplus}");
        PhotonView? firstEpPvForGlobalUnlock = null;
        if (allExtractionPoints != null)
        {
            foreach (ExtractionPoint ep in allExtractionPoints)
            {
                if (ep == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(ep);
                if (pv == null) continue;
                if (firstEpPvForGlobalUnlock == null) firstEpPvForGlobalUnlock = pv;
                try
                {
                    ExtractionPoint.State hostState = (ExtractionPoint.State)(ReflectionCache.ExtractionPoint_CurrentStateField.GetValue(ep) ?? ExtractionPoint.State.Idle);
                    bool isThisTheShopEP = (bool)(ReflectionCache.ExtractionPoint_IsShopField.GetValue(ep) ?? false);
                    pv.RPC("StateSetRPC", targetPlayer, hostState);
                    pv.RPC("ExtractionPointSurplusRPC", targetPlayer, hostSurplus);
                    if (isAnyEpActiveOnHost && currentActiveEpOnHost != null && !isThisTheShopEP)
                    {
                        if (ep == currentActiveEpOnHost)
                        {
                            bool hostGoalFetched = (bool)(ReflectionCache.ExtractionPoint_HaulGoalFetchedField.GetValue(ep) ?? false);
                            if (hostGoalFetched && ep.haulGoal > 0) pv.RPC("HaulGoalSetRPC", targetPlayer, ep.haulGoal);
                        }
                        else if (hostState == ExtractionPoint.State.Idle) pv.RPC("ButtonDenyRPC", targetPlayer);
                    }
                }
                catch (Exception ex) { LatePlugin.Log.LogError($"[LevelSyncManager][EP Sync] RPC Error for EP '{ep.name}': {ex}"); }
            }
        }
        if (!isAnyEpActiveOnHost && firstEpPvForGlobalUnlock != null)
        {
            try { firstEpPvForGlobalUnlock.RPC("ExtractionPointsUnlockRPC", targetPlayer); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[LevelSyncManager][EP Sync] Failed global UnlockRPC: {ex}"); }
        }
        LatePlugin.Log.LogInfo($"[LevelSyncManager][EP Sync] Finished EP state/goal/surplus sync for {targetPlayerNickname}.");
    }

    /// <summary>
    /// Triggers the setup RPC for ValuablePropSwitches for a late-joining player.
    /// </summary>
    internal static void TriggerPropSwitchSetup(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        ValuableDirector? director = ValuableDirector.instance;
        if (director == null) { LatePlugin.Log.LogWarning($"[LevelSyncManager][PropSwitch Sync] Instance null. Skipping for {nick}."); return; }
        PhotonView? directorPV = PhotonUtilities.GetPhotonView(director);
        if (directorPV == null) { LatePlugin.Log.LogWarning($"[LevelSyncManager][PropSwitch Sync] PV null. Skipping for {nick}."); return; }
        try { directorPV.RPC("VolumesAndSwitchSetupRPC", targetPlayer); LatePlugin.Log.LogInfo($"[LevelSyncManager][PropSwitch Sync] Sending RPC to {nick}."); }
        catch (Exception ex) { LatePlugin.Log.LogError($"[LevelSyncManager][PropSwitch Sync] Error sending RPC: {ex}"); }
    }

    /// <summary>
    /// Synchronizes the state of the TruckScreenText (pages, typing) to a late-joining player.
    /// </summary>
    internal static void SyncTruckScreenForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        TruckScreenText? screen = TruckScreenText.instance;
        if (screen == null) { LatePlugin.Log.LogWarning("[LevelSyncManager][Truck Sync] Instance null."); return; }
        try
        {
            PhotonView? pv = PhotonUtilities.GetPhotonView(screen);
            if (pv == null) { LatePlugin.Log.LogWarning("[LevelSyncManager][Truck Sync] PV null."); return; }
            if (ReflectionCache.TruckScreenText_CurrentPageIndexField == null) { LatePlugin.Log.LogError("[LevelSyncManager][Truck Sync] Reflection field null."); return; }
            pv.RPC("InitializeTextTypingRPC", targetPlayer);
            int hostPage = -1;
            try { hostPage = (int)(ReflectionCache.TruckScreenText_CurrentPageIndexField.GetValue(screen) ?? -1); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[LevelSyncManager][Truck Sync] Error reflecting currentPageIndex: {ex}"); }
            if (hostPage >= 0) { pv.RPC("GotoPageRPC", targetPlayer, hostPage); LatePlugin.Log.LogInfo($"[LevelSyncManager][Truck Sync] Synced page {hostPage} for {nick}."); }
            else { LatePlugin.Log.LogInfo($"[LevelSyncManager][Truck Sync] Synced init for {nick}. Host page invalid."); }
        }
        catch (Exception ex) { LatePlugin.Log.LogError($"[LevelSyncManager][Truck Sync] Error: {ex}"); }
    }

    /// <summary>
    /// Synchronizes Arena-specific state (cage destruction, winner, platform states)
    /// to a late-joining player.
    /// </summary>
    internal static void SyncArenaStateForPlayer(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        if (targetPlayer == null) { LatePlugin.Log.LogWarning("[LevelSyncManager][Arena Sync] Target player is null."); return; }
        if (!PhotonUtilities.IsRealMasterClient()) { return; } // Host only
        Arena arenaInstance = Arena.instance;
        if (arenaInstance == null) { LatePlugin.Log.LogWarning("[LevelSyncManager][Arena Sync] Arena.instance is null."); return; }

        PhotonView? arenaPV = null;
        if (ReflectionCache.Arena_PhotonViewField != null) { try { arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arenaInstance) as PhotonView; } catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaSync] Error reflecting Arena.photonView: {ex}"); return; } }
        if (arenaPV == null) { LatePlugin.Log.LogWarning("[LevelSyncManager][Arena Sync] Arena PV null from ReflectionCache."); return; }

        LatePlugin.Log.LogInfo($"[LevelSyncManager][Arena Sync] Starting Arena state sync for {targetNickname}.");

        bool hostCageIsDestroyed = false;
        if (ReflectionCache.Arena_CrownCageDestroyedField != null) { try { hostCageIsDestroyed = (bool)(ReflectionCache.Arena_CrownCageDestroyedField.GetValue(arenaInstance) ?? false); } catch { } }
        if (hostCageIsDestroyed) { try { arenaPV.RPC("DestroyCrownCageRPC", targetPlayer); } catch { } }

        PlayerAvatar? currentWinner = null;
        if (ReflectionCache.Arena_WinnerPlayerField != null) { try { currentWinner = ReflectionCache.Arena_WinnerPlayerField.GetValue(arenaInstance) as PlayerAvatar; } catch { } }
        if (currentWinner != null)
        {
            int winnerPhysGrabberViewID = GameUtilities.GetPhysGrabberViewId(currentWinner);
            if (winnerPhysGrabberViewID > 0) { try { arenaPV.RPC("CrownGrabRPC", targetPlayer, winnerPhysGrabberViewID); } catch { } }
        }

        int actualLivePlayerCount = 0;
        if (GameDirector.instance?.PlayerList != null && ReflectionCache.PlayerAvatar_IsDisabledField != null)
        {
            foreach (PlayerAvatar pa in GameDirector.instance.PlayerList)
            {
                if (pa == null) continue;
                try { if (!(bool)(ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(pa) ?? true)) actualLivePlayerCount++; }
                catch { actualLivePlayerCount++; }
            }
        }
        try { arenaPV.RPC("PlayerKilledRPC", targetPlayer, actualLivePlayerCount); } catch { }

        HostArenaPlatformSyncManager.Instance.StartPlatformCatchUpForPlayer(targetPlayer, arenaInstance);
        LatePlugin.Log.LogInfo($"[LevelSyncManager][Arena Sync] Finished Arena state sync (main part) for {targetNickname}. Platform sync in background.");
    }

    /// <summary>
    /// Manages the host-side coroutine for synchronizing Arena platform states
    /// to late-joining players. This is a nested helper class for LevelSyncManager.
    /// </summary>
    internal class HostArenaPlatformSyncManager : MonoBehaviourPunCallbacks // Public to be accessible as a Component
    {
        private static HostArenaPlatformSyncManager? _instance;
        public static HostArenaPlatformSyncManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<HostArenaPlatformSyncManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("LATE_HostArenaPlatformSyncManager"); // Unique name
                        _instance = go.AddComponent<HostArenaPlatformSyncManager>();
                        DontDestroyOnLoad(go); // Persist if necessary, or manage lifecycle with scene
                    }
                }
                return _instance;
            }
        }
        private const float RPC_CATCHUP_DELAY = 0.30f;

        public void StartPlatformCatchUpForPlayer(Player targetPlayer, Arena arenaInstance)
        {
            if (!PhotonUtilities.IsRealMasterClient() || arenaInstance == null || targetPlayer == null) return;
            StartCoroutine(CatchUpPlayerPlatformSequence(targetPlayer, arenaInstance));
        }

        private IEnumerator CatchUpPlayerPlatformSequence(Player targetPlayer, Arena arena)
        {
            yield return new WaitForSeconds(0.5f); // Initial delay for client to settle
            PhotonView? arenaPV = null; Arena.States hostCurrentArenaState = Arena.States.Idle; int hostArenaLevel = 0;

            if (ReflectionCache.Arena_PhotonViewField != null) { try { arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arena) as PhotonView; } catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.photonView: {ex}"); yield break; } }
            if (arenaPV == null) { LatePlugin.Log.LogError("[ArenaPlatformSync] Arena.photonView is null from ReflectionCache."); yield break; }

            if (ReflectionCache.Arena_CurrentStateField != null) { try { hostCurrentArenaState = (Arena.States)(ReflectionCache.Arena_CurrentStateField.GetValue(arena) ?? Arena.States.Idle); } catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.currentState: {ex}"); } }
            if (ReflectionCache.Arena_LevelField != null) { try { hostArenaLevel = (int)(ReflectionCache.Arena_LevelField.GetValue(arena) ?? 0); } catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.level: {ex}"); yield break; } }
            else { LatePlugin.Log.LogError("[ArenaPlatformSync] Arena_LevelField is null from ReflectionCache."); yield break; }

            LatePlugin.Log.LogInfo($"[ArenaPlatformSync] Host Arena.level: {hostArenaLevel}. Replaying for {targetPlayer.NickName}.");

            if (hostArenaLevel > 0)
            {
                for (int i = 0; i < hostArenaLevel; i++)
                {
                    if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) { LatePlugin.Log.LogWarning($"[ArenaPlatformSync] Player {targetPlayer.NickName} left during platform catch-up. Aborting."); yield break; }
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformWarning);
                    yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
                    if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) { LatePlugin.Log.LogWarning($"[ArenaPlatformSync] Player {targetPlayer.NickName} left. Aborting."); yield break; }
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformRemove);
                    yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
                }
            }

            if (ReflectionCache.Arena_CurrentStateField != null) { try { hostCurrentArenaState = (Arena.States)(ReflectionCache.Arena_CurrentStateField.GetValue(arena) ?? Arena.States.Idle); } catch { /* Already logged */ } }
            arenaPV.RPC("StateSetRPC", targetPlayer, hostCurrentArenaState); // Set final current state
            yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
            LatePlugin.Log.LogInfo($"[ArenaPlatformSync] Arena platform catch-up sequence complete for {targetPlayer.NickName}.");
        }
    }
}