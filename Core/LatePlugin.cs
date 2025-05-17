// File: L.A.T.E/Core/LatePlugin.cs
using BepInEx;
using BepInEx.Logging;

using HarmonyLib;

using UnityEngine;                    // MonoBehaviour

using LATE.Config;
using LATE.DataModels;                // GameVersion enum
using LATE.Managers.GameState;        // GameVersionSupport
using LATE.Core;                      // CoroutineHelper, PatchManager

namespace LATE.Core;

[BepInPlugin(GUID, NAME, VERSION)]
internal sealed class LatePlugin : BaseUnityPlugin
{
    /* ──────────────────  Plugin constants  ────────────────── */

    private const string GUID = LATE.PluginInfo.PLUGIN_GUID;
    private const string NAME = LATE.PluginInfo.PLUGIN_NAME;
    private const string VERSION = LATE.PluginInfo.PLUGIN_VERSION;

    private const string LogPrefix = "[L.A.T.E]";

    /* ──────────────────  Public helpers  ──────────────────── */

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static MonoBehaviour? CoroutineRunner => CoroutineHelper.CoroutineRunner;

    /* ──────────────────  Private state  ───────────────────── */

    private static Harmony? _harmony;
    private bool _initialised;

    /* ──────────────────  Unity lifecycle  ─────────────────── */

    private void Awake()
    {
        if (_initialised) return;
        _initialised = true;

        // Instance = this; // Optional
        Log = Logger;
        CoroutineHelper.SetLatePluginInstance(this); // Register with CoroutineHelper
        _harmony = new Harmony(GUID);

        Log.LogInfo($"{LogPrefix} {NAME} v{VERSION} loading …");

        // Detect exe version (for Steam-lobby helpers)
        GameVersion gv = GameVersionSupport.DetectVersion();
        Log.LogInfo($"{LogPrefix} Detected game version: {gv}");
        if (gv == GameVersion.Unknown)
            Log.LogError($"{LogPrefix} Could not determine game version – lobby-management features may misbehave.");

        // Config
        ConfigManager.Initialize(Config);

        // Patches (delegated)
        PatchManager.InitializeAndApplyPatches(_harmony, Log);

        Log.LogInfo($"{LogPrefix} {NAME} loaded.");
    }

    private void OnDestroy()
    {
        // Unpatch cleanly on domain reload / manual unload
        if (_harmony != null)
        {
            _harmony.UnpatchSelf();
            _harmony = null;
            Log.LogInfo($"{LogPrefix} {NAME} patches removed (domain reload).");
        }
        CoroutineHelper.ClearCoroutineRunnerCache();
    }
}