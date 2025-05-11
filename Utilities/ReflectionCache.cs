// File: L.A.T.E/Utilities/ReflectionCache.cs
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;

using Photon.Pun;

using LATE.Core;    // LatePlugin.Log

namespace LATE.Utilities;

internal static class ReflectionCache
{
    private const string LogPrefix = "[ReflectionCache]";

    /* ─── helpers ────────────────────────────────────────────────────────── */

    // generic: for normal (non-static) classes
    private static FieldInfo? F<T>(string name) => AccessTools.Field(typeof(T), name);
    private static MethodInfo? M<T>(string name) => AccessTools.Method(typeof(T), name);

    // non-generic: so we can handle static classes like PhotonNetwork
    private static FieldInfo? F(Type t, string name) => AccessTools.Field(t, name);
    private static MethodInfo? M(Type t, string name) => AccessTools.Method(t, name);

    private static readonly List<(FieldInfo? Fi, string Name)> _critical = new();
    private static FieldInfo? Crit(Type t, string name)
    {
        var fi = F(t, name);
        _critical.Add((fi, $"{t.Name}.{name}"));
        return fi;
    }
    private static FieldInfo? Crit<T>(string name) => Crit(typeof(T), name);   // convenience

    /* ─── PhotonNetwork (static class → use Type overloads) ──────────────── */

    internal static readonly FieldInfo? PhotonNetwork_RemoveFilterField = Crit(typeof(PhotonNetwork), "removeFilter");
    internal static readonly FieldInfo? PhotonNetwork_KeyByteSevenField = Crit(typeof(PhotonNetwork), "keyByteSeven");
    internal static readonly FieldInfo? PhotonNetwork_ServerCleanOptionsField = F(typeof(PhotonNetwork), "ServerCleanOptions");

    // Keep the ORIGINAL name so existing code compiles
    internal static readonly MethodInfo? PhotonNetwork_RaiseEventInternalMethod =
        M(typeof(PhotonNetwork), "RaiseEventInternal");

    /* ───────────────────────  RunManager   ──────────────────────── */

    internal static readonly FieldInfo? RunManager_RunManagerPUNField = F<RunManager>("runManagerPUN");
    internal static readonly FieldInfo? RunManager_RunLivesField = F<RunManager>("runLives");
    internal static readonly FieldInfo? RunManager_RestartingField = F<RunManager>("restarting");
    internal static readonly FieldInfo? RunManager_RestartingDoneField = F<RunManager>("restartingDone");
    internal static readonly FieldInfo? RunManager_LobbyJoinField = F<RunManager>("lobbyJoin");
    internal static readonly FieldInfo? RunManager_WaitToChangeSceneField = F<RunManager>("waitToChangeScene");
    internal static readonly FieldInfo? RunManager_GameOverField = F<RunManager>("gameOver");

    /* ───────────────────────  RunManagerPUN  ────────────────────── */

    internal static readonly FieldInfo? RunManagerPUN_PhotonViewField = F<RunManagerPUN>("photonView");

    /* ───────────────────────  PlayerAvatar  ─────────────────────── */

    internal static readonly FieldInfo? PlayerAvatar_SpawnedField = F<PlayerAvatar>("spawned");
    internal static readonly FieldInfo? PlayerAvatar_PhotonViewField = F<PlayerAvatar>("photonView");
    internal static readonly FieldInfo? PlayerAvatar_PlayerNameField = F<PlayerAvatar>("playerName");
    internal static readonly FieldInfo? PlayerAvatar_OutroDoneField = F<PlayerAvatar>("outroDone");
    internal static readonly FieldInfo? PlayerAvatar_VoiceChatFetchedField = F<PlayerAvatar>("voiceChatFetched");
    internal static readonly FieldInfo? PlayerAvatar_VoiceChatField = F<PlayerAvatar>("voiceChat");
    internal static readonly FieldInfo? PlayerAvatar_IsDisabledField = F<PlayerAvatar>("isDisabled");
    internal static readonly FieldInfo? PlayerAvatar_DeadSetField = F<PlayerAvatar>("deadSet");
    internal static readonly FieldInfo? PlayerAvatar_PlayerDeathHeadField = F<PlayerAvatar>("playerDeathHead");
    internal static readonly FieldInfo? PlayerAvatar_PhysGrabberField = F<PlayerAvatar>("physGrabber");

