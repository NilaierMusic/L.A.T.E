// File: L.A.T.E/Managers/LevelSyncManager.cs
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object; // Alias for UnityEngine.Object
using LATE.Core;
using LATE.Utilities;

namespace LATE.Managers;

/// <summary>
/// Responsible for synchronizing level-specific states for late-joining players.
/// This includes the overall level state (name, completion, game over),
/// module connections, extraction point states, prop switch setups,
/// Arena states, and truck screen information.
/// </summary>
internal static class LevelSyncManager
{
    private static readonly BepInEx.Logging.ManualLogSource Log = LatePlugin.Log;

    #region Core Level State Synchronization
    /// <summary>
    /// Synchronizes the core level state (current level name, levels completed, game over status)
    /// and triggers the LevelGenerator's "GenerateDone" RPC for a late-joining player.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncLevelState(Player targetPlayer)
    {
        string targetPlayerNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        RunManager runManager = RunManager.instance;

        if (runManager == null || runManager.levelCurrent == null)
        {
            Log.LogError("[LevelSyncManager] SyncLevelState: RunManager instance or current level is null. Aborting.");
            return;
        }

        if (ReflectionCache.RunManager_RunManagerPUNField == null ||
            ReflectionCache.RunManagerPUN_PhotonViewField == null ||
            ReflectionCache.RunManager_GameOverField == null)
        {
            Log.LogError("[LevelSyncManager] SyncLevelState: Critical reflection fields for RunManager/RunManagerPUN are missing from ReflectionCache. Aborting.");
            return;
        }

        LevelGenerator levelGen = LevelGenerator.Instance;

        try
        {
            object? runManagerPUNObj = ReflectionCache.RunManager_RunManagerPUNField.GetValue(runManager);
            if (runManagerPUNObj is not RunManagerPUN runManagerPUN)
            {
                Log.LogError("[LevelSyncManager] SyncLevelState: Reflected RunManagerPUN is null or not of expected type.");
                return;
            }

            if (ReflectionCache.RunManagerPUN_PhotonViewField.GetValue(runManagerPUN) is not PhotonView punPhotonView)
            {
                Log.LogError("[LevelSyncManager] SyncLevelState: Reflected RunManagerPUN's PhotonView is null or not of expected type.");
                return;
            }

            string levelName = runManager.levelCurrent.name;
            int levelsCompleted = runManager.levelsCompleted;
            bool gameOver = ReflectionCache.RunManager_GameOverField.GetValue(runManager) as bool? ?? false;

            Log.LogInfo($"[LevelSyncManager] SyncLevelState: Sending UpdateLevelRPC to {targetPlayerNickname}. Level:'{levelName}', Completed:{levelsCompleted}, GameOver:{gameOver}");
            punPhotonView.RPC("UpdateLevelRPC", targetPlayer, levelName, levelsCompleted, gameOver);
        }
        catch (Exception ex)
        {
            Log.LogError($"[LevelSyncManager] SyncLevelState: Error sending UpdateLevelRPC for {targetPlayerNickname}: {ex}");
        }

        if (levelGen != null && levelGen.PhotonView != null)
        {
            try
            {
                Log.LogInfo($"[LevelSyncManager] SyncLevelState: Sending GenerateDone RPC to {targetPlayerNickname}.");
                levelGen.PhotonView.RPC("GenerateDone", targetPlayer);
            }
            catch (Exception ex)
            {
                Log.LogError($"[LevelSyncManager] SyncLevelState: Error sending GenerateDone RPC for {targetPlayerNickname}: {ex}");
            }
        }
        else
        {
            Log.LogDebug($"[LevelSyncManager] SyncLevelState: Skipped GenerateDone RPC for {targetPlayerNickname} (LevelGenerator or its PhotonView is null).");
        }
    }
    #endregion

