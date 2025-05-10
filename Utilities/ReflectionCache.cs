// File: L.A.T.E/Utilities/ReflectionCache.cs
using System;
using System.Reflection;
using HarmonyLib;
using Photon.Pun; // For PhotonNetwork
using LATE.Core; // For LatePlugin.Log

namespace LATE.Utilities; // File-scoped namespace

/// <summary>
/// Centralized cache for all reflected FieldInfo and MethodInfo instances.
/// This helps to avoid repeated reflection calls and provides a single point
/// to manage and verify reflected members.
/// </summary>
internal static class ReflectionCache
{
    // --- PhotonNetwork reflection fields ---
    internal static readonly FieldInfo? PhotonNetwork_RemoveFilterField = AccessTools.Field(typeof(PhotonNetwork), "removeFilter");
    internal static readonly FieldInfo? PhotonNetwork_KeyByteSevenField = AccessTools.Field(typeof(PhotonNetwork), "keyByteSeven");
    internal static readonly FieldInfo? PhotonNetwork_ServerCleanOptionsField = AccessTools.Field(typeof(PhotonNetwork), "ServerCleanOptions");
    internal static readonly MethodInfo? PhotonNetwork_RaiseEventInternalMethod = AccessTools.Method(typeof(PhotonNetwork), "RaiseEventInternal");

    // --- RunManager fields ---
    internal static readonly FieldInfo? RunManager_RunManagerPUNField = AccessTools.Field(typeof(RunManager), "runManagerPUN");
    internal static readonly FieldInfo? RunManager_RunLivesField = AccessTools.Field(typeof(RunManager), "runLives");
    internal static readonly FieldInfo? RunManager_RestartingField = AccessTools.Field(typeof(RunManager), "restarting");
    internal static readonly FieldInfo? RunManager_RestartingDoneField = AccessTools.Field(typeof(RunManager), "restartingDone");
    internal static readonly FieldInfo? RunManager_LobbyJoinField = AccessTools.Field(typeof(RunManager), "lobbyJoin");
    internal static readonly FieldInfo? RunManager_WaitToChangeSceneField = AccessTools.Field(typeof(RunManager), "waitToChangeScene");
    internal static readonly FieldInfo? RunManager_GameOverField = AccessTools.Field(typeof(RunManager), "gameOver");

    // --- RunManagerPUN fields ---
    internal static readonly FieldInfo? RunManagerPUN_PhotonViewField = AccessTools.Field(typeof(RunManagerPUN), "photonView");

    // --- PlayerAvatar fields ---
    internal static readonly FieldInfo? PlayerAvatar_SpawnedField = AccessTools.Field(typeof(PlayerAvatar), "spawned");
    internal static readonly FieldInfo? PlayerAvatar_PhotonViewField = AccessTools.Field(typeof(PlayerAvatar), "photonView");
    internal static readonly FieldInfo? PlayerAvatar_PlayerNameField = AccessTools.Field(typeof(PlayerAvatar), "playerName");
    internal static readonly FieldInfo? PlayerAvatar_OutroDoneField = AccessTools.Field(typeof(PlayerAvatar), "outroDone");
    internal static readonly FieldInfo? PlayerAvatar_VoiceChatFetchedField = AccessTools.Field(typeof(PlayerAvatar), "voiceChatFetched");
    internal static readonly FieldInfo? PlayerAvatar_VoiceChatField = AccessTools.Field(typeof(PlayerAvatar), "voiceChat");
    internal static readonly FieldInfo? PlayerAvatar_IsDisabledField = AccessTools.Field(typeof(PlayerAvatar), "isDisabled");
    internal static readonly FieldInfo? PlayerAvatar_DeadSetField = AccessTools.Field(typeof(PlayerAvatar), "deadSet");
    internal static readonly FieldInfo? PlayerAvatar_PlayerDeathHeadField = AccessTools.Field(typeof(PlayerAvatar), "playerDeathHead");
    internal static readonly FieldInfo? PlayerAvatar_PhysGrabberField = AccessTools.Field(typeof(PlayerAvatar), "physGrabber");

    // --- PlayerDeathHead fields ---
    internal static readonly FieldInfo? PlayerDeathHead_PhysGrabObjectField = AccessTools.Field(typeof(PlayerDeathHead), "physGrabObject");

    // --- PhysGrabber fields ---
    internal static readonly FieldInfo? PhysGrabber_PhotonViewField = AccessTools.Field(typeof(PhysGrabber), "photonView");

    // --- PhysGrabObject fields ---
    internal static readonly FieldInfo? PhysGrabObject_PhotonViewField = AccessTools.Field(typeof(PhysGrabObject), "photonView");
    internal static readonly FieldInfo? PhysGrabObject_IsMeleeField = AccessTools.Field(typeof(PhysGrabObject), "isMelee");

