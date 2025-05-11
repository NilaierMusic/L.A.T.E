// File: L.A.T.E/Managers/LateJoinManager.cs
using LATE.Config;
using LATE.Core;
using LATE.DataModels;
using LATE.Utilities;
using Photon.Pun;
using Photon.Realtime;
using Object = UnityEngine.Object; // Alias for UnityEngine.Object

namespace LATE.Managers;

/// <summary>
/// Manages the state and synchronization process for players who join after a
/// level has started. Orchestrates calls to more specialized sync managers.
/// </summary>
internal static class LateJoinManager
{
    private static readonly BepInEx.Logging.ManualLogSource Log = LatePlugin.Log;

    #region Player Tracking State
    private static readonly HashSet<int> _playersNeedingLateJoinSync = new HashSet<int>();
    private static readonly HashSet<int> _playersJoinedLateThisScene = new HashSet<int>();
    #endregion

    #region Scene Management
    /// <summary>
    /// Clears all late-join tracking sets, typically called when a new scene loads.
    /// </summary>
    public static void ResetSceneTracking()
    {
        Log.LogDebug("[LateJoinManager] Clearing late join tracking sets for new scene.");
        _playersNeedingLateJoinSync.Clear();
        _playersJoinedLateThisScene.Clear();
    }
    #endregion

