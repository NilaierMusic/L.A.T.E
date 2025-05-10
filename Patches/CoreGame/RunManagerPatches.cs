// File: L.A.T.E/Patches/CoreGame/RunManagerPatches.cs
using System;
using System.Collections; // For IEnumerator
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using LATE.Config;
using LATE.Core; // For LatePlugin.Log, CoroutineHelper
using LATE.Managers; // For LateJoinManager, DestructionManager, PlayerStateManager, PlayerPositionManager
using LATE.Managers.GameState; // For GameVersionSupport
using LATE.Utilities; // For PhotonUtilities, GameUtilities

namespace LATE.Patches.CoreGame;

/// <summary>
/// Contains Harmony patches for the RunManager class.
/// </summary>
[HarmonyPatch]
internal static class RunManagerPatches
{
    private static bool _shouldOpenLobbyAfterGen = false;
    private static bool _normalUnlockLogicExecuted = false;
    private static Coroutine? _lobbyUnlockFailsafeCoroutine;

    /// <summary>
    /// Determines if the lobby should allow players to join based on the current game state and configuration.
    /// NOTE: This should be called AFTER the level change has occurred (RunManager.levelCurrent reflects the NEW level).
    /// </summary>
    private static bool ShouldAllowLobbyJoin(RunManager runManager, bool levelFailed)
    {
        Level currentLevel = runManager.levelCurrent;

        if (levelFailed && currentLevel == runManager.levelArena)
        {
            LatePlugin.Log.LogInfo("[RunManagerPatches] Previous level failed, current level IS Arena. Allowing join based on Arena config.");
            return ConfigManager.AllowInArena.Value;
        }

        if (levelFailed && currentLevel != runManager.levelArena)
        {
            if (!ConfigManager.LockLobbyOnLevelGenerationFailure.Value)
            {
                LatePlugin.Log.LogInfo($"[RunManagerPatches] Level reported failure (current: '{currentLevel?.name ?? "NULL"}', not Arena), but 'LockLobbyOnLevelGenerationFailure' is FALSE. Proceeding to scene-specific rules.");
            }
            else
            {
                LatePlugin.Log.LogInfo($"[RunManagerPatches] Level reported failure (current: '{currentLevel?.name ?? "NULL"}', not Arena) and 'LockLobbyOnLevelGenerationFailure' is TRUE. Disallowing join.");
                return false;
            }
        }

        LatePlugin.Log.LogDebug($"[RunManagerPatches] Evaluating scene-specific join rules for current level '{currentLevel?.name ?? "NULL"}'.");

        if (currentLevel == runManager.levelShop && ConfigManager.AllowInShop.Value) return true;
        if (currentLevel == runManager.levelLobby && ConfigManager.AllowInTruck.Value) return true;
        if (currentLevel == runManager.levelArena && ConfigManager.AllowInArena.Value) return true;
        if (currentLevel == runManager.levelLobbyMenu) return true; // Always allow in pre-game lobby menu
        if (currentLevel != null && runManager.levels.Contains(currentLevel) && ConfigManager.AllowInLevel.Value) return true;

        LatePlugin.Log.LogDebug($"[RunManagerPatches] No applicable allow condition met for level '{currentLevel?.name ?? "NULL"}'. Disallowing join.");
        return false;
    }

