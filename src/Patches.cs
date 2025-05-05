using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Linq;
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
                LateJoinEntry.Log.LogInfo("[Minimal Mod] Finished clearing scene object cache.");
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError($"Error clearing Photon cache: {ex}");
            }
        }

        /// <summary>
        /// Determines if the lobby should allow players to join based on the current game state and configuration.
        /// Includes an exception for expected Arena level failures.
        /// </summary>
        private static bool ShouldAllowLobbyJoin(bool levelFailed)
        {
            // --- Arena Exception Logic ---
            if (levelFailed)
            {
                // Check the current level AFTER the level change has occurred.
                if (SemiFunc.RunIsArena())
                {
                    LateJoinEntry.Log.LogInfo(
                        "[ShouldAllowLobbyJoin] Level failed, but current level IS Arena. Allowing join based on Arena config."
                    );
                    // Allow join ONLY if the Arena config setting is true.
                    return ConfigManager.AllowInArena.Value;
                }
                else
                {
                    // Level genuinely failed (and it's not the Arena). Disallow join.
                    LateJoinEntry.Log.LogInfo(
                        "[ShouldAllowLobbyJoin] Level failed (and not Arena). Disallowing join."
                    );
                    return false;
                }
            }
            // --- End Arena Exception ---

            // If level did NOT fail, proceed with normal config checks:
            LateJoinEntry.Log.LogDebug(
                "[ShouldAllowLobbyJoin] Level did not fail. Checking specific level types based on config..."
            );

            if (SemiFunc.RunIsShop() && ConfigManager.AllowInShop.Value)
            {
                LateJoinEntry.Log.LogDebug(
                    "[ShouldAllowLobbyJoin] Allowing join: In Shop & Config allows."
                );
                return true;
            }
            if (SemiFunc.RunIsLobby() && ConfigManager.AllowInTruck.Value) // Truck/Lobby
            {
                LateJoinEntry.Log.LogDebug(
                    "[ShouldAllowLobbyJoin] Allowing join: In Truck/Lobby & Config allows."
                );
                return true;
            }
            if (SemiFunc.RunIsLevel() && ConfigManager.AllowInLevel.Value) // Normal gameplay levels
            {
                LateJoinEntry.Log.LogDebug(
                    "[ShouldAllowLobbyJoin] Allowing join: In Level & Config allows."
                );
                return true;
            }
            // This check still works for Arena if levelFailed was initially false (unlikely but safe)
            if (SemiFunc.RunIsArena() && ConfigManager.AllowInArena.Value)
            {
                LateJoinEntry.Log.LogDebug(
                    "[ShouldAllowLobbyJoin] Allowing join: In Arena & Config allows."
                );
                return true;
            }
            if (SemiFunc.RunIsLobbyMenu()) // Always allow in the pre-game lobby menu
            {
                LateJoinEntry.Log.LogDebug("[ShouldAllowLobbyJoin] Allowing join: In LobbyMenu.");
                return true;
            }

            LateJoinEntry.Log.LogDebug(
                "[ShouldAllowLobbyJoin] No applicable allow condition met. Disallowing join."
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
                if (SteamManager.instance != null)
                {
                    SteamManager.instance.UnlockLobby();
                }
                _shouldOpenLobbyAfterGen = false; // Ensure flag is false
                return; // Stop further L.A.T.E-specific logic
            }
            else
            {
                // Mod logic IS active in the new scene. Decide if joining should be allowed eventually.
                // Pass the original levelFailed flag; ShouldAllowLobbyJoin handles the Arena exception internally.
                bool allowJoinEventually = ShouldAllowLobbyJoin(levelFailed);

                // Store the intention in the flag
                _shouldOpenLobbyAfterGen = allowJoinEventually;
                LateJoinEntry.Log.LogInfo(
                    $"[MOD Resync] Mod logic ACTIVE for new level. Lobby should open after gen: {_shouldOpenLobbyAfterGen} (Based on ShouldAllowLobbyJoin result: {allowJoinEventually})"
                );

                // CRITICAL: Keep the lobby CLOSED for now, regardless of the flag.
                // The GenerateDone postfix will handle opening it if _shouldOpenLobbyAfterGen is true.
                LateJoinEntry.Log.LogInfo(
                    "[MOD Resync] Closing/locking lobby TEMPORARILY until level generation completes."
                );
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    PhotonNetwork.CurrentRoom.IsOpen = false;
                }
                if (SteamManager.instance != null)
                {
                    SteamManager.instance.LockLobby();
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        "SteamManager instance is null when attempting temporary lock post-level change."
                    );
                }
            }

            #endregion
        }

        #endregion

        #region GameDirector Start Hook

        /// <summary>
        /// Harmony Postfix for GameDirector.SetStart.
        /// Runs on the HOST after the game state is officially set to Start for the level.
        /// This serves as the final point to unlock the lobby if needed, replacing the LevelGenerator.GenerateDone hook.
        /// </summary>
        [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.SetStart))]
        [HarmonyPostfix]
        static void GameDirector_SetStart_Postfix(GameDirector __instance)
        {
            // Only the MasterClient should perform this final step
            if (!Utilities.IsRealMasterClient())
            {
                return;
            }

            if (!Utilities.IsModLogicActive())
            {
                LateJoinEntry.Log.LogDebug(
                    "[GameDirector.SetStart Postfix] Mod logic is inactive for this scene. No lobby action needed here."
                );
                // If mod logic isn't active, the lobby should have been opened immediately during level change.
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
                if (SteamManager.instance != null)
                {
                    SteamManager.instance.UnlockLobby();
                    LateJoinEntry.Log.LogDebug(
                        "[GameDirector.SetStart Postfix] Steam lobby unlocked."
                    );
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        "[GameDirector.SetStart Postfix] Cannot unlock Steam lobby: SteamManager instance is null."
                    );
                }
            }
            else
            {
                LateJoinEntry.Log.LogInfo(
                    "[GameDirector.SetStart Postfix] Flag is FALSE. Lobby remains closed/locked as per initial decision during level change."
                );

                // Sanity Check: Ensure Photon room is closed if flag is false
                if (
                    PhotonNetwork.InRoom
                    && PhotonNetwork.CurrentRoom != null
                    && PhotonNetwork.CurrentRoom.IsOpen
                )
                {
                    LateJoinEntry.Log.LogWarning(
                        "[GameDirector.SetStart Postfix] Sanity Check: Photon room was open despite flag being false. Closing."
                    );
                    PhotonNetwork.CurrentRoom.IsOpen = false;
                }

                // Sanity Check: Ensure Steam lobby is locked if flag is false by re-applying LockLobby
                if (SteamManager.instance != null)
                {
                    LateJoinEntry.Log.LogDebug(
                        "[GameDirector.SetStart Postfix] Sanity Check: Re-applying Steam lobby lock."
                    );
                    SteamManager.instance.LockLobby();
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        "[GameDirector.SetStart Postfix] Sanity Check: Cannot lock Steam lobby - SteamManager instance is null."
                    );
                }
            }

            // CRITICAL: Reset the flag regardless of whether we opened the lobby or not.
            // Its purpose is served once SetStart is called.
            _shouldOpenLobbyAfterGen = false;
            LateJoinEntry.Log.LogDebug(
                $"[GameDirector.SetStart Postfix] Resetting _shouldOpenLobbyAfterGen flag to false."
            );
        }

        #endregion

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
                            $"[Minimal Mod] Invoking original Spawn for {assignedPlayerName} (ViewID {viewID}) at position {finalPosition} (Default logic, Overridden: {positionOverriddenByMod})"
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
                    $"[Minimal Mod] Invoking original Spawn for {joiningPlayer.NickName} (ViewID {viewID}) at position {finalPosition} (Last known position)"
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
                    $"[Minimal Mod] PlayerAvatar Start: Sending LoadingLevelAnimationCompletedRPC for ViewID {pv.ViewID}"
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
        /// HOST-SIDE ONLY: This is our NEW primary trigger for syncing late-joiner state.
        /// When the host receives this RPC from a client who was marked as needing sync,
        /// it indicates the client is ready for state synchronization.
        /// </summary>
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