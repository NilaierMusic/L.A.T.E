// File: L.A.T.E/Managers/ItemSyncManager.cs
using LATE.Core;
using LATE.Utilities;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object; // Alias for UnityEngine.Object

namespace LATE.Managers;

/// <summary>
/// Responsible for synchronizing the state of various items and valuables
/// for late-joining players. This includes item toggles, batteries, mines,
/// shop items, and items in extraction points.
/// </summary>
internal static class ItemSyncManager
{
    private static readonly BepInEx.Logging.ManualLogSource Log = LatePlugin.Log;
    private const float LevelEpRpcsDelaySeconds = 0.1f;

    #region Interactive Item State Synchronization
    /// <summary>
    /// Synchronizes the state of various interactive items (e.g., <see cref="ItemToggle"/>,
    /// <see cref="ItemBattery"/>, <see cref="ItemMine"/>) for a late-joining player.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncAllItemStatesForPlayer(Player targetPlayer)
    {
        if (targetPlayer == null)
        {
            Log.LogError("[ItemSyncManager] SyncAllItemStatesForPlayer called with null targetPlayer. Aborting.");
            return;
        }
        string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        Log.LogInfo($"[ItemSyncManager] Starting full interactive item state sync for {targetNickname}.");

        int syncedTogglesState = 0, syncedTogglesDisabled = 0, syncedBatteries = 0, syncedMines = 0;
        int syncedMelees = 0, syncedDronesActivated = 0, syncedGrenadesActive = 0, syncedTrackerTargets = 0, syncedHealthPacksUsed = 0;

        // ItemToggle: On/Off State
        ItemToggle[] allToggles = Object.FindObjectsOfType<ItemToggle>(true);
        if (allToggles != null)
        {
            foreach (ItemToggle itemToggle in allToggles)
            {
                if (itemToggle == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemToggle);
                if (pv == null) continue;

                try
                {
                    bool hostToggleState = itemToggle.toggleState;
                    pv.RPC("ToggleItemRPC", targetPlayer, hostToggleState, -1);
                    syncedTogglesState++;
                }
                catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error sending ToggleItemRPC for '{itemToggle.gameObject.name}': {ex}"); }
            }
        }

        // ItemToggle: Disabled State
        if (ReflectionCache.ItemToggle_DisabledField != null && allToggles != null) // Re-check allToggles as it might be null if first block skipped
        {
            foreach (ItemToggle itemToggle in allToggles)
            {
                if (itemToggle == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemToggle);
                if (pv == null) continue;

                try
                {
                    bool hostIsDisabled = ReflectionCache.ItemToggle_DisabledField.GetValue(itemToggle) as bool? ?? false;
                    if (hostIsDisabled)
                    {
                        pv.RPC("ToggleDisableRPC", targetPlayer, true);
                        syncedTogglesDisabled++;
                    }
                }
                catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error reflecting/sending ItemToggle.disabled RPC for '{itemToggle.gameObject.name}': {ex}"); }
            }
        }

        // ItemBattery: Battery Levels
        ItemBattery[] allBatteries = Object.FindObjectsOfType<ItemBattery>(true);
        if (allBatteries != null)
        {
            foreach (ItemBattery itemBattery in allBatteries)
            {
                if (itemBattery == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemBattery);
                if (pv == null) continue;

                try
                {
                    float hostBatteryLife = itemBattery.batteryLife;
                    int hostBatteryLifeInt = (hostBatteryLife > 0f) ? (int)Mathf.Round(hostBatteryLife / 16.6f) : 0; // Game's specific conversion
                    pv.RPC("BatteryFullPercentChangeRPC", targetPlayer, hostBatteryLifeInt, false);
                    syncedBatteries++;
                }
                catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error sending BatteryFullPercentChangeRPC for '{itemBattery.gameObject.name}': {ex}"); }
            }
        }

        // ItemMine: States
        if (ReflectionCache.ItemMine_StateField != null)
        {
            ItemMine[] allMines = Object.FindObjectsOfType<ItemMine>(true);
            if (allMines != null)
            {
                foreach (ItemMine itemMine in allMines)
                {
                    if (itemMine == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(itemMine);
                    if (pv == null) continue;

                    try
                    {
                        int hostMineStateInt = ReflectionCache.ItemMine_StateField.GetValue(itemMine) as int? ?? 0;
                        pv.RPC("StateSetRPC", targetPlayer, hostMineStateInt);
                        syncedMines++;
                    }
                    catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error reflecting/sending ItemMine.state RPC for '{itemMine.gameObject.name}': {ex}"); }
                }
            }
        }

        // ItemMelee: Broken State
        if (ReflectionCache.PhysGrabObject_IsMeleeField != null)
        {
            ItemMelee[] allMelees = Object.FindObjectsOfType<ItemMelee>(true);
            if (allMelees != null)
            {
                foreach (ItemMelee itemMelee in allMelees)
                {
                    if (itemMelee == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(itemMelee);
                    if (pv == null) continue;

                    ItemBattery? meleeBattery = itemMelee.GetComponent<ItemBattery>();
                    PhysGrabObject? meleePGO = itemMelee.GetComponent<PhysGrabObject>();
                    if (meleeBattery == null || meleePGO == null) continue;

                    try
                    {
                        bool hostIsBroken = meleeBattery.batteryLife <= 0f;
                        bool hostPGOIsMelee = ReflectionCache.PhysGrabObject_IsMeleeField.GetValue(meleePGO) as bool? ?? false;

                        if (hostIsBroken && hostPGOIsMelee)
                        {
                            pv.RPC("MeleeBreakRPC", targetPlayer);
                            syncedMelees++;
                        }
                        else if (!hostIsBroken && !hostPGOIsMelee) // Potentially a "fix" scenario if isMelee was toggled off
                        {
                            pv.RPC("MeleeFixRPC", targetPlayer);
                            syncedMelees++;
                        }
                    }
                    catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error reflecting/sending ItemMelee state RPC for '{meleePGO.gameObject.name}': {ex}"); }
                }
            }
        }

        // ItemDrone: Activated State
        ItemDrone[] allDrones = Object.FindObjectsOfType<ItemDrone>(true);
        if (allDrones != null)
        {
            foreach (ItemDrone drone in allDrones)
            {
                if (drone == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(drone);
                if (pv == null) continue;
                ItemToggle? droneToggle = drone.GetComponent<ItemToggle>();

                if (droneToggle != null && droneToggle.toggleState)
                {
                    try
                    {
                        pv.RPC("ButtonToggleRPC", targetPlayer, true);
                        syncedDronesActivated++;
                    }
                    catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error sending ItemDrone ButtonToggleRPC for '{drone.gameObject.name}': {ex}"); }
                }
            }
        }

        // ItemHealthPack: Used State
        if (ReflectionCache.ItemHealthPack_UsedField != null)
        {
            ItemHealthPack[] allHealthPacks = Object.FindObjectsOfType<ItemHealthPack>(true);
            if (allHealthPacks != null)
            {
                foreach (ItemHealthPack healthPack in allHealthPacks)
                {
                    if (healthPack == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(healthPack);
                    if (pv == null) continue;

                    try
                    {
                        bool hostIsUsed = ReflectionCache.ItemHealthPack_UsedField.GetValue(healthPack) as bool? ?? false;
                        if (hostIsUsed)
                        {
                            pv.RPC("UsedRPC", targetPlayer);
                            syncedHealthPacksUsed++;
                        }
                    }
                    catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error reflecting/sending ItemHealthPack.used RPC for '{healthPack.gameObject.name}': {ex}"); }
                }
            }
        }

        // ItemGrenade: Active State
        if (ReflectionCache.ItemGrenade_IsActiveField != null)
        {
            ItemGrenade[] allGrenades = Object.FindObjectsOfType<ItemGrenade>(true);
            if (allGrenades != null)
            {
                foreach (ItemGrenade grenade in allGrenades)
                {
                    if (grenade == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(grenade);
                    if (pv == null) continue;

                    try
                    {
                        bool hostIsActive = ReflectionCache.ItemGrenade_IsActiveField.GetValue(grenade) as bool? ?? false;
                        if (hostIsActive)
                        {
                            pv.RPC("TickStartRPC", targetPlayer);
                            syncedGrenadesActive++;
                        }
                    }
                    catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error reflecting/sending ItemGrenade.isActive RPC for '{grenade.gameObject.name}': {ex}"); }
                }
            }
        }

        // ItemTracker: Target State
        if (ReflectionCache.ItemTracker_CurrentTargetField != null)
        {
            ItemTracker[] allTrackers = Object.FindObjectsOfType<ItemTracker>(true);
            if (allTrackers != null)
            {
                foreach (ItemTracker tracker in allTrackers)
                {
                    if (tracker == null) continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(tracker);
                    if (pv == null) continue;

                    try
                    {
                        object? hostTargetObj = ReflectionCache.ItemTracker_CurrentTargetField.GetValue(tracker);
                        if (hostTargetObj is Transform hostTargetTransform && hostTargetTransform != null)
                        {
                            PhotonView? targetPV = hostTargetTransform.GetComponentInParent<PhotonView>(); // Target might be nested
                            if (targetPV != null)
                            {
                                pv.RPC("SetTargetRPC", targetPlayer, targetPV.ViewID);
                                syncedTrackerTargets++;
                            }
                        }
                    }
                    catch (Exception ex) { Log.LogError($"[ItemSyncManager] Error reflecting/sending ItemTracker.currentTarget RPC for '{tracker.gameObject.name}': {ex}"); }
                }
            }
        }

        Log.LogInfo(
            $"[ItemSyncManager] Finished full interactive item state sync for {targetNickname}. Totals: " +
            $"TogglesState={syncedTogglesState}, TogglesDisabled={syncedTogglesDisabled}, Batteries={syncedBatteries}, Mines={syncedMines}, Melees={syncedMelees}, " +
            $"DronesActivated={syncedDronesActivated}, GrenadesActive={syncedGrenadesActive}, TrackerTargets={syncedTrackerTargets}, HealthPacksUsed={syncedHealthPacksUsed}"
        );
    }
    #endregion

    #region Valuable Object Synchronization
    /// <summary>
    /// Synchronizes the dollar values of all <see cref="ValuableObject"/> instances
    /// in the scene to a late-joining player.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncAllValuablesForPlayer(Player targetPlayer)
    {
        if (targetPlayer == null)
        {
            Log.LogError("[ItemSyncManager][ValuableSync] SyncAllValuablesForPlayer called with null targetPlayer. Aborting.");
            return;
        }
        string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";

        if (ReflectionCache.ValuableObject_DollarValueSetField == null)
        {
            Log.LogError("[ItemSyncManager][ValuableSync] Reflection field ValuableObject_DollarValueSetField is missing from ReflectionCache. Aborting.");
            return;
        }

        ValuableObject[] allValuables = Object.FindObjectsOfType<ValuableObject>(true);
        if (allValuables == null || allValuables.Length == 0)
        {
            Log.LogDebug($"[ItemSyncManager][ValuableSync] No valuable objects found in scene for {targetNickname}. Skipping.");
            return;
        }

        int syncedCount = 0;
        foreach (ValuableObject valuable in allValuables)
        {
            if (valuable == null) continue;
            PhotonView? pv = PhotonUtilities.GetPhotonView(valuable);
            if (pv == null) continue;

            try
            {
                bool isValueSet = ReflectionCache.ValuableObject_DollarValueSetField.GetValue(valuable) as bool? ?? false;
                if (isValueSet)
                {
                    pv.RPC("DollarValueSetRPC", targetPlayer, valuable.dollarValueCurrent);
                    syncedCount++;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[ItemSyncManager][ValuableSync] Error processing valuable '{valuable.name}' for {targetNickname}: {ex}");
            }
        }
        Log.LogInfo($"[ItemSyncManager][ValuableSync] Synced dollar values for {syncedCount}/{allValuables.Length} valuables for {targetNickname}.");
    }
    #endregion

    #region Shop Item Synchronization
    /// <summary>
    /// Synchronizes the prices of all shop items (identified by <see cref="ItemAttributes"/>)
    /// to a late-joining player.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncAllShopItemsForPlayer(Player targetPlayer)
    {
        if (targetPlayer == null)
        {
            Log.LogError("[ItemSyncManager][ShopSync] SyncAllShopItemsForPlayer called with null targetPlayer. Aborting.");
            return;
        }
        string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";

        if (ReflectionCache.ItemAttributes_ValueField == null || ReflectionCache.ItemAttributes_ShopItemField == null)
        {
            Log.LogError("[ItemSyncManager][ShopSync] Reflection fields for ItemAttributes (Value or ShopItem) are missing from ReflectionCache. Aborting.");
            return;
        }

        ItemAttributes[] allItems = Object.FindObjectsOfType<ItemAttributes>(true);
        if (allItems == null || allItems.Length == 0)
        {
            Log.LogDebug($"[ItemSyncManager][ShopSync] No ItemAttributes objects found in scene for {targetNickname}. Skipping.");
            return;
        }

        int syncedCount = 0;
        foreach (ItemAttributes itemAttr in allItems)
        {
            if (itemAttr == null) continue;

            try
            {
                bool isShopItem = ReflectionCache.ItemAttributes_ShopItemField.GetValue(itemAttr) as bool? ?? false;
                if (!isShopItem) continue;

                PhotonView? pv = PhotonUtilities.GetPhotonView(itemAttr);
                if (pv == null) continue;

                int hostValue = ReflectionCache.ItemAttributes_ValueField.GetValue(itemAttr) as int? ?? 0;
                if (hostValue <= 0) continue; // Don't sync items with no value or negative value

                pv.RPC("GetValueRPC", targetPlayer, hostValue);
                syncedCount++;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[ItemSyncManager][ShopSync] Error processing shop item '{itemAttr.name}' for {targetNickname}: {ex}");
            }
        }
        Log.LogInfo($"[ItemSyncManager][ShopSync] Synced values for {syncedCount} shop items for {targetNickname}.");
    }
    #endregion

    #region Extraction Point Item Resynchronization
    /// <summary>
    /// Coroutine to force late-joining clients to re-evaluate items contributing to the haul
    /// for a specific extraction point by sending targeted RPCs.
    /// This should be called AFTER the EP's state (Active, HaulGoal, etc.) has been synced.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player for whom to resync EP items.</param>
    /// <param name="epToSync">The specific <see cref="ExtractionPoint"/> whose item haul needs resynchronization.</param>
    internal static IEnumerator ResyncExtractionPointItems(Player targetPlayer, ExtractionPoint epToSync)
    {
        // --- Standard Initial Checks ---
        if (targetPlayer == null)
        {
            Log.LogError("[ItemSyncManager][EPItemResync] Coroutine started with null targetPlayer. Aborting.");
            yield break;
        }
        if (epToSync == null)
        {
            Log.LogError($"[ItemSyncManager][EPItemResync] Coroutine started with null epToSync for player {targetPlayer.NickName}. Aborting.");
            yield break;
        }
        if (!PhotonUtilities.IsRealMasterClient())
        {
            Log.LogDebug("[ItemSyncManager][EPItemResync] Not MasterClient. Aborting coroutine.");
            yield break;
        }
        if (CoroutineHelper.CoroutineRunner == null)
        {
            Log.LogError("[ItemSyncManager][EPItemResync] CoroutineHelper.CoroutineRunner is null. Aborting.");
            yield break;
        }

        int targetActorNr = targetPlayer.ActorNumber;
        string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetActorNr}";
        string epName = epToSync.name;

        // Ensure player is still in the room before starting
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetActorNr))
        {
            Log.LogWarning($"[ItemSyncManager][EPItemResync] Player {targetNickname} left room before resync could start for EP '{epName}'. Aborting.");
            yield break;
        }

        // Check with LateJoinManager if this task is still pending
        if (!LateJoinManager.IsLateJoinerPendingAsyncTask(targetActorNr, LateJoinManager.LateJoinTaskType.ExtractionPointItems))
        {
            Log.LogWarning($"[ItemSyncManager][EPItemResync] Player {targetNickname} is no longer pending EP item resync for '{epName}'. Aborting.");
            yield break;
        }

        Log.LogInfo($"[ItemSyncManager][EPItemResync] Starting item list sync for {targetNickname} in EP '{epName}'.");

        bool isShopScene = SemiFunc.RunIsShop();
        List<GameObject> itemsTheHostConsidersInEpToSync = new List<GameObject>();

        // --- Gather Items the Host Considers to be in epToSync ---
        if (isShopScene)
        {
            if (ShopManager.instance != null && ReflectionCache.ShopManager_ShoppingListField != null)
            {
                try
                {
                    // Assuming ShopManager.shoppingList directly reflects items in the shop EP
                    if (ReflectionCache.ShopManager_ShoppingListField.GetValue(ShopManager.instance) is List<ItemAttributes> shopList)
                    {
                        itemsTheHostConsidersInEpToSync.AddRange(
                            shopList.Where(itemAttr => itemAttr != null && itemAttr.gameObject != null)
                                    .Select(itemAttr => itemAttr.gameObject)
                        );
                    }
                }
                catch (System.Exception ex) { Log.LogError($"[ItemSyncManager][EPItemResync-Shop] Error reflecting ShopManager.shoppingList for {targetNickname}: {ex}"); }
            }
            else { Log.LogWarning("[ItemSyncManager][EPItemResync-Shop] ShopManager instance or shoppingList field reflection missing."); }
        }
        else // Level scene
        {
            if (RoundDirector.instance != null)
            {
                try
                {
                    ExtractionPoint? hostCurrentEP = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint;
                    if (epToSync == hostCurrentEP)
                    {
                        if (RoundDirector.instance.dollarHaulList != null)
                        {
                            itemsTheHostConsidersInEpToSync.AddRange(RoundDirector.instance.dollarHaulList.Where(go => go != null));
                        }
                    }
                    else
                    {
                        Log.LogInfo($"[ItemSyncManager][EPItemResync-Level] epToSync ('{epName}') is not host's current active EP ('{hostCurrentEP?.name ?? "None"}'). Host considers 0 items for its active haul contribution.");
                    }
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[ItemSyncManager][EPItemResync-Level] Error reflecting RoundDirector.extractionPointCurrent for {targetNickname}: {ex}");
                }
            }
            else { Log.LogError("[ItemSyncManager][EPItemResync-Level] RoundDirector.instance is null. Cannot determine items."); yield break; }
        }

        Log.LogInfo($"[ItemSyncManager][EPItemResync] Host considers {itemsTheHostConsidersInEpToSync.Count} items in EP '{epName}' for {targetNickname}.");

        // Double-check player presence before sending RPCs
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetActorNr))
        {
            Log.LogWarning($"[ItemSyncManager][EPItemResync] Player {targetNickname} left room before RPCs could be sent for EP '{epName}'. Aborting.");
            yield break;
        }

        // --- Send RPCs to Sync Item Lists ---
        if (isShopScene)
        {
            int totalShopCostOnHost = 0;
            foreach (GameObject itemGO in itemsTheHostConsidersInEpToSync)
            {
                if (itemGO == null) continue;
                ItemAttributes? itemAttr = itemGO.GetComponent<ItemAttributes>();
                if (itemAttr != null)
                {
                    try
                    {
                        // Use reflection for ItemAttributes.value
                        int? itemValue = ReflectionCache.ItemAttributes_ValueField.GetValue(itemAttr) as int?;
                        if (itemValue.HasValue)
                        {
                            totalShopCostOnHost += itemValue.Value;
                        }
                        else
                        {
                            Log.LogWarning($"[ItemSyncManager][EPItemResync-Shop] Failed to get value for item '{itemGO.name}' via reflection.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Log.LogError($"[ItemSyncManager][EPItemResync-Shop] Error reflecting ItemAttributes.value for item '{itemGO.name}': {ex}");
                    }
                }
            }

            Log.LogInfo($"[ItemSyncManager][EPItemResync-Shop] Sending 'UpdateShoppingCostRPC' with total value {totalShopCostOnHost} to {targetNickname} for EP '{epName}'.");

            // Get PunManager's PhotonView using your utilities or reflection if necessary
            PhotonView? punManagerPv = PhotonUtilities.GetPhotonView(PunManager.instance); // Assuming PunManager.instance is a MonoBehaviour
            if (punManagerPv != null)
            {
                punManagerPv.RPC("UpdateShoppingCostRPC", targetPlayer, totalShopCostOnHost);
            }
            else { Log.LogError("[ItemSyncManager][EPItemResync-Shop] PunManager's PhotonView is null (or PunManager instance is null). Cannot send UpdateShoppingCostRPC."); }
        }
        else // Level scene
        {
            List<PhotonView> allValuablePVsInScene = new List<PhotonView>();
            ValuableObject[] allValuables = Object.FindObjectsOfType<ValuableObject>(true);
            foreach (ValuableObject vo in allValuables)
            {
                if (vo == null || vo.gameObject == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(vo);
                if (pv != null)
                {
                    allValuablePVsInScene.Add(pv);
                }
            }

            if (allValuablePVsInScene.Count > 0)
            {
                Log.LogInfo($"[ItemSyncManager][EPItemResync-Level] Sending global 'RemoveFromDollarHaulListRPC' for {allValuablePVsInScene.Count} potential items to {targetNickname} to clear client's list.");
                foreach (PhotonView pv in allValuablePVsInScene)
                {
                    // Check if PV is still valid before RPC (e.g., object not destroyed)
                    if (pv != null && pv.gameObject != null)
                    {
                        pv.RPC("RemoveFromDollarHaulListRPC", targetPlayer);
                    }
                }
            }
            else
            {
                Log.LogInfo($"[ItemSyncManager][EPItemResync-Level] No valuable items found in scene to send global Remove RPCs for.");
            }

            // Wait for a moment to allow the client to process these removal RPCs.
            // This should cause their HaulUI to update.
            if (LevelEpRpcsDelaySeconds > 0)
            {
                yield return new WaitForSeconds(LevelEpRpcsDelaySeconds);
            }
            else // If delay is 0, still yield a frame.
            {
                yield return null;
            }


            // Check player presence again after the delay
            if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetActorNr))
            {
                Log.LogWarning($"[ItemSyncManager][EPItemResync] Player {targetNickname} left room during RPC delay for EP '{epName}'. Aborting Add RPCs.");
                yield break;
            }

            // Step 2: Add back only the items that the host currently has in epToSync.
            if (itemsTheHostConsidersInEpToSync.Count > 0)
            {
                Log.LogInfo($"[ItemSyncManager][EPItemResync-Level] Sending 'AddToDollarHaulListRPC' for {itemsTheHostConsidersInEpToSync.Count} items currently in EP '{epName}' on host to {targetNickname}.");
                foreach (GameObject itemGO in itemsTheHostConsidersInEpToSync)
                {
                    if (itemGO == null) continue;
                    PhotonView pv = itemGO.GetPhotonView(); // Assumes ValuableObject's GameObject has the PhotonView
                    if (pv != null)
                    {
                        if (pv != null && pv.gameObject != null) // Check PV validity
                        {
                            pv.RPC("AddToDollarHaulListRPC", targetPlayer);
                        }
                    }
                    else { Log.LogWarning($"[ItemSyncManager][EPItemResync-Level] Item '{itemGO.name}' in host's EP list for '{epName}' lacks a PhotonView. Cannot send Add RPC."); }
                }
            }
            else
            {
                Log.LogInfo($"[ItemSyncManager][EPItemResync-Level] Host has 0 items in EP '{epName}' to add back to {targetNickname}. Client's list should now be clear.");
            }
        }

        // Final check and report completion
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetActorNr))
        {
            Log.LogWarning($"[ItemSyncManager][EPItemResync] Player {targetNickname} left room before resync completion for EP '{epName}' could be reported.");
            // LateJoinManager might still need to be notified, possibly with a failure/abort status
            yield break;
        }

        Log.LogInfo($"[ItemSyncManager][EPItemResync] Finished item list sync sequence for {targetNickname} in EP '{epName}'. Reporting completion.");
        LateJoinManager.ReportLateJoinAsyncTaskCompleted(targetActorNr, LateJoinManager.LateJoinTaskType.ExtractionPointItems);
    }
    #endregion
}