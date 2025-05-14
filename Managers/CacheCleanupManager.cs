// File: L.A.T.E/Managers/CacheCleanupManager.cs
using ExitGames.Client.Photon;
using LATE.Core;
using LATE.Utilities;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace LATE.Managers
{
    public static class CacheCleanupManager
    {
        // Tweak these values as necessary.
        public static int BatchSize = 10;
        public static float DelayBetweenBatches = 0.05f;

        /// <summary>
        /// Starts the throttled cleanup of stale PhotonView cache entries.
        /// This method uses your proven PhotonUtilities.ClearPhotonCache helper.
        /// </summary>
        public static void StartThrottledCleanup()
        {
            CoroutineHelper.Start(BatchRemoveStaleCache());
        }

        private static IEnumerator BatchRemoveStaleCache()
        {
            List<PhotonView> staleViews = new List<PhotonView>();

            // Collect all PhotonViews from the current (old) level;
            // we filter out persistent objects (those in DontDestroyOnLoad)
            foreach (PhotonView pv in UnityObject.FindObjectsOfType<PhotonView>(true))
            {
                if (pv == null)
                    continue;
                if (pv.gameObject.scene.buildIndex == -1)
                    continue; // Skip objects that persist between scenes

                staleViews.Add(pv);
            }

            int count = 0;
            foreach (PhotonView pv in staleViews)
            {
                if (pv != null && pv.InstantiationId > 0)
                {
                    // Remove this PhotonView’s instantiation event.
                    PhotonUtilities.ClearPhotonCache(pv);

                    // Also remove any buffered RPCs (this call is fast, being client–side).
                    PhotonNetwork.RemoveBufferedRPCs(pv.ViewID);

                    count++;

                    // Throttle: pause after processing a batch.
                    if (count % BatchSize == 0)
                    {
                        yield return new WaitForSeconds(DelayBetweenBatches);
                    }
                }
            }

            // Then bump the cache slice so that late joiners only get events from the current state.
            PhotonNetwork.NetworkingClient.OpRaiseEvent(
                0, // Use an acceptable event code (e.g. 0)
                null,
                new RaiseEventOptions { CachingOption = EventCaching.SliceIncreaseIndex },
                new SendOptions { Reliability = true }
            );

            LatePlugin.Log.LogDebug($"[CacheCleanup] Processed {count} stale PhotonViews; cache slice bumped.");

            yield break;
        }
    }
}