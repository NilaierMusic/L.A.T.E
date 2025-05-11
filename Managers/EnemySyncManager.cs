// File: L.A.T.E/Managers/EnemySyncManager.cs
using HarmonyLib; // For AccessTools if any enemy-specific field reflection remains (prefer ReflectionCache)
using LATE.Core;
using LATE.Utilities;
using Photon.Pun;
using Photon.Realtime;
using System.Reflection; // For FieldInfo if used directly (prefer ReflectionCache)
using UnityEngine;
using Object = UnityEngine.Object; // Alias for UnityEngine.Object

namespace LATE.Managers;

/// <summary>
/// Handles enemy synchronization for late-joining players. This includes notifying active enemies
/// of players joining or leaving the game, and performing a detailed state synchronization
/// for each enemy type to ensure late joiners see enemies in their correct states.
/// </summary>
internal static class EnemySyncManager
{
    private static readonly BepInEx.Logging.ManualLogSource Log = LatePlugin.Log;

    #region Helper Methods
    /// <summary>
    /// Checks if the local client is the authoritative Master Client.
    /// </summary>
    /// <returns>True if the local client is the Master Client; otherwise, false.</returns>
    private static bool IsMasterClient() => PhotonUtilities.IsRealMasterClient();

    /// <summary>
    /// Iterates over all active <see cref="Enemy"/> instances in the scene and executes a given action.
    /// Active enemies are those found by <see cref="Object.FindObjectsOfType{T}()"/>.
    /// </summary>
    /// <param name="action">The action to perform for each enemy and its associated PhotonView.</param>
    private static void ForEachActiveEnemy(Action<Enemy, PhotonView> action)
    {
        Enemy[] enemies = Object.FindObjectsOfType<Enemy>();
        if (enemies == null || enemies.Length == 0)
        {
            Log.LogDebug("[EnemySyncManager] ForEachActiveEnemy: No active enemies found.");
            return;
        }

        Log.LogDebug($"[EnemySyncManager] ForEachActiveEnemy: Found {enemies.Length} active enemies.");

        foreach (Enemy enemy in enemies)
        {
            if (enemy == null || enemy.gameObject == null) continue;

            PhotonView? enemyPv = null;
            if (ReflectionCache.Enemy_PhotonViewField != null)
            {
                try
                {
                    enemyPv = ReflectionCache.Enemy_PhotonViewField.GetValue(enemy) as PhotonView;
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[EnemySyncManager] ForEachActiveEnemy: Error reflecting Enemy.PhotonView for '{enemy.gameObject.name}': {ex.Message}");
                }
            }

            if (enemyPv == null) // Fallback if reflection failed or field is null
            {
                enemyPv = enemy.GetComponent<PhotonView>();
            }

            if (enemyPv == null)
            {
                Log.LogWarning($"[EnemySyncManager] ForEachActiveEnemy: Could not get PhotonView for active enemy '{enemy.gameObject.name}'. Skipping action.");
                continue;
            }

            try
            {
                action(enemy, enemyPv);
            }
            catch (Exception ex)
            {
                Log.LogError($"[EnemySyncManager] ForEachActiveEnemy: Action failed on '{enemy.gameObject?.name ?? "NULL_GAMEOBJECT"}' (ViewID: {enemyPv.ViewID}): {ex}");
            }
        }
    }
    #endregion

