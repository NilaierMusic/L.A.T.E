// File: L.A.T.E/Managers/LateJoinManager.cs
using LATE.Config;
using LATE.Core;
using LATE.DataModels;
using LATE.Utilities;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic; // Added for Dictionary and HashSet
using System.Linq; // Added for LINQ
using Object = UnityEngine.Object;

namespace LATE.Managers;

/// <summary>
/// Manages the state and synchronization process for players who join after a
/// level has started. Orchestrates calls to more specialized sync managers.
/// Central authority for tracking if a player is considered an "active late joiner"
/// for the current scene instance, requiring L.A.T.E. specific syncs.
/// </summary>
internal static class LateJoinManager
{
    private static readonly BepInEx.Logging.ManualLogSource Log = LatePlugin.Log;

    internal enum LateJoinTaskType
    {
        Voice,
        ExtractionPointItems
        // Add other distinct L.A.T.E. asynchronous tasks here if they arise
    }

    #region Player Tracking State
    // _playersNeedingInitialSync: Players who joined late and are awaiting their first LoadingCompleteRPC to trigger SyncAllStateForPlayer.
    private static readonly HashSet<int> _playersNeedingInitialSync = new HashSet<int>();

    // _activeLateJoinersThisScene: Players who joined late this scene and are still undergoing L.A.T.E.'s sync process.
    // Once all L.A.T.E. syncs (initial + async) are done, they are removed.
    private static readonly HashSet<int> _activeLateJoinersThisScene = new HashSet<int>();

    // _pendingAsyncTasksForLateJoiners: Tracks pending asynchronous L.A.T.E. tasks for active late joiners.
    private static readonly Dictionary<int, HashSet<LateJoinTaskType>> _pendingAsyncTasksForLateJoiners = new Dictionary<int, HashSet<LateJoinTaskType>>();
    #endregion

    #region Scene Management
    /// <summary>
    /// Clears all late-join tracking sets, typically called when a new scene loads.
    /// This makes all existing players "regular" from L.A.T.E.'s perspective for the new scene.
    /// </summary>
    public static void ResetSceneTracking()
    {
        Log.LogDebug("[LateJoinManager] Clearing all late-join tracking sets for new scene instance.");
        _playersNeedingInitialSync.Clear();
        _activeLateJoinersThisScene.Clear();
        _pendingAsyncTasksForLateJoiners.Clear();
    }
    #endregion

    #region Player Join and Tracking Logic
    /// <summary>
    /// Handles a new player joining the room, marking them for L.A.T.E. sync process if necessary.
    /// </summary>
    public static void HandlePlayerJoined(Player newPlayer)
    {
        if (newPlayer == null)
        {
            Log.LogWarning("[LateJoinManager] HandlePlayerJoined called with a null player.");
            return;
        }

        int actorNr = newPlayer.ActorNumber;
        string nickname = newPlayer.NickName ?? $"ActorNr {actorNr}";
        bool joinedDuringActiveScene = GameUtilities.IsModLogicActive();

        if (joinedDuringActiveScene)
        {
            if (_playersNeedingInitialSync.Add(actorNr))
            {
                Log.LogInfo($"[LateJoinManager] Player {nickname} (ActorNr {actorNr}) marked as needing INITIAL L.A.T.E. sync trigger (awaiting LoadingCompleteRPC).");
            }
            if (_activeLateJoinersThisScene.Add(actorNr))
            {
                Log.LogInfo($"[LateJoinManager] Player {nickname} (ActorNr {actorNr}) ADDED to _activeLateJoinersThisScene. Will undergo L.A.T.E. sync process.");
            }
        }
        else
        {
            Log.LogInfo($"[LateJoinManager] Player {nickname} (ActorNr {actorNr}) joined during an INACTIVE scene. Not marked as an active L.A.T.E. late-joiner.");
        }
    }

    /// <summary>
    /// Checks if a player is currently marked as needing the initial L.A.T.E. data sync trigger.
    /// </summary>
    internal static bool IsPlayerNeedingInitialSync(int actorNumber) => _playersNeedingInitialSync.Contains(actorNumber);

