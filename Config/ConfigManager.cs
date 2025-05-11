// File: L.A.T.E/Config/ConfigManager.cs
using BepInEx.Configuration;
using BepInEx.Logging;
using System.Reflection;
using LATE.Core; // For LatePlugin.Log
using LATE.Managers.GameState; // For GameVersionSupport
using Photon.Pun;
using LATE.Patches.CoreGame;  // For RunManagerPatches state access
using LATE.Utilities;       // For PhotonUtilities, GameUtilities

namespace LATE.Config; // File-scoped namespace

/// <summary>
/// Manages the plugin's configuration settings, loading them from the BepInEx config file.
/// </summary>
internal static class ConfigManager
{
    #region Section Constants

    private const string SectionGeneral = "General";
    private const string SectionLateJoinBehavior = "Late Join Behavior";
    private const string SectionLobbyVisibility = "Lobby Visibility"; // New section
    private const string SectionAdvanced = "Advanced (Use With Caution)";
    private const string SectionDebug = "Debugging";

    #endregion

    #region Public Config Entries

    // General Options
    internal static ConfigEntry<bool> AllowInShop { get; private set; } = null!;
    internal static ConfigEntry<bool> AllowInTruck { get; private set; } = null!;
    internal static ConfigEntry<bool> AllowInLevel { get; private set; } = null!;
    internal static ConfigEntry<bool> AllowInArena { get; private set; } = null!;

    // Late-Join Behavior
    internal static ConfigEntry<bool> KillIfPreviouslyDead { get; private set; } = null!;
    internal static ConfigEntry<bool> SpawnAtLastPosition { get; private set; } = null!;
    internal static ConfigEntry<bool> LockLobbyOnLevelGenerationFailure { get; private set; } = null!;

    // Lobby Visibility
    internal static ConfigEntry<bool> KeepPublicLobbyListed { get; private set; } = null!;

    // Advanced Options
    internal static ConfigEntry<bool> ForceReloadOnLateJoin { get; private set; } = null!;

    // Debug Options
    internal static ConfigEntry<LogLevel> ModLogLevel { get; private set; } = null!;

    #endregion

    #region Private Helpers

    private static ManualLogSource Log => LatePlugin.Log;

    #endregion

    #region Public Initialization

    /// <summary>
    /// Initializes the configuration by binding all settings to the provided BepInEx ConfigFile.
    /// This should be called once from the plugin’s Awake() method.
    /// </summary>
    /// <param name="cfg">The BepInEx ConfigFile instance.</param>
    internal static void Initialize(ConfigFile cfg)
    {
        Log.LogDebug("[Config] Binding entries...");

        // General
        AllowInShop = Bind(cfg, SectionGeneral, "Allow in shop", true, "Allow players to join while the host is in the shop.");
        AllowInTruck = Bind(cfg, SectionGeneral, "Allow in truck", true, "Allow players to join while the host is in the truck.");
        AllowInLevel = Bind(cfg, SectionGeneral, "Allow in level", true, "Allow players to join while the host is in an active level.");
        AllowInArena = Bind(cfg, SectionGeneral, "Allow in arena", true, "Allow players to join while the host is in the arena.");

        // Late-Join Behavior
        KillIfPreviouslyDead = Bind(cfg, SectionLateJoinBehavior, "Kill If Previously Dead", true, "Automatically kill late-joining players who already died in the same level.");
        SpawnAtLastPosition = Bind(cfg, SectionLateJoinBehavior, "Spawn At Last Position", true, "Spawn re-joining players at their last known position (or death head).");
        LockLobbyOnLevelGenerationFailure = Bind(cfg, SectionLateJoinBehavior, "Lock Lobby On Level Generation Failure", true,
            "Controls if the lobby is automatically locked if a level (especially modded) reports a generation failure.\n" +
            "Vanilla levels rarely fail, but modded ones might sometimes report failure even if they load.\n" +
            "Set to 'false' to keep the lobby open based on scene type, even on reported failure (unless it's a real crash to Arena).\n" +
            "Default: true (locks lobby on non-Arena failure).");

        // Lobby Visibility
        KeepPublicLobbyListed = Bind(cfg, SectionLobbyVisibility, "Keep Public Lobby Listed After Lobby Menu", true,
            "If true (default), the lobby will always try to be publicly listed when late-joining is allowed by other settings.\n" +
            "If false, the lobby is only publicly listed during the initial Lobby Menu session. After the first game starts, it becomes invite-only (still joinable via invites if late-joining is allowed for the current scene, but not in public server lists).");

        // Advanced
        ForceReloadOnLateJoin = Bind(cfg, SectionAdvanced, "Force Level Reload on Late Join", false, "!! HIGHLY DISRUPTIVE !! Forces the host to reload the level for EVERYONE when a player joins late.");

        // Debugging
        ModLogLevel = cfg.Bind(
            SectionDebug,
            "Log Level",
            LogLevel.Info,
            "Minimum log level for L.A.T.E.\n"
          + "Change on the fly to reduce spam or get more detail.\n"
          + "Values: Fatal, Error, Warning, Message, Info, Debug, All, None"
        );

        // Apply current value and react to future edits.
        ApplyLogLevel(ModLogLevel.Value);
        ModLogLevel.SettingChanged += (_, __) => ApplyLogLevel(ModLogLevel.Value);
        KeepPublicLobbyListed.SettingChanged += OnKeepPublicLobbyListedChanged; // Attach event handler


        Log.LogDebug("[Config] All entries bound");
    }

