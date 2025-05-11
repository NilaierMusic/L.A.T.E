// File: L.A.T.E/Patches/UI/MenuPageMainPatches.cs
using HarmonyLib;
using LATE.Core;
using LATE.Patches.CoreGame; // For RunManagerPatches

namespace LATE.Patches.UI;

[HarmonyPatch]
internal static class MenuPageMainPatches
{
    [HarmonyPatch(typeof(MenuPageMain), nameof(MenuPageMain.ButtonEventHostGame))]
    [HarmonyPrefix]
    static void MenuPageMain_ButtonEventHostGame_Prefix()
    {
        RunManagerPatches.SetInitialPublicListingPhaseComplete(false);
        LatePlugin.Log.LogInfo("[MenuPageMainPatches] Resetting initial public listing phase for new hosting session (HostGame).");
    }

    [HarmonyPatch(typeof(MenuPageMain), nameof(MenuPageMain.ButtonEventPlayRandom))]
    [HarmonyPrefix]
    static void MenuPageMain_ButtonEventPlayRandom_Prefix()
    {
        RunManagerPatches.SetInitialPublicListingPhaseComplete(false);
        LatePlugin.Log.LogInfo("[MenuPageMainPatches] Resetting initial public listing phase for new hosting session (PlayRandom).");
    }
}