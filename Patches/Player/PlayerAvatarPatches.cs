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
    [HarmonyPatch(typeof(PlayerAvatar), "Update")]
    public static void PlayerAvatar_Update_Postfix(PlayerAvatar __instance)
    {
        if (!GameUtilities.IsModLogicActive())
            return;

        VoiceManager.HandleAvatarUpdate(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(PlayerAvatar), "PlayerDeathRPC")]
    static void PlayerAvatar_PlayerDeathRPC_Postfix(PlayerAvatar __instance, int enemyIndex)
    {
        if (PhotonNetwork.IsMasterClient && __instance != null && __instance.photonView != null)
        {
            // Fully qualify Photon.Realtime.Player.
            Photon.Realtime.Player? owner = __instance.photonView.Owner;
            if (owner != null)
            {
                PlayerStateManager.MarkPlayerDead(owner);
                if (ReflectionCache.PlayerAvatar_PlayerDeathHeadField != null)
                {
                    try
                    {
                        object? deathHeadObj = ReflectionCache.PlayerAvatar_PlayerDeathHeadField.GetValue(__instance);
                        if (deathHeadObj is PlayerDeathHead deathHead && deathHead != null && deathHead.gameObject != null)
                        {
                            PlayerPositionManager.UpdatePlayerDeathPosition(
                                owner,
                                deathHead.transform.position,
                                deathHead.transform.rotation
                            );
                        }
                        else
                        {
                            LatePlugin.Log.LogWarning($"[PlayerAvatarPatches] PlayerDeathHead component not found or null for {owner.NickName}.");
                        }
                    }
                    catch (Exception ex)
                    {
                        LatePlugin.Log.LogError($"[PlayerAvatarPatches] Error reflecting PlayerDeathHead for {owner.NickName}: {ex}");
                    }
                }
                else
                {
                    LatePlugin.Log.LogError("[PlayerAvatarPatches] PlayerDeathHead reflection field (PlayerAvatar_PlayerDeathHeadField) is null!");
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
            LatePlugin.Log.LogError("[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Instance or PhotonView is null. Cannot determine sender.");
            return true;
        }

        // Fully qualify Photon.Realtime.Player.
        Photon.Realtime.Player sender = pv.Owner;
        if (sender == null)
        {
            LatePlugin.Log.LogError($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] PhotonView Owner is null for Avatar PV {pv.ViewID}. Cannot determine sender.");
            return true;
        }

        if (sender.IsLocal)
            return true;

        int actorNr = sender.ActorNumber;
        string nickname = sender.NickName ?? $"ActorNr {actorNr}";

        if (_reloadHasBeenTriggeredThisScene)
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Ignoring LoadingCompleteRPC from {nickname}: Reload already triggered this scene.");
            return true;
        }

        if (LateJoinManager.IsPlayerNeedingSync(actorNr))
        {
            LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Received LoadingLevelAnimationCompletedRPC from late-joiner {nickname} (ActorNr: {actorNr}).");

            if (!GameUtilities.IsModLogicActive())
            {
                LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Mod logic INACTIVE. Clearing sync need for {nickname} but not syncing.");
                LateJoinManager.ClearPlayerTracking(actorNr);
                return true;
            }

            LateJoinManager.MarkPlayerSyncTriggeredAndClearNeed(actorNr);

            if (ConfigManager.ForceReloadOnLateJoin.Value)
            {
                LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] CONFIG: Forcing level reload for late-joiner {nickname}.");
                if (RunManager.instance != null)
                {
                    _reloadHasBeenTriggeredThisScene = true;
                    LatePlugin.Log.LogInfo("[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Setting reload-triggered flag for this scene.");
                    RunManager.instance.RestartScene();
                    return false;
                }
                else
                {
                    LatePlugin.Log.LogError($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] FAILED TO FORCE RELOAD: RunManager.instance is null for {nickname}.");
                    LateJoinManager.ClearPlayerTracking(actorNr);
                    return true;
                }
            }
            else
            {
                LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.LoadingCompleteRPC_Prefix] Initiating standard late-join sync for {nickname}.");
                LateJoinManager.SyncAllStateForPlayer(sender, __instance);
                return true;
            }
        }
        return true;
    }

    #endregion

    #region PlayerAvatar MonoMod Hooks

    public static void PlayerAvatar_SpawnHook(
        Action<PlayerAvatar, Vector3, Quaternion> orig,
        PlayerAvatar self,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (!GameUtilities.IsModLogicActive())
        {
            LatePlugin.Log.LogDebug("[PlayerAvatarPatches.SpawnHook] Skipping custom spawn logic (Mod Inactive). Calling original.");
            try
            {
                orig.Invoke(self, position, rotation);
            }
            catch (Exception e)
            {
                LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] Error calling original Spawn (Mod Inactive): {e}");
            }
            return;
        }

        PhotonView? pv = PhotonUtilities.GetPhotonView(self);
        if (self == null || pv == null || pv.Owner == null)
        {
            LatePlugin.Log.LogError("[PlayerAvatarPatches.SpawnHook] PlayerAvatar, PhotonView, or Owner is null. Cannot proceed.");
            try
            {
                if (self != null)
                    orig.Invoke(self, position, rotation);
            }
            catch (Exception e)
            {
                LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] Error calling original Spawn (Null check fail): {e}");
            }
            return;
        }

        Photon.Realtime.Player joiningPlayer = pv.Owner;
        int viewID = pv.ViewID;
        Vector3 finalPosition = position;
        Quaternion finalRotation = rotation;
        bool positionOverriddenByMod = false;
        bool useNormalSpawnLogic = true;

        if (ConfigManager.SpawnAtLastPosition.Value)
        {
            if (PlayerPositionManager.TryGetLastTransform(joiningPlayer, out PlayerTransformData lastTransform))
            {
                finalPosition = lastTransform.Position;
                finalRotation = lastTransform.Rotation;
                positionOverriddenByMod = true;
                useNormalSpawnLogic = false;
                LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnHook] Spawning {joiningPlayer.NickName} (ViewID {viewID}) at last known: {finalPosition} (DeathHead: {lastTransform.IsDeathHeadPosition})");
                spawnPositionAssigned.Add(viewID);
            }
            else
            {
                useNormalSpawnLogic = true;
            }
        }
        else
        {
            useNormalSpawnLogic = true;
        }

        if (useNormalSpawnLogic)
        {
            try
            {
                bool alreadySpawned = ReflectionCache.PlayerAvatar_SpawnedField != null &&
                    (bool)(ReflectionCache.PlayerAvatar_SpawnedField.GetValue(self) ?? false);
                bool alreadyAssignedByMod = spawnPositionAssigned.Contains(viewID);

                if (!alreadySpawned && !alreadyAssignedByMod)
                {
                    string assignedPlayerName = GameUtilities.GetPlayerNickname(self);
                    if (PunManager.instance != null)
                        PunManager.instance.SyncAllDictionaries();

                    if (PhotonUtilities.IsRealMasterClient())
                    {
                        List<SpawnPoint> allSpawnPoints = Object.FindObjectsOfType<SpawnPoint>()
                            .Where(sp => sp != null && !sp.debug).ToList();

                        if (allSpawnPoints.Count > 0)
                        {
                            List<PlayerAvatar> currentPlayers = GameDirector.instance?.PlayerList ?? new List<PlayerAvatar>();
                            float minDistanceSq = 1.5f * 1.5f;
                            GameUtilities.Shuffle(allSpawnPoints);
                            bool foundAvailable = false;

                            foreach (SpawnPoint sp in allSpawnPoints)
                            {
                                bool blocked = false;
                                Vector3 spPos = sp.transform.position;

                                foreach (PlayerAvatar playerAvatarInstance in currentPlayers)
                                {
                                    if (playerAvatarInstance == null || playerAvatarInstance == self)
                                        continue;

                                    if ((playerAvatarInstance.transform.position - spPos).sqrMagnitude < minDistanceSq)
                                    {
                                        blocked = true;
                                        break;
                                    }
                                }

                                if (!blocked)
                                {
                                    finalPosition = sp.transform.position;
                                    finalRotation = sp.transform.rotation;
                                    foundAvailable = true;
                                    positionOverriddenByMod = true;
                                    LatePlugin.Log.LogInfo($"[PlayerAvatarPatches.SpawnHook] Assigning {assignedPlayerName} (ViewID {viewID}) to SP '{sp.name}' at {finalPosition}");
                                    spawnPositionAssigned.Add(viewID);
                                    break;
                                }
                            }

                            if (!foundAvailable)
                            {
                                LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.SpawnHook] All {allSpawnPoints.Count} SPs blocked for {assignedPlayerName}. Using original: {position}");
                            }
                        }
                        else
                        {
                            LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] No valid SPs for {assignedPlayerName}. Using original: {position}");
                        }
                    }

                    LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.SpawnHook] Invoking original Spawn for {assignedPlayerName} (ViewID {viewID}) at {finalPosition} (Default, Overridden: {positionOverriddenByMod})");
                    orig.Invoke(self, finalPosition, finalRotation);
                }
                else
                {
                    LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.SpawnHook] Skipping default spawn for {viewID}: spawned ({alreadySpawned}) or assigned ({alreadyAssignedByMod}).");

                    if (alreadyAssignedByMod && !alreadySpawned)
                        orig.Invoke(self, finalPosition, finalRotation);
                    else if (!alreadySpawned)
                        orig.Invoke(self, position, rotation);
                }
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] Error in default spawn logic for ViewID {viewID}: {ex}");
                try
                {
                    if (!spawnPositionAssigned.Contains(viewID))
                        orig.Invoke(self, position, rotation);
                }
                catch (Exception origEx)
                {
                    LatePlugin.Log.LogError($"[PlayerAvatarPatches.SpawnHook] Error calling original Spawn (Exception): {origEx}");
                }
            }
        }
        else if (positionOverriddenByMod)
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.SpawnHook] Invoking original Spawn for {joiningPlayer.NickName} (ViewID {viewID}) at {finalPosition} (Last known)");
            orig.Invoke(self, finalPosition, finalRotation);
        }
        else
        {
            LatePlugin.Log.LogWarning($"[PlayerAvatarPatches.SpawnHook] Unexpected state for ViewID {viewID}. Invoking original with default.");
            orig.Invoke(self, position, rotation);
        }
    }

    public static void PlayerAvatar_StartHook(Action<PlayerAvatar> orig, PlayerAvatar self)
    {
        orig.Invoke(self);
        PhotonView? pv = PhotonUtilities.GetPhotonView(self);
        if (self == null || pv == null)
            return;
        if (PhotonNetwork.IsMasterClient)
        {
            LatePlugin.Log.LogDebug($"[PlayerAvatarPatches.StartHook] PlayerAvatar Start: Sending LoadingLevelAnimationCompletedRPC for ViewID {pv.ViewID}");
            pv.RPC("LoadingLevelAnimationCompletedRPC", RpcTarget.AllBuffered);
        }
    }

    #endregion
}