    #region Player Join/Leave Notifications
    /// <summary>
    /// Notifies all active enemies that a new player has joined the game by calling the enemy's <c>PlayerAdded</c> method.
    /// This method should only be called by the MasterClient.
    /// </summary>
    /// <param name="newPlayer">The <see cref="Player"/> who joined.</param>
    /// <param name="newPlayerAvatar">The <see cref="PlayerAvatar"/> instance for the new player.</param>
    public static void NotifyEnemiesOfNewPlayer(Player newPlayer, PlayerAvatar newPlayerAvatar)
    {
        if (newPlayer == null)
        {
            Log.LogWarning("[EnemySyncManager] NotifyEnemiesOfNewPlayer: newPlayer is null. Aborting.");
            return;
        }
        if (!IsMasterClient())
        {
            Log.LogDebug("[EnemySyncManager] NotifyEnemiesOfNewPlayer: Not MasterClient. Skipping.");
            return;
        }
        if (newPlayerAvatar == null)
        {
            Log.LogWarning($"[EnemySyncManager] NotifyEnemiesOfNewPlayer: newPlayerAvatar for {newPlayer.NickName} is null. Aborting.");
            return;
        }

        PhotonView? avatarPv = PhotonUtilities.GetPhotonView(newPlayerAvatar);
        if (avatarPv == null)
        {
            Log.LogWarning($"[EnemySyncManager] NotifyEnemiesOfNewPlayer: PhotonView for {newPlayer.NickName}'s avatar is null. Aborting.");
            return;
        }

        int avatarViewId = avatarPv.ViewID;
        string newPlayerNickname = newPlayer.NickName ?? $"ActorNr {newPlayer.ActorNumber}";
        Log.LogInfo($"[EnemySyncManager] Notifying active enemies about new player {newPlayerNickname} (AvatarViewID: {avatarViewId}).");
        int updatedCount = 0;

        ForEachActiveEnemy((enemy, enemyPv) =>
        {
            try
            {
                enemy.PlayerAdded(avatarViewId);
                updatedCount++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[EnemySyncManager] Error calling PlayerAdded on '{enemy.gameObject?.name ?? "NULL_GAMEOBJECT"}' for player {newPlayerNickname}: {ex.Message}");
            }
        });

        Log.LogInfo($"[EnemySyncManager] Finished notifying {updatedCount} active enemies about {newPlayerNickname} joining.");
    }

    /// <summary>
    /// Notifies all active enemies that a player has left the game by calling the enemy's <c>PlayerRemoved</c> method.
    /// This method should only be called by the MasterClient.
    /// </summary>
    /// <param name="leavingPlayer">The <see cref="Player"/> who left.</param>
    public static void NotifyEnemiesOfLeavingPlayer(Player leavingPlayer)
    {
        if (leavingPlayer == null)
        {
            Log.LogWarning("[EnemySyncManager] NotifyEnemiesOfLeavingPlayer: leavingPlayer is null. Aborting.");
            return;
        }
        if (!IsMasterClient())
        {
            Log.LogDebug("[EnemySyncManager] NotifyEnemiesOfLeavingPlayer: Not MasterClient. Skipping.");
            return;
        }

        PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(leavingPlayer);
        if (avatar == null)
        {
            Log.LogWarning($"[EnemySyncManager] NotifyEnemiesOfLeavingPlayer: Could not find PlayerAvatar for {leavingPlayer.NickName}. Aborting.");
            return;
        }

        PhotonView? avatarPv = PhotonUtilities.GetPhotonView(avatar);
        if (avatarPv == null)
        {
            Log.LogWarning($"[EnemySyncManager] NotifyEnemiesOfLeavingPlayer: PhotonView for {leavingPlayer.NickName}'s avatar is null. Aborting.");
            return;
        }

        int avatarViewId = avatarPv.ViewID;
        string leavingPlayerNickname = leavingPlayer.NickName ?? $"ActorNr {leavingPlayer.ActorNumber}";
        Log.LogInfo($"[EnemySyncManager] Notifying active enemies that {leavingPlayerNickname} (AvatarViewID: {avatarViewId}) left.");
        int updatedCount = 0;

        ForEachActiveEnemy((enemy, enemyPv) =>
        {
            try
            {
                enemy.PlayerRemoved(avatarViewId);
                updatedCount++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[EnemySyncManager] Error calling PlayerRemoved on '{enemy.gameObject?.name ?? "NULL_GAMEOBJECT"}' for player {leavingPlayerNickname}: {ex.Message}");
            }
        });
        Log.LogInfo($"[EnemySyncManager] Finished notifying {updatedCount} active enemies about {leavingPlayerNickname} leaving.");
    }
    #endregion