    #region Player Join and Tracking Logic
    /// <summary>
    /// Handles a new player joining the room, marking them for sync if necessary.
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
            if (_playersNeedingLateJoinSync.Add(actorNr))
            {
                Log.LogInfo($"[LateJoinManager] Player {nickname} (ActorNr {actorNr}) marked as needing late join DATA sync (awaiting LoadingCompleteRPC).");
            }
            if (_playersJoinedLateThisScene.Add(actorNr))
            {
                Log.LogInfo($"[LateJoinManager] Player {nickname} (ActorNr {actorNr}) marked as having JOINED LATE THIS SCENE.");
            }
        }
        else
        {
            Log.LogInfo($"[LateJoinManager] Player {nickname} (ActorNr {actorNr}) joined during an INACTIVE scene. Not marked as a late-joiner.");
        }

        if (PhotonUtilities.IsRealMasterClient())
        {
            VoiceManager.TriggerDelayedSync($"Player {nickname} joined room", 0.5f);
        }
    }

    /// <summary>
    /// Checks if a player is currently marked as needing a late join synchronization.
    /// </summary>
    internal static bool IsPlayerNeedingSync(int actorNumber) => _playersNeedingLateJoinSync.Contains(actorNumber);

    /// <summary>
    /// Checks if a player joined late during the current scene.
    /// </summary>
    internal static bool DidPlayerJoinLateThisScene(int actorNumber) => _playersJoinedLateThisScene.Contains(actorNumber);

    /// <summary>
    /// Marks that a player's late join synchronization has been triggered and
    /// removes them from the 'needing sync' list.
    /// </summary>
    internal static void MarkPlayerSyncTriggeredAndClearNeed(int actorNumber)
    {
        if (_playersNeedingLateJoinSync.Remove(actorNumber))
        {
            Log.LogDebug($"[LateJoinManager] Sync triggered for ActorNr {actorNumber}. Removed from 'NeedingSync' list.");
        }
    }

    /// <summary>
    /// Clears all late join tracking for a specific player, typically when they leave.
    /// </summary>
    internal static void ClearPlayerTracking(int actorNumber)
    {
        bool removedNeed = _playersNeedingLateJoinSync.Remove(actorNumber);
        bool removedJoinedLate = _playersJoinedLateThisScene.Remove(actorNumber);

        if (removedNeed || removedJoinedLate)
        {
            Log.LogDebug($"[LateJoinManager] Cleared all late join tracking for ActorNr {actorNumber}.");
        }
    }
    #endregion

    #region Central Synchronization Logic
    /// <summary>
    /// Orchestrates the synchronization of all relevant game states to a late-joining player.
    /// </summary>
    public static void SyncAllStateForPlayer(Player targetPlayer, PlayerAvatar playerAvatar)
    {
        if (targetPlayer == null)
        {
            Log.LogError("[LateJoinManager] SyncAllStateForPlayer called with a null targetPlayer. Aborting.");
            return;
        }
        if (playerAvatar == null)
        {
            Log.LogError($"[LateJoinManager] SyncAllStateForPlayer called with a null playerAvatar for {targetPlayer.NickName}. Aborting death/enemy sync portions.");
            // Decide if other syncs should proceed or if this is fatal. For now, some might proceed.
        }


        int actorNr = targetPlayer.ActorNumber;
        string nickname = targetPlayer.NickName ?? $"ActorNr {actorNr}";
        Log.LogInfo($"[LateJoinManager] === Orchestrating Full State Sync for {nickname} (ActorNr {actorNr}) ===");

        try
        {
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

            if (playerAvatar != null) // Only sync enemy/death state if avatar is valid
            {
                EnemySyncManager.SyncAllEnemyStatesForPlayer(targetPlayer);
                EnemySyncManager.NotifyEnemiesOfNewPlayer(targetPlayer, playerAvatar);
                SyncPlayerDeathState(targetPlayer, playerAvatar);
            }
            else
            {
                Log.LogWarning($"[LateJoinManager] Skipping Enemy and Death sync for {nickname} due to null playerAvatar.");
            }

            LevelSyncManager.SyncTruckScreenForPlayer(targetPlayer);
            ItemSyncManager.SyncAllItemStatesForPlayer(targetPlayer);

            if (SemiFunc.RunIsArena())
            {
                LevelSyncManager.SyncArenaStateForPlayer(targetPlayer);
            }

            if (PhotonUtilities.IsRealMasterClient())
            {
                ExtractionPoint? epToResync = FindEpForResync(isShopScene);
                if (epToResync != null)
                {
                    Log.LogInfo($"[LateJoinManager] Starting ItemSyncManager.ResyncExtractionPointItems coroutine for {nickname} in EP '{epToResync.name}'.");
                    if (CoroutineHelper.CoroutineRunner != null)
                    {
                        CoroutineHelper.CoroutineRunner.StartCoroutine(ItemSyncManager.ResyncExtractionPointItems(targetPlayer, epToResync));
                    }
                    else
                    {
                        Log.LogError("[LateJoinManager] Cannot start ResyncExtractionPointItems: CoroutineHelper.CoroutineRunner is null!");
                    }
                }
                else
                {
                    Log.LogDebug($"[LateJoinManager] No suitable EP identified for item resync for {nickname}.");
                }
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"[LateJoinManager] CRITICAL ERROR during SyncAllStateForPlayer for {nickname}: {ex}");
            ClearPlayerTracking(actorNr); // Ensure tracking is cleared on error
        }
        finally
        {
            Log.LogInfo($"[LateJoinManager] === Full State Sync Orchestration Finished for {nickname} ===");
        }
    }

    /// <summary>
    /// Helper method to find the appropriate ExtractionPoint for item resynchronization.
    /// </summary>
    private static ExtractionPoint? FindEpForResync(bool isShopScene)
    {
        if (isShopScene)
        {
            // Attempt to find the shop's extraction point.
            if (ReflectionCache.ExtractionPoint_IsShopField == null)
            {
                Log.LogError("[LateJoinManager] FindEpForResync: Reflection field ExtractionPoint_IsShopField is null. Cannot find Shop EP.");
                return null;
            }

            ExtractionPoint[] allEps = Object.FindObjectsOfType<ExtractionPoint>(true); // Include inactive
            foreach (ExtractionPoint ep in allEps)
            {
                if (ep == null) continue;
                try
                {
                    if (ReflectionCache.ExtractionPoint_IsShopField.GetValue(ep) as bool? ?? false)
                    {
                        return ep;
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[LateJoinManager] FindEpForResync: Error reflecting isShop for EP '{ep.name}': {ex}");
                }
            }
            Log.LogWarning("[LateJoinManager] FindEpForResync: Could not find active Shop ExtractionPoint for item resync.");
            return null;
        }
        else // Level Scene
        {
            // Attempt to get the current active extraction point from RoundDirector.
            if (RoundDirector.instance == null || ReflectionCache.RoundDirector_ExtractionPointCurrentField == null)
            {
                Log.LogDebug("[LateJoinManager] FindEpForResync: RoundDirector instance or reflection field for current EP is null.");
                return null;
            }

            try
            {
                ExtractionPoint? currentEP = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint;
                if (currentEP == null)
                {
                    Log.LogDebug("[LateJoinManager] FindEpForResync: No active Level ExtractionPoint found via RoundDirector for item resync.");
                }
                return currentEP;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[LateJoinManager] FindEpForResync: Error reflecting current EP from RoundDirector: {ex}");
                return null;
            }
        }
    }
    #endregion

    #region Player State Synchronization (Specific)
    /// <summary>
    /// Synchronizes the death state of a player if they were previously dead in the level.
    /// </summary>
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
            Log.LogDebug($"[LateJoinManager][DeathSync] KillIfPreviouslyDead is disabled by config for {nickname}. Skipping.");
            return;
        }

        PlayerStatus status = PlayerStateManager.GetPlayerStatus(targetPlayer);
        if (status == PlayerStatus.Dead)
        {
            PhotonView? pv = PhotonUtilities.GetPhotonView(playerAvatar);
            if (pv == null)
            {
                Log.LogError($"[LateJoinManager][DeathSync] Null PhotonView for {nickname}. Cannot send PlayerDeathRPC.");
                return;
            }

            bool isDisabled = false;
            bool isDeadSet = false;

            try
            {
                if (ReflectionCache.PlayerAvatar_IsDisabledField != null)
                {
                    isDisabled = ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(playerAvatar) as bool? ?? false;
                }
                if (ReflectionCache.PlayerAvatar_DeadSetField != null)
                {
                    isDeadSet = ReflectionCache.PlayerAvatar_DeadSetField.GetValue(playerAvatar) as bool? ?? false;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[LateJoinManager][DeathSync] Error reflecting isDisabled/deadSet for {nickname}: {ex}");
                // Continue to attempt RPC as a fallback, client-side PlayerDeathRPC should handle its own state.
            }

            if (isDisabled || isDeadSet)
            {
                Log.LogInfo($"[LateJoinManager][DeathSync] Player {nickname} is already dead/disabled on host. No PlayerDeathRPC needed.");
                return;
            }

            try
            {
                Log.LogInfo($"[LateJoinManager][DeathSync] Sending PlayerDeathRPC for {nickname} (was previously dead).");
                pv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, -1); // -1 for generic death
            }
            catch (Exception ex)
            {
                Log.LogError($"[LateJoinManager][DeathSync] Error sending PlayerDeathRPC for {nickname}: {ex}");
            }
        }
        else
        {
            Log.LogDebug($"[LateJoinManager][DeathSync] Player {nickname} status is {status}. No death sync needed.");
        }
    }
    #endregion
}