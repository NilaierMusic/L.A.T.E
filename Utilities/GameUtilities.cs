using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Reflection;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Object = UnityEngine.Object;

namespace LATE
{
    /// <summary>
    /// Caches FieldInfo / MethodInfo handles that are accessed frequently via reflection.
    /// Having them in one place avoids string‑based reflection calls in hot‑paths
    /// and lets us fail early (and loudly) on game updates.
    /// </summary>
    internal static class Utilities
    {
        #region ─── Helpers ──────────────────────────────────────────────────────────────
        // Short‑hand wrappers – mainly for readability and to keep all reflection calls identical.
        private static FieldInfo F(Type type, string name) => AccessTools.Field(type, name);

        private static MethodInfo M(Type type, string name) => AccessTools.Method(type, name);

        // Logs an error if a reflection field is not set.
        private static void LogIfNull(FieldInfo field, string humanName)
        {
            if (field == null)
                LATE.Core.LatePlugin.Log?.LogError($"[Reflection] Could not locate '{humanName}'");
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
            // Ensures that not only is there a master client, but that we are it.
            return PhotonNetwork.IsMasterClient
                && PhotonNetwork.MasterClient == PhotonNetwork.LocalPlayer;
        }
        #endregion

        #region ─── Reflection Fields ───────────────────────────────────────────────────
        // --- PhotonNetwork reflection fields ---
        public static readonly FieldInfo? removeFilterFieldInfo = F(
            typeof(PhotonNetwork),
            "removeFilter"
        );
        public static readonly FieldInfo? keyByteSevenFieldInfo = F(
            typeof(PhotonNetwork),
            "keyByteSeven"
        );
        public static readonly FieldInfo? serverCleanOptionsFieldInfo = F(
            typeof(PhotonNetwork),
            "ServerCleanOptions"
        );
        public static readonly MethodInfo? raiseEventInternalMethodInfo = M(
            typeof(PhotonNetwork),
            "RaiseEventInternal"
        );

        // --- Core Game Logic reflection fields ---
        // RunManager fields.
        public static readonly FieldInfo? rmRunManagerPUNField = F(
            typeof(RunManager),
            "runManagerPUN"
        );
        public static readonly FieldInfo? rmRunLivesField = F(typeof(RunManager), "runLives");
        public static readonly FieldInfo? rmRestartingField = F(typeof(RunManager), "restarting");
        public static readonly FieldInfo? rmRestartingDoneField = F(
            typeof(RunManager),
            "restartingDone"
        );
        public static readonly FieldInfo? rmLobbyJoinField = F(typeof(RunManager), "lobbyJoin");
        public static readonly FieldInfo? rmWaitToChangeSceneField = F(
            typeof(RunManager),
            "waitToChangeScene"
        );
        public static readonly FieldInfo? rmGameOverField = F(typeof(RunManager), "gameOver");

        // --- RunManagerPUN fields ---
        public static readonly FieldInfo? rmpPhotonViewField = F(
            typeof(RunManagerPUN),
            "photonView"
        );

        // PlayerAvatar fields.
        public static readonly FieldInfo? paSpawnedField = F(typeof(PlayerAvatar), "spawned");
        public static readonly FieldInfo? paPhotonViewField = F(typeof(PlayerAvatar), "photonView");
        public static readonly FieldInfo? paPlayerNameField = F(typeof(PlayerAvatar), "playerName");
        public static readonly FieldInfo? paOutroDoneField = F(typeof(PlayerAvatar), "outroDone");
        public static readonly FieldInfo? paVoiceChatFetchedField = F(
            typeof(PlayerAvatar),
            "voiceChatFetched"
        );
        public static readonly FieldInfo? paVoiceChatField = F(typeof(PlayerAvatar), "voiceChat");
        public static readonly FieldInfo? paIsDisabledField = F(typeof(PlayerAvatar), "isDisabled");
        public static readonly FieldInfo? paDeadSetField = F(typeof(PlayerAvatar), "deadSet");
        public static readonly FieldInfo? paPlayerDeathHeadField = F(
            typeof(PlayerAvatar),
            "playerDeathHead"
        );
        public static readonly FieldInfo? pdhPhysGrabObjectField = F(
            typeof(PlayerDeathHead),
            "physGrabObject"
        );

