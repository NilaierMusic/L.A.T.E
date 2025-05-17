// File: L.A.T.E/Patches/CoreGame/RunManagerPatches.cs
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using LATE.Config;
using LATE.Core;
using LATE.DataModels;
using LATE.Managers;
using LATE.Managers.GameState;
using LATE.Patches.Player;
using LATE.Utilities;

namespace LATE.Patches.CoreGame
{
    [HarmonyPatch]
    internal static class RunManagerPatches
    {
        private const string LogPrefix = "[RunManagerPatches]";

        // Flags for lobby state management across hooks
        private static bool _shouldOpenLobbyAfterGen;
        private static bool _normalUnlockLogicExecuted;
        private static Coroutine? _lobbyUnlockFailsafeCoroutine;
        private static bool _initialPublicListingPhaseComplete;

        // Flags and data for cache cleanup and ChangeLevelHook synchronization
        private static readonly List<StalePhotonViewData> _viewsToCleanFromPreviousLevel = new List<StalePhotonViewData>();
        private static bool _isCacheCleaningInProgress = false; // Sole flag for "is cleanup running"
        private static Coroutine? _activeCacheCleanupCoroutineRef = null; // To store the actual coroutine if we need to stop it
        private static bool _origChangeLevelHookExecutionCompleted = false;
        private static bool _finalLobbyStateShouldBeOpen = true;

        /* ... (ShouldAllowLobbyJoin remains the same) ... */
        private static bool ShouldAllowLobbyJoin(RunManager rm, bool levelFailed)
        {
            Level current = rm.levelCurrent;
            if (current == null)
            {
                LatePlugin.Log.LogWarning($"{LogPrefix} ShouldAllowLobbyJoin: RunManager's current level is null. Defaulting to false (lobby closed).");
                return false;
            }

            if (levelFailed && current == rm.levelArena)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Previous level failed, now Arena. Using Arena config: {ConfigManager.General.AllowInArena.Value}");
                return ConfigManager.General.AllowInArena.Value;
            }

            if (levelFailed && current != rm.levelArena)
            {
                if (ConfigManager.LateJoin.LockLobbyOnLevelGenFail.Value)
                {
                    LatePlugin.Log.LogInfo($"{LogPrefix} Level failed (not Arena) & LockLobbyOnLevelGenFail is TRUE. Lobby will be CLOSED.");
                    return false;
                }
                LatePlugin.Log.LogInfo($"{LogPrefix} Level failed (not Arena) but LockLobbyOnLevelGenFail is FALSE. Proceeding with normal rules.");
            }

            LatePlugin.Log.LogDebug($"{LogPrefix} Evaluating lobby join rules for level: '{current.name}'");

            if (current == rm.levelShop && ConfigManager.General.AllowInShop.Value) return true;
            if (current == rm.levelLobby && ConfigManager.General.AllowInTruck.Value) return true;
            if (current == rm.levelArena && ConfigManager.General.AllowInArena.Value) return true;
            if (current == rm.levelLobbyMenu) return true;
            if (rm.levels != null && rm.levels.Contains(current) && ConfigManager.General.AllowInLevel.Value) return true;

            LatePlugin.Log.LogDebug($"{LogPrefix} No specific rule allows joining for '{current.name}'. Lobby will be CLOSED by L.A.T.E. logic.");
            return false;
        }


