// File: L.A.T.E/Patches/Objects/PhysGrabHingePatches.cs
using HarmonyLib;
using Photon.Pun;
using LATE.Core;
using LATE.Managers; // For DestructionManager

namespace LATE.Patches.Objects; // File-scoped namespace

/// <summary>
/// Contains Harmony patches for the PhysGrabHinge class.
/// </summary>
[HarmonyPatch]
internal static class PhysGrabHingePatches
{
    /// <summary>
    /// Prefix patch for PhysGrabHinge.DestroyHinge.
    /// Marks the hinge as destroyed (as a full object) and sends a buffered RPC.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabHinge), nameof(PhysGrabHinge.DestroyHinge))]
    [HarmonyPrefix]
    static bool PhysGrabHinge_DestroyHinge_Prefix(PhysGrabHinge __instance)
    {
        if (__instance != null && PhotonNetwork.IsMasterClient)
        {
            PhotonView? pv = __instance.GetComponent<PhotonView>();
            if (pv != null)
            {
                LatePlugin.Log.LogDebug(
                    $"[PhysGrabHingePatches] Prefix DestroyHinge for ViewID {pv.ViewID}. Marking object as destroyed."
                );
                DestructionManager.MarkObjectAsDestroyed(pv.ViewID);
                // Original DestroyHinge method will send the DestroyHingeRPC.
            }
            else
            {
                LatePlugin.Log.LogWarning(
                    $"[PhysGrabHingePatches] Unable to retrieve PhotonView for Hinge on {__instance.gameObject.name} during DestroyHinge."
                );
            }
        }
        return true; // Always run original method, which sends the RPC.
    }

    /// <summary>
    /// Prefix patch for PhysGrabHinge.HingeBreakImpulse.
    /// On the MasterClient, this marks the hinge as broken in the DestructionManager
    /// *before* the HingeBreakRPC is sent by the original method.
    /// </summary>
    [HarmonyPatch(typeof(PhysGrabHinge), "HingeBreakImpulse")] // This is a private method
    [HarmonyPrefix]
    static void PhysGrabHinge_HingeBreakImpulse_Prefix(PhysGrabHinge __instance)
    {
        // Only the MasterClient should mark the hinge as broken authoritatively.
        if (PhotonNetwork.IsMasterClient && __instance != null)
        {
            PhotonView? pv = __instance.GetComponent<PhotonView>();
            if (pv != null)
            {
                LatePlugin.Log.LogDebug(
                    $"[PhysGrabHingePatches] Prefix HingeBreakImpulse for ViewID {pv.ViewID}. Host marking hinge as broken."
                );
                DestructionManager.MarkHingeAsBroken(__instance, pv);
                // The original HingeBreakImpulse method will proceed to send the HingeBreakRPC.
            }
            else
            {
                LatePlugin.Log.LogWarning(
                    $"[PhysGrabHingePatches] Unable to retrieve PhotonView for Hinge on {__instance.gameObject.name} during HingeBreakImpulse prefix."
                );
            }
        }
    }
}