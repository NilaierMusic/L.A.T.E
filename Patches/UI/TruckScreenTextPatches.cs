// File: L.A.T.E/Patches/UI/TruckScreenTextPatches.cs
using HarmonyLib;
using LATE.Core; // For LatePlugin.Log
using LATE.Managers.GameState; // For GameVersionSupport
using LATE.Utilities; // For PhotonUtilities
using Photon.Pun; // For PhotonNetwork
using LATE.Patches.CoreGame;
using System.Reflection; // For FieldInfo

namespace LATE.Patches.UI; // File-scoped namespace

/// <summary>
/// Helper class to centralize the early lobby lock logic triggered by TruckScreenText state changes.
/// </summary>
internal static class EarlyLobbyLockHelper
{
    /// <summary>
    /// Attempts to lock the Photon room and Steam lobby.
    /// This is called by TruckScreenText patches when specific state transitions occur,
    /// indicating a level change is imminent.
    /// </summary>
    /// <param name="reason">A descriptive string for logging the reason for the lock.</param>
    internal static void TryLockLobby(string reason)
    {
        if (!PhotonUtilities.IsRealMasterClient())
        {
            return;
        }

        // Set the flag when the game is first truly starting.
        RunManagerPatches.SetInitialPublicListingPhaseComplete(true);

        LatePlugin.Log.LogInfo($"[L.A.T.E.] [Early Lock] Host is about to change level (Trigger: {reason}). Locking lobby NOW. Initial public phase marked complete.");

        // Lock Photon Room
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            if (PhotonNetwork.CurrentRoom.IsOpen) // Only log if it was actually open
            {
                PhotonNetwork.CurrentRoom.IsOpen = false;
                LatePlugin.Log.LogDebug("[Early Lock] Photon Room IsOpen set to false.");
            }
        }

        // Lock Steam Lobby (uses version-aware helper)
        GameVersionSupport.LockSteamLobby();
    }
}

/// <summary>
/// Contains Harmony patches for the TruckScreenText class, primarily to implement
/// an "early lock" of the lobby when certain UI states indicate a level change is imminent.
/// </summary>
[HarmonyPatch]
internal static class TruckScreenTextPatches
{
    // Cache the reflection FieldInfo for efficiency and error checking
    private static FieldInfo? _tstPlayerChatBoxStateStartField;

    // Helper method to get/cache the FieldInfo for TruckScreenText.playerChatBoxStateStart
    private static FieldInfo? GetPlayerChatBoxStateStartField()
    {
        if (_tstPlayerChatBoxStateStartField == null)
        {
            _tstPlayerChatBoxStateStartField = AccessTools.Field(typeof(TruckScreenText), "playerChatBoxStateStart");
            if (_tstPlayerChatBoxStateStartField == null)
            {
                LatePlugin.Log?.LogError("[TruckScreenTextPatches] [Reflection Error] Failed to find private field 'TruckScreenText.playerChatBoxStateStart'. Early lock patch will not function correctly.");
            }
        }
        return _tstPlayerChatBoxStateStartField;
    }

    // Patch for PlayerChatBoxStateLockedDestroySlackers
    // This state is entered when the "Destroy Slackers" button is pressed on the truck screen.
    [HarmonyPatch(typeof(TruckScreenText), "PlayerChatBoxStateLockedDestroySlackers")]
    [HarmonyPrefix]
    static bool Prefix_PlayerChatBoxStateLockedDestroySlackers(TruckScreenText __instance)
    {
        if (PhotonUtilities.IsRealMasterClient())
        {
            FieldInfo? startFlagField = GetPlayerChatBoxStateStartField();
            if (startFlagField != null)
            {
                try
                {
                    // Read the value of playerChatBoxStateStart *before* the original method potentially changes it.
                    bool isStateStarting = (bool)(startFlagField.GetValue(__instance) ?? false);

                    if (isStateStarting)
                    {
                        // This means the original method's 'if (playerChatBoxStateStart)' block is about to execute.
                        // This is the precise moment we want to lock the lobby for this state transition.
                        EarlyLobbyLockHelper.TryLockLobby("TruckScreenText.PlayerChatBoxStateLockedDestroySlackers (State Enter)");
                    }
                }
                catch (Exception ex)
                {
                    LatePlugin.Log?.LogError($"[TruckScreenTextPatches] [Early Lock Prefix - DestroySlackers] Error accessing playerChatBoxStateStart: {ex}");
                }
            }
        }
        return true; // Always return true to let the original method execute.
    }

    // Patch for PlayerChatBoxStateLockedStartingTruck
    // This state is entered when the "Start Truck" button is pressed.
    [HarmonyPatch(typeof(TruckScreenText), "PlayerChatBoxStateLockedStartingTruck")]
    [HarmonyPrefix]
    static bool Prefix_PlayerChatBoxStateLockedStartingTruck(TruckScreenText __instance)
    {
        if (PhotonUtilities.IsRealMasterClient())
        {
            FieldInfo? startFlagField = GetPlayerChatBoxStateStartField();
            if (startFlagField != null)
            {
                try
                {
                    bool isStateStarting = (bool)(startFlagField.GetValue(__instance) ?? false);
                    if (isStateStarting)
                    {
                        EarlyLobbyLockHelper.TryLockLobby("TruckScreenText.PlayerChatBoxStateLockedStartingTruck (State Enter)");
                    }
                }
                catch (Exception ex)
                {
                    LatePlugin.Log?.LogError($"[TruckScreenTextPatches] [Early Lock Prefix - StartingTruck] Error accessing playerChatBoxStateStart: {ex}");
                }
            }
        }
        return true; // Always return true.
    }
}