        // Items fields.
        public static readonly FieldInfo? iaInstanceNameField = F(
            typeof(ItemAttributes),
            "instanceName"
        );
        public static readonly FieldInfo? iaValueField = F(typeof(ItemAttributes), "value");
        public static readonly FieldInfo? iaShopItemField = F(typeof(ItemAttributes), "shopItem");
        public static readonly FieldInfo? iaDisableUIField = F(typeof(ItemAttributes), "disableUI");
        public static readonly FieldInfo? ieCurrentStateField = F(
            typeof(ItemEquippable),
            "currentState"
        );
        public static readonly FieldInfo? ieOwnerPlayerIdField = F(
            typeof(ItemEquippable),
            "ownerPlayerId"
        );
        public static readonly FieldInfo? ieSpotIndexField = F(
            typeof(ItemEquippable),
            "inventorySpotIndex"
        );

        // --- ItemGrenade fields ---
        public static readonly FieldInfo? igIsActiveField = AccessTools.Field(
            typeof(ItemGrenade),
            "isActive"
        );

        // --- ItemTracker fields ---
        public static readonly FieldInfo? itCurrentTargetField = AccessTools.Field(
            typeof(ItemTracker),
            "currentTarget"
        );

        // --- ItemMine fields ---
        public static readonly FieldInfo? imStateField = AccessTools.Field(
            typeof(ItemMine),
            "state"
        );

        public static readonly FieldInfo? itCurrentTargetPGOField = AccessTools.Field(
            typeof(ItemTracker),
            "currentTargetPhysGrabObject"
        );

        // --- ItemToggle fields ---
        public static readonly FieldInfo? itDisabledField = AccessTools.Field(
            typeof(ItemToggle),
            "disabled"
        );

        // --- ItemHealthPack fields ---
        public static readonly FieldInfo? ihpUsedField = AccessTools.Field(
            typeof(ItemHealthPack),
            "used"
        );

        // --- PhysGrabObject fields ---
        public static readonly FieldInfo? pgoIsMeleeField = AccessTools.Field(
            typeof(PhysGrabObject),
            "isMelee"
        );
        public static readonly FieldInfo? paPhysGrabberField = F(
            typeof(PlayerAvatar),
            "physGrabber"
        );
        public static readonly FieldInfo? pgPhotonViewField = F(typeof(PhysGrabber), "photonView");
        public static readonly FieldInfo? pgoPhotonViewField = AccessTools.Field(
            typeof(PhysGrabObject),
            "photonView"
        );

        // Enemies fields.
        public static readonly FieldInfo? enemyVisionField = F(typeof(Enemy), "Vision");
        public static readonly FieldInfo? enemyPhotonViewField = F(typeof(Enemy), "PhotonView");
        public static readonly FieldInfo? enemyTargetPlayerAvatarField = F(
            typeof(Enemy),
            "TargetPlayerAvatar"
        );
        public static readonly FieldInfo? enemyTargetPlayerViewIDField = F(
            typeof(Enemy),
            "TargetPlayerViewID"
        );
        public static readonly FieldInfo? eosOnScreenPlayerField = F(
            typeof(EnemyOnScreen),
            "OnScreenPlayer"
        );
        public static readonly FieldInfo? eosCulledPlayerField = F(
            typeof(EnemyOnScreen),
            "CulledPlayer"
        );
        public static readonly FieldInfo? enaAgentField = F(typeof(EnemyNavMeshAgent), "Agent");
        public static readonly FieldInfo? esiInvestigatePositionField = F(
            typeof(EnemyStateInvestigate),
            "onInvestigateTriggeredPosition"
        );

        public static readonly FieldInfo? enemyBeamer_playerTargetField = AccessTools.Field(
            typeof(EnemyBeamer),
            "playerTarget"
        );
        public static readonly FieldInfo? enemyCeilingEye_targetPlayerField = AccessTools.Field(
            typeof(EnemyCeilingEye),
            "targetPlayer"
        );
        public static readonly FieldInfo? enemyFloater_targetPlayerField = AccessTools.Field(
            typeof(EnemyFloater),
            "targetPlayer"
        );
        public static readonly FieldInfo? enemyRobe_targetPlayerField = AccessTools.Field(
            typeof(EnemyRobe),
            "targetPlayer"
        );
        public static readonly FieldInfo? enemyRunner_targetPlayerField = AccessTools.Field(
            typeof(EnemyRunner),
            "targetPlayer"
        );
        public static readonly FieldInfo? enemySlowWalker_targetPlayerField = AccessTools.Field(
            typeof(EnemySlowWalker),
            "targetPlayer"
        );
        public static readonly FieldInfo? enemyThinMan_playerTargetField = AccessTools.Field(
            typeof(EnemyThinMan),
            "playerTarget"
        );
        public static readonly FieldInfo? enemyTumbler_targetPlayerField = AccessTools.Field(
            typeof(EnemyTumbler),
            "targetPlayer"
        );
        public static readonly FieldInfo? enemyUpscream_targetPlayerField = AccessTools.Field(
            typeof(EnemyUpscream),
            "targetPlayer"
        );

