// File: L.A.T.E/Patches/UI/TruckScreenTextPatches.cs
using System;
using System.Reflection;

using HarmonyLib;

using Photon.Pun;

using LATE.Core;                 // LatePlugin.Log
using LATE.Managers.GameState;   // GameVersionSupport
using LATE.Patches.CoreGame;     // RunManagerPatches
using LATE.Utilities;            // PhotonUtilities

namespace LATE.Patches.UI;

/*──────────────────────────────────────────────────────────────────────────────*/
/*  Early-lock helper                                                          */
/*──────────────────────────────────────────────────────────────────────────────*/

internal static class EarlyLobbyLockHelper
{
    private const string LogPrefix = "[Early Lock]";

    /// <summary>Locks Photon room & Steam lobby when a level change is imminent.</summary>
    internal static void TryLockLobby(string reason)
    {
        if (!PhotonUtilities.IsRealMasterClient()) return;

        RunManagerPatches.SetInitialPublicListingPhaseComplete(true);
        LatePlugin.Log.LogInfo($"[L.A.T.E] {LogPrefix} Host about to change level (Trigger: {reason}). Locking lobby.");

        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom is { IsOpen: true })
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            LatePlugin.Log.LogDebug($"{LogPrefix} Photon room IsOpen → FALSE.");
        }

        GameVersionSupport.LockSteamLobby();
    }
}

/*──────────────────────────────────────────────────────────────────────────────*/
/*  TruckScreenText patches                                                    */
/*──────────────────────────────────────────────────────────────────────────────*/

[HarmonyPatch]
internal static class TruckScreenTextPatches
{
    private const string LogPrefix = "[TruckScreenTextPatches]";

    // Cached FieldInfo for private bool TruckScreenText.playerChatBoxStateStart
    private static FieldInfo? _playerChatBoxStateStartField;

    private static FieldInfo PlayerChatBoxStateStartField =>
        _playerChatBoxStateStartField ??= AccessTools.Field(typeof(TruckScreenText), "playerChatBoxStateStart");

    /*------------------------------------------------------------------------*/
    /*  PlayerChatBoxStateLockedDestroySlackers – PREFIX                      */
    /*------------------------------------------------------------------------*/

    [HarmonyPatch(typeof(TruckScreenText), "PlayerChatBoxStateLockedDestroySlackers")]
    [HarmonyPrefix]
    private static bool LockedDestroySlackers_Prefix(TruckScreenText __instance) =>
        CheckAndEarlyLock(__instance, "DestroySlackers");

    /*------------------------------------------------------------------------*/
    /*  PlayerChatBoxStateLockedStartingTruck – PREFIX                        */
    /*------------------------------------------------------------------------*/

    [HarmonyPatch(typeof(TruckScreenText), "PlayerChatBoxStateLockedStartingTruck")]
    [HarmonyPrefix]
    private static bool LockedStartingTruck_Prefix(TruckScreenText __instance) =>
        CheckAndEarlyLock(__instance, "StartingTruck");

    /*------------------------------------------------------------------------*/
    /*  Shared helper                                                         */
    /*------------------------------------------------------------------------*/

    private static bool CheckAndEarlyLock(TruckScreenText tst, string ctx)
    {
        if (!PhotonUtilities.IsRealMasterClient()) return true;

        if (PlayerChatBoxStateStartField == null)
        {
            LatePlugin.Log.LogError($"{LogPrefix} Reflection failed: field 'playerChatBoxStateStart' not found – early lock disabled.");
            return true;
        }

        try
        {
            bool isStateStarting = (bool)(PlayerChatBoxStateStartField.GetValue(tst) ?? false);
            if (isStateStarting)
                EarlyLobbyLockHelper.TryLockLobby($"TruckScreenText.{ctx} (State Enter)");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} {ctx}: error reading playerChatBoxStateStart → {ex}");
        }

        return true; // always allow original method to run
    }
}