    /// <summary>
    /// Marks that a player's initial L.A.T.E. sync has been triggered and removes them from the 'needing initial sync' list.
    /// This is typically called before <see cref="SyncAllStateForPlayer"/>.
    /// </summary>
    internal static void MarkInitialSyncTriggered(int actorNumber)
    {
        if (_playersNeedingInitialSync.Remove(actorNumber))
        {
            Log.LogDebug($"[LateJoinManager] Initial L.A.T.E. sync triggered for ActorNr {actorNumber}. Removed from 'NeedingInitialSync' list.");
        }
    }

    /// <summary>
    /// Checks if a player is currently considered an active late joiner for the current scene instance,
    /// meaning L.A.T.E. specific sync logic might still apply to them.
    /// </summary>
    internal static bool IsPlayerAnActiveLateJoiner(int actorNumber) => _activeLateJoinersThisScene.Contains(actorNumber);

    /// <summary>
    /// Checks if an active late joiner is still pending a specific asynchronous L.A.T.E. task.
    /// </summary>
    internal static bool IsLateJoinerPendingAsyncTask(int actorNumber, LateJoinTaskType taskType)
    {
        return _activeLateJoinersThisScene.Contains(actorNumber) &&
               _pendingAsyncTasksForLateJoiners.TryGetValue(actorNumber, out var pendingTasks) &&
               pendingTasks.Contains(taskType);
    }

    /// <summary>
    /// Called by asynchronous L.A.T.E. sync managers (e.g., VoiceManager, ItemSyncManager for EP)
    /// to report that a specific task has completed for a late-joining player.
    /// If all async tasks are complete, the player is removed from active late joiner tracking for this scene.
    /// </summary>
    public static void ReportLateJoinAsyncTaskCompleted(int actorNumber, LateJoinTaskType taskType)
    {
        if (!PhotonUtilities.IsRealMasterClient()) return;

        if (!_activeLateJoinersThisScene.Contains(actorNumber))
        {
            Log.LogDebug($"[LateJoinManager] ReportLateJoinAsyncTaskCompleted for ActorNr {actorNumber}, Task {taskType}, but player is not in _activeLateJoinersThisScene. Ignoring.");
            // Could happen if player left or scene changed during async task.
            _pendingAsyncTasksForLateJoiners.Remove(actorNumber); // Clean up pending tasks if any linger.
            return;
        }

        if (_pendingAsyncTasksForLateJoiners.TryGetValue(actorNumber, out var pendingTasks))
        {
            if (pendingTasks.Remove(taskType))
            {
                Log.LogInfo($"[LateJoinManager] Late join async task {taskType} COMPLETED for ActorNr {actorNumber}. Remaining for them: {pendingTasks.Count}");
            }

            if (pendingTasks.Count == 0)
            {
                Log.LogInfo($"[LateJoinManager] All L.A.T.E. async tasks COMPLETED for ActorNr {actorNumber}.");
                _pendingAsyncTasksForLateJoiners.Remove(actorNumber);
                if (_activeLateJoinersThisScene.Remove(actorNumber))
                {
                    Log.LogInfo($"[LateJoinManager] ActorNr {actorNumber} is now fully L.A.T.E. synced for this scene instance and REMOVED from _activeLateJoinersThisScene.");
                }
            }
        }
        else
        {
            Log.LogWarning($"[LateJoinManager] ReportLateJoinAsyncTaskCompleted for ActorNr {actorNumber}, Task {taskType}, but no pending tasks entry found. This might indicate initial setup issue or premature call.");
            // If they are an active late joiner but have no pending tasks entry, assume something went wrong with setup
            // or all tasks somehow completed without this path. For safety, try to remove from active list.
            if (_activeLateJoinersThisScene.Remove(actorNumber))
            {
                Log.LogWarning($"[LateJoinManager] ActorNr {actorNumber} removed from _activeLateJoinersThisScene due to inconsistent pending task state.");
            }
        }
    }


