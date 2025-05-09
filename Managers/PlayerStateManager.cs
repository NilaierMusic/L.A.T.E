using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Photon.Realtime;

namespace LATE
{
    /// <summary>
    /// Represents the current status of a player.
    /// </summary>
    internal enum PlayerStatus
    {
        Unknown,
        Alive,
        Dead,
    }

    /// <summary>
    /// Keeps track of each player's life-state for the current level.
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

#if DEBUG
            LateJoinEntry.Log.LogInfo($"[PlayerState] {logHint} '{player.NickName}' (ID:{userId}) → {status}");
#endif
        }

        #endregion

        #region Public API

        public static void MarkPlayerDead(Player player)
        {
            SetStatus(player, PlayerStatus.Dead, "Marked");
        }

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

        public static PlayerStatus GetPlayerStatus(Player player)
        {
            if (TryGetUserId(player, out var userId) &&
                _playerStatuses.TryGetValue(userId, out var status))
            {
                return status;
            }

            return PlayerStatus.Unknown;
        }

        public static void ResetPlayerStatuses()
        {
#if DEBUG
            LateJoinEntry.Log.LogInfo("[PlayerState] Resetting all tracked player statuses for new level.");
#endif
            _playerStatuses.Clear();
        }

        #endregion
    }
}