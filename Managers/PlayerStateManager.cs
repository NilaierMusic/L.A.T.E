// File: L.A.T.E/Managers/PlayerStateManager.cs
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Photon.Realtime;

using LATE.Core;        // LatePlugin.Log
using LATE.DataModels;  // PlayerStatus

namespace LATE.Managers;

/// <summary>
/// Tracks each player’s life-state (Alive / Dead) for the current level so that the host
/// knows whether a late-joiner should spawn alive or be killed, depending on config.
/// </summary>
internal static class PlayerStateManager
{
    private const string LogPrefix = "[PlayerState]";
    private const int InitialCapacity = 8;

    // Key: Photon UserId  →  Value: PlayerStatus
    private static readonly Dictionary<string, PlayerStatus> _playerStatuses = new(InitialCapacity);

    #region Internal helper --------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetStatus(Player player, PlayerStatus status, string logHint)
    {
        if (!player.TryGetValidUserId(out var userId)) return;

        _playerStatuses[userId] = status;
        LatePlugin.Log.LogInfo($"{LogPrefix} {logHint} '{player.NickName}' (ID:{userId}) → {status}");
    }

    #endregion


    #region Public API -------------------------------------------------------------------------

    /// <summary>Marks <paramref name="player"/> as dead for the current level.</summary>
    public static void MarkPlayerDead(Player player) =>
        SetStatus(player, PlayerStatus.Dead, "Marked");

    /// <summary>
    /// Marks <paramref name="player"/> as alive.  
    /// If the player was previously dead this counts as a revival.
    /// </summary>
    public static void MarkPlayerAlive(Player player)
    {
        if (!player.TryGetValidUserId(out var userId)) return;

        _playerStatuses.TryGetValue(userId, out var current);

        if (current == PlayerStatus.Alive) return;   // already alive

        string suffix = current == PlayerStatus.Dead ? "Revived" : "Marked initial";
        SetStatus(player, PlayerStatus.Alive, suffix);
    }

    /// <summary>Returns the current life-status of <paramref name="player"/>.</summary>
    public static PlayerStatus GetPlayerStatus(Player player) =>
        player.TryGetValidUserId(out var userId) &&
        _playerStatuses.TryGetValue(userId, out var status)
            ? status
            : PlayerStatus.Unknown;

    /// <summary>Clears all tracked statuses (call on new level load).</summary>
    public static void ResetPlayerStatuses()
    {
        _playerStatuses.Clear();
        LatePlugin.Log.LogInfo($"{LogPrefix} Resetting all tracked player statuses for new level.");
    }

    #endregion
}