        // --- EnemyParent Fields (Accessed via Enemy) ---
        public static readonly FieldInfo? enemy_EnemyParentField = F(typeof(Enemy), "EnemyParent");

        // --- EnemyParent Fields (Accessed via EnemyParent itself) ---
        public static readonly FieldInfo? ep_SpawnedField = F(typeof(EnemyParent), "Spawned");

        // Directors / Game State fields.
        public static readonly FieldInfo? rdExtractionPointActiveField = F(
            typeof(RoundDirector),
            "extractionPointActive"
        );
        public static readonly FieldInfo? rdExtractionPointCurrentField = F(
            typeof(RoundDirector),
            "extractionPointCurrent"
        );
        public static readonly FieldInfo? rdExtractionPointSurplusField = F(
            typeof(RoundDirector),
            "extractionPointSurplus"
        );
        public static readonly FieldInfo? vdValuableTargetAmountField = F(
            typeof(ValuableDirector),
            "valuableTargetAmount"
        );
        public static readonly FieldInfo? voDollarValueSetField = F(
            typeof(ValuableObject),
            "dollarValueSet"
        );
        public static readonly FieldInfo? epCurrentStateField = F(
            typeof(ExtractionPoint),
            "currentState"
        );
        public static readonly FieldInfo? epHaulGoalFetchedField = F(
            typeof(ExtractionPoint),
            "haulGoalFetched"
        );
        public static readonly FieldInfo? epIsShopField = F(typeof(ExtractionPoint), "isShop");
        public static readonly FieldInfo? smShoppingListField = AccessTools.Field(
            typeof(ShopManager),
            "shoppingList"
        );

        // Add these new fields for the Module class
        public static readonly FieldInfo? modSetupDoneField = F(typeof(Module), "SetupDone");
        public static readonly FieldInfo? modConnectingTopField = F(typeof(Module), "ConnectingTop");
        public static readonly FieldInfo? modConnectingBottomField = F(typeof(Module), "ConnectingBottom");
        public static readonly FieldInfo? modConnectingRightField = F(typeof(Module), "ConnectingRight");
        public static readonly FieldInfo? modConnectingLeftField = F(typeof(Module), "ConnectingLeft");
        public static readonly FieldInfo? modFirstField = F(typeof(Module), "First");

        // --- Arena Fields ---
        public static readonly FieldInfo? arenaWinnerPlayerField = F(typeof(Arena), "winnerPlayer");
        public static readonly FieldInfo? arenaPhotonViewField = F(typeof(Arena), "photonView");      // NEW
        public static readonly FieldInfo? arenaCurrentStateField = F(typeof(Arena), "currentState");  // NEW
        public static readonly FieldInfo? arenaLevelField = F(typeof(Arena), "level");                // NEW (already used in HostArenaPlatformSyncManager, ensure it's here)
        public static readonly FieldInfo? arenaCrownCageDestroyedField = F(typeof(Arena), "crownCageDestroyed"); // NEW (for consistency, if you use it elsewhere)
        public static readonly FieldInfo? arenaPlayersAliveField = F(typeof(Arena), "playersAlive"); // NEW (for consistency)

        // Cache the field info
        private static FieldInfo? _vpsSetupCompleteField;
        private static bool _vpsFieldChecked = false;

        // Misc fields.
        public static readonly FieldInfo? pghClosedField = F(typeof(PhysGrabHinge), "closed");
        public static readonly FieldInfo? pghBrokenField = F(typeof(PhysGrabHinge), "broken");
        public static readonly FieldInfo? pghJointField = F(typeof(PhysGrabHinge), "joint");
        public static readonly FieldInfo? tstCurrentPageIndexField = F(
            typeof(TruckScreenText),
            "currentPageIndex"
        );

