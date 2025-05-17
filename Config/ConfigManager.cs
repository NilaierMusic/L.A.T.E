// File: L.A.T.E/Config/ConfigManager.cs
using System.Reflection;

using BepInEx.Configuration;
using BepInEx.Logging;

using Photon.Pun;

using LATE.Core;               // LatePlugin.Log
using LATE.Managers.GameState; // GameVersionSupport
using LATE.Patches.CoreGame;   // RunManagerPatches
using LATE.Utilities;          // PhotonUtilities

namespace LATE.Config;

/// <summary>
/// Central place for every BepInEx config entry used by the mod.
/// •  Entries are grouped in nested static classes (General, LateJoin, …)  
/// •  Legacy flat aliases are kept at the bottom so existing call-sites still compile.
/// </summary>
internal static class ConfigManager
{
    /* ───────── helpers ───────── */

    private static ManualLogSource Log => LatePlugin.Log;

    private static ConfigEntry<bool> Bool(ConfigFile cfg, string section, string key, bool def, string desc) =>
        cfg.Bind(section, key, def, desc);

    private static ConfigEntry<float> Float(ConfigFile cfg, string section, string key, float def, string desc) =>
    cfg.Bind(section, key, def, desc);

    private static ConfigEntry<TEnum> Enum<TEnum>(ConfigFile cfg, string section, string key, TEnum def, string desc)
        where TEnum : struct, Enum =>
        cfg.Bind(section, key, def, desc);

    /* ───────── General ───────── */

    internal static class General
    {
        public static ConfigEntry<bool> AllowInShop { get; private set; } = null!;
        public static ConfigEntry<bool> AllowInTruck { get; private set; } = null!;
        public static ConfigEntry<bool> AllowInLevel { get; private set; } = null!;
        public static ConfigEntry<bool> AllowInArena { get; private set; } = null!;

        internal static void Bind(ConfigFile cfg)
        {
            const string S = nameof(General);
            AllowInShop = Bool(cfg, S, "Allow in shop", true, "Allow players to join while the host is in the shop.");
            AllowInTruck = Bool(cfg, S, "Allow in truck", true, "Allow players to join while the host is in the truck.");
            AllowInLevel = Bool(cfg, S, "Allow in level", true, "Allow players to join while the host is in an active level.");
            AllowInArena = Bool(cfg, S, "Allow in arena", true, "Allow players to join while the host is in the arena.");
        }
    }

    /* ───────── Late-join behaviour ───────── */

    internal static class LateJoin
    {
        public static ConfigEntry<bool> KillIfPreviouslyDead { get; private set; } = null!;
        public static ConfigEntry<bool> SpawnAtLastPosition { get; private set; } = null!;
        public static ConfigEntry<bool> LockLobbyOnLevelGenFail { get; private set; } = null!;
        public static ConfigEntry<float> MinSpawnDistance { get; private set; } = null!;

        internal static void Bind(ConfigFile cfg)
        {
            const string S = "Late Join Behaviour";
            KillIfPreviouslyDead = Bool(cfg, S, "Kill if previously dead", true,
                "Automatically kill late-joiners who already died in the same level.");
            SpawnAtLastPosition = Bool(cfg, S, "Spawn at last position", true,
                "Spawn re-joining players at their last known position (or death head).");
            LockLobbyOnLevelGenFail = Bool(cfg, S, "Lock lobby on level generation failure", true,
                "If true, lobby locks when a level reports generation failure.");
            MinSpawnDistance = Float(cfg, S, "Minimum Spawn Distance", 10.0f,
                "Minimum distance (in meters) between spawning players to consider a spawn point 'free'.");
        }
    }

    /* ───────── Lobby visibility ───────── */

    internal static class Lobby
    {
        public static ConfigEntry<bool> KeepPublicListed { get; private set; } = null!;

        internal static void Bind(ConfigFile cfg)
        {
            const string S = nameof(Lobby);
            KeepPublicListed = Bool(cfg, S, "Keep public lobby listed", true,
                "If true (default) the lobby stays publicly listed when late-joining is allowed.");

            KeepPublicListed.SettingChanged += OnVisibilityChanged;
        }

