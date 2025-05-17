// File: L.A.T.E/Managers/VoiceManager.cs
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using LATE.Core;
using LATE.Utilities;
using LATE.DataModels; // For PlayerStatus

namespace LATE.Managers
{
    internal static class VoiceManager
    {
        private const string LogPrefix = "[VoiceManager]";
        private static Coroutine? _masterVoiceSyncCoroutine = null;
        private static readonly Dictionary<int, int> _hostSentValidRpcForAvatarThisScene = new Dictionary<int, int>();

        private static bool ShouldAttemptVoiceSync(string contextLog = "")
        {
            if (!PhotonUtilities.IsRealMasterClient())
            {
                if (!string.IsNullOrEmpty(contextLog)) LatePlugin.Log.LogDebug($"{LogPrefix} ({contextLog}): Not MasterClient. Skipping voice operation.");
                return false;
            }
            // if (PhotonNetwork.CurrentRoom == null || PhotonNetwork.CurrentRoom.PlayerCount <= 1)
            // {
            //    if (!string.IsNullOrEmpty(contextLog)) LatePlugin.Log.LogDebug($"{LogPrefix} ({contextLog}): Alone or not in a room. Skipping voice operation.");
            //    return false;
            // }
            if (SemiFunc.RunIsLobbyMenu())
            {
                if (!string.IsNullOrEmpty(contextLog)) LatePlugin.Log.LogDebug($"{LogPrefix} ({contextLog}): In LobbyMenu. Skipping voice operation.");
                return false;
            }
            bool inRelevantScene = GameUtilities.IsModLogicActive() || SemiFunc.RunIsLobby() || SemiFunc.RunIsShop();
            if (!inRelevantScene)
            {
                if (!string.IsNullOrEmpty(contextLog)) LatePlugin.Log.LogDebug($"{LogPrefix} ({contextLog}): Not in a L.A.T.E.-relevant scene (Level, Truck, Shop, Arena). Skipping voice operation.");
                return false;
            }
            return true;
        }

        public static void Host_ScheduleVoiceSyncForLateJoiner(Player lateJoiner, PlayerAvatar lateJoinerAvatar, float delay)
        {
            if (!ShouldAttemptVoiceSync($"ScheduleVoiceSyncForLateJoiner for {lateJoiner?.NickName ?? "Unknown"}")) return;
            if (lateJoiner == null || lateJoinerAvatar == null)
            {
                LatePlugin.Log.LogWarning($"{LogPrefix} Host_ScheduleVoiceSyncForLateJoiner: Null lateJoiner or lateJoinerAvatar.");
                return;
            }

            if (CoroutineHelper.CoroutineRunner != null)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Scheduling specific voice sync for late joiner {lateJoiner.NickName} with delay {delay}s.");
                CoroutineHelper.CoroutineRunner.StartCoroutine(DelayedVoiceSyncForLateJoinerCoroutine(lateJoiner, lateJoinerAvatar, delay));
            }
            else
            {
                LatePlugin.Log.LogError($"{LogPrefix} CoroutineRunner is null. Cannot schedule voice sync for late joiner {lateJoiner.NickName}.");
                if (LateJoinManager.IsLateJoinerPendingAsyncTask(lateJoiner.ActorNumber, LateJoinManager.LateJoinTaskType.Voice))
                {
                    LatePlugin.Log.LogWarning($"{LogPrefix} Marking L.A.T.E. voice task as 'completed' (skipped due to no coroutine runner) for {lateJoiner.NickName}.");
                    LateJoinManager.ReportLateJoinAsyncTaskCompleted(lateJoiner.ActorNumber, LateJoinManager.LateJoinTaskType.Voice);
                }
            }
        }