        // Static constructor to verify critical reflection fields.
        static Utilities()
        {
            var mustExist = new (FieldInfo Field, string PrettyName)[]
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
                LogIfNull(field, name);
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
            // Check 1: Are we in the absolute main menu (before RunManager might exist)?
            if (SemiFunc.IsMainMenu())
            {
                // LATE.Core.LatePlugin.Log?.LogDebug("[Utilities.IsModLogicActive] Returning false (Reason: IsMainMenu)");
                return false;
            }

            // Check 2: Does RunManager exist? If not, probably too early.
            if (RunManager.instance == null)
            {
                // LATE.Core.LatePlugin.Log?.LogDebug("[Utilities.IsModLogicActive] Returning false (Reason: RunManager.instance is null)");
                return false;
            }

            // Check 3: Are we in the specific Lobby Menu scene?
            if (SemiFunc.RunIsLobbyMenu())
            {
                // LATE.Core.LatePlugin.Log?.LogDebug("[Utilities.IsModLogicActive] Returning false (Reason: RunIsLobbyMenu)");
                return false;
            }

            // Check 4: Are we in the Tutorial scene?
            if (SemiFunc.RunIsTutorial())
            {
                // LATE.Core.LatePlugin.Log?.LogDebug("[Utilities.IsModLogicActive] Returning false (Reason: RunIsTutorial)");
                return false;
            }

            // If none of the above conditions are met, assume we are in a valid gameplay scene
            // (Truck, Shop, Level, Arena) where the mod logic could run (config permitting).
            // LATE.Core.LatePlugin.Log?.LogDebug("[Utilities.IsModLogicActive] Returning true (Reason: Assumed valid gameplay scene)");
            return true;
        }
        #endregion

        // ─── Enemy helpers ─────────────────────────────────────────────────────────────
        /// <summary>Attempts to fetch the PhotonView attached to an Enemy instance.</summary>
        internal static bool TryGetEnemyPhotonView(Enemy enemy, out PhotonView? enemyPv)
        {
            enemyPv = null;
            if (enemy == null || enemyPhotonViewField == null)
                return false;

            try
            {
                enemyPv = enemyPhotonViewField.GetValue(enemy) as PhotonView;
                return enemyPv != null;
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"[Utilities] Failed reflecting Enemy.PhotonView on '{enemy?.gameObject?.name ?? "NULL"}': {ex}"
                );
                return false;
            }
        }

