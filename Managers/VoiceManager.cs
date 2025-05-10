// File: L.A.T.E/Managers/VoiceManager.cs
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LATE.Core; // For LatePlugin.Log, CoroutineHelper
using LATE.Utilities; // For ReflectionCache, PhotonUtilities, GameUtilities

namespace LATE.Managers; // File-scoped namespace

/// <summary>
/// Manages voice chat synchronization, particularly for players joining late
/// or when voice chat state needs refreshing.
/// </summary>
internal static class VoiceManager
{
    #region Private Fields
    private static readonly Dictionary<int, bool> _previousVoiceChatFetchedStates = new Dictionary<int, bool>();
    private static bool _syncScheduled = false;
    #endregion

    #region Public Methods
    public static void HandleAvatarUpdate(PlayerAvatar playerAvatar)
    {
        if (playerAvatar == null) return;
        PhotonView? pv = PhotonUtilities.GetPhotonView(playerAvatar);
        if (pv == null) return;

        int viewId = pv.ViewID;
        bool currentVoiceFetched = false;

        try
        {
            if (ReflectionCache.PlayerAvatar_VoiceChatFetchedField != null)
            {
                currentVoiceFetched = (bool)ReflectionCache.PlayerAvatar_VoiceChatFetchedField.GetValue(playerAvatar);
            }
            else
            {
                // Logged by ReflectionCache if critical
                return;
            }
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[VoiceManager] Error reflecting voiceChatFetched for {viewId}: {ex}");
            return;
        }

        _previousVoiceChatFetchedStates.TryGetValue(viewId, out bool previousVoiceFetched);

        if (PhotonUtilities.IsRealMasterClient())
        {
            if (currentVoiceFetched && !previousVoiceFetched)
            {
                string playerName = pv.Owner?.NickName ?? $"ActorNr {pv.OwnerActorNr}";
                LatePlugin.Log.LogInfo($"[VoiceManager] Host detected voiceChatFetched TRUE for {playerName} ({viewId}).");

                if (!_syncScheduled)
                {
                    if (CoroutineHelper.CoroutineRunner != null)
                    {
                        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.PlayerCount > 1 && GameDirector.instance != null && !SemiFunc.RunIsLobbyMenu())
                        {
                            TriggerDelayedSync($"Player {playerName} ready");
                        }
                        // else: Logged by TriggerDelayedSync if conditions not met for scheduling
                    }
                    else
                    {
                        LatePlugin.Log.LogError("[VoiceManager] Cannot schedule voice sync: CoroutineHelper.CoroutineRunner is null!");
                    }
                }
                // else: Logged by TriggerDelayedSync if already scheduled
            }
        }
        _previousVoiceChatFetchedStates[viewId] = currentVoiceFetched;
    }

    public static void TriggerDelayedSync(string reason, float delay = 1.5f)
    {
        if (_syncScheduled)
        {
            LatePlugin.Log.LogInfo($"[VoiceManager] Voice sync already scheduled, ignoring trigger: {reason}");
            return;
        }

        if (CoroutineHelper.CoroutineRunner != null)
        {
            LatePlugin.Log.LogInfo($"[VoiceManager] Scheduling delayed voice sync (Trigger: {reason}, Delay: {delay}s)...");
            CoroutineHelper.CoroutineRunner.StartCoroutine(DelayedVoiceSync(reason, delay));
            _syncScheduled = true;
        }
        else
        {
            LatePlugin.Log.LogError("[VoiceManager] Cannot schedule voice sync: CoroutineHelper.CoroutineRunner is null!");
        }
    }

    public static void HandlePlayerLeft(Player leftPlayer)
    {
        if (leftPlayer == null)
        {
            LatePlugin.Log.LogWarning("[VoiceManager] HandlePlayerLeft called with null player.");
            return;
        }

        LatePlugin.Log.LogInfo($"[VoiceManager] Cleaning up voice state for leaving player: {leftPlayer.NickName} ({leftPlayer.ActorNumber})");
        int viewIdToRemove = -1;
        PlayerAvatar? avatarToRemove = GameUtilities.FindPlayerAvatar(leftPlayer);
        if (avatarToRemove != null)
        {
            PhotonView? pv = PhotonUtilities.GetPhotonView(avatarToRemove);
            if (pv != null) viewIdToRemove = pv.ViewID;
        }

        if (viewIdToRemove != -1)
        {
            if (_previousVoiceChatFetchedStates.Remove(viewIdToRemove))
            {
                LatePlugin.Log.LogInfo($"[VoiceManager] Removed ViewID {viewIdToRemove} from voice state tracking.");
            }
        }
        // else: No warning if avatar/PV not found, player is gone.
    }
    #endregion

    #region Private Methods
    private static IEnumerator DelayedVoiceSync(string triggerReason, float delay = 1.5f)
    {
        yield return new WaitForSeconds(delay);
        LatePlugin.Log.LogInfo($"[VoiceManager] Executing delayed voice sync (Trigger: {triggerReason}, Delay: {delay}s).");
        _syncScheduled = false;

        if (!PhotonUtilities.IsRealMasterClient())
        {
            LatePlugin.Log.LogWarning("[VoiceManager] No longer MasterClient. Aborting voice sync.");
            yield break;
        }
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.PlayerCount <= 1 || GameDirector.instance == null || SemiFunc.RunIsLobbyMenu())
        {
            LatePlugin.Log.LogWarning("[VoiceManager] Skipping voice sync: Conditions not met or game state invalid.");
            yield break;
        }

        try
        {
            List<PlayerAvatar>? playerAvatars = GameDirector.instance?.PlayerList;
            if (playerAvatars == null)
            {
                LatePlugin.Log.LogError("[VoiceManager] PlayerList null for voice sync.");
                yield break;
            }

            LatePlugin.Log.LogInfo($"[VoiceManager] Syncing voice for {playerAvatars.Count} players in list.");
            List<PlayerAvatar> playersToSync = new List<PlayerAvatar>(playerAvatars); // Iterate a copy

            foreach (PlayerAvatar player in playersToSync)
            {
                PhotonView? playerPV = PhotonUtilities.GetPhotonView(player);
                if (player == null || playerPV == null)
                {
                    LatePlugin.Log.LogWarning("[VoiceManager] Null player or PhotonView found during sync. Skipping.");
                    continue;
                }

                bool isVoiceFetched = false;
                PlayerVoiceChat? voiceChat = null;
                try
                {
                    if (ReflectionCache.PlayerAvatar_VoiceChatFetchedField != null)
                    {
                        isVoiceFetched = (bool)ReflectionCache.PlayerAvatar_VoiceChatFetchedField.GetValue(player);
                    }
                    if (ReflectionCache.PlayerAvatar_VoiceChatField != null)
                    {
                        voiceChat = ReflectionCache.PlayerAvatar_VoiceChatField.GetValue(player) as PlayerVoiceChat;
                    }
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogError($"[VoiceManager] Error reflecting voice state for sync on player {playerPV.ViewID}: {ex}");
                    continue;
                }

                if (isVoiceFetched && voiceChat != null)
                {
                    PhotonView? voiceChatPV = voiceChat.GetComponent<PhotonView>(); // VoiceChat itself should have a PV
                    if (voiceChatPV != null)
                    {
                        int voiceChatViewID = voiceChatPV.ViewID;
                        string playerName = playerPV.Owner?.NickName ?? $"ActorNr {playerPV.OwnerActorNr}";
                        // LatePlugin.Log.LogInfo($"[VoiceManager] Syncing {playerName} (AvatarPV: {playerPV.ViewID}, VoiceChatPV: {voiceChatViewID}).");

                        if (PhotonUtilities.GetPhotonView(player) != null) // Re-check avatar PV
                        {
                            playerPV.RPC("UpdateMyPlayerVoiceChat", RpcTarget.AllBuffered, voiceChatViewID);
                        }
                        // else: Player might have despawned mid-sync
                    }
                    // else: PlayerVoiceChat component missing PhotonView
                }
                // else: Voice not fetched or PlayerVoiceChat component null
            }
            LatePlugin.Log.LogInfo("[VoiceManager] Delayed voice sync completed.");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[VoiceManager] Error during voice sync execution: {ex}");
            _syncScheduled = false; // Ensure flag is reset on error
        }
    }
    #endregion
}