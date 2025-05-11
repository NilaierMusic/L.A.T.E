// File: L.A.T.E/Patches/CoreGame/NetworkConnectPatches.cs
using HarmonyLib;

using Photon.Pun;
using Photon.Realtime;     // ClientState

using LATE.Core;           // LatePlugin.Log

namespace LATE.Patches.CoreGame;

/// <summary>Harmony patches for <see cref="NetworkConnect"/>.</summary>
[HarmonyPatch]
internal static class NetworkConnectPatches
{
    private const string LogPrefix = "[NetworkConnectPatches]";

    /* --------------------------------------------------------------------- */
    /*  Prefix on NetworkConnect.Start                                       */
    /* --------------------------------------------------------------------- */

    [HarmonyPatch(typeof(NetworkConnect), "Start")]
    [HarmonyPrefix]
    private static void ForceAutoSyncScene_Start_Prefix()
    {
        // Skip if initial host setup or single-player
        if (GameManager.instance == null ||
            PhotonNetwork.NetworkClientState is ClientState.Disconnected or ClientState.PeerCreated ||
            GameManager.instance.gameMode == 0)
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} Skipping AutomaticallySyncScene force – initial host/SP setup.");
            return;
        }

        if (PhotonNetwork.AutomaticallySyncScene)
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} AutomaticallySyncScene already TRUE.");
            return;
        }

        PhotonNetwork.AutomaticallySyncScene = true;
        LatePlugin.Log.LogInfo($"{LogPrefix} Forced PhotonNetwork.AutomaticallySyncScene = TRUE.");
    }

    /* --------------------------------------------------------------------- */
    /*  Postfix on NetworkConnect.OnJoinedRoom                               */
    /* --------------------------------------------------------------------- */

    [HarmonyPatch(typeof(NetworkConnect), nameof(NetworkConnect.OnJoinedRoom))]
    [HarmonyPostfix]
    private static void LogAutoSync_Postfix() =>
        LatePlugin.Log.LogInfo($"{LogPrefix} OnJoinedRoom: AutomaticallySyncScene = {PhotonNetwork.AutomaticallySyncScene}");
}