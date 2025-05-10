// File: L.A.T.E/Utilities/PhotonUtilities.cs
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using UnityEngine; // For Component, PhysGrabObject
using Hashtable = ExitGames.Client.Photon.Hashtable;
using LATE.Core; // For LatePlugin.Log

namespace LATE.Utilities; // File-scoped namespace

/// <summary>
/// Provides utility methods specifically related to Photon Networking.
/// </summary>
internal static class PhotonUtilities
{
    /// <summary>
    /// Checks if the local client is currently the authoritative Master Client.
    /// This is more specific than just PhotonNetwork.IsMasterClient, as it ensures
    /// the local player holds the master client role.
    /// </summary>
    /// <returns>True if the local player is the Master Client, false otherwise.</returns>
    public static bool IsRealMasterClient()
    {
        return PhotonNetwork.IsMasterClient
            && PhotonNetwork.MasterClient == PhotonNetwork.LocalPlayer;
    }

    /// <summary>
    /// Clears a specific object associated with a PhotonView from the Photon room cache.
    /// This is crucial for preventing issues when players join late or objects are destroyed.
    /// </summary>
    /// <param name="photonView">The PhotonView of the object to remove from cache.</param>
    public static void ClearPhotonCache(PhotonView photonView)
    {
        if (photonView == null)
        {
            LatePlugin.Log.LogWarning("[PhotonUtils] ClearPhotonCache called with null PhotonView.");
            return;
        }

        try
        {
            // Access fields from ReflectionCache
            var removeFilter = ReflectionCache.PhotonNetwork_RemoveFilterField?.GetValue(null) as Hashtable;
            var keyByteSeven = ReflectionCache.PhotonNetwork_KeyByteSevenField?.GetValue(null);
            var serverCleanOptions = ReflectionCache.PhotonNetwork_ServerCleanOptionsField?.GetValue(null) as RaiseEventOptions;
            var raiseEventMethod = ReflectionCache.PhotonNetwork_RaiseEventInternalMethod;

            if (removeFilter == null || keyByteSeven == null || serverCleanOptions == null || raiseEventMethod == null)
            {
                LatePlugin.Log.LogError("[PhotonUtils] ClearPhotonCache failed: Reflection error getting PhotonNetwork internals from ReflectionCache.");
                return;
            }

            removeFilter[keyByteSeven] = photonView.InstantiationId;
            serverCleanOptions.CachingOption = EventCaching.RemoveFromRoomCache;

            raiseEventMethod.Invoke(
                null,
                new object[]
                {
                    (byte)202, // EventCode.CleanPhotonView (from internal Photon constant, assuming 202 is correct)
                    removeFilter,
                    serverCleanOptions,
                    SendOptions.SendReliable,
                }
            );

            LatePlugin.Log.LogDebug($"[PhotonUtils] Sent RemoveFromRoomCache event using InstantiationId {photonView.InstantiationId} (ViewID: {photonView.ViewID})");
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"[PhotonUtils] Exception during ClearPhotonCache for InstantiationId {photonView.InstantiationId} (ViewID: {photonView.ViewID}): {ex}");
        }
    }

    /// <summary>
    /// Helper to get the internal PhotonView from a PhysGrabObject using reflection.
    /// </summary>
    /// <param name="pgo">The PhysGrabObject instance.</param>
    /// <returns>The PhotonView, or null if not found or reflection failed.</returns>
    public static PhotonView? GetPhotonViewFromPGO(PhysGrabObject? pgo)
    {
        if (pgo == null || ReflectionCache.PhysGrabObject_PhotonViewField == null)
            return null;

        try
        {
            return ReflectionCache.PhysGrabObject_PhotonViewField.GetValue(pgo) as PhotonView;
        }
        catch (Exception ex)
        {
            LatePlugin.Log?.LogError($"[PhotonUtils] Reflection error getting PhotonView from PGO '{pgo.gameObject?.name ?? "NULL"}': {ex}");
            return null;
        }
    }

    /// <summary>
    /// Helper to get the PhotonView from a component, checking common locations.
    /// Prioritizes direct cast, then GetComponent, then specific reflection for PlayerAvatar.
    /// </summary>
    /// <param name="component">The component to check.</param>
    /// <returns>The PhotonView, or null if not found.</returns>
    public static PhotonView? GetPhotonView(Component? component)
    {
        if (component == null)
            return null;

        if (component is PhotonView directView)
            return directView;

        var viewOnGameObject = component.GetComponent<PhotonView>();
        if (viewOnGameObject != null)
            return viewOnGameObject;

        if (component is PlayerAvatar playerAvatar && ReflectionCache.PlayerAvatar_PhotonViewField != null)
        {
            try
            {
                return ReflectionCache.PlayerAvatar_PhotonViewField.GetValue(playerAvatar) as PhotonView;
            }
            catch (Exception ex)
            {
                LatePlugin.Log?.LogWarning($"[PhotonUtils] Failed to get PlayerAvatar's PhotonView via reflection: {ex}");
            }
        }
        return null;
    }

    /// <summary>
    /// Convenience wrapper →  returns a component's PhotonView.ViewID or –1 on failure.
    /// </summary>
    public static int GetViewId(Component? comp)
    {
        return GetPhotonView(comp)?.ViewID ?? -1;
    }
}