// File: L.A.T.E/Managers/EnemySyncManager.cs
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
using LATE.Core; // For LatePlugin.Log
using LATE.Utilities; // For GameUtilities class

namespace LATE.Managers; // File-scoped namespace

/// <summary>
/// Handles enemy synchronization, including notifying enemies of players joining/leaving
/// and syncing detailed enemy states to late-joining players.
/// </summary>
internal static class EnemySyncManager // Renamed from EnemyManager
{
    #region ─── Common helpers ──────────────────────────────────────────────
    private static bool IsMaster() => GameUtilities.IsRealMasterClient(); // Updated

    // Note: This ForEachEnemy iterates only ACTIVE enemies by default.
    // The SyncAllEnemyStatesForPlayer method uses FindObjectsOfType(true) to include inactive ones.
    private static void ForEachEnemy(Action<Enemy, PhotonView?> action)
    {
        Enemy[] enemies = Object.FindObjectsOfType<Enemy>(); // includeInactive = false (default)
        LatePlugin.Log.LogDebug(
            $"[EnemySyncManager] ForEachEnemy found {enemies.Length} active enemies."
        );

        foreach (Enemy enemy in enemies)
        {
            if (enemy == null || enemy.gameObject == null)
                continue;

            PhotonView? enemyPv = null;
            try
            {
                if (GameUtilities.enemyPhotonViewField != null) // Updated
                {
                    enemyPv = GameUtilities.enemyPhotonViewField.GetValue(enemy) as PhotonView; // Updated
                }
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning(
                    $"[EnemySyncManager] Error reflecting Enemy.photonView for '{enemy.gameObject.name}': {ex.Message}"
                );
            }

            if (enemyPv == null)
            {
                enemyPv = enemy.GetComponent<PhotonView>();
                if (enemyPv == null)
                {
                    LatePlugin.Log.LogWarning(
                        $"[EnemySyncManager] Could not get PhotonView for active enemy '{enemy.gameObject.name}' via reflection or GetComponent. Skipping action."
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
                LatePlugin.Log.LogError(
                    $"[EnemySyncManager] Enemy action failed on '{enemy.gameObject?.name ?? "NULL"}' (ViewID: {enemyPv?.ViewID ?? 0}): {ex}"
                );
            }
        }
    }
    #endregion

    #region ─── Notify Enemies of New Player ────────────────────────────────
    public static void NotifyEnemiesOfNewPlayer(Player newPlayer, PlayerAvatar newPlayerAvatar)
    {
        if (newPlayer == null)
        {
            LatePlugin.Log.LogWarning(
                "[EnemySyncManager] NotifyEnemiesOfNewPlayer called with null player."
            );
            return;
        }
        if (!IsMaster())
        {
            LatePlugin.Log.LogDebug(
                $"[EnemySyncManager] Not MasterClient, skipping new-player notification for {newPlayer?.NickName ?? "<null>"}."
            );
            return;
        }
        if (newPlayerAvatar == null)
        {
            LatePlugin.Log.LogError(
                $"[EnemySyncManager] newPlayerAvatar is null for {newPlayer.NickName}. Aborting."
            );
            return;
        }

        PhotonView? avatarPv = GameUtilities.GetPhotonView(newPlayerAvatar); // Updated
        if (avatarPv == null)
        {
            LatePlugin.Log.LogError(
                $"[EnemySyncManager] Could not get PhotonView for {newPlayer.NickName}'s Avatar."
            );
            return;
        }
        int avatarViewId = avatarPv.ViewID;

        LatePlugin.Log.LogInfo(
            $"[EnemySyncManager] Notifying ACTIVE enemies about new player {newPlayer.NickName} (ViewID {avatarViewId})."
        );

        int updated = 0;
        ForEachEnemy(
            (enemy, enemyPv) =>
            {
                try
                {
                    enemy.PlayerAdded(avatarViewId);
                    updated++;
                    LatePlugin.Log.LogDebug(
                        $"[EnemySyncManager] PlayerAdded({avatarViewId}) called on ACTIVE enemy '{enemy.gameObject.name}' (Enemy ViewID: {enemyPv?.ViewID ?? 0})."
                    );
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogError(
                        $"[EnemySyncManager] Error calling PlayerAdded on '{enemy.gameObject.name}': {ex.Message}"
                    );
                }
            }
        );

        LatePlugin.Log.LogInfo(
            $"[EnemySyncManager] Finished notifying {updated} active enemies about {newPlayer.NickName}."
        );
    }
    #endregion

    #region ─── Notify Enemies of Leaving Player ────────────────────────────
    public static void NotifyEnemiesOfLeavingPlayer(Player leavingPlayer)
    {
        if (leavingPlayer == null)
        {
            LatePlugin.Log.LogWarning(
                "[EnemySyncManager] NotifyEnemiesOfLeavingPlayer called with null player."
            );
            return;
        }
        if (!IsMaster())
        {
            LatePlugin.Log.LogDebug(
                $"[EnemySyncManager] Not MasterClient, skipping leaving-notification for {leavingPlayer?.NickName ?? "<null>"}."
            );
            return;
        }

        PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(leavingPlayer); // Updated
        PhotonView? avatarPv = null;
        if (avatar != null)
        {
            avatarPv = GameUtilities.GetPhotonView(avatar); // Updated
        }

        if (avatarPv == null)
        {
            LatePlugin.Log.LogWarning(
                $"[EnemySyncManager] Could not resolve Avatar ViewID for leaving player {leavingPlayer.NickName}. Skipping enemy notification (player likely gone)."
            );
            return;
        }
        int avatarViewId = avatarPv.ViewID;

        LatePlugin.Log.LogInfo(
            $"[EnemySyncManager] Notifying ACTIVE enemies that {leavingPlayer.NickName} (ViewID {avatarViewId}) left."
        );

        int updated = 0;
        ForEachEnemy(
            (enemy, enemyPv) =>
            {
                GameUtilities.TryGetEnemyTargetViewIdReflected(enemy, out var currentTargetId); // Updated
                bool wasTarget = currentTargetId == avatarViewId;

                try
                {
                    enemy.PlayerRemoved(avatarViewId);
                    updated++;
                    LatePlugin.Log.LogDebug(
                        $"[EnemySyncManager] PlayerRemoved({avatarViewId}) called on ACTIVE enemy '{enemy.gameObject.name}' (Enemy ViewID: {enemyPv?.ViewID ?? 0}). WasTarget: {wasTarget}"
                    );
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogError(
                        $"[EnemySyncManager] Error calling PlayerRemoved on '{enemy.gameObject.name}': {ex.Message}"
                    );
                }
            }
        );

        LatePlugin.Log.LogInfo(
            $"[EnemySyncManager] Finished notifying {updated} active enemies about {leavingPlayer.NickName} leaving."
        );
    }
    #endregion

    #region ─── Sync Enemy State for Late Joiner (REVISED + REFLECTION) ─────
    public static void SyncAllEnemyStatesForPlayer(Player targetPlayer)
    {
        if (!IsMaster())
        {
            LatePlugin.Log.LogDebug(
                "[EnemySyncManager] Not MasterClient, skipping SyncAllEnemyStatesForPlayer."
            );
            return;
        }
        if (targetPlayer == null)
        {
            LatePlugin.Log.LogWarning(
                "[EnemySyncManager] SyncAllEnemyStatesForPlayer called with null targetPlayer."
            );
            return;
        }

        if (GameUtilities.enemy_EnemyParentField == null || GameUtilities.ep_SpawnedField == null) // Updated
        {
            LatePlugin.Log.LogError(
                "[EnemySyncManager] CRITICAL REFLECTION FAILURE: Cannot find required internal fields (Enemy.EnemyParent, EnemyParent.Spawned). Aborting enemy sync."
            );
            return;
        }

        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogInfo(
            $"[EnemySyncManager] === Starting FULL enemy state sync for {nick} using EnemyParent RPCs (with reflection) ==="
        );

        int processedCount = 0;
        int spawnRpcSentCount = 0;
        int despawnRpcSentCount = 0;
        int specificStateSyncedCount = 0;
        int freezeSyncedCount = 0;
        int otherStateSyncedCount = 0;

        Enemy[] allEnemies = Object.FindObjectsOfType<Enemy>(true);
        LatePlugin.Log.LogDebug(
            $"[EnemySyncManager] Found {allEnemies.Length} total Enemy components (including inactive) for state sync."
        );

        foreach (Enemy enemy in allEnemies)
        {
            processedCount++;
            if (enemy == null || enemy.gameObject == null)
            {
                LatePlugin.Log.LogWarning(
                    $"[EnemySyncManager] Encountered null enemy instance at index {processedCount - 1}. Skipping."
                );
                continue;
            }

            string enemyName = enemy.gameObject.name;
            EnemyParent? enemyParent = null;
            PhotonView? parentPv = null;

            try
            {
                enemyParent = GameUtilities.enemy_EnemyParentField.GetValue(enemy) as EnemyParent; // Updated
                if (enemyParent == null)
                {
                    enemyParent = enemy.GetComponentInParent<EnemyParent>();
                    if (enemyParent == null)
                    {
                        LatePlugin.Log.LogWarning(
                            $"[EnemySyncManager] Enemy '{enemyName}' has no EnemyParent component. Skipping sync."
                        );
                        continue;
                    }
                }
                parentPv = enemyParent.GetComponent<PhotonView>();
                if (parentPv == null)
                {
                    LatePlugin.Log.LogWarning(
                        $"[EnemySyncManager] Could not get PhotonView for EnemyParent of '{enemyName}'. Skipping sync."
                    );
                    continue;
                }
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError(
                    $"[EnemySyncManager] Reflection error getting EnemyParent for '{enemyName}': {ex}. Skipping sync."
                );
                continue;
            }

            PhotonView? enemyPv = null;
            try
            {
                if (GameUtilities.enemyPhotonViewField != null) // Updated
                {
                    enemyPv = GameUtilities.enemyPhotonViewField.GetValue(enemy) as PhotonView; // Updated
                }
            }
            catch (Exception pvEx)
            {
                LatePlugin.Log.LogWarning(
                    $"[EnemySyncManager] Error reflecting Enemy.photonView for '{enemyName}': {pvEx.Message}"
                );
            }
            if (enemyPv == null)
            {
                enemyPv = enemy.GetComponent<PhotonView>();
                if (enemyPv == null)
                {
                    LatePlugin.Log.LogWarning(
                        $"[EnemySyncManager] Could not get Enemy's own PhotonView for '{enemyName}'. Skipping detailed state sync."
                    );
                }
            }

            try
            {
                bool hostIsSpawned = false;
                try
                {
                    object? spawnedValue = GameUtilities.ep_SpawnedField.GetValue(enemyParent); // Updated
                    if (spawnedValue is bool val) hostIsSpawned = val;
                    else LatePlugin.Log.LogWarning($"[EnemySyncManager] Reflected EnemyParent.Spawned for '{enemyName}' was not a bool.");
                }
                catch (Exception spawnEx)
                {
                    LatePlugin.Log.LogError($"[EnemySyncManager] Reflection error getting EnemyParent.Spawned for '{enemyName}': {spawnEx}. Assuming false.");
                }

                if (hostIsSpawned)
                {
                    LatePlugin.Log.LogDebug($"[EnemySyncManager] Enemy '{enemyName}' is SPAWNED on host. Sending SpawnRPC to {nick}. (Parent PV: {parentPv.ViewID})");
                    parentPv.RPC("SpawnRPC", targetPlayer);
                    spawnRpcSentCount++;

                    if (enemyPv == null)
                    {
                        LatePlugin.Log.LogWarning($"[EnemySyncManager]   Skipping detailed sync for '{enemyName}' because its own PhotonView is missing.");
                        continue;
                    }

                    if (GameUtilities.TryGetEnemyTargetViewIdReflected(enemy, out int hostTargetViewId) && hostTargetViewId > 0) // Updated
                    {
                        LatePlugin.Log.LogDebug($"[EnemySyncManager]   Enemy '{enemyName}' TargetPlayerViewID {hostTargetViewId} (Should be synced by base Enemy serialization).");
                    }

                    bool specificStateSynced = false;

                    EnemyAnimal animalController = enemy.GetComponent<EnemyAnimal>();
                    if (animalController != null)
                    {
                        try
                        {
                            EnemyAnimal.State hostState = animalController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyAnimal state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyAnimal state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyBang bangController = enemy.GetComponent<EnemyBang>();
                    if (bangController != null)
                    {
                        try
                        {
                            EnemyBang.State hostState = bangController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            FieldInfo? fuseActiveField = AccessTools.Field(typeof(EnemyBang), "fuseActive");
                            FieldInfo? fuseLerpField = AccessTools.Field(typeof(EnemyBang), "fuseLerp");
                            if (fuseActiveField != null && fuseLerpField != null)
                            {
                                bool hostFuseActive = (bool)fuseActiveField.GetValue(bangController);
                                float hostFuseLerp = (float)fuseLerpField.GetValue(bangController);
                                enemyPv.RPC("FuseRPC", targetPlayer, hostFuseActive, hostFuseLerp);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyBang fuse state: Active={hostFuseActive}, Lerp={hostFuseLerp:F2}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyBang state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyBang state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyBeamer beamerController = enemy.GetComponent<EnemyBeamer>();
                    if (beamerController != null)
                    {
                        try
                        {
                            EnemyBeamer.State hostState = beamerController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            FieldInfo? moveFastField = AccessTools.Field(typeof(EnemyBeamer), "moveFast");
                            if (moveFastField != null)
                            {
                                bool hostMoveFast = (bool)moveFastField.GetValue(beamerController);
                                enemyPv.RPC("MoveFastRPC", targetPlayer, hostMoveFast);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyBeamer moveFast state: {hostMoveFast}");
                                otherStateSyncedCount++;
                            }
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(beamerController, GameUtilities.enemyBeamer_playerTargetField, "EnemyBeamer"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyBeamer target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyBeamer state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyBeamer state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyBowtie bowtieController = enemy.GetComponent<EnemyBowtie>();
                    if (bowtieController != null)
                    {
                        try
                        {
                            EnemyBowtie.State hostState = bowtieController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(bowtieController, AccessTools.Field(typeof(EnemyBowtie), "playerTarget"), "EnemyBowtie"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("NoticeRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyBowtie notice for target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyBowtie state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyBowtie state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyCeilingEye ceilingEyeController = enemy.GetComponent<EnemyCeilingEye>();
                    if (ceilingEyeController != null)
                    {
                        try
                        {
                            EnemyCeilingEye.State hostState = ceilingEyeController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(ceilingEyeController, GameUtilities.enemyCeilingEye_targetPlayerField, "EnemyCeilingEye"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("TargetPlayerRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyCeilingEye target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyCeilingEye state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyCeilingEye state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyDuck duckController = enemy.GetComponent<EnemyDuck>();
                    if (duckController != null)
                    {
                        try
                        {
                            EnemyDuck.State hostState = duckController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(duckController, AccessTools.Field(typeof(EnemyDuck), "playerTarget"), "EnemyDuck"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyDuck target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyDuck state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyDuck state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyFloater floaterController = enemy.GetComponent<EnemyFloater>();
                    if (floaterController != null)
                    {
                        try
                        {
                            EnemyFloater.State hostState = floaterController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(floaterController, GameUtilities.enemyFloater_targetPlayerField, "EnemyFloater"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("TargetPlayerRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyFloater target: {hostTarget.name}");
                                otherStateSyncedCount++;
                                if (hostState != EnemyFloater.State.Attack && hostState != EnemyFloater.State.ChargeAttack && hostState != EnemyFloater.State.DelayAttack)
                                {
                                    enemyPv.RPC("NoticeRPC", targetPlayer, hostTarget.photonView.ViewID);
                                    LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyFloater notice RPC for target: {hostTarget.name}");
                                    otherStateSyncedCount++;
                                }
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyFloater state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyFloater state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyGnome gnomeController = enemy.GetComponent<EnemyGnome>();
                    if (gnomeController != null)
                    {
                        try
                        {
                            EnemyGnome.State hostState = gnomeController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyGnome state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyGnome state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyHidden hiddenController = enemy.GetComponent<EnemyHidden>();
                    if (hiddenController != null)
                    {
                        try
                        {
                            EnemyHidden.State hostState = hiddenController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(hiddenController, AccessTools.Field(typeof(EnemyHidden), "playerTarget"), "EnemyHidden"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyHidden target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyHidden state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyHidden state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyHunter hunterController = enemy.GetComponent<EnemyHunter>();
                    if (hunterController != null)
                    {
                        try
                        {
                            EnemyHunter.State hostState = hunterController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            FieldInfo? investigatePointField = AccessTools.Field(typeof(EnemyHunter), "investigatePoint");
                            if (investigatePointField != null && (hostState == EnemyHunter.State.Investigate || hostState == EnemyHunter.State.Aim))
                            {
                                Vector3 hostInvestigatePoint = (Vector3)investigatePointField.GetValue(hunterController);
                                enemyPv.RPC("UpdateInvestigationPoint", targetPlayer, hostInvestigatePoint);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyHunter investigate point: {hostInvestigatePoint}");
                                otherStateSyncedCount++;
                            }
                            FieldInfo? moveFastField = AccessTools.Field(typeof(EnemyHunter), "moveFast");
                            if (moveFastField != null)
                            {
                                bool hostMoveFast = (bool)moveFastField.GetValue(hunterController);
                                enemyPv.RPC("MoveFastRPC", targetPlayer, hostMoveFast);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyHunter moveFast: {hostMoveFast}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyHunter state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyHunter state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyRobe robeController = enemy.GetComponent<EnemyRobe>();
                    if (robeController != null)
                    {
                        try
                        {
                            EnemyRobe.State hostState = robeController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(robeController, GameUtilities.enemyRobe_targetPlayerField, "EnemyRobe"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("TargetPlayerRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyRobe target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            FieldInfo? isOnScreenField = AccessTools.Field(typeof(EnemyRobe), "isOnScreen");
                            if (isOnScreenField != null)
                            {
                                bool hostIsOnScreen = (bool)isOnScreenField.GetValue(robeController);
                                enemyPv.RPC("UpdateOnScreenRPC", targetPlayer, hostIsOnScreen);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyRobe isOnScreen: {hostIsOnScreen}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyRobe state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyRobe state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyRunner runnerController = enemy.GetComponent<EnemyRunner>();
                    if (runnerController != null)
                    {
                        try
                        {
                            EnemyRunner.State hostState = runnerController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(runnerController, GameUtilities.enemyRunner_targetPlayerField, "EnemyRunner"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyRunner target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyRunner state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyRunner state for {enemyName}: {ex.Message}"); }
                    }

                    EnemySlowMouth mouthController = enemy.GetComponent<EnemySlowMouth>();
                    if (mouthController != null)
                    {
                        try
                        {
                            EnemySlowMouth.State hostState = mouthController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(mouthController, AccessTools.Field(typeof(EnemySlowMouth), "playerTarget"), "EnemySlowMouth"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemySlowMouth target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemySlowMouth state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemySlowMouth state for {enemyName}: {ex.Message}"); }
                    }

                    EnemySlowWalker walkerController = enemy.GetComponent<EnemySlowWalker>();
                    if (walkerController != null)
                    {
                        try
                        {
                            EnemySlowWalker.State hostState = walkerController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(walkerController, GameUtilities.enemySlowWalker_targetPlayerField, "EnemySlowWalker"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("TargetPlayerRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemySlowWalker target: {hostTarget.name}");
                                otherStateSyncedCount++;
                                if (hostState == EnemySlowWalker.State.Notice)
                                {
                                    enemyPv.RPC("NoticeRPC", targetPlayer, hostTarget.photonView.ViewID);
                                    LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemySlowWalker notice RPC.");
                                    otherStateSyncedCount++;
                                }
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemySlowWalker state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemySlowWalker state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyThinMan thinManController = enemy.GetComponent<EnemyThinMan>();
                    if (thinManController != null)
                    {
                        try
                        {
                            EnemyThinMan.State hostState = thinManController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(thinManController, GameUtilities.enemyThinMan_playerTargetField, "EnemyThinMan"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("SetTargetRPC", targetPlayer, hostTarget.photonView.ViewID, true);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyThinMan target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyThinMan state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyThinMan state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyTumbler tumblerController = enemy.GetComponent<EnemyTumbler>();
                    if (tumblerController != null)
                    {
                        try
                        {
                            EnemyTumbler.State hostState = tumblerController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(tumblerController, GameUtilities.enemyTumbler_targetPlayerField, "EnemyTumbler"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("TargetPlayerRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyTumbler target: {hostTarget.name}");
                                otherStateSyncedCount++;
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyTumbler state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyTumbler state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyUpscream upscreamController = enemy.GetComponent<EnemyUpscream>();
                    if (upscreamController != null)
                    {
                        try
                        {
                            EnemyUpscream.State hostState = upscreamController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(upscreamController, GameUtilities.enemyUpscream_targetPlayerField, "EnemyUpscream"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("TargetPlayerRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyUpscream target: {hostTarget.name}");
                                otherStateSyncedCount++;
                                if (hostState == EnemyUpscream.State.PlayerNotice || hostState == EnemyUpscream.State.GoToPlayer || hostState == EnemyUpscream.State.Attack)
                                {
                                    enemyPv.RPC("NoticeSetRPC", targetPlayer, hostTarget.photonView.ViewID);
                                    LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyUpscream notice RPC.");
                                    otherStateSyncedCount++;
                                }
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyUpscream state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyUpscream state for {enemyName}: {ex.Message}"); }
                    }

                    EnemyValuableThrower throwerController = enemy.GetComponent<EnemyValuableThrower>();
                    if (throwerController != null)
                    {
                        try
                        {
                            EnemyValuableThrower.State hostState = throwerController.currentState;
                            enemyPv.RPC("UpdateStateRPC", targetPlayer, hostState);
                            PlayerAvatar? hostTarget = GameUtilities.GetInternalPlayerTarget(throwerController, AccessTools.Field(typeof(EnemyValuableThrower), "playerTarget"), "EnemyValuableThrower"); // Updated
                            if (hostTarget != null && hostTarget.photonView != null)
                            {
                                enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, hostTarget.photonView.ViewID);
                                LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyValuableThrower target: {hostTarget.name}");
                                otherStateSyncedCount++;
                                if (hostState == EnemyValuableThrower.State.PlayerNotice || hostState == EnemyValuableThrower.State.GetValuable || hostState == EnemyValuableThrower.State.GoToTarget)
                                {
                                    enemyPv.RPC("NoticeRPC", targetPlayer, hostTarget.photonView.ViewID);
                                    LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyValuableThrower notice RPC.");
                                    otherStateSyncedCount++;
                                }
                            }
                            specificStateSyncedCount++; specificStateSynced = true;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced EnemyValuableThrower state '{hostState}'.");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error syncing EnemyValuableThrower state for {enemyName}: {ex.Message}"); }
                    }

                    if (!specificStateSynced)
                    {
                        LatePlugin.Log.LogDebug($"[EnemySyncManager]   No specific enemy type found or synced for '{enemyName}'. Base state from EnemyParent: Spawned.");
                    }

                    if (enemy.FreezeTimer > 0f)
                    {
                        try
                        {
                            enemyPv.RPC("FreezeRPC", targetPlayer, enemy.FreezeTimer);
                            freezeSyncedCount++;
                            LatePlugin.Log.LogDebug($"[EnemySyncManager]   Synced FreezeTimer ({enemy.FreezeTimer:F1}s).");
                        }
                        catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error sending FreezeRPC for {enemyName}: {ex.Message}"); }
                    }
                }
                else
                {
                    LatePlugin.Log.LogDebug($"[EnemySyncManager] Enemy '{enemyName}' is DESPAWNED on host. Sending DespawnRPC to {nick}. (Parent PV: {parentPv.ViewID})");
                    parentPv.RPC("DespawnRPC", targetPlayer);
                    despawnRpcSentCount++;
                }
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[EnemySyncManager] CRITICAL error processing enemy '{enemyName}' (Parent PV: {parentPv?.ViewID ?? 0}) for state sync: {ex}");
            }
        }

        LatePlugin.Log.LogInfo($"[EnemySyncManager] === Finished FULL enemy state sync for {nick}. ===");
        LatePlugin.Log.LogInfo($"    Processed: {processedCount}, SpawnRPCs Sent: {spawnRpcSentCount}, DespawnRPCs Sent: {despawnRpcSentCount}");
        LatePlugin.Log.LogInfo($"    SpecificStates Synced (for Spawned): {specificStateSyncedCount}, Freezes Synced: {freezeSyncedCount}, OtherStates Synced: {otherStateSyncedCount}");
    }
    #endregion
}