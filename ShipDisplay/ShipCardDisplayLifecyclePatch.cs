using HarmonyLib;
using UnityEngine;

namespace LethalCards.ShipDisplay;

[HarmonyPatch(typeof(StartOfRound))]
internal static class ShipCardDisplayLifecyclePatch
{
    private const float RetryInterval = 0.5f;
    private static float nextSpawnAttempt;

    [HarmonyPatch("Start"), HarmonyPostfix]
    private static void StartPostfix(StartOfRound __instance)
    {
        nextSpawnAttempt = 0f;
        ShipCardDisplaySpawner.TrySpawn(__instance);
    }

    [HarmonyPatch("Update"), HarmonyPostfix]
    private static void UpdatePostfix(StartOfRound __instance)
    {
        if (Time.realtimeSinceStartup < nextSpawnAttempt)
            return;

        nextSpawnAttempt = Time.realtimeSinceStartup + RetryInterval;
        ShipCardDisplaySpawner.TrySpawn(__instance);
    }

    [HarmonyPatch("SpawnUnlockable"), HarmonyPrefix]
    private static void SpawnUnlockablePrefix(int __0)
    {
        ShipCardDisplaySpawner.PreparePrefabForUnlockableIndex(__0);
    }

    [HarmonyPatch("OnDestroy"), HarmonyPrefix]
    private static void DestroyPrefix(StartOfRound __instance)
    {
        ShipCardDisplaySpawner.CleanupForRoundTeardown(__instance);
        nextSpawnAttempt = 0f;
    }
}