    #endregion

    #region Config Event Handler

    private static void OnKeepPublicLobbyListedChanged(object? sender, EventArgs e)
    {
        LatePlugin.Log.LogInfo($"[Config] Runtime: KeepPublicLobbyListed changed to: {KeepPublicLobbyListed.Value}");

        if (!PhotonUtilities.IsRealMasterClient() || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            LatePlugin.Log.LogDebug("[Config] Live update skipped: Not master or not in room/CurrentRoom is null.");
            return;
        }

        // Check if the game is in a state where the lobby *should* be open/considered for listing
        if (RunManagerPatches.GetShouldOpenLobbyAfterGen())
        {
            bool isCurrentlyPublicPhase = !RunManagerPatches.GetInitialPublicListingPhaseComplete();
            bool makeVisibleAndPublic = KeepPublicLobbyListed.Value || isCurrentlyPublicPhase;

            LatePlugin.Log.LogInfo(
                $"[Config] Applying KeepPublicLobbyListed change live. Current Public Phase: {isCurrentlyPublicPhase}, Effective Visibility/Public: {makeVisibleAndPublic}"
            );

            // Only change IsVisible if the room is already Open.
            // If IsOpen is false, IsVisible being true is meaningless and might be misleading.
            // GameDirectorPatches will handle setting IsOpen and IsVisible correctly when the lobby state is next evaluated.
            // Here, we only adjust IsVisible if the lobby is *already meant to be open*.
            if (PhotonNetwork.CurrentRoom.IsOpen)
            {
                PhotonNetwork.CurrentRoom.IsVisible = makeVisibleAndPublic;
                LatePlugin.Log.LogDebug($"[Config] Live update: Photon IsVisible set to {PhotonNetwork.CurrentRoom.IsVisible} (IsOpen was true).");
            }
            else
            {
                LatePlugin.Log.LogDebug($"[Config] Live update: Photon IsOpen is false. IsVisible not changed by live config update (will be set by GameDirectorPatches).");
            }

            // The Steam lobby unlock should also reflect the desired public/private state.
            GameVersionSupport.UnlockSteamLobby(makeVisibleAndPublic);
            LatePlugin.Log.LogDebug($"[Config] Live update: Steam lobby {(makeVisibleAndPublic ? "unlocked publicly" : "unlocked as joinable but private/friends")}.");
        }
        else
        {
            LatePlugin.Log.LogDebug("[Config] Live update skipped: Lobby is not in a state where it should be open/listed right now (_shouldOpenLobbyAfterGen is false).");
        }
    }

    #endregion

    #region Internal Helpers

    private static ConfigEntry<T> Bind<T>(ConfigFile cfg, string section, string key, T defaultValue, string description)
    {
        return cfg.Bind(section, key, defaultValue, description);
    }

    /// <summary>
    /// Sets the internal log filter level even on older BepInEx builds
    /// that don’t expose a public <c>Level</c> property.
    /// </summary>
    private static void ApplyLogLevel(LogLevel newLevel)
    {
        var levelProperty = typeof(ManualLogSource).GetProperty("Level", BindingFlags.Instance | BindingFlags.Public);
        if (levelProperty != null && levelProperty.CanWrite)
        {
            levelProperty.SetValue(Log, newLevel);
        }
        else
        {
            var levelField = typeof(ManualLogSource).GetField("level", BindingFlags.Instance | BindingFlags.NonPublic);
            if (levelField != null)
            {
                levelField.SetValue(Log, newLevel);
            }
        }
        Log.LogInfo($"[Config] Runtime log level set to <{newLevel}>");
    }
    #endregion
}