// File: L.A.T.E/Managers/LateJoinManager.cs
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections; // For IEnumerator (though ResyncExtractionPointItems moved)
using System.Collections.Generic;
// using System.Linq; // No longer needed directly here
using UnityEngine;
using Object = UnityEngine.Object;
using LATE.Core;
using LATE.Config;
using LATE.Utilities;
using LATE.DataModels;
// ItemSyncManager is now used
using LATE.Managers; // For other managers like PlayerStateManager, EnemySyncManager, VoiceManager

namespace LATE.Managers;

/// <summary>
/// Manages the state and synchronization process for players who join after a
/// level has started. Orchestrates calls to more specialized sync managers.
/// </summary>
internal static class LateJoinManager
{
    // itemResyncDelay constant has been moved to ItemSyncManager

    #region Tracking Sets
    private static readonly HashSet<int> _playersNeedingLateJoinSync = new HashSet<int>();
    private static readonly HashSet<int> _playersJoinedLateThisScene = new HashSet<int>();
    #endregion

    #region Scene Management
    public static void ResetSceneTracking()
    {
        LatePlugin.Log.LogDebug("[LateJoinManager] Clearing late join tracking sets for new scene.");
        _playersNeedingLateJoinSync.Clear();
        _playersJoinedLateThisScene.Clear();
    }
    #endregion

