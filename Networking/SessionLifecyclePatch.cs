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
    private static bool companyActive;
    private static float nextUpdate;

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
        
        //DebugSpawnPatch.SpawnDebugBoosterBoxesOnShip();

        CollectionNetworkSync.Initialize();
        GradingNetworkSync.Initialize();
        if (manager.IsServer)
        {
            CollectionSaveManager.LoadForCurrentSave();
            GradingSaveManager.LoadForCurrentSave();
        }
        else
            CollectionNetworkSync.EnsureSnapshot();

        bool ready = __instance.shipHasLanded && !__instance.shipIsLeaving && !__instance.inShipPhase &&
            __instance.currentLevel != null &&
            __instance.currentLevel.PlanetName.Contains("Gordion", System.StringComparison.OrdinalIgnoreCase) &&
            Object.FindObjectOfType<DepositItemsDesk>() != null;

        if (ready)
        {
            companyActive = true;
            // Local scenery and interaction on every peer; authoritative cards on server only.
            GradingPedestalSpawner.TrySpawn();
            if (manager.IsServer)
                GradingReturnSpawner.TrySpawnReadyCards();
        }
        else if (companyActive)
        {
            GradingReturnSpawner.Reset();
            GradingPedestalSpawner.Reset();
            companyActive = false;
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
    }
}
