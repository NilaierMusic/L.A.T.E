// File: L.A.T.E/Utilities/GameUtilities.cs
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic; // For IList for Shuffle
using System.Reflection;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Object = UnityEngine.Object;
using LATE.Core; // For LatePlugin.Log and CoroutineHelper

namespace LATE.Utilities; // File-scoped namespace

/// <summary>
/// Provides general game-related utility functions and caches for reflected members.
/// This class will be split further, with reflection caches moving to ReflectionCache.cs
/// and Photon-specific utilities to PhotonUtilities.cs.
/// </summary>
internal static class GameUtilities
{
    // Reflection fields will be moved to ReflectionCache.cs.
    // For now, they remain here but are intended to be moved.
    #region ─── Reflection Fields (To be moved to ReflectionCache.cs) ──────────────────
    public static readonly FieldInfo? removeFilterFieldInfo = AccessTools.Field(typeof(PhotonNetwork), "removeFilter");
    public static readonly FieldInfo? keyByteSevenFieldInfo = AccessTools.Field(typeof(PhotonNetwork), "keyByteSeven");
    public static readonly FieldInfo? serverCleanOptionsFieldInfo = AccessTools.Field(typeof(PhotonNetwork), "ServerCleanOptions");
    public static readonly MethodInfo? raiseEventInternalMethodInfo = AccessTools.Method(typeof(PhotonNetwork), "RaiseEventInternal");
    public static readonly FieldInfo? rmRunManagerPUNField = AccessTools.Field(typeof(RunManager), "runManagerPUN");
    public static readonly FieldInfo? rmRunLivesField = AccessTools.Field(typeof(RunManager), "runLives");
    public static readonly FieldInfo? rmRestartingField = AccessTools.Field(typeof(RunManager), "restarting");
    public static readonly FieldInfo? rmRestartingDoneField = AccessTools.Field(typeof(RunManager), "restartingDone");
    public static readonly FieldInfo? rmLobbyJoinField = AccessTools.Field(typeof(RunManager), "lobbyJoin");
    public static readonly FieldInfo? rmWaitToChangeSceneField = AccessTools.Field(typeof(RunManager), "waitToChangeScene");
    public static readonly FieldInfo? rmGameOverField = AccessTools.Field(typeof(RunManager), "gameOver");
    public static readonly FieldInfo? rmpPhotonViewField = AccessTools.Field(typeof(RunManagerPUN), "photonView");
    public static readonly FieldInfo? paSpawnedField = AccessTools.Field(typeof(PlayerAvatar), "spawned");
    public static readonly FieldInfo? paPhotonViewField = AccessTools.Field(typeof(PlayerAvatar), "photonView");
    public static readonly FieldInfo? paPlayerNameField = AccessTools.Field(typeof(PlayerAvatar), "playerName");
    public static readonly FieldInfo? paOutroDoneField = AccessTools.Field(typeof(PlayerAvatar), "outroDone");
    public static readonly FieldInfo? paVoiceChatFetchedField = AccessTools.Field(typeof(PlayerAvatar), "voiceChatFetched");
    public static readonly FieldInfo? paVoiceChatField = AccessTools.Field(typeof(PlayerAvatar), "voiceChat");
    public static readonly FieldInfo? paIsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
    public static readonly FieldInfo? paDeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
    public static readonly FieldInfo? paPlayerDeathHeadField = AccessTools.Field(typeof(PlayerAvatar), "playerDeathHead");
    public static readonly FieldInfo? pdhPhysGrabObjectField = AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");
    public static readonly FieldInfo? iaInstanceNameField = AccessTools.Field(typeof(ItemAttributes), "instanceName");
    public static readonly FieldInfo? iaValueField = AccessTools.Field(typeof(ItemAttributes), "value");
    public static readonly FieldInfo? iaShopItemField = AccessTools.Field(typeof(ItemAttributes), "shopItem");
    public static readonly FieldInfo? iaDisableUIField = AccessTools.Field(typeof(ItemAttributes), "disableUI");
    public static readonly FieldInfo? ieCurrentStateField = AccessTools.Field(typeof(ItemEquippable), "currentState");
    public static readonly FieldInfo? ieOwnerPlayerIdField = AccessTools.Field(typeof(ItemEquippable), "ownerPlayerId");
    public static readonly FieldInfo? ieSpotIndexField = AccessTools.Field(typeof(ItemEquippable), "inventorySpotIndex");
    public static readonly FieldInfo? igIsActiveField = AccessTools.Field(typeof(ItemGrenade), "isActive");
    public static readonly FieldInfo? itCurrentTargetField = AccessTools.Field(typeof(ItemTracker), "currentTarget");
    public static readonly FieldInfo? imStateField = AccessTools.Field(typeof(ItemMine), "state");
    public static readonly FieldInfo? itCurrentTargetPGOField = AccessTools.Field(typeof(ItemTracker), "currentTargetPhysGrabObject");
    public static readonly FieldInfo? itDisabledField = AccessTools.Field(typeof(ItemToggle), "disabled");
    public static readonly FieldInfo? ihpUsedField = AccessTools.Field(typeof(ItemHealthPack), "used");
    public static readonly FieldInfo? pgoIsMeleeField = AccessTools.Field(typeof(PhysGrabObject), "isMelee");
    public static readonly FieldInfo? paPhysGrabberField = AccessTools.Field(typeof(PlayerAvatar), "physGrabber");
    public static readonly FieldInfo? pgPhotonViewField = AccessTools.Field(typeof(PhysGrabber), "photonView");
    public static readonly FieldInfo? pgoPhotonViewField = AccessTools.Field(typeof(PhysGrabObject), "photonView");
    public static readonly FieldInfo? enemyVisionField = AccessTools.Field(typeof(Enemy), "Vision");
    public static readonly FieldInfo? enemyPhotonViewField = AccessTools.Field(typeof(Enemy), "PhotonView");
    public static readonly FieldInfo? enemyTargetPlayerAvatarField = AccessTools.Field(typeof(Enemy), "TargetPlayerAvatar");
    public static readonly FieldInfo? enemyTargetPlayerViewIDField = AccessTools.Field(typeof(Enemy), "TargetPlayerViewID");
    public static readonly FieldInfo? eosOnScreenPlayerField = AccessTools.Field(typeof(EnemyOnScreen), "OnScreenPlayer");
    public static readonly FieldInfo? eosCulledPlayerField = AccessTools.Field(typeof(EnemyOnScreen), "CulledPlayer");
    public static readonly FieldInfo? enaAgentField = AccessTools.Field(typeof(EnemyNavMeshAgent), "Agent");
    public static readonly FieldInfo? esiInvestigatePositionField = AccessTools.Field(typeof(EnemyStateInvestigate), "onInvestigateTriggeredPosition");
    public static readonly FieldInfo? enemyBeamer_playerTargetField = AccessTools.Field(typeof(EnemyBeamer), "playerTarget");
    public static readonly FieldInfo? enemyCeilingEye_targetPlayerField = AccessTools.Field(typeof(EnemyCeilingEye), "targetPlayer");
    public static readonly FieldInfo? enemyFloater_targetPlayerField = AccessTools.Field(typeof(EnemyFloater), "targetPlayer");
    public static readonly FieldInfo? enemyRobe_targetPlayerField = AccessTools.Field(typeof(EnemyRobe), "targetPlayer");
    public static readonly FieldInfo? enemyRunner_targetPlayerField = AccessTools.Field(typeof(EnemyRunner), "targetPlayer");
    public static readonly FieldInfo? enemySlowWalker_targetPlayerField = AccessTools.Field(typeof(EnemySlowWalker), "targetPlayer");
    public static readonly FieldInfo? enemyThinMan_playerTargetField = AccessTools.Field(typeof(EnemyThinMan), "playerTarget");
    public static readonly FieldInfo? enemyTumbler_targetPlayerField = AccessTools.Field(typeof(EnemyTumbler), "targetPlayer");
    public static readonly FieldInfo? enemyUpscream_targetPlayerField = AccessTools.Field(typeof(EnemyUpscream), "targetPlayer");
    public static readonly FieldInfo? enemy_EnemyParentField = AccessTools.Field(typeof(Enemy), "EnemyParent");
    public static readonly FieldInfo? ep_SpawnedField = AccessTools.Field(typeof(EnemyParent), "Spawned");
    public static readonly FieldInfo? rdExtractionPointActiveField = AccessTools.Field(typeof(RoundDirector), "extractionPointActive");
    public static readonly FieldInfo? rdExtractionPointCurrentField = AccessTools.Field(typeof(RoundDirector), "extractionPointCurrent");
    public static readonly FieldInfo? rdExtractionPointSurplusField = AccessTools.Field(typeof(RoundDirector), "extractionPointSurplus");
    public static readonly FieldInfo? vdValuableTargetAmountField = AccessTools.Field(typeof(ValuableDirector), "valuableTargetAmount");
    public static readonly FieldInfo? voDollarValueSetField = AccessTools.Field(typeof(ValuableObject), "dollarValueSet");
    public static readonly FieldInfo? epCurrentStateField = AccessTools.Field(typeof(ExtractionPoint), "currentState");
    public static readonly FieldInfo? epHaulGoalFetchedField = AccessTools.Field(typeof(ExtractionPoint), "haulGoalFetched");
    public static readonly FieldInfo? epIsShopField = AccessTools.Field(typeof(ExtractionPoint), "isShop");
    public static readonly FieldInfo? smShoppingListField = AccessTools.Field(typeof(ShopManager), "shoppingList");
    public static readonly FieldInfo? modSetupDoneField = AccessTools.Field(typeof(Module), "SetupDone");
    public static readonly FieldInfo? modConnectingTopField = AccessTools.Field(typeof(Module), "ConnectingTop");
    public static readonly FieldInfo? modConnectingBottomField = AccessTools.Field(typeof(Module), "ConnectingBottom");
    public static readonly FieldInfo? modConnectingRightField = AccessTools.Field(typeof(Module), "ConnectingRight");
    public static readonly FieldInfo? modConnectingLeftField = AccessTools.Field(typeof(Module), "ConnectingLeft");
    public static readonly FieldInfo? modFirstField = AccessTools.Field(typeof(Module), "First");
    public static readonly FieldInfo? arenaWinnerPlayerField = AccessTools.Field(typeof(Arena), "winnerPlayer");
    public static readonly FieldInfo? arenaPhotonViewField = AccessTools.Field(typeof(Arena), "photonView");
    public static readonly FieldInfo? arenaCurrentStateField = AccessTools.Field(typeof(Arena), "currentState");
    public static readonly FieldInfo? arenaLevelField = AccessTools.Field(typeof(Arena), "level");
    public static readonly FieldInfo? arenaCrownCageDestroyedField = AccessTools.Field(typeof(Arena), "crownCageDestroyed");
    public static readonly FieldInfo? arenaPlayersAliveField = AccessTools.Field(typeof(Arena), "playersAlive");
    private static FieldInfo? _vpsSetupCompleteField;
    private static bool _vpsFieldChecked = false;
    public static readonly FieldInfo? pghClosedField = AccessTools.Field(typeof(PhysGrabHinge), "closed");
    public static readonly FieldInfo? pghBrokenField = AccessTools.Field(typeof(PhysGrabHinge), "broken");
    public static readonly FieldInfo? pghJointField = AccessTools.Field(typeof(PhysGrabHinge), "joint");
    public static readonly FieldInfo? tstCurrentPageIndexField = AccessTools.Field(typeof(TruckScreenText), "currentPageIndex");

