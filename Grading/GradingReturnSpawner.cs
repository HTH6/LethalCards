using System.Collections.Generic;
using System.Linq;
using LethalCards.Cards;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Grading;

public static class GradingReturnSpawner
{
    private static bool attemptedThisCompanyVisit;

    public static void TrySpawnReadyCards()
    {
        if (
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (StartOfRound.Instance == null)
            return;

        SelectableLevel level =
            StartOfRound.Instance.currentLevel;

        if (level == null)
            return;

        if (
            !level.PlanetName.Contains(
                "Gordion",
                System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Wait until the Company scene is actually ready.
        DepositItemsDesk depositDesk =
            Object.FindObjectOfType<DepositItemsDesk>();

        if (depositDesk == null)
            return;

        if (attemptedThisCompanyVisit)
            return;

        attemptedThisCompanyVisit = true;

        int currentDay =
            GradingDayManager.CurrentDay;

        List<GradingJob> readyJobs =
            GradingManager
                .GetReadyJobs(currentDay)
                .ToList();

        if (readyJobs.Count == 0)
            return;

        Plugin.Log.LogInfo(
            $"GRADING RETURNS READY | " +
            $"Count={readyJobs.Count} | " +
            $"CurrentDay={currentDay}"
        );

        int spawnIndex = 0;

        foreach (GradingJob job in readyJobs)
        {
            bool spawned =
                TrySpawnJob(
                    job,
                    spawnIndex
                );

            if (!spawned)
                continue;

            // IMPORTANT:
            // Do not remove the grading job here.
            //
            // The job stays saved until the player
            // actually picks up this returned card.
            spawnIndex++;
        }
    }

    private static bool TrySpawnJob(
        GradingJob job,
        int spawnIndex)
    {
        CardDefinition? card =
            CardDatabase.GetById(
                job.CardId
            );

        if (card == null)
        {
            Plugin.Log.LogError(
                $"GRADING RETURN ERROR | " +
                $"CardId={job.CardId} | " +
                $"Reason=Card definition not found"
            );

            return false;
        }

        if (
            card.ItemAsset == null ||
            card.ItemAsset.spawnPrefab == null)
        {
            Plugin.Log.LogError(
                $"GRADING RETURN ERROR | " +
                $"CardId={job.CardId} | " +
                $"Reason=Missing ItemAsset or spawnPrefab"
            );

            return false;
        }

       Vector3 pickupSurfacePosition =
            GradingPedestalSpawner
                .GradedCardRestPosition;

        Vector3 spawnPosition =
            pickupSurfacePosition +
            GetCardSpawnOffset(
                spawnIndex
            );

        GameObject obj =
            Object.Instantiate(
                card.ItemAsset.spawnPrefab,
                spawnPosition,
                Quaternion.identity
            );

        CardInstanceData instanceData =
            obj.GetComponent<CardInstanceData>();

        if (instanceData == null)
        {
            Plugin.Log.LogError(
                $"GRADING RETURN ERROR | " +
                $"CardId={job.CardId} | " +
                $"Reason=CardInstanceData missing from prefab"
            );

            Object.Destroy(
                obj
            );

            return false;
        }

        instanceData.InitializeLoaded(
            card,
            job.Variant,
            job.Grade
        );

        GradingReturnData returnData =
            obj.AddComponent<GradingReturnData>();

        returnData.Initialize(
            job.JobId
        );

        GrabbableObject grabbable =
            obj.GetComponent<GrabbableObject>();

        if (grabbable == null)
        {
            Plugin.Log.LogError(
                $"GRADING RETURN ERROR | " +
                $"CardId={job.CardId} | " +
                $"Reason=GrabbableObject missing"
            );

            Object.Destroy(
                obj
            );

            return false;
        }

        NetworkObject networkObject =
            obj.GetComponent<NetworkObject>();

        if (networkObject == null)
        {
            Plugin.Log.LogError(
                $"GRADING RETURN ERROR | " +
                $"CardId={job.CardId} | " +
                $"Reason=NetworkObject missing"
            );

            Object.Destroy(
                obj
            );

            return false;
        }

        networkObject.Spawn();

        obj.transform.position =
            spawnPosition;

        grabbable.startFallingPosition =
            spawnPosition;

        grabbable.targetFloorPosition =
            spawnPosition;

        grabbable.fallTime =
            1f;

        // CardInstanceData applies its pending grade/value
        // during OnNetworkSpawn(), so set the physical
        // scrap value only after the NetworkObject has spawned.
        int finalValue =
            instanceData.FinalValue;

        grabbable.SetScrapValue(
            finalValue
        );

        Plugin.Log.LogInfo(
            $"GRADING SCRAP VALUE APPLIED | " +
            $"CardId={job.CardId} | " +
            $"Grade={job.Grade} | " +
            $"Value=${finalValue}"
        );

        Plugin.Log.LogInfo(
            $"GRADING CARD RETURNED | " +
            $"JobId={job.JobId} | " +
            $"CardId={job.CardId} | " +
            $"Variant={job.Variant} | " +
            $"Grade={job.Grade} | " +
            $"Value=${finalValue} | " +
            $"Position={spawnPosition}"
        );

        return true;
    }

    private static Vector3 GetCardSpawnOffset(
        int spawnIndex)
    {
        int column =
            spawnIndex % 3;

        int row =
            spawnIndex / 3;

        return new Vector3(
            (column - 1) * 0.25f,
            0f,
            row * 0.22f
        );
    }

    public static void Reset()
    {
        attemptedThisCompanyVisit =
            false;
    }
}