    // --- PhysGrabHinge fields ---
    internal static readonly FieldInfo? PhysGrabHinge_ClosedField = AccessTools.Field(typeof(PhysGrabHinge), "closed");
    internal static readonly FieldInfo? PhysGrabHinge_BrokenField = AccessTools.Field(typeof(PhysGrabHinge), "broken");
    internal static readonly FieldInfo? PhysGrabHinge_JointField = AccessTools.Field(typeof(PhysGrabHinge), "joint");

    // --- ItemAttributes fields ---
    internal static readonly FieldInfo? ItemAttributes_InstanceNameField = AccessTools.Field(typeof(ItemAttributes), "instanceName");
    internal static readonly FieldInfo? ItemAttributes_ValueField = AccessTools.Field(typeof(ItemAttributes), "value");
    internal static readonly FieldInfo? ItemAttributes_ShopItemField = AccessTools.Field(typeof(ItemAttributes), "shopItem");
    internal static readonly FieldInfo? ItemAttributes_DisableUIField = AccessTools.Field(typeof(ItemAttributes), "disableUI");

    // --- ItemEquippable fields ---
    internal static readonly FieldInfo? ItemEquippable_CurrentStateField = AccessTools.Field(typeof(ItemEquippable), "currentState");
    internal static readonly FieldInfo? ItemEquippable_OwnerPlayerIdField = AccessTools.Field(typeof(ItemEquippable), "ownerPlayerId");
    internal static readonly FieldInfo? ItemEquippable_InventorySpotIndexField = AccessTools.Field(typeof(ItemEquippable), "inventorySpotIndex");

    // --- ItemGrenade fields ---
    internal static readonly FieldInfo? ItemGrenade_IsActiveField = AccessTools.Field(typeof(ItemGrenade), "isActive");

    // --- ItemTracker fields ---
    internal static readonly FieldInfo? ItemTracker_CurrentTargetField = AccessTools.Field(typeof(ItemTracker), "currentTarget");
    internal static readonly FieldInfo? ItemTracker_CurrentTargetPGOField = AccessTools.Field(typeof(ItemTracker), "currentTargetPhysGrabObject");

    // --- ItemMine fields ---
    internal static readonly FieldInfo? ItemMine_StateField = AccessTools.Field(typeof(ItemMine), "state");

    // --- ItemToggle fields ---
    internal static readonly FieldInfo? ItemToggle_DisabledField = AccessTools.Field(typeof(ItemToggle), "disabled");

    // --- ItemHealthPack fields ---
    internal static readonly FieldInfo? ItemHealthPack_UsedField = AccessTools.Field(typeof(ItemHealthPack), "used");

    // --- Enemy fields ---
    internal static readonly FieldInfo? Enemy_VisionField = AccessTools.Field(typeof(Enemy), "Vision");
    internal static readonly FieldInfo? Enemy_PhotonViewField = AccessTools.Field(typeof(Enemy), "PhotonView");
    internal static readonly FieldInfo? Enemy_TargetPlayerAvatarField = AccessTools.Field(typeof(Enemy), "TargetPlayerAvatar");
    internal static readonly FieldInfo? Enemy_TargetPlayerViewIDField = AccessTools.Field(typeof(Enemy), "TargetPlayerViewID");
    internal static readonly FieldInfo? Enemy_EnemyParentField = AccessTools.Field(typeof(Enemy), "EnemyParent");

    // --- Enemy specific controller fields for player targets ---
    internal static readonly FieldInfo? EnemyBeamer_PlayerTargetField = AccessTools.Field(typeof(EnemyBeamer), "playerTarget");
    internal static readonly FieldInfo? EnemyCeilingEye_TargetPlayerField = AccessTools.Field(typeof(EnemyCeilingEye), "targetPlayer");
    internal static readonly FieldInfo? EnemyFloater_TargetPlayerField = AccessTools.Field(typeof(EnemyFloater), "targetPlayer");
    internal static readonly FieldInfo? EnemyRobe_TargetPlayerField = AccessTools.Field(typeof(EnemyRobe), "targetPlayer");
    internal static readonly FieldInfo? EnemyRunner_TargetPlayerField = AccessTools.Field(typeof(EnemyRunner), "targetPlayer");
    internal static readonly FieldInfo? EnemySlowWalker_TargetPlayerField = AccessTools.Field(typeof(EnemySlowWalker), "targetPlayer");
    internal static readonly FieldInfo? EnemyThinMan_PlayerTargetField = AccessTools.Field(typeof(EnemyThinMan), "playerTarget");
    internal static readonly FieldInfo? EnemyTumbler_TargetPlayerField = AccessTools.Field(typeof(EnemyTumbler), "targetPlayer");
    internal static readonly FieldInfo? EnemyUpscream_TargetPlayerField = AccessTools.Field(typeof(EnemyUpscream), "targetPlayer");
    // Add other enemy types if they have a 'playerTarget' or similar field that needs caching.

