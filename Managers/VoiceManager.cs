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

        int viewID = pv.ViewID;
        bool voiceFetchedNow = GetVoiceFetchedFlag(avatar, viewID);

        if (PhotonUtilities.IsRealMasterClient() && voiceFetchedNow &&
            !_previousVoiceChatFetchedStates.GetValueOrDefault(viewID))
        {
            string playerName = pv.Owner?.NickName ?? $"ActorNr {pv.OwnerActorNr}";
            LatePlugin.Log.LogInfo($"{LogPrefix} Host detected voiceChatFetched TRUE for {playerName} ({viewID}).");

            if (!_syncScheduled)
                TryScheduleSync($"Player {playerName} ready");
        }

        _previousVoiceChatFetchedStates[viewID] = voiceFetchedNow;
    }

    public static void TriggerDelayedSync(string reason, float delay = 1.5f) =>
        TryScheduleSync(reason, delay);

    public static void HandlePlayerLeft(Player leftPlayer)
    {
        if (leftPlayer == null)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} HandlePlayerLeft called with null player.");
            return;
        }

        LatePlugin.Log.LogInfo($"{LogPrefix} Cleaning up voice state for leaving player: {leftPlayer.NickName} ({leftPlayer.ActorNumber})");

        PhotonView? pv = PhotonUtilities.GetPhotonView(GameUtilities.FindPlayerAvatar(leftPlayer));
        if (pv != null && _previousVoiceChatFetchedStates.Remove(pv.ViewID))
            LatePlugin.Log.LogInfo($"{LogPrefix} Removed ViewID {pv.ViewID} from voice state tracking.");
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
            List<PlayerAvatar> avatars = new(GameDirector.instance.PlayerList ?? new());
            LatePlugin.Log.LogInfo($"{LogPrefix} Syncing voice for {avatars.Count} players.");

            foreach (var avatar in avatars)
            {
                PhotonView? avatarPV = PhotonUtilities.GetPhotonView(avatar);
                if (avatar == null || avatarPV == null)
                {
                    LatePlugin.Log.LogWarning($"{LogPrefix} Null avatar or PhotonView during sync. Skipping.");
                    continue;
                }

                bool isFetched = false;
                PlayerVoiceChat? voice = null;

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

                if (!isFetched || voice == null) continue;

                PhotonView voicePV = voice.GetComponent<PhotonView>();
                if (voicePV == null) continue;

                int voiceViewID = voicePV.ViewID;
                if (PhotonUtilities.GetPhotonView(avatar) != null)
                    avatarPV.RPC("UpdateMyPlayerVoiceChat", RpcTarget.AllBuffered, voiceViewID);
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