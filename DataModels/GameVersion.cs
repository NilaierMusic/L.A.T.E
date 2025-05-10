// File: L.A.T.E/DataModels/GameVersion.cs
namespace LATE.DataModels; // File-scoped namespace

/// <summary>
/// Represents different detected versions of the game, primarily for API compatibility.
/// </summary>
internal enum GameVersion
{
    /// <summary>
    /// The game version could not be determined.
    /// </summary>
    Unknown,

    /// <summary>
    /// Assumed to be a stable version of the game (e.g., parameterless SteamManager.UnlockLobby()).
    /// </summary>
    Stable,

    /// <summary>
    /// Assumed to be a beta or newer version of the game (e.g., SteamManager.UnlockLobby(bool)).
    /// </summary>
    Beta
}