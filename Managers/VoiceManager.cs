using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LATE
{
    /// <summary>
    /// Manages voice chat synchronization, particularly for players joining late
    /// or when voice chat state needs refreshing.
    /// </summary>
    internal static class VoiceManager
    {
        #region Private Fields

        /// <summary>
        /// Tracks the last known 'voiceChatFetched' state per player (by PhotonView ID).
        /// Used to detect when a player's voice chat becomes ready.
        /// </summary>
        private static readonly Dictionary<int, bool> _previousVoiceChatFetchedStates = new Dictionary<int, bool>();

        /// <summary>
        /// Flag indicating whether a voice sync coroutine is already running or scheduled.
        /// Prevents multiple syncs from being scheduled concurrently.
        /// </summary>
        private static bool _syncScheduled = false;

        #endregion

        #region Public Methods

        /// <summary>
        /// Monitors voice chat readiness (e.g., via a patch on PlayerAvatar.Update).
        /// When a player's voice chat becomes ready, the host schedules a synchronization.
        /// </summary>
        /// <param name="playerAvatar">The PlayerAvatar instance being updated.</param>
        public static void HandleAvatarUpdate(PlayerAvatar playerAvatar)
        {
            if (playerAvatar == null)
            {
                return;
            }

            // Retrieve the PhotonView using a utility method.
            PhotonView? pv = Utilities.GetPhotonView(playerAvatar);
            if (pv == null)
            {
                return;
            }

            int viewId = pv.ViewID;
            bool currentVoiceFetched = false;

            // Safely use reflection to check the 'voiceChatFetched' state.
            try
            {
                if (Utilities.paVoiceChatFetchedField != null)
                {
                    currentVoiceFetched = (bool)Utilities.paVoiceChatFetchedField.GetValue(playerAvatar);
                }
                else
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError($"Error reflecting voiceChatFetched for {viewId}: {ex}");
                return;
            }

            // Get the previously recorded state (defaults to false if not present).
            _previousVoiceChatFetchedStates.TryGetValue(viewId, out bool previousVoiceFetched);

            // Only the host (MasterClient) should schedule voice synchronization.
            if (Utilities.IsRealMasterClient())
            {
                // If the state has just turned true, schedule a sync.
                if (currentVoiceFetched && !previousVoiceFetched)
                {
                    string playerName = pv.Owner?.NickName ?? $"ActorNr {pv.OwnerActorNr}";
                    LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Host detected voiceChatFetched TRUE for {playerName} ({viewId}).");

                    if (!_syncScheduled)
                    {
                        if (LATE.Core.LatePlugin.CoroutineRunner != null)
                        {
                            // Only schedule if in a valid game state.
                            if (PhotonNetwork.InRoom
                                && PhotonNetwork.CurrentRoom.PlayerCount > 1
                                && GameDirector.instance != null
                                && !SemiFunc.RunIsLobbyMenu())
                            {
                                TriggerDelayedSync($"Player {playerName} ready");
                            }
                            else
                            {
                                LATE.Core.LatePlugin.Log.LogInfo(
                                    $"[VoiceManager] Skipping voice sync schedule: Conditions not met " +
                                    $"(InRoom={PhotonNetwork.InRoom}, Count={PhotonNetwork.CurrentRoom?.PlayerCount ?? 0}, " +
                                    $"GDState OK={(GameDirector.instance != null && !SemiFunc.RunIsLobbyMenu())}).");
                            }
                        }
                        else
                        {
                            LATE.Core.LatePlugin.Log.LogError("[VoiceManager] Cannot schedule voice sync: Coroutine runner is null!");
                        }
                    }
                    else
                    {
                        LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Voice sync already scheduled, ignoring trigger from {playerName}.");
                    }
                }
            }

            // Always update the state dictionary with the latest fetched state.
            _previousVoiceChatFetchedStates[viewId] = currentVoiceFetched;
        }

        /// <summary>
        /// Schedules a delayed voice synchronization.
        /// Can be called externally (e.g., when a player joins late).
        /// </summary>
        /// <param name="reason">A description of why the sync is being triggered (for logging).</param>
        /// <param name="delay">The delay in seconds before starting the sync.</param>
        public static void TriggerDelayedSync(string reason, float delay = 1.5f)
        {
            if (_syncScheduled)
            {
                LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Voice sync already scheduled, ignoring trigger: {reason}");
                return;
            }

            // Ensure a CoroutineRunner is available by accessing the property.
            if (LATE.Core.LatePlugin.CoroutineRunner != null)
            {
                LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Scheduling delayed voice sync (Trigger: {reason}, Delay: {delay}s)...");
                LATE.Core.LatePlugin.CoroutineRunner.StartCoroutine(DelayedVoiceSync(reason, delay));
                _syncScheduled = true;
            }
            else
            {
                LATE.Core.LatePlugin.Log.LogError("[VoiceManager] Cannot schedule voice sync: Coroutine runner is null and could not be found!");
            }
        }

        /// <summary>
        /// Cleans up the voice state tracking when a player leaves the room.
        /// </summary>
        /// <param name="leftPlayer">The Photon Player who left.</param>
        public static void HandlePlayerLeft(Player leftPlayer)
        {
            if (leftPlayer == null)
            {
                LATE.Core.LatePlugin.Log.LogWarning("[VoiceManager] HandlePlayerLeft called with null player.");
                return;
            }

            LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Cleaning up voice state for leaving player: {leftPlayer.NickName} ({leftPlayer.ActorNumber})");

            // Attempt to locate the PlayerAvatar for the leaving player to get its ViewID.
            int viewIdToRemove = -1;
            PlayerAvatar? avatarToRemove = Utilities.FindPlayerAvatar(leftPlayer);
            if (avatarToRemove != null)
            {
                PhotonView? pv = Utilities.GetPhotonView(avatarToRemove);
                if (pv != null)
                {
                    viewIdToRemove = pv.ViewID;
                }
            }

            if (viewIdToRemove != -1)
            {
                // Remove the player's entry from the state tracking.
                if (_previousVoiceChatFetchedStates.Remove(viewIdToRemove))
                {
                    LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Removed ViewID {viewIdToRemove} from voice state tracking.");
                }
            }
            else
            {
                LATE.Core.LatePlugin.Log.LogWarning($"[VoiceManager] Could not find ViewID for leaving player {leftPlayer.NickName} to cleanup voice state.");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Coroutine that performs the actual voice synchronization after a delay.
        /// Iterates through players and calls the vanilla voice chat update RPC.
        /// </summary>
        /// <param name="triggerReason">The reason this sync was initiated (for logging).</param>
        /// <param name="delay">The delay before execution.</param>
        private static IEnumerator DelayedVoiceSync(string triggerReason, float delay = 1.5f)
        {
            yield return new WaitForSeconds(delay);

            LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Executing delayed voice sync (Trigger: {triggerReason}, Delay: {delay}s).");

            // Reset the flag to allow future syncs.
            _syncScheduled = false;

            // Pre-sync checks.
            if (!Utilities.IsRealMasterClient())
            {
                LATE.Core.LatePlugin.Log.LogWarning("[VoiceManager] No longer MasterClient. Aborting voice sync.");
                yield break;
            }
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom.PlayerCount <= 1)
            {
                LATE.Core.LatePlugin.Log.LogWarning("[VoiceManager] Skipping voice sync: Not in suitable room.");
                yield break;
            }
            if (GameDirector.instance == null || SemiFunc.RunIsLobbyMenu())
            {
                LATE.Core.LatePlugin.Log.LogWarning("[VoiceManager] Game state invalid for voice sync.");
                yield break;
            }

            try
            {
                List<PlayerAvatar>? playerAvatars = GameDirector.instance?.PlayerList;
                if (playerAvatars == null)
                {
                    LATE.Core.LatePlugin.Log.LogError("[VoiceManager] PlayerList null for voice sync.");
                    yield break;
                }

                LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Syncing voice for {playerAvatars.Count} players.");

                // Create a copy of the player list to avoid iteration issues.
                List<PlayerAvatar> playersToSync = new List<PlayerAvatar>(playerAvatars);

                foreach (PlayerAvatar player in playersToSync)
                {
                    PhotonView? playerPV = Utilities.GetPhotonView(player);
                    if (player == null || playerPV == null)
                    {
                        LATE.Core.LatePlugin.Log.LogWarning("[VoiceManager] Null player or PhotonView found during sync. Skipping.");
                        continue;
                    }

                    // Retrieve voice state and related component using reflection.
                    bool isVoiceFetched = false;
                    PlayerVoiceChat? voiceChat = null;
                    try
                    {
                        if (Utilities.paVoiceChatFetchedField != null)
                        {
                            isVoiceFetched = (bool)Utilities.paVoiceChatFetchedField.GetValue(player);
                        }
                        if (Utilities.paVoiceChatField != null)
                        {
                            voiceChat = Utilities.paVoiceChatField.GetValue(player) as PlayerVoiceChat;
                        }
                    }
                    catch (Exception ex)
                    {
                        LATE.Core.LatePlugin.Log.LogError($"Error reflecting voice state for sync on player {playerPV.ViewID}: {ex}");
                        continue;
                    }

                    // Perform sync only if the voice state was fetched and the component exists.
                    if (isVoiceFetched && voiceChat != null)
                    {
                        PhotonView? voiceChatPV = voiceChat.GetComponent<PhotonView>();
                        if (voiceChatPV != null)
                        {
                            int voiceChatViewID = voiceChatPV.ViewID;
                            string playerName = playerPV.Owner?.NickName ?? $"ActorNr {playerPV.OwnerActorNr}";
                            LATE.Core.LatePlugin.Log.LogInfo($"[VoiceManager] Syncing {playerName} (VoiceViewID: {voiceChatViewID}). RPC via PV {playerPV.ViewID}.");

                            // Double-check that the player's PhotonView still exists before sending RPC.
                            if (Utilities.GetPhotonView(player) != null)
                            {
                                // Trigger the built-in buffered RPC to update the voice chat state.
                                playerPV.RPC("UpdateMyPlayerVoiceChat", RpcTarget.AllBuffered, voiceChatViewID);
                            }
                            else
                            {
                                LATE.Core.LatePlugin.Log.LogWarning($"[VoiceManager] PV for {playerName} became null just before sending RPC. Skipping.");
                            }
                        }
                        else
                        {
                            LATE.Core.LatePlugin.Log.LogWarning($"[VoiceManager] Skipping {playerPV.Owner?.NickName ?? "?"} - PlayerVoiceChat component missing PhotonView.");
                        }
                    }
                    else
                    {
                        LATE.Core.LatePlugin.Log.LogWarning($"[VoiceManager] Skipping {playerPV.Owner?.NickName ?? "?"} - voice not fetched or PlayerVoiceChat component null.");
                    }
                }
                LATE.Core.LatePlugin.Log.LogInfo("[VoiceManager] Delayed voice sync completed.");
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError($"[VoiceManager] Error during voice sync execution: {ex}");
                // Reset the flag even if an error occurs.
                _syncScheduled = false;
            }
        }

        #endregion
    }
}