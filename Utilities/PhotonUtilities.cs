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

        try
        {
            var removeFilter = ReflectionCache.PhotonNetwork_RemoveFilterField?.GetValue(null) as Hashtable;
            var keyByte7 = ReflectionCache.PhotonNetwork_KeyByteSevenField?.GetValue(null);
            var cleanOpts = ReflectionCache.PhotonNetwork_ServerCleanOptionsField?.GetValue(null) as RaiseEventOptions;
            var raiseEvent = ReflectionCache.PhotonNetwork_RaiseEventInternalMethod;

            if (removeFilter == null || keyByte7 == null || cleanOpts == null || raiseEvent == null)
            {
                LatePlugin.Log.LogError($"{LogPrefix} Reflection failure – Photon internals missing. Abort.");
                return;
            }

            removeFilter[keyByte7] = pv.InstantiationId;
            cleanOpts.CachingOption = EventCaching.RemoveFromRoomCache;

            raiseEvent.Invoke(
                null,
                new object[] { (byte)202 /*CleanPhotonView*/, removeFilter, cleanOpts, SendOptions.SendReliable });

            LatePlugin.Log.LogDebug($"{LogPrefix} Sent RemoveFromRoomCache (InstId:{pv.InstantiationId}, ViewID:{pv.ViewID}).");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} ClearPhotonCache exception for InstId {pv.InstantiationId} (ViewID:{pv.ViewID}): {ex}");
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