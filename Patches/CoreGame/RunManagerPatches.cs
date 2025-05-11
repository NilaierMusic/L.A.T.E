// File: L.A.T.E/Patches/CoreGame/RunManagerPatches.cs
using System;
using System.Collections;

using HarmonyLib;

using Photon.Pun;

using UnityEngine;                    // Coroutine / Object
using UnityObject = UnityEngine.Object;

using LATE.Config;
using LATE.Core;                      // LatePlugin.Log, CoroutineHelper
using LATE.Managers;
using LATE.Managers.GameState;
using LATE.Patches.Player;            // PlayerAvatarPatches
using LATE.Utilities;                 // GameUtilities, PhotonUtilities

namespace LATE.Patches.CoreGame;

/// <summary>Harmony patches for <see cref="RunManager"/>.</summary>
[HarmonyPatch]
internal static class RunManagerPatches
{
    private const string LogPrefix = "[RunManagerPatches]";

    private static bool _shouldOpenLobbyAfterGen;
    private static bool _normalUnlockLogicExecuted;
    private static Coroutine? _lobbyUnlockFailsafeCoroutine;
    private static bool _initialPublicListingPhaseComplete;

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Scene-specific join rules                                               */
    /*───────────────────────────────────────────────────────────────────────────*/

    private static bool ShouldAllowLobbyJoin(RunManager rm, bool levelFailed)
    {
        Level current = rm.levelCurrent;

        // 1) Arena → always configurable
        if (levelFailed && current == rm.levelArena)
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Prev. level failed, now Arena ⇒ Using Arena cfg.");
            return ConfigManager.AllowInArena.Value;
        }

        // 2) Failure in other levels
        if (levelFailed && current != rm.levelArena)
        {
            if (ConfigManager.LockLobbyOnLevelGenerationFailure.Value)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Level failed (not Arena) & lock-on-fail TRUE ⇒ disallow.");
                return false;
            }

