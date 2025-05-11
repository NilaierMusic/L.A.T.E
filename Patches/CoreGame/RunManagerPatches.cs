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
using LATE.Patches.Player; // Added for PlayerAvatarPatches access

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
    private static bool _initialPublicListingPhaseComplete = false;

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
        if (currentLevel == runManager.levelLobbyMenu) return true; // LobbyMenu should always be considered for opening
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
        LatePlugin.Log.LogDebug("[RunManagerPatches] ChangeLevelHook: Clearing/Resetting trackers.");

        // Reset scene-specific state from PlayerAvatarPatches
        PlayerAvatarPatches.spawnPositionAssigned.Clear();
        PlayerAvatarPatches._reloadHasBeenTriggeredThisScene = false;
        LatePlugin.Log.LogDebug("[RunManagerPatches] Cleared PlayerAvatarPatches scene state (spawnPositionAssigned, _reloadHasBeenTriggeredThisScene).");

        LateJoinManager.ResetSceneTracking();
        DestructionManager.ResetState();
        PlayerStateManager.ResetPlayerStatuses();
        PlayerPositionManager.ResetPositions();

        _normalUnlockLogicExecuted = false; // Reset this at the start of every level change. GameDirectorPatches will set it to true.

        if (ConfigManager.AllowInShop == null) // A basic check for config readiness
        {
            LatePlugin.Log.LogError("[RunManagerPatches] Config values not bound (AllowInShop is null)! This indicates a potential load order issue or incomplete initialization.");
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                PhotonNetwork.CurrentRoom.IsOpen = false; // Keep lobby closed on critical config error
                PhotonNetwork.CurrentRoom.IsVisible = false;
            }
            GameVersionSupport.LockSteamLobby();
            orig.Invoke(self, completedLevel, levelFailed, changeLevelType); // Still perform level change
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
            foreach (var photonView in UnityObject.FindObjectsOfType<PhotonView>())
            {
                if (photonView != null && photonView.gameObject != null && photonView.gameObject.scene.buildIndex != -1)
                {
                    PhotonUtilities.ClearPhotonCache(photonView);
                }
            }
        }

        orig.Invoke(self, completedLevel, levelFailed, changeLevelType); // This updates self.levelCurrent

        // Post level change logic
        bool modLogicActiveInNewScene = GameUtilities.IsModLogicActive(); // Checks based on NEW self.levelCurrent

        // If mod logic is inactive for the NEW scene (e.g., MainMenu, initial LobbyMenu),
        // the lobby should be made open and public.
        // The _shouldOpenLobbyAfterGen flag will be used by GameDirectorPatches to make the final decision.
        if (!modLogicActiveInNewScene)
        {
            LatePlugin.Log.LogInfo($"[RunManagerPatches] Mod logic INACTIVE for new level ('{self.levelCurrent?.name ?? "Unknown"}'). Flagging for lobby to be OPEN/VISIBLE.");
            _shouldOpenLobbyAfterGen = true; // Mark that GameDirectorPatches should open it
        }
        else // Mod logic IS active for the new scene
        {
            bool allowJoinEventually = ShouldAllowLobbyJoin(self, levelFailed);
            _shouldOpenLobbyAfterGen = allowJoinEventually;
            LatePlugin.Log.LogInfo($"[RunManagerPatches] Mod logic ACTIVE. Lobby should open after gen: {_shouldOpenLobbyAfterGen}");
        }

        // Tentatively close/lock the lobby. GameDirectorPatches will make the final decision.
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false; // Also tentatively hide
            LatePlugin.Log.LogDebug("[RunManagerPatches] Tentatively set Photon Room IsOpen=false, IsVisible=false.");
        }
        GameVersionSupport.LockSteamLobby();
        LatePlugin.Log.LogDebug("[RunManagerPatches] Tentatively locked Steam lobby.");

        // Manage failsafe coroutine
        if (CoroutineHelper.CoroutineRunner != null)
        {
            if (_lobbyUnlockFailsafeCoroutine != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                _lobbyUnlockFailsafeCoroutine = null;
                LatePlugin.Log.LogDebug("[RunManagerPatches] Stopped existing failsafe coroutine.");
            }

            // Only arm the failsafe if GameDirectorPatches is *expected* to open the lobby.
            if (_shouldOpenLobbyAfterGen)
            {
                _lobbyUnlockFailsafeCoroutine = CoroutineHelper.CoroutineRunner.StartCoroutine(LobbyUnlockFailsafeCoroutine());
            }
            else
            {
                LatePlugin.Log.LogDebug("[RunManagerPatches] Failsafe not armed as lobby is not expected to open.");
            }
        }
        else LatePlugin.Log.LogError("[RunManagerPatches] Cannot manage failsafe: CoroutineRunner is null!");
    }

    private static IEnumerator LobbyUnlockFailsafeCoroutine()
    {
        const float failsafeDelaySeconds = 30f; // Increased for safety
        LatePlugin.Log.LogInfo($"[RunManagerPatches Failsafe] Armed. Will check lobby state in {failsafeDelaySeconds}s.");
        yield return new WaitForSeconds(failsafeDelaySeconds);

        LatePlugin.Log.LogInfo("[RunManagerPatches Failsafe] Timer elapsed. Checking lobby state.");
        if (GetNormalUnlockLogicExecuted()) // Use getter
        {
            LatePlugin.Log.LogInfo("[RunManagerPatches Failsafe] Normal unlock/lock logic already executed. Failsafe action not needed.");
            _lobbyUnlockFailsafeCoroutine = null;
            yield break;
        }

        if (!PhotonUtilities.IsRealMasterClient() || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            LatePlugin.Log.LogWarning("[RunManagerPatches Failsafe] Conditions not met for failsafe action (Not master, not in room, or room null). Aborting.");
            _lobbyUnlockFailsafeCoroutine = null;
            yield break;
        }

        // Determine desired visibility based on config and initial phase state, IF _shouldOpenLobbyAfterGen was true
        // This failsafe primarily ensures the lobby becomes Open if it was supposed to. Visibility is secondary here but good to set.
        bool isCurrentlyPublicPhase = !GetInitialPublicListingPhaseComplete();
        bool makeVisibleAndPublic = ConfigManager.KeepPublicLobbyListed.Value || isCurrentlyPublicPhase;

        // If the failsafe triggers, it means GameDirector_SetStart_Postfix didn't run or didn't open the lobby as expected.
        // We *assume* if the failsafe is running, it means the lobby *should have been opened* by GameDirectorPatches.
        if (!PhotonNetwork.CurrentRoom.IsOpen || PhotonNetwork.CurrentRoom.IsVisible != makeVisibleAndPublic)
        {
            LatePlugin.Log.LogWarning(
                $"[RunManagerPatches Failsafe] Detected lobby state incorrect or normal logic not run " +
                $"(IsOpen: {PhotonNetwork.CurrentRoom.IsOpen}, IsVisible: {PhotonNetwork.CurrentRoom.IsVisible}, DesiredVisible: {makeVisibleAndPublic}). " +
                $"Forcing Open/Visible status according to config."
            );
            PhotonNetwork.CurrentRoom.IsOpen = true;
            PhotonNetwork.CurrentRoom.IsVisible = makeVisibleAndPublic;
            GameVersionSupport.UnlockSteamLobby(makeVisibleAndPublic);
        }
        else
        {
            LatePlugin.Log.LogInfo("[RunManagerPatches Failsafe] Lobby state was already correct (or normal logic ran). No forced action taken by failsafe.");
        }
        _lobbyUnlockFailsafeCoroutine = null;
    }

    // --- Getters/Setters for shared state ---
    internal static void SetNormalUnlockLogicExecuted(bool value) => _normalUnlockLogicExecuted = value;
    internal static bool GetNormalUnlockLogicExecuted() => _normalUnlockLogicExecuted;

    internal static bool GetShouldOpenLobbyAfterGen() => _shouldOpenLobbyAfterGen;
    internal static void SetShouldOpenLobbyAfterGen(bool value) => _shouldOpenLobbyAfterGen = value;

    internal static Coroutine? GetLobbyUnlockFailsafeCoroutine() => _lobbyUnlockFailsafeCoroutine;
    internal static void SetLobbyUnlockFailsafeCoroutine(Coroutine? coroutine) => _lobbyUnlockFailsafeCoroutine = coroutine;

    internal static bool GetInitialPublicListingPhaseComplete() => _initialPublicListingPhaseComplete;
    internal static void SetInitialPublicListingPhaseComplete(bool value)
    {
        if (_initialPublicListingPhaseComplete != value)
        {
            LatePlugin.Log.LogInfo($"[RunManagerPatches] _initialPublicListingPhaseComplete set to {value}");
            _initialPublicListingPhaseComplete = value;
        }
    }
}