// File: L.A.T.E/Patches/Objects/PhysGrabHingePatches.cs
using HarmonyLib;

using Photon.Pun;

using LATE.Core;          // LatePlugin.Log
using LATE.Managers;      // DestructionManager

namespace LATE.Patches.Objects;

/// <summary>Harmony patches for <see cref="PhysGrabHinge"/>.</summary>
[HarmonyPatch]
internal static class PhysGrabHingePatches
{
    private const string LogPrefix = "[PhysGrabHingePatches]";

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  DestroyHinge – Prefix                                                  */
    /*───────────────────────────────────────────────────────────────────────────*/

    [HarmonyPatch(typeof(PhysGrabHinge), nameof(PhysGrabHinge.DestroyHinge))]
    [HarmonyPrefix]
    private static bool DestroyHinge_Prefix(PhysGrabHinge __instance)
    {
        if (!PhotonNetwork.IsMasterClient || __instance == null) return true;

        if (TryGetHingeView(__instance, out var pv))
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} DestroyHinge: marking ViewID {pv.ViewID} as destroyed.");
            DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
        }
        return true;  // always let original run (it sends DestroyHingeRPC)
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  HingeBreakImpulse – Prefix (private method)                             */
    /*───────────────────────────────────────────────────────────────────────────*/

    [HarmonyPatch(typeof(PhysGrabHinge), "HingeBreakImpulse")]   // private method
    [HarmonyPrefix]
    private static void HingeBreakImpulse_Prefix(PhysGrabHinge __instance)
    {
        if (!PhotonNetwork.IsMasterClient || __instance == null) return;

        if (TryGetHingeView(__instance, out var pv))
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} HingeBreakImpulse: marking ViewID {pv.ViewID} as BROKEN.");
            DestructionManager.MarkHingeAsBroken(__instance, pv);
        }
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  Helper                                                                   */
    /*───────────────────────────────────────────────────────────────────────────*/

    private static bool TryGetHingeView(PhysGrabHinge hinge, out PhotonView pv)
    {
        pv = hinge.GetComponent<PhotonView>();
        if (pv != null) return true;

        LatePlugin.Log.LogWarning($"{LogPrefix} Unable to retrieve PhotonView for hinge on '{hinge.gameObject.name}'.");
        return false;
    }
}