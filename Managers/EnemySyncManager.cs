// File: L.A.T.E/Managers/EnemySyncManager.cs
using HarmonyLib; // For AccessTools in specific enemy sections
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;
using LATE.Core;
using LATE.Utilities; // For ReflectionCache, PhotonUtilities, GameUtilities

namespace LATE.Managers;

/// <summary>
/// Handles enemy synchronization, including notifying enemies of players joining/leaving
/// and syncing detailed enemy states to late-joining players.
/// </summary>
internal static class EnemySyncManager
{
    #region ─── Common helpers ──────────────────────────────────────────────
    private static bool IsMaster() => PhotonUtilities.IsRealMasterClient(); // Corrected: Use PhotonUtilities

    private static void ForEachEnemy(Action<Enemy, PhotonView?> action)
    {
        Enemy[] enemies = Object.FindObjectsOfType<Enemy>();
        LatePlugin.Log.LogDebug($"[EnemySyncManager] ForEachEnemy found {enemies.Length} active enemies.");

        foreach (Enemy enemy in enemies)
        {
            if (enemy == null || enemy.gameObject == null) continue;

            PhotonView? enemyPv = null;
            try
            {
                // Corrected: Use ReflectionCache
                if (ReflectionCache.Enemy_PhotonViewField != null)
                {
                    enemyPv = ReflectionCache.Enemy_PhotonViewField.GetValue(enemy) as PhotonView;
                }
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"[EnemySyncManager] Error reflecting Enemy.photonView for '{enemy.gameObject.name}': {ex.Message}");
            }

            if (enemyPv == null)
            {
                enemyPv = enemy.GetComponent<PhotonView>(); // Fallback
                if (enemyPv == null)
                {
                    LatePlugin.Log.LogWarning($"[EnemySyncManager] Could not get PhotonView for active enemy '{enemy.gameObject.name}'. Skipping action.");
                    continue;
                }
            }
            try { action(enemy, enemyPv); }
            catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Enemy action failed on '{enemy.gameObject?.name ?? "NULL"}' (ViewID: {enemyPv?.ViewID ?? 0}): {ex}"); }
        }
    }
    #endregion

    #region ─── Notify Enemies of New Player ────────────────────────────────
    public static void NotifyEnemiesOfNewPlayer(Player newPlayer, PlayerAvatar newPlayerAvatar)
    {
        if (newPlayer == null) return;
        if (!IsMaster()) return;
        if (newPlayerAvatar == null) return;

        PhotonView? avatarPv = PhotonUtilities.GetPhotonView(newPlayerAvatar); // Corrected: Use PhotonUtilities
        if (avatarPv == null) return;

        int avatarViewId = avatarPv.ViewID;
        LatePlugin.Log.LogInfo($"[EnemySyncManager] Notifying ACTIVE enemies about new player {newPlayer.NickName} (ViewID {avatarViewId}).");
        int updated = 0;
        ForEachEnemy((enemy, enemyPv) =>
        {
            try
            {
                enemy.PlayerAdded(avatarViewId);
                updated++;
            }
            catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error calling PlayerAdded on '{enemy.gameObject.name}': {ex.Message}"); }
        });
        LatePlugin.Log.LogInfo($"[EnemySyncManager] Finished notifying {updated} active enemies about {newPlayer.NickName}.");
    }
    #endregion

    #region ─── Notify Enemies of Leaving Player ────────────────────────────
    public static void NotifyEnemiesOfLeavingPlayer(Player leavingPlayer)
    {
        if (leavingPlayer == null) return;
        if (!IsMaster()) return;

        PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(leavingPlayer);
        PhotonView? avatarPv = null;
        if (avatar != null)
        {
            avatarPv = PhotonUtilities.GetPhotonView(avatar); // Corrected: Use PhotonUtilities
        }
        if (avatarPv == null) return;

        int avatarViewId = avatarPv.ViewID;
        LatePlugin.Log.LogInfo($"[EnemySyncManager] Notifying ACTIVE enemies that {leavingPlayer.NickName} (ViewID {avatarViewId}) left.");
        int updated = 0;
        ForEachEnemy((enemy, enemyPv) =>
        {
            // Corrected: Use GameUtilities (which uses ReflectionCache)
            GameUtilities.TryGetEnemyTargetViewIdReflected(enemy, out var currentTargetId);
            bool wasTarget = currentTargetId == avatarViewId;
            try
            {
                enemy.PlayerRemoved(avatarViewId);
                updated++;
            }
            catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error calling PlayerRemoved on '{enemy.gameObject.name}': {ex.Message}"); }
        });
        LatePlugin.Log.LogInfo($"[EnemySyncManager] Finished notifying {updated} active enemies about {leavingPlayer.NickName} leaving.");
    }
    #endregion

    #region ─── Sync Enemy State for Late Joiner (REVISED + REFLECTION) ─────
    public static void SyncAllEnemyStatesForPlayer(Player targetPlayer)
    {
        if (!IsMaster() || targetPlayer == null) return;

        // Corrected: Use ReflectionCache
        if (ReflectionCache.Enemy_EnemyParentField == null || ReflectionCache.EnemyParent_SpawnedField == null)
        {
            LatePlugin.Log.LogError("[EnemySyncManager] CRITICAL REFLECTION FAILURE: EnemyParent or Spawned field not found in ReflectionCache. Aborting enemy sync.");
            return;
        }

        string nick = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        LatePlugin.Log.LogInfo($"[EnemySyncManager] === Starting FULL enemy state sync for {nick} ===");
        int processedCount = 0, spawnRpcSentCount = 0, despawnRpcSentCount = 0, specificStateSyncedCount = 0, freezeSyncedCount = 0, otherStateSyncedCount = 0;
        Enemy[] allEnemies = Object.FindObjectsOfType<Enemy>(true);

        foreach (Enemy enemy in allEnemies)
        {
            processedCount++;
            if (enemy == null || enemy.gameObject == null) continue;
            string enemyName = enemy.gameObject.name;
            EnemyParent? enemyParent = null;
            PhotonView? parentPv = null;

            try
            {
                // Corrected: Use ReflectionCache
                enemyParent = ReflectionCache.Enemy_EnemyParentField.GetValue(enemy) as EnemyParent;
                if (enemyParent == null) enemyParent = enemy.GetComponentInParent<EnemyParent>(); // Fallback
                if (enemyParent == null) continue;
                parentPv = enemyParent.GetComponent<PhotonView>(); // EnemyParent should have a PV
                if (parentPv == null) continue;
            }
            catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] Error getting EnemyParent for '{enemyName}': {ex}"); continue; }

            PhotonView? enemyPv = null;
            try
            {
                // Corrected: Use ReflectionCache
                if (ReflectionCache.Enemy_PhotonViewField != null)
                    enemyPv = ReflectionCache.Enemy_PhotonViewField.GetValue(enemy) as PhotonView;
            }
            catch { /* Logged by ReflectionCache if critical */ }
            if (enemyPv == null) enemyPv = enemy.GetComponent<PhotonView>(); // Fallback
            // if enemyPv still null, detailed sync might fail, but Spawn/Despawn RPC will proceed.

            try
            {
                bool hostIsSpawned = false;
                try
                {
                    // Corrected: Use ReflectionCache
                    object? spawnedValue = ReflectionCache.EnemyParent_SpawnedField.GetValue(enemyParent);
                    if (spawnedValue is bool val) hostIsSpawned = val;
                }
                catch { /* Error logged by ReflectionCache */ }

                if (hostIsSpawned)
                {
                    parentPv.RPC("SpawnRPC", targetPlayer); spawnRpcSentCount++;
                    if (enemyPv == null) continue; // Cannot do detailed sync without enemy's PV

                    // Corrected: Use GameUtilities (which uses ReflectionCache)
                    if (GameUtilities.TryGetEnemyTargetViewIdReflected(enemy, out int hostTargetViewId) && hostTargetViewId > 0) { /* Logging */ }

                    bool specificStateSynced = false; // Per enemy

                    // EnemyAnimal
                    EnemyAnimal animal = enemy.GetComponent<EnemyAnimal>();
                    if (animal != null) { enemyPv.RPC("UpdateStateRPC", targetPlayer, animal.currentState); specificStateSyncedCount++; specificStateSynced = true; }

                    // EnemyBang
                    EnemyBang bang = enemy.GetComponent<EnemyBang>();
                    if (bang != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, bang.currentState);
                        FieldInfo? fuseActive = AccessTools.Field(typeof(EnemyBang), "fuseActive"); // These are very specific, local reflection okay
                        FieldInfo? fuseLerp = AccessTools.Field(typeof(EnemyBang), "fuseLerp");
                        if (fuseActive != null && fuseLerp != null)
                        {
                            enemyPv.RPC("FuseRPC", targetPlayer, (bool)fuseActive.GetValue(bang), (float)fuseLerp.GetValue(bang));
                            otherStateSyncedCount++;
                        }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyBeamer
                    EnemyBeamer beamer = enemy.GetComponent<EnemyBeamer>();
                    if (beamer != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, beamer.currentState);
                        FieldInfo? moveFast = AccessTools.Field(typeof(EnemyBeamer), "moveFast");
                        if (moveFast != null) { enemyPv.RPC("MoveFastRPC", targetPlayer, (bool)moveFast.GetValue(beamer)); otherStateSyncedCount++; }
                        // Corrected: Use GameUtilities & ReflectionCache for target field
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(beamer, ReflectionCache.EnemyBeamer_PlayerTargetField, "EnemyBeamer");
                        if (target?.photonView != null) { enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyBowtie
                    EnemyBowtie bowtie = enemy.GetComponent<EnemyBowtie>();
                    if (bowtie != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, bowtie.currentState);
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(bowtie, AccessTools.Field(typeof(EnemyBowtie), "playerTarget"), "EnemyBowtie");
                        if (target?.photonView != null) { enemyPv.RPC("NoticeRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyCeilingEye
                    EnemyCeilingEye ceilingEye = enemy.GetComponent<EnemyCeilingEye>();
                    if (ceilingEye != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, ceilingEye.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(ceilingEye, ReflectionCache.EnemyCeilingEye_TargetPlayerField, "EnemyCeilingEye");
                        if (target?.photonView != null) { enemyPv.RPC("TargetPlayerRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyDuck
                    EnemyDuck duck = enemy.GetComponent<EnemyDuck>();
                    if (duck != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, duck.currentState);
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(duck, AccessTools.Field(typeof(EnemyDuck), "playerTarget"), "EnemyDuck");
                        if (target?.photonView != null) { enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyFloater
                    EnemyFloater floater = enemy.GetComponent<EnemyFloater>();
                    if (floater != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, floater.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(floater, ReflectionCache.EnemyFloater_TargetPlayerField, "EnemyFloater");
                        if (target?.photonView != null)
                        {
                            enemyPv.RPC("TargetPlayerRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++;
                            if (floater.currentState != EnemyFloater.State.Attack && floater.currentState != EnemyFloater.State.ChargeAttack && floater.currentState != EnemyFloater.State.DelayAttack)
                            {
                                enemyPv.RPC("NoticeRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++;
                            }
                        }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyGnome
                    EnemyGnome gnome = enemy.GetComponent<EnemyGnome>();
                    if (gnome != null) { enemyPv.RPC("UpdateStateRPC", targetPlayer, gnome.currentState); specificStateSyncedCount++; specificStateSynced = true; }

                    // EnemyHidden
                    EnemyHidden hidden = enemy.GetComponent<EnemyHidden>();
                    if (hidden != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, hidden.currentState);
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(hidden, AccessTools.Field(typeof(EnemyHidden), "playerTarget"), "EnemyHidden");
                        if (target?.photonView != null) { enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyHunter
                    EnemyHunter hunter = enemy.GetComponent<EnemyHunter>();
                    if (hunter != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, hunter.currentState);
                        FieldInfo? invPoint = AccessTools.Field(typeof(EnemyHunter), "investigatePoint");
                        if (invPoint != null && (hunter.currentState == EnemyHunter.State.Investigate || hunter.currentState == EnemyHunter.State.Aim))
                        {
                            enemyPv.RPC("UpdateInvestigationPoint", targetPlayer, (Vector3)invPoint.GetValue(hunter)); otherStateSyncedCount++;
                        }
                        FieldInfo? moveFast = AccessTools.Field(typeof(EnemyHunter), "moveFast");
                        if (moveFast != null) { enemyPv.RPC("MoveFastRPC", targetPlayer, (bool)moveFast.GetValue(hunter)); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyRobe
                    EnemyRobe robe = enemy.GetComponent<EnemyRobe>();
                    if (robe != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, robe.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(robe, ReflectionCache.EnemyRobe_TargetPlayerField, "EnemyRobe");
                        if (target?.photonView != null) { enemyPv.RPC("TargetPlayerRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        FieldInfo? isOnScreen = AccessTools.Field(typeof(EnemyRobe), "isOnScreen");
                        if (isOnScreen != null) { enemyPv.RPC("UpdateOnScreenRPC", targetPlayer, (bool)isOnScreen.GetValue(robe)); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyRunner
                    EnemyRunner runner = enemy.GetComponent<EnemyRunner>();
                    if (runner != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, runner.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(runner, ReflectionCache.EnemyRunner_TargetPlayerField, "EnemyRunner");
                        if (target?.photonView != null) { enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemySlowMouth
                    EnemySlowMouth mouth = enemy.GetComponent<EnemySlowMouth>();
                    if (mouth != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, mouth.currentState);
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(mouth, AccessTools.Field(typeof(EnemySlowMouth), "playerTarget"), "EnemySlowMouth");
                        if (target?.photonView != null) { enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemySlowWalker
                    EnemySlowWalker walker = enemy.GetComponent<EnemySlowWalker>();
                    if (walker != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, walker.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(walker, ReflectionCache.EnemySlowWalker_TargetPlayerField, "EnemySlowWalker");
                        if (target?.photonView != null)
                        {
                            enemyPv.RPC("TargetPlayerRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++;
                            if (walker.currentState == EnemySlowWalker.State.Notice) { enemyPv.RPC("NoticeRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyThinMan
                    EnemyThinMan thinMan = enemy.GetComponent<EnemyThinMan>();
                    if (thinMan != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, thinMan.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(thinMan, ReflectionCache.EnemyThinMan_PlayerTargetField, "EnemyThinMan");
                        if (target?.photonView != null) { enemyPv.RPC("SetTargetRPC", targetPlayer, target.photonView.ViewID, true); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyTumbler
                    EnemyTumbler tumbler = enemy.GetComponent<EnemyTumbler>();
                    if (tumbler != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, tumbler.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(tumbler, ReflectionCache.EnemyTumbler_TargetPlayerField, "EnemyTumbler");
                        if (target?.photonView != null) { enemyPv.RPC("TargetPlayerRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++; }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyUpscream
                    EnemyUpscream upscream = enemy.GetComponent<EnemyUpscream>();
                    if (upscream != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, upscream.currentState);
                        // Corrected: Use GameUtilities & ReflectionCache
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(upscream, ReflectionCache.EnemyUpscream_TargetPlayerField, "EnemyUpscream");
                        if (target?.photonView != null)
                        {
                            enemyPv.RPC("TargetPlayerRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++;
                            if (upscream.currentState == EnemyUpscream.State.PlayerNotice || upscream.currentState == EnemyUpscream.State.GoToPlayer || upscream.currentState == EnemyUpscream.State.Attack)
                            {
                                enemyPv.RPC("NoticeSetRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++;
                            }
                        }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    // EnemyValuableThrower
                    EnemyValuableThrower thrower = enemy.GetComponent<EnemyValuableThrower>();
                    if (thrower != null)
                    {
                        enemyPv.RPC("UpdateStateRPC", targetPlayer, thrower.currentState);
                        PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(thrower, AccessTools.Field(typeof(EnemyValuableThrower), "playerTarget"), "EnemyValuableThrower");
                        if (target?.photonView != null)
                        {
                            enemyPv.RPC("UpdatePlayerTargetRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++;
                            if (thrower.currentState == EnemyValuableThrower.State.PlayerNotice || thrower.currentState == EnemyValuableThrower.State.GetValuable || thrower.currentState == EnemyValuableThrower.State.GoToTarget)
                            {
                                enemyPv.RPC("NoticeRPC", targetPlayer, target.photonView.ViewID); otherStateSyncedCount++;
                            }
                        }
                        specificStateSyncedCount++; specificStateSynced = true;
                    }

                    if (!specificStateSynced) { /* Logging */ }
                    if (enemy.FreezeTimer > 0f) { enemyPv.RPC("FreezeRPC", targetPlayer, enemy.FreezeTimer); freezeSyncedCount++; }
                }
                else
                {
                    parentPv.RPC("DespawnRPC", targetPlayer); despawnRpcSentCount++;
                }
            }
            catch (Exception ex) { LatePlugin.Log.LogError($"[EnemySyncManager] CRITICAL error processing enemy '{enemyName}': {ex}"); }
        }
        LatePlugin.Log.LogInfo($"[EnemySyncManager] === Finished FULL enemy state sync for {nick}. Processed: {processedCount}, SpawnRPCs: {spawnRpcSentCount}, DespawnRPCs: {despawnRpcSentCount}, SpecificStates: {specificStateSyncedCount}, Freezes: {freezeSyncedCount}, OtherStates: {otherStateSyncedCount} ===");
    }
    #endregion
}