    // --- EnemyOnScreen fields ---
    internal static readonly FieldInfo? EnemyOnScreen_OnScreenPlayerField = AccessTools.Field(typeof(EnemyOnScreen), "OnScreenPlayer");
    internal static readonly FieldInfo? EnemyOnScreen_CulledPlayerField = AccessTools.Field(typeof(EnemyOnScreen), "CulledPlayer");

    // --- EnemyNavMeshAgent fields ---
    internal static readonly FieldInfo? EnemyNavMeshAgent_AgentField = AccessTools.Field(typeof(EnemyNavMeshAgent), "Agent");

    // --- EnemyStateInvestigate fields ---
    internal static readonly FieldInfo? EnemyStateInvestigate_OnInvestigateTriggeredPositionField = AccessTools.Field(typeof(EnemyStateInvestigate), "onInvestigateTriggeredPosition");

    // --- EnemyParent Fields ---
    internal static readonly FieldInfo? EnemyParent_SpawnedField = AccessTools.Field(typeof(EnemyParent), "Spawned");

    // --- RoundDirector fields ---
    internal static readonly FieldInfo? RoundDirector_ExtractionPointActiveField = AccessTools.Field(typeof(RoundDirector), "extractionPointActive");
    internal static readonly FieldInfo? RoundDirector_ExtractionPointCurrentField = AccessTools.Field(typeof(RoundDirector), "extractionPointCurrent");
    internal static readonly FieldInfo? RoundDirector_ExtractionPointSurplusField = AccessTools.Field(typeof(RoundDirector), "extractionPointSurplus");

    // --- ValuableDirector fields ---
    internal static readonly FieldInfo? ValuableDirector_ValuableTargetAmountField = AccessTools.Field(typeof(ValuableDirector), "valuableTargetAmount");

    // --- ValuableObject fields ---
    internal static readonly FieldInfo? ValuableObject_DollarValueSetField = AccessTools.Field(typeof(ValuableObject), "dollarValueSet");

    // --- ExtractionPoint fields ---
    internal static readonly FieldInfo? ExtractionPoint_CurrentStateField = AccessTools.Field(typeof(ExtractionPoint), "currentState");
    internal static readonly FieldInfo? ExtractionPoint_HaulGoalFetchedField = AccessTools.Field(typeof(ExtractionPoint), "haulGoalFetched");
    internal static readonly FieldInfo? ExtractionPoint_IsShopField = AccessTools.Field(typeof(ExtractionPoint), "isShop");

    // --- ShopManager fields ---
    internal static readonly FieldInfo? ShopManager_ShoppingListField = AccessTools.Field(typeof(ShopManager), "shoppingList");

    // --- Module fields ---
    internal static readonly FieldInfo? Module_SetupDoneField = AccessTools.Field(typeof(Module), "SetupDone");
    internal static readonly FieldInfo? Module_ConnectingTopField = AccessTools.Field(typeof(Module), "ConnectingTop");
    internal static readonly FieldInfo? Module_ConnectingBottomField = AccessTools.Field(typeof(Module), "ConnectingBottom");
    internal static readonly FieldInfo? Module_ConnectingRightField = AccessTools.Field(typeof(Module), "ConnectingRight");
    internal static readonly FieldInfo? Module_ConnectingLeftField = AccessTools.Field(typeof(Module), "ConnectingLeft");
    internal static readonly FieldInfo? Module_FirstField = AccessTools.Field(typeof(Module), "First");

    // --- Arena Fields ---
    internal static readonly FieldInfo? Arena_WinnerPlayerField = AccessTools.Field(typeof(Arena), "winnerPlayer");
    internal static readonly FieldInfo? Arena_PhotonViewField = AccessTools.Field(typeof(Arena), "photonView");
    internal static readonly FieldInfo? Arena_CurrentStateField = AccessTools.Field(typeof(Arena), "currentState");
    internal static readonly FieldInfo? Arena_LevelField = AccessTools.Field(typeof(Arena), "level");
    internal static readonly FieldInfo? Arena_CrownCageDestroyedField = AccessTools.Field(typeof(Arena), "crownCageDestroyed");
    internal static readonly FieldInfo? Arena_PlayersAliveField = AccessTools.Field(typeof(Arena), "playersAlive");

    // --- ValuablePropSwitch fields (internal, so caching might be less critical but good for consistency) ---
    internal static readonly FieldInfo? ValuablePropSwitch_SetupCompleteField = AccessTools.Field(typeof(ValuablePropSwitch), "SetupComplete");


    // --- TruckScreenText fields ---
    internal static readonly FieldInfo? TruckScreenText_CurrentPageIndexField = AccessTools.Field(typeof(TruckScreenText), "currentPageIndex");

