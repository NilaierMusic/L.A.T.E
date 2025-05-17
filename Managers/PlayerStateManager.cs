// File: L.A.T.E/Managers/PlayerStateManager.cs
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Photon.Realtime;
using LATE.Core;
using LATE.DataModels;
using System.Text; // For StringBuilder

namespace LATE.Managers;

internal static class PlayerStateManager
{
    private const string LogPrefix = "[PlayerState]";
    private const int InitialCapacity = 8;
    private static readonly Dictionary<string, PlayerSessionData> _playerSessionStates = new(InitialCapacity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetSessionState(Player player, PlayerSessionData sessionData, string logHint)
    {
        if (!player.TryGetValidUserId(out var userId)) // Defaults to logWarningIfInvalid = true
        {
            LatePlugin.Log.LogError($"{LogPrefix} SetSessionState: Critical - Could not get valid UserId for player '{player?.NickName}'. State NOT saved.");
            return;
        }

        _playerSessionStates[userId] = sessionData;
        LatePlugin.Log.LogInfo($"{LogPrefix} {logHint} '{player.NickName}' (ID:{userId}) → {sessionData.Status}{(sessionData.Status == PlayerLifeStatus.Dead ? $" (EnemyIdx: {sessionData.DeathEnemyIndex})" : "")}");
    }

    public static void MarkPlayerDead(Player player, int enemyIndex) =>
        SetSessionState(player, new PlayerSessionData(PlayerLifeStatus.Dead, enemyIndex), "Marked");

    public static void MarkPlayerAlive(Player player)
    {
        if (!player.TryGetValidUserId(out var userId)) return;
        _playerSessionStates.TryGetValue(userId, out var currentData);
        if (currentData.Status == PlayerLifeStatus.Alive) return;
        string suffix = currentData.Status == PlayerLifeStatus.Dead ? "Revived" : "Marked initial";
        SetSessionState(player, new PlayerSessionData(PlayerLifeStatus.Alive), suffix);
    }

    public static PlayerLifeStatus GetPlayerLifeStatus(Player player)
    {
        if (!player.TryGetValidUserId(out var userId))
        {
            LatePlugin.Log.LogWarning($"{LogPrefix} GetPlayerLifeStatus: Could not get valid UserId for player '{player?.NickName ?? "NULL_PLAYER"}'. Returning Unknown.");
            return PlayerLifeStatus.Unknown;
        }

        // Diagnostic Logging:
        var sb = new StringBuilder();
        sb.AppendLine($"{LogPrefix} GetPlayerLifeStatus: Querying for UserId '{userId}' (Player: '{player.NickName}', ActorNr: {player.ActorNumber}). Current _playerSessionStates ({_playerSessionStates.Count} entries):");
        foreach (var kvp in _playerSessionStates)
        {
            sb.AppendLine($"  - Key (UserId): '{kvp.Key}', Status: {kvp.Value.Status}, EnemyIdx: {kvp.Value.DeathEnemyIndex}");
        }
        LatePlugin.Log.LogDebug(sb.ToString());

        if (_playerSessionStates.TryGetValue(userId, out var data))
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} GetPlayerLifeStatus: Found status for UserId '{userId}': {data.Status}");
            return data.Status;
        }
        else
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} GetPlayerLifeStatus: No status found for UserId '{userId}'. Returning Unknown.");
            return PlayerLifeStatus.Unknown;
        }
    }

    public static bool TryGetPlayerDeathEnemyIndex(Player player, out int enemyIndex)
    {
        enemyIndex = -1;
        if (player.TryGetValidUserId(out var userId) &&
            _playerSessionStates.TryGetValue(userId, out var data) &&
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
        LatePlugin.Log.LogInfo($"{LogPrefix} Resetting all tracked player session states for new level.");
    }
}