        /// <summary>Reads Enemy.TargetPlayerViewID via reflection.</summary>
        internal static bool TryGetEnemyTargetViewId(Enemy enemy, out int targetViewId)
        {
            targetViewId = -1;
            if (enemy == null || enemyTargetPlayerViewIDField == null)
                return false;

            try
            {
                if (enemyTargetPlayerViewIDField.GetValue(enemy) is int id)
                {
                    targetViewId = id;
                    return true;
                }
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"[Utilities] Failed reflecting Enemy.TargetPlayerViewID on '{enemy?.gameObject?.name ?? "NULL"}': {ex}"
                );
            }
            return false;
        }

        /// <summary>Reads Enemy.TargetPlayerViewID via reflection.</summary>
        internal static bool TryGetEnemyTargetViewIdReflected(Enemy enemy, out int targetViewId) // Renamed slightly
        {
            targetViewId = -1;
            if (enemy == null || enemyTargetPlayerViewIDField == null) // Use the cached FieldInfo
                return false;

            try
            {
                // Use GetValue on the specific enemy instance
                object? value = enemyTargetPlayerViewIDField.GetValue(enemy);
                if (value is int id)
                {
                    targetViewId = id;
                    return true;
                }
                LATE.Core.LatePlugin.Log.LogWarning(
                    $"[Utilities] Reflected Enemy.TargetPlayerViewID for '{enemy?.gameObject?.name ?? "NULL"}' was not an int (Type: {value?.GetType()})."
                );
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"[Utilities] Failed reflecting Enemy.TargetPlayerViewID on '{enemy?.gameObject?.name ?? "NULL"}': {ex}"
                );
            }
            return false;
        }

        // Helper to get internal PlayerAvatar target field via reflection
        internal static PlayerAvatar? GetInternalPlayerTarget(
            object enemyControllerInstance,
            FieldInfo? targetFieldInfo,
            string enemyTypeName
        )
        {
            if (enemyControllerInstance == null || targetFieldInfo == null)
                return null;

            try
            {
                return targetFieldInfo.GetValue(enemyControllerInstance) as PlayerAvatar;
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"[Utilities] Failed reflecting {enemyTypeName}.playerTarget: {ex}"
                );
                return null;
            }
        }

        internal static EnemyVision? GetEnemyVision(Enemy enemy)
        {
            if (enemy == null)
                return null;

            EnemyVision? vision = null;
            try
            {
                // Prioritize reflection using the cached FieldInfo
                if (enemyVisionField != null)
                {
                    vision = enemyVisionField.GetValue(enemy) as EnemyVision;
                }
                else
                {
                    // Log if the cached field itself is null (should have been caught by static constructor)
                    LATE.Core.LatePlugin.Log.LogWarning(
                        $"[Utilities] Cached enemyVisionField is null. Attempting GetComponent fallback for '{enemy.gameObject?.name ?? "NULL"}'."
                    );
                }

                // Fallback if reflection failed or field was null
                if (vision == null)
                {
                    vision = enemy.GetComponent<EnemyVision>();
                    if (vision != null)
                    {
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[Utilities] Used GetComponent fallback to get EnemyVision for '{enemy.gameObject?.name ?? "NULL"}'."
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"[Utilities] Error getting EnemyVision for '{enemy.gameObject?.name ?? "NULL"}': {ex}"
                );
                vision = null; // Ensure null on error
            }

            if (vision == null)
            {
                LATE.Core.LatePlugin.Log.LogWarning(
                    $"[Utilities] Failed to get EnemyVision component for enemy '{enemy.gameObject?.name ?? "NULL"}' via reflection or GetComponent."
                );
            }

            return vision;
        }

        #region ─── PhotonNetwork Cache Methods ─────────────────────────────────────────
        /// <summary>
        /// Clears a specific object associated with a PhotonView from the Photon room cache.
        /// This is crucial for preventing issues when players join late or objects are destroyed.
        /// </summary>
        /// <param name="photonView">The PhotonView of the object to remove from cache.</param>
        public static void ClearPhotonCache(PhotonView photonView)
        {
            if (photonView == null)
                return;

            try
            {
                var removeFilter = removeFilterFieldInfo?.GetValue(null) as Hashtable;
                var keyByteSeven = keyByteSevenFieldInfo?.GetValue(null);
                var serverCleanOptions =
                    serverCleanOptionsFieldInfo?.GetValue(null) as RaiseEventOptions;
                var raiseEventMethod = raiseEventInternalMethodInfo;

                if (
                    removeFilter == null
                    || keyByteSeven == null
                    || serverCleanOptions == null
                    || raiseEventMethod == null
                )
                {
                    LATE.Core.LatePlugin.Log.LogError(
                        "ClearPhotonCache failed: Reflection error getting PhotonNetwork internals."
                    );
                    return;
                }

                // Use InstantiationId to identify the object to be removed from cache.
                removeFilter[keyByteSeven] = photonView.InstantiationId;
                serverCleanOptions.CachingOption = EventCaching.RemoveFromRoomCache;

                raiseEventMethod.Invoke(
                    null,
                    new object[]
                    {
                        (byte)202,
                        removeFilter,
                        serverCleanOptions,
                        SendOptions.SendReliable,
                    }
                );

                LATE.Core.LatePlugin.Log.LogDebug(
                    $"Sent RemoveFromRoomCache event using InstantiationId {photonView.InstantiationId} (ViewID: {photonView.ViewID})"
                );
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"Exception during ClearPhotonCache for InstantiationId {photonView.InstantiationId} (ViewID: {photonView.ViewID}): {ex}"
                );
            }
        }
        #endregion

        /// <summary>
        /// Lightweight scene-wide cache for a specific component type.
        /// Can be used by other systems that frequently need all hinges / enemies etc.
        /// </summary>
        public static T[] GetCachedComponents<T>(
            ref T[] cache,
            ref float timeStamp,
            float refreshSeconds = 2f
        )
            where T : UnityEngine.Object
        {
            if (
                cache == null
                || cache.Length == 0
                || UnityEngine.Time.unscaledTime - timeStamp > refreshSeconds
            )
            {
#if UNITY_2022_2_OR_NEWER
				cache = UnityEngine.Object.FindObjectsByType<T>(
					UnityEngine.FindObjectsSortMode.None
				);
#else
                cache = UnityEngine.Object.FindObjectsOfType<T>();
#endif
                timeStamp = UnityEngine.Time.unscaledTime;
            }
            return cache;
        }

        #region ─── Coroutine Runner Methods ───────────────────────────────────────────
        /// <summary>
        /// Finds a suitable MonoBehaviour instance to run coroutines on.
        /// Prefers RunManager, falls back to GameDirector.
        /// </summary>
        public static MonoBehaviour? FindCoroutineRunner()
        {
            LATE.Core.LatePlugin.Log.LogDebug("Finding coroutine runner…");

#if UNITY_2022_2_OR_NEWER
			if (UnityEngine.Object.FindFirstObjectByType<RunManager>() is { } runMgr)
				return runMgr;
#else
            if (UnityEngine.Object.FindObjectOfType<RunManager>() is { } runMgr)
                return runMgr;
#endif

            if (GameDirector.instance is { } gDir)
                return gDir;

            LATE.Core.LatePlugin.Log.LogError(
                "Failed to find suitable MonoBehaviour (RunManager or GameDirector) for coroutines!"
            );
            return null;
        }
        #endregion

        /// <summary>
        /// Gets the FieldInfo for the internal ValuablePropSwitch.SetupComplete field.
        /// Uses reflection and caches the result.
        /// </summary>
        internal static FieldInfo? GetVpsSetupCompleteField()
        {
            if (!_vpsFieldChecked)
            {
                _vpsSetupCompleteField = AccessTools.Field(
                    typeof(ValuablePropSwitch),
                    "SetupComplete"
                );
                if (_vpsSetupCompleteField == null)
                {
                    LATE.Core.LatePlugin.Log?.LogError(
                        "[Utilities] Failed to find internal field 'ValuablePropSwitch.SetupComplete' via reflection."
                    );
                }
                _vpsFieldChecked = true;
            }
            return _vpsSetupCompleteField;
        }

        #region ─── Player Avatar Methods ──────────────────────────────────────────────
        /// <summary>
        /// Finds the PlayerAvatar associated with a given Photon Player.
        /// </summary>
        /// <param name="player">The Photon Player.</param>
        /// <returns>The associated PlayerAvatar, or null if not found.</returns>
        public static PlayerAvatar? FindPlayerAvatar(Player player)
        {
            if (player == null)
                return null;

            // 1. Check GameDirector's list first (faster lookup).
            if (GameDirector.instance?.PlayerList != null)
            {
                foreach (var avatar in GameDirector.instance.PlayerList)
                {
                    if (avatar == null)
                        continue;
                    var pv = GetPhotonView(avatar);
                    if (pv != null && pv.OwnerActorNr == player.ActorNumber)
                    {
                        return avatar;
                    }
                }
            }

            // 2. If not found in GameDirector list, search all PlayerAvatars in the scene.
            LATE.Core.LatePlugin.Log.LogDebug(
                $"Player {player.NickName} not in GameDirector list, searching scene..."
            );
            foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
            {
                if (avatar == null)
                    continue;
                var pv = GetPhotonView(avatar);
                if (pv != null && pv.OwnerActorNr == player.ActorNumber)
                {
                    return avatar;
                }
            }

            LATE.Core.LatePlugin.Log.LogWarning(
                $"[MOD Resync] Could not find PlayerAvatar for {player.NickName} (ActorNr: {player.ActorNumber})."
            );
            return null;
        }

        /// <summary>
        /// Finds the PlayerAvatar belonging to the local player.
        /// </summary>
        /// <returns>The local PlayerAvatar, or null if not found.</returns>
        public static PlayerAvatar? FindLocalPlayerAvatar()
        {
            // 1. Try getting it via PlayerController (often reliable).
            if (
                PlayerController.instance?.playerAvatar?.GetComponent<PlayerAvatar>()
                    is PlayerAvatar localAvatar
                && GetPhotonView(localAvatar)?.IsMine == true
            )
            {
                return localAvatar;
            }

            // 2. Fallback: Search all PlayerAvatars in the scene.
            foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
            {
                if (avatar == null)
                    continue;
                if (GetPhotonView(avatar)?.IsMine == true)
                {
                    return avatar;
                }
            }

            return null;
        }
        #endregion

        #region ─── PhotonView Helper Method ───────────────────────────────────────────
        /// <summary>
        /// Helper to get the PhotonView from a component, checking common locations.
        /// </summary>
        /// <param name="component">The component to check.</param>
        /// <returns>The PhotonView, or null if not found.</returns>
        public static PhotonView? GetPhotonView(Component component)
        {
            if (component == null)
                return null;

            // If the component is a PhotonView, return it directly.
            if (component is PhotonView photonView)
                return photonView;

            // Try getting a PhotonView attached to the same GameObject.
            var view = component.GetComponent<PhotonView>();
            if (view != null)
                return view;

            // Specific fallback for PlayerAvatar using a reflected field.
            if (component is PlayerAvatar && paPhotonViewField != null)
            {
                try
                {
                    return paPhotonViewField.GetValue(component) as PhotonView;
                }
                catch (Exception ex)
                {
                    // Reflection failure; optionally log or handle the error.
                    LATE.Core.LatePlugin.Log?.LogWarning("Failed to get PhotonView via reflection: " + ex);
                }
            }

            return null;
        }
        #endregion

        #region ─── Player Nickname Helper ─────────────────────────────────────────────
        /// <summary>
        /// Safely gets a display name for a player avatar, prioritizing Photon NickName.
        /// </summary>
        /// <param name="avatar">The player avatar.</param>
        /// <returns>A display name for the player.</returns>
        public static string GetPlayerNickname(PlayerAvatar avatar)
        {
            if (avatar == null)
                return "<NullAvatar>";

            var pv = GetPhotonView(avatar);

            // 1. Try PhotonView Owner NickName (most reliable).
            if (pv?.Owner?.NickName != null)
            {
                return pv.Owner.NickName;
            }

            // 2. Fallback to reflected internal playerName field.
            if (paPlayerNameField != null)
            {
                try
                {
                    object? nameObj = paPlayerNameField.GetValue(avatar);
                    if (nameObj is string nameStr && !string.IsNullOrEmpty(nameStr))
                    {
                        return nameStr + " (Reflected)";
                    }
                }
                catch (Exception ex)
                {
                    LATE.Core.LatePlugin.Log?.LogWarning(
                        $"Failed to reflect playerName for avatar: {ex.Message}"
                    );
                }
            }

            // 3. Fallback to using the ActorNumber.
            if (pv?.OwnerActorNr > 0)
            {
                return $"ActorNr {pv.OwnerActorNr}";
            }

            LATE.Core.LatePlugin.Log?.LogWarning(
                $"Could not determine nickname for avatar (ViewID: {pv?.ViewID ?? 0}), returning fallback."
            );
            return "<UnknownPlayer>";
        }
        #endregion

        /// <summary>
        /// Safely retrieves the PhotonView ID of a player's PhysGrabber.
        /// </summary>
        /// <param name="playerAvatar">The PlayerAvatar instance.</param>
        /// <returns>The ViewID, or -1 if not found or an error occurred.</returns>
        internal static int GetPhysGrabberViewId(PlayerAvatar playerAvatar)
        {
            if (playerAvatar == null || paPhysGrabberField == null || pgPhotonViewField == null)
                return -1;

            try
            {
                object physGrabberObj = paPhysGrabberField.GetValue(playerAvatar);
                if (physGrabberObj is PhysGrabber physGrabber)
                {
                    PhotonView? pv = pgPhotonViewField.GetValue(physGrabber) as PhotonView;
                    if (pv != null)
                    {
                        return pv.ViewID;
                    }
                }
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log?.LogError($"[GetPhysGrabberViewId] Reflection error: {ex}");
            }
            return -1;
        }

        /// <summary>
        /// Convenience wrapper →  returns a component's PhotonView.ViewID or –1 on failure.
        /// </summary>
        public static int GetViewId(Component comp)
        {
            return GetPhotonView(comp)?.ViewID ?? -1;
        }

        /// <summary>
        /// Helper to get the internal PhotonView from a PhysGrabObject using reflection.
        /// </summary>
        /// <param name="pgo">The PhysGrabObject instance.</param>
        /// <returns>The PhotonView, or null if not found or reflection failed.</returns>
        public static PhotonView? GetPhotonViewFromPGO(PhysGrabObject? pgo)
        {
            if (pgo == null || pgoPhotonViewField == null)
                return null;

            try
            {
                // Get the value using the cached FieldInfo
                return pgoPhotonViewField.GetValue(pgo) as PhotonView;
            }
            catch (Exception ex)
            {
                LATE.Core.LatePlugin.Log?.LogError(
                    $"[GetPhotonViewFromPGO] Reflection error getting PhotonView from PhysGrabObject '{pgo.gameObject?.name ?? "NULL"}': {ex}"
                );
                return null;
            }
        }
    }
}