using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using LethalCards.Cards;
using LethalCards.Grading;

namespace LethalCards.Debugging;

// [HarmonyPatch(typeof(StartOfRound), "Update")]
public static class DebugSpawnPatch
{
    private static bool spawnedTestPacks;

    public static void Reset() => spawnedTestPacks = false;

    [HarmonyPostfix]
    private static void Postfix()
    {
        if (NetworkManager.Singleton == null)
            return;

        if (!NetworkManager.Singleton.IsServer)
            return;

        if (GameNetworkManager.Instance == null)
            return;

        PlayerControllerB player =
            GameNetworkManager.Instance.localPlayerController;

        if (player == null)
            return;

        // Don't run while still at the main menu / before the player exists.
        if (!player.isPlayerControlled)
            return;

        // Everything below here is only for spawning
        // the Light and Heavy debug booster packs once.
        if (spawnedTestPacks)
            return;

        Plugin.Log.LogInfo(
            $"DEBUG PATCH: Player ready | " +
            $"Position={player.transform.position}"
        );

        SpawnBooster(
            Plugin.LightBoosterItem,
            "Light",
            player,
            -0.6f
        );

        SpawnBooster(
            Plugin.HeavyBoosterItem,
            "Heavy",
            player,
            0.6f
        );

        spawnedTestPacks = true;

        Plugin.Log.LogInfo(
            "DEBUG PATCH: Test booster spawn attempt complete."
        );
    }

    private static void SpawnBooster(
        Item? item,
        string label,
        PlayerControllerB player,
        float horizontalOffset)
    {
        if (item == null)
        {
            Plugin.Log.LogError(
                $"DEBUG PATCH: {label} Item is null."
            );

            return;
        }

        if (item.spawnPrefab == null)
        {
            Plugin.Log.LogError(
                $"DEBUG PATCH: {label} spawnPrefab is null."
            );

            return;
        }

        Vector3 spawnPosition =
            player.transform.position +
            player.transform.forward * 2f +
            player.transform.right * horizontalOffset +
            Vector3.up * 1f;

        Plugin.Log.LogInfo(
            $"DEBUG SPAWN REQUEST | " +
            $"Type={label} | " +
            $"Prefab={item.spawnPrefab.name} | " +
            $"PlayerPos={player.transform.position} | " +
            $"SpawnPos={spawnPosition}"
        );

        GameObject obj =
            Object.Instantiate(
                item.spawnPrefab,
                spawnPosition,
                Quaternion.identity
            );

        Plugin.Log.LogInfo(
            $"DEBUG INSTANTIATED | " +
            $"Type={label} | " +
            $"Name={obj.name} | " +
            $"Position={obj.transform.position} | " +
            $"Active={obj.activeInHierarchy}"
        );

        NetworkObject networkObject =
            obj.GetComponent<NetworkObject>();

        if (networkObject == null)
        {
            Plugin.Log.LogError(
                $"DEBUG PATCH: {label} has no NetworkObject."
            );

            Object.Destroy(obj);

            return;
        }

        Plugin.Log.LogInfo(
            $"DEBUG NETWORK BEFORE | " +
            $"Type={label} | " +
            $"IsSpawned={networkObject.IsSpawned}"
        );

        networkObject.Spawn();

        float distance =
            Vector3.Distance(
                player.transform.position,
                obj.transform.position
            );

        Plugin.Log.LogInfo(
            $"DEBUG NETWORK AFTER | " +
            $"Type={label} | " +
            $"IsSpawned={networkObject.IsSpawned} | " +
            $"Position={obj.transform.position} | " +
            $"Distance={distance:F2}m"
        );
    }
}