    #region Module Synchronization
    /// <summary>
    /// Synchronizes the connection states (top, bottom, left, right, first) of all
    /// <see cref="Module"/> components in the scene to a late-joining player.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncModuleConnectionStatesForPlayer(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        Log.LogInfo($"[LevelSyncManager][ModuleSync] Starting for {targetNickname}.");

        if (ReflectionCache.Module_SetupDoneField == null ||
            ReflectionCache.Module_ConnectingTopField == null ||
            ReflectionCache.Module_ConnectingBottomField == null ||
            ReflectionCache.Module_ConnectingRightField == null ||
            ReflectionCache.Module_ConnectingLeftField == null ||
            ReflectionCache.Module_FirstField == null)
        {
            Log.LogError("[LevelSyncManager][ModuleSync] Critical reflection failure: Required Module fields not found in ReflectionCache. Aborting.");
            return;
        }

        Module[] allModules = Object.FindObjectsOfType<Module>(true);
        if (allModules == null || allModules.Length == 0)
        {
            Log.LogDebug("[LevelSyncManager][ModuleSync] No Module components found. Skipping.");
            return;
        }

        int syncedCount = 0;
        int skippedCount = 0;
        foreach (Module module in allModules)
        {
            if (module == null)
            {
                skippedCount++;
                continue;
            }

            PhotonView? pv = PhotonUtilities.GetPhotonView(module);
            if (pv == null)
            {
                Log.LogWarning($"[LevelSyncManager][ModuleSync] Module '{module.gameObject?.name ?? "NULL_GAMEOBJECT"}' missing PhotonView. Skipping.");
                skippedCount++;
                continue;
            }

            try
            {
                bool setupDone = ReflectionCache.Module_SetupDoneField.GetValue(module) as bool? ?? false;
                if (!setupDone)
                {
                    skippedCount++;
                    continue;
                }

                bool top = ReflectionCache.Module_ConnectingTopField.GetValue(module) as bool? ?? false;
                bool bottom = ReflectionCache.Module_ConnectingBottomField.GetValue(module) as bool? ?? false;
                bool right = ReflectionCache.Module_ConnectingRightField.GetValue(module) as bool? ?? false;
                bool left = ReflectionCache.Module_ConnectingLeftField.GetValue(module) as bool? ?? false;
                bool first = ReflectionCache.Module_FirstField.GetValue(module) as bool? ?? false;

                pv.RPC("ModuleConnectionSetRPC", targetPlayer, top, bottom, right, left, first);
                syncedCount++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[LevelSyncManager][ModuleSync] Error processing module '{module.gameObject?.name ?? "NULL_GAMEOBJECT"}' (ViewID: {pv.ViewID}) for {targetNickname}: {ex}");
                skippedCount++;
            }
        }
        Log.LogInfo($"[LevelSyncManager][ModuleSync] Finished for {targetNickname}. Synced: {syncedCount}, Skipped: {skippedCount}.");
    }
    #endregion

    #region Extraction Point Synchronization
    /// <summary>
    /// Synchronizes the state, haul goal, and surplus value of all <see cref="ExtractionPoint"/>s
    /// to a late-joining player. Also handles global EP unlock RPC if no EP is active.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncExtractionPointsForPlayer(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        Log.LogInfo($"[LevelSyncManager][EPSync] Starting for {targetNickname}.");

        if (RoundDirector.instance == null ||
            ReflectionCache.ExtractionPoint_CurrentStateField == null ||
            ReflectionCache.RoundDirector_ExtractionPointActiveField == null ||
            ReflectionCache.RoundDirector_ExtractionPointSurplusField == null ||
            ReflectionCache.ExtractionPoint_HaulGoalFetchedField == null ||
            ReflectionCache.RoundDirector_ExtractionPointCurrentField == null ||
            ReflectionCache.ExtractionPoint_IsShopField == null)
        {
            Log.LogError("[LevelSyncManager][EPSync] Critical instance or reflection field missing from ReflectionCache. Aborting.");
            return;
        }

        ExtractionPoint[] allExtractionPoints = Object.FindObjectsOfType<ExtractionPoint>(true);
        int hostSurplus;
        bool isAnyEpActiveOnHost;
        ExtractionPoint? currentActiveEpOnHost;

        try
        {
            hostSurplus = ReflectionCache.RoundDirector_ExtractionPointSurplusField.GetValue(RoundDirector.instance) as int? ?? 0;
            isAnyEpActiveOnHost = ReflectionCache.RoundDirector_ExtractionPointActiveField.GetValue(RoundDirector.instance) as bool? ?? false;
            currentActiveEpOnHost = ReflectionCache.RoundDirector_ExtractionPointCurrentField.GetValue(RoundDirector.instance) as ExtractionPoint;

            if (isAnyEpActiveOnHost && currentActiveEpOnHost == null)
            {
                Log.LogWarning("[LevelSyncManager][EPSync] Host EP state inconsistent: Active but current EP is null. Treating as inactive.");
                isAnyEpActiveOnHost = false;
            }
            else if (!isAnyEpActiveOnHost && currentActiveEpOnHost != null)
            {
                Log.LogWarning("[LevelSyncManager][EPSync] Host EP state inconsistent: Inactive but current EP is set. Clearing current EP.");
                currentActiveEpOnHost = null;
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"[LevelSyncManager][EPSync] Error reflecting RoundDirector state for {targetNickname}: {ex}. Aborting.");
            return;
        }

        PhotonView? roundDirectorPv = PhotonUtilities.GetPhotonView(RoundDirector.instance);
        if (roundDirectorPv == null)
        {
            Log.LogError("[LevelSyncManager][EPSync] RoundDirector PhotonView not found. Critical for activating EP. Aborting for this player.");
            return;
        }

        Log.LogInfo($"[LevelSyncManager][EPSync] Host EP Status for {targetNickname} - Active: {isAnyEpActiveOnHost}, Current EP: '{currentActiveEpOnHost?.name ?? "None"}', Surplus: {hostSurplus}");

        PhotonView? firstEpPvForGlobalUnlock = null;

        if (allExtractionPoints != null)
        {
            foreach (ExtractionPoint ep in allExtractionPoints)
            {
                if (ep == null) continue;

                PhotonView? pv = PhotonUtilities.GetPhotonView(ep);
                if (pv == null) continue;

                if (firstEpPvForGlobalUnlock == null) firstEpPvForGlobalUnlock = pv;

                try
                {
                    ExtractionPoint.State hostState = ReflectionCache.ExtractionPoint_CurrentStateField.GetValue(ep) as ExtractionPoint.State? ?? ExtractionPoint.State.Idle;
                    bool isThisTheShopEP = ReflectionCache.ExtractionPoint_IsShopField.GetValue(ep) as bool? ?? false;

                    if (isAnyEpActiveOnHost && currentActiveEpOnHost != null && ep == currentActiveEpOnHost && !isThisTheShopEP)
                    {
                        // This is the Active EP on the host.
                        Log.LogInfo($"[LevelSyncManager][EPSync] Processing ACTIVE EP '{ep.name}' for {targetPlayer.NickName}.");

                        // Step 1: Trigger the official activation path via RoundDirector.
                        // This sets RoundDirector flags and calls ButtonPress() on the EP.
                        // ButtonPress() should call StateSet(State.Active) if the EP is Idle (which it is on join).
                        Log.LogInfo($"[LevelSyncManager][EPSync] Sending 'ExtractionPointActivateRPC' (via RoundDirector) for EP '{ep.name}' (ViewID: {pv.ViewID}) to {targetPlayer.NickName}.");
                        roundDirectorPv.RPC("ExtractionPointActivateRPC", targetPlayer, pv.ViewID);

                        // Step 2: Send the authoritative HaulGoalSetRPC.
                        // This corrects the haul goal after StateActive() might have set a preliminary one.
                        bool hostGoalFetched = ReflectionCache.ExtractionPoint_HaulGoalFetchedField.GetValue(ep) as bool? ?? false;
                        if (hostGoalFetched && ep.haulGoal > 0)
                        {
                            Log.LogInfo($"[LevelSyncManager][EPSync] Sending 'HaulGoalSetRPC' for active EP '{ep.name}' to {targetPlayer.NickName} with goal {ep.haulGoal}.");
                            pv.RPC("HaulGoalSetRPC", targetPlayer, ep.haulGoal);
                        }
                        else
                        {
                            if (!hostGoalFetched) Log.LogWarning($"[LevelSyncManager][EPSync] HaulGoal not fetched for active EP '{ep.name}'. 'HaulGoalSetRPC' not sent to {targetPlayer.NickName}.");
                            else if (ep.haulGoal <= 0) Log.LogWarning($"[LevelSyncManager][EPSync] HaulGoal is {ep.haulGoal} for active EP '{ep.name}'. 'HaulGoalSetRPC' not sent to {targetPlayer.NickName}.");
                        }

                        // Step 3: Explicitly send StateSetRPC(State.Active) *last*.
                        // This is a failsafe to ensure the state is definitely Active after everything else.
                        // It might be redundant but shouldn't cause issues if StateActive() init already ran.
                        Log.LogInfo($"[LevelSyncManager][EPSync] Explicitly sending 'StateSetRPC(Active)' for active EP '{ep.name}' to {targetPlayer.NickName}.");
                        pv.RPC("StateSetRPC", targetPlayer, ExtractionPoint.State.Active);

                        // Always sync surplus for all EPs, using the global surplus value.
                        pv.RPC("ExtractionPointSurplusRPC", targetPlayer, hostSurplus);

                    }
                    else // This is NOT the Active EP on the host (or no EP is active)
                    {
                        Log.LogInfo($"[LevelSyncManager][EPSync] Processing NON-ACTIVE EP '{ep.name}' (Host State: {hostState}) for {targetPlayer.NickName}.");
                        // Sync its actual state
                        pv.RPC("StateSetRPC", targetPlayer, hostState);

                        // Always sync surplus for all EPs.
                        pv.RPC("ExtractionPointSurplusRPC", targetPlayer, hostSurplus);

                        // If an EP *is* active elsewhere and this one is Idle, deny it.
                        if (isAnyEpActiveOnHost && currentActiveEpOnHost != null && hostState == ExtractionPoint.State.Idle && !isThisTheShopEP)
                        {
                            Log.LogInfo($"[LevelSyncManager][EPSync] Denying idle EP '{ep.name}' for {targetPlayer.NickName} as another EP is active.");
                            pv.RPC("ButtonDenyRPC", targetPlayer);
                        }
                        else
                        {
                            Log.LogInfo($"[LevelSyncManager][EPSync] Not denying EP '{ep.name}'. State is {hostState}, IsShop: {isThisTheShopEP}, IsAnyEpActive: {isAnyEpActiveOnHost}.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.LogError($"[LevelSyncManager][EPSync] RPC Error for EP '{ep.name}' for {targetPlayer.NickName}: {ex}");
                }
            }
        }

        if (!isAnyEpActiveOnHost && firstEpPvForGlobalUnlock != null)
        {
            try
            {
                firstEpPvForGlobalUnlock.RPC("ExtractionPointsUnlockRPC", targetPlayer);
                Log.LogInfo($"[LevelSyncManager][EPSync] Sent global ExtractionPointsUnlockRPC to {targetNickname}.");
            }
            catch (Exception ex)
            {
                Log.LogError($"[LevelSyncManager][EPSync] Failed global ExtractionPointsUnlockRPC for {targetNickname}: {ex}");
            }
        }
        Log.LogInfo($"[LevelSyncManager][EPSync] Finished for {targetNickname}.");
    }
    #endregion

    #region Valuable Prop Switch Synchronization
    /// <summary>
    /// Triggers the VolumesAndSwitchSetupRPC on the <see cref="ValuableDirector"/>
    /// for a late-joining player to initialize <see cref="ValuablePropSwitch"/> states.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void TriggerPropSwitchSetup(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        ValuableDirector director = ValuableDirector.instance;

        if (director == null)
        {
            Log.LogDebug($"[LevelSyncManager][PropSwitchSync] ValuableDirector instance is null. Skipping for {targetNickname}.");
            return;
        }

        PhotonView? directorPV = PhotonUtilities.GetPhotonView(director);
        if (directorPV == null)
        {
            Log.LogWarning($"[LevelSyncManager][PropSwitchSync] ValuableDirector's PhotonView is null. Skipping for {targetNickname}.");
            return;
        }

        try
        {
            directorPV.RPC("VolumesAndSwitchSetupRPC", targetPlayer);
            Log.LogInfo($"[LevelSyncManager][PropSwitchSync] Sent VolumesAndSwitchSetupRPC to {targetNickname}.");
        }
        catch (Exception ex)
        {
            Log.LogError($"[LevelSyncManager][PropSwitchSync] Error sending VolumesAndSwitchSetupRPC to {targetNickname}: {ex}");
        }
    }
    #endregion

    #region Truck Screen Synchronization
    /// <summary>
    /// Synchronizes the current page and typing state of the <see cref="TruckScreenText"/>
    /// to a late-joining player.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncTruckScreenForPlayer(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
        TruckScreenText screen = TruckScreenText.instance;

        if (screen == null)
        {
            Log.LogDebug("[LevelSyncManager][TruckSync] TruckScreenText instance is null. Skipping.");
            return;
        }

        PhotonView? pv = PhotonUtilities.GetPhotonView(screen);
        if (pv == null)
        {
            Log.LogWarning("[LevelSyncManager][TruckSync] TruckScreenText's PhotonView is null. Skipping.");
            return;
        }

        if (ReflectionCache.TruckScreenText_CurrentPageIndexField == null)
        {
            Log.LogError("[LevelSyncManager][TruckSync] Reflection field TruckScreenText_CurrentPageIndexField is missing from ReflectionCache. Aborting.");
            return;
        }

        try
        {
            pv.RPC("InitializeTextTypingRPC", targetPlayer);

            int hostPage = -1;
            try
            {
                hostPage = ReflectionCache.TruckScreenText_CurrentPageIndexField.GetValue(screen) as int? ?? -1;
            }
            catch (Exception ex)
            {
                Log.LogError($"[LevelSyncManager][TruckSync] Error reflecting currentPageIndex for {targetNickname}: {ex}");
            }

            if (hostPage >= 0)
            {
                pv.RPC("GotoPageRPC", targetPlayer, hostPage);
                Log.LogInfo($"[LevelSyncManager][TruckSync] Synced page {hostPage} for {targetNickname}.");
            }
            else
            {
                Log.LogInfo($"[LevelSyncManager][TruckSync] Synced init for {targetNickname}. Host page was invalid or not set ({hostPage}).");
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"[LevelSyncManager][TruckSync] Overall error during truck screen sync for {targetNickname}: {ex}");
        }
    }
    #endregion

    #region Arena State Synchronization
    /// <summary>
    /// Synchronizes Arena-specific states like crown cage destruction, current winner,
    /// live player count, and platform states to a late-joining player.
    /// This method is intended to be called by the host only.
    /// </summary>
    /// <param name="targetPlayer">The late-joining player to synchronize.</param>
    internal static void SyncArenaStateForPlayer(Player targetPlayer)
    {
        string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";

        if (targetPlayer == null)
        {
            Log.LogWarning("[LevelSyncManager][ArenaSync] Target player is null. Aborting.");
            return;
        }
        if (!PhotonUtilities.IsRealMasterClient())
        {
            Log.LogDebug("[LevelSyncManager][ArenaSync] Not MasterClient. Skipping.");
            return;
        }

        Arena arenaInstance = Arena.instance;
        if (arenaInstance == null)
        {
            Log.LogWarning("[LevelSyncManager][ArenaSync] Arena.instance is null. Aborting.");
            return;
        }

        PhotonView? arenaPV;
        try
        {
            if (ReflectionCache.Arena_PhotonViewField == null)
            {
                Log.LogError("[LevelSyncManager][ArenaSync] Reflection field Arena_PhotonViewField is missing from ReflectionCache. Aborting.");
                return;
            }
            arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arenaInstance) as PhotonView;
            if (arenaPV == null)
            {
                Log.LogError("[LevelSyncManager][ArenaSync] Arena's PhotonView is null after reflection. Aborting.");
                return;
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"[LevelSyncManager][ArenaSync] Error reflecting Arena.photonView for {targetNickname}: {ex}. Aborting.");
            return;
        }

        Log.LogInfo($"[LevelSyncManager][ArenaSync] Starting Arena state sync for {targetNickname}.");

        // Sync Crown Cage Destruction
        if (ReflectionCache.Arena_CrownCageDestroyedField != null)
        {
            try
            {
                bool hostCageIsDestroyed = ReflectionCache.Arena_CrownCageDestroyedField.GetValue(arenaInstance) as bool? ?? false;
                if (hostCageIsDestroyed)
                {
                    arenaPV.RPC("DestroyCrownCageRPC", targetPlayer);
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[LevelSyncManager][ArenaSync] Error syncing crown cage state for {targetNickname}: {ex}");
            }
        }

        // Sync Winner
        if (ReflectionCache.Arena_WinnerPlayerField != null)
        {
            try
            {
                if (ReflectionCache.Arena_WinnerPlayerField.GetValue(arenaInstance) is PlayerAvatar currentWinner)
                {
                    int winnerPhysGrabberViewID = GameUtilities.GetPhysGrabberViewId(currentWinner);
                    if (winnerPhysGrabberViewID > 0)
                    {
                        arenaPV.RPC("CrownGrabRPC", targetPlayer, winnerPhysGrabberViewID);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[LevelSyncManager][ArenaSync] Error syncing arena winner for {targetNickname}: {ex}");
            }
        }

        // Sync Player Killed (Live Player Count)
        if (ReflectionCache.PlayerAvatar_IsDisabledField != null && GameDirector.instance?.PlayerList != null)
        {
            int actualLivePlayerCount = 0;
            foreach (PlayerAvatar pa in GameDirector.instance.PlayerList)
            {
                if (pa == null) continue;
                try
                {
                    bool isDisabled = ReflectionCache.PlayerAvatar_IsDisabledField.GetValue(pa) as bool? ?? true;
                    if (!isDisabled)
                    {
                        actualLivePlayerCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[LevelSyncManager][ArenaSync] Error reflecting isDisabled for player {pa.name} during live count: {ex}. Assuming alive for safety.");
                    actualLivePlayerCount++;
                }
            }
            try
            {
                arenaPV.RPC("PlayerKilledRPC", targetPlayer, actualLivePlayerCount);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[LevelSyncManager][ArenaSync] Error sending PlayerKilledRPC for {targetNickname}: {ex}");
            }
        }

        // Trigger platform sync via dedicated manager
        HostArenaPlatformSyncManager.Instance.StartPlatformCatchUpForPlayer(targetPlayer, arenaInstance);

        Log.LogInfo($"[LevelSyncManager][ArenaSync] Finished main Arena state sync for {targetNickname}. Platform sync delegated.");
    }
    #endregion

    #region Nested Class: HostArenaPlatformSyncManager
    /// <summary>
    /// Manages the host-side coroutine for synchronizing <see cref="Arena"/> platform states
    /// to late-joining players. This is a nested helper class that ensures only one
    /// instance runs, attached to a persistent GameObject.
    /// </summary>
    internal class HostArenaPlatformSyncManager : MonoBehaviourPunCallbacks
    {
        private static HostArenaPlatformSyncManager? _instance;
        private static readonly object _lock = new object(); // For thread-safe singleton creation

        /// <summary>
        /// Gets the singleton instance of the <see cref="HostArenaPlatformSyncManager"/>.
        /// Creates the instance if it doesn't already exist.
        /// </summary>
        public static HostArenaPlatformSyncManager Instance
        {
            get
            {
                lock (_lock) // Ensure thread safety during instance creation
                {
                    if (_instance == null)
                    {
                        _instance = FindObjectOfType<HostArenaPlatformSyncManager>();
                        if (_instance == null)
                        {
                            GameObject go = new GameObject("LATE_HostArenaPlatformSyncManager_Singleton");
                            _instance = go.AddComponent<HostArenaPlatformSyncManager>();
                            // DontDestroyOnLoad(go); // Consider if this component truly needs to persist across main menu, etc.
                            Log.LogInfo("[ArenaPlatformSyncManager] Created Singleton Instance.");
                        }
                    }
                    return _instance;
                }
            }
        }

        private const float RPC_CATCHUP_DELAY_SECONDS = 0.30f;
        private const float INITIAL_SETTLE_DELAY_SECONDS = 0.5f;

        // Private constructor to prevent external instantiation for singleton pattern.
        private HostArenaPlatformSyncManager() { }

        /// <summary>
        /// Initiates the platform catch-up sequence for a late-joining player.
        /// This method must be called by the host.
        /// </summary>
        /// <param name="targetPlayer">The late-joining player requiring platform state synchronization.</param>
        /// <param name="arenaInstance">The current <see cref="Arena"/> instance.</param>
        public void StartPlatformCatchUpForPlayer(Player targetPlayer, Arena arenaInstance)
        {
            if (!PhotonUtilities.IsRealMasterClient())
            {
                Log.LogDebug("[ArenaPlatformSyncManager] Not MasterClient. Skipping platform catch-up start.");
                return;
            }
            if (arenaInstance == null)
            {
                Log.LogError("[ArenaPlatformSyncManager] arenaInstance is null. Cannot start platform catch-up.");
                return;
            }
            if (targetPlayer == null)
            {
                Log.LogError("[ArenaPlatformSyncManager] targetPlayer is null. Cannot start platform catch-up.");
                return;
            }

            Log.LogInfo($"[ArenaPlatformSyncManager] Queuing platform catch-up for {targetPlayer.NickName}.");
            StartCoroutine(CatchUpPlayerPlatformSequence(targetPlayer, arenaInstance));
        }

        /// <summary>
        /// Coroutine that replays the Arena platform removal sequence for a late-joining player
        /// and then sets the final current Arena state.
        /// </summary>
        private IEnumerator CatchUpPlayerPlatformSequence(Player targetPlayer, Arena arena)
        {
            string targetNickname = targetPlayer?.NickName ?? $"ActorNr {targetPlayer?.ActorNumber ?? -1}";
            Log.LogInfo($"[ArenaPlatformSync] Coroutine started for {targetNickname}. Waiting for initial client settle ({INITIAL_SETTLE_DELAY_SECONDS}s).");
            yield return new WaitForSeconds(INITIAL_SETTLE_DELAY_SECONDS);

            if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
            {
                Log.LogWarning($"[ArenaPlatformSync] Player {targetNickname} left room during initial settle. Aborting platform catch-up.");
                yield break;
            }

            PhotonView? arenaPV;
            Arena.States hostCurrentArenaState;
            int hostArenaLevel;

            if (ReflectionCache.Arena_PhotonViewField == null)
            {
                Log.LogError("[ArenaPlatformSync] Reflection field Arena_PhotonViewField is missing from ReflectionCache. Aborting for {targetNickname}.");
                yield break;
            }
            try
            {
                arenaPV = ReflectionCache.Arena_PhotonViewField.GetValue(arena) as PhotonView;
                if (arenaPV == null)
                {
                    Log.LogError($"[ArenaPlatformSync] Arena's PhotonView is null after reflection for {targetNickname}. Aborting.");
                    yield break;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.photonView for {targetNickname}: {ex}. Aborting.");
                yield break;
            }

            if (ReflectionCache.Arena_CurrentStateField == null)
            {
                Log.LogWarning($"[ArenaPlatformSync] Reflection field Arena_CurrentStateField missing. Defaulting state for {targetNickname}.");
                hostCurrentArenaState = Arena.States.Idle;
            }
            else
            {
                try
                {
                    hostCurrentArenaState = ReflectionCache.Arena_CurrentStateField.GetValue(arena) as Arena.States? ?? Arena.States.Idle;
                }
                catch (Exception ex)
                {
                    Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.currentState for {targetNickname}: {ex}. Defaulting state.");
                    hostCurrentArenaState = Arena.States.Idle;
                }
            }

            if (ReflectionCache.Arena_LevelField == null)
            {
                Log.LogError("[ArenaPlatformSync] Reflection field Arena_LevelField is missing from ReflectionCache. Cannot determine platform level for {targetNickname}. Aborting.");
                yield break;
            }
            try
            {
                hostArenaLevel = ReflectionCache.Arena_LevelField.GetValue(arena) as int? ?? 0;
            }
            catch (Exception ex)
            {
                Log.LogError($"[ArenaPlatformSync] Error reflecting Arena.level for {targetNickname}: {ex}. Aborting.");
                yield break;
            }

            Log.LogInfo($"[ArenaPlatformSync] Replaying {hostArenaLevel} platform levels for {targetNickname}. Current host state: {hostCurrentArenaState}");

            if (hostArenaLevel > 0)
            {
                for (int i = 0; i < hostArenaLevel; i++)
                {
                    if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
                    {
                        Log.LogWarning($"[ArenaPlatformSync] Player {targetNickname} left room during platform replay (Level {i + 1}/{hostArenaLevel}). Aborting.");
                        yield break;
                    }

                    Log.LogDebug($"[ArenaPlatformSync] Sending PlatformWarning (Level {i + 1}) to {targetNickname}.");
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformWarning);
                    yield return new WaitForSeconds(RPC_CATCHUP_DELAY_SECONDS);

                    if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
                    {
                        Log.LogWarning($"[ArenaPlatformSync] Player {targetNickname} left room during platform replay delay (Level {i + 1}/{hostArenaLevel}). Aborting.");
                        yield break;
                    }

                    Log.LogDebug($"[ArenaPlatformSync] Sending PlatformRemove (Level {i + 1}) to {targetNickname}.");
                    arenaPV.RPC("StateSetRPC", targetPlayer, global::Arena.States.PlatformRemove);
                    yield return new WaitForSeconds(RPC_CATCHUP_DELAY_SECONDS);
                }
            }

            // Re-fetch current state as it might have changed during the loop
            if (ReflectionCache.Arena_CurrentStateField != null)
            {
                try
                {
                    hostCurrentArenaState = ReflectionCache.Arena_CurrentStateField.GetValue(arena) as Arena.States? ?? Arena.States.Idle;
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"[ArenaPlatformSync] Error re-reflecting Arena.currentState for {targetNickname}: {ex}. Using last known.");
                }
            }

            if (PhotonNetwork.CurrentRoom == null || !PhotonNetwork.CurrentRoom.Players.ContainsKey(targetPlayer.ActorNumber))
            {
                Log.LogWarning($"[ArenaPlatformSync] Player {targetNickname} left room before final state sync. Aborting.");
                yield break;
            }

            Log.LogInfo($"[ArenaPlatformSync] Sending final Arena state ({hostCurrentArenaState}) to {targetNickname}.");
            arenaPV.RPC("StateSetRPC", targetPlayer, hostCurrentArenaState);
            yield return new WaitForSeconds(RPC_CATCHUP_DELAY_SECONDS);

            Log.LogInfo($"[ArenaPlatformSync] Arena platform catch-up sequence complete for {targetNickname}.");
        }
    }
    #endregion
}