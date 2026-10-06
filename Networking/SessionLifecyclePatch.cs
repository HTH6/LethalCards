using HarmonyLib;
using LethalCards.Cards;
using LethalCards.Debugging;
using LethalCards.Grading;
using LethalCards.Save;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Networking;

[HarmonyPatch(typeof(StartOfRound))]
public static class SessionLifecyclePatch
{
    private const float ReturnRestoreSafetyDelay = 0.25f;
    private const float ReturnPedestalSlowLoadWarning = 5f;
    private static bool companyActive;
    private static float nextUpdate;
    private static float companySceneReadyTime;
    private static float returnRestoreTime = -1f;
    private static bool returnReadinessWarningLogged;

    [HarmonyPatch("Start"), HarmonyPrefix]
    private static void StartPrefix() => ResetSession();

    [HarmonyPatch("OnDestroy"), HarmonyPrefix]
    private static void DestroyPrefix() => ResetSession();

    [HarmonyPatch("Update"), HarmonyPostfix]
    private static void UpdatePostfix(StartOfRound __instance)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || manager.ShutdownInProgress ||
            Time.realtimeSinceStartup < nextUpdate)
            return;
        nextUpdate = Time.realtimeSinceStartup + 0.25f;

        // ==================================================
        // DEBUG ONLY - BOOSTER BOX SHIP SPAWNS
        // Comment this call out for normal release gameplay.
        // Natural facility registration is independent.
        // ==================================================
        
        DebugSpawnPatch.SpawnDebugBoosterBoxesOnShip();

        CollectionNetworkSync.Initialize();
        GradingNetworkSync.Initialize();
        if (manager.IsServer)
        {
            CollectionSaveManager.LoadForCurrentSave();
            GradingSaveManager.LoadForCurrentSave();
        }
        else
            CollectionNetworkSync.EnsureSnapshot();

        bool companySceneReady = !__instance.shipIsLeaving && __instance.currentLevel != null &&
            __instance.currentLevel.PlanetName.Contains("Gordion", System.StringComparison.OrdinalIgnoreCase) &&
            Object.FindObjectOfType<DepositItemsDesk>() != null;

        if (companySceneReady)
        {
            if (!companyActive)
            {
                companyActive = true;
                companySceneReadyTime = Time.realtimeSinceStartup;
                returnRestoreTime = -1f;
                returnReadinessWarningLogged = false;
            }

            // Local scenery and interaction appear as soon as the Company scene is initialized.
            GradingPedestalSpawner.TrySpawn();

            if (manager.IsServer && GradingPedestalSpawner.IsReturnPedestalReady)
            {
                if (returnRestoreTime < 0f)
                {
                    returnRestoreTime = Time.realtimeSinceStartup + ReturnRestoreSafetyDelay;
                    Plugin.Log.LogInfo("GRADING PEDESTALS READY");
                }
                else if (Time.realtimeSinceStartup >= returnRestoreTime)
                {
                    GradingReturnSpawner.TrySpawnReadyCards();
                }
            }
            else if (manager.IsServer)
            {
                // Readiness must remain continuous through the safety delay.
                returnRestoreTime = -1f;
                if (!returnReadinessWarningLogged &&
                    Time.realtimeSinceStartup - companySceneReadyTime >= ReturnPedestalSlowLoadWarning)
                {
                    returnReadinessWarningLogged = true;
                    Plugin.Log.LogWarning("GRADING RETURN WAITING | Return pedestal still initializing");
                }
            }
        }
        else if (companyActive)
        {
            GradingReturnSpawner.Reset();
            GradingPedestalSpawner.Reset();
            companyActive = false;
            companySceneReadyTime = 0f;
            returnRestoreTime = -1f;
            returnReadinessWarningLogged = false;
        }
    }

    private static void ResetSession()
    {
        LethalCards.TerminalCommands.CollectionTerminalPatch.ResetContext();
        GradingReturnSpawner.Reset();
        GradingPedestalSpawner.Reset();
        CollectionNetworkSync.Shutdown();
        GradingNetworkSync.Shutdown();
        SaveIdentityManager.ResetRuntimeState();
        CollectionSaveManager.ResetLoadedSave();
        GradingSaveManager.ResetLoadedSave();
        CollectionManager.Clear();
        GradingManager.Clear();
        GradingDayManager.SetDay(0);
        DebugSpawnPatch.Reset();
        companyActive = false;
        nextUpdate = 0f;
        companySceneReadyTime = 0f;
        returnRestoreTime = -1f;
        returnReadinessWarningLogged = false;
    }
}
