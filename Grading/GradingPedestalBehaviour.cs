using LethalCards.Cards;
using Unity.Netcode;
using UnityEngine;
using GameNetcodeStuff;
using LethalCards.Networking;

namespace LethalCards.Grading;

public class GradingPedestalBehaviour : MonoBehaviour
{
    // public const int CostPerCard = 10;
    public static int CostPerCard => GradingManager.GradingCostPerCard;

    public void TrySubmitHeldCard(
        PlayerControllerB player)
    {
        if (
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsListening)
        {
            return;
        }

        // Host/server can process immediately.
        if (NetworkManager.Singleton.IsServer)
        {
            TrySubmitHeldCardServer(
                player
            );

            return;
        }

        // Client asks the server to validate its
        // actual held card and perform submission.
        GradingNetworkSync
            .RequestSubmission();
    }

    public static void TrySubmitHeldCardServer(
        PlayerControllerB player)
    {
        if (
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
        {
            Plugin.Log.LogWarning(
                "GRADING SERVER SUBMISSION REJECTED | " +
                "Not running on server."
            );

            return;
        }

        if (player == null)
            return;

        StartOfRound round = StartOfRound.Instance;
        if (round == null || round.currentLevel == null)
            return;

        if (round.shipIsLeaving || round.inShipPhase)
        {
            Plugin.Log.LogInfo(
                $"GRADING SUBMISSION REJECTED | ShipIsLeaving={round.shipIsLeaving} | " +
                $"InShipPhase={round.inShipPhase} | ShipHasLanded={round.shipHasLanded}");
            return;
        }

        if (
            !round.currentLevel.PlanetName.Contains("Gordion", System.StringComparison.OrdinalIgnoreCase) ||
            Object.FindObjectOfType<DepositItemsDesk>() == null ||
            Vector3.Distance(player.transform.position, GradingPedestalSpawner.GradingPedestalPosition) > 5f)
            return;

        CollectionSaveManager.LoadForCurrentSave();
        GradingSaveManager.LoadForCurrentSave();

        GrabbableObject heldObject =
            player.currentlyHeldObjectServer;

        if (heldObject == null)
        {
            Plugin.Log.LogInfo(
                $"GRADING REJECTED | " +
                $"Player={player.playerUsername} | " +
                $"Reason=No held item"
            );

            return;
        }

        CardInstanceData cardData =
            heldObject.GetComponent<CardInstanceData>();

        if (!NetworkItemConsumption.IsValidHolder(player, heldObject) ||
            heldObject.GetComponent<NetworkItemConsumption>() == null)
            return;

        if (cardData == null)
        {
            Plugin.Log.LogInfo(
                $"GRADING REJECTED | " +
                $"Player={player.playerUsername} | " +
                $"Item={heldObject.itemProperties?.itemName} | " +
                $"Reason=Not a Lethal Card"
            );

            return;
        }

        if (CardDatabase.GetById(cardData.CardId) == null)
            return;

        if (cardData.Grade > 0)
        {
            Plugin.Log.LogInfo(
                $"GRADING REJECTED | " +
                $"CardId={cardData.CardId} | " +
                $"Reason=Already graded | " +
                $"Grade={cardData.Grade}"
            );

            return;
        }

        Terminal terminal =
            Object.FindObjectOfType<Terminal>();

        if (terminal == null)
        {
            Plugin.Log.LogError(
                "GRADING ERROR | Terminal not found."
            );

            return;
        }

        if (
            terminal.groupCredits <
            CostPerCard)
        {
            GradingNetworkSync.SendInsufficientCreditsPresentation(player.actualClientId);
            Plugin.Log.LogInfo(
                $"GRADING REJECTED | " +
                $"CardId={cardData.CardId} | " +
                $"Credits={terminal.groupCredits} | " +
                $"Required={CostPerCard}"
            );

            return;
        }

        SubmitCardServer(
            player,
            heldObject,
            cardData,
            terminal
        );
    }

    private static void SubmitCardServer(
        PlayerControllerB player,
        GrabbableObject heldObject,
        CardInstanceData cardData,
        Terminal terminal)
    {
        terminal.groupCredits -=
            CostPerCard;

        // Synchronize the new shared credit total
        // through Lethal Company's normal terminal
        // networking path.
        terminal.SyncGroupCreditsServerRpc(
            terminal.groupCredits,
            terminal.numberOfItemsInDropship
        );

        Plugin.Log.LogInfo(
            $"GRADING PAYMENT | " +
            $"Player={player.playerUsername} | " +
            $"CardId={cardData.CardId} | " +
            $"Cost=${CostPerCard} | " +
            $"RemainingCredits=" +
            $"{terminal.groupCredits}"
        );

        int currentDay =
            GradingDayManager.CurrentDay;

        GradingJob job =
            GradingManager.CreateJob(
                cardData,
                currentDay
            );

        // Cosmetic only: capture authoritative identity/variant before the real card is consumed.
        GradingNetworkSync.BroadcastSubmissionPresentation(cardData.CardId, cardData.Variant);

        // Spawn the return immediately while the instant-grading test override is enabled.
        if (GradingManager.InstantGradingForTesting)
        {
            GradingReturnSpawner.TrySpawnReadyCards();
        }

        Plugin.Log.LogInfo(
            $"GRADING SUBMITTED | " +
            $"Player={player.playerUsername} | " +
            $"CardId={cardData.CardId} | " +
            $"Variant={cardData.Variant} | " +
            $"ReadyDay={job.ReadyDay}"
        );

        RemoveSubmittedCard(
            player,
            heldObject
        );
    }

    private static void RemoveSubmittedCard(
        PlayerControllerB player,
        GrabbableObject heldObject)
    {
        if (
            player == null ||
            heldObject == null)
        {
            return;
        }

        // Plugin.Log.LogInfo(
        //     $"GRADING REMOVE CARD | " +
        //     $"Player={player.playerUsername} | " +
        //     $"Object={heldObject.name} | " +
        //     $"Slot={player.currentItemSlot}"
        // );

        heldObject.GetComponent<NetworkItemConsumption>().ConsumeServer();

        // Plugin.Log.LogInfo(
        //     "GRADING CARD REMOVED | " +
        //     "Server consumption completed."
        // );
    }
}
