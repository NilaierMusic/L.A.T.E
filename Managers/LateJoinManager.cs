// File: L.A.T.E/Managers/LateJoinManager.cs
using LATE.Config;
using LATE.Core;
using LATE.DataModels;
using LATE.Utilities;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Linq;
using UnityEngine; // Added for CoroutineHelper.CoroutineRunner.StartCoroutine
using Object = UnityEngine.Object; // Explicit alias

namespace LATE.Managers;

internal static class LateJoinManager
{
    private static readonly BepInEx.Logging.ManualLogSource Log = LatePlugin.Log;

    internal enum LateJoinTaskType
    {
        Voice,
        ExtractionPointItems
    }

    #region Player Tracking State
    private static readonly HashSet<int> _playersNeedingInitialSync = new HashSet<int>();
    private static readonly HashSet<int> _activeLateJoinersThisScene = new HashSet<int>();
    private static readonly Dictionary<int, HashSet<LateJoinTaskType>> _pendingAsyncTasksForLateJoiners = new Dictionary<int, HashSet<LateJoinTaskType>>();
    #endregion

    #region Scene Management
    public static void ResetSceneTracking()
    {
        Log.LogDebug("[LateJoinManager] Clearing all late-join tracking sets for new scene instance.");
        _playersNeedingInitialSync.Clear();
        _activeLateJoinersThisScene.Clear();
        _pendingAsyncTasksForLateJoiners.Clear();
    }
    #endregion

    #region Player Join and Tracking Logic
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

    internal static bool IsPlayerNeedingInitialSync(int actorNumber) => _playersNeedingInitialSync.Contains(actorNumber);

    internal static void MarkInitialSyncTriggered(int actorNumber)
    {
        if (_playersNeedingInitialSync.Remove(actorNumber))
        {
            Log.LogDebug($"[LateJoinManager] Initial L.A.T.E. sync triggered for ActorNr {actorNumber}. Removed from 'NeedingInitialSync' list.");
        }
    }

    internal static bool IsPlayerAnActiveLateJoiner(int actorNumber) => _activeLateJoinersThisScene.Contains(actorNumber);

    internal static bool IsLateJoinerPendingAsyncTask(int actorNumber, LateJoinTaskType taskType)
    {
        return _activeLateJoinersThisScene.Contains(actorNumber) &&
               _pendingAsyncTasksForLateJoiners.TryGetValue(actorNumber, out var pendingTasks) &&
               pendingTasks.Contains(taskType);
    }

    public static void ReportLateJoinAsyncTaskCompleted(int actorNumber, LateJoinTaskType taskType)
    {
        if (!PhotonUtilities.IsRealMasterClient()) return;

        if (!_activeLateJoinersThisScene.Contains(actorNumber))
        {
            Log.LogDebug($"[LateJoinManager] ReportLateJoinAsyncTaskCompleted for ActorNr {actorNumber}, Task {taskType}, but player is not in _activeLateJoinersThisScene. Ignoring.");
            _pendingAsyncTasksForLateJoiners.Remove(actorNumber);
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
            Log.LogWarning($"[LateJoinManager] ReportLateJoinAsyncTaskCompleted for ActorNr {actorNumber}, Task {taskType}, but no pending tasks entry found.");
            if (_activeLateJoinersThisScene.Remove(actorNumber))
            {
                Log.LogWarning($"[LateJoinManager] ActorNr {actorNumber} removed from _activeLateJoinersThisScene due to inconsistent pending task state.");
            }
        }
    }

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

