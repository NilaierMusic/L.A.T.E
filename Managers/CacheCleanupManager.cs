// File: L.A.T.E/Managers/CacheCleanupManager.cs
using ExitGames.Client.Photon;
using LATE.Core;
using LATE.DataModels;
using LATE.Utilities;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LATE.Managers
{
    public static class CacheCleanupManager
    {
        public static int BatchSize = 10;
        public static float DelayBetweenBatches = 0.01f;

        public static void StartThrottledCleanup(List<StalePhotonViewData> staleViewsData, Action onComplete)
        {
            // The caller (RunManagerPatches) is now responsible for checking if a cleanup is already in progress
            // using its own _isCacheCleaningInProgress flag.

            if (staleViewsData == null || staleViewsData.Count == 0)
            {
                LatePlugin.Log.LogDebug("[CacheCleanup] No stale views provided for cleanup.");
                onComplete?.Invoke();
                return;
            }

            IEnumerator routineInstance = BatchRemoveStaleCache(new List<StalePhotonViewData>(staleViewsData), onComplete); // Pass copy
            Coroutine? coroutineAttempt = CoroutineHelper.Start(routineInstance);

            if (coroutineAttempt == null)
            {
                LatePlugin.Log.LogError("[CacheCleanup] Failed to start BatchRemoveStaleCache coroutine (CoroutineRunner might be null). Cleanup will not run effectively.");
                onComplete?.Invoke(); // Critical to call onComplete so RunManagerPatches doesn't hang
            }
            else
            {
                LatePlugin.Log.LogDebug($"[CacheCleanup] BatchRemoveStaleCache coroutine successfully started (Instance Hash: {coroutineAttempt.GetHashCode()}).");
                // The coroutine itself will call onComplete when done.
            }
        }

        private static IEnumerator BatchRemoveStaleCache(List<StalePhotonViewData> viewsToProcess, Action onCompleteCallback)
        {
            LatePlugin.Log.LogInfo($"[CacheCleanup] Starting batch removal for {viewsToProcess.Count} stale PhotonView IDs.");
            int count = 0;
            int successfullyProcessed = 0;

            foreach (StalePhotonViewData viewData in viewsToProcess)
            {
                if (viewData.InstantiationId > 0)
                {
                    PhotonUtilities.ClearPhotonCacheByInstantiationId(viewData.InstantiationId, viewData.ViewID);
                    // PhotonNetwork.RemoveBufferedRPCs(viewData.ViewID);
                    successfullyProcessed++;
                    count++;

                    if (count % BatchSize == 0)
                    {
                        yield return new WaitForSeconds(DelayBetweenBatches);
                    }
                }
                else
                {
                    LatePlugin.Log.LogWarning($"[CacheCleanup] Skipped entry with invalid InstantiationId: {viewData.InstantiationId}, ViewID: {viewData.ViewID}");
                }
            }

            LatePlugin.Log.LogInfo($"[CacheCleanup] Finished processing. {successfullyProcessed}/{viewsToProcess.Count} stale PhotonView IDs processed; cache slice bumped.");

            onCompleteCallback?.Invoke();
        }

        // StopCleanupIfRunning is more complex without a direct coroutine handle here.
        // RunManagerPatches needs to manage stopping its own conceptual "cleanup" task.
        // If the design requires stopping the Unity Coroutine itself, CacheCleanupManager would need to return it.
        // For now, let's assume RunManagerPatches simply won't start a new one if _isCacheCleaningInProgress is true.
        // If a more forceful stop of an *active* coroutine is needed, StartThrottledCleanup would need to return the Coroutine object.

        // Let's add a way to return the Coroutine so RunManagerPatches can try to stop it.
        public static Coroutine? StartThrottledCleanupAndGetCoroutine(List<StalePhotonViewData> staleViewsData, Action onComplete)
        {
            if (staleViewsData == null || staleViewsData.Count == 0)
            {
                LatePlugin.Log.LogDebug("[CacheCleanup] StartAndGet: No stale views. Invoking onComplete.");
                onComplete?.Invoke();
                return null;
            }

            // Try to get a runner specifically for this operation.
            MonoBehaviour? runner = CoroutineHelper.CoroutineRunner; // Fetches (or re-fetches if cleared) the runner.
            if (runner == null)
            {
                LatePlugin.Log.LogError("[CacheCleanup] StartAndGet: CoroutineRunner is null. Cannot start. Invoking onComplete.");
                onComplete?.Invoke();
                return null;
            }

            IEnumerator routineInstance = BatchRemoveStaleCache(new List<StalePhotonViewData>(staleViewsData), onComplete);
            Coroutine? coroutineAttempt = null;
            try
            {
                coroutineAttempt = runner.StartCoroutine(routineInstance); // Use the fetched runner directly.
            }
            catch (Exception ex)
            {
                LatePlugin.Log.LogError($"[CacheCleanup] StartAndGet: Exception during runner.StartCoroutine: {ex.Message}. Invoking onComplete.");
                coroutineAttempt = null; // Ensure it's null on exception
            }

            if (coroutineAttempt == null)
            {
                LatePlugin.Log.LogError("[CacheCleanup] StartAndGet: runner.StartCoroutine returned null or threw exception. Cleanup will not run effectively. Invoking onComplete.");
                onComplete?.Invoke(); // Ensure onComplete is called if starting failed
                return null;
            }
            else
            {
                LatePlugin.Log.LogDebug($"[CacheCleanup] StartAndGet: BatchRemoveStaleCache coroutine successfully started and returned by runner '{runner.gameObject.name}'.");
                return coroutineAttempt;
            }
        }
    }
}