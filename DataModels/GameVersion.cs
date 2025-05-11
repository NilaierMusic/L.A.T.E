// File: L.A.T.E/DataModels/GameVersion.cs
namespace LATE.DataModels;

internal enum GameVersion
{
    Unknown,  // could not be determined
    Stable,   // public Steam build (e.g. UnlockLobby())
    Beta      // beta / newer build (e.g. UnlockLobby(bool))
}