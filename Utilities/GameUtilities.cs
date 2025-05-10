// File: L.A.T.E/Utilities/GameUtilities.cs
// Note: Many fields and methods previously here have been moved to ReflectionCache.cs or PhotonUtilities.cs
using LATE.Core; // For LatePlugin.Log
using Photon.Realtime; // For Player
using System;
using System.Collections.Generic; // For IList for Shuffle
using System.Reflection; // For FieldInfo (though specific fields moved)
using UnityEngine;
using Object = UnityEngine.Object;
// No longer needs using Photon.Pun; or ExitGames.Client.Photon; or Hashtable directly here

namespace LATE.Utilities; // File-scoped namespace

/// <summary>
/// Provides general game-related utility functions.
/// Reflection field caches are now in ReflectionCache.cs.
/// Photon-specific utilities are in PhotonUtilities.cs.
/// </summary>
internal static class GameUtilities
{
    // Reflection fields have been moved to ReflectionCache.cs
    // Static constructor that checked fields has been moved to ReflectionCache.cs

    #region ─── Scene/State Checks ──────────────────────────────────────────────
    /// <summary>
    /// Checks if the L.A.T.E mod logic should be active in the current game state/scene.
    /// Logic should generally be disabled in Main Menu, Lobby Menu, and Tutorial.
    /// </summary>
    /// <returns>True if the mod logic should be active, false otherwise.</returns>
    public static bool IsModLogicActive()
    {
        if (SemiFunc.IsMainMenu()) return false;
        if (RunManager.instance == null) return false; // Early exit if RunManager isn't even up
        if (SemiFunc.RunIsLobbyMenu()) return false;
        if (SemiFunc.RunIsTutorial()) return false;
        return true; // Assumed to be in a gameplay scene (Truck, Shop, Level, Arena)
    }
    #endregion

    #region ─── Enemy Helpers (To be reviewed for EnemySyncManager / uses ReflectionCache) ──────────────────
    // These methods now use ReflectionCache for their field lookups.

