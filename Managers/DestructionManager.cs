// File: L.A.T.E/Managers/DestructionManager.cs
using LATE.Core;                          // LatePlugin.Log
using LATE.Utilities;                     // ReflectionCache, PhotonUtilities, GameUtilities
using Photon.Pun;
using Photon.Realtime;

namespace LATE.Managers;

/// <summary>
/// Tracks destroyed PhysGrabObjects & broken PhysGrabHinges and
/// synchronises their state for late-joining players.
/// </summary>
internal static class DestructionManager
{
    private const string LogPrefix = "[DestructionManager]";

    #region Fields

    private static readonly HashSet<int> _destroyedViewIDs = new();
    private static readonly HashSet<int> _brokenHingeViewIDs = new();

    private const float HingeCacheRefreshInterval = 2f;
    private static PhysGrabHinge[] _hingeCache = Array.Empty<PhysGrabHinge>();
    private static float _hingeCacheLastRefreshTime;

    #endregion


    #region Public API

    /// <summary>Marks a PhysGrabObject (by PhotonViewID) as destroyed.</summary>
    public static void MarkObjectAsDestroyed(int viewID)
    {
        if (_destroyedViewIDs.Add(viewID))
            LatePlugin.Log.LogDebug($"{LogPrefix} Marking ViewID {viewID} as destroyed.");

        _brokenHingeViewIDs.Remove(viewID);           // Keep the two sets mutually exclusive
    }

    /// <summary>Marks a PhysGrabHinge as broken.</summary>
    public static void MarkHingeAsBroken(PhysGrabHinge hingeInstance, PhotonView pv)
    {
        if (pv == null)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} MarkHingeAsBroken called with null PhotonView.");
            return;
        }

        if (_destroyedViewIDs.Contains(pv.ViewID))
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} Skipped break-mark for hinge {pv.ViewID}: already destroyed.");
            return;
        }

        if (_brokenHingeViewIDs.Add(pv.ViewID))
            LatePlugin.Log.LogDebug($"{LogPrefix} Marking hinge ViewID {pv.ViewID} as broken.");
    }

    #endregion


    #region Scene-Lifecycle

    /// <summary>Clears all tracked destruction states & the hinge cache. Call on level load.</summary>
    public static void ResetState()
    {
        LatePlugin.Log.LogDebug($"{LogPrefix} Clearing destruction/broken tracking & hinge cache.");
        _destroyedViewIDs.Clear();
        _brokenHingeViewIDs.Clear();
        _hingeCache = Array.Empty<PhysGrabHinge>();
        _hingeCacheLastRefreshTime = 0f;
    }

    #endregion


    #region Late-Join Sync

    /// <summary>Host-only: synchronises hinge state (broken/open/closed) to a late joiner.</summary>
    public static void SyncHingeStatesForPlayer(Player targetPlayer)
    {
        if (!PhotonUtilities.IsRealMasterClient() ||
            targetPlayer == null ||
            !PhotonNetwork.InRoom ||
            SemiFunc.RunIsLobbyMenu())
            return;

        if (!PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber)) return;
        if (_destroyedViewIDs.Count == 0 && _brokenHingeViewIDs.Count == 0) return;

        var targetNickname = targetPlayer.NickName ?? $"ActorNr {targetPlayer.ActorNumber}";
        var brokenField = ReflectionCache.PhysGrabHinge_BrokenField;
        var closedField = ReflectionCache.PhysGrabHinge_ClosedField;

        if (brokenField == null || closedField == null)
        {
            LatePlugin.Log.LogError($"{LogPrefix} Reflection field for hinge 'broken' or 'closed' " +
                                    "missing from ReflectionCache – cannot sync hinge states.");
            return;
        }

        var cachedHinges = GetCachedHinges();
        LatePlugin.Log.LogInfo($"{LogPrefix} Syncing ALL hinge states to {targetNickname} " +
                               $"(Total hinges in cache: {cachedHinges.Length}).");

        int brokenSyncCount = 0, openSyncCount = 0, closedSyncCount = 0;

        foreach (var hinge in cachedHinges)
        {
            if (hinge == null) continue;

            PhotonView? pv = PhotonUtilities.GetPhotonView(hinge);
            if (pv == null) continue;

            int viewID = pv.ViewID;

            if (_destroyedViewIDs.Contains(viewID)) continue;   // Object fully destroyed

            bool brokenOnHost, closedOnHost;

            try
            {
                brokenOnHost = (bool)(brokenField.GetValue(hinge) ?? false);
                closedOnHost = (bool)(closedField.GetValue(hinge) ?? true);
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"{LogPrefix} Reflection failed getting state for hinge {viewID}: {ex}");
                continue;
            }

            if (brokenOnHost)
            {
                _brokenHingeViewIDs.Add(viewID);                 // keep set in sync
                pv.RPC("HingeBreakRPC", targetPlayer);
                brokenSyncCount++;
            }
            else
            {
                _brokenHingeViewIDs.Remove(viewID);

                if (!closedOnHost)
                {
                    pv.RPC("OpenImpulseRPC", targetPlayer);
                    openSyncCount++;
                }
                else
                {
                    closedSyncCount++;
                }
            }
        }

        LatePlugin.Log.LogInfo($"{LogPrefix} Hinge sync to {targetNickname} finished – " +
                               $"Synced: {brokenSyncCount} Broken, {openSyncCount} Opened, {closedSyncCount} Confirmed Closed.");
    }

    #endregion


    #region Helpers

    private static PhysGrabHinge[] GetCachedHinges() =>
        GameUtilities.GetCachedComponents(ref _hingeCache, ref _hingeCacheLastRefreshTime, HingeCacheRefreshInterval);

    #endregion
}