    #region Full Enemy State Synchronization for Late Joiner
    /// <summary>
    /// Synchronizes the state of all enemies (including inactive ones that might become active)
    /// to a late-joining player. This is a comprehensive sync covering spawn state and
    /// type-specific properties. This method should only be called by the MasterClient.
    /// </summary>
    /// <param name="targetPlayer">The late-joining <see cref="Player"/> to synchronize states to.</param>
    public static void SyncAllEnemyStatesForPlayer(Player targetPlayer)
    {
        if (targetPlayer == null)
        {
            Log.LogError("[EnemySyncManager] SyncAllEnemyStatesForPlayer: targetPlayer is null. Aborting.");
            return;
        }
        if (!IsMasterClient())
        {
            Log.LogDebug("[EnemySyncManager] SyncAllEnemyStatesForPlayer: Not MasterClient. Skipping.");
            return;
        }

        if (ReflectionCache.Enemy_EnemyParentField == null || ReflectionCache.EnemyParent_SpawnedField == null)
        {
            Log.LogError("[EnemySyncManager] SyncAllEnemyStatesForPlayer: Critical reflection fields (Enemy_EnemyParentField or EnemyParent_SpawnedField) missing from ReflectionCache. Aborting.");
            return;
        }

        string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        Log.LogInfo($"[EnemySyncManager] === Starting FULL enemy state sync for {targetNickname} ===");

        int processedCount = 0, spawnRpcSentCount = 0, despawnRpcSentCount = 0;
        int specificStateSyncedCount = 0, freezeSyncedCount = 0, otherStateSyncedCount = 0;

        Enemy[] allEnemiesInScene = Object.FindObjectsOfType<Enemy>(true); // Include inactive enemies

        foreach (Enemy enemy in allEnemiesInScene)
        {
            processedCount++;
            if (enemy == null || enemy.gameObject == null) continue;

            string enemyName = enemy.gameObject.name;
            EnemyParent? enemyParent = null;
            PhotonView? parentPhotonView = null;

            // Get EnemyParent and its PhotonView
            try
            {
                enemyParent = ReflectionCache.Enemy_EnemyParentField.GetValue(enemy) as EnemyParent;
                if (enemyParent == null) enemyParent = enemy.GetComponentInParent<EnemyParent>(); // Fallback

                if (enemyParent == null)
                {
                    Log.LogDebug($"[EnemySyncManager] Enemy '{enemyName}' has no EnemyParent component. Skipping basic spawn sync for it.");
                    continue;
                }
                parentPhotonView = enemyParent.GetComponent<PhotonView>();
                if (parentPhotonView == null)
                {
                    Log.LogWarning($"[EnemySyncManager] EnemyParent for '{enemyName}' is missing a PhotonView. Skipping basic spawn sync.");
                    continue;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[EnemySyncManager] Error obtaining EnemyParent or its PhotonView for '{enemyName}': {ex}");
                continue;
            }

            // Get Enemy's own PhotonView (for detailed state sync)
            PhotonView? enemyPhotonView = null;
            if (ReflectionCache.Enemy_PhotonViewField != null)
            {
                try { enemyPhotonView = ReflectionCache.Enemy_PhotonViewField.GetValue(enemy) as PhotonView; }
                catch (Exception ex) { Log.LogWarning($"[EnemySyncManager] Error reflecting Enemy.PhotonView for '{enemyName}': {ex.Message}"); }
            }
            if (enemyPhotonView == null)
            {
                enemyPhotonView = enemy.GetComponent<PhotonView>();
            }

            // Sync basic spawn state (SpawnRPC or DespawnRPC)
            try
            {
                bool hostIsSpawned = ReflectionCache.EnemyParent_SpawnedField.GetValue(enemyParent) as bool? ?? false;

                if (hostIsSpawned)
                {
                    parentPhotonView.RPC("SpawnRPC", targetPlayer);
                    spawnRpcSentCount++;

                    if (enemyPhotonView != null)
                    {
                        SyncSpecificEnemyTypeState(enemy, enemyPhotonView, targetPlayer, ref specificStateSyncedCount, ref otherStateSyncedCount);
                        if (enemy.FreezeTimer > 0f)
                        {
                            enemyPhotonView.RPC("FreezeRPC", targetPlayer, enemy.FreezeTimer);
                            freezeSyncedCount++;
                        }
                    }
                    else
                    {
                        Log.LogDebug($"[EnemySyncManager] Enemy '{enemyName}' (parent ViewID: {parentPhotonView.ViewID}) is spawned, but its own PhotonView is missing. Skipping detailed state sync.");
                    }
                }
                else
                {
                    parentPhotonView.RPC("DespawnRPC", targetPlayer);
                    despawnRpcSentCount++;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[EnemySyncManager] CRITICAL error processing spawn/despawn or detailed state for enemy '{enemyName}' (Parent PV: {parentPhotonView?.ViewID}): {ex}");
            }
        }
        Log.LogInfo($"[EnemySyncManager] === Finished FULL enemy state sync for {targetNickname}. " +
                      $"Processed: {processedCount}, SpawnRPCs: {spawnRpcSentCount}, DespawnRPCs: {despawnRpcSentCount}, " +
                      $"SpecificTypeStates: {specificStateSyncedCount}, Freezes: {freezeSyncedCount}, OtherSubStates: {otherStateSyncedCount} ===");
    }

    /// <summary>
    /// Handles the synchronization of states specific to different enemy controller types.
    /// This is a helper method for <see cref="SyncAllEnemyStatesForPlayer"/>.
    /// </summary>
    private static void SyncSpecificEnemyTypeState(Enemy enemy, PhotonView enemyPhotonView, Player targetPlayer, ref int specificTypeStateCount, ref int otherSubStateCount)
    {
        bool specificHandlerApplied = false;

        // Helper to send RPC and increment counts, with error handling.
        void TrySendRPC(string rpcName, params object[] parameters)
        {
            try
            {
                enemyPhotonView.RPC(rpcName, targetPlayer, parameters);
            }
            catch (Exception ex)
            {
                Log.LogError($"[EnemySyncManager] RPC Error for '{enemy.name}' -> '{rpcName}' for player '{targetPlayer.NickName}': {ex}");
            }
        }

        // --- Individual Enemy Type Checks and Sync ---
        if (enemy.GetComponent<EnemyAnimal>() is { } animal)
        { TrySendRPC("UpdateStateRPC", animal.currentState); specificTypeStateCount++; specificHandlerApplied = true; }

        if (enemy.GetComponent<EnemyBang>() is { } bang)
        {
            TrySendRPC("UpdateStateRPC", bang.currentState);
            FieldInfo? fuseActiveField = AccessTools.Field(typeof(EnemyBang), "fuseActive");
            FieldInfo? fuseLerpField = AccessTools.Field(typeof(EnemyBang), "fuseLerp");
            if (fuseActiveField != null && fuseLerpField != null)
            {
                try
                {
                    bool fuseActive = fuseActiveField.GetValue(bang) as bool? ?? false;
                    float fuseLerp = fuseLerpField.GetValue(bang) as float? ?? 0f;
                    TrySendRPC("FuseRPC", fuseActive, fuseLerp);
                    otherSubStateCount++;
                }
                catch (Exception ex) { Log.LogError($"[EnemySyncManager] Error reflecting/sending EnemyBang Fuse state: {ex}"); }
            }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyBeamer>() is { } beamer)
        {
            TrySendRPC("UpdateStateRPC", beamer.currentState);
            FieldInfo? moveFastField = AccessTools.Field(typeof(EnemyBeamer), "moveFast");
            if (moveFastField != null)
            { try { TrySendRPC("MoveFastRPC", moveFastField.GetValue(beamer) as bool? ?? false); otherSubStateCount++; } catch (Exception ex) { Log.LogError($"[EnemySyncManager] Error reflecting/sending EnemyBeamer MoveFast state: {ex}"); } }
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(beamer, ReflectionCache.EnemyBeamer_PlayerTargetField, "EnemyBeamer");
            if (target?.photonView != null) { TrySendRPC("UpdatePlayerTargetRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyBowtie>() is { } bowtie)
        {
            TrySendRPC("UpdateStateRPC", bowtie.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(bowtie, AccessTools.Field(typeof(EnemyBowtie), "playerTarget"), "EnemyBowtie"); // Assuming playerTarget is the correct field name.
            if (target?.photonView != null) { TrySendRPC("NoticeRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyCeilingEye>() is { } ceilingEye)
        {
            TrySendRPC("UpdateStateRPC", ceilingEye.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(ceilingEye, ReflectionCache.EnemyCeilingEye_TargetPlayerField, "EnemyCeilingEye");
            if (target?.photonView != null) { TrySendRPC("TargetPlayerRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyDuck>() is { } duck)
        {
            TrySendRPC("UpdateStateRPC", duck.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(duck, AccessTools.Field(typeof(EnemyDuck), "playerTarget"), "EnemyDuck");
            if (target?.photonView != null) { TrySendRPC("UpdatePlayerTargetRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyFloater>() is { } floater)
        {
            TrySendRPC("UpdateStateRPC", floater.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(floater, ReflectionCache.EnemyFloater_TargetPlayerField, "EnemyFloater");
            if (target?.photonView != null)
            {
                TrySendRPC("TargetPlayerRPC", target.photonView.ViewID); otherSubStateCount++;
                if (floater.currentState != EnemyFloater.State.Attack &&
                    floater.currentState != EnemyFloater.State.ChargeAttack &&
                    floater.currentState != EnemyFloater.State.DelayAttack)
                {
                    TrySendRPC("NoticeRPC", target.photonView.ViewID); otherSubStateCount++;
                }
            }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyGnome>() is { } gnome)
        { TrySendRPC("UpdateStateRPC", gnome.currentState); specificTypeStateCount++; specificHandlerApplied = true; }

        if (enemy.GetComponent<EnemyHidden>() is { } hidden)
        {
            TrySendRPC("UpdateStateRPC", hidden.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(hidden, AccessTools.Field(typeof(EnemyHidden), "playerTarget"), "EnemyHidden");
            if (target?.photonView != null) { TrySendRPC("UpdatePlayerTargetRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyHunter>() is { } hunter)
        {
            TrySendRPC("UpdateStateRPC", hunter.currentState);
            FieldInfo? invPointField = AccessTools.Field(typeof(EnemyHunter), "investigatePoint");
            if (invPointField != null && (hunter.currentState == EnemyHunter.State.Investigate || hunter.currentState == EnemyHunter.State.Aim))
            { try { TrySendRPC("UpdateInvestigationPoint", (Vector3)invPointField.GetValue(hunter)); otherSubStateCount++; } catch (Exception ex) { Log.LogError($"[EnemySyncManager] Error reflecting/sending EnemyHunter InvestigatePoint: {ex}"); } }
            FieldInfo? moveFastField = AccessTools.Field(typeof(EnemyHunter), "moveFast");
            if (moveFastField != null)
            { try { TrySendRPC("MoveFastRPC", moveFastField.GetValue(hunter) as bool? ?? false); otherSubStateCount++; } catch (Exception ex) { Log.LogError($"[EnemySyncManager] Error reflecting/sending EnemyHunter MoveFast: {ex}"); } }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyRobe>() is { } robe)
        {
            TrySendRPC("UpdateStateRPC", robe.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(robe, ReflectionCache.EnemyRobe_TargetPlayerField, "EnemyRobe");
            if (target?.photonView != null) { TrySendRPC("TargetPlayerRPC", target.photonView.ViewID); otherSubStateCount++; }
            FieldInfo? isOnScreenField = AccessTools.Field(typeof(EnemyRobe), "isOnScreen");
            if (isOnScreenField != null)
            { try { TrySendRPC("UpdateOnScreenRPC", isOnScreenField.GetValue(robe) as bool? ?? false); otherSubStateCount++; } catch (Exception ex) { Log.LogError($"[EnemySyncManager] Error reflecting/sending EnemyRobe IsOnScreen: {ex}"); } }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyRunner>() is { } runner)
        {
            TrySendRPC("UpdateStateRPC", runner.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(runner, ReflectionCache.EnemyRunner_TargetPlayerField, "EnemyRunner");
            if (target?.photonView != null) { TrySendRPC("UpdatePlayerTargetRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemySlowMouth>() is { } mouth)
        {
            TrySendRPC("UpdateStateRPC", mouth.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(mouth, AccessTools.Field(typeof(EnemySlowMouth), "playerTarget"), "EnemySlowMouth");
            if (target?.photonView != null) { TrySendRPC("UpdatePlayerTargetRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemySlowWalker>() is { } walker)
        {
            TrySendRPC("UpdateStateRPC", walker.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(walker, ReflectionCache.EnemySlowWalker_TargetPlayerField, "EnemySlowWalker");
            if (target?.photonView != null)
            {
                TrySendRPC("TargetPlayerRPC", target.photonView.ViewID); otherSubStateCount++;
                if (walker.currentState == EnemySlowWalker.State.Notice)
                {
                    TrySendRPC("NoticeRPC", target.photonView.ViewID); otherSubStateCount++;
                }
            }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyThinMan>() is { } thinMan)
        {
            TrySendRPC("UpdateStateRPC", thinMan.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(thinMan, ReflectionCache.EnemyThinMan_PlayerTargetField, "EnemyThinMan");
            if (target?.photonView != null) { TrySendRPC("SetTargetRPC", target.photonView.ViewID, true); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyTumbler>() is { } tumbler)
        {
            TrySendRPC("UpdateStateRPC", tumbler.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(tumbler, ReflectionCache.EnemyTumbler_TargetPlayerField, "EnemyTumbler");
            if (target?.photonView != null) { TrySendRPC("TargetPlayerRPC", target.photonView.ViewID); otherSubStateCount++; }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyUpscream>() is { } upscream)
        {
            TrySendRPC("UpdateStateRPC", upscream.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(upscream, ReflectionCache.EnemyUpscream_TargetPlayerField, "EnemyUpscream");
            if (target?.photonView != null)
            {
                TrySendRPC("TargetPlayerRPC", target.photonView.ViewID); otherSubStateCount++;
                if (upscream.currentState == EnemyUpscream.State.PlayerNotice ||
                    upscream.currentState == EnemyUpscream.State.GoToPlayer ||
                    upscream.currentState == EnemyUpscream.State.Attack)
                {
                    TrySendRPC("NoticeSetRPC", target.photonView.ViewID); otherSubStateCount++;
                }
            }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (enemy.GetComponent<EnemyValuableThrower>() is { } thrower)
        {
            TrySendRPC("UpdateStateRPC", thrower.currentState);
            PlayerAvatar? target = GameUtilities.GetInternalPlayerTarget(thrower, AccessTools.Field(typeof(EnemyValuableThrower), "playerTarget"), "EnemyValuableThrower");
            if (target?.photonView != null)
            {
                TrySendRPC("UpdatePlayerTargetRPC", target.photonView.ViewID); otherSubStateCount++;
                if (thrower.currentState == EnemyValuableThrower.State.PlayerNotice ||
                    thrower.currentState == EnemyValuableThrower.State.GetValuable ||
                    thrower.currentState == EnemyValuableThrower.State.GoToTarget)
                {
                    TrySendRPC("NoticeRPC", target.photonView.ViewID); otherSubStateCount++;
                }
            }
            specificTypeStateCount++; specificHandlerApplied = true;
        }

        if (!specificHandlerApplied)
        {
            Log.LogDebug($"[EnemySyncManager] No specific state sync logic found or applied for enemy type: {enemy.GetType().Name} (Name: {enemy.name}).");
        }
    }
    #endregion
}