    static GameUtilities() // Renamed from Utilities to GameUtilities
    {
        var mustExist = new (FieldInfo? Field, string PrettyName)[] // Nullable FieldInfo
        {
            (paPlayerNameField, "PlayerAvatar.playerName"),
            (voDollarValueSetField, "ValuableObject.dollarValueSet"),
            (enemyVisionField, "Enemy.Vision (internal)"),
            (enemyPhotonViewField, "Enemy.PhotonView"),
            (enemyTargetPlayerAvatarField, "Enemy.TargetPlayerAvatar"),
            (enemyTargetPlayerViewIDField, "Enemy.TargetPlayerViewID"),
            (enemy_EnemyParentField, "Enemy.EnemyParent (internal)"),
            (ep_SpawnedField, "EnemyParent.Spawned (internal)"),
            (rdExtractionPointActiveField, "RoundDirector.extractionPointActive"),
            (rdExtractionPointCurrentField, "RoundDirector.extractionPointCurrent"),
            (epCurrentStateField, "ExtractionPoint.currentState"),
            (epHaulGoalFetchedField, "ExtractionPoint.haulGoalFetched"),
            (epIsShopField, "ExtractionPoint.isShop"),
            (smShoppingListField, "ShopManager.shoppingList"),
            (rdExtractionPointSurplusField, "RoundDirector.extractionPointSurplus"),
            (pghClosedField, "PhysGrabHinge.closed"),
            (pghBrokenField, "PhysGrabHinge.broken"),
            (pghJointField, "PhysGrabHinge.joint"),
            (iaValueField, "ItemAttributes.value"),
            (iaShopItemField, "ItemAttributes.shopItem"),
            (ieCurrentStateField, "ItemEquippable.currentState"),
            (ieOwnerPlayerIdField, "ItemEquippable.ownerPlayerId"),
            (ieSpotIndexField, "ItemEquippable.inventorySpotIndex"),
            (paPhysGrabberField, "PlayerAvatar.physGrabber"),
            (imStateField, "ItemMine.state"),
            (igIsActiveField, "ItemGrenade.isActive"),
            (itCurrentTargetField, "ItemTracker.currentTarget"),
            (itCurrentTargetPGOField, "ItemTracker.currentTargetPhysGrabObject"),
            (itDisabledField, "ItemToggle.disabled"),
            (ihpUsedField, "ItemHealthPack.used"),
            (pgoIsMeleeField, "PhysGrabObject.isMelee"),
            (pgPhotonViewField, "PhysGrabber.photonView"),
            (pgoPhotonViewField, "PhysGrabObject.photonView"),
            (rmGameOverField, "RunManager.gameOver"),
            (rmpPhotonViewField, "RunManagerPUN.photonView"),
            (modSetupDoneField, "Module.SetupDone"),
            (modConnectingTopField, "Module.ConnectingTop"),
            (modConnectingBottomField, "Module.ConnectingBottom"),
            (modConnectingRightField, "Module.ConnectingRight"),
            (modConnectingLeftField, "Module.ConnectingLeft"),
            (modFirstField, "Module.First"),
            (tstCurrentPageIndexField, "TruckScreenText.currentPageIndex"),
            (paIsDisabledField, "PlayerAvatar.isDisabled"),
            (paDeadSetField, "PlayerAvatar.deadSet"),
            (paPlayerDeathHeadField, "PlayerAvatar.playerDeathHead"),
            (pdhPhysGrabObjectField, "PlayerDeathHead.physGrabObject"),
            (arenaWinnerPlayerField, "Arena.winnerPlayer"),
            (arenaPhotonViewField, "Arena.photonView (private)"),
            (arenaCurrentStateField, "Arena.currentState (internal)"),
            (arenaLevelField, "Arena.level (private)"),
            (arenaCrownCageDestroyedField, "Arena.crownCageDestroyed (private)"),
            (arenaPlayersAliveField, "Arena.playersAlive (internal)"),
        };

        foreach (var (field, name) in mustExist)
        {
            if (field == null) LatePlugin.Log?.LogError($"[ReflectionCache] Critical field '{name}' not found during static initialization.");
        }
    }
    #endregion


