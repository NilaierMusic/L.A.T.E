// File: L.A.T.E/Core/CoroutineHelper.cs
using System.Collections;
using UnityEngine;
using LATE.Utilities; // GameUtilities

namespace LATE.Core
{
    internal static class CoroutineHelper
    {
        private const string LogPrefix = "[CoroutineHelper]";
        private static MonoBehaviour? _runner;
        private static LatePlugin? _latePluginInstance; // Cache for LatePlugin instance

        // Method to allow LatePlugin to register itself
        internal static void SetLatePluginInstance(LatePlugin instance)
        {
            _latePluginInstance = instance;
            // If we get a new LatePlugin instance, it's safer to clear the generic runner cache
            // in case the old one was tied to a scene that's now gone.
            ClearCoroutineRunnerCache();
            LatePlugin.Log.LogDebug($"{LogPrefix} LatePlugin instance registered. Runner cache will be re-evaluated.");
        }

        internal static MonoBehaviour? CoroutineRunner
        {
            get
            {
                if (_runner != null && IsRunnerValid(_runner)) // Check if cached runner is still valid
                {
                    return _runner;
                }

                // Prioritize LatePlugin instance if available and valid
                if (_latePluginInstance != null && IsRunnerValid(_latePluginInstance))
                {
                    LatePlugin.Log.LogDebug($"{LogPrefix} Using LatePlugin instance as CoroutineRunner.");
                    _runner = _latePluginInstance;
                    return _runner;
                }

                // Fallback to GameUtilities.FindCoroutineRunner()
                LatePlugin.Log.LogDebug($"{LogPrefix} LatePlugin instance not available or invalid. Falling back to GameUtilities.FindCoroutineRunner().");
                _runner = GameUtilities.FindCoroutineRunner(); // This might find RunManager, GameDirector etc.

                if (_runner != null && !IsRunnerValid(_runner)) // Double check validity of fallback
                {
                    LatePlugin.Log.LogWarning($"{LogPrefix} Fallback runner from GameUtilities '{_runner.gameObject.name}' is invalid. Setting runner to null.");
                    _runner = null;
                }

                if (_runner == null)
                {
                    LatePlugin.Log.LogError($"{LogPrefix} No valid CoroutineRunner found (LatePlugin or fallback).");
                }
                return _runner;
            }
        }

        private static bool IsRunnerValid(MonoBehaviour? runner)
        {
            // A C# null check is often not enough for Unity objects that might be destroyed.
            // The "unity pseudo-null" check (runner == null) handles destroyed Unity objects.
            if (runner == null) return false;
            if (runner.gameObject == null) return false; // GameObject might be destroyed
            if (!runner.gameObject.activeInHierarchy) return false; // GameObject inactive
            // if (!runner.enabled) return false; // MonoBehaviour disabled - this might be too strict for some runners like LatePlugin itself.
            return true;
        }


        internal static void ClearCoroutineRunnerCache()
        {
            LatePlugin.Log.LogDebug($"{LogPrefix} Clearing cached CoroutineHelper._runner.");
            _runner = null;
            // Do not clear _latePluginInstance here, it's set by LatePlugin.Awake
        }

        internal static Coroutine? Start(IEnumerator routine)
        {
            MonoBehaviour? currentRunner = CoroutineRunner; // Access property to ensure logic runs
            if (currentRunner != null) // Already checks validity
            {
                try
                {
                    return currentRunner.StartCoroutine(routine);
                }
                catch (Exception ex)
                {
                    LatePlugin.Log.LogError($"{LogPrefix} Exception when trying to StartCoroutine on '{currentRunner.gameObject.name}': {ex.Message}");
                    return null;
                }
            }
            LatePlugin.Log.LogError($"{LogPrefix} Cannot StartCoroutine: CoroutineRunner is null or invalid.");
            return null;
        }
    }
}