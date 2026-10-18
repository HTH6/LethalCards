using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using LethalCards.Cards;
using LethalCards.Grading;
using LethalCards.Networking;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.ShipDisplay;

public sealed class ShipCardDisplayController : NetworkBehaviour
{
    private const float InteractionDistancePadding = 1f;
    private const float AttachRetryTimeout = 2f;
    private const float HeldStateSettleTimeout = 0.6f;
    private const float LocalRequestTimeout = 2f;
    private const float InteractionDebounce = 0.12f;
    private const float DiagnosticRateLimit = 2f;
    private static readonly MethodInfo? SwitchToItemSlotMethod =
        AccessTools.Method(
            typeof(PlayerControllerB),
            "SwitchToItemSlot",
            new[] { typeof(int), typeof(GrabbableObject) });

    public static event Action<int, string, CardVariant, int, int, int, bool>?
        AuthoritativeSlotChanged;

    private readonly NetworkList<ShipCardDisplaySlotState> networkSlots = new();
    private readonly Transform?[] slotAnchors =
        new Transform?[ShipCardDisplaySaveManager.SlotCount];
    private readonly GameObject?[] slotVisuals =
        new GameObject?[ShipCardDisplaySaveManager.SlotCount];

    private bool transactionInProgress;
    private bool localInteractionPending;
    private float localInteractionPendingUntil;
    private float nextLocalInteractionTime;
    private ScanNodeProperties? aggregateScanNode;
    private static bool crewWipeHandledThisRound;
    private static readonly HashSet<ShipCardDisplayController> SpawnedControllers = new();
    private static readonly Dictionary<string, float> LastDiagnosticTimes = new();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        SpawnedControllers.Add(this);
        CacheSlotAnchors();
        networkSlots.OnListChanged += OnSlotListChanged;

        if (IsServer)
        {
            ShipCardDisplayStoredCard?[] storedSlots =
                ShipCardDisplaySaveManager.LoadForCurrentSave();
            networkSlots.Clear();
            for (int slotIndex = 0;
                 slotIndex < ShipCardDisplaySaveManager.SlotCount;
                 slotIndex++)
            {
                networkSlots.Add(
                    ShipCardDisplaySlotState.FromStored(storedSlots[slotIndex]));
            }
        }

