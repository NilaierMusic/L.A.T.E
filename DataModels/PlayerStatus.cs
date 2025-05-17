// File: L.A.T.E/DataModels/PlayerSessionData.cs
namespace LATE.DataModels
{
    internal enum PlayerLifeStatus // Renamed from PlayerStatus to avoid ambiguity if PlayerStatus is used elsewhere
    {
        Unknown,
        Alive,
        Dead
    }

    internal readonly struct PlayerSessionData
    {
        public PlayerLifeStatus Status { get; }
        public int DeathEnemyIndex { get; } // Stores enemyIndex if Status is Dead, otherwise -1 or ignored.

        public PlayerSessionData(PlayerLifeStatus status, int deathEnemyIndex = -1)
        {
            Status = status;
            DeathEnemyIndex = (status == PlayerLifeStatus.Dead) ? deathEnemyIndex : -1;
        }

        // Optional: Default instance for unknown state
        public static PlayerSessionData Unknown => new PlayerSessionData(PlayerLifeStatus.Unknown);
    }
}