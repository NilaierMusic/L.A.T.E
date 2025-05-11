// File: L.A.T.E/Core/CoroutineHelper.cs
using System.Collections;

using UnityEngine;

using LATE.Utilities; // GameUtilities

namespace LATE.Core;

/// <summary>
/// Central place to grab a <see cref="MonoBehaviour"/> suitable for starting
/// coroutines when you’re inside a static class or outside any scene object.
/// </summary>
internal static class CoroutineHelper
{
    private const string LogPrefix = "[CoroutineHelper]";
    private static MonoBehaviour? _runner;

    /// <summary>Returns (and caches) a safe runner. May be <c>null</c> if none found.</summary>
    internal static MonoBehaviour? CoroutineRunner =>
        _runner ??= GameUtilities.FindCoroutineRunner();

    /// <summary>Clear the cached runner—call on scene-change if needed.</summary>
    internal static void ClearCoroutineRunnerCache()
    {
        LatePlugin.Log.LogDebug($"{LogPrefix} Clearing cached runner.");
        _runner = null;
    }

    /// <summary>Utility sugar: try start a coroutine immediately if a runner exists.</summary>
    internal static Coroutine? Start(IEnumerator routine) =>
        CoroutineRunner != null ? CoroutineRunner.StartCoroutine(routine) : null;
}