    #region ─── Network Helpers ───────────────────────────────────────────────
    /// <summary>
    /// Checks if the local client is currently the authoritative Master Client.
    /// This is more specific than just PhotonNetwork.IsMasterClient, as it ensures
    /// the local player holds the master client role.
    /// </summary>
    /// <returns>True if the local player is the Master Client, false otherwise.</returns>
    public static bool IsRealMasterClient()
    {
        return PhotonNetwork.IsMasterClient
            && PhotonNetwork.MasterClient == PhotonNetwork.LocalPlayer;
    }
    #endregion

    #region ─── Scene/State Checks ──────────────────────────────────────────────
    /// <summary>
    /// Checks if the L.A.T.E mod logic should be active in the current game state/scene.
    /// Logic should generally be disabled in Main Menu, Lobby Menu, and Tutorial.
    /// </summary>
    /// <returns>True if the mod logic should be active, false otherwise.</returns>
    public static bool IsModLogicActive()
    {
        if (SemiFunc.IsMainMenu()) return false;
        if (RunManager.instance == null) return false;
        if (SemiFunc.RunIsLobbyMenu()) return false;
        if (SemiFunc.RunIsTutorial()) return false;
        return true;
    }
    #endregion

    #region ─── Enemy Helpers (To be reviewed for EnemySyncManager) ──────────────────
    internal static bool TryGetEnemyPhotonView(Enemy enemy, out PhotonView? enemyPv)
    {
        enemyPv = null;
        if (enemy == null || enemyPhotonViewField == null) return false;
        try
        {
            enemyPv = enemyPhotonViewField.GetValue(enemy) as PhotonView;
            return enemyPv != null;
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[GameUtilities] Failed reflecting Enemy.PhotonView on '{enemy?.gameObject?.name ?? "NULL"}': {ex}");
            return false;
        }
    }

