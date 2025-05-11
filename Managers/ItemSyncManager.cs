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
    private static readonly float ItemResyncDelaySeconds = 0.2f;

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
    /// Coroutine to force late-joining clients to re-evaluate items in the
    /// active extraction point or the shop EP by teleporting items out and then back.
    /// This helps clients correctly register items that were already in the EP when they joined.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player for whom to resync EP items.</param>
    /// <param name="epToSync">The specific <see cref="ExtractionPoint"/> whose items need resynchronization.</param>
    internal static IEnumerator ResyncExtractionPointItems(Player targetPlayer, ExtractionPoint epToSync)
    {
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

        string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        string epName = epToSync.name;
        Log.LogInfo($"[ItemSyncManager][EPItemResync] Starting for {targetNickname} in EP '{epName}'.");

        // Ensure player is still in the room
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
        {
            Log.LogWarning($"[ItemSyncManager][EPItemResync] Player {targetNickname} left room before resync could start. Aborting.");
            yield break;
        }

        bool isShopScene = SemiFunc.RunIsShop();
        Vector3 farAwayPosition = epToSync.transform.position + (Vector3.up * 500f); // Move items far up
        List<GameObject> itemsToResync = new List<GameObject>();

        // Gather items based on scene type
        if (isShopScene)
        {
            if (ShopManager.instance != null && ReflectionCache.ShopManager_ShoppingListField != null)
            {
                try
                {
                    if (ReflectionCache.ShopManager_ShoppingListField.GetValue(ShopManager.instance) is List<ItemAttributes> shopList)
                    {
                        itemsToResync.AddRange(shopList.Where(itemAttr => itemAttr != null && itemAttr.gameObject != null)
                                                      .Select(itemAttr => itemAttr.gameObject));
                    }
                }
                catch (Exception ex) { Log.LogError($"[ItemSyncManager][EPItemResync] Error reflecting ShopManager.shoppingList: {ex}"); }
            }
        }
        else // Level scene
        {
            if (RoundDirector.instance?.dollarHaulList != null)
            {
                itemsToResync.AddRange(RoundDirector.instance.dollarHaulList.Where(go => go != null));
            }
        }

        if (itemsToResync.Count == 0)
        {
            Log.LogDebug($"[ItemSyncManager][EPItemResync] No items identified in EP '{epName}' for {targetNickname}. Nothing to resync.");
            yield break;
        }

        Log.LogInfo($"[ItemSyncManager][EPItemResync] Found {itemsToResync.Count} items in '{epName}' for {targetNickname}. Preparing teleport sequence.");

        Dictionary<int, (Vector3 position, Quaternion rotation)> originalTransforms = new Dictionary<int, (Vector3, Quaternion)>();
        List<PhysGrabObject> validPhysObjectsToTeleport = new List<PhysGrabObject>();

        // Teleport items away
        foreach (GameObject itemGO in itemsToResync)
        {
            if (itemGO == null) continue;

            PhysGrabObject? pgo = itemGO.GetComponent<PhysGrabObject>();
            PhotonView? pv = PhotonUtilities.GetPhotonViewFromPGO(pgo); // Handles PGO being null

            if (pgo == null || pv == null)
            {
                Log.LogDebug($"[ItemSyncManager][EPItemResync] Skipping item '{itemGO.name}' (no PGO or PV).");
                continue;
            }

            if (!pv.IsMine) // Request ownership if not already owner
            {
                pv.RequestOwnership();
                // Note: Ownership request is asynchronous. A small delay might be needed here
                // if immediate teleportation without ownership causes issues.
                // For simplicity, we proceed, but this is a potential refinement point.
            }

            originalTransforms[pv.ViewID] = (itemGO.transform.position, itemGO.transform.rotation);
            validPhysObjectsToTeleport.Add(pgo);

            try
            {
                pgo.Teleport(farAwayPosition, itemGO.transform.rotation);
            }
            catch (Exception ex)
            {
                Log.LogError($"[ItemSyncManager][EPItemResync] Error teleporting '{itemGO.name}' (ViewID: {pv.ViewID}) AWAY: {ex}");
            }
        }

        yield return new WaitForSeconds(ItemResyncDelaySeconds);

        // Ensure player is still in room before teleporting back
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
        {
            Log.LogWarning($"[ItemSyncManager][EPItemResync] Player {targetNickname} left room during resync delay. Aborting teleport BACK.");
            yield break;
        }

        Log.LogInfo($"[ItemSyncManager][EPItemResync] Teleporting {validPhysObjectsToTeleport.Count} items back for {targetNickname}.");

        // Teleport items back
        foreach (PhysGrabObject pgo in validPhysObjectsToTeleport)
        {
            if (pgo == null || pgo.gameObject == null) continue; // Should not happen if added correctly

            PhotonView? pv = PhotonUtilities.GetPhotonViewFromPGO(pgo);
            if (pv == null) continue; // Should not happen

            int viewID = pv.ViewID;
            if (originalTransforms.TryGetValue(viewID, out var originalTransform))
            {
                if (!pv.IsMine)
                {
                    Log.LogWarning($"[ItemSyncManager][EPItemResync] Lost ownership of '{pgo.gameObject.name}' (ViewID: {viewID}) before teleporting back.");
                    // Optionally, re-request ownership or skip teleport back for this item.
                }
                try
                {
                    pgo.Teleport(originalTransform.position, originalTransform.rotation);
                }
                catch (Exception ex)
                {
                    Log.LogError($"[ItemSyncManager][EPItemResync] Error teleporting '{pgo.gameObject.name}' (ViewID: {viewID}) BACK: {ex}");
                }
            }
            else
            {
                Log.LogWarning($"[ItemSyncManager][EPItemResync] Could not find original transform for item with ViewID {viewID} ('{pgo.gameObject.name}'). Cannot teleport back.");
            }
        }
        Log.LogInfo($"[ItemSyncManager][EPItemResync] Finished item resync sequence for {targetNickname} in EP '{epName}'.");
    }
    #endregion
}