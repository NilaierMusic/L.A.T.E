using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace L.A.T.E
{
    internal enum GameVersion
    {
        Unknown,
        Stable, // Assumes parameterless UnlockLobby()
        Beta    // Assumes UnlockLobby(bool)
    }

    internal static class GameVersionSupport
    {
        private static ManualLogSource Log => LateJoinEntry.Log;
        private static GameVersion _detectedVersion = GameVersion.Unknown;

        // Cached MethodInfo handles
        private static MethodInfo? _unlockLobbyMethod;
        private static MethodInfo? _lockLobbyMethod;
        private static bool _reflectionChecked = false;

        /// <summary>
        /// Detects the game version based on SteamManager.UnlockLobby signature.
        /// </summary>
        public static GameVersion DetectVersion()
        {
            if (_detectedVersion != GameVersion.Unknown)
            {
                return _detectedVersion;
            }

            Log.LogInfo("[VersionDetect] Attempting to detect game version via SteamManager signatures...");

            // Try finding the Beta signature: UnlockLobby(bool)
            MethodInfo? betaUnlockMethod = AccessTools.Method(typeof(SteamManager), "UnlockLobby", new Type[] { typeof(bool) });

            if (betaUnlockMethod != null)
            {
                Log.LogInfo("[VersionDetect] Found UnlockLobby(bool) signature. Assuming BETA version.");
                _detectedVersion = GameVersion.Beta;
                _unlockLobbyMethod = betaUnlockMethod;
                // LockLobby seems unchanged, but let's find it too
                _lockLobbyMethod = AccessTools.Method(typeof(SteamManager), "LockLobby", Type.EmptyTypes);
            }
            else
            {
                // Try finding the Stable signature: UnlockLobby()
                MethodInfo? stableUnlockMethod = AccessTools.Method(typeof(SteamManager), "UnlockLobby", Type.EmptyTypes);
                if (stableUnlockMethod != null)
                {
                    Log.LogInfo("[VersionDetect] Found parameterless UnlockLobby() signature. Assuming STABLE version.");
                    _detectedVersion = GameVersion.Stable;
                    _unlockLobbyMethod = stableUnlockMethod;
                    _lockLobbyMethod = AccessTools.Method(typeof(SteamManager), "LockLobby", Type.EmptyTypes);
                }
                else
                {
                    Log.LogError("[VersionDetect] CRITICAL: Could not find UnlockLobby() OR UnlockLobby(bool) in SteamManager! Version detection failed.");
                    _detectedVersion = GameVersion.Unknown; // Remain unknown
                }
            }

            if (_lockLobbyMethod == null && _detectedVersion != GameVersion.Unknown)
            {
                Log.LogWarning($"[VersionDetect] Could not find LockLobby() method for detected version {_detectedVersion}. Locking might fail.");
            }

            _reflectionChecked = true;
            return _detectedVersion;
        }

        /// <summary>
        /// Calls the appropriate SteamManager.UnlockLobby method based on the detected version.
        /// </summary>
        /// <param name="makePublicForBeta">Only used for Beta: Determines the 'open' parameter (true = public, false = private/friends).</param>
        public static void UnlockSteamLobby(bool makePublicForBeta = true)
        {
            if (!_reflectionChecked) DetectVersion(); // Ensure detection has run

            if (_unlockLobbyMethod == null || SteamManager.instance == null)
            {
                Log.LogError($"[SteamHelper] Cannot unlock lobby: {(SteamManager.instance == null ? "SteamManager instance is null" : "UnlockLobby method not found")}. Detected Version: {_detectedVersion}");
                return;
            }

            try
            {
                if (_detectedVersion == GameVersion.Beta)
                {
                    Log.LogDebug($"[SteamHelper] Invoking BETA UnlockLobby(bool) with parameter: {makePublicForBeta}");
                    _unlockLobbyMethod.Invoke(SteamManager.instance, new object[] { makePublicForBeta });
                }
                else if (_detectedVersion == GameVersion.Stable)
                {
                    Log.LogDebug($"[SteamHelper] Invoking STABLE UnlockLobby()");
                    _unlockLobbyMethod.Invoke(SteamManager.instance, null); // No parameters
                }
                else
                {
                    Log.LogError("[SteamHelper] Cannot unlock lobby: Game version is Unknown.");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[SteamHelper] Exception invoking UnlockLobby for version {_detectedVersion}: {ex}");
            }
        }

        /// <summary>
        /// Calls the SteamManager.LockLobby method.
        /// </summary>
        public static void LockSteamLobby()
        {
            if (!_reflectionChecked) DetectVersion(); // Ensure detection has run

            if (_lockLobbyMethod == null || SteamManager.instance == null)
            {
                Log.LogError($"[SteamHelper] Cannot lock lobby: {(SteamManager.instance == null ? "SteamManager instance is null" : "LockLobby method not found")}. Detected Version: {_detectedVersion}");
                return;
            }

            try
            {
                Log.LogDebug($"[SteamHelper] Invoking LockLobby()");
                _lockLobbyMethod.Invoke(SteamManager.instance, null);
            }
            catch (Exception ex)
            {
                Log.LogError($"[SteamHelper] Exception invoking LockLobby: {ex}");
            }
        }
    }
}