    internal static bool TryGetEnemyTargetViewIdReflected(Enemy enemy, out int targetViewId)
    {
        targetViewId = -1;
        if (enemy == null || enemyTargetPlayerViewIDField == null) return false;
        try
        {
            object? value = enemyTargetPlayerViewIDField.GetValue(enemy);
            if (value is int id)
            {
                targetViewId = id;
                return true;
            }
            LatePlugin.Log.LogWarning($"[GameUtilities] Reflected Enemy.TargetPlayerViewID for '{enemy?.gameObject?.name ?? "NULL"}' was not an int (Type: {value?.GetType()}).");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[GameUtilities] Failed reflecting Enemy.TargetPlayerViewID on '{enemy?.gameObject?.name ?? "NULL"}': {ex}");
        }
        return false;
    }

    internal static PlayerAvatar? GetInternalPlayerTarget(object enemyControllerInstance, FieldInfo? targetFieldInfo, string enemyTypeName)
    {
        if (enemyControllerInstance == null || targetFieldInfo == null) return null;
        try
        {
            return targetFieldInfo.GetValue(enemyControllerInstance) as PlayerAvatar;
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[GameUtilities] Failed reflecting {enemyTypeName}.playerTarget: {ex}");
            return null;
        }
    }

    internal static EnemyVision? GetEnemyVision(Enemy enemy)
    {
        if (enemy == null) return null;
        EnemyVision? vision = null;
        try
        {
            if (enemyVisionField != null) vision = enemyVisionField.GetValue(enemy) as EnemyVision;
            if (vision == null) vision = enemy.GetComponent<EnemyVision>();
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[GameUtilities] Error getting EnemyVision for '{enemy.gameObject?.name ?? "NULL"}': {ex}");
            vision = null;
        }
        if (vision == null) LatePlugin.Log.LogWarning($"[GameUtilities] Failed to get EnemyVision for enemy '{enemy.gameObject?.name ?? "NULL"}'.");
        return vision;
    }
    #endregion

