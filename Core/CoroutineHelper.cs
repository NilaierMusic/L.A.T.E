// File: L.A.T.E/Core/CoroutineHelper.cs
using UnityEngine;
using LATE.Utilities; // Now correctly refers to the LATE.Utilities namespace

namespace LATE.Core; // File-scoped namespace

/// <summary>
/// Helper class for managing a global MonoBehaviour instance to run coroutines.
/// </summary>
internal static class CoroutineHelper
{
    private static MonoBehaviour? _coroutineRunner;

    /// <summary>
    /// Gets a MonoBehaviour instance that can be used to start coroutines.
    /// It attempts to find a suitable game object (like RunManager or GameDirector)
    /// and caches it.
    /// </summary>
    internal static MonoBehaviour? CoroutineRunner
    {
        get
        {
            if (_coroutineRunner == null)
            {
                // Now calls the method on GameUtilities within the LATE.Utilities namespace
                _coroutineRunner = GameUtilities.FindCoroutineRunner();
            }
            return _coroutineRunner;
        }
    }

    /// <summary>
    /// Clears the cached CoroutineRunner reference. This can be useful if the
    /// game scene changes and the previous runner becomes invalid.
    /// </summary>
    internal static void ClearCoroutineRunnerCache()
    {
        LatePlugin.Log.LogDebug("Clearing cached CoroutineRunner in CoroutineHelper.");
        _coroutineRunner = null;
    }
}