    /// <summary>
    /// Hook for RunManager.ChangeLevel. Manages lobby state and resets trackers.
    /// This is a MonoMod Hook, not a Harmony Patch defined by attributes here.
    /// Its application is handled by Core.PatchManager.
    /// </summary>
    public static void RunManager_ChangeLevelHook(
        Action<RunManager, bool, bool, RunManager.ChangeLevelType> orig,
        RunManager self,
        bool completedLevel,
        bool levelFailed,
        RunManager.ChangeLevelType changeLevelType)
    {
        LatePlugin.Log.LogDebug("[RunManagerPatches] ChangeLevelHook: Clearing trackers.");
        // Patches.spawnPositionAssigned.Clear(); // This will be moved to PlayerAvatarPatches
        LateJoinManager.ResetSceneTracking();
        DestructionManager.ResetState();
        PlayerStateManager.ResetPlayerStatuses();
        PlayerPositionManager.ResetPositions();
        // Patches._reloadHasBeenTriggeredThisScene = false; // This will be moved to PlayerAvatarPatches

        _shouldOpenLobbyAfterGen = false;

        if (ConfigManager.AllowInShop == null) // Basic config check
        {
            LatePlugin.Log.LogError("[RunManagerPatches] Config values not bound!");
            if (PhotonNetwork.InRoom) PhotonNetwork.CurrentRoom.IsOpen = false;
            orig.Invoke(self, completedLevel, levelFailed, changeLevelType);
            return;
        }

        if (!PhotonUtilities.IsRealMasterClient())
        {
            orig.Invoke(self, completedLevel, levelFailed, changeLevelType);
            return;
        }

        LatePlugin.Log.LogInfo($"[RunManagerPatches] Host changing level. Completed: {completedLevel}, Failed: {levelFailed}, Type: {changeLevelType}. Pre-Change Level: '{self.levelCurrent?.name ?? "None"}'");

        PhotonUtilities.ClearPhotonCache(null); // Note: Original called TryClearPhotonCaches which iterates. This needs adjustment or direct call.
                                                // For now, we'll assume a more direct approach or a similar helper in PhotonUtilities if needed.
                                                // The original TryClearPhotonCaches iterated Object.FindObjectsOfType<PhotonView>()
                                                // This will be refined when PhotonUtilities is fully fleshed out.
                                                // For now, let's replicate the broad idea:
        if (PhotonNetwork.InRoom) // A guard for clearing cache
        {
            foreach (var photonView in Object.FindObjectsOfType<PhotonView>())
            {
                if (photonView != null && photonView.gameObject != null && photonView.gameObject.scene.buildIndex != -1)
                {
                    PhotonUtilities.ClearPhotonCache(photonView);
                }
            }
        }


        orig.Invoke(self, completedLevel, levelFailed, changeLevelType); // Call original method

        // Post level change logic
        bool modLogicActiveInNewScene = GameUtilities.IsModLogicActive();
        if (!modLogicActiveInNewScene)
        {
            LatePlugin.Log.LogInfo($"[RunManagerPatches] Mod logic INACTIVE for new level ('{self.levelCurrent?.name ?? "Unknown"}'). Ensuring lobby is OPEN.");
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null) PhotonNetwork.CurrentRoom.IsOpen = true;
            GameVersionSupport.UnlockSteamLobby(true);
            _shouldOpenLobbyAfterGen = false;
            if (_lobbyUnlockFailsafeCoroutine != null && CoroutineHelper.CoroutineRunner != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                _lobbyUnlockFailsafeCoroutine = null;
            }
        }
        else
        {
            bool allowJoinEventually = ShouldAllowLobbyJoin(self, levelFailed);
            _shouldOpenLobbyAfterGen = allowJoinEventually;
            LatePlugin.Log.LogInfo($"[RunManagerPatches] Mod logic ACTIVE. Lobby should open after gen: {_shouldOpenLobbyAfterGen}");

            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null) PhotonNetwork.CurrentRoom.IsOpen = false;
            GameVersionSupport.LockSteamLobby();
            _normalUnlockLogicExecuted = false;

            if (CoroutineHelper.CoroutineRunner != null)
            {
                if (_lobbyUnlockFailsafeCoroutine != null) CoroutineHelper.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                if (_shouldOpenLobbyAfterGen)
                    _lobbyUnlockFailsafeCoroutine = CoroutineHelper.CoroutineRunner.StartCoroutine(LobbyUnlockFailsafeCoroutine());
                else _lobbyUnlockFailsafeCoroutine = null;
            }
            else LatePlugin.Log.LogError("[RunManagerPatches] Cannot manage failsafe: CoroutineRunner is null!");
        }
    }

    /// <summary>
    /// Failsafe coroutine to unlock the lobby if the normal unlock mechanism fails.
    /// </summary>
    private static IEnumerator LobbyUnlockFailsafeCoroutine()
    {
        const float failsafeDelaySeconds = 30f;
        LatePlugin.Log.LogInfo($"[RunManagerPatches Failsafe] Armed. Will check lobby state in {failsafeDelaySeconds}s.");
        yield return new WaitForSeconds(failsafeDelaySeconds);

        LatePlugin.Log.LogInfo("[RunManagerPatches Failsafe] Timer elapsed. Checking lobby state.");
        if (_normalUnlockLogicExecuted)
        {
            LatePlugin.Log.LogInfo("[RunManagerPatches Failsafe] Normal unlock logic executed. Failsafe action not needed.");
            _lobbyUnlockFailsafeCoroutine = null;
            yield break;
        }

        if (!PhotonUtilities.IsRealMasterClient() || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            LatePlugin.Log.LogWarning("[RunManagerPatches Failsafe] Conditions not met for failsafe unlock. Aborting.");
            _lobbyUnlockFailsafeCoroutine = null;
            yield break;
        }

        // Failsafe only started if _shouldOpenLobbyAfterGen was true.
        // If still closed, force it.
        if (!PhotonNetwork.CurrentRoom.IsOpen)
        {
            LatePlugin.Log.LogWarning("[RunManagerPatches Failsafe] Detected lobby STILL LOCKED. Forcing unlock.");
            PhotonNetwork.CurrentRoom.IsOpen = true;
            GameVersionSupport.UnlockSteamLobby(true);
        }
        else
        {
            LatePlugin.Log.LogInfo("[RunManagerPatches Failsafe] Lobby was found to be already open. No forced action taken.");
        }
        _lobbyUnlockFailsafeCoroutine = null;
    }

    // This method needs to be accessible by GameDirectorPatches.
    // We can make it internal static within this class.
    internal static void SetNormalUnlockLogicExecuted(bool value)
    {
        _normalUnlockLogicExecuted = value;
    }
    internal static bool GetShouldOpenLobbyAfterGen() => _shouldOpenLobbyAfterGen;
    internal static void SetShouldOpenLobbyAfterGen(bool value)
    {
        _shouldOpenLobbyAfterGen = value;
    }
    internal static Coroutine? GetLobbyUnlockFailsafeCoroutine() => _lobbyUnlockFailsafeCoroutine;
    internal static void SetLobbyUnlockFailsafeCoroutine(Coroutine? coroutine)
    {
        _lobbyUnlockFailsafeCoroutine = coroutine;
    }
}