        RefreshAllVisuals();
        RefreshAggregateScanNode();
    }

    public override void OnNetworkDespawn()
    {
        SpawnedControllers.Remove(this);
        networkSlots.OnListChanged -= OnSlotListChanged;
        ClearVisuals();
        base.OnNetworkDespawn();
    }

    private void OnEnable()
    {
        // Ship furniture can be disabled during end-of-round transitions. Reconcile
        // presentation from the replicated list when it becomes visible again.
        if (!IsSpawned)
            return;

        CacheSlotAnchors();
        RefreshAllVisuals();
        RefreshAggregateScanNode();
    }

    internal bool TryGetSlotState(
        int slotIndex,
        out ShipCardDisplaySlotState state)
    {
        if (slotIndex >= 0 && slotIndex < networkSlots.Count)
        {
            state = networkSlots[slotIndex];
            return true;
        }

        state = ShipCardDisplaySlotState.Empty;
        return false;
    }

    internal void RequestDeposit(PlayerControllerB player)
    {
        // Resolve the selected slot again at the instant the request is built.
        // PlayerControllerB.currentlyHeldObject is not refreshed by vanilla when
        // switching between two non-empty inventory slots.
        GrabbableObject? card =
            ShipCardDisplayInteractionPatch.GetSelectedCard(player);
        if (!IsSpawned || player == null || card == null ||
            !card.TryGetComponent(out NetworkObject cardNetworkObject))
        {
            return;
        }

        if (!TryBeginLocalInteraction())
            return;

        InteractServerRpc(
            true,
            -1,
            new NetworkObjectReference(cardNetworkObject));
    }

    internal void RequestWithdrawal(PlayerControllerB player, int slotIndex)
    {
        if (!IsSpawned || player == null || !TryBeginLocalInteraction())
            return;

        InteractServerRpc(false, slotIndex, default);
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractServerRpc(
        bool isDeposit,
        int requestedSlot,
        NetworkObjectReference requestedCard,
        ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (transactionInProgress || !IsSpawned)
        {
            SendResult(senderClientId, InteractionResult.Busy);
            return;
        }

        transactionInProgress = true;
        StartCoroutine(ProcessInteraction(
            senderClientId,
            isDeposit,
            requestedSlot,
            requestedCard));
    }

    private IEnumerator ProcessInteraction(
        ulong senderClientId,
        bool isDeposit,
        int requestedSlot,
        NetworkObjectReference requestedCard)
    {
        try
        {
            if (!TryResolvePlayer(senderClientId, out PlayerControllerB player) ||
                !ValidatePlayerAndDistance(player))
            {
                Reject(senderClientId, "InvalidPlayerOrDistance");
                yield break;
            }

            if (isDeposit)
            {
                // A freshly picked-up object can be selected locally before the
                // vanilla grab RPC has finished transferring ownership and
                // publishing currentlyHeldObjectServer. Wait briefly for those
                // authoritative fields instead of weakening their validation.
                float deadline = Time.realtimeSinceStartup + HeldStateSettleTimeout;
                while (!IsHeldCardAuthoritativelyReady(player, requestedCard) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                TryDeposit(player, requestedCard, senderClientId);
            }
            else
            {
                float deadline = Time.realtimeSinceStartup + HeldStateSettleTimeout;
                while (!IsEmptySelectedSlotAuthoritativelyReady(player) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                TryWithdraw(player, requestedSlot, senderClientId);
            }
        }
        finally
        {
            transactionInProgress = false;
        }
    }

    private void TryDeposit(
        PlayerControllerB player,
        NetworkObjectReference requestedCard,
        ulong senderClientId)
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null ||
            !requestedCard.TryGet(out NetworkObject cardNetworkObject, manager))
        {
            Reject(senderClientId, "InvalidCardReference");
            return;
        }

        GrabbableObject? card = cardNetworkObject.GetComponent<GrabbableObject>();
        CardInstanceData? cardData = cardNetworkObject.GetComponent<CardInstanceData>();
        NetworkItemConsumption? consumption =
            cardNetworkObject.GetComponent<NetworkItemConsumption>();
        CardDefinition? definition = cardData == null
            ? null
            : CardDatabase.GetById(cardData.CardId);

        if (card == null || cardData == null || consumption == null ||
            definition == null || definition.SetNumber < 1 ||
            definition.SetNumber > ShipCardDisplaySaveManager.SlotCount ||
            !NetworkItemConsumption.IsValidHolder(player, card))
        {
            Reject(
                senderClientId,
                "InvalidHeldCard",
                Plugin.EnableDebugLogging
                    ? BuildHeldCardDiagnostic(
                        player,
                        requestedCard,
                        cardNetworkObject,
                        card,
                        cardData,
                        consumption,
                        definition)
                    : string.Empty);
            return;
        }

        int inventorySlot = FindInventorySlot(player, card);
        if (inventorySlot < 0 || inventorySlot != player.currentItemSlot)
        {
            Reject(
                senderClientId,
                "HeldCardSlotMismatch",
                Plugin.EnableDebugLogging
                    ? BuildHeldCardDiagnostic(
                        player,
                        requestedCard,
                        cardNetworkObject,
                        card,
                        cardData,
                        consumption,
                        definition)
                    : string.Empty);
            return;
        }

        int displaySlot = definition.SetNumber - 1;
        ShipCardDisplayStoredCard? outgoing =
            displaySlot < networkSlots.Count
                ? networkSlots[displaySlot].ToStored()
                : null;
        if (outgoing != null && !CanReconstruct(outgoing))
        {
            Reject(senderClientId, "StoredCardPrefabUnavailable");
            return;
        }

        ShipCardDisplayStoredCard incoming = new(
            definition.CardId,
            cardData.Variant,
            cardData.Grade,
            Math.Max(0, cardData.UngradedValue),
            Math.Max(0, cardData.FinalValue));

        if (!consumption.TryConsumeServer())
        {
            Reject(senderClientId, "CardConsumptionFailed");
            return;
        }

        if (!ShipCardDisplaySaveManager.TryCommitSlot(displaySlot, incoming))
        {
            RestoreIncomingAfterFailure(player, inventorySlot, incoming);
            Reject(senderClientId, "PersistenceFailed");
            return;
        }

        if (outgoing != null &&
            !TrySpawnIntoInventory(player, inventorySlot, outgoing, out _))
        {
            bool rollbackSaved =
                ShipCardDisplaySaveManager.TryCommitSlot(displaySlot, outgoing);
            RestoreIncomingAfterFailure(player, inventorySlot, incoming);
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY SWAP ROLLED BACK | Slot={displaySlot + 1} | " +
                $"PersistenceRollback={rollbackSaved}");
            Reject(senderClientId, "CardReconstructionFailed");
            return;
        }

        ApplyNetworkSlot(displaySlot, incoming);
        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY {(outgoing == null ? "DEPOSIT" : "SWAP")} | " +
            $"Player={player.playerUsername} | Slot={displaySlot + 1} | " +
            $"Incoming={incoming.CardId}/{incoming.Variant}/Grade{incoming.Grade}/Value{incoming.FinalValue}" +
            (outgoing == null
                ? string.Empty
                : $" | Outgoing={outgoing.CardId}/{outgoing.Variant}/Grade{outgoing.Grade}/Value{outgoing.FinalValue}"));
        SendResult(senderClientId, InteractionResult.Success);
    }

    private void TryWithdraw(
        PlayerControllerB player,
        int requestedSlot,
        ulong senderClientId)
    {
        if (requestedSlot < 0 ||
            requestedSlot >= ShipCardDisplaySaveManager.SlotCount ||
            requestedSlot >= networkSlots.Count ||
            !ValidateRequestedSlotAim(player, requestedSlot))
        {
            Reject(senderClientId, "InvalidSlotTarget");
            return;
        }

        ShipCardDisplayStoredCard? storedCard =
            networkSlots[requestedSlot].ToStored();
        if (storedCard == null)
        {
            SendResult(senderClientId, InteractionResult.AlreadyEmpty);
            return;
        }

        if (player.currentlyHeldObjectServer != null ||
            player.currentItemSlot < 0 ||
            player.currentItemSlot >= player.ItemSlots.Length ||
            player.ItemSlots[player.currentItemSlot] != null)
        {
            SendResult(senderClientId, InteractionResult.InventoryFull);
            return;
        }

        if (!CanReconstruct(storedCard))
        {
            Reject(senderClientId, "StoredCardPrefabUnavailable");
            return;
        }

        int inventorySlot = player.currentItemSlot;
        if (!ShipCardDisplaySaveManager.TryCommitSlot(requestedSlot, null))
        {
            Reject(senderClientId, "PersistenceFailed");
            return;
        }

        if (!TrySpawnIntoInventory(player, inventorySlot, storedCard, out _))
        {
            bool rollbackSaved =
                ShipCardDisplaySaveManager.TryCommitSlot(requestedSlot, storedCard);
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY WITHDRAW ROLLED BACK | Slot={requestedSlot + 1} | " +
                $"PersistenceRollback={rollbackSaved}");
            Reject(senderClientId, "CardReconstructionFailed");
            return;
        }

        ApplyNetworkSlot(requestedSlot, null);
        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY WITHDRAW | Player={player.playerUsername} | " +
            $"Slot={requestedSlot + 1} | Card={storedCard.CardId}/{storedCard.Variant}/" +
            $"Grade{storedCard.Grade}/Value{storedCard.FinalValue}");
        SendResult(senderClientId, InteractionResult.Success);
    }

    private bool TrySpawnIntoInventory(
        PlayerControllerB player,
        int inventorySlot,
        ShipCardDisplayStoredCard storedCard,
        out NetworkObject? spawnedNetworkObject)
    {
        spawnedNetworkObject = null;
        CardDefinition? definition = CardDatabase.GetById(storedCard.CardId);
        GameObject? prefab = definition?.ItemAsset?.spawnPrefab;
        if (definition == null || prefab == null || StartOfRound.Instance == null)
            return false;

        GameObject instance = UnityEngine.Object.Instantiate(
            prefab,
            player.transform.position,
            Quaternion.identity,
            StartOfRound.Instance.propsContainer);
        NetworkObject? networkObject = instance.GetComponent<NetworkObject>();
        GrabbableObject? card = instance.GetComponent<GrabbableObject>();
        CardInstanceData? cardData = instance.GetComponent<CardInstanceData>();
        if (networkObject == null || card == null || cardData == null)
        {
            UnityEngine.Object.Destroy(instance);
            return false;
        }

        cardData.InitializeLoaded(
            definition,
            storedCard.Variant,
            storedCard.Grade,
            storedCard.FinalValue,
            storedCard.UngradedValue);

        try
        {
            networkObject.Spawn();
            networkObject.ChangeOwnership(player.actualClientId);
            if (!ApplyCardToInventory(player, inventorySlot, card))
            {
                networkObject.Despawn(true);
                return false;
            }

            spawnedNetworkObject = networkObject;
            AttachCardClientRpc(
                player.actualClientId,
                inventorySlot,
                new NetworkObjectReference(networkObject));
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY CARD SPAWN FAILED | CardId={storedCard.CardId} | {exception}");
            if (networkObject.IsSpawned)
                networkObject.Despawn(true);
            else
                UnityEngine.Object.Destroy(instance);
            return false;
        }
    }

    [ClientRpc]
    private void AttachCardClientRpc(
        ulong playerClientId,
        int inventorySlot,
        NetworkObjectReference cardReference)
    {
        // The server applied the state before sending this RPC. This also avoids
        // double-counting carry weight on a listen-server host.
        if (IsServer)
            return;

        StartCoroutine(AttachCardWhenReady(
            playerClientId,
            inventorySlot,
            cardReference));
    }

    private IEnumerator AttachCardWhenReady(
        ulong playerClientId,
        int inventorySlot,
        NetworkObjectReference cardReference)
    {
        float deadline = Time.realtimeSinceStartup + AttachRetryTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (TryResolvePlayer(playerClientId, out PlayerControllerB player) &&
                inventorySlot >= 0 && inventorySlot < player.ItemSlots.Length &&
                cardReference.TryGet(out NetworkObject networkObject) &&
                networkObject != null)
            {
                GrabbableObject? existing = player.ItemSlots[inventorySlot];
                GrabbableObject? card = networkObject.GetComponent<GrabbableObject>();
                bool ownershipReady = !player.IsOwner || networkObject.IsOwner;
                if (card != null && ownershipReady &&
                    (existing == null || existing == card))
                {
                    ApplyCardToInventory(player, inventorySlot, card);
                    yield break;
                }
            }

            yield return null;
        }

        Plugin.Log.LogWarning(
            $"SHIP CARD DISPLAY INVENTORY ATTACH TIMED OUT | " +
            $"PlayerClientId={playerClientId} | Slot={inventorySlot}");
    }

    private static bool ApplyCardToInventory(
        PlayerControllerB player,
        int inventorySlot,
        GrabbableObject card)
    {
        if (player == null || card == null || inventorySlot < 0 ||
            inventorySlot >= player.ItemSlots.Length)
        {
            return false;
        }

        GrabbableObject? existing = player.ItemSlots[inventorySlot];
        if (existing != null && existing != card)
            return false;

        bool alreadyInInventory = FindInventorySlot(player, card) >= 0;
        if (SwitchToItemSlotMethod == null)
        {
            Plugin.Log.LogError(
                "SHIP CARD DISPLAY INVENTORY ATTACH FAILED | " +
                "PlayerControllerB.SwitchToItemSlot was not found");
            return false;
        }

        try
        {
            SwitchToItemSlotMethod.Invoke(
                player,
                new object[] { inventorySlot, card });
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY INVENTORY ATTACH FAILED | {exception}");
            return false;
        }
        card.EnablePhysics(false);
        card.isHeld = true;
        card.heldByPlayerOnServer = true;
        card.hasHitGround = false;
        card.isInFactory = player.isInsideFactory;
        card.isPocketed = false;
        card.deactivated = false;
        card.isHeldByEnemy = false;
        card.playerHeldBy = player;
        card.parentObject = player.IsOwner
            ? player.localItemHolder
            : player.serverItemHolder;
        player.currentlyHeldObjectServer = card;
        if (player.IsOwner)
        {
            player.currentlyHeldObject = card;
            card.GrabItemOnClient();
        }
        player.isHoldingObject = true;
        player.twoHanded = card.itemProperties.twoHanded;
        player.twoHandedAnimation = card.itemProperties.twoHandedAnimation;
        player.SetItemInElevator(
            player.isInHangarShipRoom,
            player.isInElevator,
            card);

        if (!alreadyInInventory)
        {
            player.carryWeight = Mathf.Clamp(
                player.carryWeight + Mathf.Clamp(card.itemProperties.weight - 1f, 0f, 10f),
                1f,
                10f);
            if (player.IsOwner && StartOfRound.Instance != null)
                StartOfRound.Instance.SendChangedWeightEvent();
        }

        return true;
    }

    private void RestoreIncomingAfterFailure(
        PlayerControllerB player,
        int inventorySlot,
        ShipCardDisplayStoredCard incoming)
    {
        if (!TrySpawnIntoInventory(player, inventorySlot, incoming, out _))
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY ROLLBACK FAILED | CardId={incoming.CardId} | " +
                $"Player={player.playerUsername} | InventorySlot={inventorySlot}");
        }
    }

    private static bool CanReconstruct(ShipCardDisplayStoredCard storedCard)
    {
        CardDefinition? definition = CardDatabase.GetById(storedCard.CardId);
        return definition?.ItemAsset?.spawnPrefab != null;
    }

    private bool TryBeginLocalInteraction()
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextLocalInteractionTime)
            return false;
        if (IsLocalInteractionPending())
            return false;

        localInteractionPending = true;
        localInteractionPendingUntil = now + LocalRequestTimeout;
        return true;
    }

    internal bool IsLocalInteractionPending()
    {
        if (!localInteractionPending)
            return false;

        if (Time.realtimeSinceStartup < localInteractionPendingUntil)
            return true;

        localInteractionPending = false;
        return false;
    }

    private static bool IsHeldCardAuthoritativelyReady(
        PlayerControllerB player,
        NetworkObjectReference requestedCard)
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null ||
            !requestedCard.TryGet(out NetworkObject networkObject, manager))
        {
            return false;
        }

        GrabbableObject? card = networkObject.GetComponent<GrabbableObject>();
        CardInstanceData? cardData = networkObject.GetComponent<CardInstanceData>();
        return card != null && cardData != null &&
               networkObject.GetComponent<NetworkItemConsumption>() != null &&
               CardDatabase.GetById(cardData.CardId) != null &&
               player.ItemSlots != null &&
               player.currentItemSlot >= 0 &&
               player.currentItemSlot < player.ItemSlots.Length &&
               player.ItemSlots[player.currentItemSlot] == card &&
               NetworkItemConsumption.IsValidHolder(player, card);
    }

    private static bool IsEmptySelectedSlotAuthoritativelyReady(
        PlayerControllerB player) =>
        player != null && player.ItemSlots != null &&
        player.currentItemSlot >= 0 &&
        player.currentItemSlot < player.ItemSlots.Length &&
        player.ItemSlots[player.currentItemSlot] == null &&
        player.currentlyHeldObjectServer == null;

    private static string BuildHeldCardDiagnostic(
        PlayerControllerB player,
        NetworkObjectReference requestedCard,
        NetworkObject cardNetworkObject,
        GrabbableObject? card,
        CardInstanceData? cardData,
        NetworkItemConsumption? consumption,
        CardDefinition? definition)
    {
        ulong expectedOwner = player.actualClientId;
        string currentHeldId = player.currentlyHeldObjectServer != null &&
                               player.currentlyHeldObjectServer.IsSpawned
            ? player.currentlyHeldObjectServer.NetworkObjectId.ToString()
            : "none";
        string selectedObjectId = player.ItemSlots != null &&
                                  player.currentItemSlot >= 0 &&
                                  player.currentItemSlot < player.ItemSlots.Length &&
                                  player.ItemSlots[player.currentItemSlot] != null &&
                                  player.ItemSlots[player.currentItemSlot].IsSpawned
            ? player.ItemSlots[player.currentItemSlot].NetworkObjectId.ToString()
            : "none";
        int inventorySlot = card == null ? -1 : FindInventorySlot(player, card);
        GradingReturnPedestalLock? pedestalLock =
            cardNetworkObject.GetComponent<GradingReturnPedestalLock>();
        return
            $"RequestedObject={requestedCard.NetworkObjectId} | " +
            $"ResolvedObject={cardNetworkObject.NetworkObjectId} | " +
            $"Grabbable={card != null} | CardData={cardData != null} | " +
            $"Consumption={consumption != null} | Definition={definition?.CardId ?? "none"} | " +
            $"Spawned={card?.IsSpawned == true} | HeldByServer={card?.heldByPlayerOnServer == true} | " +
            $"Owner={(card == null ? "none" : card.OwnerClientId.ToString())} | ExpectedOwner={expectedOwner} | " +
            $"CurrentHeld={currentHeldId} | CurrentMatches={player.currentlyHeldObjectServer == card} | " +
            $"InventorySlot={inventorySlot} | CurrentItemSlot={player.currentItemSlot} | " +
            $"SelectedObject={selectedObjectId} | PlayerHolding={player.isHoldingObject} | " +
            $"Grade={cardData?.Grade.ToString() ?? "unknown"} | " +
            $"PedestalLock={pedestalLock != null && pedestalLock.enabled}";
    }

    private static void LogDiagnosticRateLimited(string key, string message)
    {
        float now = Time.realtimeSinceStartup;
        if (LastDiagnosticTimes.TryGetValue(key, out float lastTime) &&
            now - lastTime < DiagnosticRateLimit)
        {
            return;
        }

        if (LastDiagnosticTimes.Count > 128)
            LastDiagnosticTimes.Clear();
        LastDiagnosticTimes[key] = now;
        Plugin.Log.LogWarning(message);
    }

    private bool ValidatePlayerAndDistance(PlayerControllerB player)
    {
        if (player == null || !player.isPlayerControlled || player.isPlayerDead ||
            player.gameplayCamera == null)
        {
            return false;
        }

        Collider? interactionCollider = FindInteractionCollider();
        Vector3 target = interactionCollider != null
            ? interactionCollider.ClosestPoint(player.gameplayCamera.transform.position)
            : transform.position;
        float maximumDistance = player.grabDistance + InteractionDistancePadding;
        Vector3 cameraToTarget = target - player.gameplayCamera.transform.position;
        if (cameraToTarget.magnitude > maximumDistance ||
            cameraToTarget.sqrMagnitude < 0.001f ||
            Vector3.Dot(
                player.gameplayCamera.transform.forward,
                cameraToTarget.normalized) < 0.75f)
        {
            return false;
        }

        return !Physics.Linecast(
            player.gameplayCamera.transform.position,
            target,
            1 << 30,
            QueryTriggerInteraction.Ignore);
    }

    private bool ValidateRequestedSlotAim(PlayerControllerB player, int slotIndex)
    {
        if (player.gameplayCamera == null)
            return false;

        Ray cameraRay = new(
            player.gameplayCamera.transform.position,
            player.gameplayCamera.transform.forward);
        return TrySelectSlotFromRay(cameraRay, player.grabDistance, out int selectedSlot) &&
               selectedSlot == slotIndex;
    }

    internal bool TrySelectSlotFromRay(
        Ray worldRay,
        float maximumDistance,
        out int slotIndex)
    {
        slotIndex = -1;
        Vector3 localOrigin = transform.InverseTransformPoint(worldRay.origin);
        Vector3 localDirection = transform.InverseTransformDirection(worldRay.direction).normalized;
        float localScale = Mathf.Max(
            0.0001f,
            Mathf.Max(
                Mathf.Abs(transform.lossyScale.x),
                Mathf.Max(Mathf.Abs(transform.lossyScale.y), Mathf.Abs(transform.lossyScale.z))));
        float localMaximumDistance = maximumDistance / localScale;
        float selectionRadius = GetLocalSlotSelectionRadius();
        float bestPerpendicularDistance = float.MaxValue;

        for (int index = 0; index < slotAnchors.Length; index++)
        {
            Transform? anchor = slotAnchors[index];
            if (anchor == null)
                continue;

            Vector3 localAnchor = transform.InverseTransformPoint(anchor.position);
            Vector3 originToAnchor = localAnchor - localOrigin;
            float rayDistance = Vector3.Dot(originToAnchor, localDirection);
            if (rayDistance <= 0f || rayDistance > localMaximumDistance)
                continue;

            Vector3 closestPoint = localOrigin + localDirection * rayDistance;
            float perpendicularDistance = Vector3.Distance(localAnchor, closestPoint);
            if (perpendicularDistance > selectionRadius ||
                perpendicularDistance >= bestPerpendicularDistance)
            {
                continue;
            }

            bestPerpendicularDistance = perpendicularDistance;
            slotIndex = index;
        }

        return slotIndex >= 0;
    }

    private float GetLocalSlotSelectionRadius()
    {
        float nearestSpacing = float.MaxValue;
        for (int left = 0; left < slotAnchors.Length; left++)
        {
            if (slotAnchors[left] == null)
                continue;
            Vector3 leftPosition = transform.InverseTransformPoint(slotAnchors[left]!.position);
            for (int right = left + 1; right < slotAnchors.Length; right++)
            {
                if (slotAnchors[right] == null)
                    continue;
                Vector3 rightPosition = transform.InverseTransformPoint(slotAnchors[right]!.position);
                float spacing = Vector3.Distance(leftPosition, rightPosition);
                if (spacing > 0.001f && spacing < nearestSpacing)
                    nearestSpacing = spacing;
            }
        }

        return nearestSpacing < float.MaxValue
            ? Mathf.Clamp(nearestSpacing * 0.46f, 0.045f, 0.16f)
            : 0.08f;
    }

    private Collider? FindInteractionCollider()
    {
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            if (collider.name == ShipCardDisplaySpawner.InteractionColliderName)
                return collider;
        return null;
    }

    private void RefreshAggregateScanNode()
    {
        if (aggregateScanNode == null)
        {
            foreach (ScanNodeProperties scanNode in
                     GetComponentsInChildren<ScanNodeProperties>(true))
            {
                if (scanNode.name == ShipCardDisplaySpawner.AggregateScanNodeName)
                {
                    aggregateScanNode = scanNode;
                    break;
                }
            }
        }

        if (aggregateScanNode == null)
            return;

        int occupiedSlots = 0;
        long totalValue = 0;
        for (int slotIndex = 0; slotIndex < networkSlots.Count; slotIndex++)
        {
            ShipCardDisplaySlotState state = networkSlots[slotIndex];
            if (!state.Occupied)
                continue;
            occupiedSlots++;
            totalValue += Math.Max(0, state.FinalValue);
        }

        int safeTotal = (int)Math.Min(int.MaxValue, totalValue);
        aggregateScanNode.headerText = "Card Display Case";
        aggregateScanNode.subText =
            $"{occupiedSlots}/{ShipCardDisplaySaveManager.SlotCount} cards | Value: ${safeTotal}";
        aggregateScanNode.scrapValue = safeTotal;
    }

    private static int FindInventorySlot(
        PlayerControllerB player,
        GrabbableObject card)
    {
        for (int slot = 0; slot < player.ItemSlots.Length; slot++)
            if (player.ItemSlots[slot] == card)
                return slot;
        return -1;
    }

    private static bool TryResolvePlayer(
        ulong clientId,
        out PlayerControllerB player)
    {
        player = null!;
        StartOfRound? round = StartOfRound.Instance;
        if (round?.allPlayerScripts == null)
            return false;

        foreach (PlayerControllerB candidate in round.allPlayerScripts)
        {
            if (candidate != null && candidate.actualClientId == clientId)
            {
                player = candidate;
                return true;
            }
        }

        return false;
    }

    private void ApplyNetworkSlot(
        int slotIndex,
        ShipCardDisplayStoredCard? storedCard)
    {
        networkSlots[slotIndex] =
            ShipCardDisplaySlotState.FromStored(storedCard);
        RaiseAuthoritativeSlotChanged(slotIndex, storedCard);
        RefreshAggregateScanNode();
    }

    private static void RaiseAuthoritativeSlotChanged(
        int slotIndex,
        ShipCardDisplayStoredCard? storedCard)
    {
        AuthoritativeSlotChanged?.Invoke(
            slotIndex,
            storedCard?.CardId ?? string.Empty,
            storedCard?.Variant ?? CardVariant.Standard,
            storedCard?.Grade ?? 0,
            storedCard?.UngradedValue ?? 0,
            storedCard?.FinalValue ?? 0,
            storedCard != null);
    }

    internal static void ResetCrewWipeGuard() =>
        crewWipeHandledThisRound = false;

    internal static void ClearAuthoritativeStorageForCrewWipe()
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (crewWipeHandledThisRound || manager == null || !manager.IsServer)
            return;

        bool saved = ShipCardDisplaySaveManager.TryClearAll(
            out int savedBefore,
            out int savedAfter);
        if (!saved)
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY WIPE | Server=True | Before={savedBefore} | " +
                $"After={savedAfter} | Controllers=Unknown | Spawned=Unknown | " +
                "Visuals=Unknown | ScanValue=Unknown | Saved=False | " +
                "Reason=PersistenceWriteFailed");
            return;
        }

        crewWipeHandledThisRound = true;

        int controllerCount = 0;
        int runtimeBefore = 0;
        int runtimeAfter = 0;
        int remainingVisuals = 0;
        int scanValue = 0;
        int networkEntries = 0;
        bool allSpawned = true;
        bool hostVisualsRefreshed = true;
        SpawnedControllers.RemoveWhere(candidate => candidate == null);
        List<ShipCardDisplayController> controllers = new(SpawnedControllers);
        foreach (ShipCardDisplayController controller in controllers)
        {
            if (controller == null)
                continue;

            if (!controller.IsServer)
                continue;

            allSpawned &= controller.IsSpawned;
            if (!controller.IsSpawned)
                continue;

            controllerCount++;
            controller.ClearRuntimeStorageForCrewWipe(
                out int controllerBefore,
                out int controllerAfter,
                out int controllerVisuals,
                out int controllerScanValue,
                out int controllerEntries,
                out bool controllerVisualsRefreshed);
            runtimeBefore += controllerBefore;
            runtimeAfter += controllerAfter;
            remainingVisuals += controllerVisuals;
            scanValue += controllerScanValue;
            networkEntries += controllerEntries;
            hostVisualsRefreshed &= controllerVisualsRefreshed;
        }

        if (controllerCount == 0)
        {
            Plugin.Log.LogWarning(
                $"SHIP CARD DISPLAY WIPE | Server=True | Before={savedBefore} | " +
                $"After={savedAfter} | Controllers=0 | Spawned=False | " +
                "Visuals=Deferred | ScanValue=Deferred | Saved=True | " +
                "HostVisualsRefreshed=False | RuntimeClear=DeferredUntilNetworkSpawn");
            return;
        }

        bool runtimeCleared = runtimeAfter == 0 && remainingVisuals == 0 &&
                              scanValue == 0 &&
                              networkEntries == controllerCount * ShipCardDisplaySaveManager.SlotCount;
        string diagnostic =
            $"SHIP CARD DISPLAY WIPE | Server=True | Before={runtimeBefore} | " +
            $"After={runtimeAfter} | Controllers={controllerCount} | Spawned={allSpawned} | " +
            $"Entries={networkEntries} | Visuals={remainingVisuals} | ScanValue={scanValue} | " +
            $"Saved={saved} | SavedBefore={savedBefore} | SavedAfter={savedAfter} | " +
            $"HostVisualsRefreshed={hostVisualsRefreshed}";
        if (runtimeCleared)
        {
            Plugin.Log.LogAlways(
                $"SHIP CARD DISPLAY CLEARED | Reason=CrewWipe | CardsRemoved={runtimeBefore}");
            Plugin.Log.LogInfo(diagnostic);
        }
        else
            Plugin.Log.LogError(diagnostic + " | Reason=RuntimeStateNotEmpty");
    }

    private void ClearRuntimeStorageForCrewWipe(
        out int occupiedBefore,
        out int occupiedAfter,
        out int visualCount,
        out int scanValue,
        out int networkEntries,
        out bool visualsRefreshed)
    {
        occupiedBefore = CountOccupiedNetworkSlots();
        for (int slotIndex = 0;
             slotIndex < ShipCardDisplaySaveManager.SlotCount &&
             slotIndex < networkSlots.Count;
             slotIndex++)
        {
            if (!networkSlots[slotIndex].Occupied)
                continue;

            networkSlots[slotIndex] = ShipCardDisplaySlotState.Empty;
            RaiseAuthoritativeSlotChanged(slotIndex, null);
        }

        // Host NetworkList callbacks normally refresh each changed slot. Refresh
        // explicitly as well because wipe teardown can disable the furniture
        // hierarchy while the authoritative object remains network-spawned.
        RefreshAllVisuals();
        RefreshAggregateScanNode();
        visualsRefreshed = true;
        occupiedAfter = CountOccupiedNetworkSlots();
        visualCount = CountVisuals();
        scanValue = aggregateScanNode == null ? -1 : aggregateScanNode.scrapValue;
        networkEntries = networkSlots.Count;
    }

    private int CountOccupiedNetworkSlots()
    {
        int occupied = 0;
        for (int slotIndex = 0; slotIndex < networkSlots.Count; slotIndex++)
            if (networkSlots[slotIndex].Occupied)
                occupied++;
        return occupied;
    }

    private int CountVisuals()
    {
        int count = 0;
        foreach (GameObject? visual in slotVisuals)
            if (visual != null)
                count++;
        return count;
    }

    private void Reject(ulong clientId, string reason, string details = "")
    {
        LogDiagnosticRateLimited(
            $"{clientId}:{reason}",
            $"SHIP CARD DISPLAY INTERACTION REJECTED | Cabinet={NetworkObjectId} | " +
            $"Client={clientId} | Reason={reason}" +
            (string.IsNullOrEmpty(details) ? string.Empty : $" | {details}"));
        SendResult(clientId, InteractionResult.Failed);
    }

    private void SendResult(ulong clientId, InteractionResult result)
    {
        InteractionResultClientRpc(
            (int)result,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });
    }

    [ClientRpc]
    private void InteractionResultClientRpc(
        int resultValue,
        ClientRpcParams clientRpcParams = default)
    {
        localInteractionPending = false;
        nextLocalInteractionTime = Time.realtimeSinceStartup + InteractionDebounce;
        if (!Enum.IsDefined(typeof(InteractionResult), resultValue) ||
            (InteractionResult)resultValue == InteractionResult.Success ||
            HUDManager.Instance == null)
        {
            return;
        }

        string message = (InteractionResult)resultValue switch
        {
            InteractionResult.AlreadyEmpty => "That display slot is already empty.",
            InteractionResult.InventoryFull => "Select an empty inventory slot first.",
            InteractionResult.Busy => "The display case is handling another card.",
            _ => "The card transaction could not be completed."
        };
        HUDManager.Instance.DisplayTip(
            "Ship Card Display",
            message,
            false,
            false,
            "LC_Tip1");
    }

    private void CacheSlotAnchors()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.StartsWith("Slot", StringComparison.OrdinalIgnoreCase) ||
                child.name.Length != 6 ||
                !int.TryParse(child.name.Substring(4), out int slotNumber) ||
                slotNumber < 1 || slotNumber > slotAnchors.Length)
            {
                continue;
            }

            slotAnchors[slotNumber - 1] = child;
        }
    }

    private void OnSlotListChanged(
        NetworkListEvent<ShipCardDisplaySlotState> changeEvent)
    {
        if (changeEvent.Index >= 0 && changeEvent.Index < slotAnchors.Length)
            RefreshSlotVisual(changeEvent.Index);
        else
            RefreshAllVisuals();
        RefreshAggregateScanNode();
    }

    private void RefreshAllVisuals()
    {
        for (int slotIndex = 0; slotIndex < slotAnchors.Length; slotIndex++)
            RefreshSlotVisual(slotIndex);
    }

    private void RefreshSlotVisual(int slotIndex)
    {
        GameObject? oldVisual = slotVisuals[slotIndex];
        if (oldVisual != null)
        {
            oldVisual.SetActive(false);
            Destroy(oldVisual);
            slotVisuals[slotIndex] = null;
        }

        Transform? anchor = slotAnchors[slotIndex];
        if (anchor == null || slotIndex >= networkSlots.Count)
            return;

        ShipCardDisplayStoredCard? storedCard =
            networkSlots[slotIndex].ToStored();
        if (storedCard == null)
            return;

        CardDefinition? definition = CardDatabase.GetById(storedCard.CardId);
        if (definition == null || definition.SetNumber != slotIndex + 1)
        {
            Plugin.Log.LogWarning(
                $"SHIP CARD DISPLAY VISUAL REJECTED | Slot={slotIndex + 1} | " +
                $"CardId={storedCard.CardId} | Reason=Set-number mismatch");
            return;
        }

        slotVisuals[slotIndex] =
            ShipCardDisplayVisualFactory.Create(anchor, storedCard);
    }

    private void ClearVisuals()
    {
        for (int slotIndex = 0; slotIndex < slotVisuals.Length; slotIndex++)
        {
            if (slotVisuals[slotIndex] != null)
                Destroy(slotVisuals[slotIndex]);
            slotVisuals[slotIndex] = null;
        }
    }

    private enum InteractionResult
    {
        Success,
        AlreadyEmpty,
        InventoryFull,
        Busy,
        Failed
    }
}