    /// <summary>
    /// Clears all L.A.T.E. late join tracking for a specific player, typically when they leave.
    /// </summary>
    internal static void ClearPlayerTracking(int actorNumber)
    {
        bool removedInitial = _playersNeedingInitialSync.Remove(actorNumber);
        bool removedActive = _activeLateJoinersThisScene.Remove(actorNumber);
        bool removedPending = _pendingAsyncTasksForLateJoiners.Remove(actorNumber);

        if (removedInitial || removedActive || removedPending)
        {
            Log.LogDebug($"[LateJoinManager] Cleared all L.A.T.E. late join tracking for ActorNr {actorNumber}. (Initial: {removedInitial}, Active: {removedActive}, PendingAsync: {removedPending})");
        }
    }
    #endregion

    #region Central Synchronization Logic
    /// <summary>
    /// Orchestrates the synchronization of all relevant game states to a late-joining player.
    /// This method initiates both synchronous RPCs and sets up tracking for asynchronous L.A.T.E. tasks.
    /// </summary>
    public static void SyncAllStateForPlayer(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        if (targetPlayer == null)
        {
            Log.LogError("[LateJoinManager] SyncAllStateForPlayer called with a null targetPlayer. Aborting.");
            return;
        }

        int actorNr = targetPlayer.ActorNumber;
        string nickname = targetPlayer.NickName ?? $"ActorNr {actorNr}";

        // Crucial Gate: Only proceed if this player is marked as an active late joiner for this scene.
        if (!IsPlayerAnActiveLateJoiner(actorNr))
        {
            Log.LogWarning($"[LateJoinManager] SyncAllStateForPlayer called for {nickname}, but they are NOT in _activeLateJoinersThisScene. Aborting L.A.T.E. sync. This might be normal if they were already fully synced or left.");
            _pendingAsyncTasksForLateJoiners.Remove(actorNr); // Ensure no pending tasks linger if state is inconsistent
            return;
        }

        if (playerAvatar == null)
        {
            Log.LogError($"[LateJoinManager] SyncAllStateForPlayer called with a null playerAvatar for {nickname}. Aborting death/enemy sync portions. This is problematic for full sync.");
            // Decide if other syncs should proceed or if this is fatal.
            // For now, we'll proceed but this player might not become "fully L.A.T.E. synced" if critical async tasks depend on the avatar.
        }

        Log.LogInfo($"[LateJoinManager] === Orchestrating Full L.A.T.E. State Sync for Active Late Joiner: {nickname} (ActorNr {actorNr}) ===");

        try
        {
            // --- Perform synchronous L.A.T.E. syncs ---
            LevelSyncManager.SyncLevelState(targetPlayer);
            LevelSyncManager.SyncModuleConnectionStatesForPlayer(targetPlayer);
            LevelSyncManager.SyncExtractionPointsForPlayer(targetPlayer);

            bool isShopScene = SemiFunc.RunIsShop();
            if (isShopScene)
            {
                ItemSyncManager.SyncAllShopItemsForPlayer(targetPlayer);
            }
            else
            {
                ItemSyncManager.SyncAllValuablesForPlayer(targetPlayer);
                DestructionManager.SyncHingeStatesForPlayer(targetPlayer);
            }
            LevelSyncManager.TriggerPropSwitchSetup(targetPlayer);

            if (playerAvatar != null)
            {
                EnemySyncManager.SyncAllEnemyStatesForPlayer(targetPlayer);
                EnemySyncManager.NotifyEnemiesOfNewPlayer(targetPlayer, playerAvatar);
                SyncPlayerDeathState(targetPlayer, playerAvatar);
            }
            else
            {
                Log.LogWarning($"[LateJoinManager] Skipping Enemy and Death sync for {nickname} due to null playerAvatar during SyncAllStateForPlayer.");
            }

            LevelSyncManager.SyncTruckScreenForPlayer(targetPlayer);
            ItemSyncManager.SyncAllItemStatesForPlayer(targetPlayer);

            if (SemiFunc.RunIsArena())
            {
                LevelSyncManager.SyncArenaStateForPlayer(targetPlayer);
            }

            // --- Setup tracking for asynchronous L.A.T.E. tasks ---
            var asyncTasksToTrack = new HashSet<LateJoinTaskType>
            {
                LateJoinTaskType.Voice
            };

            ExtractionPoint? epToResync = null; // Moved declaration higher
            if (PhotonUtilities.IsRealMasterClient()) // EP resync is host-only
            {
                epToResync = FindEpForResync(isShopScene);
                if (epToResync != null)
                {
                    asyncTasksToTrack.Add(LateJoinTaskType.ExtractionPointItems);
                }
            }
            _pendingAsyncTasksForLateJoiners[actorNr] = asyncTasksToTrack;
            Log.LogInfo($"[LateJoinManager] ActorNr {actorNr} has {_pendingAsyncTasksForLateJoiners[actorNr].Count} async L.A.T.E. tasks pending: [{string.Join(", ", _pendingAsyncTasksForLateJoiners[actorNr])}]");


            // --- Start/Signal asynchronous L.A.T.E. tasks ---
            // VoiceManager will be signalled by its own HandleAvatarUpdate or a direct call if necessary
            // VoiceManager.ScheduleSyncForLateJoiner(targetPlayer); // Or similar, VoiceManager will check IsLateJoinerPendingAsyncTask
            // For now, VoiceManager's existing TryScheduleSync (if called by its HandleAvatarUpdate when player is ready and pending)
            // will lead to it calling ReportLateJoinAsyncTaskCompleted.

            if (epToResync != null) // Check again, as it's conditional
            {
                Log.LogInfo($"[LateJoinManager] Starting ItemSyncManager.ResyncExtractionPointItems coroutine for {nickname} in EP '{epToResync.name}'.");
                if (CoroutineHelper.CoroutineRunner != null)
                {
                    // Pass actorNr to the coroutine or ensure ItemSyncManager knows who it's for to report back.
                    // The coroutine already takes targetPlayer.
                    CoroutineHelper.CoroutineRunner.StartCoroutine(ItemSyncManager.ResyncExtractionPointItems(targetPlayer, epToResync));
                }
                else
                {
                    Log.LogError("[LateJoinManager] Cannot start ResyncExtractionPointItems: CoroutineHelper.CoroutineRunner is null! EP Resync for this player will not complete.");
                    ReportLateJoinAsyncTaskCompleted(actorNr, LateJoinTaskType.ExtractionPointItems); // Mark as failed/skipped
                }
            }

            // If there are no async tasks registered, the player might be considered fully synced immediately.
            // This is handled if _pendingAsyncTasksForLateJoiners[actorNr] is empty after setup.
            if (_pendingAsyncTasksForLateJoiners.TryGetValue(actorNr, out var tasks) && tasks.Count == 0)
            {
                Log.LogInfo($"[LateJoinManager] ActorNr {actorNr} has no L.A.T.E. async tasks pending after initial sync. Marking as fully L.A.T.E. synced.");
                _pendingAsyncTasksForLateJoiners.Remove(actorNr);
                if (_activeLateJoinersThisScene.Remove(actorNr))
                {
                    Log.LogInfo($"[LateJoinManager] ActorNr {actorNr} REMOVED from _activeLateJoinersThisScene immediately after initial sync (no async tasks).");
                }
            }

        }
        catch (Exception ex)
        {
            Log.LogError($"[LateJoinManager] CRITICAL ERROR during SyncAllStateForPlayer for {nickname}: {ex}");
            // Ensure player is cleaned up from tracking if a major error occurs during the initial sync phase
            ClearPlayerTracking(actorNr);
        }
        finally
        {
            Log.LogInfo($"[LateJoinManager] === L.A.T.E. State Sync Orchestration (Initial Phase) Finished for {nickname} ===");
        }
    }