            LatePlugin.Log.LogInfo($"{LogPrefix} Level failed (not Arena) but lock-on-fail FALSE ⇒ continue rules.");
        }

        // 3) Scene-specific allow list
        LatePlugin.Log.LogDebug($"{LogPrefix} Evaluating rules for '{current?.name ?? "NULL"}'.");

        if (current == rm.levelShop && ConfigManager.AllowInShop.Value) return true;
        if (current == rm.levelLobby && ConfigManager.AllowInTruck.Value) return true;
        if (current == rm.levelArena && ConfigManager.AllowInArena.Value) return true;
        if (current == rm.levelLobbyMenu) return true; // Always open
        if (current != null && rm.levels.Contains(current) && ConfigManager.AllowInLevel.Value) return true;

        LatePlugin.Log.LogDebug($"{LogPrefix} No allow condition met – disallow.");
        return false;
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  ChangeLevel HOOK                                                        */
    /*───────────────────────────────────────────────────────────────────────────*/

    public static void RunManager_ChangeLevelHook(
        Action<RunManager, bool, bool, RunManager.ChangeLevelType> orig,
        RunManager self,
        bool completedLevel,
        bool levelFailed,
        RunManager.ChangeLevelType changeType)
    {
        /* --- Clear per-scene state ---------------------------------------- */
        LatePlugin.Log.LogDebug($"{LogPrefix} ChangeLevelHook: resetting trackers.");

        PlayerAvatarPatches.spawnPositionAssigned.Clear();
        PlayerAvatarPatches._reloadHasBeenTriggeredThisScene = false;

        LateJoinManager.ResetSceneTracking();
        DestructionManager.ResetState();
        PlayerStateManager.ResetPlayerStatuses();
        PlayerPositionManager.ResetPositions();

        _normalUnlockLogicExecuted = false; // GameDirectorPatches will flip this to TRUE later.

        /* --- Config sanity-check ------------------------------------------ */
        if (ConfigManager.AllowInShop == null) // simple “config bound?” check
        {
            LatePlugin.Log.LogError($"{LogPrefix} Config not initialised – keeping lobby CLOSED.");
            CloseLobbyHard();
            orig(self, completedLevel, levelFailed, changeType);
            return;
        }

        /* --- Non-host? Just run vanilla & bail --------------------------- */
        if (!PhotonUtilities.IsRealMasterClient())
        {
            orig(self, completedLevel, levelFailed, changeType);
            return;
        }

        LatePlugin.Log.LogInfo(
            $"{LogPrefix} Host changing level | Completed:{completedLevel} Failed:{levelFailed} Type:{changeType} " +
            $"| PreLevel:'{self.levelCurrent?.name ?? "None"}'");

        /* --- Clear Photon cache for current scene objects ---------------- */
        if (PhotonNetwork.InRoom)
        {
            foreach (PhotonView pv in UnityObject.FindObjectsOfType<PhotonView>())
                if (pv != null && pv.gameObject.scene.buildIndex != -1)
                    PhotonUtilities.ClearPhotonCache(pv);
        }

        /* --- Execute vanilla method (updates levelCurrent) --------------- */
        orig(self, completedLevel, levelFailed, changeType);

        /* --- Determine desired lobby state for the NEW scene ------------- */
        bool modLogicActive = GameUtilities.IsModLogicActive();  // now uses updated levelCurrent

        if (!modLogicActive)
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Mod logic INACTIVE for '{self.levelCurrent?.name ?? "Unknown"}' ⇒ lobby OPEN.");
            _shouldOpenLobbyAfterGen = true;
        }
        else
        {
            _shouldOpenLobbyAfterGen = ShouldAllowLobbyJoin(self, levelFailed);
            LatePlugin.Log.LogInfo($"{LogPrefix} Mod logic ACTIVE. Lobby should open: {_shouldOpenLobbyAfterGen}");
        }

        /* --- Tentatively close/lock everything; GameDirector will fix ---- */
        CloseLobbyTentative();

        /* --- Arm or disarm failsafe -------------------------------------- */
        ManageFailsafeCoroutine();
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Failsafe coroutine                                                     */
    /*───────────────────────────────────────────────────────────────────────────*/

    private const float FailsafeDelaySeconds = 30f;

    private static IEnumerator LobbyUnlockFailsafeCoroutine()
    {
        LatePlugin.Log.LogInfo($"{LogPrefix} Failsafe armed. Will check lobby in {FailsafeDelaySeconds}s.");

        yield return new WaitForSeconds(FailsafeDelaySeconds);

        LatePlugin.Log.LogInfo($"{LogPrefix} Failsafe timer elapsed – verifying lobby.");

        if (_normalUnlockLogicExecuted)
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Normal logic already executed – no failsafe action.");
            _lobbyUnlockFailsafeCoroutine = null;
            yield break;
        }

        if (!PhotonUtilities.IsRealMasterClient() || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} Failsafe abort – not master / no room.");
            _lobbyUnlockFailsafeCoroutine = null;
            yield break;
        }

        bool publicPhase = !_initialPublicListingPhaseComplete;
        bool shouldBeVis = ConfigManager.KeepPublicLobbyListed.Value || publicPhase;

        if (!PhotonNetwork.CurrentRoom.IsOpen || PhotonNetwork.CurrentRoom.IsVisible != shouldBeVis)
        {
            LatePlugin.Log.LogWarning(
                $"{LogPrefix} Lobby incorrect (IsOpen:{PhotonNetwork.CurrentRoom.IsOpen}, Visible:{PhotonNetwork.CurrentRoom.IsVisible}, Desired:{shouldBeVis}) – forcing fix.");
            PhotonNetwork.CurrentRoom.IsOpen = true;
            PhotonNetwork.CurrentRoom.IsVisible = shouldBeVis;
            GameVersionSupport.UnlockSteamLobby(shouldBeVis);
        }
        else
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Lobby already correct – no action.");
        }

        _lobbyUnlockFailsafeCoroutine = null;
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Small helpers                                                          */
    /*───────────────────────────────────────────────────────────────────────────*/

    private static void CloseLobbyHard()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
        }
        GameVersionSupport.LockSteamLobby();
    }

    private static void CloseLobbyTentative()
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
            LatePlugin.Log.LogDebug($"{LogPrefix} Tentatively closed Photon room.");
        }
        GameVersionSupport.LockSteamLobby();
        LatePlugin.Log.LogDebug($"{LogPrefix} Tentatively locked Steam lobby.");
    }

    private static void ManageFailsafeCoroutine()
    {
        if (CoroutineHelper.CoroutineRunner == null)
        {
            LatePlugin.Log.LogError($"{LogPrefix} Cannot manage failsafe – CoroutineRunner NULL.");
            return;
        }

        // Stop any existing coroutine
        if (_lobbyUnlockFailsafeCoroutine != null)
        {
            CoroutineHelper.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
            _lobbyUnlockFailsafeCoroutine = null;
            LatePlugin.Log.LogDebug($"{LogPrefix} Stopped existing failsafe coroutine.");
        }

        // Arm if lobby is expected to open
        if (_shouldOpenLobbyAfterGen)
            _lobbyUnlockFailsafeCoroutine = CoroutineHelper.CoroutineRunner.StartCoroutine(LobbyUnlockFailsafeCoroutine());
        else
            LatePlugin.Log.LogDebug($"{LogPrefix} Failsafe not armed – lobby not expected to open.");
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Getters / Setters (internal – used by other patches)                   */
    /*───────────────────────────────────────────────────────────────────────────*/

    internal static void SetNormalUnlockLogicExecuted(bool v) => _normalUnlockLogicExecuted = v;
    internal static bool GetNormalUnlockLogicExecuted() => _normalUnlockLogicExecuted;

    internal static bool GetShouldOpenLobbyAfterGen() => _shouldOpenLobbyAfterGen;
    internal static void SetShouldOpenLobbyAfterGen(bool v) => _shouldOpenLobbyAfterGen = v;

    internal static Coroutine? GetLobbyUnlockFailsafeCoroutine() => _lobbyUnlockFailsafeCoroutine;
    internal static void SetLobbyUnlockFailsafeCoroutine(Coroutine? c) => _lobbyUnlockFailsafeCoroutine = c;

    internal static bool GetInitialPublicListingPhaseComplete() => _initialPublicListingPhaseComplete;
    internal static void SetInitialPublicListingPhaseComplete(bool v)
    {
        if (_initialPublicListingPhaseComplete == v) return;
        _initialPublicListingPhaseComplete = v;
        LatePlugin.Log.LogInfo($"{LogPrefix} _initialPublicListingPhaseComplete set to {v}");
    }
}