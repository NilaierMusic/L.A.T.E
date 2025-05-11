// File: L.A.T.E/Patches/Objects/PhysGrabObjectPatches.cs
using HarmonyLib;

using Photon.Pun;

using LATE.Core;          // LatePlugin.Log
using LATE.Managers;      // DestructionManager

namespace LATE.Patches.Objects;

/// <summary>Harmony patches for <see cref="PhysGrabObject"/> and its impact detector.</summary>
[HarmonyPatch]
internal static class PhysGrabObjectPatches
{
    private const string LogPrefix = "[PhysGrabObjectPatches]";

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  PhysGrabObject.DestroyPhysGrabObject – PREFIX                           */
    /*───────────────────────────────────────────────────────────────────────────*/

    [HarmonyPatch(typeof(PhysGrabObject), nameof(PhysGrabObject.DestroyPhysGrabObject))]
    [HarmonyPrefix]
    private static bool DestroyPhysGrabObject_Prefix(PhysGrabObject __instance)
    {
        if (!PhotonNetwork.IsMasterClient || __instance == null) return true;

        if (TryGetView(__instance, out var pv))
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} DestroyPhysGrabObject: marking ViewID {pv.ViewID} as DESTROYED.");
            DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
            pv.RPC("DestroyPhysGrabObjectRPC", RpcTarget.AllBuffered);
        }

        return true; // always allow original method to run (extra cleanup etc.)
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  PhysGrabObjectImpactDetector.DestroyObject – PREFIX                     */
    /*───────────────────────────────────────────────────────────────────────────*/

    [HarmonyPatch(typeof(PhysGrabObjectImpactDetector), nameof(PhysGrabObjectImpactDetector.DestroyObject))]
    [HarmonyPrefix]
    private static bool ImpactDetector_DestroyObject_Prefix(PhysGrabObjectImpactDetector __instance, bool effects)
    {
        if (!PhotonNetwork.IsMasterClient || __instance == null) return true;

        if (__instance.GetComponent<PhysGrabObject>() is not { } pgo)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} ImpactDetector: no PhysGrabObject on '{__instance.gameObject.name}'.");
            return true;
        }

        if (TryGetView(pgo, out var pv))
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} ImpactDetector.DestroyObject: marking ViewID {pv.ViewID} as DESTROYED.");
            DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
            pv.RPC("DestroyObjectRPC", RpcTarget.AllBuffered, effects);
        }

        return true;
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Helper                                                                   */
    /*───────────────────────────────────────────────────────────────────────────*/

    private static bool TryGetView(UnityEngine.Component comp, out PhotonView pv)
    {
        pv = comp.GetComponent<PhotonView>();
        if (pv != null) return true;

        LatePlugin.Log.LogWarning($"{LogPrefix} Unable to retrieve PhotonView for '{comp.gameObject.name}'.");
        return false;
    }
}