    static ReflectionCache()
    {
        // List of fields that are considered critical for the mod's functionality.
        // If any of these are not found, a log error will be generated.
        var criticalFields = new (FieldInfo? Field, string Name)[]
        {
            (PlayerAvatar_PlayerNameField, "PlayerAvatar.playerName"),
            (ValuableObject_DollarValueSetField, "ValuableObject.dollarValueSet"),
            (Enemy_VisionField, "Enemy.Vision (internal property, check game updates if null)"),
            (Enemy_PhotonViewField, "Enemy.PhotonView (internal property, check game updates if null)"),
            (Enemy_TargetPlayerAvatarField, "Enemy.TargetPlayerAvatar (internal property, check game updates if null)"),
            (Enemy_TargetPlayerViewIDField, "Enemy.TargetPlayerViewID (internal property, check game updates if null)"),
            (Enemy_EnemyParentField, "Enemy.EnemyParent (internal field, check game updates if null)"),
            (EnemyParent_SpawnedField, "EnemyParent.Spawned (internal field, check game updates if null)"),
            (RoundDirector_ExtractionPointActiveField, "RoundDirector.extractionPointActive"),
            (RoundDirector_ExtractionPointCurrentField, "RoundDirector.extractionPointCurrent"),
            (ExtractionPoint_CurrentStateField, "ExtractionPoint.currentState (internal property)"),
            (ExtractionPoint_HaulGoalFetchedField, "ExtractionPoint.haulGoalFetched (internal field)"),
            (ExtractionPoint_IsShopField, "ExtractionPoint.isShop"),
            (ShopManager_ShoppingListField, "ShopManager.shoppingList (internal field)"),
            (RoundDirector_ExtractionPointSurplusField, "RoundDirector.extractionPointSurplus"),
            (PhysGrabHinge_ClosedField, "PhysGrabHinge.closed (internal field)"),
            (PhysGrabHinge_BrokenField, "PhysGrabHinge.broken (internal field)"),
            (ItemAttributes_ValueField, "ItemAttributes.value"),
            (ItemAttributes_ShopItemField, "ItemAttributes.shopItem"),
            (ItemEquippable_CurrentStateField, "ItemEquippable.currentState (internal property)"),
            (ItemEquippable_OwnerPlayerIdField, "ItemEquippable.ownerPlayerId (internal field)"),
            (ItemEquippable_InventorySpotIndexField, "ItemEquippable.inventorySpotIndex (internal field)"),
            (PlayerAvatar_PhysGrabberField, "PlayerAvatar.physGrabber"),
            (ItemMine_StateField, "ItemMine.state (internal property)"),
            (ItemGrenade_IsActiveField, "ItemGrenade.isActive (internal field)"),
            (ItemTracker_CurrentTargetField, "ItemTracker.currentTarget (internal field)"),
            (ItemToggle_DisabledField, "ItemToggle.disabled (internal field)"),
            (ItemHealthPack_UsedField, "ItemHealthPack.used (internal field)"),
            (PhysGrabObject_IsMeleeField, "PhysGrabObject.isMelee"),
            (PhysGrabber_PhotonViewField, "PhysGrabber.photonView (internal field)"),
            (PhysGrabObject_PhotonViewField, "PhysGrabObject.photonView (internal field)"),
            (RunManager_GameOverField, "RunManager.gameOver"),
            (RunManagerPUN_PhotonViewField, "RunManagerPUN.photonView (internal field)"),
            (Module_SetupDoneField, "Module.SetupDone (internal field)"),
            (TruckScreenText_CurrentPageIndexField, "TruckScreenText.currentPageIndex (internal field)"),
            (PlayerAvatar_IsDisabledField, "PlayerAvatar.isDisabled"),
            (PlayerAvatar_DeadSetField, "PlayerAvatar.deadSet"),
            (PlayerAvatar_PlayerDeathHeadField, "PlayerAvatar.playerDeathHead"),
            (PlayerDeathHead_PhysGrabObjectField, "PlayerDeathHead.physGrabObject"),
            (Arena_WinnerPlayerField, "Arena.winnerPlayer (internal field)"),
            (Arena_PhotonViewField, "Arena.photonView (internal field)"),
            (Arena_CurrentStateField, "Arena.currentState (internal property)"),
            (Arena_LevelField, "Arena.level (internal field)"),
            (Arena_CrownCageDestroyedField, "Arena.crownCageDestroyed (internal field)"),
        };

        foreach (var (field, name) in criticalFields)
        {
            if (field == null)
            {
                LatePlugin.Log?.LogError($"[ReflectionCache] CRITICAL: Reflected member '{name}' not found. Mod functionality may be impaired. This might be due to a game update.");
            }
        }
        LatePlugin.Log?.LogDebug($"[ReflectionCache] Static initialization complete. Verified {criticalFields.Length} critical members.");
    }
}