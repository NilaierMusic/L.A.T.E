// File: L.A.T.E/Managers/DestructionManager.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Object = UnityEngine.Object;
using LATE.Core; // For LatePlugin.Log
using LATE.Utilities; // For ReflectionCache, PhotonUtilities, GameUtilities

namespace LATE.Managers; // File-scoped namespace

/// <summary>
/// Manages the tracking of destroyed PhysGrabObjects and broken PhysGrabHinges.
/// This information is used to synchronize the state of these objects for late-joining players.
/// </summary>
internal static class DestructionManager
{
    #region Private Fields

    private static readonly HashSet<int> _destroyedViewIDs = new HashSet<int>();
    private static readonly HashSet<int> _brokenHingeViewIDs = new HashSet<int>();

    private const float HingeCacheRefreshInterval = 2f;
    private static PhysGrabHinge[] _hingeCache = Array.Empty<PhysGrabHinge>();
    private static float _hingeCacheLastRefreshTime;

    #endregion

    #region Public Marking Methods

    /// <summary>
    /// Marks a PhysGrabObject (identified by its PhotonView ID) as destroyed.
    /// </summary>
    /// <param name="viewID">The PhotonView ID of the destroyed object.</param>
    public static void MarkObjectAsDestroyed(int viewID)
    {
        if (_destroyedViewIDs.Add(viewID))
        {
            LatePlugin.Log.LogDebug($"[DestructionManager] Marking ViewID {viewID} as destroyed.");
        }
        _brokenHingeViewIDs.Remove(viewID); // Ensure mutual exclusivity
    }

    /// <summary>
    /// Marks a PhysGrabHinge as broken.
    /// </summary>
    /// <param name="hingeInstance">The PhysGrabHinge instance.</param>
    /// <param name="pv">The PhotonView of the hinge.</param>
    public static void MarkHingeAsBroken(PhysGrabHinge hingeInstance, PhotonView pv)
    {
        if (pv == null)
        {
            LatePlugin.Log.LogWarning("[DestructionManager] MarkHingeAsBroken called with null PhotonView.");
            return;
        }

        if (_destroyedViewIDs.Contains(pv.ViewID))
        {
            LatePlugin.Log.LogDebug($"[DestructionManager] Skipped break-mark for hinge {pv.ViewID}: Object already marked as fully destroyed.");
            return;
        }

        if (_brokenHingeViewIDs.Add(pv.ViewID))
        {
            LatePlugin.Log.LogDebug($"[DestructionManager] Marking hinge ViewID {pv.ViewID} as broken.");
        }
    }

    #endregion

    #region Scene Reset Methods

    /// <summary>
    /// Clears all tracked destruction states and the hinge cache.
    /// Typically called when a new level loads.
    /// </summary>
    public static void ResetState()
    {
        LatePlugin.Log.LogDebug("[DestructionManager] Clearing destruction/broken tracking & hinge cache.");
        _destroyedViewIDs.Clear();
        _brokenHingeViewIDs.Clear();
        _hingeCache = Array.Empty<PhysGrabHinge>();
        _hingeCacheLastRefreshTime = 0f;
    }

    #endregion

    #region Sync for Late-Joiners

    /// <summary>
    /// Synchronizes the states of all hinges (broken, open/closed) to a specific late-joining player.
    /// This method is host-only.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to sync to.</param>
    public static void SyncHingeStatesForPlayer(Player targetPlayer)
    {
        if (!PhotonUtilities.IsRealMasterClient() || targetPlayer == null || !PhotonNetwork.InRoom || SemiFunc.RunIsLobbyMenu())
        {
            return;
        }
        if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) return;
        if (_destroyedViewIDs.Count == 0 && _brokenHingeViewIDs.Count == 0) return;

        string targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        FieldInfo? brokenField = ReflectionCache.PhysGrabHinge_BrokenField;
        FieldInfo? closedField = ReflectionCache.PhysGrabHinge_ClosedField;

        if (brokenField == null || closedField == null)
        {
            LatePlugin.Log.LogError("[DestructionManager] Reflection field for hinge 'broken' or 'closed' missing from ReflectionCache – cannot sync hinge states.");
            return;
        }

        PhysGrabHinge[] cachedHinges = GetCachedHinges(); // Uses GameUtilities.GetCachedComponents internally
        LatePlugin.Log.LogInfo($"[DestructionManager] Syncing ALL hinge states to {targetNickname} (Total hinges in cache: {cachedHinges.Length}).");

        int brokenSyncCount = 0;
        int openSyncCount = 0;
        int closedSyncCount = 0;

        foreach (PhysGrabHinge hinge in cachedHinges)
        {
            if (hinge == null || hinge.gameObject == null) continue;

            PhotonView? pv = PhotonUtilities.GetPhotonView(hinge);
            if (pv == null) continue;

            int viewID = pv.ViewID;

            if (_destroyedViewIDs.Contains(viewID))
            {
                // LatePlugin.Log.LogDebug($"[DestructionManager] Skipping hinge {viewID} sync: Object fully destroyed.");
                continue;
            }

            bool brokenOnHost;
            bool closedOnHost;
            try
            {
                brokenOnHost = (bool)(brokenField.GetValue(hinge) ?? false);
                closedOnHost = (bool)(closedField.GetValue(hinge) ?? true); // Default to true (closed)
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[DestructionManager] Reflection failed getting state for hinge {viewID}: {ex}");
                continue;
            }

            if (brokenOnHost)
            {
                if (_brokenHingeViewIDs.Add(viewID)) // Mark if not already, for consistency
                {
                    // LatePlugin.Log.LogDebug($"[DestructionManager] Ensuring hinge {viewID} is marked as broken during sync pass.");
                }
                //LatePlugin.Log.LogDebug($"[DestructionManager] Sending HingeBreakRPC for broken hinge {viewID} to {targetNickname}.");
                pv.RPC("HingeBreakRPC", targetPlayer);
                brokenSyncCount++;
            }
            else // Hinge is NOT broken on host
            {
                _brokenHingeViewIDs.Remove(viewID); // Ensure not marked if host says it's not broken

                if (!closedOnHost) // Host says it's OPEN
                {
                    //LatePlugin.Log.LogDebug($"[DestructionManager] Sending OpenImpulseRPC for OPEN hinge {viewID} to {targetNickname}.");
                    pv.RPC("OpenImpulseRPC", targetPlayer);
                    openSyncCount++;
                }
                else // Host says it's CLOSED
                {
                    closedSyncCount++;
                    //LatePlugin.Log.LogDebug($"[DestructionManager] Hinge {viewID} is CLOSED on host, no RPC needed for {targetNickname}.");
                }
            }
        }
        LatePlugin.Log.LogInfo($"[DestructionManager] Hinge sync to {targetNickname} finished – Synced: {brokenSyncCount} Broken, {openSyncCount} Opened, {closedSyncCount} Confirmed Closed.");
    }
    #endregion

    #region Internal Helpers
    private static PhysGrabHinge[] GetCachedHinges()
    {
        // GameUtilities.GetCachedComponents handles the caching logic
        return GameUtilities.GetCachedComponents(ref _hingeCache, ref _hingeCacheLastRefreshTime, HingeCacheRefreshInterval);
    }
    #endregion
}