// File: L.A.T.E/Managers/PlayerStateManager.cs
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Photon.Realtime;
using LATE.Core;
using LATE.DataModels;
using LATE.Utilities; // For GameUtilities and ReflectionCache
using System;          // For Exception
using System.Text;

internal static class PlayerStateManager
{
    private const string LogPrefix = "[PlayerState]";
    private const int InitialCapacity = 8;
    // The dictionary key remains string, but will now store SteamID
    private static readonly Dictionary<string, PlayerSessionData> _playerSessionStates = new(InitialCapacity);

    // Helper to get SteamID string from Player (can be a shared utility, duplicated for now)
    private static bool TryGetPlayerSteamIdString(Player player, out string steamId)
    {
        steamId = string.Empty;
        if (player == null)
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} TryGetPlayerSteamIdString: Player object is null.");
            return false;
        }

        PlayerAvatar? avatar = GameUtilities.FindPlayerAvatar(player);
        if (avatar == null)
        {
            // GameUtilities.FindPlayerAvatar already logs a warning
            return false;
        }

        if (ReflectionCache.PlayerAvatar_SteamIDField == null)
        {
            LatePlugin.Log.LogError($"{LogPrefix} TryGetPlayerSteamIdString: ReflectionCache.PlayerAvatar_SteamIDField is null. Cannot get SteamID. Check ReflectionCache setup.");
            return false;
        }

        try
        {
            object? idObj = ReflectionCache.PlayerAvatar_SteamIDField.GetValue(avatar);
            if (idObj is string sID && !string.IsNullOrEmpty(sID))
            {
                steamId = sID;
                return true;
            }
            LatePlugin.Log.LogWarning($"{LogPrefix} TryGetPlayerSteamIdString: PlayerAvatar.steamID for '{player.NickName}' (ActorNr: {player.ActorNumber}) is null, empty, or not a string after reflection. Value: '{idObj}', Type: '{idObj?.GetType().FullName ?? "null"}'. Waiting for game to populate it?");
            return false;
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} TryGetPlayerSteamIdString: Exception reflecting PlayerAvatar.steamID for '{player.NickName}': {ex}");
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetSessionState(Player player, PlayerSessionData sessionData, string logHint)
    {
        // Add this check here as it's the core state-setting method
        if (player != null && player.IsMasterClient)
        {
            // LatePlugin.Log.LogDebug($"{LogPrefix} SetSessionState: Skipping state set for Host ({player.NickName}). Hint: {logHint}");
            return;
        }
        if (!TryGetPlayerSteamIdString(player, out var steamId))
        {
            LatePlugin.Log.LogError($"{LogPrefix} SetSessionState: Critical - Could not get valid SteamID for player '{player?.NickName}'. State NOT saved.");
            return;
        }

        _playerSessionStates[steamId] = sessionData;
        LatePlugin.Log.LogInfo($"{LogPrefix} {logHint} '{player.NickName}' (SteamID:{steamId}) → {sessionData.Status}{(sessionData.Status == PlayerLifeStatus.Dead ? $" (EnemyIdx: {sessionData.DeathEnemyIndex})" : "")}");
    }

    public static void MarkPlayerDead(Player player, int enemyIndex) =>
        SetSessionState(player, new PlayerSessionData(PlayerLifeStatus.Dead, enemyIndex), "Marked");

    public static void MarkPlayerAlive(Player player)
    {
        if (!TryGetPlayerSteamIdString(player, out var steamId)) return;
        _playerSessionStates.TryGetValue(steamId, out var currentData);
        if (currentData.Status == PlayerLifeStatus.Alive) return; // Already alive
        string suffix = currentData.Status == PlayerLifeStatus.Dead ? "Revived" : "Marked initial";
        SetSessionState(player, new PlayerSessionData(PlayerLifeStatus.Alive), suffix);
    }

    public static PlayerLifeStatus GetPlayerLifeStatus(Player player)
    {
        if (!TryGetPlayerSteamIdString(player, out var steamId))
        {
            // TryGetPlayerSteamIdString already logs sufficiently detailed info if player or avatar is null, or reflection fails.
            // The original message here is a bit redundant if the helper also logs.
            LatePlugin.Log.LogWarning($"{LogPrefix} GetPlayerLifeStatus: Could not get valid SteamID for player '{player?.NickName ?? "NULL_PLAYER"}'. Returning Unknown.");
            return PlayerLifeStatus.Unknown;
        }

        // Removed verbose logging of all states from previous step.
        // Add more targeted debug logging if necessary:
        // LatePlugin.Log.LogDebug($"{LogPrefix} GetPlayerLifeStatus: Querying for SteamID '{steamId}' (Player: '{player.NickName}', ActorNr: {player.ActorNumber}).");

        if (_playerSessionStates.TryGetValue(steamId, out var data))
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} GetPlayerLifeStatus: Found status for SteamID '{steamId}' ('{player.NickName}'): {data.Status}");
            return data.Status;
        }
        else
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} GetPlayerLifeStatus: No status found for SteamID '{steamId}' ('{player.NickName}'). Returning Unknown.");
            return PlayerLifeStatus.Unknown;
        }
    }

    public static bool TryGetPlayerDeathEnemyIndex(Player player, out int enemyIndex)
    {
        enemyIndex = -1;
        if (TryGetPlayerSteamIdString(player, out var steamId) &&
            _playerSessionStates.TryGetValue(steamId, out var data) &&
            data.Status == PlayerLifeStatus.Dead)
        {
            enemyIndex = data.DeathEnemyIndex;
            return true;
        }
        return false;
    }

    public static void ResetPlayerSessionStates()
    {
        _playerSessionStates.Clear();
        LatePlugin.Log.LogInfo($"{LogPrefix} Resetting all tracked player session states (SteamID based) for new level.");
    }
}