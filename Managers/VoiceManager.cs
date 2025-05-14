// File: L.A.T.E/Managers/VoiceManager.cs
using System;
using System.Collections;
using System.Collections.Generic;

using Photon.Pun;
using Photon.Realtime;

using UnityEngine;

using LATE.Core;        // LatePlugin.Log, CoroutineHelper
using LATE.Utilities;   // ReflectionCache, PhotonUtilities, GameUtilities

namespace LATE.Managers;

/// <summary>
/// Manages voice-chat synchronisation, especially for late-joiners or whenever a
/// refresh is required.
/// </summary>
internal static class VoiceManager
{
    private const string LogPrefix = "[VoiceManager]";

    // Key: AvatarPV ViewID  →  value: last known voiceChatFetched state
    private static readonly Dictionary<int, bool> _previousVoiceChatFetchedStates = new();

    private static bool _syncScheduled;

    #region Public API -------------------------------------------------------------------------

    public static void HandleAvatarUpdate(PlayerAvatar avatar)
    {
        if (avatar == null) return;
        PhotonView? pv = PhotonUtilities.GetPhotonView(avatar);
        if (pv == null) return;
        Player? owner = pv.Owner;
        if (owner == null) return;

        int avatarViewID = pv.ViewID;
        int actorNumber = owner.ActorNumber;
        bool voiceFetchedNow = GetVoiceFetchedFlag(avatar, avatarViewID);

        // Schedule a general sync if a late joiner (pending voice task) becomes ready.
        // The coroutine will iterate and find all such players.
        if (PhotonUtilities.IsRealMasterClient() &&
            LateJoinManager.IsLateJoinerPendingAsyncTask(actorNumber, LateJoinManager.LateJoinTaskType.Voice) &&
            voiceFetchedNow &&
            !_previousVoiceChatFetchedStates.GetValueOrDefault(avatarViewID))
        {
            string playerName = owner.NickName ?? $"ActorNr {actorNumber}";
            LatePlugin.Log.LogInfo($"{LogPrefix} Host detected voiceChatFetched TRUE for LATE JOINER {playerName} (Avatar ViewID: {avatarViewID}) who needs voice sync. Scheduling general voice sync.");
            TryScheduleSync($"Late joiner {playerName} voice component ready");
        }
        _previousVoiceChatFetchedStates[avatarViewID] = voiceFetchedNow;
    }

    // TriggerDelayedSync is kept for potential other uses, but primary scheduling for late joiners
    // will now happen via HandleAvatarUpdate detecting readiness of a player pending the voice task.
    public static void TriggerDelayedSync(string reason, float delay = 1.5f) => TryScheduleSync(reason, delay);


