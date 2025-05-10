// File: L.A.T.E/Managers/LateJoinManager.cs
using Photon.Pun;
using Photon.Realtime;
using System;
//using System.Collections; // No longer needed directly here after ResyncExtractionPointItems moved
using System.Collections.Generic;
using UnityEngine; // For PlayerAvatar
using Object = UnityEngine.Object; // For FindObjectsOfType in one remaining method, can be removed if that method moves
using LATE.Core;
using LATE.Config;
using LATE.Utilities;
using LATE.DataModels;
using LATE.Managers; // For ItemSyncManager, LevelSyncManager, etc.

namespace LATE.Managers;

/// <summary>
/// Manages the state and synchronization process for players who join after a
/// level has started. Orchestrates calls to more specialized sync managers.
/// </summary>
internal static class LateJoinManager
{
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
                LatePlugin.Log.LogInfo($"[LateJoinManager] Player {nickname} marked as needing late join DATA sync (awaiting LoadingCompleteRPC).");
            if (_playersJoinedLateThisScene.Add(actorNr))
                LatePlugin.Log.LogInfo($"[LateJoinManager] Player {nickname} marked as JOINED LATE THIS SCENE.");
        }
        else LatePlugin.Log.LogInfo($"[LateJoinManager] Player {nickname} joined during INACTIVE scene. Not marked as late-joiner.");

        if (PhotonUtilities.IsRealMasterClient())
            VoiceManager.TriggerDelayedSync($"Player {nickname} joined room", 0.5f);
    }
    #endregion

    #region Public Accessors& Modifiers for Tracking
    internal static bool IsPlayerNeedingSync(int actorNumber) => _playersNeedingLateJoinSync.Contains(actorNumber);
    internal static bool DidPlayerJoinLateThisScene(int actorNumber) => _playersJoinedLateThisScene.Contains(actorNumber);
    internal static void MarkPlayerSyncTriggeredAndClearNeed(int actorNumber)
    {
        if (_playersNeedingLateJoinSync.Remove(actorNumber))
            LatePlugin.Log.LogDebug($"[LateJoinManager] Sync triggered for ActorNr {actorNumber}. Removed from 'NeedingSync' list.");
    }
    internal static void ClearPlayerTracking(int actorNumber)
    {
        bool removedNeed = _playersNeedingLateJoinSync.Remove(actorNumber);
        bool removedJoinedLate = _playersJoinedLateThisScene.Remove(actorNumber);
        if (removedNeed || removedJoinedLate)
            LatePlugin.Log?.LogDebug($"[LateJoinManager] Cleared all sync tracking for ActorNr {actorNumber}.");
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
            // MODIFIED: Call LevelSyncManager for level-specific syncs
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
            LevelSyncManager.TriggerPropSwitchSetup(targetPlayer); // MODIFIED

            EnemySyncManager.SyncAllEnemyStatesForPlayer(targetPlayer);
            EnemySyncManager.NotifyEnemiesOfNewPlayer(targetPlayer, playerAvatar);

            SyncPlayerDeathState(targetPlayer, playerAvatar); // Remains here for now
            LevelSyncManager.SyncTruckScreenForPlayer(targetPlayer); // MODIFIED
            ItemSyncManager.SyncAllItemStatesForPlayer(targetPlayer);

            if (SemiFunc.RunIsArena())
            {
                LevelSyncManager.SyncArenaStateForPlayer(targetPlayer); // MODIFIED
            }

            // Host-specific EP item resync (calls ItemSyncManager's coroutine)
            if (PhotonUtilities.IsRealMasterClient())
            {
                ExtractionPoint? epToResync = FindEpForResync(isShopScene);
                if (epToResync != null)
                {
                    LatePlugin.Log.LogInfo($"[LateJoinManager] Starting ItemSyncManager.ResyncExtractionPointItems coroutine for {nickname} in EP '{epToResync.name}'.");
                    if (CoroutineHelper.CoroutineRunner != null)
                        CoroutineHelper.CoroutineRunner.StartCoroutine(ItemSyncManager.ResyncExtractionPointItems(targetPlayer, epToResync));
                    else LatePlugin.Log.LogError("[LateJoinManager] Cannot start ResyncExtractionPointItems: CoroutineHelper.CoroutineRunner is null!");
                }
                else LatePlugin.Log.LogInfo($"[LateJoinManager] No suitable EP identified for item resync for {nickname}.");
            }
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

    // Helper method to find EP for resync, to keep SyncAllStateForPlayer cleaner
    private static ExtractionPoint? FindEpForResync(bool isShopScene)
    {
        ExtractionPoint? epToResync = null;
        if (isShopScene)
        {
            ExtractionPoint[] allEps = Object.FindObjectsOfType<ExtractionPoint>();
            foreach (ExtractionPoint ep in allEps)
            {
                if (ep == null || ReflectionCache.ExtractionPoint_IsShopField == null) continue;
                try
                {
                    if ((bool)(ReflectionCache.ExtractionPoint_IsShopField.GetValue(ep) ?? false)) { epToResync = ep; break; }
                }
                catch { /* Logged by ReflectionCache or already handled */ }
            }
            if (epToResync == null) LatePlugin.Log.LogWarning("[LateJoinManager] Could not find Shop EP for item resync.");
        }
        else // Level Scene
        {
            if (RoundDirector.instance != null && ReflectionCache.RoundDirector_ExtractionPointCurrentField != null)
            {
                try { epToResync = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint; } catch { /* Logged */ }
            }
            if (epToResync == null) LatePlugin.Log.LogDebug("[LateJoinManager] No active Level EP found for item resync.");
        }
        return epToResync;
    }

    #endregion

    // SyncPlayerDeathState is the only detailed sync logic remaining directly in LateJoinManager.
    // Other sync methods have been moved to ItemSyncManager or LevelSyncManager.
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
            if (pv == null) { LatePlugin.Log.LogError($"[LateJoinManager][Death Sync] Null PV for {nick}."); return; }
            bool isDisabled = false, isDeadSet = false;
            try
            {
                if (ReflectionCache.PlayerAvatar_IsDisabledField != null) isDisabled = (bool)(ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(playerAvatar) ?? false);
                if (ReflectionCache.PlayerAvatar_DeadSetField != null) isDeadSet = (bool)(ReflectionCache.PlayerAvatar_DeadSetField.GetValue(playerAvatar) ?? false);
            }
            catch (Exception ex) { LatePlugin.Log.LogError($"[LateJoinManager][Death Sync] Error reflecting isDisabled/deadSet for {nick}: {ex}"); }
            if (isDisabled || isDeadSet) { LatePlugin.Log.LogInfo($"[LateJoinManager][Death Sync] Player {nick} already dead/disabled."); return; }
            try { pv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, -1); LatePlugin.Log.LogInfo($"[LateJoinManager][Death Sync] Sent PlayerDeathRPC for {nick}."); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[LateJoinManager][Death Sync] Error sending PlayerDeathRPC for {nick}: {ex}"); }
        }
    }
}