        private static IEnumerator DelayedVoiceSyncForLateJoinerCoroutine(Player lateJoiner, PlayerAvatar lateJoinerAvatar, float delay)
        {
            yield return new WaitForSeconds(delay);
            string lateJoinerName = lateJoiner?.NickName ?? "Unknown";

            if (!ShouldAttemptVoiceSync($"DelayedVoiceSyncForLateJoinerCoroutine for {lateJoinerName} (post-delay)"))
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Conditions for voice sync (late joiner {lateJoinerName}) no longer met after delay. Aborting.");
                TryCompleteLateJoinerVoiceTask(lateJoiner, $"conditions no longer met post-delay for {lateJoinerName}");
                yield break;
            }
            if (lateJoiner == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(lateJoiner.ActorNumber))
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Late joiner {lateJoinerName} left or became null during voice sync delay. Aborting.");
                yield break;
            }
            if (lateJoinerAvatar == null || lateJoinerAvatar.gameObject == null)
            {
                LatePlugin.Log.LogWarning($"{LogPrefix} Late joiner {lateJoinerName}'s avatar became null or destroyed during voice sync delay. Aborting.");
                TryCompleteLateJoinerVoiceTask(lateJoiner, $"avatar null post-delay for {lateJoinerName}");
                yield break;
            }

            LatePlugin.Log.LogInfo($"{LogPrefix} Executing delayed voice sync for late joiner {lateJoinerName}.");
            Host_SendExistingVoiceLinksToLateJoiner(lateJoiner);
            Host_BroadcastLateJoinerVoiceLinkToAll(lateJoiner, lateJoinerAvatar);
        }

        public static void Host_SendExistingVoiceLinksToLateJoiner(Player newLateJoiner)
        {
            if (!ShouldAttemptVoiceSync($"SendExistingVoiceLinksToLateJoiner for {newLateJoiner?.NickName ?? "Unknown"}")) return;
            if (newLateJoiner == null) return;

            LatePlugin.Log.LogInfo($"{LogPrefix} Sending all existing voice links TO new late joiner {newLateJoiner.NickName}.");
            var playersInRoomSnapshot = PhotonNetwork.CurrentRoom.Players.Values.ToList();
            int rpcSentCount = 0;

            foreach (Player playerToBroadcastAbout in playersInRoomSnapshot)
            {
                if (playerToBroadcastAbout == null || playerToBroadcastAbout.ActorNumber == newLateJoiner.ActorNumber) continue;
                PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(playerToBroadcastAbout);
                if (avatar == null) { LatePlugin.Log.LogDebug($"{LogPrefix} SendToLateJoiner: Null avatar for {playerToBroadcastAbout.NickName}."); continue; }
                if (PlayerStateManager.GetPlayerLifeStatus(playerToBroadcastAbout) == PlayerLifeStatus.Dead)
                {
                    LatePlugin.Log.LogInfo($"{LogPrefix} SendToLateJoiner: {playerToBroadcastAbout.NickName} is dead. Skipping send to {newLateJoiner.NickName}.");
                    continue;
                }
                PhotonView? avatarPV = PhotonUtilities.GetPhotonView(avatar);
                if (avatarPV == null) { LatePlugin.Log.LogDebug($"{LogPrefix} SendToLateJoiner: Null avatarPV for {playerToBroadcastAbout.NickName}."); continue; }

                // *** THIS IS WHERE THE ERROR WAS ***
                PlayerVoiceChat? voiceChatComponent = GetPlayerVoiceChatComponent(avatar); // Moved GetPlayerVoiceChatComponent back into the class
                if (voiceChatComponent == null) { LatePlugin.Log.LogDebug($"{LogPrefix} SendToLateJoiner: Null PlayerVoiceChat for {playerToBroadcastAbout.NickName}."); continue; }

                PhotonView? voiceChatPV = PhotonUtilities.GetPhotonView(voiceChatComponent);
                if (voiceChatPV == null) { LatePlugin.Log.LogDebug($"{LogPrefix} SendToLateJoiner: Null PV on PlayerVoiceChat for {playerToBroadcastAbout.NickName}."); continue; }

                // *** THIS IS WHERE THE ERROR WAS ***
                bool isVoiceComponentReadyOnHost = IsVoiceChatComponentReady(voiceChatComponent, avatarPV.IsMine); // Moved IsVoiceChatComponentReady back into the class
                if (isVoiceComponentReadyOnHost)
                {
                    LatePlugin.Log.LogInfo($"{LogPrefix} Sending {playerToBroadcastAbout.NickName}'s VoiceChatPV {voiceChatPV.ViewID} TO {newLateJoiner.NickName}.");
                    avatarPV.RPC("UpdateMyPlayerVoiceChat", newLateJoiner, voiceChatPV.ViewID);
                    rpcSentCount++;
                }
                else
                {
                    LatePlugin.Log.LogDebug($"{LogPrefix} SendToLateJoiner: Voice for {playerToBroadcastAbout.NickName} not ready. Not sent to {newLateJoiner.NickName}.");
                }
            }
            LatePlugin.Log.LogInfo($"{LogPrefix} Finished sending {rpcSentCount} existing voice links TO {newLateJoiner.NickName}.");
        }

        public static void Host_BroadcastLateJoinerVoiceLinkToAll(Player lateJoiner, PlayerAvatar lateJoinerAvatar)
        {
            if (!ShouldAttemptVoiceSync($"BroadcastLateJoinerVoiceLinkToAll for {lateJoiner?.NickName ?? "Unknown"}")) return;
            if (lateJoiner == null || lateJoinerAvatar == null) return;

            LatePlugin.Log.LogInfo($"{LogPrefix} Broadcasting new late joiner {lateJoiner.NickName}'s voice link to ALL.");
            if (PlayerStateManager.GetPlayerLifeStatus(lateJoiner) == PlayerLifeStatus.Dead)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} BroadcastLateJoinerToAll: {lateJoiner.NickName} is dead. Skipping broadcast.");
                TryCompleteLateJoinerVoiceTask(lateJoiner, $"is dead on host for {lateJoiner.NickName}");
                return;
            }
            bool broadcastAttempted = EnsurePlayerVoiceLinkIsBroadcastToAll(lateJoinerAvatar, $"Late joiner {lateJoiner.NickName} broadcast to all");
            if (LateJoinManager.IsLateJoinerPendingAsyncTask(lateJoiner.ActorNumber, LateJoinManager.LateJoinTaskType.Voice))
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} L.A.T.E. voice task for {lateJoiner.NickName} completed (Broadcast attempted: {broadcastAttempted}).");
                LateJoinManager.ReportLateJoinAsyncTaskCompleted(lateJoiner.ActorNumber, LateJoinManager.LateJoinTaskType.Voice);
            }
        }

        private static void TryCompleteLateJoinerVoiceTask(Player? lateJoiner, string reasonForCompletion)
        {
            if (lateJoiner != null && LateJoinManager.IsLateJoinerPendingAsyncTask(lateJoiner.ActorNumber, LateJoinManager.LateJoinTaskType.Voice))
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} L.A.T.E. voice task for {lateJoiner.NickName} completed (Reason: {reasonForCompletion}).");
                LateJoinManager.ReportLateJoinAsyncTaskCompleted(lateJoiner.ActorNumber, LateJoinManager.LateJoinTaskType.Voice);
            }
        }

        public static void Host_OnPlayerRevived(PlayerAvatar revivedAvatar)
        {
            if (!ShouldAttemptVoiceSync($"OnPlayerRevived for {GameUtilities.GetPlayerNickname(revivedAvatar)}")) return;
            if (revivedAvatar == null) return;
            string playerName = GameUtilities.GetPlayerNickname(revivedAvatar);
            LatePlugin.Log.LogInfo($"{LogPrefix} Player {playerName} revived. Attempting broadcast & scheduling resync.");
            EnsurePlayerVoiceLinkIsBroadcastToAll(revivedAvatar, $"Player {playerName} revived");
            ScheduleComprehensiveVoiceResync($"Player {playerName} revived", 1.5f);
        }

        public static void Host_TrySendInitialAvatarVoiceRpc(PlayerAvatar avatar)
        {
            if (!ShouldAttemptVoiceSync($"TrySendInitialAvatarVoiceRpc for {GameUtilities.GetPlayerNickname(avatar)}")) return;
            if (avatar == null) return;
            PhotonView? avatarPV = PhotonUtilities.GetPhotonView(avatar);
            if (avatarPV == null || avatarPV.Owner == null) return;
            if (PlayerStateManager.GetPlayerLifeStatus(avatarPV.Owner) == PlayerLifeStatus.Dead) return;
            EnsurePlayerVoiceLinkIsBroadcastToAll(avatar, "Initial/periodic check");
        }

        #region Scene and Player Lifecycle
        public static void ResetPerSceneStates()
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} Clearing _hostSentValidRpcForAvatarThisScene and stopping any active sync coroutine.");
            _hostSentValidRpcForAvatarThisScene.Clear();
            if (_masterVoiceSyncCoroutine != null && CoroutineHelper.CoroutineRunner != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(_masterVoiceSyncCoroutine);
            }
            _masterVoiceSyncCoroutine = null;
        }

        public static void Host_OnPlayerLeftRoom(Player leftPlayer)
        {
            if (!PhotonUtilities.IsRealMasterClient() || leftPlayer == null) return;

            // If a scene change is actively happening, it's safer to skip this.
            // ResetPerSceneStates() will be called by RunManager_ChangeLevelHook anyway.
            if (GameUtilities.IsSceneChangeInProgress())
            {
                LatePlugin.Log.LogDebug($"{LogPrefix} Host_OnPlayerLeftRoom: Scene change in progress. Skipping cleanup for {leftPlayer.NickName}. ResetPerSceneStates will handle general cleanup.");
                return;
            }

            LatePlugin.Log.LogDebug($"{LogPrefix} Host_OnPlayerLeftRoom: Processing player {leftPlayer.NickName} (ActorNr: {leftPlayer.ActorNumber}).");

            List<int> avatarViewIDsToRemove = new List<int>();
            // Create a temporary list of keys to iterate over, to avoid issues if the dictionary structure changes unexpectedly (though unlikely here)
            // Or, iterate directly if confident _hostSentValidRpcForAvatarThisScene is not modified by other threads/callbacks during this loop.
            // For this specific case, building a list of IDs to remove is fine.
            foreach (var entry in _hostSentValidRpcForAvatarThisScene) // Iterating a Dictionary's KVP is usually safe for reads
            {
                PhotonView? pv = null;
                try
                {
                    // PhotonView.Find can be problematic if views are being destroyed or scene is unstable.
                    // The IsSceneChangeInProgress guard above should mitigate this significantly.
                    pv = PhotonView.Find(entry.Key);
                }
                catch (Exception ex)
                {
                    // This catch is for unexpected errors from PhotonView.Find itself.
                    LatePlugin.Log.LogWarning($"{LogPrefix} Host_OnPlayerLeftRoom: Exception finding PhotonView for ID {entry.Key} (Player: {leftPlayer.NickName}): {ex.Message}. Marking for removal.");
                    avatarViewIDsToRemove.Add(entry.Key);
                    continue;
                }

                if (pv == null) // PhotonView with this ID no longer exists in the scene.
                {
                    avatarViewIDsToRemove.Add(entry.Key);
                    LatePlugin.Log.LogDebug($"{LogPrefix} Host_OnPlayerLeftRoom: Avatar ViewID {entry.Key} (associated with {leftPlayer.NickName}'s departure) not found. Marking for removal from RPC tracking.");
                    continue;
                }

                // If the PhotonView exists, check if it belongs to the player who left.
                // pv.Owner can be null if the owner already left and Photon cleaned up some info.
                if (pv.Owner == null || pv.Owner.ActorNumber == leftPlayer.ActorNumber)
                {
                    avatarViewIDsToRemove.Add(entry.Key);
                    // Log with care, pv.Owner might be null
                    string ownerInfo = pv.Owner != null ? $"Owner: {pv.Owner.NickName} (ActorNr: {pv.Owner.ActorNumber})" : "Owner: Null";
                    LatePlugin.Log.LogDebug($"{LogPrefix} Host_OnPlayerLeftRoom: Avatar ViewID {entry.Key} ({ownerInfo}) matches leaving player {leftPlayer.NickName} or owner is null. Marking for removal.");
                }
            }

            if (avatarViewIDsToRemove.Count > 0)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Host_OnPlayerLeftRoom: Removing {avatarViewIDsToRemove.Count} Avatar ViewID(s) from RPC tracking related to leaving player {leftPlayer.NickName}.");
                foreach (int viewID in avatarViewIDsToRemove)
                {
                    _hostSentValidRpcForAvatarThisScene.Remove(viewID);
                }
            }
            else
            {
                LatePlugin.Log.LogDebug($"{LogPrefix} Host_OnPlayerLeftRoom: No Avatar ViewIDs found in RPC tracking associated with leaving player {leftPlayer.NickName}.");
            }
        }
        #endregion

        #region Private Core Logic (Host Only)

        // Re-inserted helper methods
        private static bool IsVoiceChatComponentReady(PlayerVoiceChat? vc, bool isLocalAvatar)
        {
            if (vc == null) return false;
            bool ttsInst = false;
            bool recEnabled = false;

            if (ReflectionCache.PlayerVoiceChat_TTSinstantiatedField != null)
            {
                try { ttsInst = ReflectionCache.PlayerVoiceChat_TTSinstantiatedField.GetValue(vc) as bool? ?? false; }
                catch (System.Exception ex) { LatePlugin.Log.LogError($"{LogPrefix} Error reflecting TTSinstantiated: {ex.Message}"); }
            }
            else { LatePlugin.Log.LogError($"{LogPrefix} ReflectionCache.PlayerVoiceChat_TTSinstantiatedField is null!"); return false; }

            if (!ttsInst) return false;

            if (isLocalAvatar)
            {
                if (ReflectionCache.PlayerVoiceChat_RecordingEnabledField != null)
                {
                    try { recEnabled = ReflectionCache.PlayerVoiceChat_RecordingEnabledField.GetValue(vc) as bool? ?? false; }
                    catch (System.Exception ex) { LatePlugin.Log.LogError($"{LogPrefix} Error reflecting RecordingEnabled: {ex.Message}"); }
                }
                else { LatePlugin.Log.LogError($"{LogPrefix} ReflectionCache.PlayerVoiceChat_RecordingEnabledField is null for local avatar check!"); return false; }
                return recEnabled;
            }
            return true;
        }

        private static PlayerVoiceChat? GetPlayerVoiceChatComponent(PlayerAvatar? avatar)
        {
            if (avatar == null) return null;
            PlayerVoiceChat? pvc = null;
            if (ReflectionCache.PlayerAvatar_VoiceChatField != null)
            {
                try { pvc = ReflectionCache.PlayerAvatar_VoiceChatField.GetValue(avatar) as PlayerVoiceChat; }
                catch (System.Exception ex) { LatePlugin.Log.LogError($"{LogPrefix} Error reflecting PlayerAvatar_VoiceChatField: {ex}"); }
            }
            if (pvc == null)
            {
                pvc = avatar.GetComponentInChildren<PlayerVoiceChat>(true);
            }
            return pvc;
        }
        // End of re-inserted helper methods


        private static bool EnsurePlayerVoiceLinkIsBroadcastToAll(PlayerAvatar avatarToBroadcast, string context)
        {
            if (!PhotonUtilities.IsRealMasterClient() || avatarToBroadcast == null) return false;
            PhotonView? avatarPV = PhotonUtilities.GetPhotonView(avatarToBroadcast);
            if (avatarPV == null || avatarPV.Owner == null) return false;

            if (PlayerStateManager.GetPlayerLifeStatus(avatarPV.Owner) == PlayerLifeStatus.Dead)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} EnsureBroadcast ({context}): {GameUtilities.GetPlayerNickname(avatarToBroadcast)} is dead. Skipping.");
                return false;
            }

            // *** THIS IS WHERE THE ERROR WAS ***
            PlayerVoiceChat? voiceChatComponent = GetPlayerVoiceChatComponent(avatarToBroadcast); // Now calls the in-class method
            PhotonView? voiceChatPV = PhotonUtilities.GetPhotonView(voiceChatComponent);

            if (voiceChatComponent != null && voiceChatPV != null)
            {
                if (_hostSentValidRpcForAvatarThisScene.TryGetValue(avatarPV.ViewID, out int sentVoicePvId) && sentVoicePvId == voiceChatPV.ViewID)
                {
                    // LatePlugin.Log.LogDebug($"{LogPrefix} EnsureBroadcast ({context}): Link for {GameUtilities.GetPlayerNickname(avatarToBroadcast)} already sent. Confirmed.");
                    return true;
                }

                // *** THIS IS WHERE THE ERROR WAS ***
                bool isVoiceComponentReadyOnHost = IsVoiceChatComponentReady(voiceChatComponent, avatarPV.IsMine); // Now calls the in-class method
                if (isVoiceComponentReadyOnHost)
                {
                    string playerName = GameUtilities.GetPlayerNickname(avatarToBroadcast);
                    LatePlugin.Log.LogInfo($"{LogPrefix} EnsureBroadcast ({context}): {playerName}'s VoiceChat (ViewID: {voiceChatPV.ViewID}) ready. Broadcasting RPC.");
                    avatarPV.RPC("UpdateMyPlayerVoiceChat", RpcTarget.AllBuffered, voiceChatPV.ViewID);
                    _hostSentValidRpcForAvatarThisScene[avatarPV.ViewID] = voiceChatPV.ViewID;
                    return true;
                }
                else
                {
                    LatePlugin.Log.LogDebug($"{LogPrefix} EnsureBroadcast ({context}): Voice for {GameUtilities.GetPlayerNickname(avatarToBroadcast)} not ready. Not sent.");
                }
            }
            else
            {
                // LatePlugin.Log.LogDebug($"{LogPrefix} EnsureBroadcast ({context}): Voice component or PV null for {GameUtilities.GetPlayerNickname(avatarToBroadcast)}. Not sent.");
            }
            return false;
        }

        private static void ScheduleComprehensiveVoiceResync(string reason, float delay)
        {
            if (!ShouldAttemptVoiceSync($"ScheduleComprehensiveVoiceResync due to {reason}")) return;
            if (_masterVoiceSyncCoroutine != null)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} CompResync already scheduled. New: '{reason}'. Will proceed.");
                return;
            }
            if (CoroutineHelper.CoroutineRunner == null) { LatePlugin.Log.LogError($"{LogPrefix} Cannot schedule CompResync for '{reason}': CoroutineRunner null."); return; }
            LatePlugin.Log.LogInfo($"{LogPrefix} Scheduling CompResync (Trigger: {reason}, Delay: {delay}s)...");
            _masterVoiceSyncCoroutine = CoroutineHelper.CoroutineRunner.StartCoroutine(DelayedComprehensiveVoiceResyncCoroutine(reason, delay));
        }

        private static IEnumerator DelayedComprehensiveVoiceResyncCoroutine(string triggerReason, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!ShouldAttemptVoiceSync($"DelayedComprehensiveVoiceResyncCoroutine for {triggerReason} (post-delay)"))
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Conditions for CompResync (Trigger: {triggerReason}) no longer met. Aborting.");
                _masterVoiceSyncCoroutine = null;
                yield break;
            }
            _masterVoiceSyncCoroutine = null;
            LatePlugin.Log.LogInfo($"{LogPrefix} Executing CompResync (Trigger: {triggerReason}).");
            int processedCount = 0;
            var playersInRoomSnapshot = PhotonNetwork.CurrentRoom.Players.Values.ToList();

            foreach (Player playerToBroadcastAbout in playersInRoomSnapshot)
            {
                if (playerToBroadcastAbout == null) continue;
                PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(playerToBroadcastAbout);
                if (avatar == null) { continue; }

                EnsurePlayerVoiceLinkIsBroadcastToAll(avatar, $"Comprehensive Resync for {playerToBroadcastAbout.NickName}");
                processedCount++;

                if (LateJoinManager.IsLateJoinerPendingAsyncTask(playerToBroadcastAbout.ActorNumber, LateJoinManager.LateJoinTaskType.Voice))
                {
                    bool wasSentOrConfirmed = _hostSentValidRpcForAvatarThisScene.ContainsKey(PhotonUtilities.GetViewId(avatar));
                    bool isAlive = PlayerStateManager.GetPlayerLifeStatus(playerToBroadcastAbout) == PlayerLifeStatus.Alive;
                    if (wasSentOrConfirmed && isAlive)
                    {
                        LatePlugin.Log.LogInfo($"{LogPrefix} CompResync: Fallback L.A.T.E. voice task marked complete for {playerToBroadcastAbout.NickName}.");
                        LateJoinManager.ReportLateJoinAsyncTaskCompleted(playerToBroadcastAbout.ActorNumber, LateJoinManager.LateJoinTaskType.Voice);
                    }
                    else if (!isAlive)
                    {
                        LatePlugin.Log.LogInfo($"{LogPrefix} CompResync: {playerToBroadcastAbout.NickName} is dead. Completing pending L.A.T.E. voice task as skipped.");
                        LateJoinManager.ReportLateJoinAsyncTaskCompleted(playerToBroadcastAbout.ActorNumber, LateJoinManager.LateJoinTaskType.Voice);
                    }
                }
            }
            LatePlugin.Log.LogInfo($"{LogPrefix} CompResync finished. Processed {processedCount} players.");
        }
        #endregion
    }
}