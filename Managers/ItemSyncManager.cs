// File: L.A.T.E/Managers/ItemSyncManager.cs
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using LATE.Core;       // For LatePlugin.Log, CoroutineHelper
using LATE.Utilities;  // For ReflectionCache, PhotonUtilities, GameUtilities

namespace LATE.Managers; // File-scoped namespace

/// <summary>
/// Responsible for synchronizing the state of various items and valuables
/// for late-joining players. This includes item toggles, batteries, mines,
/// shop items, and items in extraction points.
/// </summary>
internal static class ItemSyncManager
{
    private static readonly float itemResyncDelay = 0.2f; // Moved from LateJoinManager

    /// <summary>
    /// Synchronizes the state of various interactive items (Toggle, Battery, Mine state, etc.)
    /// for a late-joining player.
    /// </summary>
    internal static void SyncAllItemStatesForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogInfo($"[ItemSyncManager] Starting FULL item state sync for {nick}.");

        int syncedTogglesState = 0, syncedTogglesDisabled = 0, syncedBatteries = 0, syncedMines = 0;
        int syncedMelees = 0, syncedDronesActivated = 0, syncedGrenadesActive = 0, syncedTrackerTargets = 0, syncedHealthPacksUsed = 0;

        // ItemToggle States (ON/OFF)
        ItemToggle[] allToggles = Object.FindObjectsOfType<ItemToggle>(true);
        if (allToggles != null)
        {
            foreach (ItemToggle itemToggle in allToggles)
            {
                if (itemToggle == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemToggle);
                if (pv == null) continue;
                bool hostToggleState = itemToggle.toggleState;
                pv.RPC("ToggleItemRPC", targetPlayer, hostToggleState, -1);
                syncedTogglesState++;
            }
        }