    /* ───────────────────────  PlayerDeathHead  ──────────────────── */

    internal static readonly FieldInfo? PlayerDeathHead_PhysGrabObjectField = F<PlayerDeathHead>("physGrabObject");

    /* ───────────────────────  PhysGrabber + friends  ────────────── */

    internal static readonly FieldInfo? PhysGrabber_PhotonViewField = F<PhysGrabber>("photonView");

    internal static readonly FieldInfo? PhysGrabObject_PhotonViewField = F<PhysGrabObject>("photonView");
    internal static readonly FieldInfo? PhysGrabObject_IsMeleeField = F<PhysGrabObject>("isMelee");

    internal static readonly FieldInfo? PhysGrabHinge_ClosedField = F<PhysGrabHinge>("closed");
    internal static readonly FieldInfo? PhysGrabHinge_BrokenField = F<PhysGrabHinge>("broken");
    internal static readonly FieldInfo? PhysGrabHinge_JointField = F<PhysGrabHinge>("joint");

    /* ───────────────────────  Item/Inventory  ───────────────────── */

    internal static readonly FieldInfo? ItemAttributes_InstanceNameField = F<ItemAttributes>("instanceName");
    internal static readonly FieldInfo? ItemAttributes_ValueField = F<ItemAttributes>("value");
    internal static readonly FieldInfo? ItemAttributes_ShopItemField = F<ItemAttributes>("shopItem");
    internal static readonly FieldInfo? ItemAttributes_DisableUIField = F<ItemAttributes>("disableUI");

    internal static readonly FieldInfo? ItemEquippable_CurrentStateField = F<ItemEquippable>("currentState");
    internal static readonly FieldInfo? ItemEquippable_OwnerPlayerIdField = F<ItemEquippable>("ownerPlayerId");
    internal static readonly FieldInfo? ItemEquippable_InventorySpotIndex = F<ItemEquippable>("inventorySpotIndex");

    internal static readonly FieldInfo? ItemGrenade_IsActiveField = F<ItemGrenade>("isActive");

    internal static readonly FieldInfo? ItemTracker_CurrentTargetField = F<ItemTracker>("currentTarget");
    internal static readonly FieldInfo? ItemTracker_CurrentTargetPGOField = F<ItemTracker>("currentTargetPhysGrabObject");

    internal static readonly FieldInfo? ItemMine_StateField = F<ItemMine>("state");
    internal static readonly FieldInfo? ItemToggle_DisabledField = F<ItemToggle>("disabled");
    internal static readonly FieldInfo? ItemHealthPack_UsedField = F<ItemHealthPack>("used");

    /* ───────────────────────  Enemy & AI  ───────────────────────── */

    internal static readonly FieldInfo? Enemy_VisionField = F<Enemy>("Vision");
    internal static readonly FieldInfo? Enemy_PhotonViewField = F<Enemy>("PhotonView");
    internal static readonly FieldInfo? Enemy_TargetPlayerAvatarField = F<Enemy>("TargetPlayerAvatar");
    internal static readonly FieldInfo? Enemy_TargetPlayerViewIDField = F<Enemy>("TargetPlayerViewID");
    internal static readonly FieldInfo? Enemy_EnemyParentField = F<Enemy>("EnemyParent");

    // per-enemy controller “playerTarget” style fields
    internal static readonly FieldInfo? EnemyBeamer_PlayerTargetField = F<EnemyBeamer>("playerTarget");
    internal static readonly FieldInfo? EnemyCeilingEye_TargetPlayerField = F<EnemyCeilingEye>("targetPlayer");
    internal static readonly FieldInfo? EnemyFloater_TargetPlayerField = F<EnemyFloater>("targetPlayer");
    internal static readonly FieldInfo? EnemyRobe_TargetPlayerField = F<EnemyRobe>("targetPlayer");
    internal static readonly FieldInfo? EnemyRunner_TargetPlayerField = F<EnemyRunner>("targetPlayer");
    internal static readonly FieldInfo? EnemySlowWalker_TargetPlayerField = F<EnemySlowWalker>("targetPlayer");
    internal static readonly FieldInfo? EnemyThinMan_PlayerTargetField = F<EnemyThinMan>("playerTarget");
    internal static readonly FieldInfo? EnemyTumbler_TargetPlayerField = F<EnemyTumbler>("targetPlayer");
    internal static readonly FieldInfo? EnemyUpscream_TargetPlayerField = F<EnemyUpscream>("targetPlayer");

