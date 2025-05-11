// File: L.A.T.E/DataModels/PlayerTransformData.cs
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LATE.DataModels; // File-scoped namespace

/// <summary>
/// An immutable data structure holding a player's last known position and rotation,
/// and whether this position represents their death location.
/// </summary>
internal readonly struct PlayerTransformData : IEquatable<PlayerTransformData>
{
    /// <summary>
    /// Gets the last known position of the player.
    /// </summary>
    public Vector3 Position { get; }

    /// <summary>
    /// Gets the last known rotation of the player.
    /// </summary>
    public Quaternion Rotation { get; }

    /// <summary>
    /// Gets a value indicating whether this transform data represents the player's death head position.
    /// </summary>
    public bool IsDeathHeadPosition { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerTransformData"/> struct.
    /// </summary>
    /// <param name="position">The player's position.</param>
    /// <param name="rotation">The player's rotation.</param>
    /// <param name="isDeathHead">True if this is the position of a death head; otherwise, false.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PlayerTransformData(in Vector3 position, in Quaternion rotation, bool isDeathHead = false)
    {
        Position = position;
        Rotation = rotation;
        IsDeathHeadPosition = isDeathHead;
    }

    public bool Equals(PlayerTransformData other) =>
        Position.Equals(other.Position) &&
        Rotation.Equals(other.Rotation) &&
        IsDeathHeadPosition == other.IsDeathHeadPosition;

    public override bool Equals(object? obj) =>
        obj is PlayerTransformData other && Equals(other);

    public override int GetHashCode()
    {
        unchecked // overflow is fine
        {
            int hash = 17;
            hash = hash * 23 + Position.GetHashCode();
            hash = hash * 23 + Rotation.GetHashCode();
            hash = hash * 23 + IsDeathHeadPosition.GetHashCode();
            return hash;
        }
    }
}