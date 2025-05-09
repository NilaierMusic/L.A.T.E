using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LATE
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
                LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Marking ViewID {viewID} as destroyed.");
            }
            else
            {
                LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Duplicate destroy mark for ViewID {viewID} ignored.");
            }

            // Remove from broken list to keep sets mutually exclusive.
            _brokenHingeViewIDs.Remove(viewID);
        }

        public static void MarkHingeAsBroken(PhysGrabHinge hingeInstance, PhotonView pv)
        {
            if (pv == null)
            {
                LATE.Core.LatePlugin.Log.LogWarning("[DestructionManager] MarkHingeAsBroken called with null PhotonView.");
                return;
            }

            if (_destroyedViewIDs.Contains(pv.ViewID))
            {
                LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Skipped break-mark – object {pv.ViewID} already destroyed.");
                return;
            }

            if (_brokenHingeViewIDs.Add(pv.ViewID))
            {
                LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Marking hinge ViewID {pv.ViewID} as broken.");
            }
        }

        #endregion

        #region Scene Reset Methods

        public static void ResetState()
        {
            LATE.Core.LatePlugin.Log.LogDebug("[DestructionManager] Clearing destruction/broken tracking & hinge cache.");
            _destroyedViewIDs.Clear();
            _brokenHingeViewIDs.Clear();
            _hingeCache = Array.Empty<PhysGrabHinge>();
            _hingeCacheLastRefreshTime = 0f;
        }

        #endregion

        #region Sync for Late-Joiners

        public static void SyncHingeStatesForPlayer(Player targetPlayer)
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
            FieldInfo? closedField = Utilities.pghClosedField; // Get the cached field

            if (brokenField == null || closedField == null) // Check both fields
            {
                LATE.Core.LatePlugin.Log.LogError("[DestructionManager] Reflection field 'broken' or 'closed' missing – cannot sync hinge states.");
                return;
            }

            PhysGrabHinge[] cachedHinges = GetCachedHinges();
            LATE.Core.LatePlugin.Log.LogInfo($"[DestructionManager] Syncing ALL hinge states to {targetNickname} (Total hinges: {cachedHinges.Length}).");

            int brokenSyncCount = 0;
            int openSyncCount = 0;
            int closedSyncCount = 0; // Optional, for tracking

            foreach (PhysGrabHinge hinge in cachedHinges)
            {
                if (hinge == null || hinge.gameObject == null) continue;

                PhotonView? pv = Utilities.GetPhotonView(hinge);
                if (pv == null) continue;

                int viewID = pv.ViewID;

                // Skip fully destroyed objects tracked separately (if applicable, otherwise this check might be redundant)
                if (_destroyedViewIDs.Contains(viewID))
                {
                    LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Skipping hinge {viewID} sync: Marked as destroyed.");
                    continue;
                }

                bool brokenOnHost;
                bool closedOnHost;

                try
                {
                    // Get both states using reflection
                    brokenOnHost = (bool)(brokenField.GetValue(hinge) ?? false);
                    closedOnHost = (bool)(closedField.GetValue(hinge) ?? true); // Default to true (closed) if reflection fails? Safer.
                }
                catch (Exception ex)
                {
                    LATE.Core.LatePlugin.Log.LogError($"[DestructionManager] Reflection failed getting state for hinge {viewID}: {ex}");
                    continue;
                }

                // --- Sync Logic ---
                if (brokenOnHost)
                {
                    // If the host says it's broken, send the break RPC.
                    // Also ensure it's marked in our tracking.
                    if (_brokenHingeViewIDs.Add(viewID)) // Add if not already marked
                    {
                        LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Marking hinge {viewID} as broken during sync.");
                    }
                    LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Sending HingeBreakRPC for broken hinge {viewID} to {targetNickname}.");
                    pv.RPC("HingeBreakRPC", targetPlayer);
                    brokenSyncCount++;
                }
                else // Hinge is NOT broken on host
                {
                    // If it's marked as broken locally but host says it's not, remove the mark.
                    _brokenHingeViewIDs.Remove(viewID);

                    // Now sync the open/closed state for non-broken hinges
                    if (!closedOnHost) // Host says it's OPEN
                    {
                        LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Sending OpenImpulseRPC for OPEN hinge {viewID} to {targetNickname}.");
                        pv.RPC("OpenImpulseRPC", targetPlayer); // Tell the client to open it
                        openSyncCount++;
                    }
                    else // Host says it's CLOSED
                    {
                        // Optional: Send CloseImpulseRPC for robustness?
                        // The default state is usually closed, so this might be redundant unless
                        // a door could somehow be open on the client by default.
                        // Let's skip sending CloseImpulseRPC for now to avoid unnecessary RPCs,
                        // unless testing shows it's needed.
                        closedSyncCount++; // Just count it for logging
                        LATE.Core.LatePlugin.Log.LogDebug($"[DestructionManager] Hinge {viewID} is CLOSED on host, no RPC needed for {targetNickname}.");
                    }
                }
            }

            LATE.Core.LatePlugin.Log.LogInfo($"[DestructionManager] Hinge sync to {targetNickname} finished – Synced: {brokenSyncCount} Broken, {openSyncCount} Opened, {closedSyncCount} Confirmed Closed.");
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