        public static void RunManager_ChangeLevelHook(
            Action<RunManager, bool, bool, RunManager.ChangeLevelType> orig,
            RunManager self,
            bool completedLevel,
            bool levelFailed,
            RunManager.ChangeLevelType changeType)
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} ChangeLevelHook: Resetting local trackers and scene state.");
            PlayerAvatarPatches.spawnPositionAssigned.Clear();
            PlayerAvatarPatches._reloadHasBeenTriggeredThisScene = false;
            LateJoinManager.ResetSceneTracking();
            DestructionManager.ResetState();
            PlayerStateManager.ResetPlayerSessionStates();
            PlayerPositionManager.ResetPositions();
            VoiceManager.ResetPerSceneStates();
            _normalUnlockLogicExecuted = false;
            CoroutineHelper.ClearCoroutineRunnerCache();

            if (ConfigManager.General.AllowInShop == null)
            {
                LatePlugin.Log.LogError($"{LogPrefix} Config not initialised. Keeping lobby CLOSED and running original ChangeLevel.");
                CloseLobbyHard();
                orig(self, completedLevel, levelFailed, changeType);
                return;
            }

            if (!PhotonUtilities.IsRealMasterClient())
            {
                LatePlugin.Log.LogDebug($"{LogPrefix} Not MasterClient. Running original ChangeLevel and returning.");
                orig(self, completedLevel, levelFailed, changeType);
                return;
            }

            LatePlugin.Log.LogInfo(
                $"{LogPrefix} Host changing level | Completed:{completedLevel} Failed:{levelFailed} Type:{changeType} " +
                $"| From Level:'{self.levelCurrent?.name ?? "None"}'");

            if (PhotonNetwork.InRoom) // Check InRoom as RemoveBufferedRPCs requires it.
            {
                try
                {
                    object? runManagerPUNObj = ReflectionCache.RunManager_RunManagerPUNField?.GetValue(self);
                    if (runManagerPUNObj is MonoBehaviour punMonoBehaviour)
                    {
                        PhotonView? punPV = ReflectionCache.RunManagerPUN_PhotonViewField?.GetValue(punMonoBehaviour) as PhotonView;
                        if (punPV != null)
                        {
                            LatePlugin.Log.LogInfo($"{LogPrefix} Clearing buffered RPCs for RunManagerPUN (ViewID: {punPV.ViewID}).");
                            PhotonNetwork.RemoveBufferedRPCs(punPV.ViewID);
                        }
                        else { LatePlugin.Log.LogWarning($"{LogPrefix} RunManagerPUN's PhotonView is null via reflection."); }
                    }
                    else { LatePlugin.Log.LogWarning($"{LogPrefix} RunManagerPUN object is null or not a MonoBehaviour via reflection."); }
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogError($"{LogPrefix} Error clearing RPCs for RunManagerPUN: {ex}");
                }
            }

            _origChangeLevelHookExecutionCompleted = false;

            if (_isCacheCleaningInProgress) // Check our own flag
            {
                LatePlugin.Log.LogWarning($"{LogPrefix} A previous cache cleanup was still marked in progress by our flag. Attempting to stop it.");
                if (_activeCacheCleanupCoroutineRef != null && CoroutineHelper.CoroutineRunner != null)
                {
                    CoroutineHelper.CoroutineRunner.StopCoroutine(_activeCacheCleanupCoroutineRef);
                    LatePlugin.Log.LogInfo($"{LogPrefix} Existing cache cleanup coroutine stopped.");
                }
                else if (_activeCacheCleanupCoroutineRef != null)
                {
                    LatePlugin.Log.LogWarning($"{LogPrefix} CoroutineRunner is null, cannot stop existing cache cleanup coroutine explicitly.");
                }
                _activeCacheCleanupCoroutineRef = null;
                _isCacheCleaningInProgress = false; // Reset our flag since we "stopped" it or it was orphaned.
            }
            _viewsToCleanFromPreviousLevel.Clear();

            if (PhotonNetwork.InRoom)
            {
                // ... (collect _viewsToCleanFromPreviousLevel - same as before) ...
                LatePlugin.Log.LogDebug($"{LogPrefix} Collecting PhotonViews from current level ('{self.levelCurrent?.name ?? "None"}') for cleanup.");
                foreach (PhotonView pv_loop in UnityEngine.Object.FindObjectsOfType<PhotonView>(true))
                {
                    if (pv_loop != null && pv_loop.gameObject != null &&
                        pv_loop.gameObject.scene.buildIndex != -1 && pv_loop.InstantiationId > 0)
                    {
                        _viewsToCleanFromPreviousLevel.Add(new StalePhotonViewData(pv_loop.InstantiationId, pv_loop.ViewID));
                    }
                }
                LatePlugin.Log.LogInfo($"{LogPrefix} Collected {_viewsToCleanFromPreviousLevel.Count} PhotonView IDs for cleanup.");
            }

            Action onCleanupComplete = () => {
                if (!_isCacheCleaningInProgress && _activeCacheCleanupCoroutineRef == null && !_origChangeLevelHookExecutionCompleted)
                { // Added a more specific check
                    // This condition (_origChangeLevelHookExecutionCompleted being false) suggests we are still early in the ChangeLevelHook.
                    // If cleanup is already marked as "not in progress", this is a sign of premature/duplicate call.
                    LatePlugin.Log.LogWarning($"{LogPrefix} onCleanupComplete invoked, but state indicates cleanup already finished or wasn't properly started for this hook instance. Ignoring duplicate call.");
                    return;
                }
                LatePlugin.Log.LogInfo($"{LogPrefix} Background cache cleanup COMPLETED (onCleanupComplete lambda in RunManagerPatches executing. Current _isCacheCleaningInProgress: {_isCacheCleaningInProgress}).");
                _isCacheCleaningInProgress = false;
                _activeCacheCleanupCoroutineRef = null;
                FinalizeLobbyStateIfReady();
            };

            if (_viewsToCleanFromPreviousLevel.Count > 0)
            {
                // Set the flag *before* attempting to start.
                _isCacheCleaningInProgress = true;
                _activeCacheCleanupCoroutineRef = null; // Ensure it's null before assignment attempt

                LatePlugin.Log.LogInfo($"{LogPrefix} Attempting to start background cache cleanup for {_viewsToCleanFromPreviousLevel.Count} views.");

                Coroutine? startedCoroutine = CacheCleanupManager.StartThrottledCleanupAndGetCoroutine(
                    new List<StalePhotonViewData>(_viewsToCleanFromPreviousLevel),
                    onCleanupComplete
                );

                if (startedCoroutine != null)
                {
                    _activeCacheCleanupCoroutineRef = startedCoroutine; // Store if successfully started
                    LatePlugin.Log.LogDebug($"{LogPrefix} Cache cleanup coroutine successfully started and reference stored.");
                }
                else
                {
                    // If startedCoroutine is null, CacheCleanupManager should have already called onCleanupComplete.
                    // This would set _isCacheCleaningInProgress to false.
                    LatePlugin.Log.LogWarning($"{LogPrefix} CacheCleanupManager reported that coroutine failed to start. onCleanupComplete should have been invoked by CacheManager.");
                    if (_isCacheCleaningInProgress)
                    {
                        // This is a fallback for an inconsistent state.
                        LatePlugin.Log.LogError($"{LogPrefix} Inconsistent State: CacheManager failed to start coroutine but _isCacheCleaningInProgress is still true. Forcing onCleanupComplete.");
                        onCleanupComplete(); // Call it to try and recover state.
                    }
                }
            }
            else
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} No PhotonViews needed cleanup from previous level. Invoking onCleanupComplete directly.");
                _isCacheCleaningInProgress = false;
                _activeCacheCleanupCoroutineRef = null;
                onCleanupComplete();
            }
            _viewsToCleanFromPreviousLevel.Clear();


            CloseLobbyTentative();
            LatePlugin.Log.LogDebug($"{LogPrefix} Lobby tentatively closed before original ChangeLevel call.");

            orig(self, completedLevel, levelFailed, changeType);
            LatePlugin.Log.LogInfo($"{LogPrefix} Original ChangeLevel executed. New current level: '{self.levelCurrent?.name ?? "Unknown"}'");

            _origChangeLevelHookExecutionCompleted = true;

            bool modLogicActiveCurrentScene = GameUtilities.IsModLogicActive();

            if (!modLogicActiveCurrentScene)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Mod logic INACTIVE for new level '{self.levelCurrent?.name ?? "Unknown"}'. Lobby intended to be OPEN.");
                _finalLobbyStateShouldBeOpen = true;
            }
            else
            {
                _finalLobbyStateShouldBeOpen = ShouldAllowLobbyJoin(self, levelFailed);
                LatePlugin.Log.LogInfo($"{LogPrefix} Mod logic ACTIVE for new level '{self.levelCurrent?.name ?? "Unknown"}'. Lobby intended to be: {(_finalLobbyStateShouldBeOpen ? "OPEN" : "CLOSED")}");
            }

            SetShouldOpenLobbyAfterGen(_finalLobbyStateShouldBeOpen);
            LatePlugin.Log.LogDebug($"{LogPrefix} Set _shouldOpenLobbyAfterGen to: {_shouldOpenLobbyAfterGen} for GameDirector.SetStart hook.");

            FinalizeLobbyStateIfReady();
        }

        /* ... (FinalizeLobbyStateIfReady, Failsafe, Lobby helpers, Getters/Setters remain the same as previous correct version) ... */
        private static void FinalizeLobbyStateIfReady()
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} Attempting to finalize lobby state. CacheCleaningInProgress: {_isCacheCleaningInProgress}, OrigHookExecutionCompleted: {_origChangeLevelHookExecutionCompleted}");

            if (!_isCacheCleaningInProgress && _origChangeLevelHookExecutionCompleted)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Both cleanup and level change logic complete. Setting final lobby state.");
                if (_finalLobbyStateShouldBeOpen)
                {
                    LatePlugin.Log.LogInfo($"{LogPrefix} Finalizing: Opening lobby as per new level rules.");
                    OpenLobby();
                }
                else
                {
                    LatePlugin.Log.LogInfo($"{LogPrefix} Finalizing: Keeping lobby closed as per new level rules.");
                    CloseLobbyHard();
                }
                ManageFailsafeCoroutine();
            }
            else if (_origChangeLevelHookExecutionCompleted)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Level change logic complete, but cache cleanup is still in progress. Lobby remains tentatively closed.");
            }
            else if (!_isCacheCleaningInProgress)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Cache cleanup complete, but level change logic (orig call) is still in progress. Waiting for orig call to complete.");
            }
        }

        private const float FailsafeDelaySeconds = 30f;
        private static IEnumerator LobbyUnlockFailsafeCoroutine()
        {
            LatePlugin.Log.LogInfo($"{LogPrefix} Failsafe armed. Will check lobby in {FailsafeDelaySeconds}s.");
            yield return new WaitForSeconds(FailsafeDelaySeconds);

            LatePlugin.Log.LogInfo($"{LogPrefix} Failsafe timer elapsed – verifying lobby.");
            if (_normalUnlockLogicExecuted)
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Failsafe: Normal logic already executed. No action.");
                _lobbyUnlockFailsafeCoroutine = null;
                yield break;
            }
            if (!PhotonUtilities.IsRealMasterClient() || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            {
                LatePlugin.Log.LogWarning($"{LogPrefix} Failsafe: Aborting - not MasterClient or not in a room.");
                _lobbyUnlockFailsafeCoroutine = null;
                yield break;
            }

            bool publicPhaseInProgress = !GetInitialPublicListingPhaseComplete();
            bool desiredVisibility = ConfigManager.Lobby.KeepPublicListed.Value || publicPhaseInProgress;

            if (!PhotonNetwork.CurrentRoom.IsOpen || PhotonNetwork.CurrentRoom.IsVisible != desiredVisibility)
            {
                LatePlugin.Log.LogWarning(
                    $"{LogPrefix} Failsafe: Lobby state incorrect (IsOpen:{PhotonNetwork.CurrentRoom.IsOpen}, IsVisible:{PhotonNetwork.CurrentRoom.IsVisible}, DesiredVisible:{desiredVisibility}). Forcing fix.");
                PhotonNetwork.CurrentRoom.IsOpen = true;
                PhotonNetwork.CurrentRoom.IsVisible = desiredVisibility;
                GameVersionSupport.UnlockSteamLobby(desiredVisibility);
            }
            else
            {
                LatePlugin.Log.LogInfo($"{LogPrefix} Failsafe: Lobby state already correct. No action.");
            }
            _lobbyUnlockFailsafeCoroutine = null;
        }

        private static void OpenLobby()
        {
            if (PhotonNetwork.InRoom && PhotonUtilities.IsRealMasterClient())
            {
                PhotonNetwork.CurrentRoom.IsOpen = true;
                bool publicPhaseInProgress = !GetInitialPublicListingPhaseComplete();
                bool makeVisible = ConfigManager.Lobby.KeepPublicListed.Value || publicPhaseInProgress;
                PhotonNetwork.CurrentRoom.IsVisible = makeVisible;
                LatePlugin.Log.LogInfo($"{LogPrefix} Lobby OPENED. IsOpen=true, IsVisible={makeVisible}.");
                GameVersionSupport.UnlockSteamLobby(makeVisible);
            }
            else
            {
                LatePlugin.Log.LogWarning($"{LogPrefix} Attempted OpenLobby but not in room or not master client.");
            }
        }

        private static void CloseLobbyHard()
        {
            if (PhotonNetwork.InRoom && PhotonUtilities.IsRealMasterClient())
            {
                PhotonNetwork.CurrentRoom.IsOpen = false;
                PhotonNetwork.CurrentRoom.IsVisible = false;
                LatePlugin.Log.LogInfo($"{LogPrefix} Lobby HARD CLOSED. IsOpen=false, IsVisible=false.");
            }
            GameVersionSupport.LockSteamLobby();
        }

        private static void CloseLobbyTentative()
        {
            if (PhotonNetwork.InRoom && PhotonUtilities.IsRealMasterClient())
            {
                PhotonNetwork.CurrentRoom.IsOpen = false;
                PhotonNetwork.CurrentRoom.IsVisible = false;
                LatePlugin.Log.LogDebug($"{LogPrefix} Tentatively closed Photon room (IsOpen=false, IsVisible=false).");
            }
            GameVersionSupport.LockSteamLobby();
            LatePlugin.Log.LogDebug($"{LogPrefix} Tentatively locked Steam lobby.");
        }

        private static void ManageFailsafeCoroutine()
        {
            if (CoroutineHelper.CoroutineRunner == null)
            {
                LatePlugin.Log.LogError($"{LogPrefix} Cannot manage failsafe: CoroutineRunner is NULL.");
                return;
            }
            if (_lobbyUnlockFailsafeCoroutine != null)
            {
                CoroutineHelper.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                _lobbyUnlockFailsafeCoroutine = null;
                LatePlugin.Log.LogDebug($"{LogPrefix} Stopped existing failsafe coroutine.");
            }

            if (_finalLobbyStateShouldBeOpen)
            {
                _lobbyUnlockFailsafeCoroutine = CoroutineHelper.CoroutineRunner.StartCoroutine(LobbyUnlockFailsafeCoroutine());
            }
            else
            {
                LatePlugin.Log.LogDebug($"{LogPrefix} Failsafe not armed: lobby is intended to be closed (_finalLobbyStateShouldBeOpen is false).");
            }
        }

        internal static void SetNormalUnlockLogicExecuted(bool v) => _normalUnlockLogicExecuted = v;
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
}