    // ... (FindEpForResync and SyncPlayerDeathState remain mostly the same)
    private static ExtractionPoint? FindEpForResync(bool isShopScene)
    {
        if (isShopScene)
        {
            if (ReflectionCache.ExtractionPoint_IsShopField == null)
            {
                Log.LogError("[LateJoinManager] FindEpForResync: Reflection field ExtractionPoint_IsShopField is null. Cannot find Shop EP.");
                return null;
            }
            ExtractionPoint[] allEps = Object.FindObjectsOfType<ExtractionPoint>(true);
            foreach (ExtractionPoint ep in allEps)
            {
                if (ep == null) continue;
                try { if (ReflectionCache.ExtractionPoint_IsShopField.GetValue(ep) as bool? ?? false) return ep; }
                catch (Exception ex) { Log.LogWarning($"[LateJoinManager] FindEpForResync: Error reflecting isShop for EP '{ep.name}': {ex}"); }
            }
            Log.LogWarning("[LateJoinManager] FindEpForResync: Could not find active Shop ExtractionPoint for item resync.");
            return null;
        }
        else
        {
            if (RoundDirector.instance == null || ReflectionCache.RoundDirector_ExtractionPointCurrentField == null)
            {
                Log.LogDebug("[LateJoinManager] FindEpForResync: RoundDirector instance or reflection field for current EP is null.");
                return null;
            }
            try
            {
                ExtractionPoint? currentEP = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint;
                if (currentEP == null) Log.LogDebug("[LateJoinManager] FindEpForResync: No active Level ExtractionPoint found for item resync.");
                return currentEP;
            }
            catch (Exception ex) { Log.LogWarning($"[LateJoinManager] FindEpForResync: Error reflecting current EP from RoundDirector: {ex}"); return null; }
        }
    }