    #region Player Join Handling(Tracking Logic)
    public static void HandlePlayerJoined(Player newPlayer)
    {
        if (newPlayer == null)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager] HandlePlayerJoined called with null player.");
            return;
        }

        int actorNr = newPlayer.ActorNumber;
        string nickname = newPlayer.NickName ?? $"ActorNr {actorNr}";
        bool joinedDuringActiveScene = GameUtilities.IsModLogicActive();

        if (joinedDuringActiveScene)
        {
            if (_playersNeedingLateJoinSync.Add(actorNr))
            {
                LatePlugin.Log.LogInfo($"[LateJoinManager] Player {nickname} marked as needing late join DATA sync (awaiting LoadingCompleteRPC).");
            }
            if (_playersJoinedLateThisScene.Add(actorNr))
            {
                LatePlugin.Log.LogInfo($"[LateJoinManager] Player {nickname} marked as JOINED LATE THIS SCENE.");
            }
        }
        else
        {
            LatePlugin.Log.LogInfo($"[LateJoinManager] Player {nickname} joined during INACTIVE scene. Not marked as late-joiner.");
        }

        if (PhotonUtilities.IsRealMasterClient())
        {
            VoiceManager.TriggerDelayedSync($"Player {nickname} joined room", 0.5f);
        }
    }
    #endregion

    #region Public Accessors& Modifiers for Tracking
    internal static bool IsPlayerNeedingSync(int actorNumber) => _playersNeedingLateJoinSync.Contains(actorNumber);
    internal static bool DidPlayerJoinLateThisScene(int actorNumber) => _playersJoinedLateThisScene.Contains(actorNumber);

    internal static void MarkPlayerSyncTriggeredAndClearNeed(int actorNumber)
    {
        if (_playersNeedingLateJoinSync.Remove(actorNumber))
        {
            LatePlugin.Log.LogDebug($"[LateJoinManager] Sync triggered for ActorNr {actorNumber}. Removed from 'NeedingSync' list.");
        }
    }

    internal static void ClearPlayerTracking(int actorNumber)
    {
        bool removedNeed = _playersNeedingLateJoinSync.Remove(actorNumber);
        bool removedJoinedLate = _playersJoinedLateThisScene.Remove(actorNumber);
        if (removedNeed || removedJoinedLate)
        {
            LatePlugin.Log?.LogDebug($"[LateJoinManager] Cleared all sync tracking for ActorNr {actorNumber}.");
        }
    }
    #endregion

    #region Central Synchronization Method
    public static void SyncAllStateForPlayer(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        int actorNr = targetPlayer.ActorNumber;
        string nickname = targetPlayer.NickName ?? $"ActorNr {actorNr}";
        LatePlugin.Log.LogInfo($"[LateJoinManager] === Orchestrating SyncAllStateForPlayer for {nickname} ===");
        try
        {
            // Calls to LevelSyncManager will be added when its methods are moved.
            SyncLevelState(targetPlayer); // To LevelSyncManager
            SyncModuleConnectionStatesForPlayer(targetPlayer); // To LevelSyncManager
            SyncExtractionPointsForPlayer(targetPlayer); // To LevelSyncManager

            bool isShopScene = SemiFunc.RunIsShop();
            if (isShopScene)
            {
                ItemSyncManager.SyncAllShopItemsForPlayer(targetPlayer); // MODIFIED: Call ItemSyncManager
            }
            else
            {
                ItemSyncManager.SyncAllValuablesForPlayer(targetPlayer); // MODIFIED: Call ItemSyncManager
                DestructionManager.SyncHingeStatesForPlayer(targetPlayer);
            }
            TriggerPropSwitchSetup(targetPlayer); // To LevelSyncManager

            EnemySyncManager.SyncAllEnemyStatesForPlayer(targetPlayer);
            EnemySyncManager.NotifyEnemiesOfNewPlayer(targetPlayer, playerAvatar);

            SyncPlayerDeathState(targetPlayer, playerAvatar);
            SyncTruckScreenForPlayer(targetPlayer); // To LevelSyncManager
            ItemSyncManager.SyncAllItemStatesForPlayer(targetPlayer); // MODIFIED: Call ItemSyncManager

            if (SemiFunc.RunIsArena())
            {
                SyncArenaStateForPlayer(targetPlayer); // To LevelSyncManager
            }

            if (PhotonUtilities.IsRealMasterClient())
            {
                LatePlugin.Log.LogDebug($"[LateJoinManager] Host preparing to potentially resync EP items for {nickname}.");
                ExtractionPoint? epToResync = null;

                if (isShopScene)
                {
                    ExtractionPoint[] allEps = Object.FindObjectsOfType<ExtractionPoint>();
                    foreach (ExtractionPoint ep in allEps)
                    {
                        if (ep == null || ReflectionCache.ExtractionPoint_IsShopField == null) continue;
                        try
                        {
                            bool isThisTheShopEP = (bool)(ReflectionCache.ExtractionPoint_IsShopField.GetValue(ep) ?? false);
                            if (isThisTheShopEP) { epToResync = ep; break; }
                        }
                        catch (Exception ex) { LatePlugin.Log.LogWarning($"[LateJoinManager] Error checking isShop field on EP '{ep?.name ?? "NULL"}': {ex.Message}"); }
                    }
                    if (epToResync == null) LatePlugin.Log.LogWarning("[LateJoinManager] Could not find Shop EP for item resync.");
                }
                else // Level Scene
                {
                    if (RoundDirector.instance != null && ReflectionCache.RoundDirector_ExtractionPointCurrentField != null)
                    {
                        try
                        {
                            epToResync = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint;
                            if (epToResync != null) LatePlugin.Log.LogDebug($"[LateJoinManager] Found active Level EP '{epToResync.name}' for item resync.");
                            else LatePlugin.Log.LogDebug("[LateJoinManager] No active Level EP found. Skipping item resync.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[LateJoinManager] Error getting current EP from RoundDirector: {ex}"); }
                    }
                    else LatePlugin.Log.LogWarning("[LateJoinManager] RoundDirector instance or rdExtractionPointCurrentField is null. Cannot get active EP for resync.");
                }

                if (epToResync != null)
                {
                    LatePlugin.Log.LogInfo($"[LateJoinManager] Starting ResyncExtractionPointItems coroutine for {nickname} in EP '{epToResync.name}'.");
                    if (CoroutineHelper.CoroutineRunner != null)
                    {
                        // MODIFIED: Call ItemSyncManager's coroutine
                        CoroutineHelper.CoroutineRunner.StartCoroutine(ItemSyncManager.ResyncExtractionPointItems(targetPlayer, epToResync));
                    }
                    else { LatePlugin.Log.LogError("[LateJoinManager] Cannot start ResyncExtractionPointItems: CoroutineHelper.CoroutineRunner is null!"); }
                }
                else LatePlugin.Log.LogInfo($"[LateJoinManager] No suitable EP identified for item resync for {nickname}.");
            }
            else LatePlugin.Log.LogDebug("[LateJoinManager] Not Master Client, skipping EP item resync trigger.");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[LateJoinManager] CRITICAL ERROR during SyncAllStateForPlayer for {nickname}: {ex}");
            ClearPlayerTracking(actorNr);
        }
        finally
        {
            LatePlugin.Log.LogInfo($"[LateJoinManager] === SyncAllStateForPlayer Orchestration Finished for {nickname} ===");
        }
    }
    #endregion

    // Methods SyncAllItemStatesForPlayer, SyncAllValuablesForPlayer, SyncAllShopItemsForPlayer, and ResyncExtractionPointItems
    // have been MOVED to ItemSyncManager.cs.

    // The remaining sync methods (SyncLevelState, SyncModuleConnectionStatesForPlayer, etc.) are still here
    // and will be moved to LevelSyncManager.cs in the next patch.

    #region Temporary Sync Methods (To be moved to LevelSyncManager)

    private static void SyncLevelState(Player targetPlayer)
    {
        string targetPlayerNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        RunManager? runManager = RunManager.instance;
        if (runManager == null || runManager.levelCurrent == null)
        {
            LatePlugin.Log.LogError("[LateJoinManager][Level Sync] Null RunManager/levelCurrent during SyncLevelState.");
            return;
        }
        if (ReflectionCache.RunManager_RunManagerPUNField == null || ReflectionCache.RunManagerPUN_PhotonViewField == null || ReflectionCache.RunManager_GameOverField == null)
        {
            LatePlugin.Log.LogError("[LateJoinManager][Level Sync] Null reflection fields from ReflectionCache for level state.");
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

            LatePlugin.Log.LogInfo($"[LateJoinManager][Level Sync] Sending UpdateLevelRPC to {targetPlayerNickname}. Level:'{levelName}', Completed:{levelsCompleted}, GameOver:{gameOver}");
            punPhotonView.RPC("UpdateLevelRPC", targetPlayer, levelName, levelsCompleted, gameOver);
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[LateJoinManager][Level Sync] Error sending UpdateLevelRPC: {ex}");
        }

        if (levelGen != null && levelGen.PhotonView != null)
        {
            try
            {
                LatePlugin.Log.LogInfo($"[LateJoinManager][Level Sync] Sending GenerateDone RPC to {targetPlayerNickname}.");
                levelGen.PhotonView.RPC("GenerateDone", targetPlayer);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[LateJoinManager][Level Sync] Error sending GenerateDone RPC: {ex}");
            }
        }
        else
        {
            LatePlugin.Log.LogWarning($"[LateJoinManager][Level Sync] Skipped GenerateDone RPC (LevelGenerator or its PhotonView is null).");
        }
    }

    private static void SyncModuleConnectionStatesForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogInfo($"[LateJoinManager][Module Sync] Starting Module connection sync for {nick}.");

        if (ReflectionCache.Module_SetupDoneField == null || ReflectionCache.Module_ConnectingTopField == null ||
            ReflectionCache.Module_ConnectingBottomField == null || ReflectionCache.Module_ConnectingRightField == null ||
            ReflectionCache.Module_ConnectingLeftField == null || ReflectionCache.Module_FirstField == null)
        {
            LatePlugin.Log.LogError("[LateJoinManager][Module Sync] Critical reflection failure: Required Module fields not found in ReflectionCache. Aborting sync.");
            return;
        }

        Module[] allModules = Object.FindObjectsOfType<Module>(true);
        if (allModules == null || allModules.Length == 0)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager][Module Sync] Found 0 Module components. Skipping sync.");
            return;
        }
        int syncedCount = 0; int skippedCount = 0;
        foreach (Module module in allModules)
        {
            if (module == null) { skippedCount++; continue; }
            PhotonView? pv = PhotonUtilities.GetPhotonView(module);
            if (pv == null) { skippedCount++; continue; }
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
            catch (Exception ex) { LatePlugin.Log.LogError($"[Module Sync] Error for module '{module.gameObject?.name ?? "NULL"}' (ViewID: {pv.ViewID}): {ex}"); skippedCount++; }
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][Module Sync] Finished for {nick}. Synced: {syncedCount}, Skipped: {skippedCount}.");
    }

    private static void SyncExtractionPointsForPlayer(Player targetPlayer)
    {
        string targetPlayerNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogDebug($"[LateJoinManager][EP Sync] Starting EP state/goal/surplus sync for {targetPlayerNickname}.");

        if (RoundDirector.instance == null || ReflectionCache.ExtractionPoint_CurrentStateField == null ||
            ReflectionCache.RoundDirector_ExtractionPointActiveField == null || ReflectionCache.RoundDirector_ExtractionPointSurplusField == null ||
            ReflectionCache.ExtractionPoint_HaulGoalFetchedField == null || ReflectionCache.RoundDirector_ExtractionPointCurrentField == null ||
            ReflectionCache.ExtractionPoint_IsShopField == null)
        {
            LatePlugin.Log.LogError("[LateJoinManager][EP Sync] Critical instance or field missing from ReflectionCache for EP sync.");
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
        catch (Exception ex) { LatePlugin.Log.LogError($"[EP Sync] Error reflecting RoundDirector state: {ex}. Aborting."); return; }

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
                catch (Exception ex) { LatePlugin.Log.LogError($"[EP Sync] RPC Error for EP '{ep.name}': {ex}"); }
            }
        }
        if (!isAnyEpActiveOnHost && firstEpPvForGlobalUnlock != null)
        {
            try { firstEpPvForGlobalUnlock.RPC("ExtractionPointsUnlockRPC", targetPlayer); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[EP Sync] Failed global UnlockRPC: {ex}"); }
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][EP Sync] Finished EP state/goal/surplus sync for {targetPlayerNickname}.");
    }

    private static void SyncPlayerDeathState(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        if (!ConfigManager.KillIfPreviouslyDead.Value)
        {
            LatePlugin.Log.LogDebug("[LateJoinManager][Death Sync] KillIfPreviouslyDead is disabled by config. Skipping death sync.");
            return;
        }
        PlayerStatus status = PlayerStateManager.GetPlayerStatus(targetPlayer);
        if (status == PlayerStatus.Dead)
        {
            PhotonView? pv = PhotonUtilities.GetPhotonView(playerAvatar);
            if (pv == null) { LatePlugin.Log.LogError($"[Death Sync] Null PV for {nick}."); return; }
            bool isDisabled = false, isDeadSet = false;
            try
            {
                if (ReflectionCache.PlayerAvatar_IsDisabledField != null) isDisabled = (bool)(ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(playerAvatar) ?? false);
                if (ReflectionCache.PlayerAvatar_DeadSetField != null) isDeadSet = (bool)(ReflectionCache.PlayerAvatar_DeadSetField.GetValue(playerAvatar) ?? false);
            }
            catch (Exception ex) { LatePlugin.Log.LogError($"[Death Sync] Error reflecting isDisabled/deadSet for {nick}: {ex}"); }
            if (isDisabled || isDeadSet) { LatePlugin.Log.LogInfo($"[Death Sync] Player {nick} already dead/disabled."); return; }
            try { pv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, -1); LatePlugin.Log.LogInfo($"[Death Sync] Sent PlayerDeathRPC for {nick}."); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[Death Sync] Error sending PlayerDeathRPC for {nick}: {ex}"); }
        }
    }

    private static void SyncTruckScreenForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        TruckScreenText? screen = TruckScreenText.instance;
        if (screen == null) { LatePlugin.Log.LogWarning("[Truck Sync] Instance null."); return; }
        try
        {
            PhotonView? pv = PhotonUtilities.GetPhotonView(screen);
            if (pv == null) { LatePlugin.Log.LogWarning("[Truck Sync] PV null."); return; }
            if (ReflectionCache.TruckScreenText_CurrentPageIndexField == null) { LatePlugin.Log.LogError("[Truck Sync] Reflection field null."); return; }
            pv.RPC("InitializeTextTypingRPC", targetPlayer);
            int hostPage = -1;
            try { hostPage = (int)(ReflectionCache.TruckScreenText_CurrentPageIndexField.GetValue(screen) ?? -1); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[Truck Sync] Error reflecting currentPageIndex: {ex}"); }
            if (hostPage >= 0) { pv.RPC("GotoPageRPC", targetPlayer, hostPage); LatePlugin.Log.LogInfo($"[Truck Sync] Synced page {hostPage} for {nick}."); }
            else { LatePlugin.Log.LogInfo($"[Truck Sync] Synced init for {nick}. Host page invalid."); }
        }
        catch (Exception ex) { LatePlugin.Log.LogError($"[Truck Sync] Error: {ex}"); }
    }

    private static void TriggerPropSwitchSetup(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        ValuableDirector? director = ValuableDirector.instance;
        if (director == null) { LatePlugin.Log.LogWarning($"[PropSwitch Sync] Instance null."); return; }
        PhotonView? directorPV = PhotonUtilities.GetPhotonView(director);
        if (directorPV == null) { LatePlugin.Log.LogWarning($"[PropSwitch Sync] PV null."); return; }
        try { directorPV.RPC("VolumesAndSwitchSetupRPC", targetPlayer); LatePlugin.Log.LogInfo($"[PropSwitch Sync] Sending RPC to {nick}."); }
        catch (Exception ex) { LatePlugin.Log.LogError($"[PropSwitch Sync] Error sending RPC: {ex}"); }
    }

    private static void SyncArenaStateForPlayer(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        if (targetPlayer == null) { LatePlugin.Log.LogWarning("[Arena Sync] Target player is null."); return; }
        if (!PhotonUtilities.IsRealMasterClient()) { return; }
        Arena arenaInstance = Arena.instance;
        if (arenaInstance == null) { LatePlugin.Log.LogWarning("[Arena Sync] Arena.instance is null."); return; }
        PhotonView? arenaPV = null;
        if (ReflectionCache.Arena_PhotonViewField != null) { try { arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arenaInstance) as PhotonView; } catch { } }
        if (arenaPV == null) { LatePlugin.Log.LogWarning("[Arena Sync] Arena PV null from ReflectionCache."); return; }

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
        LatePlugin.Log.LogInfo($"[LateJoinManager][Arena Sync] Finished Arena state sync (main part) for {targetNickname}.");
    }

    // Nested class (will also move to LevelSyncManager)
    public class HostArenaPlatformSyncManager : MonoBehaviourPunCallbacks
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
                        GameObject go = new GameObject("HostArenaPlatformSyncManager_LATE_LJ");
                        _instance = go.AddComponent<HostArenaPlatformSyncManager>();
                        DontDestroyOnLoad(go);
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
            yield return new WaitForSeconds(0.5f);
            PhotonView? arenaPV = null; Arena.States hostCurrentArenaState = Arena.States.Idle; int hostArenaLevel = 0;
            if (ReflectionCache.Arena_PhotonViewField != null) { try { arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arena) as PhotonView; } catch { } }
            if (arenaPV == null) { yield break; }
            if (ReflectionCache.Arena_CurrentStateField != null) { try { hostCurrentArenaState = (Arena.States)(ReflectionCache.Arena_CurrentStateField.GetValue(arena) ?? Arena.States.Idle); } catch { } }
            if (ReflectionCache.Arena_LevelField != null) { try { hostArenaLevel = (int)(ReflectionCache.Arena_LevelField.GetValue(arena) ?? 0); } catch { yield break; } }
            else { yield break; }
            if (hostArenaLevel > 0)
            {
                for (int i = 0; i < hostArenaLevel; i++)
                {
                    if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) yield break;
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformWarning); yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
                    if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) yield break;
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformRemove); yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
                }
            }
            if (ReflectionCache.Arena_CurrentStateField != null) { try { hostCurrentArenaState = (Arena.States)(ReflectionCache.Arena_CurrentStateField.GetValue(arena) ?? Arena.States.Idle); } catch { } }
            arenaPV.RPC("StateSetRPC", targetPlayer, hostCurrentArenaState); yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
        }
    }
    #endregion
}