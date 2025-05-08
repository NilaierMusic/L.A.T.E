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
    /// <summary>
    /// Manages the state and synchronization process for players who join after a
    /// level has started.
    /// </summary>
    internal static class LateJoinManager
    {
        #region Constants and Fields
        // Delay for the teleport-back sequence for items in the extraction point.
        private static readonly float itemResyncDelay = 0.2f; // How long items stay "away"
        #endregion

        #region Tracking Sets
        private static readonly HashSet<int> _playersNeedingLateJoinSync = new HashSet<int>();
        private static readonly HashSet<int> _playersJoinedLateThisScene = new HashSet<int>();
        #endregion

        #region Scene Management
        public static void ResetSceneTracking()
        {
            LateJoinEntry.Log.LogDebug(
                "[LateJoinManager] Clearing late join tracking sets for new scene."
            );
            _playersNeedingLateJoinSync.Clear();
            _playersJoinedLateThisScene.Clear();
        }
        #endregion

        #region Player Join Handling(Tracking Logic)
        /// <summary>
        /// Handles initial notification when a player enters the room.
        /// Marks players who join during an active scene for both data sync and
        /// late-join restrictions.
        /// </summary>
        public static void HandlePlayerJoined(Player newPlayer)
        {
            if (newPlayer == null)
            {
                LateJoinEntry.Log.LogWarning(
                    "[LateJoinManager] HandlePlayerJoined called with null player."
                );
                return;
            }

            int actorNr = newPlayer.ActorNumber;
            string nickname = newPlayer.NickName ?? $"ActorNr {actorNr}";
            bool joinedDuringActiveScene = Utilities.IsModLogicActive();

            if (joinedDuringActiveScene)
            {
                // Mark for the RPC trigger check
                if (_playersNeedingLateJoinSync.Add(actorNr))
                {
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager] Player {nickname} marked as needing late join DATA sync (awaiting LoadingCompleteRPC)."
                    );
                }

                // Mark for persistent late-join status (e.g., item equip blocking)
                if (_playersJoinedLateThisScene.Add(actorNr))
                {
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager] Player {nickname} marked as JOINED LATE THIS SCENE."
                    );
                }
            }
            else
            {
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager] Player {nickname} joined during INACTIVE scene. Not marked as late-joiner."
                );
            }

            if (Utilities.IsRealMasterClient())
            {
                VoiceManager.TriggerDelayedSync($"Player {nickname} joined room", 0.5f);
            }
        }
        #endregion

        #region Public Accessors& Modifiers for Tracking
        internal static bool IsPlayerNeedingSync(int actorNumber) =>
            _playersNeedingLateJoinSync.Contains(actorNumber);

        internal static bool DidPlayerJoinLateThisScene(int actorNumber) =>
            _playersJoinedLateThisScene.Contains(actorNumber);

        internal static void MarkPlayerSyncTriggeredAndClearNeed(int actorNumber)
        {
            // Remove from the "needing sync" list as the trigger has fired.
            bool removedNeed = _playersNeedingLateJoinSync.Remove(actorNumber);

            if (removedNeed)
            {
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager] Sync triggered for ActorNr {actorNumber}. Removed from 'NeedingSync' list."
                );
            }
        }

        internal static void ClearPlayerTracking(int actorNumber)
        {
            bool removedNeed = _playersNeedingLateJoinSync.Remove(actorNumber);
            bool removedJoinedLate = _playersJoinedLateThisScene.Remove(actorNumber);
            if (
                removedNeed || removedJoinedLate
            )
            {
                LateJoinEntry.Log?.LogDebug(
                    $"[LateJoinManager] Cleared all sync tracking for ActorNr {actorNumber}."
                );
            }
        }
        #endregion

        #region Central Synchronization Method
        /// <summary>
        /// Executes all necessary synchronization steps for a late-joining player.
        /// </summary>
        public static void SyncAllStateForPlayer(Player targetPlayer, PlayerAvatar playerAvatar)
        {
            int actorNr = targetPlayer.ActorNumber;
            string nickname = targetPlayer.NickName ?? $"ActorNr {actorNr}";
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager] === Executing SyncAllStateForPlayer for {nickname} ==="
            );
            try
            {
                // Standard Sync Calls
                SyncLevelState(targetPlayer);
                SyncModuleConnectionStatesForPlayer(targetPlayer);
                SyncExtractionPointsForPlayer(targetPlayer); // Syncs EP state first

                bool isShopScene = SemiFunc.RunIsShop();
                if (isShopScene)
                {
                    SyncAllShopItemsForPlayer(targetPlayer); // Sync shop item values
                }
                else
                {
                    SyncAllValuablesForPlayer(targetPlayer); // Sync valuable values
                    DestructionManager.SyncHingeStatesForPlayer(targetPlayer); // Relies on host sending targeted RPCs if needed
                }
                TriggerPropSwitchSetup(targetPlayer);
                EnemyManager.SyncAllEnemyStatesForPlayer(targetPlayer);
                EnemyManager.NotifyEnemiesOfNewPlayer(targetPlayer, playerAvatar);
                SyncPlayerDeathState(targetPlayer, playerAvatar);
                SyncTruckScreenForPlayer(targetPlayer);

                SyncAllItemStatesForPlayer(targetPlayer);

                // Arena Specific Sync
                if (SemiFunc.RunIsArena())
                {
                    SyncArenaStateForPlayer(targetPlayer);
                }

                if (Utilities.IsRealMasterClient())
                {
                    LateJoinEntry.Log.LogDebug(
                        $"[LateJoinManager] Host preparing to potentially resync EP items for {nickname}."
                    );
                    ExtractionPoint? epToResync = null;

                    if (isShopScene)
                    {
                        // Find the Shop Extraction Point
                        ExtractionPoint[] allEps = Object.FindObjectsOfType<ExtractionPoint>();
                        foreach (ExtractionPoint ep in allEps)
                        {
                            if (ep == null || Utilities.epIsShopField == null)
                                continue;
                            try
                            {
                                bool isThisTheShopEP = (bool)(
                                    Utilities.epIsShopField.GetValue(ep) ?? false
                                );
                                if (isThisTheShopEP)
                                {
                                    epToResync = ep;
                                    LateJoinEntry.Log.LogDebug(
                                        $"[LateJoinManager] Found Shop EP '{ep.name}' for item resync."
                                    );
                                    break; // Found the shop EP
                                }
                            }
                            catch (Exception ex)
                            {
                                LateJoinEntry.Log.LogWarning(
                                    $"[LateJoinManager] Error checking isShop field on EP '{ep?.name ?? "NULL"}': {ex.Message}"
                                );
                            }
                        }
                        if (epToResync == null)
                        {
                            LateJoinEntry.Log.LogWarning(
                                "[LateJoinManager] Could not find Shop EP for item " + "resync."
                            );
                        }
                    }
                    else // Level Scene
                    {
                        // Get the currently active Extraction Point
                        if (
                            RoundDirector.instance != null
                            && Utilities.rdExtractionPointCurrentField != null
                        )
                        {
                            try
                            {
                                epToResync =
                                    Utilities.rdExtractionPointCurrentField.GetValue(
                                        RoundDirector.instance
                                    ) as ExtractionPoint;
                                if (epToResync != null)
                                {
                                    LateJoinEntry.Log.LogDebug(
                                        $"[LateJoinManager] Found active Level EP '{epToResync.name}' for item resync."
                                    );
                                }
                                else
                                {
                                    LateJoinEntry.Log.LogDebug(
                                        "[LateJoinManager] No active Level EP found "
                                            + "(RoundDirector.extractionPointCurrent is "
                                            + "null). Skipping item resync."
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                LateJoinEntry.Log.LogError(
                                    $"[LateJoinManager] Error getting current EP from RoundDirector: {ex}"
                                );
                            }
                        }
                        else
                        {
                            LateJoinEntry.Log.LogWarning(
                                "[LateJoinManager] RoundDirector instance or "
                                    + "rdExtractionPointCurrentField is null. Cannot get "
                                    + "active EP for resync."
                            );
                        }
                    }

                    // If we found a relevant EP, start the resync coroutine
                    if (epToResync != null)
                    {
                        LateJoinEntry.Log.LogInfo(
                            $"[LateJoinManager] Starting ResyncExtractionPointItems coroutine for {nickname} in EP '{epToResync.name}'."
                        );

                        // Use the CoroutineRunner property instead of Instance
                        MonoBehaviour? runner = LateJoinEntry.CoroutineRunner;
                        if (runner != null)
                        {
                            runner.StartCoroutine(
                                ResyncExtractionPointItems(targetPlayer, epToResync)
                            );
                        }

                        else
                        {
                            // Log an error if no suitable MonoBehaviour was found to
                            // run the coroutine
                            LateJoinEntry.Log.LogError(
                                "[LateJoinManager] Cannot start "
                                    + "ResyncExtractionPointItems: "
                                    + "LateJoinEntry.CoroutineRunner is null! Ensure "
                                    + "Utilities.FindCoroutineRunner() can find an active "
                                    + "MonoBehaviour."
                            );
                        }
                    }
                    else
                    {
                        LateJoinEntry.Log.LogInfo(
                            $"[LateJoinManager] No suitable EP identified for item resync for {nickname}."
                        );
                    }
                }
                else
                {
                    LateJoinEntry.Log.LogDebug(
                        "[LateJoinManager] Not Master Client, " + "skipping EP item resync trigger."
                    );
                }
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager] CRITICAL ERROR during SyncAllStateForPlayer for {nickname}: {ex}"
                );
                ClearPlayerTracking(actorNr);
            }
            finally
            {
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager] === SyncAllStateForPlayer Finished for {nickname} ==="
                );
            }
        }
        #endregion

        #region NEW Module Sync Method
        /// <summary>
        /// Finds all Module components and re-sends their connection state RPC
        /// specifically to the target late-joining player.
        /// </summary>
        private static void SyncModuleConnectionStatesForPlayer(Player targetPlayer)
        {
            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            LateJoinEntry.Log.LogInfo($"[LateJoinManager][Module Sync] Starting Module connection sync for {nick}.");

            // Check if reflection fields are loaded
            if (Utilities.modSetupDoneField == null || Utilities.modConnectingTopField == null ||
                Utilities.modConnectingBottomField == null || Utilities.modConnectingRightField == null ||
                Utilities.modConnectingLeftField == null || Utilities.modFirstField == null)
            {
                LateJoinEntry.Log.LogError("[LateJoinManager][Module Sync] Critical reflection failure: Required Module fields not found in Utilities. Aborting sync.");
                return;
            }

            Module[] allModules = Object.FindObjectsOfType<Module>(true);
            if (allModules == null || allModules.Length == 0)
            {
                LateJoinEntry.Log.LogWarning("[LateJoinManager][Module Sync] Found 0 Module components. Skipping sync.");
                return;
            }

            int syncedCount = 0;
            int skippedCount = 0;

            foreach (Module module in allModules)
            {
                if (module == null) // Basic null check for the module itself
                {
                    LateJoinEntry.Log.LogWarning($"[LateJoinManager][Module Sync] Encountered null module instance. Skipping.");
                    skippedCount++;
                    continue;
                }

                PhotonView? pv = Utilities.GetPhotonView(module);
                if (pv == null)
                {
                    LateJoinEntry.Log.LogWarning($"[LateJoinManager][Module Sync] Module '{module.gameObject?.name ?? "NULL"}' is missing PhotonView. Skipping.");
                    skippedCount++;
                    continue;
                }

                try
                {
                    // --- Use Reflection to get module state ---
                    bool setupDone = (bool)(Utilities.modSetupDoneField.GetValue(module) ?? false);

                    if (!setupDone) // Skip modules that aren't fully set up on the host
                    {
                        LateJoinEntry.Log.LogDebug($"[LateJoinManager][Module Sync] Skipping module '{module.gameObject?.name ?? "NULL GameObject"}' (ViewID: {pv.ViewID}): Not SetupDone on host.");
                        skippedCount++;
                        continue;
                    }

                    // Get the connection state directly using reflection
                    bool top = (bool)(Utilities.modConnectingTopField.GetValue(module) ?? false);
                    bool bottom = (bool)(Utilities.modConnectingBottomField.GetValue(module) ?? false);
                    bool right = (bool)(Utilities.modConnectingRightField.GetValue(module) ?? false);
                    bool left = (bool)(Utilities.modConnectingLeftField.GetValue(module) ?? false);
                    bool first = (bool)(Utilities.modFirstField.GetValue(module) ?? false);
                    // ------------------------------------------

                    LateJoinEntry.Log.LogDebug($"[LateJoinManager][Module Sync] Syncing module '{module.gameObject?.name ?? "NULL GameObject"}' (ViewID: {pv.ViewID}) state to {nick}: T={top}, B={bottom}, R={right}, L={left}, First={first}");

                    // Send the existing RPC, but targeted only at the late joiner
                    pv.RPC("ModuleConnectionSetRPC", targetPlayer, top, bottom, right, left, first);
                    syncedCount++;
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError($"[LateJoinManager][Module Sync] Error processing or sending ModuleConnectionSetRPC for module '{module.gameObject?.name ?? "NULL"}' (ViewID: {pv.ViewID}) to {nick}: {ex}");
                    skippedCount++; // Count as skipped on error
                }
            }

            LateJoinEntry.Log.LogInfo($"[LateJoinManager][Module Sync] Finished Module connection sync for {nick}. Synced: {syncedCount}, Skipped: {skippedCount} (Out of {allModules.Length} total).");
        }
        #endregion // End NEW Module Sync Method

        #region Individual Sync Methods(Unchanged from previous version)
        private static void SyncLevelState(Player targetPlayer)
        {
            string targetPlayerNickname =
                targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            RunManager? runManager = RunManager.instance;
            if (runManager == null || runManager.levelCurrent == null)
            {
                LateJoinEntry.Log.LogError(
                    "[LateJoinManager][Level Sync] Null RunManager/levelCurrent."
                );
                return;
            }
            if (
                Utilities.rmRunManagerPUNField == null
                || Utilities.rmpPhotonViewField == null
                || Utilities.rmGameOverField == null
            )
            {
                LateJoinEntry.Log.LogError("[LateJoinManager][Level Sync] Null reflection fields.");
                return;
            }
            LevelGenerator? levelGen = LevelGenerator.Instance;
            try
            {
                object? runManagerPUNObj = Utilities.rmRunManagerPUNField.GetValue(runManager);
                RunManagerPUN? runManagerPUN = runManagerPUNObj as RunManagerPUN;
                if (runManagerPUN == null)
                    throw new Exception("Null RunManagerPUN");
                PhotonView? punPhotonView =
                    Utilities.rmpPhotonViewField.GetValue(runManagerPUN) as PhotonView;
                if (punPhotonView == null)
                    throw new Exception("Null RunManagerPUN PV");
                string levelName = runManager.levelCurrent.name;
                int levelsCompleted = runManager.levelsCompleted;
                bool gameOver = (bool)(Utilities.rmGameOverField.GetValue(runManager) ?? false);
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Level Sync] Sending UpdateLevelRPC to {targetPlayerNickname}. Lvl:{levelName}, Comp:{levelsCompleted}, Over:{gameOver}"
                );
                punPhotonView.RPC(
                    "UpdateLevelRPC",
                    targetPlayer,
                    levelName,
                    levelsCompleted,
                    gameOver
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Level Sync] Error sending UpdateLevelRPC: {ex}"
                );
                return;
            }
            if (levelGen != null && levelGen.PhotonView != null)
            {
                try
                {
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager][Level Sync] Sending GenerateDone RPC to {targetPlayerNickname}."
                    );
                    levelGen.PhotonView.RPC("GenerateDone", targetPlayer);
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError(
                        $"[LateJoinManager][Level Sync] Error sending GenerateDone RPC: {ex}"
                    );
                }
            }
            else
            {
                LateJoinEntry.Log.LogWarning(
                    $"[LateJoinManager][Level Sync] Skipped GenerateDone RPC."
                );
            }
        }

        /// <summary>
        /// Synchronizes the state of various interactive items (Toggle, Battery,
        /// Mine state) for a late-joining player.
        /// </summary>
        private static void SyncAllItemStatesForPlayer(Player targetPlayer)
        {
            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Item State Sync] Starting FULL item state sync for {nick}."
            );

            int syncedTogglesState = 0;
            int syncedTogglesDisabled = 0;
            int syncedBatteries = 0;
            int syncedMines = 0;
            int syncedMelees = 0;
            int syncedDronesActivated = 0;
            int syncedGrenadesActive = 0;
            int syncedTrackerTargets = 0;
            int syncedHealthPacksUsed = 0;

            // --- Sync ItemToggle States (ON/OFF) ---
            // This syncs the basic toggleState using ToggleItemRPC
            ItemToggle[] allToggles = Array.Empty<ItemToggle>(); // Cache for reuse in the disabled check
            try
            {
                allToggles = Object.FindObjectsOfType<ItemToggle>(); // Find all toggles
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allToggles.Length} ItemToggles for State/Disabled sync."
                );
                foreach (ItemToggle itemToggle in allToggles)
                {
                    if (itemToggle == null)
                        continue;
                    PhotonView pv = itemToggle.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    bool hostToggleState = itemToggle.toggleState;
                    pv.RPC("ToggleItemRPC", targetPlayer, hostToggleState, -1); // Sync the basic ON/OFF state
                    syncedTogglesState++;
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedTogglesState} ItemToggle ON/OFF states for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemToggle ON/OFF states: {ex}"
                );
            }

            // --- Sync ItemToggle Disabled State ---
            // This specifically syncs if the toggle itself is interactable, using
            // ToggleDisableRPC
            try
            {
                FieldInfo? disabledField = Utilities.itDisabledField;
                if (disabledField == null)
                {
                    LateJoinEntry.Log.LogError(
                        $"[LateJoinManager][Item State Sync] Reflection failed: ItemToggle.disabled field not found. Skipping disabled sync."
                    );
                }
                else
                {
                    // Reuse the array found above
                    foreach (ItemToggle itemToggle in allToggles)
                    {
                        if (itemToggle == null)
                            continue;
                        PhotonView pv = itemToggle.GetComponent<PhotonView>();
                        if (pv == null)
                            continue;

                        try
                        {
                            object? hostDisabledObj = disabledField.GetValue(itemToggle);
                            if (hostDisabledObj == null)
                                continue;
                            bool hostIsDisabled = (bool)hostDisabledObj;

                            if (hostIsDisabled) // Only need to send the RPC if it is disabled
                            {
                                pv.RPC("ToggleDisableRPC", targetPlayer, true);
                                syncedTogglesDisabled++;
                            }
                        }
                        catch (Exception refEx)
                        {
                            LateJoinEntry.Log.LogError(
                                $"[LateJoinManager][Item State Sync] Reflection error getting ItemToggle.disabled for '{itemToggle.gameObject.name}': {refEx}"
                            );
                        }
                    }
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager][Item State Sync] Synced {syncedTogglesDisabled} ItemToggle DISABLED states for {nick}."
                    );
                }
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemToggle DISABLED states: {ex}"
                );
            }

            // --- Sync ItemBattery Levels ---
            try
            {
                ItemBattery[] allBatteries = Object.FindObjectsOfType<ItemBattery>();
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allBatteries.Length} ItemBatteries."
                );
                foreach (ItemBattery itemBattery in allBatteries)
                {
                    if (itemBattery == null)
                        continue;
                    PhotonView pv = itemBattery.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    float hostBatteryLife = itemBattery.batteryLife;
                    int hostBatteryLifeInt = 0;
                    if (hostBatteryLife > 0f)
                    {
                        hostBatteryLifeInt = (int)Mathf.Round(hostBatteryLife / 16.6f);
                    }
                    pv.RPC("BatteryFullPercentChangeRPC", targetPlayer, hostBatteryLifeInt, false);
                    syncedBatteries++;
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedBatteries} ItemBattery levels for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemBattery levels: {ex}"
                );
            }

            // --- Sync ItemMine States (Using Reflection) ---
            try
            {
                ItemMine[] allMines = Object.FindObjectsOfType<ItemMine>();
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allMines.Length} ItemMines."
                );
                foreach (ItemMine itemMine in allMines)
                {
                    if (itemMine == null)
                        continue;
                    PhotonView pv = itemMine.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    FieldInfo? stateField = Utilities.imStateField;
                    if (stateField == null)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection failed: ItemMine.state field not found."
                        );
                        continue;
                    }
                    try
                    {
                        object? hostStateObj = stateField.GetValue(itemMine);
                        if (hostStateObj == null)
                            continue;
                        int hostMineStateInt = (int)hostStateObj;
                        pv.RPC("StateSetRPC", targetPlayer, hostMineStateInt);
                        syncedMines++;
                    }
                    catch (Exception refEx)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection error getting ItemMine state for '{itemMine.gameObject.name}': {refEx}"
                        );
                    }
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedMines} ItemMine states for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemMine states: {ex}"
                );
            }

            // --- Sync ItemMelee Broken State (Using Reflection) ---
            try
            {
                ItemMelee[] allMelees = Object.FindObjectsOfType<ItemMelee>();
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allMelees.Length} ItemMelees."
                );
                foreach (ItemMelee itemMelee in allMelees)
                {
                    if (itemMelee == null)
                        continue;
                    PhotonView pv = itemMelee.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    ItemBattery meleeBattery = itemMelee.GetComponent<ItemBattery>();
                    PhysGrabObject meleePGO = itemMelee.GetComponent<PhysGrabObject>();
                    if (meleeBattery == null || meleePGO == null)
                    {
                        LateJoinEntry.Log.LogWarning(
                            $"[LateJoinManager][Item State Sync] Skipping melee item '{itemMelee.gameObject.name}' - missing components."
                        );
                        continue;
                    }
                    FieldInfo? isMeleeField = Utilities.pgoIsMeleeField;
                    if (isMeleeField == null)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection failed: PhysGrabObject.isMelee field not found."
                        );
                        continue;
                    }
                    try
                    {
                        bool hostIsBroken = meleeBattery.batteryLife <= 0f;
                        object? hostPGOIsMeleeObj = isMeleeField.GetValue(meleePGO);
                        if (hostPGOIsMeleeObj == null)
                            continue;
                        bool hostPGOIsMelee = (bool)hostPGOIsMeleeObj;
                        if (hostIsBroken && hostPGOIsMelee)
                        {
                            pv.RPC("MeleeBreakRPC", targetPlayer);
                            syncedMelees++;
                        }
                        else if (!hostIsBroken && !hostPGOIsMelee)
                        {
                            pv.RPC("MeleeFixRPC", targetPlayer);
                            syncedMelees++;
                        }
                    }
                    catch (Exception refEx)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection error getting PhysGrabObject.isMelee for '{meleePGO.gameObject.name}': {refEx}"
                        );
                    }
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedMelees} ItemMelee broken states for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemMelee states: {ex}"
                );
            }

            // --- Sync ItemDrone Activated State ---
            // We rely on the ItemToggle sync primarily, but send ButtonToggleRPC if active for robustness.
            try
            {
                ItemDrone[] allDrones = Object.FindObjectsOfType<ItemDrone>();
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allDrones.Length} ItemDrones."
                );
                foreach (ItemDrone drone in allDrones)
                {
                    if (drone == null)
                        continue;
                    PhotonView pv = drone.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    ItemToggle droneToggle = drone.GetComponent<ItemToggle>(); // Drones use ItemToggle

                    if (droneToggle != null && droneToggle.toggleState) // Check the toggle state
                    {
                        // Explicitly call ButtonToggleRPC to ensure activation logic runs
                        pv.RPC("ButtonToggleRPC", targetPlayer, true);
                        syncedDronesActivated++;
                    }
                    // If toggleState is false, the previous ToggleItemRPC sync should handle deactivation.
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedDronesActivated} ItemDrone ACTIVATED states for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemDrone activated states: {ex}"
                );
            }

            // --- Sync ItemHealthPack Used State (USING REFLECTION) ---
            try
            {
                ItemHealthPack[] allHealthPacks = Object.FindObjectsOfType<ItemHealthPack>();
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allHealthPacks.Length} ItemHealthPacks."
                );
                foreach (ItemHealthPack healthPack in allHealthPacks)
                {
                    if (healthPack == null)
                        continue;
                    PhotonView pv = healthPack.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;

                    FieldInfo? usedField = Utilities.ihpUsedField;
                    if (usedField == null)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection failed: ItemHealthPack.used field not found."
                        );
                        continue;
                    }

                    try
                    {
                        object? hostUsedObj = usedField.GetValue(healthPack);
                        if (hostUsedObj == null)
                            continue;
                        bool hostIsUsed = (bool)hostUsedObj;

                        if (hostIsUsed)
                        {
                            // If used on host, call the RPC on the client to trigger particles, disable toggle etc.
                            pv.RPC("UsedRPC", targetPlayer);
                            syncedHealthPacksUsed++;
                        }
                        // If not used, no RPC needed as that's the default state.
                    }
                    catch (Exception refEx)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection error getting ItemHealthPack.used for '{healthPack.gameObject.name}': {refEx}"
                        );
                    }
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedHealthPacksUsed} USED ItemHealthPack states for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemHealthPack used states: {ex}"
                );
            }

            // --- Sync ItemGrenade Active State (USING REFLECTION) ---
            try
            {
                ItemGrenade[] allGrenades = Object.FindObjectsOfType<ItemGrenade>();
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allGrenades.Length} ItemGrenades."
                );
                foreach (ItemGrenade grenade in allGrenades)
                {
                    if (grenade == null)
                        continue;
                    PhotonView pv = grenade.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    FieldInfo? isActiveField = Utilities.igIsActiveField;
                    if (isActiveField == null)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection failed: ItemGrenade.isActive field not found."
                        );
                        continue;
                    }
                    try
                    {
                        object? hostIsActiveObj = isActiveField.GetValue(grenade);
                        if (hostIsActiveObj == null)
                            continue;
                        bool hostIsActive = (bool)hostIsActiveObj;
                        if (hostIsActive)
                        {
                            pv.RPC("TickStartRPC", targetPlayer);
                            syncedGrenadesActive++;
                        }
                    }
                    catch (Exception refEx)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection error getting ItemGrenade.isActive for '{grenade.gameObject.name}': {refEx}"
                        );
                    }
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedGrenadesActive} ItemGrenade active states (Active Only) for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemGrenade active states: {ex}"
                );
            }

            // --- Sync ItemTracker Target State (USING REFLECTION) ---
            try
            {
                ItemTracker[] allTrackers = Object.FindObjectsOfType<ItemTracker>();
                LateJoinEntry.Log.LogDebug(
                    $"[LateJoinManager][Item State Sync] Found {allTrackers.Length} ItemTrackers."
                );
                foreach (ItemTracker tracker in allTrackers)
                {
                    if (tracker == null)
                        continue;
                    PhotonView pv = tracker.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    FieldInfo? targetField = Utilities.itCurrentTargetField;
                    if (targetField == null)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection failed: ItemTracker.currentTarget field not found."
                        );
                        continue;
                    }
                    try
                    {
                        object? hostTargetObj = targetField.GetValue(tracker);
                        if (
                            hostTargetObj is Transform hostTargetTransform
                            && hostTargetTransform != null
                        )
                        {
                            PhotonView? targetPV =
                                hostTargetTransform.GetComponentInParent<PhotonView>();
                            if (targetPV != null)
                            {
                                int targetViewID = targetPV.ViewID;
                                pv.RPC("SetTargetRPC", targetPlayer, targetViewID);
                                syncedTrackerTargets++;
                            }
                            else
                            {
                                LateJoinEntry.Log.LogWarning(
                                    $"[LateJoinManager][Item State Sync] Tracker '{tracker.gameObject.name}' has target '{hostTargetTransform.name}', but target has no PhotonView. Cannot sync target."
                                );
                            }
                        }
                    }
                    catch (Exception refEx)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item State Sync] Reflection error getting ItemTracker.currentTarget for '{tracker.gameObject.name}': {refEx}"
                        );
                    }
                }
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item State Sync] Synced {syncedTrackerTargets} ItemTracker targets (Targets Only) for {nick}."
                );
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][Item State Sync] Error syncing ItemTracker targets: {ex}"
                );
            }

            // --- FINAL LOG ---
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Item State Sync] Finished FULL item state sync for {nick}. Totals: "
                    + $"TogglesState={syncedTogglesState}, TogglesDisabled={syncedTogglesDisabled}, Batteries={syncedBatteries}, Mines={syncedMines}, Melees={syncedMelees}, "
                    + $"DronesActivated={syncedDronesActivated}, GrenadesActive={syncedGrenadesActive}, TrackerTargets={syncedTrackerTargets}, HealthPacksUsed={syncedHealthPacksUsed}"
            );
        }

        private static void SyncExtractionPointsForPlayer(Player targetPlayer)
        {
            string targetPlayerNickname =
                targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            LateJoinEntry.Log.LogDebug(
                $"[LateJoinManager][EP Sync] Starting EP state/goal/surplus sync for {targetPlayerNickname}."
            );
            if (
                RoundDirector.instance == null
                || Utilities.epCurrentStateField == null
                || Utilities.rdExtractionPointActiveField == null
                || Utilities.rdExtractionPointSurplusField == null
                || Utilities.epHaulGoalFetchedField == null
                || Utilities.rdExtractionPointCurrentField == null
                || Utilities.epIsShopField == null
            )
            {
                LateJoinEntry.Log.LogError(
                    "[LateJoinManager][EP Sync] Critical instance or field missing."
                );
                return;
            }
            ExtractionPoint[]? allExtractionPoints = Object.FindObjectsOfType<ExtractionPoint>();
            int hostSurplus = 0;
            bool isAnyEpActiveOnHost = false;
            ExtractionPoint? currentActiveEpOnHost = null;
            try
            {
                hostSurplus = (int)(
                    Utilities.rdExtractionPointSurplusField.GetValue(RoundDirector.instance) ?? 0
                );
                isAnyEpActiveOnHost = (bool)(
                    Utilities.rdExtractionPointActiveField.GetValue(RoundDirector.instance) ?? false
                );
                currentActiveEpOnHost =
                    Utilities.rdExtractionPointCurrentField.GetValue(RoundDirector.instance)
                    as ExtractionPoint;
                if (isAnyEpActiveOnHost && currentActiveEpOnHost == null)
                    isAnyEpActiveOnHost = false;
                else if (!isAnyEpActiveOnHost && currentActiveEpOnHost != null)
                    currentActiveEpOnHost = null;
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][EP Sync] Error reflecting RD state: {ex}. Aborting."
                );
                return;
            }
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][EP Sync] Host EP Active:{isAnyEpActiveOnHost}. Current:{currentActiveEpOnHost?.name ?? "None"}. Surplus:{hostSurplus}"
            );
            PhotonView? firstEpPvForGlobalUnlock = null;
            if (allExtractionPoints != null)
            {
                foreach (ExtractionPoint ep in allExtractionPoints)
                {
                    if (ep == null)
                        continue;
                    PhotonView? pv = ep.GetComponent<PhotonView>();
                    if (pv == null)
                        continue;
                    if (firstEpPvForGlobalUnlock == null)
                        firstEpPvForGlobalUnlock = pv;
                    try
                    {
                        ExtractionPoint.State hostState = (ExtractionPoint.State)(
                            Utilities.epCurrentStateField.GetValue(ep) ?? ExtractionPoint.State.Idle
                        );
                        bool isThisTheShopEP = (bool)(
                            Utilities.epIsShopField.GetValue(ep) ?? false
                        );
                        pv.RPC("StateSetRPC", targetPlayer, hostState);
                        pv.RPC("ExtractionPointSurplusRPC", targetPlayer, hostSurplus);
                        if (
                            isAnyEpActiveOnHost
                            && currentActiveEpOnHost != null
                            && !isThisTheShopEP
                        )
                        {
                            if (ep == currentActiveEpOnHost)
                            {
                                bool hostGoalFetched = (bool)(
                                    Utilities.epHaulGoalFetchedField.GetValue(ep) ?? false
                                );
                                if (hostGoalFetched && ep.haulGoal > 0)
                                {
                                    pv.RPC("HaulGoalSetRPC", targetPlayer, ep.haulGoal);
                                }
                            }
                            else if (hostState == ExtractionPoint.State.Idle)
                            {
                                pv.RPC("ButtonDenyRPC", targetPlayer);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][EP Sync] RPC Error for EP '{ep.name}': {ex}"
                        );
                    }
                }
            }
            if (!isAnyEpActiveOnHost)
            {
                if (firstEpPvForGlobalUnlock != null)
                {
                    try
                    {
                        firstEpPvForGlobalUnlock.RPC("ExtractionPointsUnlockRPC", targetPlayer);
                    }
                    catch (Exception unlockEx)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][EP Sync] Failed global UnlockRPC: {unlockEx}"
                        );
                    }
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        "[LateJoinManager][EP Sync] Cannot send global unlock RPC."
                    );
                }
            }
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][EP Sync] Finished EP state/goal/surplus sync for {targetPlayerNickname}."
            );
        }

        private static void SyncAllValuablesForPlayer(Player targetPlayer)
        {
            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            if (Utilities.voDollarValueSetField == null)
            {
                LateJoinEntry.Log.LogError("[LateJoinManager][Valuable Sync] Reflection failed.");
                return;
            }
            ValuableObject[]? allValuables = Object.FindObjectsOfType<ValuableObject>();
            if (allValuables == null || allValuables.Length == 0)
            {
                LateJoinEntry.Log.LogWarning(
                    $"[LateJoinManager][Valuable Sync] Found 0 valuables."
                );
                return;
            }
            int syncedCount = 0;
            foreach (ValuableObject valuable in allValuables)
            {
                if (valuable == null)
                    continue;
                PhotonView? pv = valuable.GetComponent<PhotonView>();
                if (pv == null)
                    continue;
                bool isValueSet = false;
                try
                {
                    isValueSet = (bool)(
                        Utilities.voDollarValueSetField.GetValue(valuable) ?? false
                    );
                }
                catch
                {
                    continue;
                }
                if (isValueSet)
                {
                    try
                    {
                        pv.RPC("DollarValueSetRPC", targetPlayer, valuable.dollarValueCurrent);
                        syncedCount++;
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log.LogWarning(
                            $"[LateJoinManager][Valuable Sync] RPC failed for {valuable.name}: {ex.Message}"
                        );
                    }
                }
            }
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Valuable Sync] Synced values for {syncedCount}/{allValuables.Length} valuables for {nick}."
            );
        }

        private static void SyncAllShopItemsForPlayer(Player targetPlayer)
        {
            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            if (Utilities.iaValueField == null || Utilities.iaShopItemField == null)
            {
                LateJoinEntry.Log.LogError("[LateJoinManager][Shop Sync] Reflection failed.");
                return;
            }
            ItemAttributes[]? allItems = Object.FindObjectsOfType<ItemAttributes>();
            if (allItems == null || allItems.Length == 0)
            {
                LateJoinEntry.Log.LogWarning($"[LateJoinManager][Shop Sync] Found 0 items.");
                return;
            }
            int syncedCount = 0;
            foreach (ItemAttributes itemAttr in allItems)
            {
                if (itemAttr == null)
                    continue;
                bool isShopItem = false;
                try
                {
                    isShopItem = (bool)(Utilities.iaShopItemField.GetValue(itemAttr) ?? false);
                }
                catch
                {
                    continue;
                }
                if (!isShopItem)
                    continue;
                PhotonView? pv = itemAttr.GetComponent<PhotonView>();
                if (pv == null)
                    continue;
                int hostValue = 0;
                try
                {
                    hostValue = (int)(Utilities.iaValueField.GetValue(itemAttr) ?? 0);
                }
                catch
                {
                    continue;
                }
                if (hostValue <= 0)
                    continue;
                try
                {
                    pv.RPC("GetValueRPC", targetPlayer, hostValue);
                    syncedCount++;
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogWarning(
                        $"[LateJoinManager][Shop Sync] RPC failed for {itemAttr.name}: {ex.Message}"
                    );
                }
            }
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Shop Sync] Synced {syncedCount} shop items for {nick}."
            );
        }

        private static void SyncPlayerDeathState(Player targetPlayer, PlayerAvatar playerAvatar)
        {
            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            if (!ConfigManager.KillIfPreviouslyDead.Value)
            {
                LateJoinEntry.Log.LogDebug("[LateJoinManager][Death Sync] Disabled by config.");
                return;
            }
            PlayerStatus status = PlayerStateManager.GetPlayerStatus(targetPlayer);
            if (status == PlayerStatus.Dead)
            {
                PhotonView? pv = Utilities.GetPhotonView(playerAvatar);
                if (pv == null)
                {
                    LateJoinEntry.Log.LogError(
                        $"[LateJoinManager][Death Sync] Null PV for {nick}."
                    );
                    return;
                }
                bool isDisabled = false,
                    isDeadSet = false;
                try
                {
                    if (Utilities.paIsDisabledField != null)
                        isDisabled = (bool)(
                            Utilities.paIsDisabledField.GetValue(playerAvatar) ?? false
                        );
                }
                catch { }
                try
                {
                    if (Utilities.paDeadSetField != null)
                        isDeadSet = (bool)(
                            Utilities.paDeadSetField.GetValue(playerAvatar) ?? false
                        );
                }
                catch { }
                if (isDisabled || isDeadSet)
                {
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager][Death Sync] Player {nick} already dead/disabled."
                    );
                    return;
                }
                try
                {
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager][Death Sync] Sending PlayerDeathRPC for {nick}."
                    );
                    pv.RPC("PlayerDeathRPC", RpcTarget.AllBuffered, -1);
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError(
                        $"[LateJoinManager][Death Sync] Error sending RPC: {ex}"
                    );
                }
            }
        }

        private static void SyncTruckScreenForPlayer(Player targetPlayer)
        {
            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            TruckScreenText? screen = TruckScreenText.instance;
            if (screen == null)
            {
                LateJoinEntry.Log.LogWarning("[LateJoinManager][Truck Sync] Instance null.");
                return;
            }
            try
            {
                PhotonView? pv = screen.GetComponent<PhotonView>();
                if (pv == null)
                {
                    LateJoinEntry.Log.LogWarning("[LateJoinManager][Truck Sync] PV null.");
                    return;
                }
                if (Utilities.tstCurrentPageIndexField == null)
                {
                    LateJoinEntry.Log.LogError(
                        "[LateJoinManager][Truck Sync] Reflection field null."
                    );
                    return;
                }
                pv.RPC("InitializeTextTypingRPC", targetPlayer);
                int hostPage = -1;
                try
                {
                    hostPage = (int)(Utilities.tstCurrentPageIndexField.GetValue(screen) ?? -1);
                }
                catch
                {
                    hostPage = -1;
                }
                if (hostPage >= 0)
                {
                    pv.RPC("GotoPageRPC", targetPlayer, hostPage);
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager][Truck Sync] Synced page {hostPage} for {nick}."
                    );
                }
                else
                {
                    LateJoinEntry.Log.LogInfo(
                        $"[LateJoinManager][Truck Sync] Synced init for {nick}. Host page invalid."
                    );
                }
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError($"[LateJoinManager][Truck Sync] Error: {ex}");
            }
        }

        private static void TriggerPropSwitchSetup(Player targetPlayer)
        {
            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            ValuableDirector? director = ValuableDirector.instance;
            if (director == null)
            {
                LateJoinEntry.Log.LogWarning($"[LateJoinManager][PropSwitch Sync] Instance null.");
                return;
            }
            PhotonView? directorPV = director.GetComponent<PhotonView>();
            if (directorPV == null)
            {
                LateJoinEntry.Log.LogWarning($"[LateJoinManager][PropSwitch Sync] PV null.");
                return;
            }
            try
            {
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][PropSwitch Sync] Sending VolumesAndSwitchSetupRPC to {nick}."
                );
                directorPV.RPC("VolumesAndSwitchSetupRPC", targetPlayer);
            }
            catch (Exception ex)
            {
                LateJoinEntry.Log.LogError(
                    $"[LateJoinManager][PropSwitch Sync] Error sending RPC: {ex}"
                );
            }
        }

        #endregion

        /// <summary>
        /// Manages the host-side coroutine for synchronizing Arena platform states
        /// to late-joining players.
        /// </summary>
        public class HostArenaPlatformSyncManager : MonoBehaviourPunCallbacks
        {
            // Keep this private to the manager, LateJoinManager will initiate it
            private static HostArenaPlatformSyncManager? _instance;
            public static HostArenaPlatformSyncManager Instance
            {
                get
                {
                    if (_instance == null)
                    {
                        // Try to find an existing instance first
                        _instance = FindObjectOfType<HostArenaPlatformSyncManager>();
                        if (_instance == null)
                        {
                            // If not found, create it
                            GameObject go = new GameObject("HostArenaPlatformSyncManager_LATE");
                            _instance = go.AddComponent<HostArenaPlatformSyncManager>();
                            DontDestroyOnLoad(go); // Make it persistent if needed across scene loads
                            LateJoinEntry.Log.LogInfo("[HostArenaPlatformSyncManager] Created instance.");
                        }
                    }
                    return _instance;
                }
            }

            private const float RPC_CATCHUP_DELAY = 0.30f; // Tuned slightly from 0.25f

            // This method will be called by LateJoinManager.SyncArenaStateForPlayer
            public void StartPlatformCatchUpForPlayer(Player targetPlayer, Arena arenaInstance)
            {
                if (!PhotonNetwork.IsMasterClient || arenaInstance == null || targetPlayer == null)
                {
                    LateJoinEntry.Log.LogWarning("[HostArenaPlatformSyncManager] Cannot start catch-up: Not master, or null arena/player.");
                    return;
                }
                LateJoinEntry.Log.LogInfo($"[HostArenaPlatformSyncManager] Starting platform catch-up for {targetPlayer.NickName}.");
                StartCoroutine(CatchUpPlayerPlatformSequence(targetPlayer, arenaInstance));
            }

            private IEnumerator CatchUpPlayerPlatformSequence(Player targetPlayer, Arena arena)
            {
                yield return new WaitForSeconds(0.5f);

                // --- Get PhotonView and CurrentState via Reflection ---
                PhotonView? arenaPV = null;
                global::Arena.States hostCurrentArenaState = global::Arena.States.Idle; // Default
                int hostArenaLevel = 0;

                if (Utilities.arenaPhotonViewField != null)
                {
                    try { arenaPV = Utilities.arenaPhotonViewField.GetValue(arena) as PhotonView; }
                    catch (Exception ex) { LateJoinEntry.Log.LogError($"[HostArenaPlatformSyncManager] Error reflecting Arena.photonView: {ex}"); yield break; }
                }
                if (arenaPV == null)
                {
                    LateJoinEntry.Log.LogError("[HostArenaPlatformSyncManager] Arena.photonView is null after reflection. Aborting platform sync.");
                    yield break;
                }

                if (Utilities.arenaCurrentStateField != null)
                {
                    try { hostCurrentArenaState = (global::Arena.States)(Utilities.arenaCurrentStateField.GetValue(arena) ?? global::Arena.States.Idle); }
                    catch (Exception ex) { LateJoinEntry.Log.LogError($"[HostArenaPlatformSyncManager] Error reflecting Arena.currentState: {ex}"); /* Continue with default or break */ }
                }
                else { LateJoinEntry.Log.LogError("[HostArenaPlatformSyncManager] Arena.currentState field info null."); }


                if (Utilities.arenaLevelField != null) // Changed from AccessTools.Field to Utilities
                {
                    try
                    {
                        hostArenaLevel = (int)(Utilities.arenaLevelField.GetValue(arena) ?? 0);
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log.LogError($"[HostArenaPlatformSyncManager] Error reflecting Arena.level: {ex}");
                        yield break;
                    }
                }
                else
                {
                    LateJoinEntry.Log.LogError("[HostArenaPlatformSyncManager] Arena.level field not found via Utilities. Aborting platform sync.");
                    yield break;
                }
                // --- End Reflection ---


                LateJoinEntry.Log.LogInfo($"[HostArenaPlatformSyncManager] Host Arena.level is {hostArenaLevel}. Current Arena state on host: {hostCurrentArenaState}");

                if (hostArenaLevel > 0)
                {
                    LateJoinEntry.Log.LogDebug($"[HostArenaPlatformSyncManager] Replaying {hostArenaLevel} platform stages for {targetPlayer.NickName}.");
                    for (int i = 0; i < hostArenaLevel; i++)
                    {
                        if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
                        {
                            LateJoinEntry.Log.LogWarning($"[HostArenaPlatformSyncManager] Player {targetPlayer.NickName} left during platform catch-up. Aborting.");
                            yield break;
                        }

                        LateJoinEntry.Log.LogDebug($"[HostArenaPlatformSyncManager] Sending PlatformWarning (for client level {i + 1}) to {targetPlayer.NickName}.");
                        arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformWarning); // Use reflected arenaPV
                        yield return new WaitForSeconds(RPC_CATCHUP_DELAY);

                        if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
                        {
                            LateJoinEntry.Log.LogWarning($"[HostArenaPlatformSyncManager] Player {targetPlayer.NickName} left during platform catch-up. Aborting.");
                            yield break;
                        }

                        LateJoinEntry.Log.LogDebug($"[HostArenaPlatformSyncManager] Sending PlatformRemove (for client level {i + 1}) to {targetPlayer.NickName}.");
                        arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformRemove); // Use reflected arenaPV
                        yield return new WaitForSeconds(RPC_CATCHUP_DELAY);
                    }
                    LateJoinEntry.Log.LogInfo($"[HostArenaPlatformSyncManager] Finished replaying {hostArenaLevel} platform stages for {targetPlayer.NickName}.");
                }
                else
                {
                    LateJoinEntry.Log.LogInfo($"[HostArenaPlatformSyncManager] No platform stages (hostArenaLevel = 0) to replay for {targetPlayer.NickName}.");
                }

                // Re-fetch current state in case it changed during the yield loop (unlikely for Arena but good practice)
                if (Utilities.arenaCurrentStateField != null)
                {
                    try { hostCurrentArenaState = (global::Arena.States)(Utilities.arenaCurrentStateField.GetValue(arena) ?? global::Arena.States.Idle); }
                    catch { /* Already logged */ }
                }

                LateJoinEntry.Log.LogInfo($"[HostArenaPlatformSyncManager] Setting final CURRENT Arena state ({hostCurrentArenaState}) for {targetPlayer.NickName}.");
                arenaPV.RPC("StateSetRPC", targetPlayer, hostCurrentArenaState); // Use reflected arenaPV
                yield return new WaitForSeconds(RPC_CATCHUP_DELAY);

                LateJoinEntry.Log.LogInfo($"[HostArenaPlatformSyncManager] Arena platform catch-up sequence complete for {targetPlayer.NickName}. Their Arena.level should now be approx {hostArenaLevel}.");
            }
        }

        /// <summary>
        /// Synchronizes Arena-specific state (cage destruction, winner) to a
        /// late-joining player by re-sending existing RPCs targeted only at that
        /// player. Called only when the host is in the Arena level during a
        /// late-join sync.
        /// </summary>
        private static void SyncArenaStateForPlayer(Player targetPlayer)
        {
            string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
            if (targetPlayer == null)
            {
                LateJoinEntry.Log.LogWarning("[LateJoinManager][Arena Sync] Target player is null. Aborting sync.");
                return;
            }

            LateJoinEntry.Log.LogInfo($"[LateJoinManager][Arena Sync] Starting FULL Arena state sync for {targetNickname} (ActorNr: {targetPlayer.ActorNumber}).");

            if (!Utilities.IsRealMasterClient())
            {
                LateJoinEntry.Log.LogDebug("[LateJoinManager][Arena Sync] Not Master Client. Skipping host-specific sync.");
                return;
            }

            Arena arenaInstance = Arena.instance;
            if (arenaInstance == null)
            {
                LateJoinEntry.Log.LogWarning("[LateJoinManager][Arena Sync] Arena.instance is null. Aborting.");
                return;
            }

            // --- Get Arena PhotonView via Reflection ---
            PhotonView? arenaPV = null;
            if (Utilities.arenaPhotonViewField != null)
            {
                try
                {
                    arenaPV = Utilities.arenaPhotonViewField.GetValue(arenaInstance) as PhotonView;
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError($"[LateJoinManager][Arena Sync] Error reflecting Arena.photonView: {ex}");
                    return; // Critical failure
                }
            }
            if (arenaPV == null)
            {
                LateJoinEntry.Log.LogWarning("[LateJoinManager][Arena Sync] Arena PhotonView is null after reflection. Aborting.");
                return;
            }
            LateJoinEntry.Log.LogDebug("[LateJoinManager][Arena Sync] Base pre-checks passed, Arena PV obtained.");


            // --- 1. Sync One-Off States (Cage, Winner, Pedestal) ---

            // Sync Cage Destruction
            bool hostCageIsDestroyed = false;
            if (Utilities.arenaCrownCageDestroyedField != null) // Use Utilities field
            {
                try
                {
                    object fieldValue = Utilities.arenaCrownCageDestroyedField.GetValue(arenaInstance);
                    if (fieldValue is bool) hostCageIsDestroyed = (bool)fieldValue;
                    else LateJoinEntry.Log.LogWarning($"[LateJoinManager][Arena Sync] Reflected 'crownCageDestroyed' was not bool.");
                }
                catch (Exception ex) { LateJoinEntry.Log.LogWarning($"[LateJoinManager][Arena Sync] Error reflecting 'crownCageDestroyed': {ex}"); }
            }
            else { LateJoinEntry.Log.LogWarning("[LateJoinManager][Arena Sync] Utilities.arenaCrownCageDestroyedField is null."); }

            if (hostCageIsDestroyed)
            {
                LateJoinEntry.Log.LogInfo($"[LateJoinManager][Arena Sync] Sending DestroyCrownCageRPC to {targetNickname}.");
                try { arenaPV.RPC("DestroyCrownCageRPC", targetPlayer); } // Use reflected arenaPV
                catch (Exception ex) { LateJoinEntry.Log.LogError($"[LateJoinManager][Arena Sync] Failed DestroyCrownCageRPC: {ex}"); }
            }

            // Sync Winner
            PlayerAvatar? currentWinner = null;
            if (Utilities.arenaWinnerPlayerField != null) // Use Utilities field
            {
                try { currentWinner = Utilities.arenaWinnerPlayerField.GetValue(arenaInstance) as PlayerAvatar; }
                catch (Exception ex) { LateJoinEntry.Log.LogWarning($"[LateJoinManager][Arena Sync] Error reflecting 'winnerPlayer': {ex}"); }
            }
            else { LateJoinEntry.Log.LogWarning("[LateJoinManager][Arena Sync] Utilities.arenaWinnerPlayerField is null."); }

            if (currentWinner != null)
            {
                int winnerPhysGrabberViewID = Utilities.GetPhysGrabberViewId(currentWinner);
                if (winnerPhysGrabberViewID > 0)
                {
                    LateJoinEntry.Log.LogInfo($"[LateJoinManager][Arena Sync] Sending CrownGrabRPC to {targetNickname}.");
                    try { arenaPV.RPC("CrownGrabRPC", targetPlayer, winnerPhysGrabberViewID); } // Use reflected arenaPV
                    catch (Exception ex) { LateJoinEntry.Log.LogError($"[LateJoinManager][Arena Sync] Failed CrownGrabRPC: {ex}"); }
                }
            }

            // Sync Pedestal Screen Player Count
            int actualLivePlayerCount = 0;
            GameDirector gameDirectorInstance = GameDirector.instance;
            if (gameDirectorInstance != null && Utilities.paIsDisabledField != null)
            {
                List<PlayerAvatar> currentPlayersInScene = gameDirectorInstance.PlayerList;
                if (currentPlayersInScene != null)
                {
                    foreach (PlayerAvatar playerAvatar in currentPlayersInScene)
                    {
                        if (playerAvatar == null) continue;
                        try
                        {
                            object isDisabledValue = Utilities.paIsDisabledField.GetValue(playerAvatar);
                            if (isDisabledValue is bool isDisabled && !isDisabled)
                            {
                                actualLivePlayerCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            LateJoinEntry.Log.LogError($"[LateJoinManager][Arena Sync] Error reflecting 'isDisabled' for pedestal: {ex}");
                            actualLivePlayerCount++; // Or some default handling
                        }
                    }
                }
            }


            LateJoinEntry.Log.LogInfo($"[LateJoinManager][Arena Sync] Calculated actual live player count: {actualLivePlayerCount}. Sending PlayerKilledRPC to {targetNickname}.");
            try { arenaPV.RPC("PlayerKilledRPC", targetPlayer, actualLivePlayerCount); } // Use reflected arenaPV
            catch (Exception ex) { LateJoinEntry.Log.LogError($"[LateJoinManager][Arena Sync] Error sending PlayerKilledRPC: {ex}"); }


            // --- 2. Sync Arena Platform States ---
            LateJoinEntry.Log.LogInfo($"[LateJoinManager][Arena Sync] Initiating Arena Platform catch-up sequence for {targetNickname}.");
            HostArenaPlatformSyncManager syncManager = HostArenaPlatformSyncManager.Instance;
            if (syncManager != null)
            {
                // Pass the arenaInstance (which is the Arena object itself)
                syncManager.StartPlatformCatchUpForPlayer(targetPlayer, arenaInstance);
            }
            else
            {
                LateJoinEntry.Log.LogError("[LateJoinManager][Arena Sync] HostArenaPlatformSyncManager instance is null! Cannot sync platforms.");
            }

            LateJoinEntry.Log.LogInfo($"[LateJoinManager][Arena Sync] Finished Arena state sync (main part) for {targetNickname}. Platform sync running in background.");
        }

        #region Extraction Point Item Resync Coroutine
        /// <summary>
        /// Coroutine to force late-joining clients to re-evaluate items in the
        /// active extraction point or the shop EP by teleporting items out and
        /// back.
        /// </summary>
        private static IEnumerator ResyncExtractionPointItems(
            Player targetPlayer,
            ExtractionPoint epToSync
        )
        {
            string targetNickname = targetPlayer?.NickName ?? "<Unknown>";
            string epName = epToSync?.name ?? "<Unknown EP>";
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Item Resync] Starting for {targetNickname} in EP '{epName}'"
            );

            // Safety Checks
            if (targetPlayer == null || epToSync == null || !Utilities.IsRealMasterClient())
            {
                LateJoinEntry.Log.LogWarning(
                    "[LateJoinManager][Item Resync] Aborting: Invalid state."
                );
                yield break;
            }
            if (
                PhotonNetwork.CurrentRoom == null
                || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)
            )
            {
                LateJoinEntry.Log.LogWarning(
                    $"[LateJoinManager][Item Resync] Aborting: Player {targetNickname} left."
                );
                yield break;
            }

            bool isShop = SemiFunc.RunIsShop();
            Vector3 farAwayPosition = new Vector3(
                epToSync.transform.position.x,
                epToSync.transform.position.y + 500f,
                epToSync.transform.position.z
            );
            List<GameObject> itemsToResync = new List<GameObject>();

            // --- Identify Items Based on Scene ---
            if (isShop)
            {
                LateJoinEntry.Log.LogDebug(
                    "[LateJoinManager][Item Resync] " + "Identifying items from ShopManager."
                );
                if (ShopManager.instance != null && Utilities.smShoppingListField != null) // Check instance and reflection field
                {
                    try
                    {
                        // Use reflection to get the shopping list
                        object? listObject = Utilities.smShoppingListField.GetValue(
                            ShopManager.instance
                        );
                        if (listObject is List<ItemAttributes> shopList) // Cast to the expected type
                        {
                            // Get GameObjects from ItemAttributes
                            foreach (ItemAttributes? itemAttr in shopList)
                            {
                                if (itemAttr != null && itemAttr.gameObject != null)
                                {
                                    itemsToResync.Add(itemAttr.gameObject);
                                }
                            }
                        }
                        else
                        {
                            LateJoinEntry.Log.LogWarning(
                                "[LateJoinManager][Item Resync] Reflected shoppingList "
                                    + "is null or not a List<ItemAttributes>."
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item Resync] Error reflecting or accessing ShopManager.shoppingList: {ex}"
                        );
                    }
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        "[LateJoinManager][Item Resync] ShopManager instance or "
                            + "shoppingList reflection field is null."
                    );
                }
            }
            else // Level scene
            {
                LateJoinEntry.Log.LogDebug(
                    "[LateJoinManager][Item Resync] Identifying items from "
                        + "RoundDirector.dollarHaulList."
                );
                if (RoundDirector.instance != null && RoundDirector.instance.dollarHaulList != null)
                {
                    itemsToResync = RoundDirector
                        .instance.dollarHaulList.Where(go => go != null)
                        .ToList();
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        "[LateJoinManager][Item Resync] RoundDirector instance or "
                            + "dollarHaulList is null."
                    );
                }
            }

            if (itemsToResync.Count == 0)
            {
                LateJoinEntry.Log.LogInfo(
                    $"[LateJoinManager][Item Resync] No items identified in EP '{epName}'."
                );
                yield break;
            }

            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Item Resync] Found {itemsToResync.Count} items in '{epName}' for {targetNickname}."
            );

            // --- Store Original Positions and Teleport Away ---
            Dictionary<int, (Vector3 pos, Quaternion rot)> originalTransforms =
                new Dictionary<int, (Vector3, Quaternion)>();
            List<PhysGrabObject> validPhysObjects = new List<PhysGrabObject>();

            foreach (GameObject itemGO in itemsToResync)
            {
                PhysGrabObject? pgo = itemGO.GetComponent<PhysGrabObject>();
                PhotonView? pv = Utilities.GetPhotonViewFromPGO(pgo);

                if (pgo == null || pv == null)
                {
                    LateJoinEntry.Log.LogWarning(
                        $"[LateJoinManager][Item Resync] Item '{itemGO.name}' missing PGO/PV. Skipping."
                    );
                    continue;
                }

                if (!pv.IsMine)
                {
                    LateJoinEntry.Log.LogDebug(
                        $"[LateJoinManager][Item Resync] Requesting ownership of {itemGO.name} (ViewID: {pv.ViewID})."
                    );
                    pv.RequestOwnership();
                }

                originalTransforms[pv.ViewID] = (
                    itemGO.transform.position,
                    itemGO.transform.rotation
                );
                validPhysObjects.Add(pgo);

                try
                {
                    LateJoinEntry.Log.LogDebug(
                        $"[LateJoinManager][Item Resync] Teleporting {itemGO.name} (ViewID: {pv.ViewID}) AWAY."
                    );
                    pgo.Teleport(farAwayPosition, itemGO.transform.rotation);
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError(
                        $"[LateJoinManager][Item Resync] Error teleporting {itemGO.name} AWAY: {ex}"
                    );
                }
            }

            // --- Wait Briefly ---
            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Item Resync] Waiting {itemResyncDelay}s..."
            );
            yield return new WaitForSeconds(itemResyncDelay);

            // --- Check Player Still Valid and Teleport Back ---
            if (
                PhotonNetwork.CurrentRoom == null
                || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)
            )
            {
                LateJoinEntry.Log.LogWarning(
                    $"[LateJoinManager][Item Resync] Aborting teleport BACK: Player {targetNickname} left."
                );
                yield break;
            }

            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Item Resync] Teleporting {validPhysObjects.Count} items back for {targetNickname}."
            );
            foreach (PhysGrabObject? pgo in validPhysObjects)
            {
                if (pgo == null || pgo.gameObject == null)
                    continue;
                PhotonView? pv = Utilities.GetPhotonViewFromPGO(pgo);
                if (pv == null)
                    continue;

                int viewID = pv.ViewID;
                if (originalTransforms.TryGetValue(viewID, out var originalTransform))
                {
                    if (!pv.IsMine)
                    {
                        LateJoinEntry.Log.LogWarning(
                            $"[LateJoinManager][Item Resync] Lost ownership of {pgo.gameObject.name}?"
                        );
                    }
                    try
                    {
                        LateJoinEntry.Log.LogDebug(
                            $"[LateJoinManager][Item Resync] Teleporting {pgo.gameObject.name} (ViewID: {viewID}) BACK to {originalTransform.pos}."
                        );
                        pgo.Teleport(originalTransform.pos, originalTransform.rot);
                    }
                    catch (Exception ex)
                    {
                        LateJoinEntry.Log.LogError(
                            $"[LateJoinManager][Item Resync] Error teleporting {pgo.gameObject.name} BACK: {ex}"
                        );
                    }
                }
                else
                {
                    LateJoinEntry.Log.LogWarning(
                        $"[LateJoinManager][Item Resync] No original transform for ViewID {viewID}."
                    );
                }
            }

            LateJoinEntry.Log.LogInfo(
                $"[LateJoinManager][Item Resync] Finished item resync sequence for {targetNickname} in EP '{epName}'."
            );
        }
        #endregion
    }
}