    /* ───────────────────────  EnemyOnScreen + Nav/Investigate  ──── */

    internal static readonly FieldInfo? EnemyOnScreen_OnScreenPlayerField = F<EnemyOnScreen>("OnScreenPlayer");
    internal static readonly FieldInfo? EnemyOnScreen_CulledPlayerField = F<EnemyOnScreen>("CulledPlayer");

    internal static readonly FieldInfo? EnemyNavMeshAgent_AgentField = F<EnemyNavMeshAgent>("Agent");

    internal static readonly FieldInfo? EnemyStateInvestigate_OnInvestigateTriggeredPositionField =
        F<EnemyStateInvestigate>("onInvestigateTriggeredPosition");

    /* ───────────────────────  EnemyParent  ──────────────────────── */

    internal static readonly FieldInfo? EnemyParent_SpawnedField = F<EnemyParent>("Spawned");

    /* ───────────────────────  Round / Valuable / Extraction  ────── */

    internal static readonly FieldInfo? RoundDirector_ExtractionPointActiveField = F<RoundDirector>("extractionPointActive");
    internal static readonly FieldInfo? RoundDirector_ExtractionPointCurrentField = F<RoundDirector>("extractionPointCurrent");
    internal static readonly FieldInfo? RoundDirector_ExtractionPointSurplusField = F<RoundDirector>("extractionPointSurplus");

    internal static readonly FieldInfo? ValuableDirector_ValuableTargetAmountField = F<ValuableDirector>("valuableTargetAmount");
    internal static readonly FieldInfo? ValuableObject_DollarValueSetField = F<ValuableObject>("dollarValueSet");

    internal static readonly FieldInfo? ExtractionPoint_CurrentStateField = F<ExtractionPoint>("currentState");
    internal static readonly FieldInfo? ExtractionPoint_HaulGoalFetchedField = F<ExtractionPoint>("haulGoalFetched");
    internal static readonly FieldInfo? ExtractionPoint_IsShopField = F<ExtractionPoint>("isShop");

    internal static readonly FieldInfo? ShopManager_ShoppingListField = F<ShopManager>("shoppingList");

    /* ───────────────────────  Module / Arena / Misc  ────────────── */

    internal static readonly FieldInfo? Module_SetupDoneField = F<Module>("SetupDone");
    internal static readonly FieldInfo? Module_ConnectingTopField = F<Module>("ConnectingTop");
    internal static readonly FieldInfo? Module_ConnectingBottomField = F<Module>("ConnectingBottom");
    internal static readonly FieldInfo? Module_ConnectingRightField = F<Module>("ConnectingRight");
    internal static readonly FieldInfo? Module_ConnectingLeftField = F<Module>("ConnectingLeft");
    internal static readonly FieldInfo? Module_FirstField = F<Module>("First");

    internal static readonly FieldInfo? Arena_WinnerPlayerField = F<Arena>("winnerPlayer");
    internal static readonly FieldInfo? Arena_PhotonViewField = F<Arena>("photonView");
    internal static readonly FieldInfo? Arena_CurrentStateField = F<Arena>("currentState");
    internal static readonly FieldInfo? Arena_LevelField = F<Arena>("level");
    internal static readonly FieldInfo? Arena_CrownCageDestroyedField = F<Arena>("crownCageDestroyed");
    internal static readonly FieldInfo? Arena_PlayersAliveField = F<Arena>("playersAlive");

    /* ───────────────────────  ValuablePropSwitch  ───────────────── */

    internal static readonly FieldInfo? ValuablePropSwitch_SetupCompleteField = F<ValuablePropSwitch>("SetupComplete");

    /* ───────────────────────  TruckScreenText  ──────────────────── */

    internal static readonly FieldInfo? TruckScreenText_CurrentPageIndexField = F<TruckScreenText>("currentPageIndex");

    static ReflectionCache()
    {
        int missing = 0;
        foreach (var (fi, name) in _critical)
            if (fi == null)
            {
                ++missing;
                LatePlugin.Log.LogError($"{LogPrefix} CRITICAL: {name} not found! Game update may have broken the mod.");
            }

        LatePlugin.Log.LogDebug($"{LogPrefix} Init complete – {_critical.Count} critical fields scanned, {missing} missing.");
    }
}