    public static void HandlePlayerLeft(Player leftPlayer)
    {
        if (leftPlayer == null) return;
        // LateJoinManager.ClearPlayerTracking handles removing them from L.A.T.E. pending tasks.
        // We just need to clean up our avatar-instance specific state.
        PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(leftPlayer);
        PhotonView? pv = PhotonUtilities.GetPhotonView(avatar);
        if (pv != null && _previousVoiceChatFetchedStates.Remove(pv.ViewID))
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Removed Avatar ViewID {pv.ViewID} from _previousVoiceChatFetchedStates for leaving player {leftPlayer.NickName}.");
        }
    }

    public static void ResetAllPerSceneStates() // Renamed for clarity
    {
        LatePlugin.Log.LogDebug($"{LogPrefix} Clearing _previousVoiceChatFetchedStates (per-avatar-instance tracking).");
        _previousVoiceChatFetchedStates.Clear();
        // _syncScheduled logic will handle itself or be reset by TryScheduleSync.
    }

    #endregion


    #region Private helpers --------------------------------------------------------------------

    private static bool GetVoiceFetchedFlag(PlayerAvatar avatar, int viewID)
    {
        try
        {
            if (ReflectionCache.PlayerAvatar_VoiceChatFetchedField is not null)
                return (bool)ReflectionCache.PlayerAvatar_VoiceChatFetchedField.GetValue(avatar);
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} Error reflecting voiceChatFetched for {viewID}: {ex}");
        }
        return false;
    }

    private static void TryScheduleSync(string reason, float delay = 1.5f)
    {
        if (_syncScheduled)
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Voice sync already scheduled, ignoring trigger: {reason}");
            return;
        }

        if (CoroutineHelper.CoroutineRunner == null)
        {
            LatePlugin.Log.LogError($"{LogPrefix} Cannot schedule voice sync: CoroutineHelper.CoroutineRunner is null!");
            return;
        }

        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.PlayerCount <= 1 ||
            GameDirector.instance == null || SemiFunc.RunIsLobbyMenu())
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Skipping voice-sync scheduling – game state not suitable.");
            return;
        }

        LatePlugin.Log.LogInfo($"{LogPrefix} Scheduling delayed voice sync (Trigger: {reason}, Delay: {delay}s)...");
        CoroutineHelper.CoroutineRunner.StartCoroutine(DelayedVoiceSync(reason, delay));
        _syncScheduled = true;
    }

    private static IEnumerator DelayedVoiceSync(string triggerReason, float delay)
    {
        yield return new WaitForSeconds(delay);
        LatePlugin.Log.LogInfo($"{LogPrefix} Executing delayed voice sync (Trigger: {triggerReason}, Delay: {delay}s).");
        _syncScheduled = false;

        // Validate host/game state once more
        if (!PhotonUtilities.IsRealMasterClient())
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} No longer MasterClient. Aborting voice sync.");
            yield break;
        }
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.PlayerCount <= 1 ||
            GameDirector.instance == null || SemiFunc.RunIsLobbyMenu())
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} Skipping voice sync: Conditions not met or game state invalid.");
            yield break;
        }

        try
        {
            int successfullySyncedCount = 0;
            // Iterate through all players in the room to find those pending the L.A.T.E. voice task
            if (PhotonNetwork.CurrentRoom == null) yield break;

            // Create a list of players to iterate to avoid issues if collection changes
            var playersInRoom = PhotonNetwork.CurrentRoom.Players.Values.ToList();

            foreach (Player player in playersInRoom)
            {
                if (player == null) continue;
                int actorNumber = player.ActorNumber;

                // Check if this player is an active late joiner AND specifically pending the voice task
                if (!LateJoinManager.IsLateJoinerPendingAsyncTask(actorNumber, LateJoinManager.LateJoinTaskType.Voice))
                {
                    continue; // Not a late joiner for L.A.T.E. sync or voice task already done/not assigned
                }

                PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(player);
                PhotonView? avatarPV = PhotonUtilities.GetPhotonView(avatar);

                if (avatar == null || avatarPV == null)
                {
                    LatePlugin.Log.LogWarning($"{LogPrefix} Null avatar or PV for player {player.NickName} (ActorNr {actorNumber}) pending voice sync. Will retry if another sync is triggered.");
                    continue;
                }

                bool isFetched = GetVoiceFetchedFlag(avatar, avatarPV.ViewID);
                PlayerVoiceChat? voice = null; PhotonView? voicePV = null;

                try
                {
                    if (ReflectionCache.PlayerAvatar_VoiceChatFetchedField is not null)
                        isFetched = (bool)ReflectionCache.PlayerAvatar_VoiceChatFetchedField.GetValue(avatar);

                    if (ReflectionCache.PlayerAvatar_VoiceChatField is not null)
                        voice = ReflectionCache.PlayerAvatar_VoiceChatField.GetValue(avatar) as PlayerVoiceChat;
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogError($"{LogPrefix} Error reflecting voice state for avatar {avatarPV.ViewID}: {ex}");
                    continue;
                }

                if (!isFetched || voice == null || voicePV == null)
                {
                    LatePlugin.Log.LogDebug($"{LogPrefix} Late joiner {player.NickName} (ActorNr {actorNumber}) pending voice sync but not yet ready (fetched: {isFetched}, voiceComp: {voice != null}, voicePV: {voicePV != null}).");
                    continue;
                }

                int voiceViewID = voicePV.ViewID;
                avatarPV.RPC("UpdateMyPlayerVoiceChat", RpcTarget.AllBuffered, voiceViewID);

                // Report completion to LateJoinManager
                LateJoinManager.ReportLateJoinAsyncTaskCompleted(actorNumber, LateJoinManager.LateJoinTaskType.Voice);
                // Log for voice sync itself is now handled by ReportLateJoinAsyncTaskCompleted or its callers.
                successfullySyncedCount++;
            }

            LatePlugin.Log.LogInfo($"{LogPrefix} Delayed voice sync completed.");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} Error during voice sync execution: {ex}");
            _syncScheduled = false; // make sure flag resets on failure
        }
    }

    #endregion
}