    internal static bool TryGetEnemyPhotonView(Enemy enemy, out PhotonView? enemyPv)
    {
        enemyPv = null;
        if (enemy == null || ReflectionCache.Enemy_PhotonViewField == null) return false;
        try
        {
            enemyPv = ReflectionCache.Enemy_PhotonViewField.GetValue(enemy) as PhotonView;
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
        if (enemy == null || ReflectionCache.Enemy_TargetPlayerViewIDField == null) return false;
        try
        {
            object? value = ReflectionCache.Enemy_TargetPlayerViewIDField.GetValue(enemy);
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
        // targetFieldInfo will come from ReflectionCache.EnemyXYZ_TargetField
        if (enemyControllerInstance == null || targetFieldInfo == null) return null;
        try
        {
            return targetFieldInfo.GetValue(enemyControllerInstance) as PlayerAvatar;
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[GameUtilities] Failed reflecting {enemyTypeName}.playerTarget (using provided FieldInfo): {ex}");
            return null;
        }
    }

    internal static EnemyVision? GetEnemyVision(Enemy enemy)
    {
        if (enemy == null) return null;
        EnemyVision? vision = null;
        try
        {
            if (ReflectionCache.Enemy_VisionField != null)
            {
                vision = ReflectionCache.Enemy_VisionField.GetValue(enemy) as EnemyVision;
            }
            if (vision == null)
            {
                // Fallback if reflection failed or field was null
                vision = enemy.GetComponent<EnemyVision>();
            }
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[GameUtilities] Error getting EnemyVision for '{enemy.gameObject?.name ?? "NULL"}': {ex}");
            vision = null;
        }
        if (vision == null)
        {
            LatePlugin.Log.LogWarning($"[GameUtilities] Failed to get EnemyVision for enemy '{enemy.gameObject?.name ?? "NULL"}'.");
        }
        return vision;
    }
    #endregion

    #region ─── Component Cache ─────────────────────────────────────────────────────
    /// <summary>
    /// Lightweight scene-wide cache for a specific component type.
    /// </summary>
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
    /// <summary>
    /// Finds a suitable MonoBehaviour instance to run coroutines on.
    /// Prefers RunManager, falls back to GameDirector.
    /// </summary>
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
    /// <summary>
    /// Gets the FieldInfo for the internal ValuablePropSwitch.SetupComplete field via ReflectionCache.
    /// </summary>
    internal static FieldInfo? GetVpsSetupCompleteField()
    {
        // No local caching needed here, ReflectionCache handles it.
        // We just return the cached field from ReflectionCache.
        if (ReflectionCache.ValuablePropSwitch_SetupCompleteField == null)
        {
            // Logged by ReflectionCache's static constructor if critical and missing
        }
        return ReflectionCache.ValuablePropSwitch_SetupCompleteField;
    }
    #endregion

    #region ─── Player Avatar Methods ──────────────────────────────────────────────
    /// <summary>
    /// Finds the PlayerAvatar associated with a given Photon Player.
    /// </summary>
    public static PlayerAvatar? FindPlayerAvatar(Player player)
    {
        if (player == null) return null;
        if (GameDirector.instance?.PlayerList != null)
        {
            foreach (var avatar in GameDirector.instance.PlayerList)
            {
                if (avatar == null) continue;
                var pv = PhotonUtilities.GetPhotonView(avatar); // Use PhotonUtilities
                if (pv != null && pv.OwnerActorNr == player.ActorNumber) return avatar;
            }
        }
        // Fallback: Search all PlayerAvatars in the scene.
        foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
        {
            if (avatar == null) continue;
            var pv = PhotonUtilities.GetPhotonView(avatar); // Use PhotonUtilities
            if (pv != null && pv.OwnerActorNr == player.ActorNumber) return avatar;
        }
        LatePlugin.Log.LogWarning($"[GameUtilities] Could not find PlayerAvatar for {player.NickName} (ActorNr: {player.ActorNumber}).");
        return null;
    }

    /// <summary>
    /// Finds the PlayerAvatar belonging to the local player.
    /// </summary>
    public static PlayerAvatar? FindLocalPlayerAvatar()
    {
        if (PlayerController.instance?.playerAvatar?.GetComponent<PlayerAvatar>() is PlayerAvatar localAvatar &&
            PhotonUtilities.GetPhotonView(localAvatar)?.IsMine == true) // Use PhotonUtilities
        {
            return localAvatar;
        }
        foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
        {
            if (avatar == null) continue;
            if (PhotonUtilities.GetPhotonView(avatar)?.IsMine == true) return avatar; // Use PhotonUtilities
        }
        return null;
    }
    #endregion

    #region ─── Player Nickname Helper ─────────────────────────────────────────────
    /// <summary>
    /// Safely gets a display name for a player avatar, prioritizing Photon NickName.
    /// </summary>
    public static string GetPlayerNickname(PlayerAvatar avatar)
    {
        if (avatar == null) return "<NullAvatar>";
        var pv = PhotonUtilities.GetPhotonView(avatar); // Use PhotonUtilities

        if (pv?.Owner?.NickName != null) return pv.Owner.NickName;

        if (ReflectionCache.PlayerAvatar_PlayerNameField != null)
        {
            try
            {
                object? nameObj = ReflectionCache.PlayerAvatar_PlayerNameField.GetValue(avatar);
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
    /// <summary>
    /// Safely retrieves the PhotonView ID of a player's PhysGrabber using ReflectionCache.
    /// </summary>
    internal static int GetPhysGrabberViewId(PlayerAvatar playerAvatar)
    {
        if (playerAvatar == null || ReflectionCache.PlayerAvatar_PhysGrabberField == null || ReflectionCache.PhysGrabber_PhotonViewField == null) return -1;
        try
        {
            object? physGrabberObj = ReflectionCache.PlayerAvatar_PhysGrabberField.GetValue(playerAvatar);
            if (physGrabberObj is PhysGrabber physGrabber)
            {
                PhotonView? pv = ReflectionCache.PhysGrabber_PhotonViewField.GetValue(physGrabber) as PhotonView;
                if (pv != null) return pv.ViewID;
            }
        }
        catch (Exception ex) { LatePlugin.Log?.LogError($"[GameUtilities] GetPhysGrabberViewId reflection error: {ex}"); }
        return -1;
    }
    #endregion

    #region --- Shuffle Extension ---
    private static readonly System.Random Rng = new System.Random();

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
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
    #endregion
}