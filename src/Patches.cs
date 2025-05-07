using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace L.A.T.E
{
    [HarmonyPatch]
    internal static class Patches
    {
        #region Private Fields

        private static readonly System.Random rng = new System.Random();
        private static readonly HashSet<int> spawnPositionAssigned = new HashSet<int>();

        private static bool _reloadHasBeenTriggeredThisScene = false;

        private static bool _shouldOpenLobbyAfterGen = false;

        private static Coroutine? _lobbyUnlockFailsafeCoroutine;
        private static bool _normalUnlockLogicExecuted = false;

        #endregion

        #region Utility Methods

        /// <summary>
        /// Randomly shuffles the elements of a list using the Fisher-Yates algorithm.
        /// </summary>
        public static void Shuffle<T>(this IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Clears the Photon cache for all scene objects.
        /// </summary>
        private static void TryClearPhotonCaches()
        {
            LateJoinEntry.Log.LogInfo("[Clear Cache] Clearing Photon cache for scene objects.");
            try
            {
                foreach (var photonView in Object.FindObjectsOfType<PhotonView>())
                {
                    if (
                        photonView == null
                        || photonView.gameObject == null
                        || photonView.gameObject.scene.buildIndex == -1
                    )
                        continue;
                    Utilities.ClearPhotonCache(photonView);
                }
                LateJoinEntry.Log.LogInfo("[Clear Cache] Finished clearing scene object cache.");
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError($"Error clearing Photon cache: {ex}");
            }
        }

        /// <summary>
        /// Determines if the lobby should allow players to join based on the current game state and configuration.
        /// NOTE: This should be called AFTER the level change has occurred (RunManager.levelCurrent reflects the NEW level).
        /// </summary>
        private static bool ShouldAllowLobbyJoin(RunManager runManager, bool levelFailed)
        {
            Level currentLevel = runManager.levelCurrent; // Get the level AFTER the change

            // --- Arena Exception Logic (Handles true failures leading to Arena) ---
            if (levelFailed && currentLevel == runManager.levelArena)
            {
                LateJoinEntry.Log.LogInfo(
                   "[ShouldAllowLobbyJoin] Previous level failed, current level IS Arena. Allowing join based on Arena config."
               );
                // Allow join ONLY if the Arena config setting is true.
                return ConfigManager.AllowInArena.Value;
            }
            // --- End Arena Exception ---

            // --- Handle Non-Arena Failures with Config Option ---
            // This block executes if levelFailed is true BUT we didn't go to Arena (e.g., modded level "failed" but game continues)
            if (levelFailed && currentLevel != runManager.levelArena) // Explicitly check not Arena here
            {
                if (!ConfigManager.LockLobbyOnLevelGenerationFailure.Value) // Check the new config option
                {
                    LateJoinEntry.Log.LogInfo(
                       $"[ShouldAllowLobbyJoin] Level reported failure (current: '{currentLevel?.name ?? "NULL"}', not Arena), but 'LockLobbyOnLevelGenerationFailure' is FALSE. " +
                       "Proceeding to check scene-specific join rules as if no failure occurred."
                   );
                    // By not returning false here, we fall through to the normal scene checks below.
                }
                else // Config says TO lock on failure (default behavior)
                {
                    LateJoinEntry.Log.LogInfo(
                       $"[ShouldAllowLobbyJoin] Level reported failure (current: '{currentLevel?.name ?? "NULL"}', not Arena) and 'LockLobbyOnLevelGenerationFailure' is TRUE. Disallowing join."
                   );
                    return false; // Disallow join
                }
            }
            // --- End Non-Arena Failures ---


            // If level did NOT fail (levelFailed was initially false)
            // OR if levelFailed was true but LockLobbyOnLevelGenerationFailure is false (and not Arena),
            // proceed with normal config checks based on the NEW level:
            LateJoinEntry.Log.LogDebug(
                $"[ShouldAllowLobbyJoin] Evaluating scene-specific join rules for current level '{currentLevel?.name ?? "NULL"}' (levelFailed considered as per config: {levelFailed && (currentLevel == runManager.levelArena || ConfigManager.LockLobbyOnLevelGenerationFailure.Value)})."
            );

            // Direct comparisons using RunManager level references
            if (currentLevel == runManager.levelShop && ConfigManager.AllowInShop.Value)
            {
                LateJoinEntry.Log.LogDebug(
                    "[ShouldAllowLobbyJoin] Allowing join: In Shop & Config allows."
                );
                return true;
            }
            if (currentLevel == runManager.levelLobby && ConfigManager.AllowInTruck.Value) // Truck/Lobby
            {
                LateJoinEntry.Log.LogDebug(
                    "[ShouldAllowLobbyJoin] Allowing join: In Truck/Lobby & Config allows."
                );
                return true;
            }
            // This now correctly handles non-failure case for Arena, OR if failure occurred but config allows proceeding
            if (currentLevel == runManager.levelArena && ConfigManager.AllowInArena.Value)
            {
                LateJoinEntry.Log.LogDebug(
                    "[ShouldAllowLobbyJoin] Allowing join: In Arena & Config allows."
                );
                return true;
            }
            if (currentLevel == runManager.levelLobbyMenu) // Always allow in the pre-game lobby menu
            {
                LateJoinEntry.Log.LogDebug("[ShouldAllowLobbyJoin] Allowing join: In LobbyMenu.");
                return true;
            }

            // Check if it's a "normal" run level by seeing if it's in the RunManager's list of levels
            if (currentLevel != null && runManager.levels.Contains(currentLevel) && ConfigManager.AllowInLevel.Value)
            {
                LateJoinEntry.Log.LogDebug(
                   "[ShouldAllowLobbyJoin] Allowing join: In a standard Level & Config allows."
               );
                return true;
            }

            // Default: Disallow join if no specific rule allows it
            LateJoinEntry.Log.LogDebug(
                $"[ShouldAllowLobbyJoin] No applicable allow condition met for level '{currentLevel?.name ?? "NULL"}'. Disallowing join."
            );
            return false;
        }

        #endregion

        #region RunManager Level Change Hook

        /// <summary>
        /// Hook for level changes. Resets trackers, validates configuration, clears caches, and adjusts lobby state DELAYED until level generation is complete.
        /// </summary>
        public static void RunManager_ChangeLevelHook(
            Action<RunManager, bool, bool, RunManager.ChangeLevelType> orig,
            RunManager self,
            bool completedLevel,
            bool levelFailed,
            RunManager.ChangeLevelType changeLevelType
        )
        {
            #region Reset State and Tracking
            LateJoinEntry.Log.LogDebug("[State Reset] Clearing trackers for new level.");
            spawnPositionAssigned.Clear();
            LateJoinManager.ResetSceneTracking();
            DestructionManager.ResetState();
            PlayerStateManager.ResetPlayerStatuses();
            PlayerPositionManager.ResetPositions();
            _reloadHasBeenTriggeredThisScene = false;
            LateJoinEntry.Log.LogDebug("[State Reset] Reset scene reload trigger flag.");

            _shouldOpenLobbyAfterGen = false;
            LateJoinEntry.Log.LogDebug("[State Reset] Reset lobby-open-after-gen flag.");
            #endregion

            #region Validate Configuration
            if (
                ConfigManager.AllowInShop == null
                || ConfigManager.AllowInTruck == null
                || ConfigManager.AllowInLevel == null
                || ConfigManager.AllowInArena == null
            )
            {
                LateJoinEntry.Log.LogError("[MOD Resync] Config values not bound!");
                if (PhotonNetwork.InRoom)
                    PhotonNetwork.CurrentRoom.IsOpen = false; // Keep lobby closed on config error
                orig.Invoke(self, completedLevel, levelFailed, changeLevelType); // Still perform level change
                return;
            }
            #endregion

            // Only MasterClient should manage lobby state and sync logic
            if (!Utilities.IsRealMasterClient())
            {
                // Non-hosts just execute the original level change
                orig.Invoke(self, completedLevel, levelFailed, changeLevelType);
                return;
            }

            // Log level change intent
            LateJoinEntry.Log.LogInfo(
                $"[MOD Resync] Host changing level (Completed: {completedLevel}, Failed: {levelFailed}, Type: {changeLevelType}). Current Level Pre-Change: '{self.levelCurrent?.name ?? "None"}'"
            );

            // --- Log pre-change state for debugging ---
            Level? preChangeCurrentLevel = self.levelCurrent;
            Level? preChangeLobbyLevel = self.levelLobby;
            LateJoinEntry.Log.LogDebug(
                $"[MOD Resync Debug Pre] CurrentLevel: Name='{preChangeCurrentLevel?.name ?? "NULL"}'"
            );
            LateJoinEntry.Log.LogDebug(
                $"[MOD Resync Debug Pre] LobbyLevel:   Name='{preChangeLobbyLevel?.name ?? "NULL"}'"
            );

            #region Clear Photon Cache (if needed)
            // Clear cache BEFORE the original method changes the scene context
            TryClearPhotonCaches();
            #endregion

            orig.Invoke(self, completedLevel, levelFailed, changeLevelType);

            #region Adjust Lobby Openness (Post Level Change)

            // --- Log post-change state for debugging ---
            Level? postChangeCurrentLevel = self.levelCurrent;
            Level? postChangeLobbyLevel = self.levelLobby;
            LateJoinEntry.Log.LogDebug(
                $"[MOD Resync Debug Post] CurrentLevel: Name='{postChangeCurrentLevel?.name ?? "NULL"}'"
            );
            LateJoinEntry.Log.LogDebug(
                $"[MOD Resync Debug Post] LobbyLevel:   Name='{postChangeLobbyLevel?.name ?? "NULL"}'"
            );
            LateJoinEntry.Log.LogDebug(
                $"[MOD Resync Debug Post] RunIsArena() check result: {SemiFunc.RunIsArena()}"
            );

            // Check if mod logic should be active in the NEW scene
            bool modLogicActiveInNewScene = Utilities.IsModLogicActive();

            if (!modLogicActiveInNewScene)
            {
                // If mod logic is disabled (MainMenu, LobbyMenu, Tutorial), ensure lobby is OPEN immediately
                LateJoinEntry.Log.LogInfo(
                    $"[MOD Resync] Mod logic is disabled for the new level ('{self.levelCurrent?.name ?? "Unknown"}'). Ensuring lobby is OPEN NOW."
                );
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    PhotonNetwork.CurrentRoom.IsOpen = true;
                }
                // --- Use Helper ---
                GameVersionSupport.UnlockSteamLobby(true); // Unlock (make public for beta)
                // ---------------
                _shouldOpenLobbyAfterGen = false; // Ensure this is false

                // Disarm failsafe if mod logic becomes inactive
                if (_lobbyUnlockFailsafeCoroutine != null && LateJoinEntry.CoroutineRunner != null)
                {
                    LateJoinEntry.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                    _lobbyUnlockFailsafeCoroutine = null;
                    LateJoinEntry.Log.LogDebug("[L.A.T.E. Failsafe] Mod logic inactive. Disarmed any existing failsafe.");
                }
            }
            else
            {
                bool allowJoinEventually = ShouldAllowLobbyJoin(self, levelFailed);
                _shouldOpenLobbyAfterGen = allowJoinEventually;

                LateJoinEntry.Log.LogInfo(
                    $"[MOD Resync] Mod logic ACTIVE for new level. Lobby should open after gen: {_shouldOpenLobbyAfterGen} (Based on ShouldAllowLobbyJoin result: {allowJoinEventually})"
                );

                LateJoinEntry.Log.LogInfo(
                    "[MOD Resync] Closing/locking lobby TEMPORARILY until level generation completes."
                );
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    PhotonNetwork.CurrentRoom.IsOpen = false;
                }
                GameVersionSupport.LockSteamLobby();
                _normalUnlockLogicExecuted = false; // Reset for this level change cycle

                // ---- START FAILSAVE ----
                if (LateJoinEntry.CoroutineRunner != null)
                {
                    // Stop any previous failsafe coroutine
                    if (_lobbyUnlockFailsafeCoroutine != null)
                    {
                        LateJoinEntry.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                        LateJoinEntry.Log.LogDebug("[L.A.T.E. Failsafe] Stopped previous failsafe coroutine.");
                    }

                    if (_shouldOpenLobbyAfterGen) // Only arm failsafe if we intend to open it
                    {
                        _lobbyUnlockFailsafeCoroutine = LateJoinEntry.CoroutineRunner.StartCoroutine(LobbyUnlockFailsafeCoroutine());
                        // Log for arming is inside the coroutine itself
                    }
                    else
                    {
                        _lobbyUnlockFailsafeCoroutine = null; // Ensure it's null if we don't intend to open
                        LateJoinEntry.Log.LogDebug("[L.A.T.E. Failsafe] Intentionally keeping lobby closed. Failsafe not armed.");
                    }
                }
                else
                {
                    LateJoinEntry.Log.LogError("[L.A.T.E. Failsafe] Cannot manage failsafe: CoroutineRunner is null!");
                    _lobbyUnlockFailsafeCoroutine = null;
                }
                // ---- END FAILSAVE ----
            }
            #endregion
        }

        #endregion

        #region GameDirector Start Hook

        /// <summary>
        /// Harmony Postfix for GameDirector.SetStart.
        /// Runs on the HOST after the game state is officially set to Start for the level.
        /// This serves as the final point to unlock the lobby if needed, and disarms the failsafe.
        /// </summary>
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.SetStart))]
        [HarmonyPostfix]
        static void GameDirector_SetStart_Postfix(GameDirector __instance)
        {
            // Only the MasterClient should perform this final step
            if (!Utilities.IsRealMasterClient())
            {
                return;
            }

            // If L.A.T.E. mod logic is inactive for this scene, our lobby management shouldn't run.
            // The lobby state should have been handled immediately during RunManager_ChangeLevelHook.
            // We also don't want to interfere with any failsafe if it was (incorrectly) armed.
            if (!Utilities.IsModLogicActive())
            {
                LateJoinEntry.Log.LogDebug(
                    "[GameDirector.SetStart Postfix] Mod logic is inactive for this scene. No lobby action needed here by L.A.T.E."
                );
                // It's important NOT to set _normalUnlockLogicExecuted or stop the failsafe here,
                // as those are tied to L.A.T.E.'s active management cycle which isn't happening.
                // The RunManager_ChangeLevelHook should have already disarmed any failsafe if logic became inactive.
                return;
            }

            LateJoinEntry.Log.LogInfo(
                $"[GameDirector.SetStart Postfix] GameDirector state set to Start. Checking if lobby should open (Flag: {_shouldOpenLobbyAfterGen})."
            );

            // Check the flag set during RunManager_ChangeLevelHook
            if (_shouldOpenLobbyAfterGen)
            {
                LateJoinEntry.Log.LogInfo(
                    "[GameDirector.SetStart Postfix] Flag is TRUE. Opening Photon room and unlocking Steam lobby NOW."
                );

                // Open Photon Room
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    PhotonNetwork.CurrentRoom.IsOpen = true;
                    LateJoinEntry.Log.LogDebug(
                        "[GameDirector.SetStart Postfix] Photon Room IsOpen set to true."
                    );
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        "[GameDirector.SetStart Postfix] Cannot open Photon room: Not in room or CurrentRoom is null."
                    );
                }

                // Unlock Steam Lobby
                GameVersionSupport.UnlockSteamLobby(true);
                LateJoinEntry.Log.LogDebug(
                    "[GameDirector.SetStart Postfix] Steam lobby unlock attempted."
                );

                _normalUnlockLogicExecuted = true; // Signal that this "open" path was successful
                LateJoinEntry.Log.LogInfo("[L.A.T.E.] Normal lobby 'open' sequence completed successfully.");

                // Disarm the failsafe as it's no longer needed
                if (_lobbyUnlockFailsafeCoroutine != null && LateJoinEntry.CoroutineRunner != null)
                {
                    LateJoinEntry.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                    _lobbyUnlockFailsafeCoroutine = null;
                    LateJoinEntry.Log.LogDebug("[L.A.T.E. Failsafe] Disarmed by successful normal 'open' logic.");
                }
            }
            else // _shouldOpenLobbyAfterGen was FALSE (meaning we intended to keep it locked)
            {
                LateJoinEntry.Log.LogInfo(
                    "[GameDirector.SetStart Postfix] Flag is FALSE. Lobby remains closed/locked as per initial decision during level change."
                );

                // Sanity Check: Ensure Photon room is closed if flag is false
                if (
                    PhotonNetwork.InRoom
                    && PhotonNetwork.CurrentRoom != null
                    && PhotonNetwork.CurrentRoom.IsOpen // Only act if it's unexpectedly open
                )
                {
                    LateJoinEntry.Log.LogWarning(
                        "[GameDirector.SetStart Postfix] Sanity Check: Photon room was open despite L.A.T.E. intending it to be closed. Closing."
                    );
                    PhotonNetwork.CurrentRoom.IsOpen = false;
                }

                // Sanity Check: Ensure Steam lobby is locked if flag is false by re-applying LockLobby
                GameVersionSupport.LockSteamLobby();
                LateJoinEntry.Log.LogDebug(
                   "[GameDirector.SetStart Postfix] Sanity Check: Steam lobby lock (re)attempted as L.A.T.E. intended it to be closed."
               );

                _normalUnlockLogicExecuted = true; // Signal that this "keep closed" path was successful
                                                   // This is important so the failsafe (which shouldn't have been armed if _shouldOpenLobbyAfterGen was false,
                                                   // but as a precaution) knows that L.A.T.E. made a conscious decision.
                LateJoinEntry.Log.LogInfo("[L.A.T.E.] Normal lobby 'keep closed' sequence completed.");

                // Disarm the failsafe (it shouldn't be running if _shouldOpenLobbyAfterGen was false, but good practice to be sure)
                if (_lobbyUnlockFailsafeCoroutine != null && LateJoinEntry.CoroutineRunner != null)
                {
                    LateJoinEntry.CoroutineRunner.StopCoroutine(_lobbyUnlockFailsafeCoroutine);
                    _lobbyUnlockFailsafeCoroutine = null;
                    LateJoinEntry.Log.LogDebug("[L.A.T.E. Failsafe] Disarmed by successful normal 'keep closed' logic (failsafe should not have been active).");
                }
            }

            // CRITICAL: Reset the _shouldOpenLobbyAfterGen flag regardless of whether we opened the lobby or not.
            // This prepares it for the next level change.
            _shouldOpenLobbyAfterGen = false;
            LateJoinEntry.Log.LogDebug(
                $"[GameDirector.SetStart Postfix] Resetting _shouldOpenLobbyAfterGen flag to false."
            );
        }

        #endregion

        /// <summary>
        /// Failsafe coroutine to unlock the lobby if the normal unlock mechanism fails.
        /// </summary>
        private static IEnumerator LobbyUnlockFailsafeCoroutine()
        {
            const float failsafeDelaySeconds = 30f; // Configurable? For now, 30 seconds.
            LateJoinEntry.Log.LogInfo($"[L.A.T.E. Failsafe] Armed. Will check lobby state in {failsafeDelaySeconds} seconds if normal unlock doesn't occur.");

            yield return new WaitForSeconds(failsafeDelaySeconds);

            LateJoinEntry.Log.LogInfo("[L.A.T.E. Failsafe] Timer elapsed. Checking lobby state.");

            // Check if the normal unlock logic already ran and was successful
            if (_normalUnlockLogicExecuted)
            {
                LateJoinEntry.Log.LogInfo("[L.A.T.E. Failsafe] Normal unlock logic was executed. Failsafe action not needed.");
                _lobbyUnlockFailsafeCoroutine = null;
                yield break;
            }

            // Check if we are still the host and in a room
            if (!Utilities.IsRealMasterClient() || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            {
                LateJoinEntry.Log.LogWarning("[L.A.T.E. Failsafe] Conditions not met for failsafe unlock (not host, not in room, or room is null). Aborting.");
                _lobbyUnlockFailsafeCoroutine = null;
                yield break;
            }

            // The failsafe was only started if _shouldOpenLobbyAfterGen was true.
            // So, if we reach here and the lobby is still locked, it means something went wrong.
            bool photonRoomIsStillClosed = !PhotonNetwork.CurrentRoom.IsOpen;
            // We assume the Steam lobby is also still locked if our Photon room is.

            if (photonRoomIsStillClosed)
            {
                LateJoinEntry.Log.LogWarning("[L.A.T.E. Failsafe] Detected lobby is STILL LOCKED after timeout and normal unlock logic did not execute. Forcing unlock.");

                // Force unlock Photon Room
                PhotonNetwork.CurrentRoom.IsOpen = true;
                LateJoinEntry.Log.LogInfo("[L.A.T.E. Failsafe] Photon Room IsOpen set to true (FORCED).");

                // Force unlock Steam Lobby (make it public as per original intention)
                GameVersionSupport.UnlockSteamLobby(true);
                LateJoinEntry.Log.LogInfo("[L.A.T.E. Failsafe] Steam lobby unlock attempted (FORCED).");
            }
            else
            {
                LateJoinEntry.Log.LogInfo("[L.A.T.E. Failsafe] Lobby was found to be already open. No forced action taken.");
            }

            _lobbyUnlockFailsafeCoroutine = null; // Mark coroutine as finished
        }

        #region Photon Object Patches

        /// <summary>
        /// Postfix for PlayerAvatar.Update to handle voice manager updates.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerAvatar), "Update")]
        public static void PlayerAvatar_Update_Postfix(PlayerAvatar __instance)
        {
            if (!Utilities.IsModLogicActive())
            {
                return; // Don't run voice manager checks in disabled scenes
            }

            VoiceManager.HandleAvatarUpdate(__instance);
        }

        /// <summary>
        /// Prefix patch for PhysGrabObject destruction. Marks the object as destroyed and sends a buffered RPC.
        /// </summary>
        [HarmonyPatch(typeof(PhysGrabObject), nameof(PhysGrabObject.DestroyPhysGrabObject))]
        [HarmonyPrefix]
        static bool PhysGrabObject_DestroyPhysGrabObject_Prefix(PhysGrabObject __instance)
        {
            if (__instance != null && PhotonNetwork.IsMasterClient)
            {
                PhotonView pv = __instance.GetComponent<PhotonView>();
                if (pv != null)
                {
                    LateJoinEntry.Log.LogDebug(
                        $"[Patch Prefix] Intercepting DestroyPhysGrabObject for ViewID {pv.ViewID}. Sending buffered RPC and marking as destroyed."
                    );
                    DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
                    pv.RPC("DestroyPhysGrabObjectRPC", RpcTarget.AllBuffered);
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        $"[Patch Prefix] Unable to retrieve PhotonView for PhysGrabObject on {__instance.gameObject.name} during DestroyPhysGrabObject."
                    );
                }
            }
            return true;
        }

        /// <summary>
        /// Prefix patch for impact-based object destruction. Marks the object as destroyed and sends a buffered RPC.
        /// </summary>
        [HarmonyPatch(
            typeof(PhysGrabObjectImpactDetector),
            nameof(PhysGrabObjectImpactDetector.DestroyObject)
        )]
        [HarmonyPrefix]
        static bool PhysGrabImpact_DestroyObject_Prefix(
            PhysGrabObjectImpactDetector __instance,
            bool effects
        )
        {
            if (__instance != null && PhotonNetwork.IsMasterClient)
            {
                PhysGrabObject pgo = __instance.GetComponent<PhysGrabObject>();
                if (pgo != null)
                {
                    PhotonView pv = pgo.GetComponent<PhotonView>();
                    if (pv != null)
                    {
                        LateJoinEntry.Log.LogDebug(
                            $"[Patch Prefix] Intercepting ImpactDetector.DestroyObject for ViewID {pv.ViewID}. Sending buffered RPC and marking as destroyed."
                        );
                        DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
                        pv.RPC("DestroyObjectRPC", RpcTarget.AllBuffered, effects);
                    }
                    else
                    {
                        LateJoinEntry.Log.LogWarning(
                            $"[Patch Prefix] Unable to retrieve PhotonView for PhysGrabObject on {pgo.gameObject.name} during DestroyObject via ImpactDetector."
                        );
                    }
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        $"[Patch Prefix] Unable to retrieve PhysGrabObject for ImpactDetector on {__instance.gameObject.name} during DestroyObject."
                    );
                }
            }
            return true;
        }

        /// <summary>
        /// Prefix patch for PhysGrabHinge destruction. Marks the hinge as destroyed and sends a buffered RPC.
        /// </summary>
        [HarmonyPatch(typeof(PhysGrabHinge), nameof(PhysGrabHinge.DestroyHinge))]
        [HarmonyPrefix]
        static bool PhysGrabHinge_DestroyHinge_Prefix(PhysGrabHinge __instance)
        {
            if (__instance != null && PhotonNetwork.IsMasterClient)
            {
                PhotonView pv = __instance.GetComponent<PhotonView>();
                if (pv != null)
                {
                    LateJoinEntry.Log.LogDebug(
                        $"[Patch Prefix] Intercepting DestroyHinge for ViewID {pv.ViewID}. Sending buffered RPC and marking as destroyed."
                    );
                    DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
                    pv.RPC("DestroyHingeRPC", RpcTarget.AllBuffered);
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        $"[Patch Prefix] Unable to retrieve PhotonView for Hinge on {__instance.gameObject.name} during DestroyHinge."
                    );
                }
            }
            return true;
        }

        #endregion

        #region Spawn Hooks

        /// <summary>
        /// Hook for player avatar spawning. Attempts to assign a spawn point based on last known position or available spawn points.
        /// </summary>
        public static void PlayerAvatar_SpawnHook(
            Action<PlayerAvatar, Vector3, Quaternion> orig,
            PlayerAvatar self,
            Vector3 position,
            Quaternion rotation
        )
        {
            bool skipCustomLogic = false;
            string skipReason = "";

            // Check 1: Is this the initial main menu? (Use the specific SemiFunc for this)
            if (SemiFunc.IsMainMenu())
            {
                skipCustomLogic = true;
                skipReason = "SemiFunc.IsMainMenu()";
            }
            // Check 2: Does RunManager exist yet? If not, we're likely too early for game logic.
            else if (RunManager.instance == null)
            {
                skipCustomLogic = true;
                skipReason = "RunManager.instance == null";
            }
            // Check 3: Is it the Lobby Menu scene? (Requires RunManager.instance)
            else if (SemiFunc.RunIsLobbyMenu())
            {
                skipCustomLogic = true;
                skipReason = "SemiFunc.RunIsLobbyMenu()";
            }
            // Check 4: Is it the Tutorial scene? (Requires RunManager.instance)
            else if (SemiFunc.RunIsTutorial())
            {
                skipCustomLogic = true;
                skipReason = "SemiFunc.RunIsTutorial()";
            }

            // Apply the skip logic
            if (skipCustomLogic)
            {
                LateJoinEntry.Log.LogDebug(
                    $"[Spawn Hook] Skipping custom spawn logic ({skipReason}). Calling original."
                );
                try
                {
                    // Directly call the original method and exit.
                    orig.Invoke(self, position, rotation);
                }
                catch (Exception e)
                {
                    LateJoinEntry.Log.LogError(
                        $"[Spawn Hook] Error calling original SpawnRPC during skip ({skipReason}): {e}"
                    );
                }
                return; // Important: Exit the hook early
            }

            // These checks are still useful for other scenes or potential race conditions.
            if (self == null || self.photonView == null || self.photonView.Owner == null)
            {
                LateJoinEntry.Log.LogError(
                    "[Spawn Hook] PlayerAvatar, PhotonView, or Owner is null (in expected gameplay scene). Cannot proceed with custom logic."
                );
                try
                {
                    // Still try to call original even if we can't run custom logic
                    if (self != null) // Check self again just in case
                        orig.Invoke(self, position, rotation);
                }
                catch (Exception e)
                {
                    LateJoinEntry.Log.LogError(
                        $"Error calling original SpawnRPC on early exit (in expected gameplay scene): {e}"
                    );
                }
                return;
            }

            PhotonView pv = self.photonView;
            Player joiningPlayer = pv.Owner;
            int viewID = pv.ViewID;

            Vector3 finalPosition = position;
            Quaternion finalRotation = rotation;
            bool positionOverriddenByMod = false;
            bool useNormalSpawnLogic = true; // Default to using normal spawn logic
            #region Spawn At Last Position Logic

            if (ConfigManager.SpawnAtLastPosition.Value)
            {
                if (
                    PlayerPositionManager.TryGetLastTransform(
                        joiningPlayer,
                        out PlayerTransformData lastTransform
                    )
                )
                {
                    // Found a previous position for this player in this level instance.
                    finalPosition = lastTransform.Position;
                    finalRotation = lastTransform.Rotation;
                    positionOverriddenByMod = true;
                    useNormalSpawnLogic = false;
                    LateJoinEntry.Log.LogInfo(
                        $"[Spawn Hook] Spawning player {joiningPlayer.NickName} (ViewID {viewID}) at last known position: {finalPosition} (Was Death Head: {lastTransform.IsDeathHeadPosition})"
                    );
                    spawnPositionAssigned.Add(viewID);
                }
                else
                {
                    LateJoinEntry.Log.LogDebug(
                        $"[Spawn Hook] No previous position found for {joiningPlayer.NickName}. Using default spawn logic."
                    );
                    useNormalSpawnLogic = true;
                }
            }
            else
            {
                LateJoinEntry.Log.LogDebug(
                    "[Spawn Hook] SpawnAtLastPosition is disabled. Using default spawn logic."
                );
                useNormalSpawnLogic = true;
            }

            #endregion

            #region Default Spawn Logic

            if (useNormalSpawnLogic)
            {
                try
                {
                    bool alreadySpawned = false;
                    if (Utilities.paSpawnedField != null)
                    {
                        try
                        {
                            alreadySpawned = (bool)Utilities.paSpawnedField.GetValue(self);
                        }
                        catch { }
                    }
                    else
                    {
                        LateJoinEntry.Log.LogError(
                            $"paSpawnedField is null in SpawnHook for ViewID {viewID}!"
                        );
                    }

                    bool alreadyAssignedByMod = spawnPositionAssigned.Contains(viewID);

                    if (!alreadySpawned && !alreadyAssignedByMod)
                    {
                        string assignedPlayerName = Utilities.GetPlayerNickname(self);
                        if (PunManager.instance != null)
                        {
                            PunManager.instance.SyncAllDictionaries();
                        }

                        if (Utilities.IsRealMasterClient())
                        {
                            List<SpawnPoint> allSpawnPoints = Object
                                .FindObjectsOfType<SpawnPoint>()
                                .Where(sp => sp != null && !sp.debug)
                                .ToList();
                            if (allSpawnPoints.Count > 0)
                            {
                                List<PlayerAvatar> currentPlayers =
                                    GameDirector.instance?.PlayerList ?? new List<PlayerAvatar>();
                                float minDistanceSq = 1.5f * 1.5f;
                                allSpawnPoints.Shuffle();
                                bool foundAvailable = false;
                                foreach (SpawnPoint sp in allSpawnPoints)
                                {
                                    bool blocked = false;
                                    Vector3 spPos = sp.transform.position;
                                    foreach (PlayerAvatar player in currentPlayers)
                                    {
                                        if (player == null || player == self)
                                            continue;
                                        if (
                                            (player.transform.position - spPos).sqrMagnitude
                                            < minDistanceSq
                                        )
                                        {
                                            blocked = true;
                                            break;
                                        }
                                    }
                                    if (!blocked)
                                    {
                                        finalPosition = sp.transform.position;
                                        finalRotation = sp.transform.rotation;
                                        foundAvailable = true;
                                        positionOverriddenByMod = true;
                                        LateJoinEntry.Log.LogInfo(
                                            $"[Spawn Fix] Assigning player {assignedPlayerName} (ViewID {viewID}) to spawn point '{sp.name}' at {finalPosition}"
                                        );
                                        spawnPositionAssigned.Add(viewID);
                                        break;
                                    }
                                }
                                if (!foundAvailable)
                                {
                                    LateJoinEntry.Log.LogWarning(
                                        $"[Spawn Fix] All {allSpawnPoints.Count} spawn points blocked for {assignedPlayerName}. Using original: {position}"
                                    );
                                }
                            }
                            else
                            {
                                LateJoinEntry.Log.LogError(
                                    $"[Spawn Fix] No valid spawn points found for {assignedPlayerName}. Using original: {position}"
                                );
                            }
                        }
                        LateJoinEntry.Log.LogDebug(
                            $"[Spawn Fix] Invoking original Spawn for {assignedPlayerName} (ViewID {viewID}) at position {finalPosition} (Default logic, Overridden: {positionOverriddenByMod})"
                        );
                        orig.Invoke(self, finalPosition, finalRotation);
                    }
                    else
                    {
                        LateJoinEntry.Log.LogDebug(
                            $"[Spawn Hook] Skipping default spawn logic for {viewID}: already spawned ({alreadySpawned}) or assigned ({alreadyAssignedByMod})."
                        );
                    }
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError(
                        $"Error in default SpawnHook logic for ViewID {viewID}: {ex}"
                    );
                    try
                    {
                        if (!spawnPositionAssigned.Contains(viewID))
                            orig.Invoke(self, position, rotation);
                    }
                    catch (Exception origEx)
                    {
                        LateJoinEntry.Log.LogError(
                            $"Error calling original SpawnRPC after exception: {origEx}"
                        );
                    }
                }
            }
            else if (positionOverriddenByMod)
            {
                LateJoinEntry.Log.LogDebug(
                    $"[Spawn Fix] Invoking original Spawn for {joiningPlayer.NickName} (ViewID {viewID}) at position {finalPosition} (Last known position)"
                );
                orig.Invoke(self, finalPosition, finalRotation);
            }
            else
            {
                LateJoinEntry.Log.LogWarning(
                    $"[Spawn Hook] Reached unexpected state for ViewID {viewID}. Invoking original spawn with default args."
                );
                orig.Invoke(self, position, rotation);
            }

            #endregion
        }

        #endregion

        #region Player Start Hook

        /// <summary>
        /// Hook for the PlayerAvatar's start method. Invokes the original start and notifies that loading is complete.
        /// </summary>
        public static void PlayerAvatar_StartHook(Action<PlayerAvatar> orig, PlayerAvatar self)
        {
            orig.Invoke(self);

            PhotonView? pv = Utilities.GetPhotonView(self);
            if (self == null || pv == null)
                return;

            if (PhotonNetwork.IsMasterClient)
            {
                LateJoinEntry.Log.LogDebug(
                    $"[Late Join] PlayerAvatar Start: Sending LoadingLevelAnimationCompletedRPC for ViewID {pv.ViewID}"
                );
                pv.RPC("LoadingLevelAnimationCompletedRPC", RpcTarget.AllBuffered);
            }
        }

        #endregion

        #region Network Manager Hooks

        /// <summary>
        /// Handles post-processing when a new player enters the room.
        /// </summary>
        public static void NetworkManager_OnPlayerEnteredRoom_Postfix(Player newPlayer)
        {
            if (!Utilities.IsModLogicActive())
            {
                LateJoinEntry.Log?.LogDebug(
                    $"[Patches] Player entered room in disabled scene. Skipping L.A.T.E join handling for {newPlayer?.NickName ?? "NULL"}."
                );
                return; // Stop processing here
            }

            LateJoinEntry.Log.LogDebug(
                $"[Patches] Player entered room: {newPlayer?.NickName ?? "NULL"} (ActorNr: {newPlayer?.ActorNumber ?? -1}) in an active scene."
            );
            if (newPlayer != null)
            {
                LateJoinManager.HandlePlayerJoined(newPlayer);
            }
            else
            {
                LateJoinEntry.Log.LogWarning(
                    "[Patches] Received null player in OnPlayerEnteredRoom_Postfix."
                );
            }
        }

        /// <summary>
        /// Handles when a player leaves the room by tracking position and cleaning up tracking.
        /// </summary>
        public static void NetworkManager_OnPlayerLeftRoom_Postfix(Player otherPlayer)
        {
            // Clear all tracking for the leaving player
            if (otherPlayer != null)
            {
                LateJoinManager.ClearPlayerTracking(otherPlayer.ActorNumber); // Use the combined cleanup method
            }

            if (!Utilities.IsModLogicActive())
            {
                LateJoinEntry.Log?.LogDebug(
                    $"[Patches] Player left room in disabled scene. Skipping L.A.T.E leave handling for {otherPlayer?.NickName ?? "NULL"}."
                );

                if (otherPlayer != null)
                    LateJoinEntry.Log?.LogInfo(
                        $"[Patches][BaseGamePassthrough] Player left room: {otherPlayer.NickName} (ActorNr: {otherPlayer.ActorNumber})"
                    );
                else
                    LateJoinEntry.Log?.LogWarning(
                        "[Patches][BaseGamePassthrough] Received null player in OnPlayerLeftRoom_Postfix."
                    );

                return; // Stop L.A.T.E processing here
            }

            if (otherPlayer != null)
            {
                LateJoinEntry.Log.LogInfo(
                    $"[Patches] Player left room in active scene: {otherPlayer.NickName} (ActorNr: {otherPlayer.ActorNumber})"
                );

                if (Utilities.IsRealMasterClient())
                {
                    PlayerAvatar? avatar = Utilities.FindPlayerAvatar(otherPlayer);
                    if (avatar != null)
                    {
                        PlayerPositionManager.UpdatePlayerPosition(
                            otherPlayer,
                            avatar.transform.position,
                            avatar.transform.rotation
                        );
                    }
                    else
                    {
                        LateJoinEntry.Log.LogWarning(
                            $"[PositionManager] Could not find PlayerAvatar for leaving player {otherPlayer.NickName} to track position."
                        );
                    }

                    // Only host needs to notify enemies
                    EnemyManager.NotifyEnemiesOfLeavingPlayer(otherPlayer);
                }

                VoiceManager.HandlePlayerLeft(otherPlayer);
            }
            else
            {
                LateJoinEntry.Log.LogWarning(
                    "[Patches] Received null player in OnPlayerLeftRoom_Postfix (active scene)."
                );
            }
        }

        #endregion

        #region Player State Tracking Patches

        /// <summary>
        /// Postfix patch for PlayerAvatar.PlayerDeathRPC that marks the player as dead and updates death head position.
        /// </summary>
        [HarmonyPatch(typeof(PlayerAvatar), "PlayerDeathRPC")]
        [HarmonyPostfix]
        static void PlayerAvatar_PlayerDeathRPC_Postfix(PlayerAvatar __instance, int enemyIndex)
        {
            if (PhotonNetwork.IsMasterClient && __instance != null && __instance.photonView != null)
            {
                Player owner = __instance.photonView.Owner;
                if (owner != null)
                {
                    PlayerStateManager.MarkPlayerDead(owner);

                    #region Track Death Head Position

                    if (Utilities.paPlayerDeathHeadField != null)
                    {
                        try
                        {
                            object? deathHeadObj = Utilities.paPlayerDeathHeadField.GetValue(
                                __instance
                            );
                            if (
                                deathHeadObj is PlayerDeathHead deathHead
                                && deathHead != null
                                && deathHead.gameObject != null
                            )
                            {
                                // Capture the current death head transform.
                                PlayerPositionManager.UpdatePlayerDeathPosition(
                                    owner,
                                    deathHead.transform.position,
                                    deathHead.transform.rotation
                                );
                            }
                            else
                            {
                                LateJoinEntry.Log.LogWarning(
                                    $"[PositionManager] PlayerDeathHead component not found or null for {owner.NickName}."
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            LateJoinEntry.Log.LogError(
                                $"[PositionManager] Error reflecting PlayerDeathHead for {owner.NickName}: {ex}"
                            );
                        }
                    }
                    else
                    {
                        LateJoinEntry.Log.LogError(
                            "[PositionManager] PlayerDeathHead reflection field is null!"
                        );
                    }

                    #endregion
                }
            }
        }

        #endregion

        #region Player Avatar Patches (New Trigger)

        /// <summary>
        /// Harmony Prefix for PlayerAvatar.LoadingLevelAnimationCompletedRPC.
        /// HOST-SIDE ONLY: This is our primary trigger for syncing late-joiner state.
        /// When the host receives this RPC from a client who was marked as needing sync,
        /// it indicates the client is ready for state synchronization.
        /// </summary>
        [HarmonyPriority(Priority.Last)]
        [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.LoadingLevelAnimationCompletedRPC))]
        [HarmonyPrefix]
        static bool PlayerAvatar_LoadingLevelAnimationCompletedRPC_Prefix(PlayerAvatar __instance)
        {
            // Only Host executes sync logic
            if (!Utilities.IsRealMasterClient())
            {
                return true; // Let original RPC logic run on clients/non-hosts
            }

            // --- Get the sender (Owner of the PhotonView) ---
            PhotonView? pv = __instance?.photonView;
            if (pv == null || __instance == null)
            {
                LateJoinEntry.Log.LogError(
                    "[LateJoin Trigger RPC] Instance or PhotonView is null in LoadingCompleteRPC prefix. Cannot determine sender."
                );
                return true; // Let original run, but can't proceed
            }
            Player sender = pv.Owner; // The owner of the avatar's PhotonView is the player who sent the RPC

            // --- Guard Clauses ---
            if (sender == null)
            {
                // This shouldn't happen for a player avatar, but good practice
                LateJoinEntry.Log.LogError(
                    $"[LateJoin Trigger RPC] PhotonView Owner is null for Avatar PV {pv.ViewID}. Cannot determine sender."
                );
                return true;
            }

            if (sender.IsLocal)
            {
                // Host shouldn't trigger sync for itself via this RPC
                return true;
            }

            int actorNr = sender.ActorNumber;
            string nickname = sender.NickName ?? $"ActorNr {actorNr}";

            // Check if a reload is currently in progress for this scene instance.
            if (_reloadHasBeenTriggeredThisScene)
            {
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoin Trigger RPC] Ignoring LoadingCompleteRPC from {nickname}: Reload already triggered this scene."
                );
                return true; // Don't interfere with reload
            }

            // Check if THIS specific player actually needs the late-join sync action
            if (LateJoinManager.IsPlayerNeedingSync(actorNr))
            {
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoin Trigger RPC] Received LoadingLevelAnimationCompletedRPC from late-joiner {nickname} (ActorNr: {actorNr})."
                );

                // Check if mod logic should even be active in the current scene
                if (!Utilities.IsModLogicActive())
                {
                    LateJoinEntry.Log.LogWarning(
                        $"[LateJoin Trigger RPC] Mod logic is INACTIVE in current scene. Clearing sync need for {nickname} but not syncing."
                    );
                    LateJoinManager.ClearPlayerTracking(actorNr); // Clean up tracking
                    return true; // Let original logic run, but don't sync
                }

                // --- Trigger Action ---
                LateJoinManager.MarkPlayerSyncTriggeredAndClearNeed(actorNr);

                // Check the config option: Reload or Normal Sync?
                if (ConfigManager.ForceReloadOnLateJoin.Value)
                {
                    // -----> PERFORM RELOAD SEQUENCE <-----
                    LateJoinEntry.Log.LogWarning(
                        $"[LateJoin Trigger RPC] CONFIG OPTION ENABLED: Forcing level reload because player {nickname} completed loading late."
                    );

                    if (RunManager.instance != null)
                    {
                        _reloadHasBeenTriggeredThisScene = true;
                        LateJoinEntry.Log.LogInfo(
                            $"[LateJoin Trigger RPC] Setting reload-triggered flag for this scene instance."
                        );
                        RunManager.instance.RestartScene();
                        return false; // Prevent original RPC method from running unnecessarily during reload
                    }
                    else
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoin Trigger RPC] FAILED TO FORCE RELOAD: RunManager.instance is null for late joiner {nickname}."
                        );
                        LateJoinManager.ClearPlayerTracking(actorNr);
                        return true;
                    }
                }
                else // ---> Config option is FALSE (Normal Sync Logic) <---
                {
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoin Trigger RPC] Initiating standard late-join sync for {nickname}."
                    );
                    // Pass the PlayerAvatar instance (__instance) we got from the patch parameter
                    LateJoinManager.SyncAllStateForPlayer(sender, __instance);
                    return true; // Let the original RPC method run
                }
            }
            // Else: Player didn't need sync. Let original RPC run.
            return true;
        }

        #endregion

        #region Enemy Vision Dictionary Fix Patches

        /// <summary>
        /// Harmony Prefix for EnemyVision.VisionTrigger.
        /// Ensures the playerID exists in the vision dictionaries before the original method proceeds.
        /// If missing, attempts to add the player via PlayerAdded as a fallback.
        /// </summary>
        [HarmonyPatch(typeof(EnemyVision), nameof(EnemyVision.VisionTrigger))]
        [HarmonyPrefix]
        static bool EnemyVision_VisionTrigger_Prefix(
            EnemyVision __instance,
            int playerID,
            PlayerAvatar player
        )
        {
            // Early exit if critical components are missing
            if (__instance == null || player == null)
                return true; // Let original run maybe handle null? Or log error?

            bool needToAdd = false;
            string missingDict = "";

            // Check if keys exist
            if (!__instance.VisionsTriggered.ContainsKey(playerID))
            {
                needToAdd = true;
                missingDict = "VisionsTriggered";
            }
            else if (!__instance.VisionTriggered.ContainsKey(playerID)) // Check the second dictionary too
            {
                needToAdd = true;
                missingDict = "VisionTriggered";
            }

            if (needToAdd)
            {
                string enemyName = __instance.gameObject?.name ?? "UnknownEnemy";
                string playerName = player.photonView?.Owner?.NickName ?? $"ViewID {playerID}";
                LateJoinEntry.Log.LogWarning(
                    $"[Patch Fix] EnemyVision.VisionTrigger: Key {playerID} ({playerName}) missing in '{missingDict}' for enemy '{enemyName}'. Attempting recovery via PlayerAdded."
                );

                try
                {
                    // Attempt to add the player to the dictionaries
                    __instance.PlayerAdded(playerID);

                    // Re-check if the key was successfully added
                    if (
                        !__instance.VisionsTriggered.ContainsKey(playerID)
                        || !__instance.VisionTriggered.ContainsKey(playerID)
                    )
                    {
                        LateJoinEntry.Log.LogError(
                            $"[Patch Fix] EnemyVision.VisionTrigger: FAILED to add key {playerID} via PlayerAdded for enemy '{enemyName}'. Skipping original trigger logic to prevent crash."
                        );
                        return false; // Prevent original method execution
                    }
                    else
                    {
                        LateJoinEntry.Log.LogInfo(
                            $"[Patch Fix] EnemyVision.VisionTrigger: Successfully recovered key {playerID} for enemy '{enemyName}'."
                        );
                        // Now safe to proceed with the original method
                    }
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError(
                        $"[Patch Fix] EnemyVision.VisionTrigger: Exception during PlayerAdded recovery for key {playerID} on enemy '{enemyName}': {ex}"
                    );
                    return false; // Prevent original method execution on error
                }
            }

            // If keys existed or were successfully added, run the original method
            return true;
        }

        /// <summary>
        /// Harmony Prefix for EnemyTriggerAttack.OnTriggerStay.
        /// Ensures the player's ViewID exists in the associated EnemyVision's dictionary
        /// before the original method accesses it.
        /// </summary>
        [HarmonyPatch(typeof(EnemyTriggerAttack), "OnTriggerStay")]
        [HarmonyPrefix]
        static bool EnemyTriggerAttack_OnTriggerStay_Prefix(
            EnemyTriggerAttack __instance,
            Collider other
        )
        {
            // Minimal checks
            if (__instance == null || other == null || __instance.Enemy == null)
                return true;

            EnemyVision? visionInstance = Utilities.GetEnemyVision(__instance.Enemy);
            if (visionInstance == null)
            {
                // Log handled by Utilities.GetEnemyVision
                return true; // Cannot proceed with check/fix, let original run
            }

            // Check if the collider belongs to a player
            PlayerTrigger component = other.GetComponent<PlayerTrigger>();
            if (
                component != null
                && component.PlayerAvatar != null
                && component.PlayerAvatar.photonView != null
            )
            {
                PlayerAvatar playerAvatar = component.PlayerAvatar;
                int viewID = playerAvatar.photonView.ViewID;

                // The critical check using the resolved visionInstance
                if (!visionInstance.VisionTriggered.ContainsKey(viewID))
                {
                    string enemyName = __instance.gameObject?.name ?? "UnknownEnemyTrigger";
                    string playerName =
                        playerAvatar.photonView?.Owner?.NickName ?? $"ViewID {viewID}";
                    LateJoinEntry.Log.LogWarning(
                        $"[Patch Fix] EnemyTriggerAttack.OnTriggerStay: Key {viewID} ({playerName}) missing in 'VisionTriggered' for enemy '{enemyName}'. Attempting recovery via PlayerAdded."
                    );

                    try
                    {
                        // Attempt recovery
                        visionInstance.PlayerAdded(viewID);

                        // Re-check
                        if (!visionInstance.VisionTriggered.ContainsKey(viewID))
                        {
                            LateJoinEntry.Log.LogError(
                                $"[Patch Fix] EnemyTriggerAttack.OnTriggerStay: FAILED to add key {viewID} via PlayerAdded for enemy '{enemyName}'."
                            );
                        }
                        else
                        {
                            LateJoinEntry.Log.LogInfo(
                                $"[Patch Fix] EnemyTriggerAttack.OnTriggerStay: Successfully recovered key {viewID} for enemy '{enemyName}'."
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[Patch Fix] EnemyTriggerAttack.OnTriggerStay: Exception during PlayerAdded recovery for key {viewID} on enemy '{enemyName}': {ex}"
                        );
                    }
                }
            }

            // Let the original OnTriggerStay logic run
            return true;
        }

        #endregion
    }

    public static class EarlyLobbyLockHelper
    {
        public static void TryLockLobby(string reason)
        {
            if (!Utilities.IsRealMasterClient()) return;

            // Check if a level change is already in progress to avoid redundant locks
            // or conflicts if RunManager.ChangeLevel is already doing its thing.
            // This might require a new static flag in your mod if RunManager's internal
            // state isn't easily accessible or reliable for this check.
            // For now, we'll assume the call to this helper is the *first* indication.

            LateJoinEntry.Log.LogInfo($"[L.A.T.E.] [Early Lock] Host is about to change level (Trigger: {reason}). Locking lobby NOW.");
            if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
            {
                if (PhotonNetwork.CurrentRoom.IsOpen) // Only log if it was open
                {
                    PhotonNetwork.CurrentRoom.IsOpen = false;
                    LateJoinEntry.Log.LogDebug("[Early Lock] Photon Room IsOpen set to false.");
                }
            }
            GameVersionSupport.LockSteamLobby(); // This helper should handle Steam lobby visibility
        }
    }

    [HarmonyPatch]
    public static class TruckScreenText_ChatBoxState_EarlyLock_Patches // Keep the same class name
    {
        // Cache the reflection FieldInfo for efficiency and error checking
        private static FieldInfo? _tstPlayerChatBoxStateStartField;

        // Helper method to get/cache the FieldInfo
        private static FieldInfo? GetPlayerChatBoxStateStartField()
        {
            if (_tstPlayerChatBoxStateStartField == null)
            {
                _tstPlayerChatBoxStateStartField = AccessTools.Field(typeof(TruckScreenText), "playerChatBoxStateStart");
                if (_tstPlayerChatBoxStateStartField == null)
                {
                    // Log error if reflection fails - this is critical for the patch
                    LateJoinEntry.Log?.LogError("[Reflection Error] Failed to find private field 'TruckScreenText.playerChatBoxStateStart'. Early lock patch will not function correctly.");
                }
            }
            return _tstPlayerChatBoxStateStartField;
        }

        // --- Patch for PlayerChatBoxStateLockedDestroySlackers ---
        [HarmonyPatch(typeof(TruckScreenText), "PlayerChatBoxStateLockedDestroySlackers")]
        [HarmonyPrefix] // ***** CHANGED TO PREFIX *****
        static bool Prefix_PlayerChatBoxStateLockedDestroySlackers(TruckScreenText __instance) // Now returns bool, takes __instance
        {
            // Only run lock logic on the host
            if (Utilities.IsRealMasterClient())
            {
                FieldInfo? startFlagField = GetPlayerChatBoxStateStartField();
                if (startFlagField != null) // Check if reflection succeeded
                {
                    try
                    {
                        // Read the value of the flag *before* the original method potentially changes it
                        bool isStateStarting = (bool)(startFlagField.GetValue(__instance) ?? false);

                        if (isStateStarting)
                        {
                            // The original method's 'if (playerChatBoxStateStart)' block is about to execute
                            // This is the single time we want to lock the lobby for this state transition.
                            EarlyLobbyLockHelper.TryLockLobby("TruckScreenText.PlayerChatBoxStateLockedDestroySlackers (State Enter)");
                        }
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log?.LogError($"[Early Lock Prefix - DestroySlackers] Error accessing playerChatBoxStateStart: {ex}");
                    }
                }
            }
            return true; // ***** IMPORTANT: Always return true to let the original method execute *****
        }

        // --- Patch for PlayerChatBoxStateLockedStartingTruck ---
        [HarmonyPatch(typeof(TruckScreenText), "PlayerChatBoxStateLockedStartingTruck")]
        [HarmonyPrefix] // ***** CHANGED TO PREFIX *****
        static bool Prefix_PlayerChatBoxStateLockedStartingTruck(TruckScreenText __instance) // Now returns bool, takes __instance
        {
            // Only run lock logic on the host
            if (Utilities.IsRealMasterClient())
            {
                FieldInfo? startFlagField = GetPlayerChatBoxStateStartField();
                if (startFlagField != null) // Check if reflection succeeded
                {
                    try
                    {
                        // Read the value of the flag *before* the original method potentially changes it
                        bool isStateStarting = (bool)(startFlagField.GetValue(__instance) ?? false);

                        if (isStateStarting)
                        {
                            // The original method's 'if (playerChatBoxStateStart)' block is about to execute
                            // This is the single time we want to lock the lobby for this state transition.
                            EarlyLobbyLockHelper.TryLockLobby("TruckScreenText.PlayerChatBoxStateLockedStartingTruck (State Enter)");
                        }
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log?.LogError($"[Early Lock Prefix - StartingTruck] Error accessing playerChatBoxStateStart: {ex}");
                    }
                }
            }
            return true; // ***** IMPORTANT: Always return true to let the original method execute *****
        }
    }

    [HarmonyPatch]
    internal static class NetworkConnect_Patches
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
                && GameManager.instance.gameMode != 0
            )
            {
                // Check if AutomaticallySyncScene is currently false (it should be here,
                // as the original Start method sets it false right after this prefix runs,
                // but we force it true before that happens).
                if (!PhotonNetwork.AutomaticallySyncScene)
                {
                    LateJoinEntry.Log.LogInfo(
                        "[L.A.T.E] Forcing PhotonNetwork.AutomaticallySyncScene = true in NetworkConnect.Start (Prefix)"
                    );
                    PhotonNetwork.AutomaticallySyncScene = true;
                }
                else
                {
                    // This case might happen if another mod already set it true, which is fine.
                    LateJoinEntry.Log.LogDebug(
                        "[L.A.T.E] PhotonNetwork.AutomaticallySyncScene is already true in NetworkConnect.Start (Prefix)."
                    );
                }
            }
            else
            {
                // This might be the initial host setup or single player, let original logic run.
                LateJoinEntry.Log.LogDebug(
                    "[L.A.T.E] Skipping AutomaticallySyncScene force in NetworkConnect.Start (Prefix) - Likely initial host/SP setup."
                );
            }
        }

        // Patch OnJoinedRoom just for logging verification
        [HarmonyPatch(typeof(NetworkConnect), nameof(NetworkConnect.OnJoinedRoom))]
        [HarmonyPostfix]
        static void LogAutoSyncPostfix()
        {
            LateJoinEntry.Log.LogInfo(
                $"[L.A.T.E] NetworkConnect.OnJoinedRoom Postfix: AutomaticallySyncScene is now {PhotonNetwork.AutomaticallySyncScene}"
            );
        }
    }
}