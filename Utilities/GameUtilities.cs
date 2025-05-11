// File: L.A.T.E/Utilities/GameUtilities.cs
using LATE.Core;
using Photon.Pun; // Added for PhotonView
using Photon.Realtime;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LATE.Utilities
{
    internal static class GameUtilities
    {
        #region ─── Scene/State Checks ──────────────────────────────────────────────
        public static bool IsModLogicActive()
        {
            if (SemiFunc.IsMainMenu()) return false;
            if (RunManager.instance == null) return false;
            if (SemiFunc.RunIsLobbyMenu()) return false;
            if (SemiFunc.RunIsTutorial()) return false;
            return true;
        }
        #endregion

        #region ─── Enemy Helpers (Uses ReflectionCache) ─────────────────────────
        internal static bool TryGetEnemyPhotonView(Enemy enemy, out PhotonView? enemyPv)
        {
            enemyPv = null;
            if (enemy == null || ReflectionCache.Enemy_PhotonViewField == null)
                return false;
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
            if (enemy == null || ReflectionCache.Enemy_TargetPlayerViewIDField == null)
                return false;
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
            if (enemyControllerInstance == null || targetFieldInfo == null) return null;
            try
            {
                return targetFieldInfo.GetValue(enemyControllerInstance) as PlayerAvatar;
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[GameUtilities] Failed reflecting {enemyTypeName}.playerTarget (using FieldInfo '{targetFieldInfo.Name}'): {ex}");
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
                    vision = enemy.GetComponent<EnemyVision>();
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[GameUtilities] Error getting EnemyVision for '{enemy.gameObject?.name ?? "NULL"}': {ex}");
                vision = null;
            }
            if (vision == null)
                LatePlugin.Log.LogWarning($"[GameUtilities] Failed to get EnemyVision for enemy '{enemy.gameObject?.name ?? "NULL"}'.");
            return vision;
        }
        #endregion

        #region ─── Component Cache ─────────────────────────────────────────────────────
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
        public static MonoBehaviour? FindCoroutineRunner()
        {
            LatePlugin.Log.LogDebug("[GameUtilities] Finding coroutine runner…");
#if UNITY_2022_2_OR_NEWER
            if (Object.FindFirstObjectByType<RunManager>() is { } runMgr)
                return runMgr;
#else
            if (Object.FindObjectOfType<RunManager>() is { } runMgr)
                return runMgr;
#endif
            if (GameDirector.instance is { } gDir)
                return gDir;
            LatePlugin.Log.LogError("[GameUtilities] Failed to find suitable MonoBehaviour (RunManager or GameDirector) for coroutines!");
            return null;
        }
        #endregion

        #region ─── ValuablePropSwitch Helper ──────────────────────────────────────────
        internal static FieldInfo? GetVpsSetupCompleteField()
        {
            return ReflectionCache.ValuablePropSwitch_SetupCompleteField;
        }
        #endregion

        #region ─── Player Avatar Methods ──────────────────────────────────────────────
        public static PlayerAvatar? FindPlayerAvatar(Player player)
        {
            if (player == null)
                return null;
            if (GameDirector.instance?.PlayerList != null)
            {
                foreach (var avatar in GameDirector.instance.PlayerList)
                {
                    if (avatar == null)
                        continue;
                    PhotonView? pv = PhotonUtilities.GetPhotonView(avatar);
                    if (pv != null && pv.OwnerActorNr == player.ActorNumber)
                        return avatar;
                }
            }
            foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
            {
                if (avatar == null)
                    continue;
                PhotonView? pv = PhotonUtilities.GetPhotonView(avatar);
                if (pv != null && pv.OwnerActorNr == player.ActorNumber)
                    return avatar;
            }
            LatePlugin.Log.LogWarning($"[GameUtilities] Could not find PlayerAvatar for {player.NickName} (ActorNr: {player.ActorNumber}).");
            return null;
        }

        public static PlayerAvatar? FindLocalPlayerAvatar()
        {
            if (PlayerController.instance?.playerAvatar?.GetComponent<PlayerAvatar>() is PlayerAvatar localAvatar &&
                PhotonUtilities.GetPhotonView(localAvatar)?.IsMine == true)
            {
                return localAvatar;
            }
            foreach (PlayerAvatar avatar in Object.FindObjectsOfType<PlayerAvatar>())
            {
                if (avatar == null)
                    continue;
                if (PhotonUtilities.GetPhotonView(avatar)?.IsMine == true)
                    return avatar;
            }
            return null;
        }
        #endregion

        #region ─── Player Nickname Helper ─────────────────────────────────────────────
        public static string GetPlayerNickname(PlayerAvatar avatar)
        {
            if (avatar == null)
                return "<NullAvatar>";

            PhotonView? pv = PhotonUtilities.GetPhotonView(avatar);
            if (pv?.Owner?.NickName != null)
                return pv.Owner.NickName;

            if (ReflectionCache.PlayerAvatar_PlayerNameField != null)
            {
                try
                {
                    object? nameObj = ReflectionCache.PlayerAvatar_PlayerNameField.GetValue(avatar);
                    if (nameObj is string nameStr && !string.IsNullOrEmpty(nameStr))
                        return nameStr + " (Reflected)";
                }
                catch (Exception ex)
                {
                    LatePlugin.Log?.LogWarning($"[GameUtilities] Failed to reflect playerName for avatar: {ex.Message}");
                }
            }

            if (pv?.OwnerActorNr > 0)
                return $"ActorNr {pv.OwnerActorNr}";

            LatePlugin.Log?.LogWarning($"[GameUtilities] Could not determine nickname for avatar (ViewID: {pv?.ViewID ?? 0}), returning fallback.");
            return "<UnknownPlayer>";
        }
        #endregion

        #region ─── PhysGrabber Helper ─────────────────────────────────────────────────
        internal static int GetPhysGrabberViewId(PlayerAvatar playerAvatar)
        {
            if (playerAvatar == null ||
                ReflectionCache.PlayerAvatar_PhysGrabberField == null ||
                ReflectionCache.PhysGrabber_PhotonViewField == null)
                return -1;
            try
            {
                object? physGrabberObj = ReflectionCache.PlayerAvatar_PhysGrabberField.GetValue(playerAvatar);
                if (physGrabberObj is PhysGrabber physGrabber)
                {
                    PhotonView? pv = ReflectionCache.PhysGrabber_PhotonViewField.GetValue(physGrabber) as PhotonView;
                    if (pv != null)
                        return pv.ViewID;
                }
            }
            catch (Exception ex)
            {
                LatePlugin.Log?.LogError($"[GameUtilities] GetPhysGrabberViewId reflection error: {ex}");
            }
            return -1;
        }
        #endregion

        #region ─── Shuffle Extension ─────────────────────────────────────────────────
        private static readonly System.Random Rng = new System.Random();
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
}