    private static void SyncExistingDeadPlayersToLateJoiner(Player lateJoiningPlayer)
    {
        if (!PhotonUtilities.IsRealMasterClient()) return;

        string lateJoinerName = lateJoiningPlayer.NickName ?? $"ActorNr {lateJoiningPlayer.ActorNumber}";
        Log.LogInfo($"[LateJoinManager][SyncDeadPlayers] Starting sync of existing dead players TO {lateJoinerName}.");
        int syncedCount = 0;

        foreach (var existingPlayerEntry in PhotonNetwork.CurrentRoom.Players)
        {
            Player existingPlayer = existingPlayerEntry.Value;
            if (existingPlayer == null || existingPlayer.ActorNumber == lateJoiningPlayer.ActorNumber)
            {
                continue;
            }

            if (PlayerStateManager.GetPlayerLifeStatus(existingPlayer) == PlayerLifeStatus.Dead)
            {
                if (PlayerStateManager.TryGetPlayerDeathEnemyIndex(existingPlayer, out int enemyIdxOfDeadPlayer))
                {
                    PlayerAvatar? deadPlayerAvatar = GameUtilities.FindPlayerAvatar(existingPlayer);
                    if (deadPlayerAvatar != null && deadPlayerAvatar.photonView != null)
                    {
                        Log.LogInfo($"[LateJoinManager][SyncDeadPlayers] Player {existingPlayer.NickName} is dead. Sending PlayerDeathRPC to {lateJoinerName} for this player (Avatar ViewID: {deadPlayerAvatar.photonView.ViewID}) with enemyIndex {enemyIdxOfDeadPlayer}.");
                        try
                        {
                            deadPlayerAvatar.photonView.RPC("PlayerDeathRPC", lateJoiningPlayer, enemyIdxOfDeadPlayer);
                            syncedCount++;
                        }
                        catch (Exception ex)
                        {
                            Log.LogError($"[LateJoinManager][SyncDeadPlayers] Error sending PlayerDeathRPC for {deadPlayerAvatar.name}'s death to {lateJoinerName}: {ex}");
                        }
                    }
                    else
                    {
                        Log.LogWarning($"[LateJoinManager][SyncDeadPlayers] Could not find PlayerAvatar for dead player {existingPlayer.NickName} to sync its state to {lateJoinerName}.");
                    }
                }
                else
                {
                    Log.LogWarning($"[LateJoinManager][SyncDeadPlayers] Player {existingPlayer.NickName} is dead, but couldn't retrieve their death enemyIndex.");
                }
            }
        }
        Log.LogInfo($"[LateJoinManager][SyncDeadPlayers] Finished syncing death states of {syncedCount} other players to {lateJoinerName}.");
    }

