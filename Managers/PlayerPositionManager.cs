// File: L.A.T.E/Managers/PlayerPositionManager.cs
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Photon.Realtime;
using UnityEngine;
using LATE.Core; // For LatePlugin.Log
using LATE.DataModels; // For PlayerTransformData

namespace LATE.Managers; // File-scoped namespace

// PhotonPlayerExtensions class remains the same as it's a local utility here.
// If it were more general, it might move to LATE.Utilities.
internal static class PhotonPlayerExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetValidUserId(this Player player, out string userId)
    {
        userId = player?.UserId ?? string.Empty;
        return !string.IsNullOrEmpty(userId);
    }
}

/// <summary>
/// Keeps track of every player's last meaningful position (either their alive position
/// or their death head's position) within the current level instance.
/// This is used by the "Spawn At Last Position" feature for rejoining players.
/// </summary>
internal static class PlayerPositionManager
{
    #region Private Fields

    private static readonly Dictionary<string, PlayerTransformData> _lastTransforms =
        new Dictionary<string, PlayerTransformData>();

    private const string LogPrefix = "[PositionManager] ";

    #endregion

    #region Public API

    /// <summary>
    /// Records the latest ALIVE position for <paramref name="player"/>.
    /// This is ignored if a death-head position is already stored for the player,
    /// as the death position takes precedence for respawning.
    /// </summary>
    /// <param name="player">The player whose position is being updated.</param>
    /// <param name="position">The player's current world position.</param>
    /// <param name="rotation">The player's current world rotation.</param>
    public static void UpdatePlayerPosition(Player player, in Vector3 position, in Quaternion rotation)
    {
        if (!player.TryGetValidUserId(out var userId))
        {
            return;
        }

        // If a death-head transform is already stored, skip the update.
        if (_lastTransforms.TryGetValue(userId, out var existingTransform) && existingTransform.IsDeathHeadPosition)
        {
            LatePlugin.Log.LogDebug(
                $"{LogPrefix}Skipping normal position update for {player.NickName}; death position already tracked."
            );
            return;
        }

        _lastTransforms[userId] = new PlayerTransformData(position, rotation, isDeathHead: false);
        LatePlugin.Log.LogInfo(
            $"{LogPrefix}Updated ALIVE position for '{player.NickName}' (ID: {userId}) to {position}"
        );
    }

    /// <summary>
    /// Records the DEATH-HEAD position for <paramref name="player"/>.
    /// This always overwrites any previous entry for the player, as the death
    /// position is considered more definitive for respawn purposes.
    /// </summary>
    /// <param name="player">The player whose death position is being recorded.</param>
    /// <param name="position">The world position of the player's death head.</param>
    /// <param name="rotation">The world rotation of the player's death head.</param>
    public static void UpdatePlayerDeathPosition(Player player, in Vector3 position, in Quaternion rotation)
    {
        if (!player.TryGetValidUserId(out var userId))
        {
            return;
        }

        _lastTransforms[userId] = new PlayerTransformData(position, rotation, isDeathHead: true);
        LatePlugin.Log.LogInfo(
            $"{LogPrefix}Updated DEATH position for '{player.NickName}' (ID: {userId}) to {position}"
        );
    }

    /// <summary>
    /// Attempts to fetch the last stored transform (either alive or death head) for <paramref name="player"/>.
    /// </summary>
    /// <param name="player">The player whose transform to retrieve.</param>
    /// <param name="transformData">
    /// When this method returns, contains the <see cref="PlayerTransformData"/>
    /// for the specified player, if found; otherwise, the default value.
    /// </param>
    /// <returns>True if a transform was found for the player; otherwise, false.</returns>
    public static bool TryGetLastTransform(Player player, out PlayerTransformData transformData)
    {
        transformData = default;
        if (player.TryGetValidUserId(out var userId))
        {
            return _lastTransforms.TryGetValue(userId, out transformData);
        }
        return false;
    }

    /// <summary>
    /// Removes any stored transform for <paramref name="player"/> (e.g., upon revival or if state needs reset).
    /// </summary>
    /// <param name="player">The player whose position record to clear.</param>
    public static void ClearPlayerPositionRecord(Player player)
    {
        if (!player.TryGetValidUserId(out var userId))
        {
            return;
        }

        if (_lastTransforms.Remove(userId))
        {
            LatePlugin.Log.LogInfo(
                $"{LogPrefix}Cleared position record for '{player.NickName}' (ID: {userId})."
            );
        }
    }

    /// <summary>
    /// Clears every stored transform. This is typically called when a new level is loaded
    /// to ensure no stale data persists across scenes.
    /// </summary>
    public static void ResetPositions()
    {
        _lastTransforms.Clear();
        LatePlugin.Log.LogInfo($"{LogPrefix}Reset all tracked player positions for new level.");
    }
    #endregion
}