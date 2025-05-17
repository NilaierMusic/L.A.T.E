// File: L.A.T.E/Utilities/PhotonUtilities.cs
using ExitGames.Client.Photon;
using LATE.Core;                               // LatePlugin.Log
using Photon.Pun;
using Photon.Realtime;
using Component = UnityEngine.Component;

namespace LATE.Utilities;

/// <summary>
/// Utility helpers that wrap / augment Photon functionality.
/// </summary>
internal static class PhotonUtilities
{
    private const string LogPrefix = "[PhotonUtils]";

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Master-client check                                                    */
    /*───────────────────────────────────────────────────────────────────────────*/

    public static bool IsRealMasterClient() =>
        PhotonNetwork.IsMasterClient &&
        PhotonNetwork.MasterClient == PhotonNetwork.LocalPlayer;

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Cache-clear helper                                                     */
    /*───────────────────────────────────────────────────────────────────────────*/

    public static void ClearPhotonCache(PhotonView pv)
    {
        if (pv == null)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} ClearPhotonCache called with NULL PhotonView.");
            return;
        }
        if (pv.InstantiationId <= 0)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} ClearPhotonCache called for PhotonView with no InstantiationId (ViewID:{pv.ViewID}). Scene object?");
            return;
        }
        ClearPhotonCacheByInstantiationId(pv.InstantiationId, pv.ViewID);
    }

    // New overload
    public static void ClearPhotonCacheByInstantiationId(int instantiationId, int sourceViewIdForLog = 0)
    {
        if (instantiationId <= 0)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} ClearPhotonCacheByInstantiationId called with invalid InstantiationId: {instantiationId}.");
            return;
        }

        try
        {
            // These are static fields, so fetching them each time is okay.
            var removeFilter = ReflectionCache.PhotonNetwork_RemoveFilterField?.GetValue(null) as Hashtable;
            var keyByte7 = ReflectionCache.PhotonNetwork_KeyByteSevenField?.GetValue(null);
            // IMPORTANT: PhotonNetwork.ServerCleanOptions is a shared instance.
            // We must set its CachingOption specifically for this call.
            var cleanOpts = ReflectionCache.PhotonNetwork_ServerCleanOptionsField?.GetValue(null) as RaiseEventOptions;
            var raiseEvent = ReflectionCache.PhotonNetwork_RaiseEventInternalMethod;

            if (removeFilter == null || keyByte7 == null || cleanOpts == null || raiseEvent == null)
            {
                LatePlugin.Log.LogError($"{LogPrefix} Reflection failure – Photon internals missing for ClearPhotonCacheByInstantiationId. Abort.");
                return;
            }

            // Prepare the filter for the specific instantiation ID
            removeFilter[keyByte7] = instantiationId;

            // Set the caching option for this specific operation
            cleanOpts.CachingOption = EventCaching.RemoveFromRoomCache; // This modifies the static instance for this call.

            raiseEvent.Invoke(
                null,
                new object[] { (byte)202 /*CleanPhotonView*/, removeFilter, cleanOpts, SendOptions.SendReliable });

            LatePlugin.Log.LogDebug($"{LogPrefix} Sent RemoveFromRoomCache (InstId:{instantiationId}, OrigViewID for log:{sourceViewIdForLog}).");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} ClearPhotonCacheByInstantiationId exception for InstId {instantiationId} (OrigViewID for log:{sourceViewIdForLog}): {ex}");
        }
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Reflection helpers                                                     */
    /*───────────────────────────────────────────────────────────────────────────*/

    public static PhotonView? GetPhotonViewFromPGO(PhysGrabObject? pgo)
    {
        if (pgo == null || ReflectionCache.PhysGrabObject_PhotonViewField == null) return null;

        try
        {
            return ReflectionCache.PhysGrabObject_PhotonViewField.GetValue(pgo) as PhotonView;
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} Reflection error getting PhotonView from PGO '{pgo.gameObject?.name ?? "NULL"}': {ex}");
            return null;
        }
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Generic “get PV” helper                                                */
    /*───────────────────────────────────────────────────────────────────────────*/

    public static PhotonView? GetPhotonView(Component? comp)
    {
        if (comp == null) return null;

        if (comp is PhotonView direct) return direct;

        if (comp.TryGetComponent(out PhotonView attached)) return attached;

        if (comp is PlayerAvatar avatar && ReflectionCache.PlayerAvatar_PhotonViewField != null)
        {
            try
            {
                return ReflectionCache.PlayerAvatar_PhotonViewField.GetValue(avatar) as PhotonView;
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogWarning($"{LogPrefix} Failed reflecting PlayerAvatar.PhotonView: {ex}");
            }
        }
        return null;
    }

    /// <summary>Convenience wrapper: returns a component’s ViewID or –1 on failure.</summary>
    public static int GetViewId(Component? comp) => GetPhotonView(comp)?.ViewID ?? -1;
}