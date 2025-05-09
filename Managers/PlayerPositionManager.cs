using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Photon.Realtime;
using UnityEngine;

namespace LATE
{
    /// <summary>
    /// Immutable package with a player's last known transform.
    /// </summary>
    internal readonly struct PlayerTransformData
    {
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public bool IsDeathHeadPosition { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PlayerTransformData(in Vector3 position, in Quaternion rotation, bool isDeathHead = false)
        {
            Position = position;
            Rotation = rotation;
            IsDeathHeadPosition = isDeathHead;
        }
    }

    /// <summary>
    /// Small guard helper so we don't repeat the same null/empty checks.
    /// </summary>
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
    /// Keeps track of every player's last meaningful position (alive or dead).
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
        /// Ignored if a death-head position is already stored.
        /// </summary>
        public static void UpdatePlayerPosition(Player player, in Vector3 position, in Quaternion rotation)
        {
            if (!player.TryGetValidUserId(out var userId))
            {
                return;
            }

            // If a death-head transform is already stored, skip the update.
            if (_lastTransforms.TryGetValue(userId, out var existingTransform) && existingTransform.IsDeathHeadPosition)
            {
                LATE.Core.LatePlugin.Log.LogDebug(
                    $"{LogPrefix}Skipping normal update for {player.NickName}; death position already tracked."
                );

                return;
            }

            _lastTransforms[userId] = new PlayerTransformData(position, rotation, isDeathHead: false);
            LATE.Core.LatePlugin.Log.LogInfo(
                $"{LogPrefix}Updated ALIVE position for '{player.NickName}' (ID: {userId}) to {position}"
            );
        }

        /// <summary>
        /// Records the DEATH-HEAD position for <paramref name="player"/>.
        /// Always overwrites any previous entry.
        /// </summary>
        public static void UpdatePlayerDeathPosition(Player player, in Vector3 position, in Quaternion rotation)
        {
            if (!player.TryGetValidUserId(out var userId))
            {
                return;
            }

            _lastTransforms[userId] = new PlayerTransformData(position, rotation, isDeathHead: true);
            LATE.Core.LatePlugin.Log.LogInfo(
                $"{LogPrefix}Updated DEATH position for '{player.NickName}' (ID: {userId}) to {position}"
            );
        }

        /// <summary>
        /// Attempts to fetch the last stored transform for <paramref name="player"/>.
        /// </summary>
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
        /// Removes any stored transform for <paramref name="player"/> (e.g. on revival).
        /// </summary>
        public static void ClearPlayerPositionRecord(Player player)
        {
            if (!player.TryGetValidUserId(out var userId))
            {
                return;
            }

            if (_lastTransforms.Remove(userId))
            {
                LATE.Core.LatePlugin.Log.LogInfo(
                    $"{LogPrefix}Cleared position record for '{player.NickName}' (ID: {userId})."
                );
            }
        }

        /// <summary>
        /// Clears every stored transform (called when a new level is loaded).
        /// </summary>
        public static void ResetPositions()
        {
            _lastTransforms.Clear();
            LATE.Core.LatePlugin.Log.LogInfo($"{LogPrefix}Reset all tracked player positions for new level.");
        }

        #endregion
    }
}