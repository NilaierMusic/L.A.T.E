// File: L.A.T.E/Patches/Player/PlayerAvatarPatches.cs
using HarmonyLib;
using LATE.Config;
using LATE.Core;
using LATE.DataModels;
using LATE.Managers;
using LATE.Utilities;
using Photon.Pun;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LATE.Patches.Player; // File-scoped namespace

/// <summary>
/// Contains Harmony patches and MonoMod hooks for the PlayerAvatar class.
/// </summary>
[HarmonyPatch]
internal static class PlayerAvatarPatches
{
    internal static readonly HashSet<int> spawnPositionAssigned = new HashSet<int>();
    internal static bool _reloadHasBeenTriggeredThisScene = false;

    #region PlayerAvatar Patches

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAvatar), "Update")] // Or "LateUpdate" might be even safer
    public static void PlayerAvatar_Update_Postfix_VoiceManager(PlayerAvatar __instance)
    {
        // No need to check GameUtilities.IsModLogicActive() here, VoiceManager methods do their own checks.
        if (PhotonUtilities.IsRealMasterClient() && __instance != null)
        {
            VoiceManager.Host_TrySendInitialAvatarVoiceRpc(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), "SpawnRPC")]
    [HarmonyPrefix]
    public static void PlayerAvatar_SpawnRPC_Prefix(PlayerAvatar __instance, ref Vector3 position, ref Quaternion rotation, PhotonMessageInfo _info)
    {
        if (!PhotonNetwork.IsMasterClient || !ConfigManager.SpawnAtLastPosition.Value)
        {
            return;
        }

        Photon.Realtime.Player? targetPlayer = __instance.photonView?.Owner;
        if (targetPlayer == null)
        {
            LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnRPC_Prefix] Target player (owner of PhotonViewID {__instance.photonView?.ViewID}) is null. Cannot apply last position.");
            return;
        }

        // Defensive check for RPC sender, though primarily MC should be initiating modifications.
        if (_info.Sender != null && !_info.Sender.IsMasterClient)
        {
            // This case indicates a non-master client somehow sent an RPC that this prefix is intercepting.
            // The MasterClient should not act upon it to change spawn positions.
            // However, the critical logic here is that only the MasterClient *modifies* ref parameters.
            // So, this check is more of a log/awareness point. The `!PhotonNetwork.IsMasterClient` above is the main guard.
        }

        string targetPlayerName = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        bool positionOverriddenByMod = false;
        bool triedLastKnownPosition = false; // To track if we attempted to use a stored position

        if (targetPlayer.IsMasterClient)
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.SpawnRPC_Prefix] Target player '{targetPlayerName}' is the Host. L.A.T.E. will not modify their spawn parameters from the original RPC call.");
            // positionOverriddenByMod will remain false, original position & rotation will be used.
            // No need to `return` here, just ensure positionOverriddenByMod isn't set true for host.
            // The existing logic below will effectively skip if positionOverriddenByMod is false
            // and triedLastKnownPosition is also false (as it would be for a host's initial spawn).
        }
        else // Only apply L.A.T.E.'s spawn logic for non-host players
        {
            if (PlayerPositionManager.TryGetLastTransform(targetPlayer, out PlayerTransformData lastTransformData))
            {
                triedLastKnownPosition = true;
                LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnRPC_Prefix] Found last transform for '{targetPlayerName}' at {lastTransformData.Position}. IsDeathHead: {lastTransformData.IsDeathHeadPosition}");
                if (IsSpawnPositionValid(lastTransformData.Position, targetPlayer, selfRadius: 0.5f, selfHeight: 1.8f))
                {
                    LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnRPC_Prefix] Applying last known VALID position for '{targetPlayerName}': {lastTransformData.Position}");
                    position = lastTransformData.Position;
                    rotation = lastTransformData.Rotation;
                    positionOverriddenByMod = true;
                }
                else
                {
                    LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.SpawnRPC_Prefix] Last position for '{targetPlayerName}' ({lastTransformData.Position}) was invalid. Will consider alternatives.");
                }
            }

            if (!positionOverriddenByMod)
            {
                bool isLateJoiner = LateJoinManager.IsPlayerAnActiveLateJoiner(targetPlayer.ActorNumber);

                // Try to use a truck spawn point if:
                // 1. The player is an active late-joiner (and didn't get a valid last known position).
                // 2. The player is NOT a late-joiner BUT we had prior (invalid) position data for them (triedLastKnownPosition = true),
                //    suggesting they are rejoining or respawning and their previous spot was bad.
                if (isLateJoiner || triedLastKnownPosition)
                {
                    string reason = isLateJoiner ? "Late joiner" : "Returning player with invalid prior/no valid spot";
                    LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnRPC_Prefix] {reason}. Attempting safe truck spawn for '{targetPlayerName}'.");
                    if (TryFindSafeTruckSpawnPoint(__instance, out Vector3 truckSpawnPos, out Quaternion truckSpawnRot))
                    {
                        LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnRPC_Prefix] Assigning '{targetPlayerName}' to safe truck SP: {truckSpawnPos}");
                        position = truckSpawnPos;
                        rotation = truckSpawnRot;
                        positionOverriddenByMod = true;
                    }
                    else
                    {
                        LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.SpawnRPC_Prefix] No safe truck SP found for '{targetPlayerName}'. Using game's original requested: {position}");
                        // positionOverriddenByMod remains false; game's original parameters will be used.
                    }
                }
                else // This is a non-late-joiner, AND they have NO prior L.A.T.E. position data (e.g., initial level spawn for everyone).
                {
                    LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnRPC_Prefix] Player '{targetPlayerName}' is not a late joiner and has no prior L.A.T.E. position data. Using game's originally requested spawn parameters: Pos={position}, RotEuler={rotation.eulerAngles}");
                    // positionOverriddenByMod remains false; game's original parameters will be used.
                }
            }
        }

        if (positionOverriddenByMod)
        {
            spawnPositionAssigned.Add(__instance.photonView.ViewID);
        }
        LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnRPC_Prefix] Final spawn for '{targetPlayerName}' (ViewID {__instance.photonView.ViewID}) will be Pos:{position}, Rot:{rotation.eulerAngles}. OverriddenByMod: {positionOverriddenByMod}");
    }

    // Placeholder - Implement this thoroughly!
    private static bool IsSpawnPositionValid(Vector3 proposedPosition, Photon.Realtime.Player forPlayer, float selfRadius, float selfHeight)
    {
        // 1. NavMesh Check (if applicable and NavMesh class is accessible)
        if (UnityEngine.AI.NavMesh.SamplePosition(proposedPosition, out UnityEngine.AI.NavMeshHit navHit, 1.0f, UnityEngine.AI.NavMesh.AllAreas))
        {
            // Check if the sampled position is close enough to the proposed position
            if (Vector3.Distance(proposedPosition, navHit.position) > 0.5f) // Allow some tolerance
            {
                LatePlugin.Log.LogWarning($"[SpawnValidation] Proposed position {proposedPosition} for {forPlayer.NickName} is too far from NavMesh. Sampled: {navHit.position}");
                // return false; // Could be too strict if player can spawn slightly off-mesh
            }
        }
        else
        {
            LatePlugin.Log.LogWarning($"[SpawnValidation] Proposed position {proposedPosition} for {forPlayer.NickName} is not on a NavMesh.");
            // return false; // Depends on game if this is a hard requirement
        }

        // 2. Obstruction Check (simple OverlapCapsule)
        // LayerMask should target relevant colliders (environment, other players, enemies, items)
        // Avoid layers that the player itself is on if you don't want self-collision checks here
        int layerMask = LayerMask.GetMask("Default", "PlayerOnlyCollision", "EnemyOnlyCollision", "Level", "Phys Grab Object"); // Example layers

        Vector3 point1 = proposedPosition + Vector3.up * selfRadius; // Bottom of capsule
        Vector3 point2 = proposedPosition + Vector3.up * (selfHeight - selfRadius); // Top of capsule

        Collider[] colliders = Physics.OverlapCapsule(point1, point2, selfRadius, layerMask, QueryTriggerInteraction.Ignore);

        foreach (Collider col in colliders)
        {
            // Ignore collision with the player's own avatar if it's somehow already there and collidable
            PlayerAvatar? avatarComponent = col.GetComponentInParent<PlayerAvatar>();
            if (avatarComponent != null && avatarComponent.photonView?.Owner == forPlayer)
            {
                continue;
            }

            LatePlugin.Log.LogWarning($"[SpawnValidation] Proposed position {proposedPosition} for {forPlayer.NickName} obstructed by '{col.gameObject.name}' (Layer: {LayerMask.LayerToName(col.gameObject.layer)}).");
            return false;
        }

        // 3. Bounds Check (Example: simple Y coordinate check)
        if (proposedPosition.y < -50 || proposedPosition.y > 2000) // Adjust these values to your game's typical level bounds
        {
            LatePlugin.Log.LogWarning($"[SpawnValidation] Proposed position {proposedPosition} for {forPlayer.NickName} is out of Y-bounds.");
            return false;
        }

        // 4. Ceiling Check (Raycast down to ensure there's ground, not spawning inside a ceiling)
        if (Physics.Raycast(proposedPosition + Vector3.up * (selfHeight + 0.1f), Vector3.down, out RaycastHit groundHit, selfHeight + 0.5f, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (groundHit.point.y > proposedPosition.y + 0.2f) // If ground is significantly above feet
            {
                LatePlugin.Log.LogWarning($"[SpawnValidation] Proposed position {proposedPosition} for {forPlayer.NickName} seems to be under a low ceiling or inside geometry. Ground hit at {groundHit.point.y}");
                // return false; // This might be too aggressive depending on how precise spawn points are.
            }
        }
        else
        {
            LatePlugin.Log.LogWarning($"[SpawnValidation] Raycast down from {proposedPosition} for {forPlayer.NickName} found no ground nearby.");
            // return false; // No ground beneath
        }


        LatePlugin.Log.LogInfo($"[SpawnValidation] Position {proposedPosition} for {forPlayer.NickName} is considered VALID.");
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAvatar), "PlayerDeathRPC")]
    static void PlayerAvatar_PlayerDeathRPC_Postfix(PlayerAvatar __instance, int enemyIndex)
    {
        if (PhotonUtilities.IsRealMasterClient() && __instance != null && __instance.photonView != null)
        {
            Photon.Realtime.Player? owner = __instance.photonView.Owner;
            if (owner != null)
            {
                LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] Player {owner.NickName} died. Marking as DEAD (EnemyIdx: {enemyIndex}) in PlayerStateManager.");
                PlayerStateManager.MarkPlayerDead(owner, enemyIndex);

                PlayerAvatarCollision? collisionComponent = null;
                if (ReflectionCache.PlayerAvatar_PlayerAvatarCollisionField != null)
                {
                    try
                    {
                        // Attempt to get the PlayerAvatarCollision component using the reflected FieldInfo
                        collisionComponent = ReflectionCache.PlayerAvatar_PlayerAvatarCollisionField.GetValue(__instance) as PlayerAvatarCollision;
                        if (collisionComponent == null && ReflectionCache.PlayerAvatar_PlayerAvatarCollisionField.GetValue(__instance) != null)
                        {
                            // Value was not null but couldn't be cast, which is odd if types match.
                            LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] PlayerAvatar.playerAvatarCollision reflected value was not null but could not be cast to PlayerAvatarCollision for {owner.NickName}. Type was: {ReflectionCache.PlayerAvatar_PlayerAvatarCollisionField.GetValue(__instance)?.GetType()}");
                        }
                        else if (collisionComponent == null)
                        {
                            // This means PlayerAvatar.playerAvatarCollision field was actually null on the instance
                            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] Reflected PlayerAvatar.playerAvatarCollision field was null on the instance for {owner.NickName}.");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        LatePlugin.Log.LogError($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] Error getting PlayerAvatarCollision component via reflection for {owner.NickName}: {ex}");
                        // collisionComponent remains null
                    }
                }
                else
                {
                    // This means AccessTools.Field couldn't find "playerAvatarCollision" when ReflectionCache was initialized.
                    LatePlugin.Log.LogError($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] ReflectionCache.PlayerAvatar_PlayerAvatarCollisionField is NULL. Reflection setup for this field failed. Cannot access PlayerAvatarCollision. Check game version compatibility.");
                    // collisionComponent remains null
                }

                if (collisionComponent != null)
                {
                    Vector3? deathPos = null; // Use nullable Vector3
                    if (ReflectionCache.PlayerAvatarCollision_DeathHeadPositionField != null)
                    {
                        try
                        {
                            object? posValue = ReflectionCache.PlayerAvatarCollision_DeathHeadPositionField.GetValue(collisionComponent);
                            if (posValue is Vector3 vectorPos)
                            {
                                deathPos = vectorPos;
                            }
                            else if (posValue != null)
                            {
                                LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] Reflected PlayerAvatarCollision.deathHeadPosition for {owner.NickName} was not a Vector3. Actual type: {posValue.GetType()}. Using fallback.");
                            }
                            else
                            {
                                LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] Reflected PlayerAvatarCollision.deathHeadPosition was null for {owner.NickName}. Using fallback.");
                            }
                        }
                        catch (System.Exception ex)
                        {
                            LatePlugin.Log.LogError($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] Error reflecting deathHeadPosition from PlayerAvatarCollision for {owner.NickName}: {ex}. Using fallback.");
                        }
                    }
                    else
                    {
                        LatePlugin.Log.LogError($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] ReflectionCache for PlayerAvatarCollision.deathHeadPosition is null. Cannot get death head position via reflection. Using fallback.");
                    }

                    if (deathPos.HasValue)
                    {
                        PlayerPositionManager.UpdatePlayerDeathPosition(
                            owner,
                            deathPos.Value,
                            __instance.localCameraTransform.rotation
                        );
                    }
                    else
                    {
                        LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] Could not get deathHeadPosition for {owner.NickName}. Falling back to PlayerAvatar's transform position.");
                        PlayerPositionManager.UpdatePlayerDeathPosition(
                           owner,
                           __instance.transform.position, // Fallback position
                           __instance.transform.rotation
                       );
                    }
                }
                else
                {
                    LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.PlayerDeathRPC_Postfix] PlayerAvatarCollision component is null for {owner.NickName}. Falling back to PlayerAvatar's transform for death position.");
                    PlayerPositionManager.UpdatePlayerDeathPosition(
                       owner,
                       __instance.transform.position, // Fallback position
                       __instance.transform.rotation
                   );
                }
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAvatar), "ReviveRPC")]
    static void PlayerAvatar_ReviveRPC_Postfix(PlayerAvatar __instance, bool _revivedByTruck)
    {
        if (PhotonUtilities.IsRealMasterClient() && __instance != null && __instance.photonView != null)
        {
            Photon.Realtime.Player? owner = __instance.photonView.Owner;
            if (owner != null)
            {
                LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.ReviveRPC_Postfix] Player {owner.NickName} revived. Marking as ALIVE in PlayerStateManager.");
                PlayerStateManager.MarkPlayerAlive(owner);
                VoiceManager.Host_OnPlayerRevived(__instance);

                if (ConfigManager.SpawnAtLastPosition.Value)
                {
                    // When revived, their "last position" for a future rejoin should be their revive spot,
                    // not their previous death spot.
                    LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.ReviveRPC_Postfix] Updating last known position for {owner.NickName} to revive spot: {__instance.transform.position}");
                    PlayerPositionManager.UpdatePlayerPosition(owner, __instance.transform.position, __instance.transform.rotation);
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.OnPhotonSerializeView))]
    [HarmonyPostfix]
    public static void PlayerAvatar_OnPhotonSerializeView_Postfix_TrackPosition(PlayerAvatar __instance, PhotonStream stream, PhotonMessageInfo info)
    {
        // ADD THIS CHECK
        if (!GameUtilities.IsModLogicActive()) return;

        if (PhotonNetwork.IsMasterClient && ConfigManager.SpawnAtLastPosition.Value && stream.IsReading)
        {
            if (__instance.photonView != null && !__instance.photonView.IsMine) // For remote players' avatars
            {
                Photon.Realtime.Player? owner = __instance.photonView.Owner;
                if (owner != null)
                {
                    // Change to use transform.position and transform.rotation
                    PlayerPositionManager.UpdatePlayerPosition(
                        owner,
                        __instance.transform.position,
                        __instance.transform.rotation
                    );
                }
            }
        }
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.LoadingLevelAnimationCompletedRPC))]
    static bool PlayerAvatar_LoadingLevelAnimationCompletedRPC_Prefix(PlayerAvatar __instance)
    {
        if (!PhotonUtilities.IsRealMasterClient())
            return true;

        PhotonView? pv = PhotonUtilities.GetPhotonView(__instance);
        if (pv == null || __instance == null)
        {
            LatePlugin.Log.LogError("[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Instance or PhotonView is null.");
            return true;
        }
        Photon.Realtime.Player sender = pv.Owner;
        if (sender == null)
        {
            LatePlugin.Log.LogError($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] PhotonView Owner is null for Avatar PV {pv.ViewID}.");
            return true;
        }
        if (sender.IsLocal) return true;

        int actorNr = sender.ActorNumber;
        string nickname = sender.NickName ?? $"ActorNr {actorNr}";

        if (_reloadHasBeenTriggeredThisScene)
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Ignoring LoadingCompleteRPC from {nickname}: Reload already triggered this scene.");
            return true;
        }

        // CRITICAL: Only proceed with L.A.T.E. specific voice sync if this player is identified as an active late joiner.
        if (LateJoinManager.IsPlayerAnActiveLateJoiner(actorNr))
        {
            LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Received LoadingLevelAnimationCompletedRPC from L.A.T.E. active late-joiner {nickname}.");

            if (!GameUtilities.IsModLogicActive() && !SemiFunc.RunIsLobby() && !SemiFunc.RunIsShop()) // Check if we are in a scene L.A.T.E. should operate in
            {
                LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Mod logic INACTIVE for current scene. Clearing L.A.T.E. tracking for {nickname} but not performing full L.A.T.E. syncs.");
                LateJoinManager.ClearPlayerTracking(actorNr);
                return true;
            }

            if (LateJoinManager.IsPlayerNeedingInitialSync(actorNr))
            {
                LateJoinManager.MarkInitialSyncTriggered(actorNr);
            }

            if (ConfigManager.ForceReloadOnLateJoin.Value)
            {
                LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] CONFIG: Forcing level reload for late-joiner {nickname}.");
                if (RunManager.instance != null)
                {
                    _reloadHasBeenTriggeredThisScene = true;
                    RunManager.instance.RestartScene();
                    return false; // Prevent original RPC if we are reloading
                }
                else
                {
                    LatePlugin.Log.LogError($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] FAILED TO FORCE RELOAD: RunManager.instance is null for {nickname}. Proceeding with L.A.T.E. sync.");
                    // Fall through to normal L.A.T.E. sync
                }
            }

            LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Initiating L.A.T.E. full sync (non-voice) for {nickname}.");
            LateJoinManager.SyncAllStateForPlayer(sender, __instance);

            // Schedule the specific voice sync for this late joiner with a delay.
            // 3.5 to 4 seconds might be safer to ensure client-side PlayerVoiceChat.TTSinstantiatedTimer (3s) has passed.
            VoiceManager.Host_ScheduleVoiceSyncForLateJoiner(sender, __instance, 3.5f);
        }
        else
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Received LoadingCompleteRPC from {nickname}, but they are NOT currently tracked as an active L.A.T.E. late-joiner. L.A.T.E. voice sync skipped.");
        }
        return true;
    }

    #endregion

    #region PlayerAvatar MonoMod Hooks

    private static readonly HashSet<int> spawnPositionCorrectedInStart = new HashSet<int>(); // Add this to your class

    public static bool TryFindSafeTruckSpawnPoint(PlayerAvatar forAvatar, out Vector3 spawnPos, out Quaternion spawnRot)
    {
        spawnPos = Vector3.zero;
        spawnRot = Quaternion.identity;

        if (!PhotonUtilities.IsRealMasterClient()) // Only master client should determine spawn points
        {
            LatePlugin.Log.LogWarning("[TryFindSafeTruckSpawnPoint] Not MasterClient, cannot determine spawn point.");
            return false;
        }

        List<SpawnPoint> allSpawnPoints = Object.FindObjectsOfType<SpawnPoint>()
            .Where(sp => sp != null && !sp.debug).ToList(); // Assuming 'debug' spawn points are not for players

        if (allSpawnPoints.Count == 0)
        {
            LatePlugin.Log.LogError("[TryFindSafeTruckSpawnPoint] No valid SpawnPoints found in the level.");
            return false;
        }

        List<PlayerAvatar> currentPlayers = GameDirector.instance?.PlayerList ?? new List<PlayerAvatar>();
        float minDistanceSq = ConfigManager.MinSpawnDistance.Value * ConfigManager.MinSpawnDistance.Value; // Make this configurable, e.g., 2.0f * 2.0f
        GameUtilities.Shuffle(allSpawnPoints); // Assuming you have a Shuffle utility

        foreach (SpawnPoint sp in allSpawnPoints)
        {
            bool blocked = false;
            Vector3 spPos = sp.transform.position + Vector3.up * 0.1f; // Slight upward offset

            foreach (PlayerAvatar playerAvatarInstance in currentPlayers)
            {
                if (playerAvatarInstance == null || playerAvatarInstance == forAvatar)
                    continue;

                if ((playerAvatarInstance.transform.position - spPos).sqrMagnitude < minDistanceSq)
                {
                    blocked = true;
                    break;
                }
            }

            if (!blocked)
            {
                spawnPos = spPos;
                spawnRot = sp.transform.rotation;
                LatePlugin.Log.LogInfo($"[TryFindSafeTruckSpawnPoint] Found available SP '{sp.name}' at {spawnPos} for {GameUtilities.GetPlayerNickname(forAvatar)}.");
                return true;
            }
        }

        LatePlugin.Log.LogWarning($"[TryFindSafeTruckSpawnPoint] All {allSpawnPoints.Count} SPs blocked or unavailable for {GameUtilities.GetPlayerNickname(forAvatar)}.");
        // Fallback: use the first spawn point even if "blocked", or a default truck position if you have one
        if (allSpawnPoints.Count > 0)
        {
            spawnPos = allSpawnPoints[0].transform.position + Vector3.up * 0.1f;
            spawnRot = allSpawnPoints[0].transform.rotation;
            LatePlugin.Log.LogWarning($"[TryFindSafeTruckSpawnPoint] Fallback: Using first SP '{allSpawnPoints[0].name}' at {spawnPos} as a last resort.");
            return true;
        }
        return false;
    }

    public static void PlayerAvatar_SpawnHook(
        Action<PlayerAvatar, Vector3, Quaternion> orig,
        PlayerAvatar self,
        Vector3 position,
        Quaternion rotation)
    {
        if (self == null)
        {
            LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] 'self' is null. Cannot proceed.");
            return;
        }

        PhotonView? pv = PhotonUtilities.GetPhotonView(self);
        string playerName = GameUtilities.GetPlayerNickname(self);

        bool alreadySpawned = false;
        if (ReflectionCache.PlayerAvatar_SpawnedField != null)
        {
            try
            {
                alreadySpawned = ReflectionCache.PlayerAvatar_SpawnedField.GetValue(self) is bool val && val;
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] Error reflecting PlayerAvatar.spawned for {playerName}: {ex}");
                // Proceed with caution, or consider not calling orig if reflection fails.
                // For now, if reflection fails, let's assume not spawned to be safer to original behavior.
            }
        }
        else
        {
            LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] ReflectionCache.PlayerAvatar_SpawnedField is null! Cannot check if avatar {playerName} is already spawned.");
            // Fallback: Call original to maintain game flow, though it might cause redundant RPCs.
        }

        if (!alreadySpawned)
        {
            LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnHook] Avatar {playerName} (ViewID {pv?.ViewID}) is not yet marked as 'spawned'. Invoking original game's PlayerAvatar.Spawn(Pos:{position}, Rot:{rotation.eulerAngles}). This will broadcast SpawnRPC.");
            try
            {
                orig.Invoke(self, position, rotation); // This calls the original PlayerAvatar.Spawn, which sends the RPC
            }
            catch (Exception e)
            {
                LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] Error calling original PlayerAvatar.Spawn for {playerName}: {e}");
            }
        }
        else
        {
            LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnHook] Avatar {playerName} (ViewID {pv?.ViewID}) is already marked as 'spawned'. Skipping call to original game's PlayerAvatar.Spawn to prevent redundant SpawnRPC broadcast.");
            // If already spawned, the game likely doesn't intend for its position to be reset by a new generic SpawnRPC
            // unless explicitly handled by a more specific mechanism (like a direct targeted RPC from your mod if needed).
            // We might still want to ensure the local representation matches the 'position' and 'rotation'
            // if this SpawnHook was called directly with new authoritative coordinates for an already spawned player.
            // However, the original PlayerAvatar.Spawn itself just broadcasts.
            // The actual position setting happens in SpawnRPC.
            // For now, mimicking the old mod's guard is the safest first step.
        }
    }

    public static void PlayerAvatar_StartHook(Action<PlayerAvatar> orig, PlayerAvatar self)
    {
        orig.Invoke(self); // Let the original Start logic run

        // Initial logging to see when StartHook is called for whom
        PhotonView? pvForLog = PhotonUtilities.GetPhotonView(self);
        string playerNameForLog = GameUtilities.GetPlayerNickname(self);
        LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.StartHook] Entered for {playerNameForLog} (ViewID: {pvForLog?.ViewID}, IsMine: {pvForLog?.IsMine ?? false}, IsMasterClientOwner: {pvForLog?.Owner?.IsMasterClient ?? false})");


        if (!GameUtilities.IsModLogicActive() || !PhotonNetwork.IsMasterClient)
        {
            // ... (existing logic for non-MC or inactive mod logic, ensure LoadingLevelAnimationCompletedRPC is handled appropriately) ...
            // This part seems okay, mainly for ensuring clients get LoadingLevelAnimationCompletedRPC
            // if the game or MC's original Start doesn't cover it for them.
            bool levelAnimCompleted = ReflectionCache.PlayerAvatar_LevelAnimationCompletedField != null &&
                                      ReflectionCache.PlayerAvatar_LevelAnimationCompletedField.GetValue(self) is bool val && val;
            if (PhotonNetwork.IsMasterClient && self.photonView != null && !levelAnimCompleted)
            {
                LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.StartHook] Non-ModLogic/Non-MC Path: {GameUtilities.GetPlayerNickname(self)} (ViewID {self.photonView?.ViewID}) - MC ensuring LoadingLevelAnimationCompletedRPC (AllBuffered).");
                self.photonView?.RPC("LoadingLevelAnimationCompletedRPC", RpcTarget.AllBuffered);
            }
            return;
        }

        PhotonView? pv = PhotonUtilities.GetPhotonView(self);
        if (self == null || pv == null || pv.Owner == null)
        {
            LatePlugin.Log.LogWarning("[PlayerAvatarPatches.StartHook] PlayerAvatar, PhotonView, or Owner is null in StartHook. Cannot proceed with correction logic.");
            return;
        }

        // ***** ADD THIS CRITICAL CHECK *****
        if (pv.Owner.IsMasterClient)
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.StartHook] Player {GameUtilities.GetPlayerNickname(self)} is the HOST. Skipping L.A.T.E.'s corrective origin-spawn logic for them.");
            // Ensure LoadingLevelAnimationCompletedRPC is sent by host for self if needed, though original Start likely handles this via PlayerSpawn.
            // This is a bit redundant with the block above but re-evaluating for host-specific case.
            bool levelAnimCompletedHost = ReflectionCache.PlayerAvatar_LevelAnimationCompletedField != null &&
                                          ReflectionCache.PlayerAvatar_LevelAnimationCompletedField.GetValue(self) is bool val && val;
            if (!levelAnimCompletedHost)
            {
                LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.StartHook] Host {GameUtilities.GetPlayerNickname(self)} (ViewID {pv.ViewID}) - levelAnimationCompleted is false. Sending RPC from StartHook (AllBuffered).");
                pv.RPC("LoadingLevelAnimationCompletedRPC", RpcTarget.AllBuffered);
            }
            return; // Do not proceed with corrective spawn for the host.
        }
        // ***** END OF ADDED CHECK *****


        int viewID = pv.ViewID;
        bool isAtOrigin = self.transform.position.sqrMagnitude < 1.0f;
        bool levelIsActive = GameDirector.instance != null && GameDirector.instance.currentState == GameDirector.gameState.Main;

        if (levelIsActive && isAtOrigin && !spawnPositionCorrectedInStart.Contains(viewID))
        {
            string playerName = GameUtilities.GetPlayerNickname(self);
            LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.StartHook] Player {playerName} (ViewID {viewID}, Owner: {pv.Owner.NickName}) is at origin ({self.transform.position}) after Start. Attempting corrective spawn.");

            if (TryFindSafeTruckSpawnPoint(self, out Vector3 correctedPosition, out Quaternion correctedRotation))
            {
                LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.StartHook] Corrective spawn for {playerName} to {correctedPosition}. Sending SpawnRPC to owner: {pv.Owner.NickName}.");
                pv.RPC("SpawnRPC", pv.Owner, correctedPosition, correctedRotation);
                spawnPositionCorrectedInStart.Add(viewID);
                spawnPositionAssigned.Add(viewID);
            }
            else
            {
                LatePlugin.Log.LogError($"[PlayerAvatarPatches.StartHook] Could not find a safe truck spawn point for corrective spawn of {playerName}.");
            }
        }

        // Ensure non-host clients (especially late joiners processed here) also get this if not already set.
        bool levelAnimCompletedAfterLogic = ReflectionCache.PlayerAvatar_LevelAnimationCompletedField != null &&
                                             ReflectionCache.PlayerAvatar_LevelAnimationCompletedField.GetValue(self) is bool val2 && val2;
        if (!levelAnimCompletedAfterLogic)
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.StartHook] {GameUtilities.GetPlayerNickname(self)} (ViewID {pv.ViewID}) - levelAnimationCompleted is false after L.A.T.E. logic. Sending RPC (AllBuffered).");
            pv.RPC("LoadingLevelAnimationCompletedRPC", RpcTarget.AllBuffered);
        }
    }

    #endregion
}