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

        LatePlugin.Log.LogDebug(
            $"[NetworkManagerPatches] Player entered room: {newPlayer?.NickName ?? "NULL"} (ActorNr: {newPlayer?.ActorNumber ?? -1}) in an active scene."
        );

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
        if (otherPlayer != null)
        {
            LateJoinManager.ClearPlayerTracking(otherPlayer.ActorNumber);
        }

        if (!GameUtilities.IsModLogicActive())
        {
            LatePlugin.Log?.LogDebug(
                $"[NetworkManagerPatches] Player left room in disabled scene. Skipping L.A.T.E leave handling for {otherPlayer?.NickName ?? "NULL"}."
            );

            if (otherPlayer != null)
            {
                LatePlugin.Log?.LogInfo(
                    $"[NetworkManagerPatches][BaseGamePassthrough] Player left room: {otherPlayer.NickName} (ActorNr: {otherPlayer.ActorNumber})"
                );
            }
            else
            {
                LatePlugin.Log?.LogWarning(
                    "[NetworkManagerPatches][BaseGamePassthrough] Received null player in OnPlayerLeftRoom_Postfix."
                );
            }

            return;
        }

        if (otherPlayer != null)
        {
            LatePlugin.Log.LogInfo(
                $"[NetworkManagerPatches] Player left room in active scene: {otherPlayer.NickName} (ActorNr: {otherPlayer.ActorNumber})"
            );

            if (PhotonUtilities.IsRealMasterClient())
            {
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
                        $"[NetworkManagerPatches] Could not find PlayerAvatar for leaving player {otherPlayer.NickName} to track position."
                    );
                }

                EnemySyncManager.NotifyEnemiesOfLeavingPlayer(otherPlayer);
            }

            VoiceManager.HandlePlayerLeft(otherPlayer);
        }
        else
        {
            LatePlugin.Log.LogWarning(
                "[NetworkManagerPatches] Received null player in OnPlayerLeftRoom_Postfix (active scene)."
            );
        }
    }
}