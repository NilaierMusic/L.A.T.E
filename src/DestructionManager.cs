using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace L.A.T.E
{
    internal static class DestructionManager
    {
        #region Private Fields

        private static readonly HashSet<int> _destroyedViewIDs = new HashSet<int>();
        private static readonly HashSet<int> _brokenHingeViewIDs = new HashSet<int>();

        // Cache interval for PhysGrabHinge to avoid expensive scene scans.
        private const float HingeCacheRefreshInterval = 2f;
        private static PhysGrabHinge[] _hingeCache = Array.Empty<PhysGrabHinge>();
        private static float _hingeCacheLastRefreshTime;

        #endregion

        #region Public Marking Methods

        public static void MarkObjectAsDestroyed(int viewID)
        {
            if (_destroyedViewIDs.Add(viewID))
            {
                LateJoinEntry.Log.LogDebug($"[DestructionManager] Marking ViewID {viewID} as destroyed.");
            }
            else
            {
                LateJoinEntry.Log.LogDebug($"[DestructionManager] Duplicate destroy mark for ViewID {viewID} ignored.");
            }

            // Remove from broken list to keep sets mutually exclusive.
            _brokenHingeViewIDs.Remove(viewID);
        }

        public static void MarkHingeAsBroken(PhysGrabHinge hingeInstance, PhotonView pv)
        {
            if (pv == null)
            {
                LateJoinEntry.Log.LogWarning("[DestructionManager] MarkHingeAsBroken called with null PhotonView.");
                return;
            }

            if (_destroyedViewIDs.Contains(pv.ViewID))
            {
                LateJoinEntry.Log.LogDebug($"[DestructionManager] Skipped break-mark – object {pv.ViewID} already destroyed.");
                return;
            }

            if (_brokenHingeViewIDs.Add(pv.ViewID))
            {
                LateJoinEntry.Log.LogDebug($"[DestructionManager] Marking hinge ViewID {pv.ViewID} as broken.");
            }
        }

        #endregion

        #region Scene Reset Methods

        public static void ResetState()
        {
            LateJoinEntry.Log.LogDebug("[DestructionManager] Clearing destruction/broken tracking & hinge cache.");
            _destroyedViewIDs.Clear();
            _brokenHingeViewIDs.Clear();
            _hingeCache = Array.Empty<PhysGrabHinge>();
            _hingeCacheLastRefreshTime = 0f;
        }

        #endregion

        #region Sync for Late-Joiners

        public static void SyncBrokenHingesForPlayer(Player targetPlayer)
        {
            // Precondition checks.
            if (!Utilities.IsRealMasterClient())
            {
                return;
            }

            if (targetPlayer == null)
            {
                return;
            }

            if (!PhotonNetwork.InRoom || SemiFunc.RunIsLobbyMenu())
            {
                return;
            }

            if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
            {
                return;
            }

            // If nothing is tracked, bail out.
            if (_destroyedViewIDs.Count == 0 && _brokenHingeViewIDs.Count == 0)
            {
                return;
            }

            string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
            FieldInfo? brokenField = Utilities.pghBrokenField;
            if (brokenField == null)
            {
                LateJoinEntry.Log.LogError("[DestructionManager] Reflection field 'broken' missing – cannot sync.");
                return;
            }

            // Grab (or refresh) cached hinge objects.
            PhysGrabHinge[] cachedHinges = GetCachedHinges();

            LateJoinEntry.Log.LogInfo($"[DestructionManager] Syncing hinge state to {targetNickname} (Total hinges: {cachedHinges.Length}).");

            int brokenHingeSyncCount = 0;

            foreach (PhysGrabHinge hinge in cachedHinges)
            {
                if (hinge == null || hinge.gameObject == null)
                {
                    continue;
                }

                PhotonView? pv = Utilities.GetPhotonView(hinge);
                if (pv == null)
                {
                    continue;
                }

                int viewID = pv.ViewID;

                // Skip if the hinge is marked as destroyed.
                if (_destroyedViewIDs.Contains(viewID))
                {
                    continue;
                }

                // Only process hinges that may be broken.
                if (!_brokenHingeViewIDs.Contains(viewID))
                {
                    continue;
                }

                bool brokenOnHost;
                try
                {
                    brokenOnHost = (bool)brokenField.GetValue(hinge);
                }
                catch (Exception ex)
                {
                    LateJoinEntry.Log.LogError($"[DestructionManager] Reflection failed for hinge {viewID}: {ex}");
                    continue;
                }

                if (brokenOnHost)
                {
                    pv.RPC("HingeBreakRPC", targetPlayer);
                    brokenHingeSyncCount++;
                }
                else
                {
                    // Clean up stale entry if host reports hinge isn't broken.
                    _brokenHingeViewIDs.Remove(viewID);
                }
            }

            LateJoinEntry.Log.LogInfo($"[DestructionManager] Sync to {targetNickname} finished – synced {brokenHingeSyncCount} broken hinges.");
        }

        #endregion

        #region Internal Helpers

        private static PhysGrabHinge[] GetCachedHinges()
        {
            if (_hingeCache.Length == 0 || Time.unscaledTime - _hingeCacheLastRefreshTime > HingeCacheRefreshInterval)
            {
#if UNITY_2022_2_OR_NEWER
                _hingeCache = Object.FindObjectsByType<PhysGrabHinge>(FindObjectsSortMode.None);
#else
                _hingeCache = Object.FindObjectsOfType<PhysGrabHinge>();
#endif
                _hingeCacheLastRefreshTime = Time.unscaledTime;
            }
            return _hingeCache;
        }

        #endregion
    }
}