        // ItemToggle Disabled State
        if (ReflectionCache.ItemToggle_DisabledField != null && allToggles != null)
        {
            foreach (ItemToggle itemToggle in allToggles)
            {
                if (itemToggle == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemToggle);
                if (pv == null) continue;
                try
                {
                    bool hostIsDisabled = (bool)(ReflectionCache.ItemToggle_DisabledField.GetValue(itemToggle) ?? false);
                    if (hostIsDisabled) { pv.RPC("ToggleDisableRPC", targetPlayer, true); syncedTogglesDisabled++; }
                }
                catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSyncManager] Error reflecting ItemToggle.disabled for '{itemToggle.gameObject.name}': {refEx}"); }
            }
        }

        // ItemBattery Levels
        ItemBattery[] allBatteries = Object.FindObjectsOfType<ItemBattery>(true);
        if (allBatteries != null)
        {
            foreach (ItemBattery itemBattery in allBatteries)
            {
                if (itemBattery == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(itemBattery);
                if (pv == null) continue;
                float hostBatteryLife = itemBattery.batteryLife;
                int hostBatteryLifeInt = (hostBatteryLife > 0f) ? (int)Mathf.Round(hostBatteryLife / 16.6f) : 0;
                pv.RPC("BatteryFullPercentChangeRPC", targetPlayer, hostBatteryLifeInt, false);
                syncedBatteries++;
            }
        }

        // ItemMine States
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
                        int hostMineStateInt = (int)(ReflectionCache.ItemMine_StateField.GetValue(itemMine) ?? 0);
                        pv.RPC("StateSetRPC", targetPlayer, hostMineStateInt);
                        syncedMines++;
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSyncManager] Error reflecting ItemMine.state for '{itemMine.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemMelee Broken State
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
                        bool hostPGOIsMelee = (bool)(ReflectionCache.PhysGrabObject_IsMeleeField.GetValue(meleePGO) ?? false);
                        if (hostIsBroken && hostPGOIsMelee) { pv.RPC("MeleeBreakRPC", targetPlayer); syncedMelees++; }
                        else if (!hostIsBroken && !hostPGOIsMelee) { pv.RPC("MeleeFixRPC", targetPlayer); syncedMelees++; }
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSyncManager] Error reflecting PhysGrabObject.isMelee for '{meleePGO.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemDrone Activated State
        ItemDrone[] allDrones = Object.FindObjectsOfType<ItemDrone>(true);
        if (allDrones != null)
        {
            foreach (ItemDrone drone in allDrones)
            {
                if (drone == null) continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(drone);
                if (pv == null) continue;
                ItemToggle? droneToggle = drone.GetComponent<ItemToggle>();
                if (droneToggle != null && droneToggle.toggleState) { pv.RPC("ButtonToggleRPC", targetPlayer, true); syncedDronesActivated++; }
            }
        }

        // ItemHealthPack Used State
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
                        bool hostIsUsed = (bool)(ReflectionCache.ItemHealthPack_UsedField.GetValue(healthPack) ?? false);
                        if (hostIsUsed) { pv.RPC("UsedRPC", targetPlayer); syncedHealthPacksUsed++; }
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSyncManager] Error reflecting ItemHealthPack.used for '{healthPack.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemGrenade Active State
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
                        bool hostIsActive = (bool)(ReflectionCache.ItemGrenade_IsActiveField.GetValue(grenade) ?? false);
                        if (hostIsActive) { pv.RPC("TickStartRPC", targetPlayer); syncedGrenadesActive++; }
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSyncManager] Error reflecting ItemGrenade.isActive for '{grenade.gameObject.name}': {refEx}"); }
                }
            }
        }

        // ItemTracker Target State
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
                            PhotonView? targetPV = hostTargetTransform.GetComponentInParent<PhotonView>();
                            if (targetPV != null) { pv.RPC("SetTargetRPC", targetPlayer, targetPV.ViewID); syncedTrackerTargets++; }
                        }
                    }
                    catch (Exception refEx) { LatePlugin.Log.LogError($"[ItemSyncManager] Error reflecting ItemTracker.currentTarget for '{tracker.gameObject.name}': {refEx}"); }
                }
            }
        }

        LatePlugin.Log.LogInfo(
            $"[ItemSyncManager] Finished FULL item state sync for {nick}. Totals: " +
            $"TogglesState={syncedTogglesState}, TogglesDisabled={syncedTogglesDisabled}, Batteries={syncedBatteries}, Mines={syncedMines}, Melees={syncedMelees}, " +
            $"DronesActivated={syncedDronesActivated}, GrenadesActive={syncedGrenadesActive}, TrackerTargets={syncedTrackerTargets}, HealthPacksUsed={syncedHealthPacksUsed}"
        );
    }

    /// <summary>
    /// Synchronizes the dollar values of all ValuableObjects in the scene to a late-joining player.
    /// </summary>
    internal static void SyncAllValuablesForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        if (ReflectionCache.ValuableObject_DollarValueSetField == null)
        {
            LatePlugin.Log.LogError("[ItemSyncManager][Valuable Sync] Reflection field ValuableObject_DollarValueSetField is null from ReflectionCache.");
            return;
        }

        ValuableObject[]? allValuables = Object.FindObjectsOfType<ValuableObject>(true);
        if (allValuables == null || allValuables.Length == 0)
        {
            LatePlugin.Log.LogWarning($"[ItemSyncManager][Valuable Sync] Found 0 valuable objects in scene for {nick}.");
            return;
        }

        int syncedCount = 0;
        foreach (ValuableObject valuable in allValuables)
        {
            if (valuable == null) continue;
            PhotonView? pv = PhotonUtilities.GetPhotonView(valuable);
            if (pv == null) continue;

            bool isValueSet = false;
            try
            {
                isValueSet = (bool)(ReflectionCache.ValuableObject_DollarValueSetField.GetValue(valuable) ?? false);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[ItemSyncManager][Valuable Sync] Error reflecting dollarValueSet for {valuable.name}: {ex.Message}. Skipping item.");
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
                    LatePlugin.Log.LogWarning($"[ItemSyncManager][Valuable Sync] DollarValueSetRPC failed for {valuable.name}: {ex.Message}");
                }
            }
        }
        LatePlugin.Log.LogInfo($"[ItemSyncManager][Valuable Sync] Synced dollar values for {syncedCount}/{allValuables.Length} valuables for {nick}.");
    }

    /// <summary>
    /// Synchronizes the prices of all shop items to a late-joining player.
    /// </summary>
    internal static void SyncAllShopItemsForPlayer(Player targetPlayer)
    {
        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        if (ReflectionCache.ItemAttributes_ValueField == null || ReflectionCache.ItemAttributes_ShopItemField == null)
        {
            LatePlugin.Log.LogError("[ItemSyncManager][Shop Sync] Reflection fields for ItemAttributes (Value or ShopItem) are null from ReflectionCache.");
            return;
        }

        ItemAttributes[]? allItems = Object.FindObjectsOfType<ItemAttributes>(true);
        if (allItems == null || allItems.Length == 0)
        {
            LatePlugin.Log.LogWarning($"[ItemSyncManager][Shop Sync] Found 0 ItemAttributes objects in scene for {nick}.");
            return;
        }

        int syncedCount = 0;
        foreach (ItemAttributes itemAttr in allItems)
        {
            if (itemAttr == null) continue;

            bool isShopItem = false;
            try
            {
                isShopItem = (bool)(ReflectionCache.ItemAttributes_ShopItemField.GetValue(itemAttr) ?? false);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[ItemSyncManager][Shop Sync] Error reflecting shopItem for {itemAttr.name}: {ex.Message}. Skipping item.");
                continue;
            }

            if (!isShopItem) continue;

            PhotonView? pv = PhotonUtilities.GetPhotonView(itemAttr);
            if (pv == null) continue;

            int hostValue = 0;
            try
            {
                hostValue = (int)(ReflectionCache.ItemAttributes_ValueField.GetValue(itemAttr) ?? 0);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[ItemSyncManager][Shop Sync] Error reflecting value for {itemAttr.name}: {ex.Message}. Skipping item.");
                continue;
            }

            if (hostValue <= 0) continue;

            try
            {
                pv.RPC("GetValueRPC", targetPlayer, hostValue);
                syncedCount++;
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[ItemSyncManager][Shop Sync] GetValueRPC failed for {itemAttr.name}: {ex.Message}");
            }
        }
        LatePlugin.Log.LogInfo($"[ItemSyncManager][Shop Sync] Synced values for {syncedCount} shop items for {nick}.");
    }

    /// <summary>
    /// Coroutine to force late-joining clients to re-evaluate items in the
    /// active extraction point or the shop EP by teleporting items out and back.
    /// </summary>
    internal static IEnumerator ResyncExtractionPointItems(Player targetPlayer, ExtractionPoint epToSync)
    {
        string targetNickname = targetPlayer?.NickName ?? "<UnknownPlayer>";
        string epName = epToSync?.name ?? "<UnknownEP>";
        LatePlugin.Log.LogInfo($"[ItemSyncManager][EP Item Resync] Starting for {targetNickname} in EP '{epName}'");

        if (targetPlayer == null || epToSync == null || !PhotonUtilities.IsRealMasterClient() || CoroutineHelper.CoroutineRunner == null)
        {
            LatePlugin.Log.LogWarning("[ItemSyncManager][EP Item Resync] Aborting: Invalid state (null player/ep, not master, or null runner).");
            yield break;
        }
        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
        {
            LatePlugin.Log.LogWarning($"[ItemSyncManager][EP Item Resync] Aborting: Player {targetNickname} left room.");
            yield break;
        }

        bool isShop = SemiFunc.RunIsShop();
        Vector3 farAwayPosition = new Vector3(epToSync.transform.position.x, epToSync.transform.position.y + 500f, epToSync.transform.position.z);
        List<GameObject> itemsToResync = new List<GameObject>();

        if (isShop)
        {
            if (ShopManager.instance != null && ReflectionCache.ShopManager_ShoppingListField != null)
            {
                try
                {
                    object? listObject = ReflectionCache.ShopManager_ShoppingListField.GetValue(ShopManager.instance);
                    if (listObject is List<ItemAttributes> shopList)
                    {
                        itemsToResync.AddRange(shopList.Where(itemAttr => itemAttr != null && itemAttr.gameObject != null).Select(itemAttr => itemAttr.gameObject));
                    }
                }
                catch (Exception ex) { LatePlugin.Log.LogError($"[ItemSyncManager][EP Item Resync] Error reflecting ShopManager.shoppingList: {ex}"); }
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
            LatePlugin.Log.LogInfo($"[ItemSyncManager][EP Item Resync] No items identified in EP '{epName}' for {targetNickname}.");
            yield break;
        }

        LatePlugin.Log.LogInfo($"[ItemSyncManager][EP Item Resync] Found {itemsToResync.Count} items in '{epName}' for {targetNickname}.");

        Dictionary<int, (Vector3 pos, Quaternion rot)> originalTransforms = new Dictionary<int, (Vector3, Quaternion)>();
        List<PhysGrabObject> validPhysObjects = new List<PhysGrabObject>();

        foreach (GameObject itemGO in itemsToResync)
        {
            if (itemGO == null) continue;
            PhysGrabObject? pgo = itemGO.GetComponent<PhysGrabObject>();
            PhotonView? pv = PhotonUtilities.GetPhotonViewFromPGO(pgo);
            if (pgo == null || pv == null) continue;

            if (!pv.IsMine) pv.RequestOwnership();

            originalTransforms[pv.ViewID] = (itemGO.transform.position, itemGO.transform.rotation);
            validPhysObjects.Add(pgo);
            try { pgo.Teleport(farAwayPosition, itemGO.transform.rotation); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[ItemSyncManager][EP Item Resync] Error teleporting {itemGO.name} AWAY: {ex}"); }
        }

        yield return new WaitForSeconds(itemResyncDelay);

        if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
        {
            LatePlugin.Log.LogWarning($"[ItemSyncManager][EP Item Resync] Aborting teleport BACK: Player {targetNickname} left.");
            yield break;
        }

        LatePlugin.Log.LogInfo($"[ItemSyncManager][EP Item Resync] Teleporting {validPhysObjects.Count} items back for {targetNickname}.");
        foreach (PhysGrabObject pgo in validPhysObjects)
        {
            if (pgo == null || pgo.gameObject == null) continue;
            PhotonView? pv = PhotonUtilities.GetPhotonViewFromPGO(pgo);
            if (pv == null) continue;

            int viewID = pv.ViewID;
            if (originalTransforms.TryGetValue(viewID, out var originalTransform))
            {
                if (!pv.IsMine) LatePlugin.Log.LogWarning($"[ItemSyncManager][EP Item Resync] Lost ownership of {pgo.gameObject.name} before teleport back?");
                try { pgo.Teleport(originalTransform.pos, originalTransform.rot); }
                catch (Exception ex) { LatePlugin.Log.LogError($"[ItemSyncManager][EP Item Resync] Error teleporting {pgo.gameObject.name} BACK: {ex}"); }
            }
            else { LatePlugin.Log.LogWarning($"[ItemSyncManager][EP Item Resync] No original transform for ViewID {viewID}."); }
        }
        LatePlugin.Log.LogInfo($"[ItemSyncManager][EP Item Resync] Finished item resync sequence for {targetNickname} in EP '{epName}'.");
    }
}