    private static void SyncPlayerDeathState(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        if (targetPlayer == null || playerAvatar == null)
        {
            Log.LogWarning("[LateJoinManager][DeathSync] TargetPlayer or PlayerAvatar is null. Skipping death sync.");
            return;
        }
        string nickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        if (!ConfigManager.KillIfPreviouslyDead.Value)
        {
            Log.LogDebug($"[LateJoinManager][DeathSync] KillIfPreviouslyDead is disabled for {nickname}. Skipping.");
            return;
        }
        PlayerStatus status = PlayerStateManager.GetPlayerStatus(targetPlayer);
        if (status == PlayerStatus.Dead)
        {
            PhotonView? pv = PhotonUtilities.GetPhotonView(playerAvatar);
            if (pv == null) { Log.LogError($"[LateJoinManager][DeathSync] Null PhotonView for {nickname}. Cannot send PlayerDeathRPC."); return; }
            bool isDisabled = false, isDeadSet = false;
            try
            {
                if (ReflectionCache.PlayerAvatar_IsDisabledField != null) isDisabled = ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(playerAvatar) as bool? ?? false;
                if (ReflectionCache.PlayerAvatar_DeadSetField != null) isDeadSet = ReflectionCache.PlayerAvatar_DeadSetField.GetValue(playerAvatar) as bool? ?? false;
            }
            catch (Exception ex) { Log.LogError($"[LateJoinManager][DeathSync] Error reflecting isDisabled/deadSet for {nickname}: {ex}"); }
            if (isDisabled || isDeadSet) { Log.LogInfo($"[LateJoinManager][DeathSync] Player {nickname} is already dead/disabled on host. No PlayerDeathRPC needed."); return; }
            try { Log.LogInfo($"[LateJoinManager][DeathSync] Sending PlayerDeathRPC for {nickname} (was previously dead)."); pv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, -1); }
            catch (Exception ex) { Log.LogError($"[LateJoinManager][DeathSync] Error sending PlayerDeathRPC for {nickname}: {ex}"); }
        }
        else Log.LogDebug($"[LateJoinManager][DeathSync] Player {nickname} status is {status}. No death sync needed.");
    }
    #endregion
}