    public static void SyncAllStateForPlayer(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        if (targetPlayer == null)
        {
            Log.LogError("[LateJoinManager] SyncAllStateForPlayer called with a null targetPlayer. Aborting.");
            return;
        }

        int actorNr = targetPlayer.ActorNumber;
        string nickname = targetPlayer.NickName ?? $"ActorNr {actorNr}";

        if (!IsPlayerAnActiveLateJoiner(actorNr))
        {
            Log.LogWarning($"[LateJoinManager] SyncAllStateForPlayer called for {nickname}, but they are NOT in _activeLateJoinersThisScene. Aborting L.A.T.E. sync.");
            _pendingAsyncTasksForLateJoiners.Remove(actorNr);
            return;
        }

        if (playerAvatar == null)
        {
            Log.LogError($"[LateJoinManager] SyncAllStateForPlayer called with a null playerAvatar for {nickname}. Most syncs will be skipped. This is problematic for full sync.");
            // Decide if other syncs should proceed or if this is fatal.
            // For now, we'll proceed but this player might not become "fully L.A.T.E. synced" if critical async tasks depend on the avatar.
        }

        Log.LogInfo($"[LateJoinManager] === Orchestrating Full L.A.T.E. State Sync for Active Late Joiner: {nickname} (ActorNr {actorNr}) ===");

        try
        {
            // --- Perform synchronous L.A.T.E. syncs (excluding self-death for now) ---
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
                // Sync OTHER dead players TO this lateJoiner
                SyncExistingDeadPlayersToLateJoiner(targetPlayer);
            }
            else
            {
                Log.LogWarning($"[LateJoinManager] Skipping Enemy and Existing-Dead-Player sync for {nickname} due to null playerAvatar during SyncAllStateForPlayer.");
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

            ExtractionPoint? epToResync = null;
            if (PhotonUtilities.IsRealMasterClient())
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
            if (epToResync != null && CoroutineHelper.CoroutineRunner != null)
            {
                Log.LogInfo($"[LateJoinManager] Starting ItemSyncManager.ResyncExtractionPointItems coroutine for {nickname} in EP '{epToResync.name}'.");
                CoroutineHelper.CoroutineRunner.StartCoroutine(ItemSyncManager.ResyncExtractionPointItems(targetPlayer, epToResync));
            }
            else if (epToResync != null) // CoroutineRunner was null
            {
                Log.LogError("[LateJoinManager] Cannot start ResyncExtractionPointItems: CoroutineHelper.CoroutineRunner is null! EP Resync for this player will not complete.");
                ReportLateJoinAsyncTaskCompleted(actorNr, LateJoinManager.LateJoinTaskType.ExtractionPointItems); // Mark as failed/skipped
            }

            // --- Final step: Handle self-death based on KillIfPreviouslyDead config ---
            if (playerAvatar != null) // Need avatar for this
            {
                PlayerLifeStatus currentStatus = PlayerStateManager.GetPlayerLifeStatus(targetPlayer);
                // ADDED/ENSURED DIAGNOSTIC LOGGING HERE:
                Log.LogInfo($"[LateJoinManager] FINAL STEP CHECK for {nickname}: PlayerStateManager status is {currentStatus}. KillIfPreviouslyDead config: {ConfigManager.KillIfPreviouslyDead.Value}. TargetPlayer UserId: '{targetPlayer.UserId}'");

                if (currentStatus == PlayerLifeStatus.Dead)
                {
                    if (ConfigManager.KillIfPreviouslyDead.Value)
                    {
                        PhotonView? targetPv = PhotonUtilities.GetPhotonView(playerAvatar);
                        if (targetPv != null)
                        {
                            if (PlayerStateManager.TryGetPlayerDeathEnemyIndex(targetPlayer, out int selfDeathEnemyIndex))
                            {
                                Log.LogInfo($"[LateJoinManager] FINAL STEP: Killing late-joiner {nickname} as they were previously dead (KillIfPreviouslyDead=true). EnemyIndex: {selfDeathEnemyIndex}. Sending PlayerDeathRPC via AllBuffered.");
                                targetPv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, selfDeathEnemyIndex);
                            }
                            else
                            {
                                Log.LogWarning($"[LateJoinManager] FINAL STEP: Late joiner {nickname} was previously dead (KillIfPreviouslyDead=true), but could not retrieve enemyIndex for their death. Sending PlayerDeathRPC with -1 via AllBuffered.");
                                targetPv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, -1); // Fallback
                            }
                        }
                        else
                        {
                            Log.LogWarning($"[LateJoinManager] FINAL STEP: Could not get PhotonView for late joiner {nickname} to sync self-death (KillIfPreviouslyDead=true).");
                        }
                    }
                    else // KillIfPreviouslyDead is false, but they were dead. Forgive them.
                    {
                        Log.LogInfo($"[LateJoinManager] FINAL STEP: Late-joiner {nickname} was previously dead, but KillIfPreviouslyDead is false. Marking as ALIVE in PlayerStateManager (forgiving previous death for this session).");
                        PlayerStateManager.MarkPlayerAlive(targetPlayer);
                    }
                }
                // If currentStatus is Alive or Unknown, they join alive by default (unless other game logic kills them later).
            }
            else
            {
                Log.LogWarning($"[LateJoinManager] FINAL STEP: Skipping self-death check for {nickname} due to null playerAvatar.");
            }


            // If there are no async tasks registered, the player might be considered fully synced immediately.
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
            ClearPlayerTracking(actorNr);
        }
        finally
        {
            Log.LogInfo($"[LateJoinManager] === L.A.T.E. State Sync Orchestration (Initial Phase) Finished for {nickname} ===");
        }
    }

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
    #endregion
}