    #region --- Photon Cache Clear (To be moved to PhotonUtilities.cs) ---
    public static void ClearPhotonCache(PhotonView photonView)
    {
        if (photonView == null) return;
        try
        {
            var removeFilter = removeFilterFieldInfo?.GetValue(null) as Hashtable;
            var keyByteSeven = keyByteSevenFieldInfo?.GetValue(null);
            var serverCleanOptions = serverCleanOptionsFieldInfo?.GetValue(null) as RaiseEventOptions;
            var raiseEventMethod = raiseEventInternalMethodInfo;

            if (removeFilter == null || keyByteSeven == null || serverCleanOptions == null || raiseEventMethod == null)
            {
                LatePlugin.Log.LogError("ClearPhotonCache failed: Reflection error getting PhotonNetwork internals.");
                return;
            }
            removeFilter[keyByteSeven] = photonView.InstantiationId;
            serverCleanOptions.CachingOption = EventCaching.RemoveFromRoomCache;
            raiseEventMethod.Invoke(null, new object[] { (byte)202, removeFilter, serverCleanOptions, SendOptions.SendReliable });
            LatePlugin.Log.LogDebug($"Sent RemoveFromRoomCache event using InstantiationId {photonView.InstantiationId} (ViewID: {photonView.ViewID})");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"Exception during ClearPhotonCache for InstantiationId {photonView.InstantiationId} (ViewID: {photonView.ViewID}): {ex}");
        }
    }
    #endregion


    #region ─── Component Cache ─────────────────────────────────────────────────────
    public static T[] GetCachedComponents<T>(ref T[] cache, ref float timeStamp, float refreshSeconds = 2f) where T : Object
    {
        if (cache == null || cache.Length == 0 || Time.unscaledTime - timeStamp > refreshSeconds)
        {
#if UNITY_2022_2_OR_NEWER
            cache = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            cache = Object.FindObjectsOfType<T>();
#endif
            timeStamp = Time.unscaledTime;
        }
        return cache;
    }
    #endregion

    #region ─── Coroutine Runner Methods ───────────────────────────────────────────
    public static MonoBehaviour? FindCoroutineRunner()
    {
        LatePlugin.Log.LogDebug("[GameUtilities] Finding coroutine runner…");
#if UNITY_2022_2_OR_NEWER
        if (Object.FindFirstObjectByType<RunManager>() is { } runMgr) return runMgr;
#else
        if (Object.FindObjectOfType<RunManager>() is { } runMgr) return runMgr;
#endif
        if (GameDirector.instance is { } gDir) return gDir;
        LatePlugin.Log.LogError("[GameUtilities] Failed to find suitable MonoBehaviour (RunManager or GameDirector) for coroutines!");
        return null;
    }
    #endregion

    #region ─── ValuablePropSwitch Helper ──────────────────────────────────────────
    internal static FieldInfo? GetVpsSetupCompleteField()
    {
        if (!_vpsFieldChecked)
        {
            _vpsSetupCompleteField = AccessTools.Field(typeof(ValuablePropSwitch), "SetupComplete");
            if (_vpsSetupCompleteField == null) LatePlugin.Log?.LogError("[GameUtilities] Failed to find internal field 'ValuablePropSwitch.SetupComplete'.");
            _vpsFieldChecked = true;
        }
        return _vpsSetupCompleteField;
    }
    #endregion

    #region ─── Player Avatar Methods ──────────────────────────────────────────────
    public static PlayerAvatar? FindPlayerAvatar(Player player)
    {
        if (player == null) return null;
        if (GameDirector.instance?.PlayerList != null)
        {
            foreach (var avatar in GameDirector.instance.PlayerList)
            {
                if (avatar == null) continue;
                var pv = GetPhotonView(avatar);
                if (pv != null && pv.OwnerActorNr == player.ActorNumber) return avatar;
            }
        }
        foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
        {
            if (avatar == null) continue;
            var pv = GetPhotonView(avatar);
            if (pv != null && pv.OwnerActorNr == player.ActorNumber) return avatar;
        }
        LatePlugin.Log.LogWarning($"[GameUtilities] Could not find PlayerAvatar for {player.NickName} (ActorNr: {player.ActorNumber}).");
        return null;
    }

    public static PlayerAvatar? FindLocalPlayerAvatar()
    {
        if (PlayerController.instance?.playerAvatar?.GetComponent<PlayerAvatar>() is PlayerAvatar localAvatar && GetPhotonView(localAvatar)?.IsMine == true)
        {
            return localAvatar;
        }
        foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
        {
            if (avatar == null) continue;
            if (GetPhotonView(avatar)?.IsMine == true) return avatar;
        }
        return null;
    }
    #endregion

    #region ─── PhotonView Helper Method ───────────────────────────────────────────
    public static PhotonView? GetPhotonView(Component component)
    {
        if (component == null) return null;
        if (component is PhotonView photonView) return photonView;
        var view = component.GetComponent<PhotonView>();
        if (view != null) return view;
        if (component is PlayerAvatar && paPhotonViewField != null)
        {
            try { return paPhotonViewField.GetValue(component) as PhotonView; }
            catch (Exception ex) { LatePlugin.Log?.LogWarning($"[GameUtilities] Failed to get PhotonView via reflection: {ex}"); }
        }
        return null;
    }
    #endregion

    #region ─── Player Nickname Helper ─────────────────────────────────────────────
    public static string GetPlayerNickname(PlayerAvatar avatar)
    {
        if (avatar == null) return "<NullAvatar>";
        var pv = GetPhotonView(avatar);
        if (pv?.Owner?.NickName != null) return pv.Owner.NickName;
        if (paPlayerNameField != null)
        {
            try
            {
                object? nameObj = paPlayerNameField.GetValue(avatar);
                if (nameObj is string nameStr && !string.IsNullOrEmpty(nameStr)) return nameStr + " (Reflected)";
            }
            catch (Exception ex) { LatePlugin.Log?.LogWarning($"[GameUtilities] Failed to reflect playerName for avatar: {ex.Message}"); }
        }
        if (pv?.OwnerActorNr > 0) return $"ActorNr {pv.OwnerActorNr}";
        LatePlugin.Log?.LogWarning($"[GameUtilities] Could not determine nickname for avatar (ViewID: {pv?.ViewID ?? 0}), returning fallback.");
        return "<UnknownPlayer>";
    }
    #endregion

    #region ─── PhysGrabber Helper ─────────────────────────────────────────────────
    internal static int GetPhysGrabberViewId(PlayerAvatar playerAvatar)
    {
        if (playerAvatar == null || paPhysGrabberField == null || pgPhotonViewField == null) return -1;
        try
        {
            object physGrabberObj = paPhysGrabberField.GetValue(playerAvatar);
            if (physGrabberObj is PhysGrabber physGrabber)
            {
                PhotonView? pv = pgPhotonViewField.GetValue(physGrabber) as PhotonView;
                if (pv != null) return pv.ViewID;
            }
        }
        catch (Exception ex) { LatePlugin.Log?.LogError($"[GameUtilities] GetPhysGrabberViewId reflection error: {ex}"); }
        return -1;
    }
    #endregion

    #region ─── ViewID Helper ──────────────────────────────────────────────────────
    public static int GetViewId(Component comp)
    {
        return GetPhotonView(comp)?.ViewID ?? -1;
    }
    #endregion

    #region --- PGO PhotonView Helper (To be moved to PhotonUtilities) ---
    public static PhotonView? GetPhotonViewFromPGO(PhysGrabObject? pgo)
    {
        if (pgo == null || pgoPhotonViewField == null) return null;
        try
        {
            return pgoPhotonViewField.GetValue(pgo) as PhotonView;
        }
        catch (Exception ex)
        {
            LatePlugin.Log?.LogError($"[GameUtilities] Reflection error getting PhotonView from PGO '{pgo.gameObject?.name ?? "NULL"}': {ex}");
            return null;
        }
    }
    #endregion

    #region --- Shuffle Extension ---
    private static readonly System.Random Rng = new System.Random(); // Shared Random instance for Shuffle

    /// <summary>
    /// Randomly shuffles the elements of a list using the Fisher-Yates algorithm.
    /// </summary>
    public static void Shuffle<T>(this IList<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Rng.Next(n + 1);
            (list[k], list[n]) = (list[n], list[k]); // Tuple swap
        }
    }
    #endregion
}