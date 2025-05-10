// File: L.A.T.E/DataModels/PlayerStatus.cs
namespace LATE.DataModels; // File-scoped namespace

/// <summary>
/// Represents the current life-state of a player within a level.
/// </summary>
internal enum PlayerStatus
{
    /// <summary>
    /// The player's status is not yet known or tracked.
    /// </summary>
    Unknown,

    /// <summary>
    /// The player is currently alive.
    /// </summary>
    Alive,

    /// <summary>
    /// The player is currently dead.
    /// </summary>
    Dead,
}