        private static void OnVisibilityChanged(object? _, EventArgs __)
        {
            Log.LogInfo($"[Config] Runtime: KeepPublicLobbyListed changed to {KeepPublicListed.Value}");

            if (!PhotonUtilities.IsRealMasterClient() || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
                return;

            if (!RunManagerPatches.GetShouldOpenLobbyAfterGen()) return;

            bool publicPhase = !RunManagerPatches.GetInitialPublicListingPhaseComplete();
            bool makeVisible = KeepPublicListed.Value || publicPhase;

            if (PhotonNetwork.CurrentRoom.IsOpen)
                PhotonNetwork.CurrentRoom.IsVisible = makeVisible;

            GameVersionSupport.UnlockSteamLobby(makeVisible);
        }
    }

    /* ───────── Advanced / Debug ───────── */

    internal static class Advanced
    {
        public static ConfigEntry<bool> ForceReloadOnLateJoin { get; private set; } = null!;

        internal static void Bind(ConfigFile cfg)
        {
            const string S = nameof(Advanced);
            ForceReloadOnLateJoin = Bool(cfg, S, "Force level reload on late join", false,
                "!! HIGHLY DISRUPTIVE !! Forces a full level reload when someone joins late.");
        }
    }

    internal static class Debug
    {
        public static ConfigEntry<LogLevel> LogLevelEntry { get; private set; } = null!;

        internal static void Bind(ConfigFile cfg)
        {
            const string S = nameof(Debug);
            LogLevelEntry = Enum(cfg, S, "Log level", LogLevel.Info,
                "Minimum log level for L.A.T.E.  Values: Fatal, Error, Warning, Message, Info, Debug, All, None");

            ApplyLogLevel(LogLevelEntry.Value);
            LogLevelEntry.SettingChanged += (_, __) => ApplyLogLevel(LogLevelEntry.Value);
        }
    }

    /* ───────── Initialise all groups ───────── */

    internal static void Initialize(ConfigFile cfg)   // kept original name
    {
        Log.LogDebug("[Config] Binding entries …");

        General.Bind(cfg);
        LateJoin.Bind(cfg);
        Lobby.Bind(cfg);
        Advanced.Bind(cfg);
        Debug.Bind(cfg);

        Log.LogDebug("[Config] All entries bound.");
    }

    /* ───────── helper: apply log-level ───────── */

    private static void ApplyLogLevel(LogLevel level)
    {
        var prop = typeof(ManualLogSource).GetProperty("Level", BindingFlags.Instance | BindingFlags.Public);
        if (prop?.CanWrite == true) prop.SetValue(Log, level);
        else typeof(ManualLogSource)
             .GetField("level", BindingFlags.Instance | BindingFlags.NonPublic)?
             .SetValue(Log, level);

        Log.LogInfo($"[Config] Runtime log level set to <{level}>");
    }

    /* ───────── Legacy flat aliases (back-compat) ───────── */

    #region Legacy-Aliases

    // General
    internal static ConfigEntry<bool> AllowInShop => General.AllowInShop;
    internal static ConfigEntry<bool> AllowInTruck => General.AllowInTruck;
    internal static ConfigEntry<bool> AllowInLevel => General.AllowInLevel;
    internal static ConfigEntry<bool> AllowInArena => General.AllowInArena;

    // Late-join
    internal static ConfigEntry<bool> KillIfPreviouslyDead => LateJoin.KillIfPreviouslyDead;
    internal static ConfigEntry<bool> SpawnAtLastPosition => LateJoin.SpawnAtLastPosition;
    internal static ConfigEntry<bool> LockLobbyOnLevelGenerationFailure => LateJoin.LockLobbyOnLevelGenFail;
    internal static ConfigEntry<float> MinSpawnDistance => LateJoin.MinSpawnDistance;

    // Lobby
    internal static ConfigEntry<bool> KeepPublicLobbyListed => Lobby.KeepPublicListed;

    // Advanced
    internal static ConfigEntry<bool> ForceReloadOnLateJoin => Advanced.ForceReloadOnLateJoin;

    // Debug
    internal static ConfigEntry<LogLevel> ModLogLevel => Debug.LogLevelEntry;

    #endregion
}