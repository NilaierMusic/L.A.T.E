// File: L.A.T.E/Managers/PlayerPositionManager.cs
using LATE.Core;        // LatePlugin.Log
using LATE.DataModels;  // PlayerTransformData
using Photon.Realtime;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LATE.Managers;

/// <summary>
/// Local Photon player helper.  
/// If it becomes broadly useful we can move it to `LATE.Utilities`.
/// </summary>
internal static class PhotonPlayerExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetValidUserId(this Player player, out string userId, bool logWarningIfInvalid = true)
    {
        userId = player?.UserId ?? string.Empty;
        bool isValid = !string.IsNullOrEmpty(userId);
        if (!isValid && logWarningIfInvalid && player != null) // Avoid logging if player itself is null
        {
            LatePlugin.Log.LogWarning($"[PhotonPlayerExtensions] TryGetValidUserId: Player '{player.NickName}' (ActorNr: {player.ActorNumber}, IsInactive: {player.IsInactive}) has an invalid or empty UserId ('{userId}'). This will break L.A.T.E. state tracking.");
        }
        else if (!isValid && logWarningIfInvalid && player == null)
        {
            LatePlugin.Log.LogWarning($"[PhotonPlayerExtensions] TryGetValidUserId: Player object itself is null.");
        }
        return isValid;
    }
}

/// <summary>
/// Keeps track of every player’s last meaningful position (alive or death-head) within
/// the current level.  Used by the “Spawn At Last Position” feature for re-joiners.
/// </summary>
internal static class PlayerPositionManager
{
    private const string LogPrefix = "[PositionManager]";

    private static readonly Dictionary<string, PlayerTransformData> _lastTransforms = new();

    #region Public API -------------------------------------------------------------------------

    /// <summary>
    /// Records the latest ALIVE position for <paramref name="player"/>.
    /// Ignored if a death-head position is already stored – that takes precedence.
    /// </summary>
    public static void UpdatePlayerPosition(Player player, in Vector3 position, in Quaternion rotation)
    {
        if (!player.TryGetValidUserId(out var userId)) return;

        if (_lastTransforms.TryGetValue(userId, out var existing) && existing.IsDeathHeadPosition)
        {
            LatePlugin.Log.LogDebug(
                $"{LogPrefix} Skipping normal position update for {player.NickName}; death position already tracked.");
            return;
        }

        _lastTransforms[userId] = new(position, rotation, isDeathHead: false);

        LatePlugin.Log.LogInfo(
            $"{LogPrefix} Updated ALIVE position for '{player.NickName}' (ID: {userId}) to {position}");
    }

    /// <summary>
    /// Records the DEATH-HEAD position for <paramref name="player"/>. Always overwrites.
    /// </summary>
    public static void UpdatePlayerDeathPosition(Player player, in Vector3 position, in Quaternion rotation)
    {
        if (!player.TryGetValidUserId(out var userId)) return;

        _lastTransforms[userId] = new(position, rotation, isDeathHead: true);

        LatePlugin.Log.LogInfo(
            $"{LogPrefix} Updated DEATH position for '{player.NickName}' (ID: {userId}) to {position}");
    }

    /// <summary>Attempts to fetch the last stored transform for <paramref name="player"/>.</summary>
    public static bool TryGetLastTransform(Player player, out PlayerTransformData transformData)
    {
        transformData = default;
        return player.TryGetValidUserId(out var userId) &&
               _lastTransforms.TryGetValue(userId, out transformData);
    }

    /// <summary>Removes any stored transform for <paramref name="player"/>.</summary>
    public static void ClearPlayerPositionRecord(Player player)
    {
        if (!player.TryGetValidUserId(out var userId)) return;

        if (_lastTransforms.Remove(userId))
            LatePlugin.Log.LogInfo(
                $"{LogPrefix} Cleared position record for '{player.NickName}' (ID: {userId}).");
    }

    /// <summary>Clears every stored transform (called on level load).</summary>
    public static void ResetPositions()
    {
        _lastTransforms.Clear();
        LatePlugin.Log.LogInfo($"{LogPrefix} Reset all tracked player positions for new level.");
    }

    #endregion
}