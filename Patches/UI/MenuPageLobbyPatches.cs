// File: L.A.T.E/Patches/UI/MenuPageMainPatches.cs
using HarmonyLib;

using LATE.Core;                 // LatePlugin.Log
using LATE.Patches.CoreGame;     // RunManagerPatches

namespace LATE.Patches.UI;

/// <summary>Harmony patches for <see cref="MenuPageMain"/> buttons.</summary>
[HarmonyPatch]
internal static class MenuPageMainPatches
{
    private const string LogPrefix = "[MenuPageMainPatches]";

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  ButtonEventHostGame – PREFIX                                            */
    /*───────────────────────────────────────────────────────────────────────────*/

    [HarmonyPatch(typeof(MenuPageMain), nameof(MenuPageMain.ButtonEventHostGame))]
    [HarmonyPrefix]
    private static void ButtonEventHostGame_Prefix()
    {
        RunManagerPatches.SetInitialPublicListingPhaseComplete(false);
        LatePlugin.Log.LogInfo($"{LogPrefix} Reset initial public-listing phase (HostGame).");
    }

    /*───────────────────────────────────────────────────────────────────────────*/
    /*  ButtonEventPlayRandom – PREFIX                                          */
    /*───────────────────────────────────────────────────────────────────────────*/

    [HarmonyPatch(typeof(MenuPageMain), nameof(MenuPageMain.ButtonEventPlayRandom))]
    [HarmonyPrefix]
    private static void ButtonEventPlayRandom_Prefix()
    {
        RunManagerPatches.SetInitialPublicListingPhaseComplete(false);
        LatePlugin.Log.LogInfo($"{LogPrefix} Reset initial public-listing phase (PlayRandom).");
    }
}