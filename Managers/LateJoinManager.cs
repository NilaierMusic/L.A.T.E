// File: L.A.T.E/Managers/LateJoinManager.cs
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq; // Added for Linq usage in ResyncExtractionPointItems
using UnityEngine;
using Object = UnityEngine.Object;
using LATE.Core;
using LATE.Config;
using LATE.Utilities;
using LATE.DataModels; // For PlayerStatus in SyncPlayerDeathState

// Using directives for other managers will be added if/when this class calls them.
// For now, it contains the full logic that will eventually be split.

namespace LATE.Managers;

/// <summary>
/// Manages the state and synchronization process for players who join after a
/// level has started. Orchestrates calls to more specialized sync managers once they are populated.
/// Currently holds the detailed sync logic that will be migrated.
/// </summary>
internal static class LateJoinManager
{
    #region Constants and Fields
    // This constant will eventually move to ItemSyncManager with its coroutine.
    private static readonly float itemResyncDelay = 0.2f;
    #endregion

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

    #region Central Synchronization Method (Will delegate more in future)
    public static void SyncAllStateForPlayer(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        int actorNr = targetPlayer.ActorNumber;
        string nickname = targetPlayer.NickName ?? $"ActorNr {actorNr}";
        LatePlugin.Log.LogInfo($"[LateJoinManager] === Orchestrating SyncAllStateForPlayer for {nickname} ===");
        try
        {
            // These methods currently reside in this class but will be moved.
            // ItemSyncManager and LevelSyncManager will be responsible for these.
            SyncLevelState(targetPlayer); // To LevelSyncManager
            SyncModuleConnectionStatesForPlayer(targetPlayer); // To LevelSyncManager
            SyncExtractionPointsForPlayer(targetPlayer); // To LevelSyncManager

            bool isShopScene = SemiFunc.RunIsShop();
            if (isShopScene)
            {
                SyncAllShopItemsForPlayer(targetPlayer); // To ItemSyncManager
            }
            else
            {
                SyncAllValuablesForPlayer(targetPlayer); // To ItemSyncManager
                DestructionManager.SyncHingeStatesForPlayer(targetPlayer); // Already in LATE.Managers
            }
            TriggerPropSwitchSetup(targetPlayer); // To LevelSyncManager

            EnemySyncManager.SyncAllEnemyStatesForPlayer(targetPlayer); // Already in LATE.Managers
            EnemySyncManager.NotifyEnemiesOfNewPlayer(targetPlayer, playerAvatar); // Already in LATE.Managers

            SyncPlayerDeathState(targetPlayer, playerAvatar); // Stays here or to a PlayerSyncManager? For now, here.
            SyncTruckScreenForPlayer(targetPlayer); // To LevelSyncManager
            SyncAllItemStatesForPlayer(targetPlayer); // To ItemSyncManager

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
                        CoroutineHelper.CoroutineRunner.StartCoroutine(ResyncExtractionPointItems(targetPlayer, epToResync)); // To ItemSyncManager
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

    #region Individual Sync Methods (To be moved to specific managers)

    // To LevelSyncManager
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

        LevelGenerator? levelGen = LevelGenerator.Instance; // Potentially null
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
            // Do not return here, try to sync LevelGenerator state if possible
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

    // To LevelSyncManager
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

        Module[] allModules = Object.FindObjectsOfType<Module>(true); // Include inactive modules
        if (allModules == null || allModules.Length == 0)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager][Module Sync] Found 0 Module components. Skipping sync.");
            return;
        }

        int syncedCount = 0;
        int skippedCount = 0;

