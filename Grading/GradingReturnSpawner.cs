using System.Collections.Generic;
using System.Linq;
using LethalCards.Cards;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Grading;

public static class GradingReturnSpawner
{
    private static readonly HashSet<string>
        spawnedJobIds =
            new HashSet<string>();

    private static readonly List<GradingReturnData> spawnedReturns = new();

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

        if (!StartOfRound.Instance.shipHasLanded || StartOfRound.Instance.shipIsLeaving || StartOfRound.Instance.inShipPhase)
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


        int currentDay =
            GradingDayManager.CurrentDay;

        List<GradingJob> readyJobs =
            GradingManager
                .GetReadyJobs(currentDay)
                .Where(job =>
                    !spawnedJobIds.Contains(
                        job.JobId))
                .ToList();

        if (readyJobs.Count == 0)
            return;

        Plugin.Log.LogInfo(
            $"GRADING RETURNS READY | " +
            $"NewCount={readyJobs.Count} | " +
            $"CurrentDay={currentDay}"
        );

        int spawnIndex =
            spawnedJobIds.Count;

        foreach (GradingJob job in readyJobs)
        {
            bool spawned =
                TrySpawnJob(
                    job,
                    spawnIndex
                );

            if (!spawned)
                continue;

            spawnedJobIds.Add(
                job.JobId
            );

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

        spawnPosition.y = -0.88f;

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

        job.EnsureFinalValue();
        instanceData.InitializeLoaded(
            card,
            job.Variant,
            job.Grade,
            job.FinalValue
        );

        GradingReturnData returnData =
            obj.GetComponent<GradingReturnData>();

        if (returnData == null)
        {
            Plugin.Log.LogError("GRADING RETURN ERROR | GradingReturnData missing from registered prefab.");
            Object.Destroy(obj);
            return false;
        }

        returnData.Initialize(
            job.JobId,
            spawnPosition
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
        spawnedReturns.Add(returnData);

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
            (column - 1) * 0.55f,
            0f,
            row * 0.22f
        );
    }

    public static void Reset()
    {
        // Retire unclaimed physical copies before a later visit can respawn them.
        foreach (GradingReturnData data in spawnedReturns)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.ShutdownInProgress ||
                data == null || !data.IsSpawned || !data.IsServer)
                continue;
            data.TryClaim();
            if (GradingManager.GetJobById(data.JobId) != null)
                data.NetworkObject.Despawn(true);
        }
        spawnedReturns.Clear();
        spawnedJobIds.Clear();
    }
}
