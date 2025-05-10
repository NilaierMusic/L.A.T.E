// File: L.A.T.E/Managers/PlayerStateManager.cs
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Photon.Realtime;
using LATE.Core; // For LatePlugin.Log
using LATE.DataModels; // For PlayerStatus enum

namespace LATE.Managers; // File-scoped namespace

/// <summary>
/// Keeps track of each player's life-state (Alive/Dead) for the current level.
/// This state is primarily used by the host to determine if a rejoining player
/// should be killed upon late-joining, based on configuration.
/// </summary>
internal static class PlayerStateManager
{
    private const int InitialCapacity = 8;

    // Key: Photon UserId → Value: status
    private static readonly Dictionary<string, PlayerStatus> _playerStatuses =
        new Dictionary<string, PlayerStatus>(InitialCapacity);

    #region Internal Helpers

    /// <summary>
    /// Fast null/empty guard that also returns the UserId.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryGetUserId(Player player, out string userId)
    {
        if (player != null && !string.IsNullOrEmpty(player.UserId))
        {
            userId = player.UserId;
            return true;
        }

        userId = string.Empty;
        return false;
    }

    /// <summary>
    /// Shared internal setter with optional logging.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetStatus(Player player, PlayerStatus status, string logHint)
    {
        if (!TryGetUserId(player, out var userId))
        {
            return;
        }

        _playerStatuses[userId] = status;

        LatePlugin.Log.LogInfo($"[PlayerState] {logHint} '{player.NickName}' (ID:{userId}) → {status}");
    }

    #endregion

    #region Public API

    /// <summary>
    /// Marks the specified player as dead for the current level.
    /// </summary>
    /// <param name="player">The player to mark as dead.</param>
    public static void MarkPlayerDead(Player player)
    {
        SetStatus(player, PlayerStatus.Dead, "Marked");
    }

    /// <summary>
    /// Marks the specified player as alive for the current level.
    /// If the player was previously dead, this indicates a revival.
    /// </summary>
    /// <param name="player">The player to mark as alive.</param>
    public static void MarkPlayerAlive(Player player)
    {
        if (!TryGetUserId(player, out var userId))
        {
            return;
        }

        _playerStatuses.TryGetValue(userId, out var currentStatus);
        if (currentStatus == PlayerStatus.Alive)
        {
            // Player already alive; nothing to do.
            return;
        }

        string statusSuffix = currentStatus == PlayerStatus.Dead ? "Revived" : "Marked initial";
        SetStatus(player, PlayerStatus.Alive, statusSuffix);
    }

    /// <summary>
    /// Gets the current life-status of the specified player for the current level.
    /// </summary>
    /// <param name="player">The player whose status to retrieve.</param>
    /// <returns>The <see cref="PlayerStatus"/> of the player.</returns>
    public static PlayerStatus GetPlayerStatus(Player player)
    {
        if (TryGetUserId(player, out var userId) &&
            _playerStatuses.TryGetValue(userId, out var status))
        {
            return status;
        }

        return PlayerStatus.Unknown;
    }

    /// <summary>
    /// Clears all tracked player statuses. Typically called when a new level starts.
    /// </summary>
    public static void ResetPlayerStatuses()
    {
        LatePlugin.Log.LogInfo("[PlayerState] Resetting all tracked player statuses for new level.");
        _playerStatuses.Clear();
    }

    #endregion
}