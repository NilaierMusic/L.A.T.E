// File: L.A.T.E/Patches/Objects/PhysGrabObjectPatches.cs
using HarmonyLib;
using Photon.Pun;
using LATE.Core;
using LATE.Managers; // For DestructionManager
// Removed LATE.Utilities as it's not directly used by these specific patches

namespace LATE.Patches.Objects; // File-scoped namespace

/// <summary>
/// Contains Harmony patches for PhysGrabObject and related classes like PhysGrabObjectImpactDetector.
/// </summary>
[HarmonyPatch]
internal static class PhysGrabObjectPatches
{
    /// <summary>
    /// Prefix patch for PhysGrabObject.DestroyPhysGrabObject.
    /// Marks the object as destroyed and sends a buffered RPC to ensure all clients (including late joiners) see the destruction.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObject), nameof(PhysGrabObject.DestroyPhysGrabObject))]
    [HarmonyPrefix]
    static bool PhysGrabObject_DestroyPhysGrabObject_Prefix(PhysGrabObject __instance)
    {
        if (__instance != null && PhotonNetwork.IsMasterClient)
        {
            PhotonView? pv = __instance.GetComponent<PhotonView>(); // PhysGrabObject usually has a PhotonView
            if (pv != null)
            {
                LatePlugin.Log.LogDebug(
                    $"[PhysGrabObjectPatches] Intercepting DestroyPhysGrabObject for ViewID {pv.ViewID}. Sending buffered RPC and marking."
                );
                DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
                pv.RPC("DestroyPhysGrabObjectRPC", RpcTarget.AllBuffered); // Ensure this RPC is defined in PhysGrabObject
            }
            else
            {
                LatePlugin.Log.LogWarning(
                    $"[PhysGrabObjectPatches] Unable to retrieve PhotonView for PhysGrabObject on {__instance.gameObject.name} during DestroyPhysGrabObject."
                );
            }
        }
        return true; // Always run original method (it might do more cleanup)
    }

    /// <summary>
    /// Prefix patch for PhysGrabObjectImpactDetector.DestroyObject.
    /// Marks the object as destroyed and sends a buffered RPC.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.DestroyObject))]
    [HarmonyPrefix]
    static bool PhysGrabImpact_DestroyObject_Prefix(PhysGrabObjectImpactDetector __instance, bool effects)
    {
        if (__instance != null && PhotonNetwork.IsMasterClient)
        {
            PhysGrabObject? pgo = __instance.GetComponent<PhysGrabObject>(); // The detector is usually on the same GO as the PGO
            if (pgo != null)
            {
                PhotonView? pv = pgo.GetComponent<PhotonView>();
                if (pv != null)
                {
                    LatePlugin.Log.LogDebug(
                        $"[PhysGrabObjectPatches] Intercepting ImpactDetector.DestroyObject for ViewID {pv.ViewID}. Sending buffered RPC and marking."
                    );
                    DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
                    // Assuming DestroyObjectRPC exists on PhysGrabObject and handles the 'effects' parameter
                    pv.RPC("DestroyObjectRPC", RpcTarget.AllBuffered, effects);
                }
                else
                {
                    LatePlugin.Log.LogWarning(
                        $"[PhysGrabObjectPatches] Unable to retrieve PhotonView for PhysGrabObject on {pgo.gameObject.name} during DestroyObject via ImpactDetector."
                    );
                }
            }
            else
            {
                LatePlugin.Log.LogWarning(
                    $"[PhysGrabObjectPatches] Unable to retrieve PhysGrabObject for ImpactDetector on {__instance.gameObject.name} during DestroyObject."
                );
            }
        }
        return true; // Always run original method
    }
}