        foreach (Module module in allModules)
        {
            if (module == null)
            {
                skippedCount++;
                continue;
            }
            PhotonView? pv = PhotonUtilities.GetPhotonView(module);
            if (pv == null)
            {
                LatePlugin.Log.LogWarning($"[LateJoinManager][Module Sync] Module '{module.gameObject?.name ?? "NULL_GAMEOBJECT"}' is missing PhotonView. Skipping.");
                skippedCount++;
                continue;
            }

            try
            {
                bool setupDone = (bool)(ReflectionCache.Module_SetupDoneField.GetValue(module) ?? false);
                if (!setupDone)
                {
                    //LatePlugin.Log.LogDebug($"[LateJoinManager][Module Sync] Skipping module '{module.gameObject?.name ?? "NULL_GAMEOBJECT"}' (ViewID: {pv.ViewID}): Not SetupDone on host.");
                    skippedCount++;
                    continue;
                }

                bool top = (bool)(ReflectionCache.Module_ConnectingTopField.GetValue(module) ?? false);
                bool bottom = (bool)(ReflectionCache.Module_ConnectingBottomField.GetValue(module) ?? false);
                bool right = (bool)(ReflectionCache.Module_ConnectingRightField.GetValue(module) ?? false);
                bool left = (bool)(ReflectionCache.Module_ConnectingLeftField.GetValue(module) ?? false);
                bool first = (bool)(ReflectionCache.Module_FirstField.GetValue(module) ?? false);

                //LatePlugin.Log.LogDebug($"[LateJoinManager][Module Sync] Syncing module '{module.gameObject?.name ?? "NULL_GAMEOBJECT"}' (ViewID: {pv.ViewID}) state to {nick}: T={top}, B={bottom}, R={right}, L={left}, First={first}");
                pv.RPC("ModuleConnectionSetRPC", targetPlayer, top, bottom, right, left, first);
                syncedCount++;
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[LateJoinManager][Module Sync] Error processing or sending ModuleConnectionSetRPC for module '{module.gameObject?.name ?? "NULL_GAMEOBJECT"}' (ViewID: {pv.ViewID}) to {nick}: {ex}");
                skippedCount++;
            }
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][Module Sync] Finished Module connection sync for {nick}. Synced: {syncedCount}, Skipped: {skippedCount} (Out of {allModules.Length} total).");
    }

    // To LevelSyncManager
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

        ExtractionPoint[]? allExtractionPoints = Object.FindObjectsOfType<ExtractionPoint>(); // Find all, including inactive
        int hostSurplus = 0;
        bool isAnyEpActiveOnHost = false;
        ExtractionPoint? currentActiveEpOnHost = null;

        try
        {
            hostSurplus = (int)(ReflectionCache.RoundDirector_ExtractionPointSurplusField.GetValue(RoundDirector.instance) ?? 0);
            isAnyEpActiveOnHost = (bool)(ReflectionCache.RoundDirector_ExtractionPointActiveField.GetValue(RoundDirector.instance) ?? false);
            currentActiveEpOnHost = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint;

            if (isAnyEpActiveOnHost && currentActiveEpOnHost == null) isAnyEpActiveOnHost = false; // Correct inconsistent state
            else if (!isAnyEpActiveOnHost && currentActiveEpOnHost != null) currentActiveEpOnHost = null; // Correct inconsistent state
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[LateJoinManager][EP Sync] Error reflecting RoundDirector state: {ex}. Aborting EP sync.");
            return;
        }

        LatePlugin.Log.LogInfo($"[LateJoinManager][EP Sync] Host EP Active: {isAnyEpActiveOnHost}. Current EP: '{currentActiveEpOnHost?.name ?? "None"}'. Surplus: {hostSurplus}");
        PhotonView? firstEpPvForGlobalUnlock = null; // Used if no EP is active

        if (allExtractionPoints != null)
        {
            foreach (ExtractionPoint ep in allExtractionPoints)
            {
                if (ep == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(ep);
                if (pv == null) continue;

                if (firstEpPvForGlobalUnlock == null) firstEpPvForGlobalUnlock = pv; // Cache first valid PV

                try
                {
                    ExtractionPoint.State hostState = (ExtractionPoint.State)(ReflectionCache.ExtractionPoint_CurrentStateField.GetValue(ep) ?? ExtractionPoint.State.Idle);
                    bool isThisTheShopEP = (bool)(ReflectionCache.ExtractionPoint_IsShopField.GetValue(ep) ?? false);

                    pv.RPC("StateSetRPC", targetPlayer, hostState);
                    pv.RPC("ExtractionPointSurplusRPC", targetPlayer, hostSurplus);

                    if (isAnyEpActiveOnHost && currentActiveEpOnHost != null && !isThisTheShopEP)
                    {
                        if (ep == currentActiveEpOnHost) // This is the currently active EP on host
                        {
                            bool hostGoalFetched = (bool)(ReflectionCache.ExtractionPoint_HaulGoalFetchedField.GetValue(ep) ?? false);
                            if (hostGoalFetched && ep.haulGoal > 0) // Ensure haulGoal is positive
                            {
                                pv.RPC("HaulGoalSetRPC", targetPlayer, ep.haulGoal);
                            }
                        }
                        else if (hostState == ExtractionPoint.State.Idle) // Other EPs that are idle should be deniable
                        {
                            pv.RPC("ButtonDenyRPC", targetPlayer);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogError($"[LateJoinManager][EP Sync] RPC Error for EP '{ep.name}': {ex}");
                }
            }
        }

        if (!isAnyEpActiveOnHost) // If no EP is active, ensure client can activate one
        {
            if (firstEpPvForGlobalUnlock != null)
            {
                try
                {
                    firstEpPvForGlobalUnlock.RPC("ExtractionPointsUnlockRPC", targetPlayer);
                }
                catch (Exception unlockEx)
                {
                    LatePlugin.Log.LogError($"[LateJoinManager][EP Sync] Failed global ExtractionPointsUnlockRPC: {unlockEx}");
                }
            }
            else LatePlugin.Log.LogWarning("[LateJoinManager][EP Sync] Cannot send global EP unlock RPC: No valid EP PhotonView found.");
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][EP Sync] Finished EP state/goal/surplus sync for {targetPlayerNickname}.");
    }

    // To ItemSyncManager
    private static void SyncAllValuablesForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        if (ReflectionCache.ValuableObject_DollarValueSetField == null)
        {
            LatePlugin.Log.LogError("[LateJoinManager][Valuable Sync] Reflection field ValuableObject_DollarValueSetField is null from ReflectionCache.");
            return;
        }

        ValuableObject[]? allValuables = Object.FindObjectsOfType<ValuableObject>(); // Include inactive
        if (allValuables == null || allValuables.Length == 0)
        {
            LatePlugin.Log.LogWarning($"[LateJoinManager][Valuable Sync] Found 0 valuable objects in scene for {nick}.");
            return;
        }

        int syncedCount = 0;
        foreach (ValuableObject valuable in allValuables)
        {
            if (valuable == null) continue;
            PhotonView? pv = PhotonUtilities.GetPhotonView(valuable);
            if (pv == null) continue;

            bool isValueSet = false;
            try
            {
                isValueSet = (bool)(ReflectionCache.ValuableObject_DollarValueSetField.GetValue(valuable) ?? false);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[LateJoinManager][Valuable Sync] Error reflecting dollarValueSet for {valuable.name}: {ex.Message}. Skipping item.");
                continue;
            }

            if (isValueSet) // Only sync if the value has been set on the host
            {
                try
                {
                    pv.RPC("DollarValueSetRPC", targetPlayer, valuable.dollarValueCurrent);
                    syncedCount++;
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogWarning($"[LateJoinManager][Valuable Sync] DollarValueSetRPC failed for {valuable.name}: {ex.Message}");
                }
            }
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][Valuable Sync] Synced dollar values for {syncedCount}/{allValuables.Length} valuables for {nick}.");
    }

    // To ItemSyncManager
    private static void SyncAllShopItemsForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        if (ReflectionCache.ItemAttributes_ValueField == null || ReflectionCache.ItemAttributes_ShopItemField == null)
        {
            LatePlugin.Log.LogError("[LateJoinManager][Shop Sync] Reflection fields for ItemAttributes (Value or ShopItem) are null from ReflectionCache.");
            return;
        }

        ItemAttributes[]? allItems = Object.FindObjectsOfType<ItemAttributes>(); // Include inactive
        if (allItems == null || allItems.Length == 0)
        {
            LatePlugin.Log.LogWarning($"[LateJoinManager][Shop Sync] Found 0 ItemAttributes objects in scene for {nick}.");
            return;
        }

        int syncedCount = 0;
        foreach (ItemAttributes itemAttr in allItems)
        {
            if (itemAttr == null) continue;

            bool isShopItem = false;
            try
            {
                isShopItem = (bool)(ReflectionCache.ItemAttributes_ShopItemField.GetValue(itemAttr) ?? false);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[LateJoinManager][Shop Sync] Error reflecting shopItem for {itemAttr.name}: {ex.Message}. Skipping item.");
                continue;
            }

            if (!isShopItem) continue; // Only sync actual shop items

            PhotonView? pv = PhotonUtilities.GetPhotonView(itemAttr);
            if (pv == null) continue;

            int hostValue = 0;
            try
            {
                hostValue = (int)(ReflectionCache.ItemAttributes_ValueField.GetValue(itemAttr) ?? 0);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[LateJoinManager][Shop Sync] Error reflecting value for {itemAttr.name}: {ex.Message}. Skipping item.");
                continue;
            }

            if (hostValue <= 0) continue; // Don't sync if value is zero or less (e.g., not set or free)

            try
            {
                pv.RPC("GetValueRPC", targetPlayer, hostValue);
                syncedCount++;
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[LateJoinManager][Shop Sync] GetValueRPC failed for {itemAttr.name}: {ex.Message}");
            }
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][Shop Sync] Synced values for {syncedCount} shop items for {nick}.");
    }

    // Stays here or to a PlayerSyncManager? For now, here.
    private static void SyncPlayerDeathState(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        if (!ConfigManager.KillIfPreviouslyDead.Value)
        {
            LatePlugin.Log.LogDebug("[LateJoinManager][Death Sync] KillIfPreviouslyDead is disabled by config. Skipping death sync.");
            return;
        }

        PlayerStatus status = PlayerStateManager.GetPlayerStatus(targetPlayer);
        if (status == PlayerStatus.Dead) // Using the enum from LATE.DataModels
        {
            PhotonView? pv = PhotonUtilities.GetPhotonView(playerAvatar);
            if (pv == null)
            {
                LatePlugin.Log.LogError($"[LateJoinManager][Death Sync] Could not get PhotonView for player {nick} to sync death state.");
                return;
            }

            bool isDisabled = false;
            bool isDeadSet = false;
            try
            {
                if (ReflectionCache.PlayerAvatar_IsDisabledField != null)
                    isDisabled = (bool)(ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(playerAvatar) ?? false);

                if (ReflectionCache.PlayerAvatar_DeadSetField != null)
                    isDeadSet = (bool)(ReflectionCache.PlayerAvatar_DeadSetField.GetValue(playerAvatar) ?? false);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[LateJoinManager][Death Sync] Error reflecting isDisabled/deadSet for {nick}: {ex}");
                // Continue, attempt to send PlayerDeathRPC anyway if appropriate
            }

            if (isDisabled || isDeadSet)
            {
                LatePlugin.Log.LogInfo($"[LateJoinManager][Death Sync] Player {nick} is already dead or disabled on their client. No PlayerDeathRPC needed.");
                return;
            }

            try
            {
                LatePlugin.Log.LogInfo($"[LateJoinManager][Death Sync] Sending PlayerDeathRPC for previously dead player {nick}.");
                pv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, -1); // -1 for generic death, not by specific enemy
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[LateJoinManager][Death Sync] Error sending PlayerDeathRPC for {nick}: {ex}");
            }
        }
    }

    // To LevelSyncManager
    private static void SyncTruckScreenForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        TruckScreenText? screen = TruckScreenText.instance;
        if (screen == null)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager][Truck Sync] TruckScreenText.instance is null. Skipping sync.");
            return;
        }

        try
        {
            // TruckScreenText itself has a PhotonView
            PhotonView? pv = PhotonUtilities.GetPhotonView(screen);
            if (pv == null)
            {
                LatePlugin.Log.LogWarning("[LateJoinManager][Truck Sync] TruckScreenText PhotonView is null. Skipping sync.");
                return;
            }
            if (ReflectionCache.TruckScreenText_CurrentPageIndexField == null)
            {
                LatePlugin.Log.LogError("[LateJoinManager][Truck Sync] Reflection field TruckScreenText_CurrentPageIndexField is null from ReflectionCache.");
                return;
            }

            pv.RPC("InitializeTextTypingRPC", targetPlayer); // Initialize first

            int hostPage = -1;
            try
            {
                hostPage = (int)(ReflectionCache.TruckScreenText_CurrentPageIndexField.GetValue(screen) ?? -1);
            }
            catch (Exception refEx)
            {
                LatePlugin.Log.LogError($"[LateJoinManager][Truck Sync] Error reflecting currentPageIndex: {refEx}");
                hostPage = -1; // Default to invalid on error
            }

            if (hostPage >= 0)
            {
                pv.RPC("GotoPageRPC", targetPlayer, hostPage);
                LatePlugin.Log.LogInfo($"[LateJoinManager][Truck Sync] Synced truck screen page {hostPage} for {nick}.");
            }
            else
            {
                LatePlugin.Log.LogInfo($"[LateJoinManager][Truck Sync] Synced truck screen initialization for {nick}. Host page was invalid or not set ({hostPage}).");
            }
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[LateJoinManager][Truck Sync] General error during truck screen sync for {nick}: {ex}");
        }
    }

    // To LevelSyncManager
    private static void TriggerPropSwitchSetup(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        ValuableDirector? director = ValuableDirector.instance;
        if (director == null)
        {
            LatePlugin.Log.LogWarning($"[LateJoinManager][PropSwitch Sync] ValuableDirector.instance is null. Skipping setup for {nick}.");
            return;
        }

        // ValuableDirector itself has a PhotonView
        PhotonView? directorPV = PhotonUtilities.GetPhotonView(director);
        if (directorPV == null)
        {
            LatePlugin.Log.LogWarning($"[LateJoinManager][PropSwitch Sync] ValuableDirector PhotonView is null. Skipping setup for {nick}.");
            return;
        }

        try
        {
            LatePlugin.Log.LogInfo($"[LateJoinManager][PropSwitch Sync] Sending VolumesAndSwitchSetupRPC to {nick}.");
            directorPV.RPC("VolumesAndSwitchSetupRPC", targetPlayer);
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[LateJoinManager][PropSwitch Sync] Error sending VolumesAndSwitchSetupRPC to {nick}: {ex}");
        }
    }

    // To ItemSyncManager
    private static void SyncAllItemStatesForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogInfo($"[LateJoinManager][Item State Sync] Starting FULL item state sync for {nick}.");

        int syncedTogglesState = 0, syncedTogglesDisabled = 0, syncedBatteries = 0, syncedMines = 0;
        int syncedMelees = 0, syncedDronesActivated = 0, syncedGrenadesActive = 0, syncedTrackerTargets = 0, syncedHealthPacksUsed = 0;

        // ItemToggle States (ON/OFF)
        ItemToggle[] allToggles = Object.FindObjectsOfType<ItemToggle>(true);
        if (allToggles != null)
        {
            foreach (ItemToggle itemToggle in allToggles)
            {
                if (itemToggle == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemToggle);
                if (pv == null) continue;
                bool hostToggleState = itemToggle.toggleState; // Direct access, no reflection needed here
                pv.RPC("ToggleItemRPC", targetPlayer, hostToggleState, -1);
                syncedTogglesState++;
            }
        }

        // ItemToggle Disabled State
        if (ReflectionCache.ItemToggle_DisabledField != null && allToggles != null)
        {
            foreach (ItemToggle itemToggle in allToggles) // Reuse found toggles
            {
                if (itemToggle == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemToggle);
                if (pv == null) continue;
                try
                {
                    bool hostIsDisabled = (bool)(ReflectionCache.ItemToggle_DisabledField.GetValue(itemToggle) ?? false);
                    if (hostIsDisabled) { pv.RPC("ToggleDisableRPC", targetPlayer, true); syncedTogglesDisabled++; }
                }
                catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSync] Error reflecting ItemToggle.disabled for '{itemToggle.gameObject.name}': {refEx}"); }
            }
        }

        // ItemBattery Levels
        ItemBattery[] allBatteries = Object.FindObjectsOfType<ItemBattery>(true);
        if (allBatteries != null)
        {
            foreach (ItemBattery itemBattery in allBatteries)
            {
                if (itemBattery == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemBattery);
                if (pv == null) continue;
                float hostBatteryLife = itemBattery.batteryLife; // Direct access
                int hostBatteryLifeInt = (hostBatteryLife > 0f) ? (int)Mathf.Round(hostBatteryLife / 16.6f) : 0;
                pv.RPC("BatteryFullPercentChangeRPC", targetPlayer, hostBatteryLifeInt, false);
                syncedBatteries++;
            }
        }

        // ItemMine States
        if (ReflectionCache.ItemMine_StateField != null)
        {
            ItemMine[] allMines = Object.FindObjectsOfType<ItemMine>(true);
            if (allMines != null)
            {
                foreach (ItemMine itemMine in allMines)
                {
                    if (itemMine == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(itemMine);
                    if (pv == null) continue;
                    try
                    {
                        int hostMineStateInt = (int)(ReflectionCache.ItemMine_StateField.GetValue(itemMine) ?? 0); // Default to 0 if null
                        pv.RPC("StateSetRPC", targetPlayer, hostMineStateInt);
                        syncedMines++;
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSync] Error reflecting ItemMine.state for '{itemMine.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemMelee Broken State
        if (ReflectionCache.PhysGrabObject_IsMeleeField != null)
        {
            ItemMelee[] allMelees = Object.FindObjectsOfType<ItemMelee>(true);
            if (allMelees != null)
            {
                foreach (ItemMelee itemMelee in allMelees)
                {
                    if (itemMelee == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(itemMelee);
                    if (pv == null) continue;
                    ItemBattery? meleeBattery = itemMelee.GetComponent<ItemBattery>(); // Needs ItemBattery component
                    PhysGrabObject? meleePGO = itemMelee.GetComponent<PhysGrabObject>(); // Needs PhysGrabObject
                    if (meleeBattery == null || meleePGO == null) continue;
                    try
                    {
                        bool hostIsBroken = meleeBattery.batteryLife <= 0f;
                        bool hostPGOIsMelee = (bool)(ReflectionCache.PhysGrabObject_IsMeleeField.GetValue(meleePGO) ?? false);
                        if (hostIsBroken && hostPGOIsMelee) { pv.RPC("MeleeBreakRPC", targetPlayer); syncedMelees++; }
                        else if (!hostIsBroken && !hostPGOIsMelee) { pv.RPC("MeleeFixRPC", targetPlayer); syncedMelees++; } // Fix if it was wrongly broken client-side
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSync] Error reflecting PhysGrabObject.isMelee for '{meleePGO.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemDrone Activated State
        ItemDrone[] allDrones = Object.FindObjectsOfType<ItemDrone>(true);
        if (allDrones != null)
        {
            foreach (ItemDrone drone in allDrones)
            {
                if (drone == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(drone);
                if (pv == null) continue;
                ItemToggle? droneToggle = drone.GetComponent<ItemToggle>();
                if (droneToggle != null && droneToggle.toggleState) { pv.RPC("ButtonToggleRPC", targetPlayer, true); syncedDronesActivated++; }
            }
        }

        // ItemHealthPack Used State
        if (ReflectionCache.ItemHealthPack_UsedField != null)
        {
            ItemHealthPack[] allHealthPacks = Object.FindObjectsOfType<ItemHealthPack>(true);
            if (allHealthPacks != null)
            {
                foreach (ItemHealthPack healthPack in allHealthPacks)
                {
                    if (healthPack == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(healthPack);
                    if (pv == null) continue;
                    try
                    {
                        bool hostIsUsed = (bool)(ReflectionCache.ItemHealthPack_UsedField.GetValue(healthPack) ?? false);
                        if (hostIsUsed) { pv.RPC("UsedRPC", targetPlayer); syncedHealthPacksUsed++; }
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSync] Error reflecting ItemHealthPack.used for '{healthPack.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemGrenade Active State
        if (ReflectionCache.ItemGrenade_IsActiveField != null)
        {
            ItemGrenade[] allGrenades = Object.FindObjectsOfType<ItemGrenade>(true);
            if (allGrenades != null)
            {
                foreach (ItemGrenade grenade in allGrenades)
                {
                    if (grenade == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(grenade);
                    if (pv == null) continue;
                    try
                    {
                        bool hostIsActive = (bool)(ReflectionCache.ItemGrenade_IsActiveField.GetValue(grenade) ?? false);
                        if (hostIsActive) { pv.RPC("TickStartRPC", targetPlayer); syncedGrenadesActive++; }
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSync] Error reflecting ItemGrenade.isActive for '{grenade.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemTracker Target State
        if (ReflectionCache.ItemTracker_CurrentTargetField != null)
        {
            ItemTracker[] allTrackers = Object.FindObjectsOfType<ItemTracker>(true);
            if (allTrackers != null)
            {
                foreach (ItemTracker tracker in allTrackers)
                {
                    if (tracker == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(tracker);
                    if (pv == null) continue;
                    try
                    {
                        object? hostTargetObj = ReflectionCache.ItemTracker_CurrentTargetField.GetValue(tracker);
                        if (hostTargetObj is Transform hostTargetTransform && hostTargetTransform != null)
                        {
                            PhotonView? targetPV = hostTargetTransform.GetComponentInParent<PhotonView>(); // Get PV from target transform
                            if (targetPV != null) { pv.RPC("SetTargetRPC", targetPlayer, targetPV.ViewID); syncedTrackerTargets++; }
                        }
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSync] Error reflecting ItemTracker.currentTarget for '{tracker.gameObject.name}': {refEx}"); }
                }
            }
        }

        LatePlugin.Log.LogInfo(
            $"[LateJoinManager][Item State Sync] Finished FULL item state sync for {nick}. Totals: " +
            $"TogglesState={syncedTogglesState}, TogglesDisabled={syncedTogglesDisabled}, Batteries={syncedBatteries}, Mines={syncedMines}, Melees={syncedMelees}, " +
            $"DronesActivated={syncedDronesActivated}, GrenadesActive={syncedGrenadesActive}, TrackerTargets={syncedTrackerTargets}, HealthPacksUsed={syncedHealthPacksUsed}"
        );
    }

    // To LevelSyncManager
    private static void SyncArenaStateForPlayer(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        if (targetPlayer == null)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager][Arena Sync] Target player is null. Aborting sync.");
            return;
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][Arena Sync] Starting FULL Arena state sync for {targetNickname}.");

        if (!PhotonUtilities.IsRealMasterClient())
        {
            LatePlugin.Log.LogDebug("[LateJoinManager][Arena Sync] Not Master Client. Skipping host-specific sync.");
            return;
        }

        Arena arenaInstance = Arena.instance;
        if (arenaInstance == null)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager][Arena Sync] Arena.instance is null. Aborting.");
            return;
        }

        PhotonView? arenaPV = null;
        if (ReflectionCache.Arena_PhotonViewField != null)
        {
            try { arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arenaInstance) as PhotonView; }
            catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaSync] Error reflecting Arena.photonView: {ex}"); return; }
        }
        if (arenaPV == null)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager][Arena Sync] Arena PhotonView is null from ReflectionCache. Aborting.");
            return;
        }

        // Sync Cage Destruction
        bool hostCageIsDestroyed = false;
        if (ReflectionCache.Arena_CrownCageDestroyedField != null)
        {
            try { hostCageIsDestroyed = (bool)(ReflectionCache.Arena_CrownCageDestroyedField.GetValue(arenaInstance) ?? false); }
            catch (Exception ex) { LatePlugin.Log.LogWarning($"[ArenaSync] Error reflecting 'crownCageDestroyed': {ex}"); }
        }
        if (hostCageIsDestroyed) { try { arenaPV.RPC("DestroyCrownCageRPC", targetPlayer); } catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaSync] Failed DestroyCrownCageRPC: {ex}"); } }

        // Sync Winner
        PlayerAvatar? currentWinner = null;
        if (ReflectionCache.Arena_WinnerPlayerField != null)
        {
            try { currentWinner = ReflectionCache.Arena_WinnerPlayerField.GetValue(arenaInstance) as PlayerAvatar; }
            catch (Exception ex) { LatePlugin.Log.LogWarning($"[ArenaSync] Error reflecting 'winnerPlayer': {ex}"); }
        }
        if (currentWinner != null)
        {
            int winnerPhysGrabberViewID = GameUtilities.GetPhysGrabberViewId(currentWinner);
            if (winnerPhysGrabberViewID > 0) { try { arenaPV.RPC("CrownGrabRPC", targetPlayer, winnerPhysGrabberViewID); } catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaSync] Failed CrownGrabRPC: {ex}"); } }
        }

        // Sync Pedestal Screen Player Count
        int actualLivePlayerCount = 0;
        if (GameDirector.instance?.PlayerList != null && ReflectionCache.PlayerAvatar_IsDisabledField != null)
        {
            foreach (PlayerAvatar pa in GameDirector.instance.PlayerList)
            {
                if (pa == null) continue;
                try
                {
                    if (!(bool)(ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(pa) ?? true)) actualLivePlayerCount++;
                }
                catch { actualLivePlayerCount++; } // Assume alive on reflection error to be safe
            }
        }
        try { arenaPV.RPC("PlayerKilledRPC", targetPlayer, actualLivePlayerCount); } catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaSync] Error sending PlayerKilledRPC: {ex}"); }

        // Sync Arena Platform States
        HostArenaPlatformSyncManager syncManager = HostArenaPlatformSyncManager.Instance; // Nested class instance
        if (syncManager != null) syncManager.StartPlatformCatchUpForPlayer(targetPlayer, arenaInstance);
        else LatePlugin.Log.LogError("[LateJoinManager][Arena Sync] HostArenaPlatformSyncManager instance is null!");

        LatePlugin.Log.LogInfo($"[LateJoinManager][Arena Sync] Finished Arena state sync (main part) for {targetNickname}.");
    }

    // Nested class for Arena Platform Sync Coroutine
    // This will move with SyncArenaStateForPlayer to LevelSyncManager
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
                        GameObject go = new GameObject("HostArenaPlatformSyncManager_LATE"); // Unique name
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
            yield return new WaitForSeconds(0.5f); // Initial delay

            PhotonView? arenaPV = null;
            Arena.States hostCurrentArenaState = Arena.States.Idle;
            int hostArenaLevel = 0;

            if (ReflectionCache.Arena_PhotonViewField != null)
            {
                try { arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arena) as PhotonView; }
                catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.photonView: {ex}"); yield break; }
            }
            if (arenaPV == null) { LatePlugin.Log.LogError("[ArenaPlatformSync] Arena.photonView is null from ReflectionCache."); yield break; }

            if (ReflectionCache.Arena_CurrentStateField != null)
            {
                try { hostCurrentArenaState = (Arena.States)(ReflectionCache.Arena_CurrentStateField.GetValue(arena) ?? Arena.States.Idle); }
                catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.currentState: {ex}"); }
            }
            if (ReflectionCache.Arena_LevelField != null)
            {
                try { hostArenaLevel = (int)(ReflectionCache.Arena_LevelField.GetValue(arena) ?? 0); }
                catch (Exception ex) { LatePlugin.Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.level: {ex}"); yield break; }
            }
            else { LatePlugin.Log.LogError("[ArenaPlatformSync] Arena_LevelField is null from ReflectionCache."); yield break; }

            if (hostArenaLevel > 0)
            {
                for (int i = 0; i < hostArenaLevel; i++)
                {
                    if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) { LatePlugin.Log.LogWarning($"[ArenaPlatformSync] Player {targetPlayer.NickName} left. Aborting."); yield break; }
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformWarning);
                    yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
                    if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) { LatePlugin.Log.LogWarning($"[ArenaPlatformSync] Player {targetPlayer.NickName} left. Aborting."); yield break; }
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformRemove);
                    yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
                }
            }

            // Re-fetch current state before final set
            if (ReflectionCache.Arena_CurrentStateField != null)
            {
                try { hostCurrentArenaState = (Arena.States)(ReflectionCache.Arena_CurrentStateField.GetValue(arena) ?? Arena.States.Idle); } catch { /* Already logged */ }
            }
            arenaPV.RPC("StateSetRPC", targetPlayer, hostCurrentArenaState);
            yield return new WaitForSeconds(RPC_CATCHUP_DELAY); // Small delay for final state to apply
            LatePlugin.Log.LogInfo($"[ArenaPlatformSync] Arena platform catch-up sequence complete for {targetPlayer.NickName}.");
        }
    }

    // To ItemSyncManager
    private static IEnumerator ResyncExtractionPointItems(Player targetPlayer, ExtractionPoint epToSync)
    {
        string targetNickname = targetPlayer?.NickName ?? "<UnknownPlayer>";
        string epName = epToSync?.name ?? "<UnknownEP>";
        LatePlugin.Log.LogInfo($"[LateJoinManager][Item Resync] Starting for {targetNickname} in EP '{epName}'");

        if (targetPlayer == null || epToSync == null || !PhotonUtilities.IsRealMasterClient() || CoroutineHelper.CoroutineRunner == null)
        {
            LatePlugin.Log.LogWarning("[LateJoinManager][Item Resync] Aborting: Invalid state (null player/ep, not master, or null runner).");
            yield break;
        }
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
        {
            LatePlugin.Log.LogWarning($"[LateJoinManager][Item Resync] Aborting: Player {targetNickname} left room.");
            yield break;
        }

        bool isShop = SemiFunc.RunIsShop();
        Vector3 farAwayPosition = new Vector3(epToSync.transform.position.x, epToSync.transform.position.y + 500f, epToSync.transform.position.z);
        List<GameObject> itemsToResync = new List<GameObject>();

        if (isShop)
        {
            if (ShopManager.instance != null && ReflectionCache.ShopManager_ShoppingListField != null)
            {
                try
                {
                    object? listObject = ReflectionCache.ShopManager_ShoppingListField.GetValue(ShopManager.instance);
                    if (listObject is List<ItemAttributes> shopList)
                    {
                        itemsToResync.AddRange(shopList.Where(itemAttr => itemAttr != null && itemAttr.gameObject != null).Select(itemAttr => itemAttr.gameObject));
                    }
                }
                catch (Exception ex) { LatePlugin.Log.LogError($"[ItemResync] Error reflecting ShopManager.shoppingList: {ex}"); }
            }
        }
        else // Level scene
        {
            if (RoundDirector.instance?.dollarHaulList != null)
            {
                itemsToResync.AddRange(RoundDirector.instance.dollarHaulList.Where(go => go != null));
            }
        }

        if (itemsToResync.Count == 0)
        {
            LatePlugin.Log.LogInfo($"[LateJoinManager][Item Resync] No items identified in EP '{epName}' for {targetNickname}.");
            yield break;
        }

        Dictionary<int, (Vector3 pos, Quaternion rot)> originalTransforms = new Dictionary<int, (Vector3, Quaternion)>();
        List<PhysGrabObject> validPhysObjects = new List<PhysGrabObject>();

        foreach (GameObject itemGO in itemsToResync)
        {
            PhysGrabObject? pgo = itemGO.GetComponent<PhysGrabObject>();
            PhotonView? pv = PhotonUtilities.GetPhotonViewFromPGO(pgo); // Use utility
            if (pgo == null || pv == null) continue;

            if (!pv.IsMine) pv.RequestOwnership(); // Request if not ours

            originalTransforms[pv.ViewID] = (itemGO.transform.position, itemGO.transform.rotation);
            validPhysObjects.Add(pgo);
            try { pgo.Teleport(farAwayPosition, itemGO.transform.rotation); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[ItemResync] Error teleporting {itemGO.name} AWAY: {ex}"); }
        }

        yield return new WaitForSeconds(itemResyncDelay);

        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
        {
            LatePlugin.Log.LogWarning($"[LateJoinManager][Item Resync] Aborting teleport BACK: Player {targetNickname} left.");
            yield break;
        }

        foreach (PhysGrabObject pgo in validPhysObjects)
        {
            if (pgo == null || pgo.gameObject == null) continue;
            PhotonView? pv = PhotonUtilities.GetPhotonViewFromPGO(pgo);
            if (pv == null) continue;
            if (originalTransforms.TryGetValue(pv.ViewID, out var originalTransform))
            {
                if (!pv.IsMine) LatePlugin.Log.LogWarning($"[ItemResync] Lost ownership of {pgo.gameObject.name} before teleport back?");
                try { pgo.Teleport(originalTransform.pos, originalTransform.rot); }
                catch (Exception ex) { LatePlugin.Log.LogError($"[ItemResync] Error teleporting {pgo.gameObject.name} BACK: {ex}"); }
            }
        }
        LatePlugin.Log.LogInfo($"[LateJoinManager][Item Resync] Finished item resync sequence for {targetNickname} in EP '{epName}'.");
    }
    #endregion
}