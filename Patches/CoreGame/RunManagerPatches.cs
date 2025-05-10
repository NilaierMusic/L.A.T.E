// File: L.A.T.E/Patches/CoreGame/RunManagerPatches.cs
using System;
using System.Collections;
using HarmonyLib;
using Photon.Pun;
using UnityEngine; // Required for Coroutine, MonoBehaviour, Object
using LATE.Config;
using LATE.Core;
using LATE.Managers;
using LATE.Managers.GameState;
using LATE.Utilities;

// Explicitly using UnityEngine.Object to avoid ambiguity with System.Object
using UnityObject = UnityEngine.Object;


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
        if (currentLevel == runManager.levelLobbyMenu) return true;
        if (currentLevel != null && runManager.levels.Contains(currentLevel) && ConfigManager.AllowInLevel.Value) return true;

        LatePlugin.Log.LogDebug($"[RunManagerPatches] No applicable allow condition met for level '{currentLevel?.name ?? "NULL"}'. Disallowing join.");
        return false;
    }

    public static void RunManager_ChangeLevelHook(
        Action<RunManager, bool, bool, RunManager.ChangeLevelType> orig,
        RunManager self,
        bool completedLevel,
        bool levelFailed,
        RunManager.ChangeLevelType changeLevelType)
    {
        LatePlugin.Log.LogDebug("[RunManagerPatches] ChangeLevelHook: Clearing trackers.");
        // Patches.spawnPositionAssigned.Clear(); // This state belongs to PlayerAvatarPatches
        LateJoinManager.ResetSceneTracking();
        DestructionManager.ResetState();
        PlayerStateManager.ResetPlayerStatuses();
        PlayerPositionManager.ResetPositions();
        // Patches._reloadHasBeenTriggeredThisScene = false; // This state belongs to PlayerAvatarPatches

        _shouldOpenLobbyAfterGen = false;

        if (ConfigManager.AllowInShop == null)
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

        // Clear Photon cache for scene objects before level change
        if (PhotonNetwork.InRoom)
        {
            // Corrected: Use UnityObject alias for UnityEngine.Object
            foreach (var photonView in UnityObject.FindObjectsOfType<PhotonView>())
            {
                // Ensure PV is valid and part of a scene (not an asset or DontDestroyOnLoad without scene context)
                if (photonView != null && photonView.gameObject != null && photonView.gameObject.scene.buildIndex != -1)
                {
                    PhotonUtilities.ClearPhotonCache(photonView);
                }
            }
        }

        orig.Invoke(self, completedLevel, levelFailed, changeLevelType);

        bool modLogicActiveInNewScene = GameUtilities.IsModLogicActive();
        if (!modLogicActiveInNewScene)
        {
            LatePlugin.Log.LogInfo($"[RunManagerPatches] Mod logic INACTIVE for new level ('{self.levelCurrent?.name ?? "Unknown"}'). Ensuring lobby is OPEN.");
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null) PhotonNetwork.CurrentRoom.IsOpen = true;
            GameVersionSupport.UnlockSteamLobby(true);
            _shouldOpenLobbyAfterGen = false; // Ensure this is reset
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

        if (!PhotonNetwork.CurrentRoom.IsOpen) // Only act if it's still closed
        {
            LatePlugin.Log.LogWarning("[RunManagerPatches Failsafe] Detected lobby STILL LOCKED. Forcing unlock.");
            PhotonNetwork.CurrentRoom.IsOpen = true;
            GameVersionSupport.UnlockSteamLobby(true); // Ensure Steam lobby is also unlocked
        }
        else
        {
            LatePlugin.Log.LogInfo("[RunManagerPatches Failsafe] Lobby was found to be already open. No forced action taken.");
        }
        _lobbyUnlockFailsafeCoroutine = null;
    }

    // Getters/Setters for state shared with GameDirectorPatches
    internal static void SetNormalUnlockLogicExecuted(bool value) => _normalUnlockLogicExecuted = value;
    internal static bool GetShouldOpenLobbyAfterGen() => _shouldOpenLobbyAfterGen;
    internal static void SetShouldOpenLobbyAfterGen(bool value) => _shouldOpenLobbyAfterGen = value;
    internal static Coroutine? GetLobbyUnlockFailsafeCoroutine() => _lobbyUnlockFailsafeCoroutine;
    internal static void SetLobbyUnlockFailsafeCoroutine(Coroutine? coroutine) => _lobbyUnlockFailsafeCoroutine = coroutine;
}