using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LATE
{
    /// <summary>Handles enemy <-> player bookkeeping for late-join & leave events.</summary>
    internal static class EnemyManager
    {
        #region ─── Common helpers ──────────────────────────────────────────────
        private static bool IsMaster() => Utilities.IsRealMasterClient();

        // Note: This ForEachEnemy iterates only ACTIVE enemies by default.
        // The SyncAllEnemyStatesForPlayer method uses FindObjectsOfType(true) to include inactive ones.
        private static void ForEachEnemy(Action<Enemy, PhotonView?> action)
        {
            Enemy[] enemies = Object.FindObjectsOfType<Enemy>(); // includeInactive = false (default)
            LATE.Core.LatePlugin.Log.LogDebug(
                $"[EnemyManager] ForEachEnemy found {enemies.Length} active enemies."
            ); // Adjusted log for clarity

            foreach (Enemy enemy in enemies)
            {
                if (enemy == null || enemy.gameObject == null)
                    continue; // Added null check for safety

                // Use the cached PhotonView from Enemy script if available
                PhotonView? enemyPv = null;
                try
                {
                    // Access internal photonView field via reflection helper
                    if (Utilities.enemyPhotonViewField != null)
                    {
                        enemyPv = Utilities.enemyPhotonViewField.GetValue(enemy) as PhotonView;
                    }
                }
                catch (Exception ex)
                {
                    LATE.Core.LatePlugin.Log.LogWarning(
                        $"[EnemyManager] Error reflecting Enemy.photonView for '{enemy.gameObject.name}': {ex.Message}"
                    );
                }

                if (enemyPv == null)
                {
                    // Attempt direct GetComponent as a final fallback
                    enemyPv = enemy.GetComponent<PhotonView>();
                    if (enemyPv == null)
                    {
                        LATE.Core.LatePlugin.Log.LogWarning(
                            $"[EnemyManager] Could not get PhotonView for active enemy '{enemy.gameObject.name}' via reflection or GetComponent. Skipping action."
                        );
                        continue;
                    }
                }

                try
                {
                    action(enemy, enemyPv);
                }
                catch (Exception ex)
                {
                    LATE.Core.LatePlugin.Log.LogError(
                        $"[EnemyManager] Enemy action failed on '{enemy.gameObject?.name ?? "NULL"}' (ViewID: {enemyPv?.ViewID ?? 0}): {ex}"
                    );
                }
            }
        }
        #endregion

        #region ─── Notify Enemies of New Player ────────────────────────────────
        /// <summary>
        /// Notifies every existing ACTIVE Enemy that a new player has spawned so
        /// components such as EnemyOnScreen are refreshed.
        /// This is called *after* SyncAllEnemyStatesForPlayer ensures the enemy exists/is active on the client.
        /// </summary>
        public static void NotifyEnemiesOfNewPlayer(Player newPlayer, PlayerAvatar newPlayerAvatar)
        {
            // ── Sanity / early-outs ─────────────────────────────────────────
            if (newPlayer == null)
            {
                LATE.Core.LatePlugin.Log.LogWarning(
                    "[EnemyManager] NotifyEnemiesOfNewPlayer called with null player."
                );
                return;
            }
            if (!IsMaster())
            {
                LATE.Core.LatePlugin.Log.LogDebug(
                    $"[EnemyManager] Not MasterClient, skipping new-player notification for {newPlayer?.NickName ?? "<null>"}."
                );
                return;
            }
            if (newPlayerAvatar == null)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"[EnemyManager] newPlayerAvatar is null for {newPlayer.NickName}. Aborting."
                );
                return;
            }

            // ── Resolve the avatar PhotonView ──────────────────────────────
            PhotonView? avatarPv = Utilities.GetPhotonView(newPlayerAvatar); // Use helper
            if (avatarPv == null)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    $"[EnemyManager] Could not get PhotonView for {newPlayer.NickName}'s Avatar."
                );
                return;
            }
            int avatarViewId = avatarPv.ViewID;

            LATE.Core.LatePlugin.Log.LogInfo(
                $"[EnemyManager] Notifying ACTIVE enemies about new player {newPlayer.NickName} (ViewID {avatarViewId})."
            );

            // ── Actual enemy iteration (only needs active enemies) ──────────────────
            int updated = 0;
            ForEachEnemy(
                (enemy, enemyPv) => // Uses the helper iterating ACTIVE enemies
                {
                    try
                    {
                        enemy.PlayerAdded(avatarViewId); // Call the public method
                        updated++;
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[EnemyManager] PlayerAdded({avatarViewId}) called on ACTIVE enemy '{enemy.gameObject.name}' (Enemy ViewID: {enemyPv?.ViewID ?? 0})."
                        );
                    }
                    catch (Exception ex)
                    {
                        LATE.Core.LatePlugin.Log.LogError(
                            $"[EnemyManager] Error calling PlayerAdded on '{enemy.gameObject.name}': {ex.Message}"
                        );
                    }
                }
            );

            LATE.Core.LatePlugin.Log.LogInfo(
                $"[EnemyManager] Finished notifying {updated} active enemies about {newPlayer.NickName}."
            );
        }
        #endregion

        #region ─── Notify Enemies of Leaving Player ────────────────────────────
        /// <summary>
        /// Called when a player leaves → informs every ACTIVE Enemy.
        /// </summary>
        public static void NotifyEnemiesOfLeavingPlayer(Player leavingPlayer)
        {
            // ── Sanity / early-outs ─────────────────────────────────────────
            if (leavingPlayer == null)
            {
                LATE.Core.LatePlugin.Log.LogWarning(
                    "[EnemyManager] NotifyEnemiesOfLeavingPlayer called with null player."
                );
                return;
            }
            if (!IsMaster())
            {
                LATE.Core.LatePlugin.Log.LogDebug(
                    $"[EnemyManager] Not MasterClient, skipping leaving-notification for {leavingPlayer?.NickName ?? "<null>"}."
                );
                return;
            }

            // ── Resolve the leaving player's Avatar & ViewID ───────────────
            PlayerAvatar? avatar = Utilities.FindPlayerAvatar(leavingPlayer); // Use helper
            PhotonView? avatarPv = null;
            if (avatar != null)
            {
                avatarPv = Utilities.GetPhotonView(avatar);
            }

            if (avatarPv == null)
            {
                // This is less critical than joining, as the player is gone. Might happen if they leave before avatar fully despawns.
                LATE.Core.LatePlugin.Log.LogWarning(
                    $"[EnemyManager] Could not resolve Avatar ViewID for leaving player {leavingPlayer.NickName}. Skipping enemy notification (player likely gone)."
                );
                return;
            }
            int avatarViewId = avatarPv.ViewID;

            LATE.Core.LatePlugin.Log.LogInfo(
                $"[EnemyManager] Notifying ACTIVE enemies that {leavingPlayer.NickName} (ViewID {avatarViewId}) left."
            );

            // ── Actual enemy iteration (only needs active enemies) ───────────────────
            int updated = 0;
            ForEachEnemy(
                (enemy, enemyPv) => // Uses the helper iterating ACTIVE enemies
                {
                    // Use reflection helper to check target ID
                    Utilities.TryGetEnemyTargetViewIdReflected(enemy, out var currentTargetId);
                    bool wasTarget = currentTargetId == avatarViewId;

                    try
                    {
                        enemy.PlayerRemoved(avatarViewId); // Call public method
                        updated++;
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[EnemyManager] PlayerRemoved({avatarViewId}) called on ACTIVE enemy '{enemy.gameObject.name}' (Enemy ViewID: {enemyPv?.ViewID ?? 0}). WasTarget: {wasTarget}"
                        );
                    }
                    catch (Exception ex)
                    {
                        LATE.Core.LatePlugin.Log.LogError(
                            $"[EnemyManager] Error calling PlayerRemoved on '{enemy.gameObject.name}': {ex.Message}"
                        );
                    }
                }
            );

            LATE.Core.LatePlugin.Log.LogInfo(
                $"[EnemyManager] Finished notifying {updated} active enemies about {leavingPlayer.NickName} leaving."
            );
        }
        #endregion

        #region ─── Sync Enemy State for Late Joiner (REVISED + REFLECTION) ─────
        /// <summary>
        /// Synchronizes the Spawned/Despawned state of all enemies for a specific late-joining player
        /// using the EnemyParent's SpawnRPC/DespawnRPC. Accesses internal fields via reflection.
        /// For enemies synced as 'Spawned', it then syncs their detailed state (targets, specific behaviours, etc.).
        /// </summary>
        public static void SyncAllEnemyStatesForPlayer(Player targetPlayer)
        {
            if (!IsMaster())
            {
                LATE.Core.LatePlugin.Log.LogDebug(
                    "[EnemyManager] Not MasterClient, skipping SyncAllEnemyStatesForPlayer."
                );
                return;
            }
            if (targetPlayer == null)
            {
                LATE.Core.LatePlugin.Log.LogWarning(
                    "[EnemyManager] SyncAllEnemyStatesForPlayer called with null targetPlayer."
                );
                return;
            }

            // --- Reflection Check ---
            if (Utilities.enemy_EnemyParentField == null || Utilities.ep_SpawnedField == null)
            {
                LATE.Core.LatePlugin.Log.LogError(
                    "[EnemyManager] CRITICAL REFLECTION FAILURE: Cannot find required internal fields (Enemy.EnemyParent, EnemyParent.Spawned, EnemyParent.photonView). Aborting enemy sync."
                );
                return;
            }

            string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            LATE.Core.LatePlugin.Log.LogInfo(
                $"[EnemyManager] === Starting FULL enemy state sync for {nick} using EnemyParent RPCs (with reflection) ==="
            );

            int processedCount = 0;
            int spawnRpcSentCount = 0;
            int despawnRpcSentCount = 0;
            int specificStateSyncedCount = 0;
            int freezeSyncedCount = 0;
            int otherStateSyncedCount = 0;

            Enemy[] allEnemies = Object.FindObjectsOfType<Enemy>(true);
            LATE.Core.LatePlugin.Log.LogDebug(
                $"[EnemyManager] Found {allEnemies.Length} total Enemy components (including inactive) for state sync."
            );

            foreach (Enemy enemy in allEnemies)
            {
                processedCount++;
                if (enemy == null || enemy.gameObject == null)
                {
                    LATE.Core.LatePlugin.Log.LogWarning(
                        $"[EnemyManager] Encountered null enemy instance at index {processedCount - 1}. Skipping."
                    );
                    continue;
                }

                string enemyName = enemy.gameObject.name;

                // --- Get EnemyParent and its PhotonView using REFLECTION ---
                EnemyParent? enemyParent = null;
                PhotonView? parentPv = null; // Initialize to null

                try
                {
                    // Get EnemyParent instance from Enemy using reflection
                    enemyParent = Utilities.enemy_EnemyParentField.GetValue(enemy) as EnemyParent;
                    if (enemyParent == null)
                    {
                        enemyParent = enemy.GetComponentInParent<EnemyParent>(); // Fallback
                        if (enemyParent == null)
                        {
                            LATE.Core.LatePlugin.Log.LogWarning(
                                $"[EnemyManager] Enemy '{enemyName}' has no EnemyParent component (checked reflection and hierarchy). Skipping sync."
                            );
                            continue;
                        }
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[EnemyManager] Used GetComponentInParent fallback for EnemyParent on '{enemyName}'."
                        );
                    }

                    // Get PhotonView directly from EnemyParent using GetComponent (SAFER)
                    parentPv = enemyParent.GetComponent<PhotonView>();
                    if (parentPv == null)
                    {
                        LATE.Core.LatePlugin.Log.LogWarning(
                            $"[EnemyManager] Could not get PhotonView component for EnemyParent of '{enemyName}'. Skipping sync."
                        );
                        continue; // We absolutely need this PV for RPCs
                    }
                }
                catch (Exception ex)
                {
                    // This catch now primarily covers the reflection for enemy_EnemyParentField
                    LATE.Core.LatePlugin.Log.LogError(
                        $"[EnemyManager] Reflection error getting EnemyParent for '{enemyName}': {ex}. Skipping sync."
                    );
                    continue;
                }

                // Also need the Enemy's own PV for specific state RPCs
                // (Keep the existing logic for getting enemyPv using reflection/GetComponent)
                PhotonView? enemyPv = null;
                try
                {
                    if (Utilities.enemyPhotonViewField != null)
                    {
                        enemyPv = Utilities.enemyPhotonViewField.GetValue(enemy) as PhotonView;
                    }
                }
                catch (Exception pvEx)
                {
                    LATE.Core.LatePlugin.Log.LogWarning(
                        $"[EnemyManager] Error reflecting Enemy.photonView for '{enemyName}': {pvEx.Message}"
                    );
                }

                if (enemyPv == null)
                {
                    enemyPv = enemy.GetComponent<PhotonView>(); // Final fallback
                    if (enemyPv == null)
                    {
                        LATE.Core.LatePlugin.Log.LogWarning(
                            $"[EnemyManager] Could not get Enemy's own PhotonView for '{enemyName}'. Skipping detailed state sync."
                        );
                    }
                    else
                    {
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[EnemyManager] Used GetComponent fallback for Enemy PhotonView on '{enemyName}'."
                        );
                    }
                }

                try // Wrap individual enemy processing
                {
                    // --- 1. Determine Host State using REFLECTION for EnemyParent.Spawned ---
                    bool hostIsSpawned = false;
                    try
                    {
                        object spawnedValue = Utilities.ep_SpawnedField.GetValue(enemyParent);
                        if (spawnedValue is bool)
                        {
                            hostIsSpawned = (bool)spawnedValue;
                        }
                        else
                        {
                            LATE.Core.LatePlugin.Log.LogWarning(
                                $"[EnemyManager] Reflected EnemyParent.Spawned for '{enemyName}' was not a bool (Type: {spawnedValue?.GetType()}). Assuming false."
                            );
                        }
                    }
                    catch (Exception spawnEx)
                    {
                        LATE.Core.LatePlugin.Log.LogError(
                            $"[EnemyManager] Reflection error getting EnemyParent.Spawned for '{enemyName}': {spawnEx}. Assuming false."
                        );
                        hostIsSpawned = false; // Default to despawned on error
                    }

                    // --- 2. Send Explicit Spawn/Despawn RPC via EnemyParent PV ---
                    if (hostIsSpawned)
                    {
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[EnemyManager] Enemy '{enemyName}' is SPAWNED on host. Sending SpawnRPC to {nick}. (Parent PV: {parentPv.ViewID})"
                        );
                        parentPv.RPC("SpawnRPC", targetPlayer);
                        spawnRpcSentCount++;

                        // --- 3. Sync Detailed State ONLY for Spawned Enemies ---
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[EnemyManager] Syncing detailed state for spawned enemy '{enemyName}' (Enemy PV: {enemyPv?.ViewID ?? 0})..."
                        );

                        if (enemyPv == null)
                        {
                            LATE.Core.LatePlugin.Log.LogWarning(
                                $"[EnemyManager]   Skipping detailed sync for '{enemyName}' because its own PhotonView is missing."
                            );
                            continue;
                        }

                        // --- Base Enemy State Sync ---
                        if (
                            Utilities.TryGetEnemyTargetViewIdReflected(
                                enemy,
                                out int hostTargetViewId
                            )
                            && hostTargetViewId > 0
                        )
                        {
                            LATE.Core.LatePlugin.Log.LogDebug(
                                $"[EnemyManager]   Enemy '{enemyName}' TargetPlayerViewID {hostTargetViewId} (Should be synced by base Enemy serialization)."
                            );
                        }

                        // --- Specific Enemy Type State Sync ---
                        bool specificStateSynced = false; // Reset per enemy

                        // EnemyAnimal
                        EnemyAnimal animalController = enemy.GetComponent<EnemyAnimal>();
                        if (animalController != null)
                        {
                            try
                            {
                                EnemyAnimal.State hostState = animalController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyAnimal state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyAnimal state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyBang
                        EnemyBang bangController = enemy.GetComponent<EnemyBang>();
                        if (bangController != null)
                        {
                            try
                            {
                                EnemyBang.State hostState = bangController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                FieldInfo? fuseActiveField = AccessTools.Field(
                                    typeof(EnemyBang),
                                    "fuseActive"
                                );
                                FieldInfo? fuseLerpField = AccessTools.Field(
                                    typeof(EnemyBang),
                                    "fuseLerp"
                                );
                                if (fuseActiveField != null && fuseLerpField != null)
                                {
                                    bool hostFuseActive = (bool)
                                        fuseActiveField.GetValue(bangController);
                                    float hostFuseLerp = (float)
                                        fuseLerpField.GetValue(bangController);
                                    enemyPv.RPC(
                                        "FuseRPC",
                                        targetPlayer,
                                        hostFuseActive,
                                        hostFuseLerp
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyBang fuse state: Active={hostFuseActive}, Lerp={hostFuseLerp:F2}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyBang state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyBang state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyBeamer
                        EnemyBeamer beamerController = enemy.GetComponent<EnemyBeamer>();
                        if (beamerController != null)
                        {
                            try
                            {
                                EnemyBeamer.State hostState = beamerController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                FieldInfo? moveFastField = AccessTools.Field(
                                    typeof(EnemyBeamer),
                                    "moveFast"
                                );
                                if (moveFastField != null)
                                {
                                    bool hostMoveFast = (bool)
                                        moveFastField.GetValue(beamerController);
                                    enemyPv.RPC("MoveFastRPC", targetPlayer, hostMoveFast);
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyBeamer moveFast state: {hostMoveFast}"
                                    );
                                    otherStateSyncedCount++;
                                }

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    beamerController,
                                    Utilities.enemyBeamer_playerTargetField,
                                    "EnemyBeamer"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "UpdatePlayerTargetRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyBeamer target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }

                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyBeamer state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyBeamer state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyBowtie
                        EnemyBowtie bowtieController = enemy.GetComponent<EnemyBowtie>();
                        if (bowtieController != null)
                        {
                            try
                            {
                                EnemyBowtie.State hostState = bowtieController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    bowtieController,
                                    AccessTools.Field(typeof(EnemyBowtie), "playerTarget"),
                                    "EnemyBowtie"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "NoticeRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyBowtie notice for target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyBowtie state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyBowtie state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyCeilingEye
                        EnemyCeilingEye ceilingEyeController =
                            enemy.GetComponent<EnemyCeilingEye>();
                        if (ceilingEyeController != null)
                        {
                            try
                            {
                                EnemyCeilingEye.State hostState = ceilingEyeController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    ceilingEyeController,
                                    Utilities.enemyCeilingEye_targetPlayerField,
                                    "EnemyCeilingEye"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "TargetPlayerRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyCeilingEye target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyCeilingEye state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyCeilingEye state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyDuck
                        EnemyDuck duckController = enemy.GetComponent<EnemyDuck>();
                        if (duckController != null)
                        {
                            try
                            {
                                EnemyDuck.State hostState = duckController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    duckController,
                                    AccessTools.Field(typeof(EnemyDuck), "playerTarget"),
                                    "EnemyDuck"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "UpdatePlayerTargetRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyDuck target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyDuck state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyDuck state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyFloater
                        EnemyFloater floaterController = enemy.GetComponent<EnemyFloater>();
                        if (floaterController != null)
                        {
                            try
                            {
                                EnemyFloater.State hostState = floaterController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    floaterController,
                                    Utilities.enemyFloater_targetPlayerField,
                                    "EnemyFloater"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "TargetPlayerRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyFloater target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;

                                    if (
                                        hostState != EnemyFloater.State.Attack
                                        && hostState != EnemyFloater.State.ChargeAttack
                                        && hostState != EnemyFloater.State.DelayAttack
                                    )
                                    {
                                        enemyPv.RPC(
                                            "NoticeRPC",
                                            targetPlayer,
                                            hostTarget.photonView.ViewID
                                        );
                                        LATE.Core.LatePlugin.Log.LogDebug(
                                            $"[EnemyManager]   Synced EnemyFloater notice RPC for target: {hostTarget.name}"
                                        );
                                        otherStateSyncedCount++;
                                    }
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyFloater state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyFloater state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyGnome
                        EnemyGnome gnomeController = enemy.GetComponent<EnemyGnome>();
                        if (gnomeController != null)
                        {
                            try
                            {
                                EnemyGnome.State hostState = gnomeController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyGnome state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyGnome state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyHidden
                        EnemyHidden hiddenController = enemy.GetComponent<EnemyHidden>();
                        if (hiddenController != null)
                        {
                            try
                            {
                                EnemyHidden.State hostState = hiddenController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    hiddenController,
                                    AccessTools.Field(typeof(EnemyHidden), "playerTarget"),
                                    "EnemyHidden"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "UpdatePlayerTargetRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyHidden target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyHidden state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyHidden state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyHunter
                        EnemyHunter hunterController = enemy.GetComponent<EnemyHunter>();
                        if (hunterController != null)
                        {
                            try
                            {
                                EnemyHunter.State hostState = hunterController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                FieldInfo? investigatePointField = AccessTools.Field(
                                    typeof(EnemyHunter),
                                    "investigatePoint"
                                );
                                if (
                                    investigatePointField != null
                                    && (
                                        hostState == EnemyHunter.State.Investigate
                                        || hostState == EnemyHunter.State.Aim
                                    )
                                )
                                {
                                    Vector3 hostInvestigatePoint = (Vector3)
                                        investigatePointField.GetValue(hunterController);
                                    enemyPv.RPC(
                                        "UpdateInvestigationPoint",
                                        targetPlayer,
                                        hostInvestigatePoint
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyHunter investigate point: {hostInvestigatePoint}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                FieldInfo? moveFastField = AccessTools.Field(
                                    typeof(EnemyHunter),
                                    "moveFast"
                                );
                                if (moveFastField != null)
                                {
                                    bool hostMoveFast = (bool)
                                        moveFastField.GetValue(hunterController);
                                    enemyPv.RPC("MoveFastRPC", targetPlayer, hostMoveFast);
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyHunter moveFast: {hostMoveFast}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyHunter state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyHunter state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyRobe
                        EnemyRobe robeController = enemy.GetComponent<EnemyRobe>();
                        if (robeController != null)
                        {
                            try
                            {
                                EnemyRobe.State hostState = robeController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    robeController,
                                    Utilities.enemyRobe_targetPlayerField,
                                    "EnemyRobe"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "TargetPlayerRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyRobe target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }

                                FieldInfo? isOnScreenField = AccessTools.Field(
                                    typeof(EnemyRobe),
                                    "isOnScreen"
                                );
                                if (isOnScreenField != null)
                                {
                                    bool hostIsOnScreen = (bool)
                                        isOnScreenField.GetValue(robeController);
                                    enemyPv.RPC("UpdateOnScreenRPC", targetPlayer, hostIsOnScreen);
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyRobe isOnScreen: {hostIsOnScreen}"
                                    );
                                    otherStateSyncedCount++;
                                }

                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyRobe state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyRobe state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyRunner
                        EnemyRunner runnerController = enemy.GetComponent<EnemyRunner>();
                        if (runnerController != null)
                        {
                            try
                            {
                                EnemyRunner.State hostState = runnerController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    runnerController,
                                    Utilities.enemyRunner_targetPlayerField,
                                    "EnemyRunner"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "UpdatePlayerTargetRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyRunner target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyRunner state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyRunner state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemySlowMouth
                        EnemySlowMouth mouthController = enemy.GetComponent<EnemySlowMouth>();
                        if (mouthController != null)
                        {
                            try
                            {
                                EnemySlowMouth.State hostState = mouthController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    mouthController,
                                    AccessTools.Field(typeof(EnemySlowMouth), "playerTarget"),
                                    "EnemySlowMouth"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "UpdatePlayerTargetRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemySlowMouth target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemySlowMouth state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemySlowMouth state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemySlowWalker
                        EnemySlowWalker walkerController = enemy.GetComponent<EnemySlowWalker>();
                        if (walkerController != null)
                        {
                            try
                            {
                                EnemySlowWalker.State hostState = walkerController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    walkerController,
                                    Utilities.enemySlowWalker_targetPlayerField,
                                    "EnemySlowWalker"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "TargetPlayerRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemySlowWalker target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;

                                    if (hostState == EnemySlowWalker.State.Notice)
                                    {
                                        enemyPv.RPC(
                                            "NoticeRPC",
                                            targetPlayer,
                                            hostTarget.photonView.ViewID
                                        );
                                        LATE.Core.LatePlugin.Log.LogDebug(
                                            $"[EnemyManager]   Synced EnemySlowWalker notice RPC."
                                        );
                                        otherStateSyncedCount++;
                                    }
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemySlowWalker state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemySlowWalker state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyThinMan
                        EnemyThinMan thinManController = enemy.GetComponent<EnemyThinMan>();
                        if (thinManController != null)
                        {
                            try
                            {
                                EnemyThinMan.State hostState = thinManController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    thinManController,
                                    Utilities.enemyThinMan_playerTargetField,
                                    "EnemyThinMan"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "SetTargetRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID,
                                        true
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyThinMan target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyThinMan state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyThinMan state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyTumbler
                        EnemyTumbler tumblerController = enemy.GetComponent<EnemyTumbler>();
                        if (tumblerController != null)
                        {
                            try
                            {
                                EnemyTumbler.State hostState = tumblerController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    tumblerController,
                                    Utilities.enemyTumbler_targetPlayerField,
                                    "EnemyTumbler"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "TargetPlayerRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyTumbler target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyTumbler state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyTumbler state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyUpscream
                        EnemyUpscream upscreamController = enemy.GetComponent<EnemyUpscream>();
                        if (upscreamController != null)
                        {
                            try
                            {
                                EnemyUpscream.State hostState = upscreamController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    upscreamController,
                                    Utilities.enemyUpscream_targetPlayerField,
                                    "EnemyUpscream"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "TargetPlayerRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyUpscream target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;

                                    if (
                                        hostState == EnemyUpscream.State.PlayerNotice
                                        || hostState == EnemyUpscream.State.GoToPlayer
                                        || hostState == EnemyUpscream.State.Attack
                                    )
                                    {
                                        enemyPv.RPC(
                                            "NoticeSetRPC",
                                            targetPlayer,
                                            hostTarget.photonView.ViewID
                                        );
                                        LATE.Core.LatePlugin.Log.LogDebug(
                                            $"[EnemyManager]   Synced EnemyUpscream notice RPC."
                                        );
                                        otherStateSyncedCount++;
                                    }
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyUpscream state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyUpscream state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // EnemyValuableThrower
                        EnemyValuableThrower throwerController =
                            enemy.GetComponent<EnemyValuableThrower>();
                        if (throwerController != null)
                        {
                            try
                            {
                                EnemyValuableThrower.State hostState =
                                    throwerController.currentState;
                                enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);

                                PlayerAvatar? hostTarget = Utilities.GetInternalPlayerTarget(
                                    throwerController,
                                    AccessTools.Field(typeof(EnemyValuableThrower), "playerTarget"),
                                    "EnemyValuableThrower"
                                );
                                if (hostTarget != null && hostTarget.photonView != null)
                                {
                                    enemyPv.RPC(
                                        "UpdatePlayerTargetRPC",
                                        targetPlayer,
                                        hostTarget.photonView.ViewID
                                    );
                                    LATE.Core.LatePlugin.Log.LogDebug(
                                        $"[EnemyManager]   Synced EnemyValuableThrower target: {hostTarget.name}"
                                    );
                                    otherStateSyncedCount++;

                                    if (
                                        hostState == EnemyValuableThrower.State.PlayerNotice
                                        || hostState == EnemyValuableThrower.State.GetValuable
                                        || hostState == EnemyValuableThrower.State.GoToTarget
                                    )
                                    {
                                        enemyPv.RPC(
                                            "NoticeRPC",
                                            targetPlayer,
                                            hostTarget.photonView.ViewID
                                        );
                                        LATE.Core.LatePlugin.Log.LogDebug(
                                            $"[EnemyManager]   Synced EnemyValuableThrower notice RPC."
                                        );
                                        otherStateSyncedCount++;
                                    }
                                }
                                specificStateSyncedCount++;
                                specificStateSynced = true;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced EnemyValuableThrower state '{hostState}'."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error syncing EnemyValuableThrower state for {enemyName}: {ex.Message}"
                                );
                            }
                        }

                        // --- End of Specific Enemy Type Sync ---

                        if (!specificStateSynced)
                        {
                            LATE.Core.LatePlugin.Log.LogDebug(
                                $"[EnemyManager]   No specific enemy type found or synced for '{enemyName}'. Base state from EnemyParent: Spawned."
                            );
                        }

                        // --- Other Base Enemy States (Only if Spawned) ---
                        if (enemy.FreezeTimer > 0f)
                        {
                            try
                            {
                                // FreezeRPC is on Enemy, so use its PV
                                enemyPv.RPC("FreezeRPC", targetPlayer, enemy.FreezeTimer);
                                freezeSyncedCount++;
                                LATE.Core.LatePlugin.Log.LogDebug(
                                    $"[EnemyManager]   Synced FreezeTimer ({enemy.FreezeTimer:F1}s)."
                                );
                            }
                            catch (Exception ex)
                            {
                                LATE.Core.LatePlugin.Log.LogError(
                                    $"[EnemyManager] Error sending FreezeRPC for {enemyName}: {ex.Message}"
                                );
                            }
                        }
                    }
                    else // !hostIsSpawned
                    {
                        // Host considers this enemy DESPAWNED. Ensure client has it despawned.
                        LATE.Core.LatePlugin.Log.LogDebug(
                            $"[EnemyManager] Enemy '{enemyName}' is DESPAWNED on host. Sending DespawnRPC to {nick}. (Parent PV: {parentPv.ViewID})"
                        );
                        parentPv.RPC("DespawnRPC", targetPlayer);
                        despawnRpcSentCount++;

                        // DO NOT sync detailed state (target, specific states) for despawned enemies.
                    }
                }
                catch (Exception ex)
                {
                    LATE.Core.LatePlugin.Log.LogError(
                        $"[EnemyManager] CRITICAL error processing enemy '{enemyName}' (Parent PV: {parentPv?.ViewID ?? 0}) for state sync: {ex}"
                    );
                }
            }

            LATE.Core.LatePlugin.Log.LogInfo(
                $"[EnemyManager] === Finished FULL enemy state sync for {nick}. ==="
            );
            LATE.Core.LatePlugin.Log.LogInfo(
                $"    Processed: {processedCount}, SpawnRPCs Sent: {spawnRpcSentCount}, DespawnRPCs Sent: {despawnRpcSentCount}"
            );
            LATE.Core.LatePlugin.Log.LogInfo(
                $"    SpecificStates Synced (for Spawned): {specificStateSyncedCount}, Freezes Synced: {freezeSyncedCount}, OtherStates Synced: {otherStateSyncedCount}"
            );
        }
        #endregion
    }
}