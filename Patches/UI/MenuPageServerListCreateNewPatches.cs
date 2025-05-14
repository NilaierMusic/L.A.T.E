// File: L.A.T.E/Patches/UI/MenuPageServerListCreateNewPatches.cs
using HarmonyLib;

using LATE.Core;                 // LatePlugin.Log
using LATE.Patches.CoreGame;     // RunManagerPatches

namespace LATE.Patches.UI;

/// <summary>Harmony patch for “Create New” in the server-list menu.</summary>
[HarmonyPatch]
internal static class MenuPageServerListCreateNewPatches
{
    private const string LogPrefix = "[MenuPageServerListCreateNewPatches]";

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  ButtonConfirm – PREFIX                                                  */
    /*───────────────────────────────────────────────────────────────────────────*/
    /*
    [HarmonyPatch(typeof(MenuPageServerListCreateNew), nameof(MenuPageServerListCreateNew.ButtonConfirm))]
    [HarmonyPrefix]
    private static void ButtonConfirm_Prefix()
    {
        // Starting a fresh public game → reset listing-phase flag
        RunManagerPatches.SetInitialPublicListingPhaseComplete(false);
        LatePlugin.Log.LogInfo($"{LogPrefix} Reset initial public-listing phase for new custom public game.");
    }*/
}