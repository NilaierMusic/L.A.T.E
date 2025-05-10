// File: L.A.T.E/Patches/CoreGame/NetworkConnectPatches.cs
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime; // For ClientState
using LATE.Core; // For LatePlugin.Log

namespace LATE.Patches.CoreGame; // File-scoped namespace

/// <summary>
/// Contains Harmony patches for the NetworkConnect class.
/// </summary>
[HarmonyPatch]
internal static class NetworkConnectPatches
{
    [HarmonyPatch(typeof(NetworkConnect), "Start")]
    [HarmonyPrefix]
    static void ForceAutoSyncSceneStartPrefix()
    {
        // Only force this on clients joining, not the initial host setup or singleplayer.
        // Check if not disconnected/peercreated AND in multiplayer mode.
        if (
            GameManager.instance != null
            && PhotonNetwork.NetworkClientState != ClientState.Disconnected
            && PhotonNetwork.NetworkClientState != ClientState.PeerCreated
            && GameManager.instance.gameMode != 0 // 0 is typically singleplayer/offline
        )
        {
            if (!PhotonNetwork.AutomaticallySyncScene)
            {
                LatePlugin.Log.LogInfo(
                    "[L.A.T.E] Forcing PhotonNetwork.AutomaticallySyncScene = true in NetworkConnect.Start (Prefix)"
                );
                PhotonNetwork.AutomaticallySyncScene = true;
            }
            else
            {
                LatePlugin.Log.LogDebug(
                    "[L.A.T.E] PhotonNetwork.AutomaticallySyncScene is already true in NetworkConnect.Start (Prefix)."
                );
            }
        }
        else
        {
            LatePlugin.Log.LogDebug(
                "[L.A.T.E] Skipping AutomaticallySyncScene force in NetworkConnect.Start (Prefix) - Likely initial host/SP setup."
            );
        }
    }

    [HarmonyPatch(typeof(NetworkConnect), nameof(NetworkConnect.OnJoinedRoom))]
    [HarmonyPostfix]
    static void LogAutoSyncPostfix()
    {
        LatePlugin.Log.LogInfo(
            $"[L.A.T.E] NetworkConnect.OnJoinedRoom Postfix: AutomaticallySyncScene is now {PhotonNetwork.AutomaticallySyncScene}"
        );
    }
}