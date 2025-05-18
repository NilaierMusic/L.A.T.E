// File: L.A.T.E/Patches/Player/PlayerControllerPatches.cs
using HarmonyLib;
using LATE.Config;
using LATE.Core;
using LATE.Managers;
using LATE.Utilities;
using Photon.Pun;
using UnityEngine;

namespace LATE.Patches.Player;

[HarmonyPatch]
internal static class PlayerControllerPatches
{
    private const string LogPrefix = "[PlayerControllerPatches]";

    [HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
    [HarmonyPostfix]
    private static void PlayerController_FixedUpdate_Postfix_TrackPosition(PlayerController __instance)
    {
        // ADD THIS CHECK
        if (!GameUtilities.IsModLogicActive()) return;

        if (PhotonNetwork.IsMasterClient && ConfigManager.SpawnAtLastPosition.Value)
        {
            if (__instance.playerAvatarScript != null && __instance.playerAvatarScript.photonView != null && __instance.playerAvatarScript.photonView.IsMine)
            {
                // This is the Host's own player controller
                if (PhotonNetwork.LocalPlayer != null) // Ensure LocalPlayer is not null
                {
                    PlayerPositionManager.UpdatePlayerPosition(
                        PhotonNetwork.LocalPlayer,
                        __instance.transform.position,
                        __instance.transform.rotation
                    );
                }
                else
                {
                    LatePlugin.Log.LogWarning($"{LogPrefix} PhotonNetwork.LocalPlayer is null. Cannot update host's position.");
                }
            }
        }
    }
}