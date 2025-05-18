// File: L.A.T.E/Managers/PlayerPositionManager.cs
using LATE.Core;
using LATE.DataModels;
using Photon.Realtime;
using System.Collections.Generic;
using UnityEngine;
using LATE.Utilities; // For GameUtilities and ReflectionCache
using System;          // For Exception

// Remove or comment out the old PhotonPlayerExtensions class if it's no longer broadly used
/*
internal static class PhotonPlayerExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetValidUserId(this Player player, out string userId, bool logWarningIfInvalid = true)
    {
        // ... old implementation ...
    }
}
*/

internal static class PlayerPositionManager
{
    private const string LogPrefix = "[PositionManager]";
    // The dictionary key remains string, but will now store SteamID
    private static readonly Dictionary<string, PlayerTransformData> _lastTransforms = new();

    // Helper to get SteamID string from Player
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
            // LatePlugin.Log.LogWarning($"{LogPrefix} TryGetPlayerSteamIdString: Could not find PlayerAvatar for player '{player.NickName}' (ActorNr: {player.ActorNumber}).");
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
            // Log if steamID is null or empty after successful reflection, as this is unexpected if the field is populated.
            LatePlugin.Log.LogWarning($"{LogPrefix} TryGetPlayerSteamIdString: PlayerAvatar.steamID for '{player.NickName}' (ActorNr: {player.ActorNumber}) is null, empty, or not a string after reflection. Value: '{idObj}', Type: '{idObj?.GetType().FullName ?? "null"}'. Waiting for game to populate it?");
            return false;
        }
        catch (Exception ex)
        {
            LatePlugin.Log.LogError($"{LogPrefix} TryGetPlayerSteamIdString: Exception reflecting PlayerAvatar.steamID for '{player.NickName}': {ex}");
            return false;
        }
    }

    public static void UpdatePlayerPosition(Player player, in Vector3 position, in Quaternion rotation)
    {
        // If the player is the MasterClient (host), don't track their position with this system.
        // Their position is inherently current.
        if (player != null && player.IsMasterClient)
        {
            // LatePlugin.Log.LogDebug($"{LogPrefix} Skipping position update for Host ({player.NickName}).");
            return;
        }

        if (!TryGetPlayerSteamIdString(player, out var steamId)) return;

        if (_lastTransforms.TryGetValue(steamId, out var existing) && existing.IsDeathHeadPosition)
        {
            LatePlugin.Log.LogDebug(
                $"{LogPrefix} Skipping normal position update for {player.NickName} (SteamID: {steamId}); death position already tracked.");
            return;
        }
        _lastTransforms[steamId] = new(position, rotation, isDeathHead: false);
        LatePlugin.Log.LogDebug( // Kept as Debug from previous fix
            $"{LogPrefix} Updated ALIVE position for '{player.NickName}' (SteamID: {steamId}) to {position}");
    }

    public static void UpdatePlayerDeathPosition(Player player, in Vector3 position, in Quaternion rotation)
    {
        // If the player is the MasterClient (host), don't track their death position with this system.
        if (player != null && player.IsMasterClient)
        {
            // LatePlugin.Log.LogDebug($"{LogPrefix} Skipping death position update for Host ({player.NickName}).");
            return;
        }
        if (!TryGetPlayerSteamIdString(player, out var steamId)) return;

        _lastTransforms[steamId] = new(position, rotation, isDeathHead: true);
        LatePlugin.Log.LogDebug( // Kept as Debug from previous fix
            $"{LogPrefix} Updated DEATH position for '{player.NickName}' (SteamID: {steamId}) to {position}");
    }

    public static bool TryGetLastTransform(Player player, out PlayerTransformData transformData)
    {
        transformData = default;
        return TryGetPlayerSteamIdString(player, out var steamId) &&
               _lastTransforms.TryGetValue(steamId, out transformData);
    }

    public static void ClearPlayerPositionRecord(Player player)
    {
        if (!TryGetPlayerSteamIdString(player, out var steamId)) return;

        if (_lastTransforms.Remove(steamId))
            LatePlugin.Log.LogInfo(
                $"{LogPrefix} Cleared position record for '{player.NickName}' (SteamID: {steamId}).");
    }

    public static void ResetPositions()
    {
        _lastTransforms.Clear();
        LatePlugin.Log.LogInfo($"{LogPrefix} Reset all tracked player positions (SteamID based) for new level.");
    }
}