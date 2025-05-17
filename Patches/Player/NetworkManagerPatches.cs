// File: L.A.T.E/Patches/Player/NetworkManagerPatches.cs
using HarmonyLib;
using LATE.Core;
using LATE.Managers;
using LATE.Utilities;

namespace LATE.Patches.Player; // File-scoped namespace

/// <summary>
/// Contains Harmony patches for the NetworkManager class.
/// </summary>
[HarmonyPatch]
internal static class NetworkManagerPatches
{
    /// <summary>
    /// Handles post-processing when a new player enters the room.
    /// This is an EXPLICIT Harmony patch.
    /// </summary>
    public static void NetworkManager_OnPlayerEnteredRoom_Postfix(Photon.Realtime.Player newPlayer)
    {
        if (!GameUtilities.IsModLogicActive())
        {
            LatePlugin.Log?.LogDebug(
                $"[NetworkManagerPatches] Player entered room in disabled scene. Skipping L.A.T.E join handling for {newPlayer?.NickName ?? "NULL"}."
            );
            return;
        }

       // LatePlugin.Log.LogDebug(
       //    $"[NetworkManagerPatches] Player entered room: {newPlayer?.NickName ?? "NULL"} (ActorNr: {newPlayer?.ActorNumber ?? -1}) in an active scene."
       // );

        if (newPlayer != null)
        {
            LateJoinManager.HandlePlayerJoined(newPlayer);
        }
        else
        {
            LatePlugin.Log.LogWarning(
                "[NetworkManagerPatches] Received null player in OnPlayerEnteredRoom_Postfix."
            );
        }
    }

    /// <summary>
    /// Handles when a player leaves the room by tracking position and cleaning up tracking.
    /// This is an EXPLICIT Harmony patch.
    /// </summary>
    public static void NetworkManager_OnPlayerLeftRoom_Postfix(Photon.Realtime.Player otherPlayer)
    {
        if (otherPlayer == null) // Early exit for null player
        {
            LatePlugin.Log?.LogWarning("[NetworkManagerPatches] Received null player in OnPlayerLeftRoom_Postfix.");
            return;
        }

        // Always clear L.A.T.E. specific tracking for the player.
        // This primarily manipulates internal data structures and should be relatively safe.
        LateJoinManager.ClearPlayerTracking(otherPlayer.ActorNumber);

        // If a scene change is actively happening (e.g., RunManager.restarting is true),
        // skip complex operations that rely on scene objects which might be in a volatile state.
        if (GameUtilities.IsSceneChangeInProgress())
        {
            LatePlugin.Log.LogInfo(
                $"[NetworkManagerPatches] Player {otherPlayer.NickName} (ActorNr: {otherPlayer.ActorNumber}) left during a scene change. " +
                "Skipping complex avatar/enemy/voice cleanup to prevent errors. Basic L.A.T.E. tracking cleared."
            );
            // VoiceManager.ResetPerSceneStates() called by RunManager_ChangeLevelHook will handle general voice cleanup.
            return;
        }

        // Proceed with more detailed cleanup if not in a scene change and mod logic is active.
        if (!GameUtilities.IsModLogicActive())
        {
            LatePlugin.Log?.LogDebug(
                $"[NetworkManagerPatches] Player left room in disabled scene. Skipping L.A.T.E leave handling for {otherPlayer.NickName}."
            );
            // Log for base game passthrough if needed, but primary L.A.T.E. logic is skipped.
            return;
        }

        LatePlugin.Log.LogInfo(
            $"[NetworkManagerPatches] Player {otherPlayer.NickName} (ActorNr: {otherPlayer.ActorNumber}) left room in active scene (not changing level)."
        );

        if (PhotonUtilities.IsRealMasterClient())
        {
            // Attempt to find avatar and update position
            PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(otherPlayer);
            if (avatar != null)
            {
                PlayerPositionManager.UpdatePlayerPosition(
                    otherPlayer,
                    avatar.transform.position,
                    avatar.transform.rotation
                );
            }
            else
            {
                LatePlugin.Log.LogWarning(
                    $"[NetworkManagerPatches] Could not find PlayerAvatar for leaving player {otherPlayer.NickName} to track position (active scene, not changing level)."
                );
            }

            // Notify enemies
            try
            {
                EnemySyncManager.NotifyEnemiesOfLeavingPlayer(otherPlayer);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[NetworkManagerPatches] Error in EnemySyncManager.NotifyEnemiesOfLeavingPlayer for {otherPlayer.NickName}: {ex}");
            }
        }

        // Handle VoiceManager cleanup for the player.
        try
        {
            VoiceManager.Host_OnPlayerLeftRoom(otherPlayer);
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[NetworkManagerPatches] Error in VoiceManager.Host_OnPlayerLeftRoom for {otherPlayer.NickName}: {ex}");
        }
    }
}