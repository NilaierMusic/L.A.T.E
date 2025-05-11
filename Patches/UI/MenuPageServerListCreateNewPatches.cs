// File: L.A.T.E/Patches/UI/MenuPageServerListCreateNewPatches.cs
using HarmonyLib;
using LATE.Core;
using LATE.Patches.CoreGame; // For RunManagerPatches

namespace LATE.Patches.UI;

[HarmonyPatch]
internal static class MenuPageServerListCreateNewPatches
{
    [HarmonyPatch(typeof(MenuPageServerListCreateNew), nameof(MenuPageServerListCreateNew.ButtonConfirm))]
    [HarmonyPrefix]
    static void MenuPageServerListCreateNew_ButtonConfirm_Prefix()
    {
        // This button leads to creating a new public game.
        RunManagerPatches.SetInitialPublicListingPhaseComplete(false);
        LatePlugin.Log.LogInfo("[MenuPageServerListCreateNewPatches] Resetting initial public listing phase for new custom public game creation.");
    }
}