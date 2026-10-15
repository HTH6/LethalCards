using System.Collections;
using GameNetcodeStuff;
using LethalCards.Cards;
using LethalCards.Networking;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Binders;

public class CardBinderBehaviour : NetworkBehaviour
{
    public const int InitialScrapValue = 10;
    private const float ServerInteractionDistancePadding = 1f;
    private bool insertionInProgress;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        GrabbableObject? binder = GetComponent<GrabbableObject>();
        if (IsServer && binder != null && !binder.scrapPersistedThroughRounds)
            StartCoroutine(InitializeNewBinderValue());
    }

    private IEnumerator InitializeNewBinderValue()
    {
        // RoundManager broadcasts its level-multiplied natural scrap values after
        // spawning the objects. Apply the binder's fixed starting value afterward.
        yield return null;

        GrabbableObject? binder = GetComponent<GrabbableObject>();
        if (!IsServer || !IsSpawned || binder == null || binder.scrapPersistedThroughRounds)
            yield break;

        binder.SetScrapValue(InitialScrapValue);
        ApplyBinderValueClientRpc(InitialScrapValue);
    }

    internal void RequestStoreHeldCard(PlayerControllerB player)
    {
        if (!IsSpawned || player == null)
            return;

        GrabbableObject? heldCard = CardBinderInteractionPatch.GetHeldCard(player);
        if (heldCard == null || !heldCard.TryGetComponent(out NetworkObject cardNetworkObject))
            return;

        StoreCardServerRpc(new NetworkObjectReference(cardNetworkObject));
    }

    [ServerRpc(RequireOwnership = false)]
    private void StoreCardServerRpc(
        NetworkObjectReference requestedCard,
        ServerRpcParams rpcParams = default)
    {
        if (insertionInProgress || !IsSpawned)
            return;

        insertionInProgress = true;
        try
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            NetworkManager? manager = NetworkManager.Singleton;
            bool playerResolved = TryResolvePlayer(senderClientId, out PlayerControllerB player);
            bool cardRefValid = requestedCard.NetworkObjectId != ulong.MaxValue;
            NetworkObject? cardNetworkObject = null;
            bool cardNetworkObjectResolved = false;
            if (cardRefValid && manager != null &&
                requestedCard.TryGet(out NetworkObject resolvedCardNetworkObject, manager))
            {
                cardNetworkObject = resolvedCardNetworkObject;
                cardNetworkObjectResolved = true;
            }

            GrabbableObject? card = cardNetworkObjectResolved
                ? cardNetworkObject!.GetComponent<GrabbableObject>()
                : null;
            CardInstanceData? cardData = cardNetworkObjectResolved
                ? cardNetworkObject!.GetComponent<CardInstanceData>()
                : null;
            NetworkItemConsumption? consumption = cardNetworkObjectResolved
                ? cardNetworkObject!.GetComponent<NetworkItemConsumption>()
                : null;
            GrabbableObject? heldObjectServer = playerResolved
                ? player.currentlyHeldObjectServer
                : null;
            bool heldObjectMatches = card != null && heldObjectServer == card;

            Plugin.Log.LogInfo(
                $"CARD BINDER INSERT REQUEST | Binder={NetworkObjectId} | SenderClientId={senderClientId} | " +
                $"PlayerResolved={playerResolved} | CardRefValid={cardRefValid} | " +
                $"CardNetworkObjectResolved={cardNetworkObjectResolved} | " +
                $"CardNetworkObjectId={(cardRefValid ? requestedCard.NetworkObjectId.ToString() : "<null>")} | " +
                $"GrabbableResolved={card != null} | CardDataResolved={cardData != null} | " +
                $"ConsumptionResolved={consumption != null} | " +
                $"HeldObjectServer={(heldObjectServer != null && heldObjectServer.IsSpawned ? heldObjectServer.NetworkObjectId.ToString() : "<null>")} | " +
                $"HeldObjectMatches={heldObjectMatches}");

            if (!playerResolved)
            {
                LogRejected(senderClientId, "InvalidPlayerReference");
                return;
            }

            if (!player.isPlayerControlled || player.isPlayerDead)
            {
                LogRejected(senderClientId, "InvalidPlayerState");
                return;
            }

            if (!cardRefValid || !cardNetworkObjectResolved)
            {
                LogRejected(senderClientId, "InvalidCardReference");
                return;
            }

            GrabbableObject? binder = GetComponent<GrabbableObject>();
            if (binder == null)
            {
                LogRejected(senderClientId, "MissingBinderComponent");
                return;
            }

            if (card == null)
            {
                LogRejected(senderClientId, "MissingGrabbableComponent");
                return;
            }

            if (cardData == null)
            {
                LogRejected(senderClientId, "MissingCardComponent");
                return;
            }

            if (consumption == null)
            {
                LogRejected(senderClientId, "MissingConsumptionComponent");
                return;
            }

            if (CardDatabase.GetById(cardData.CardId) == null)
            {
                LogRejected(senderClientId, "UnknownCardDefinition");
                return;
            }

            if (binder.isHeld || binder.heldByPlayerOnServer ||
                !NetworkItemConsumption.IsValidHolder(player, card))
            {
                LogRejected(senderClientId, "InvalidHeldState");
                return;
            }

            float maximumDistance = player.grabDistance + ServerInteractionDistancePadding;
            if (Vector3.Distance(player.transform.position, transform.position) > maximumDistance)
            {
                LogRejected(senderClientId, "OutOfRange");
                return;
            }

            if (player.gameplayCamera == null || Physics.Linecast(
                    player.gameplayCamera.transform.position,
                    transform.position + player.transform.up * 0.16f,
                    1 << 30,
                    QueryTriggerInteraction.Ignore))
            {
                LogRejected(senderClientId, "Obstructed");
                return;
            }

            // Capture the server-owned current value exactly once before despawn.
            string cardId = cardData.CardId;
            int valueToStore = System.Math.Max(0, cardData.FinalValue);
            int previousBinderValue = System.Math.Max(0, binder.scrapValue);
            int newBinderValue = (int)System.Math.Min(
                int.MaxValue,
                (long)previousBinderValue + valueToStore);

            if (!consumption.TryConsumeServer())
            {
                LogRejected(senderClientId, "CardConsumptionFailed");
                return;
            }

            binder.SetScrapValue(newBinderValue);
            ApplyBinderValueClientRpc(newBinderValue);
            Plugin.Log.LogInfo(
                $"CARD BINDER STORED CARD | Player={player.playerUsername} | CardId={cardId} | " +
                $"CardValue={valueToStore} | OldBinderValue={previousBinderValue} | " +
                $"NewBinderValue={newBinderValue}");
        }
        finally
        {
            insertionInProgress = false;
        }
    }

    [ClientRpc]
    private void ApplyBinderValueClientRpc(int value)
    {
        GrabbableObject? binder = GetComponent<GrabbableObject>();
        if (binder != null && binder.scrapValue != value)
            binder.SetScrapValue(value);
    }

    private static bool TryResolvePlayer(ulong clientId, out PlayerControllerB player)
    {
        player = null!;
        NetworkManager? manager = NetworkManager.Singleton;
        StartOfRound? round = StartOfRound.Instance;
        if (manager == null || !manager.IsServer || round == null ||
            round.allPlayerScripts == null)
            return false;

        foreach (PlayerControllerB candidate in round.allPlayerScripts)
        {
            if (candidate == null || candidate.actualClientId != clientId)
                continue;

            player = candidate;
            return true;
        }

        return false;
    }

    private void LogRejected(ulong clientId, string reason)
    {
        Plugin.Log.LogWarning(
            $"CARD BINDER INSERT REJECTED | Binder={NetworkObjectId} | Client={clientId} | Reason={reason}");
    }
}
