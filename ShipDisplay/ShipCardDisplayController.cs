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
    private const float AttachmentResponseTimeout = 2.5f;
    private const int MaximumAttachmentAttempts = 2;
    private const float HeldStateSettleTimeout = 0.6f;
    private const float LocalRequestTimeout = 7f;
    private const float InteractionDebounce = 0.12f;
    private const float DiagnosticRateLimit = 2f;
    private const float MaximumSnapshotOriginOffset = 1.25f;
    private const int InteractionCollisionMask = ~0;
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
    private readonly Dictionary<ulong, PendingAttachment> pendingAttachments = new();
    private readonly HashSet<ulong> activeAttachmentCoroutines = new();
    private readonly HashSet<ulong> terminalTransactions = new();

    private bool transactionInProgress;
    private ulong activeTransactionId;
    private ulong activeTransactionClientId;
    private bool localInteractionPending;
    private float localInteractionPendingUntil;
    private float nextLocalInteractionTime;
    private ScanNodeProperties? aggregateScanNode;
    private ulong nextTransactionId = 1;
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
        if (activeTransactionId != 0)
        {
            SendTerminalResult(
                activeTransactionClientId,
                activeTransactionId,
                InteractionResult.Failed,
                "Canceled",
                "Reason=ControllerDespawned",
                false);
        }
        foreach (PendingAttachment pending in pendingAttachments.Values)
        {
            SendTerminalResult(
                pending.ClientId,
                pending.TransactionId,
                InteractionResult.AttachmentFailed,
                "Canceled",
                "Reason=ControllerDespawnedDuringAttachment",
                false);
        }
        pendingAttachments.Clear();
        activeAttachmentCoroutines.Clear();
        localInteractionPending = false;
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

    internal void RequestDeposit(PlayerControllerB player, Ray interactionRay)
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
            new NetworkObjectReference(cardNetworkObject),
            transform.InverseTransformPoint(interactionRay.origin),
            transform.InverseTransformDirection(interactionRay.direction).normalized);
    }

    internal void RequestWithdrawal(
        PlayerControllerB player,
        int slotIndex,
        Ray interactionRay)
    {
        if (!IsSpawned || player == null || !TryBeginLocalInteraction())
            return;

        InteractServerRpc(
            false,
            slotIndex,
            default,
            transform.InverseTransformPoint(interactionRay.origin),
            transform.InverseTransformDirection(interactionRay.direction).normalized);
    }

    [ServerRpc(RequireOwnership = false)]
    private void InteractServerRpc(
        bool isDeposit,
        int requestedSlot,
        NetworkObjectReference requestedCard,
        Vector3 localRayOrigin,
        Vector3 localRayDirection,
        ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        ulong transactionId = nextTransactionId++;
        LogTransaction(
            transactionId,
            senderClientId,
            isDeposit ? "Deposit" : "Withdraw",
            "Received",
            $"RequestedDisplaySlot={requestedSlot + 1} | " +
            $"RequestedObject={requestedCard.NetworkObjectId} | " +
            $"LocalRayOrigin={localRayOrigin} | LocalRayDirection={localRayDirection}");
        if (transactionInProgress || !IsSpawned)
        {
            SendTerminalResult(
                senderClientId,
                transactionId,
                InteractionResult.Busy,
                "Rejected",
                transactionInProgress ? "Reason=Busy" : "Reason=ControllerNotSpawned");
            return;
        }

        transactionInProgress = true;
        activeTransactionId = transactionId;
        activeTransactionClientId = senderClientId;
        StartCoroutine(ProcessInteraction(
            senderClientId,
            transactionId,
            isDeposit,
            requestedSlot,
            requestedCard,
            localRayOrigin,
            localRayDirection));
    }

    private IEnumerator ProcessInteraction(
        ulong senderClientId,
        ulong transactionId,
        bool isDeposit,
        int requestedSlot,
        NetworkObjectReference requestedCard,
        Vector3 localRayOrigin,
        Vector3 localRayDirection)
    {
        try
        {
            if (!TryResolvePlayer(senderClientId, out PlayerControllerB player))
            {
                Reject(senderClientId, transactionId, "PlayerNotFound");
                yield break;
            }

            LogTransaction(
                transactionId,
                senderClientId,
                isDeposit ? "Deposit" : "Withdraw",
                "PlayerResolved",
                $"SelectedInventorySlot={player.currentItemSlot} | " +
                $"ServerHeld={(player.currentlyHeldObjectServer == null ? "none" : player.currentlyHeldObjectServer.NetworkObjectId.ToString())}");

            if (!ValidatePlayerState(player, out string playerFailure))
            {
                Reject(senderClientId, transactionId, playerFailure);
                yield break;
            }

            if (!TryValidateInteractionSnapshot(
                    player,
                    localRayOrigin,
                    localRayDirection,
                    transactionId,
                    out int snapshotSlot,
                    out string snapshotFailure,
                    out string snapshotDetails))
            {
                Reject(
                    senderClientId,
                    transactionId,
                    snapshotFailure,
                    snapshotDetails);
                yield break;
            }

            if (!isDeposit && snapshotSlot != requestedSlot)
            {
                Reject(
                    senderClientId,
                    transactionId,
                    "InvalidSlotTarget",
                    $"RequestedSlot={requestedSlot + 1} | SnapshotSlot={snapshotSlot + 1}");
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

                if (!IsHeldCardAuthoritativelyReady(player, requestedCard))
                {
                    if (HasRequestedCardSelectionChanged(player, requestedCard))
                    {
                        CancelChangedInteraction(
                            senderClientId,
                            transactionId,
                            "RequestedHeldCardNoLongerSelected");
                    }
                    else
                    {
                        RejectRetryable(
                            senderClientId,
                            transactionId,
                            "HeldCardSynchronizationTimeout");
                    }
                    yield break;
                }

                if (!ValidateCurrentPlayerAccess(
                        player,
                        transactionId,
                        out string accessFailure,
                        out string accessDetails))
                {
                    Reject(
                        senderClientId,
                        transactionId,
                        accessFailure,
                        accessDetails);
                    yield break;
                }

                TryDeposit(player, requestedCard, senderClientId, transactionId);
            }
            else
            {
                float deadline = Time.realtimeSinceStartup + HeldStateSettleTimeout;
                while (!IsEmptySelectedSlotAuthoritativelyReady(player) &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (!IsEmptySelectedSlotAuthoritativelyReady(player))
                {
                    if (HasSettledNonEmptySelection(player))
                    {
                        CancelChangedInteraction(
                            senderClientId,
                            transactionId,
                            "SelectedInventorySlotNoLongerEmpty");
                    }
                    else
                    {
                        RejectRetryable(
                            senderClientId,
                            transactionId,
                            "InventorySynchronizationTimeout");
                    }
                    yield break;
                }

                if (!ValidateCurrentPlayerAccess(
                        player,
                        transactionId,
                        out string accessFailure,
                        out string accessDetails))
                {
                    Reject(
                        senderClientId,
                        transactionId,
                        accessFailure,
                        accessDetails);
                    yield break;
                }

                TryWithdraw(player, requestedSlot, senderClientId, transactionId);
            }
        }
        finally
        {
            transactionInProgress = false;
            if (activeTransactionId == transactionId)
            {
                activeTransactionId = 0;
                activeTransactionClientId = 0;
            }
            if (!terminalTransactions.Contains(transactionId) &&
                !pendingAttachments.ContainsKey(transactionId))
            {
                Plugin.Log.LogError(
                    $"SHIP CARD DISPLAY TRANSACTION ENDED WITHOUT RESULT | " +
                    $"Transaction={transactionId} | Client={senderClientId}");
                SendTerminalResult(
                    senderClientId,
                    transactionId,
                    InteractionResult.Failed,
                    "Failed",
                    "Reason=UnexpectedCoroutineTermination");
            }
        }
    }

    private void TryDeposit(
        PlayerControllerB player,
        NetworkObjectReference requestedCard,
        ulong senderClientId,
        ulong transactionId)
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null ||
            !requestedCard.TryGet(out NetworkObject cardNetworkObject, manager))
        {
            Reject(senderClientId, transactionId, "InvalidCardReference");
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
                transactionId,
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
                transactionId,
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
            Reject(senderClientId, transactionId, "StoredCardPrefabUnavailable");
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
            Reject(senderClientId, transactionId, "CardConsumptionFailed");
            return;
        }

        if (!ShipCardDisplaySaveManager.TryCommitSlot(displaySlot, incoming))
        {
            RestoreIncomingAfterFailure(player, inventorySlot, incoming);
            Reject(senderClientId, transactionId, "PersistenceFailed");
            return;
        }

        bool awaitingOwnerAttachment = false;
        NetworkObject? reconstructedObject = null;
        if (outgoing != null &&
            !TrySpawnIntoInventory(
                player,
                inventorySlot,
                outgoing,
                transactionId,
                "Swap",
                true,
                out reconstructedObject,
                out awaitingOwnerAttachment))
        {
            bool rollbackSaved =
                ShipCardDisplaySaveManager.TryCommitSlot(displaySlot, outgoing);
            RestoreIncomingAfterFailure(player, inventorySlot, incoming);
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY SWAP ROLLED BACK | Slot={displaySlot + 1} | " +
                $"PersistenceRollback={rollbackSaved}");
            Reject(senderClientId, transactionId, "CardReconstructionFailed");
            return;
        }

        ApplyNetworkSlot(displaySlot, incoming);
        LogTransaction(
            transactionId,
            senderClientId,
            outgoing == null ? "Deposit" : "Swap",
            "Committed",
            $"DisplaySlot={displaySlot + 1} | IncomingObject={cardNetworkObject.NetworkObjectId} | " +
            $"OutgoingObject={(reconstructedObject == null ? "none" : reconstructedObject.NetworkObjectId.ToString())} | " +
            $"AwaitingOwnerAttachment={awaitingOwnerAttachment}");
        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY {(outgoing == null ? "DEPOSIT" : "SWAP")} | " +
            $"Player={player.playerUsername} | Slot={displaySlot + 1} | " +
            $"Incoming={incoming.CardId}/{incoming.Variant}/Grade{incoming.Grade}/Value{incoming.FinalValue}" +
            (outgoing == null
                ? string.Empty
                : $" | Outgoing={outgoing.CardId}/{outgoing.Variant}/Grade{outgoing.Grade}/Value{outgoing.FinalValue}"));
        if (!awaitingOwnerAttachment)
            CompleteTransaction(senderClientId, transactionId);
    }

    private void TryWithdraw(
        PlayerControllerB player,
        int requestedSlot,
        ulong senderClientId,
        ulong transactionId)
    {
        if (requestedSlot < 0 ||
            requestedSlot >= ShipCardDisplaySaveManager.SlotCount ||
            requestedSlot >= networkSlots.Count)
        {
            Reject(senderClientId, transactionId, "InvalidSlotTarget");
            return;
        }

        ShipCardDisplayStoredCard? storedCard =
            networkSlots[requestedSlot].ToStored();
        if (storedCard == null)
        {
            SendTerminalResult(
                senderClientId,
                transactionId,
                InteractionResult.AlreadyEmpty,
                "Rejected",
                "Reason=AlreadyEmpty");
            return;
        }

        if (player.currentlyHeldObjectServer != null ||
            player.currentItemSlot < 0 ||
            player.currentItemSlot >= player.ItemSlots.Length ||
            player.ItemSlots[player.currentItemSlot] != null)
        {
            SendTerminalResult(
                senderClientId,
                transactionId,
                InteractionResult.InventoryFull,
                "Rejected",
                "Reason=InventoryFull");
            return;
        }

        if (!CanReconstruct(storedCard))
        {
            Reject(senderClientId, transactionId, "StoredCardPrefabUnavailable");
            return;
        }

        int inventorySlot = player.currentItemSlot;
        if (!ShipCardDisplaySaveManager.TryCommitSlot(requestedSlot, null))
        {
            Reject(senderClientId, transactionId, "PersistenceFailed");
            return;
        }

        if (!TrySpawnIntoInventory(
                player,
                inventorySlot,
                storedCard,
                transactionId,
                "Withdraw",
                true,
                out NetworkObject? reconstructedObject,
                out bool awaitingOwnerAttachment))
        {
            bool rollbackSaved =
                ShipCardDisplaySaveManager.TryCommitSlot(requestedSlot, storedCard);
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY WITHDRAW ROLLED BACK | Slot={requestedSlot + 1} | " +
                $"PersistenceRollback={rollbackSaved}");
            Reject(senderClientId, transactionId, "CardReconstructionFailed");
            return;
        }

        ApplyNetworkSlot(requestedSlot, null);
        LogTransaction(
            transactionId,
            senderClientId,
            "Withdraw",
            "Committed",
            $"DisplaySlot={requestedSlot + 1} | " +
            $"OutgoingObject={reconstructedObject?.NetworkObjectId.ToString() ?? "none"} | " +
            $"AwaitingOwnerAttachment={awaitingOwnerAttachment}");
        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY WITHDRAW | Player={player.playerUsername} | " +
            $"Slot={requestedSlot + 1} | Card={storedCard.CardId}/{storedCard.Variant}/" +
            $"Grade{storedCard.Grade}/Value{storedCard.FinalValue}");
        if (!awaitingOwnerAttachment)
            CompleteTransaction(senderClientId, transactionId);
    }

    private bool TrySpawnIntoInventory(
        PlayerControllerB player,
        int inventorySlot,
        ShipCardDisplayStoredCard storedCard,
        ulong transactionId,
        string action,
        bool trackOwnerAttachment,
        out NetworkObject? spawnedNetworkObject,
        out bool awaitingOwnerAttachment)
    {
        spawnedNetworkObject = null;
        awaitingOwnerAttachment = false;
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
            networkObject.SpawnWithOwnership(player.actualClientId);
            if (!ApplyCardToInventory(player, inventorySlot, card))
            {
                networkObject.Despawn(true);
                return false;
            }

            spawnedNetworkObject = networkObject;
            bool remoteOwner = NetworkManager.Singleton != null &&
                               player.actualClientId != NetworkManager.ServerClientId;
            if (trackOwnerAttachment && remoteOwner)
            {
                PendingAttachment pending = new(
                    transactionId,
                    player.actualClientId,
                    inventorySlot,
                    networkObject.NetworkObjectId,
                    action);
                pendingAttachments[transactionId] = pending;
                awaitingOwnerAttachment = true;
                SendAttachmentAttempt(pending);
            }
            else
            {
                AttachCardClientRpc(
                    transactionId,
                    player.actualClientId,
                    inventorySlot,
                    new NetworkObjectReference(networkObject));
            }
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
        ulong transactionId,
        ulong playerClientId,
        int inventorySlot,
        NetworkObjectReference cardReference)
    {
        // The server applied the state before sending this RPC. This also avoids
        // double-counting carry weight on a listen-server host.
        if (IsServer)
            return;

        if (transactionId != 0 &&
            !activeAttachmentCoroutines.Add(transactionId))
            return;

        StartCoroutine(AttachCardWhenReady(
            transactionId,
            playerClientId,
            inventorySlot,
            cardReference));
    }

    private IEnumerator AttachCardWhenReady(
        ulong transactionId,
        ulong playerClientId,
        int inventorySlot,
        NetworkObjectReference cardReference)
    {
        float deadline = Time.realtimeSinceStartup + AttachRetryTimeout;
        string terminalReason = "PrerequisitesNotReady";
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
                    bool applied = ApplyCardToInventory(player, inventorySlot, card);
                    bool postconditionsValid = applied &&
                        ValidateInventoryPostconditions(
                            player,
                            inventorySlot,
                            card,
                            out terminalReason);
                    if (postconditionsValid)
                    {
                        if (transactionId != 0)
                            activeAttachmentCoroutines.Remove(transactionId);
                        LogTransaction(
                            transactionId,
                            playerClientId,
                            "Reconstruction",
                            "OwnerAttached",
                            $"NetworkObject={networkObject.NetworkObjectId} | Slot={inventorySlot} | " +
                            $"Equipped={player.currentItemSlot == inventorySlot}");
                        if (player.IsOwner && transactionId != 0)
                            ConfirmAttachmentServerRpc(
                                transactionId,
                                networkObject.NetworkObjectId,
                                true);
                        yield break;
                    }

                    if (!applied)
                        terminalReason = "ApplyCardToInventoryReturnedFalse";
                }
                else if (!ownershipReady)
                    terminalReason = "OwnershipNotReady";
                else if (existing != null && existing != card)
                    terminalReason = "IntendedSlotOccupied";
            }

            yield return null;
        }

        if (transactionId != 0)
            activeAttachmentCoroutines.Remove(transactionId);
        bool ownerClient = TryResolvePlayer(
            playerClientId,
            out PlayerControllerB owner) && owner.IsOwner;
        string timeoutMessage =
            $"SHIP CARD DISPLAY INVENTORY ATTACH TIMED OUT | " +
            $"Transaction={transactionId} | PlayerClientId={playerClientId} | " +
            $"Slot={inventorySlot} | OwnerClient={ownerClient} | Reason={terminalReason}" +
            BuildAttachmentDiagnostic(playerClientId, inventorySlot, cardReference);
        if (ownerClient)
            Plugin.Log.LogWarning(timeoutMessage);
        else
            Plugin.Log.LogInfo(timeoutMessage);
        if (transactionId != 0 && ownerClient)
        {
            ConfirmAttachmentServerRpc(
                transactionId,
                cardReference.NetworkObjectId,
                false);
        }
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

        int existingInventorySlot = FindInventorySlot(player, card);
        bool alreadyInInventory = existingInventorySlot >= 0;
        if (SwitchToItemSlotMethod == null)
        {
            Plugin.Log.LogError(
                "SHIP CARD DISPLAY INVENTORY ATTACH FAILED | " +
                "PlayerControllerB.SwitchToItemSlot was not found");
            return false;
        }

        bool shouldEquip = player.currentItemSlot == inventorySlot;
        try
        {
            if (existingInventorySlot >= 0 && existingInventorySlot != inventorySlot)
            {
                player.ItemSlots[existingInventorySlot] = null;
                if (player.IsOwner && HUDManager.Instance != null &&
                    existingInventorySlot < HUDManager.Instance.itemSlotIcons.Length)
                {
                    HUDManager.Instance.itemSlotIcons[existingInventorySlot].enabled = false;
                }
            }

            if (shouldEquip)
            {
                SwitchToItemSlotMethod.Invoke(
                    player,
                    new object[] { inventorySlot, card });
            }
            else
            {
                player.ItemSlots[inventorySlot] = card;
                if (player.IsOwner && HUDManager.Instance != null &&
                    inventorySlot < HUDManager.Instance.itemSlotIcons.Length)
                {
                    HUDManager.Instance.itemSlotIcons[inventorySlot].sprite =
                        card.itemProperties.itemIcon;
                    HUDManager.Instance.itemSlotIcons[inventorySlot].enabled = true;
                }
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY INVENTORY ATTACH FAILED | {exception}");
            return false;
        }
        // Spawned reconstruction is attached before Unity invokes Start().
        // Initialize vanilla's cached scale and complete collider set first so
        // physical and ScanNode colliders can be disabled immediately.
        if (!CardPhysicalState.EnsureInstance(
                card,
                "ShipCardDisplay.ApplyCardToInventory"))
            return false;
        card.EnablePhysics(false);
        card.isHeld = true;
        card.heldByPlayerOnServer = true;
        card.hasHitGround = false;
        card.isInFactory = player.isInsideFactory;
        card.isPocketed = !shouldEquip;
        card.deactivated = false;
        card.isHeldByEnemy = false;
        card.playerHeldBy = player;
        card.parentObject = player.IsOwner
            ? player.localItemHolder
            : player.serverItemHolder;
        if (shouldEquip)
        {
            player.currentlyHeldObjectServer = card;
            if (player.IsOwner)
            {
                player.currentlyHeldObject = card;
                card.GrabItemOnClient();
            }
            card.EnableItemMeshes(true);
            player.isHoldingObject = true;
            player.twoHanded = card.itemProperties.twoHanded;
            player.twoHandedAnimation = card.itemProperties.twoHandedAnimation;
        }
        else
        {
            if (player.currentlyHeldObjectServer == card)
                player.currentlyHeldObjectServer = null;
            if (player.currentlyHeldObject == card)
                player.currentlyHeldObject = null;
            card.EnableItemMeshes(false);
        }
        player.SetItemInElevator(
            player.isInHangarShipRoom,
            player.isInElevator,
            card);
        card.GetComponent<CardInstanceData>()?
            .RefreshPhysicalScanNodeVisibility(true);

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

    private static bool ValidateInventoryPostconditions(
        PlayerControllerB player,
        int inventorySlot,
        GrabbableObject card,
        out string failure)
    {
        failure = string.Empty;
        if (player.ItemSlots[inventorySlot] != card)
        {
            failure = "InventorySlotMismatch";
            return false;
        }

        int matchingSlots = 0;
        foreach (GrabbableObject? item in player.ItemSlots)
            if (item == card)
                matchingSlots++;
        if (matchingSlots != 1)
        {
            failure = "DuplicateInventoryReference";
            return false;
        }

        if (card.playerHeldBy != player || card.parentObject == null)
        {
            failure = "HolderReferenceMismatch";
            return false;
        }

        Vector3 localScale = card.transform.localScale;
        Vector3 lossyScale = card.transform.lossyScale;
        if (!IsFinite(localScale) || !IsFinite(lossyScale) ||
            localScale.sqrMagnitude < 0.000001f ||
            lossyScale.sqrMagnitude < 0.000001f)
        {
            failure = "InvalidTransformScale";
            return false;
        }

        bool shouldBeEquipped = player.currentItemSlot == inventorySlot;
        if (shouldBeEquipped)
        {
            if (player.currentlyHeldObjectServer != card ||
                (player.IsOwner && player.currentlyHeldObject != card) ||
                !card.isHeld || card.isPocketed ||
                !HasVisiblePhysicalRenderer(card))
            {
                failure = "EquippedPresentationMismatch";
                return false;
            }
        }
        else if (!card.isPocketed ||
                 player.currentlyHeldObjectServer == card ||
                 player.currentlyHeldObject == card)
        {
            failure = "PocketedPresentationMismatch";
            return false;
        }

        return true;
    }

    private static bool HasVisiblePhysicalRenderer(GrabbableObject card)
    {
        foreach (Renderer renderer in card.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetComponentInParent<ScanNodeProperties>() != null)
                continue;
            if (renderer.gameObject.activeInHierarchy && renderer.enabled)
                return true;
        }

        return false;
    }

    private static string BuildAttachmentDiagnostic(
        ulong playerClientId,
        int inventorySlot,
        NetworkObjectReference cardReference)
    {
        if (!TryResolvePlayer(playerClientId, out PlayerControllerB player))
            return " | Player=Unresolved";

        string intendedSlot = inventorySlot >= 0 &&
                              inventorySlot < player.ItemSlots.Length &&
                              player.ItemSlots[inventorySlot] != null
            ? player.ItemSlots[inventorySlot].NetworkObjectId.ToString()
            : "none";
        string currentHeld = player.currentlyHeldObject != null
            ? player.currentlyHeldObject.NetworkObjectId.ToString()
            : "none";
        string serverHeld = player.currentlyHeldObjectServer != null
            ? player.currentlyHeldObjectServer.NetworkObjectId.ToString()
            : "none";
        string itemState = string.Empty;
        if (cardReference.TryGet(out NetworkObject networkObject) &&
            networkObject != null)
        {
            GrabbableObject? card = networkObject.GetComponent<GrabbableObject>();
            if (card != null)
            {
                int enabledRenderers = 0;
                int totalRenderers = 0;
                foreach (Renderer renderer in
                         card.GetComponentsInChildren<Renderer>(true))
                {
                    totalRenderers++;
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                        enabledRenderers++;
                }

                int enabledColliders = 0;
                int totalColliders = 0;
                foreach (Collider collider in
                         card.GetComponentsInChildren<Collider>(true))
                {
                    totalColliders++;
                    if (collider.enabled)
                        enabledColliders++;
                }

                itemState =
                    $" | Spawned={networkObject.IsSpawned} | Owner={networkObject.OwnerClientId} | " +
                    $"Held={card.isHeld} | Pocketed={card.isPocketed} | " +
                    $"Parent={card.parentObject?.name ?? "none"} | " +
                    $"TransformParent={card.transform.parent?.name ?? "none"} | " +
                    $"Renderers={enabledRenderers}/{totalRenderers} | " +
                    $"Colliders={enabledColliders}/{totalColliders} | " +
                    $"LocalScale={card.transform.localScale} | LossyScale={card.transform.lossyScale}";
            }
        }

        return $" | SelectedSlot={player.currentItemSlot} | " +
               $"IntendedSlotObject={intendedSlot} | CurrentHeld={currentHeld} | " +
               $"ServerHeld={serverHeld}{itemState}";
    }

    private void RestoreIncomingAfterFailure(
        PlayerControllerB player,
        int inventorySlot,
        ShipCardDisplayStoredCard incoming)
    {
        if (!TrySpawnIntoInventory(
                player,
                inventorySlot,
                incoming,
                0,
                "RollbackRestore",
                false,
                out _,
                out _))
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY ROLLBACK FAILED | CardId={incoming.CardId} | " +
                $"Player={player.playerUsername} | InventorySlot={inventorySlot}");
        }
    }

    private void Update()
    {
        if (!IsServer || pendingAttachments.Count == 0)
            return;

        float now = Time.realtimeSinceStartup;
        List<ulong>? expired = null;
        foreach (KeyValuePair<ulong, PendingAttachment> entry in pendingAttachments)
        {
            if (now < entry.Value.ResponseDeadline)
                continue;
            expired ??= new List<ulong>();
            expired.Add(entry.Key);
        }

        if (expired == null)
            return;

        foreach (ulong transactionId in expired)
            RetryOrFailAttachment(transactionId, "AcknowledgmentTimeout");
    }

    private void SendAttachmentAttempt(PendingAttachment pending)
    {
        if (!IsServer || !pendingAttachments.ContainsKey(pending.TransactionId) ||
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                pending.NetworkObjectId,
                out NetworkObject networkObject) ||
            networkObject == null || !networkObject.IsSpawned)
        {
            pending.Attempts = MaximumAttachmentAttempts;
            pendingAttachments[pending.TransactionId] = pending;
            RetryOrFailAttachment(pending.TransactionId, "ReconstructedObjectMissing");
            return;
        }

        pending.Attempts++;
        pending.ResponseDeadline =
            Time.realtimeSinceStartup + AttachmentResponseTimeout;
        pendingAttachments[pending.TransactionId] = pending;
        LogTransaction(
            pending.TransactionId,
            pending.ClientId,
            pending.Action,
            "AttachmentSent",
            $"Attempt={pending.Attempts} | NetworkObject={pending.NetworkObjectId} | " +
            $"Slot={pending.InventorySlot}");
        AttachCardClientRpc(
            pending.TransactionId,
            pending.ClientId,
            pending.InventorySlot,
            new NetworkObjectReference(networkObject));
    }

    [ServerRpc(RequireOwnership = false)]
    private void ConfirmAttachmentServerRpc(
        ulong transactionId,
        ulong networkObjectId,
        bool succeeded,
        ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (!pendingAttachments.TryGetValue(
                transactionId,
                out PendingAttachment pending) ||
            pending.ClientId != senderClientId ||
            pending.NetworkObjectId != networkObjectId)
        {
            LogTransaction(
                transactionId,
                senderClientId,
                "Reconstruction",
                "AcknowledgmentIgnored",
                $"NetworkObject={networkObjectId} | Reason=UnknownOrMismatchedTransaction");
            return;
        }

        string failure = "OwnerReportedFailure";
        if (!succeeded || !ValidateServerAttachment(pending, out failure))
        {
            RetryOrFailAttachment(
                transactionId,
                failure);
            return;
        }

        pendingAttachments.Remove(transactionId);
        LogTransaction(
            transactionId,
            senderClientId,
            pending.Action,
            "AttachmentConfirmed",
            $"NetworkObject={networkObjectId} | Slot={pending.InventorySlot}");
        CompleteTransaction(senderClientId, transactionId);
    }

    private bool ValidateServerAttachment(
        PendingAttachment pending,
        out string failure)
    {
        failure = string.Empty;
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(
                pending.NetworkObjectId,
                out NetworkObject networkObject) ||
            networkObject == null || !networkObject.IsSpawned)
        {
            failure = "ReconstructedObjectMissing";
            return false;
        }

        if (networkObject.OwnerClientId != pending.ClientId)
        {
            failure = "OwnerMismatch";
            return false;
        }

        if (!TryResolvePlayer(pending.ClientId, out PlayerControllerB player) ||
            pending.InventorySlot < 0 ||
            pending.InventorySlot >= player.ItemSlots.Length ||
            player.ItemSlots[pending.InventorySlot] !=
                networkObject.GetComponent<GrabbableObject>())
        {
            failure = "ServerInventorySlotMismatch";
            return false;
        }

        return true;
    }

    private void RetryOrFailAttachment(ulong transactionId, string reason)
    {
        if (!pendingAttachments.TryGetValue(
                transactionId,
                out PendingAttachment pending))
        {
            return;
        }

        NetworkManager? manager = NetworkManager.Singleton;
        bool clientConnected = manager != null &&
            manager.ConnectedClients.ContainsKey(pending.ClientId);
        if (clientConnected && pending.Attempts < MaximumAttachmentAttempts)
        {
            LogTransaction(
                transactionId,
                pending.ClientId,
                pending.Action,
                "AttachmentRetry",
                $"PreviousAttempt={pending.Attempts} | Reason={reason}");
            SendAttachmentAttempt(pending);
            return;
        }

        pendingAttachments.Remove(transactionId);
        Plugin.Log.LogError(
            $"SHIP CARD DISPLAY RECONCILIATION FAILED | Transaction={transactionId} | " +
            $"Client={pending.ClientId} | Action={pending.Action} | " +
            $"NetworkObject={pending.NetworkObjectId} | Slot={pending.InventorySlot} | " +
            $"Attempts={pending.Attempts} | ClientConnected={clientConnected} | Reason={reason}");
        SendTerminalResult(
            pending.ClientId,
            transactionId,
            InteractionResult.AttachmentFailed,
            "Failed",
            $"Reason={reason} | Attempts={pending.Attempts} | ClientConnected={clientConnected}",
            clientConnected);
    }

    private void CompleteTransaction(ulong clientId, ulong transactionId)
    {
        SendTerminalResult(
            clientId,
            transactionId,
            InteractionResult.Success,
            "Completed",
            "Result=Success");
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

    private static bool HasRequestedCardSelectionChanged(
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
        int inventorySlot = card == null ? -1 : FindInventorySlot(player, card);
        return inventorySlot >= 0 && inventorySlot != player.currentItemSlot;
    }

    private static bool HasSettledNonEmptySelection(PlayerControllerB player)
    {
        if (player.currentItemSlot < 0 ||
            player.currentItemSlot >= player.ItemSlots.Length)
        {
            return false;
        }

        GrabbableObject? selected = player.ItemSlots[player.currentItemSlot];
        return selected != null &&
               player.currentlyHeldObjectServer == selected &&
               selected.heldByPlayerOnServer;
    }

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

    private static bool ValidatePlayerState(
        PlayerControllerB player,
        out string failure)
    {
        if (player == null || !player.isPlayerControlled)
        {
            failure = "InvalidPlayerState";
            return false;
        }

        if (player.isPlayerDead)
        {
            failure = "PlayerDead";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private bool TryValidateInteractionSnapshot(
        PlayerControllerB player,
        Vector3 localRayOrigin,
        Vector3 localRayDirection,
        ulong transactionId,
        out int selectedSlot,
        out string failure,
        out string details)
    {
        selectedSlot = -1;
        failure = string.Empty;
        details = string.Empty;
        if (!IsFinite(localRayOrigin) || !IsFinite(localRayDirection) ||
            localRayDirection.sqrMagnitude < 0.81f ||
            localRayDirection.sqrMagnitude > 1.21f)
        {
            failure = "InvalidInteractionSnapshot";
            details =
                $"LocalRayOrigin={localRayOrigin} | LocalRayDirection={localRayDirection}";
            return false;
        }

        Vector3 worldOrigin = transform.TransformPoint(localRayOrigin);
        Vector3 worldDirection =
            transform.TransformDirection(localRayDirection).normalized;
        Vector3 expectedEyePosition = player.playerEye != null
            ? player.playerEye.position
            : player.transform.position + Vector3.up * 1.5f;
        if (Vector3.Distance(worldOrigin, expectedEyePosition) >
            MaximumSnapshotOriginOffset)
        {
            failure = "InvalidSnapshotOrigin";
            details =
                $"RayOrigin={worldOrigin} | ExpectedEye={expectedEyePosition} | " +
                $"Offset={Vector3.Distance(worldOrigin, expectedEyePosition):F3}";
            return false;
        }

        Collider? interactionCollider = FindInteractionCollider();
        Vector3 target = interactionCollider != null
            ? interactionCollider.ClosestPoint(worldOrigin)
            : transform.position;
        Vector3 originToTarget = target - worldOrigin;
        if (originToTarget.magnitude > player.grabDistance ||
            originToTarget.sqrMagnitude < 0.001f ||
            Vector3.Dot(
                worldDirection,
                originToTarget.normalized) < 0.75f)
        {
            failure = "InteractionOutOfRange";
            details =
                $"RayOrigin={worldOrigin} | RayDirection={worldDirection} | " +
                $"Target={target} | Distance={originToTarget.magnitude:F3} | " +
                $"MaximumDistance={player.grabDistance:F3}";
            return false;
        }

        Ray snapshotRay = new(worldOrigin, worldDirection);
        if (!IsCabinetFirstUnobstructedHit(
                player,
                snapshotRay,
                player.grabDistance,
                transactionId,
                "RequestSnapshot",
                out failure,
                out details))
        {
            return false;
        }

        // Slot identity is derived from the immutable request-time ray. Inventory
        // settlement can take several frames, but later camera motion cannot
        // redirect the already-authenticated interaction to another slot.
        TrySelectSlotFromRay(snapshotRay, player.grabDistance, out selectedSlot);
        return true;
    }

    private bool ValidateCurrentPlayerAccess(
        PlayerControllerB player,
        ulong transactionId,
        out string failure,
        out string details)
    {
        if (!ValidatePlayerState(player, out _))
        {
            failure = "InvalidPlayerState";
            details = string.Empty;
            return false;
        }

        Vector3 currentEyePosition = player.playerEye != null
            ? player.playerEye.position
            : player.transform.position + Vector3.up * 1.5f;
        Collider? interactionCollider = FindInteractionCollider();
        Vector3 target = interactionCollider != null
            ? interactionCollider.ClosestPoint(currentEyePosition)
            : transform.position;
        Vector3 eyeToTarget = target - currentEyePosition;
        float maximumDistance = player.grabDistance + InteractionDistancePadding;
        if (eyeToTarget.sqrMagnitude < 0.001f ||
            eyeToTarget.magnitude > maximumDistance)
        {
            failure = "InteractionOutOfRange";
            details =
                $"CurrentEye={currentEyePosition} | Target={target} | " +
                $"Distance={eyeToTarget.magnitude:F3} | MaximumDistance={maximumDistance:F3}";
            return false;
        }

        return IsCabinetFirstUnobstructedHit(
            player,
            new Ray(currentEyePosition, eyeToTarget.normalized),
            maximumDistance,
            transactionId,
            "CurrentAccess",
            out failure,
            out details);
    }

    private bool IsCabinetFirstUnobstructedHit(
        PlayerControllerB player,
        Ray ray,
        float maximumDistance,
        ulong transactionId,
        string validationStage,
        out string failure,
        out string details)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            maximumDistance,
            InteractionCollisionMask,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            Collider? collider = hit.collider;
            if (collider == null)
                continue;

            ShipCardDisplayController? hitController =
                collider.GetComponentInParent<ShipCardDisplayController>();
            bool cabinetOwned = hitController == this;
            bool playerOwned = collider.transform.IsChildOf(player.transform);
            bool inventoryOwned = IsColliderOwnedByPlayerInventory(
                collider,
                player,
                out GrabbableObject? inventoryItem);
            string decision;
            if (playerOwned)
                decision = "IgnoredRequestingPlayerCollider";
            else if (inventoryOwned)
                decision = "IgnoredRequestingPlayerInventoryCollider";
            else if (cabinetOwned)
                decision = "AcceptedCabinetCollider";
            else if (collider.isTrigger)
                decision = "IgnoredUnrelatedTrigger";
            else
                decision = "BlockedByWorldCollider";

            string hitDetails = BuildOcclusionHitDiagnostic(
                ray,
                maximumDistance,
                collider,
                hit.distance,
                cabinetOwned,
                playerOwned,
                inventoryOwned,
                inventoryItem,
                validationStage,
                decision);
            if (Plugin.EnableDebugLogging)
            {
                LogTransaction(
                    transactionId,
                    player.actualClientId,
                    "Interaction",
                    "OcclusionTrace",
                    hitDetails);
            }

            if (playerOwned || inventoryOwned)
                continue;

            if (cabinetOwned)
            {
                failure = string.Empty;
                details = string.Empty;
                return true;
            }

            if (!collider.isTrigger)
            {
                failure = "InteractionOccluded";
                details = hitDetails;
                return false;
            }
        }

        failure = "InvalidCabinetTarget";
        details =
            $"Stage={validationStage} | RayOrigin={ray.origin} | " +
            $"RayDirection={ray.direction} | MaximumDistance={maximumDistance:F3} | " +
            $"CollisionMask={InteractionCollisionMask} | Hits={hits.Length} | " +
            "Decision=NoCabinetColliderHit";
        return false;
    }

    internal static bool IsColliderOwnedByPlayerInventory(
        Collider collider,
        PlayerControllerB player,
        out GrabbableObject? inventoryItem)
    {
        inventoryItem = collider == null
            ? null
            : collider.GetComponentInParent<GrabbableObject>();
        if (inventoryItem == null || player == null || player.ItemSlots == null)
            return false;

        foreach (GrabbableObject? item in player.ItemSlots)
        {
            if (item == inventoryItem)
            {
                // A stale slot reference must not make an ordinary world item
                // transparent to server line-of-sight validation.
                return inventoryItem.playerHeldBy == player ||
                       inventoryItem == player.currentlyHeldObject ||
                       inventoryItem == player.currentlyHeldObjectServer;
            }
        }

        return false;
    }

    private static string BuildOcclusionHitDiagnostic(
        Ray ray,
        float maximumDistance,
        Collider collider,
        float hitDistance,
        bool cabinetOwned,
        bool playerOwned,
        bool inventoryOwned,
        GrabbableObject? inventoryItem,
        string validationStage,
        string decision)
    {
        int layer = collider.gameObject.layer;
        return
            $"Stage={validationStage} | RayOrigin={ray.origin} | " +
            $"RayDirection={ray.direction} | MaximumDistance={maximumDistance:F3} | " +
            $"CollisionMask={InteractionCollisionMask} | Collider={collider.name} | " +
            $"Path={BuildHierarchyPath(collider.transform)} | Layer={layer} " +
            $"({LayerMask.LayerToName(layer)}) | HitDistance={hitDistance:F3} | " +
            $"CabinetOwned={cabinetOwned} | PlayerOwned={playerOwned} | " +
            $"InventoryOwned={inventoryOwned} | " +
            $"InventoryObject={(inventoryItem == null ? "none" : inventoryItem.name)} | " +
            $"Trigger={collider.isTrigger} | Decision={decision}";
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        string path = transform.name;
        Transform? parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

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

    private void Reject(
        ulong clientId,
        ulong transactionId,
        string reason,
        string details = "")
    {
        InteractionResult result = reason == "InteractionOccluded"
            ? InteractionResult.Occluded
            : InteractionResult.Failed;
        SendTerminalResult(
            clientId,
            transactionId,
            result,
            "Rejected",
            $"Reason={reason}" +
            (string.IsNullOrEmpty(details) ? string.Empty : $" | {details}"));
        LogDiagnosticRateLimited(
            $"{clientId}:{reason}",
            $"SHIP CARD DISPLAY INTERACTION REJECTED | Transaction={transactionId} | " +
            $"Cabinet={NetworkObjectId} | Client={clientId} | Reason={reason}" +
            (string.IsNullOrEmpty(details) ? string.Empty : $" | {details}"));
    }

    private void RejectRetryable(
        ulong clientId,
        ulong transactionId,
        string reason)
    {
        SendTerminalResult(
            clientId,
            transactionId,
            InteractionResult.Synchronizing,
            "Retryable",
            $"Reason={reason}");
    }

    private void CancelChangedInteraction(
        ulong clientId,
        ulong transactionId,
        string reason)
    {
        SendTerminalResult(
            clientId,
            transactionId,
            InteractionResult.ActionChanged,
            "Canceled",
            $"Reason={reason}");
    }

    private static void LogTransaction(
        ulong transactionId,
        ulong clientId,
        string action,
        string state,
        string details)
    {
        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY TRANSACTION | Transaction={transactionId} | " +
            $"Client={clientId} | Action={action} | State={state} | {details}");
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

    private void SendTerminalResult(
        ulong clientId,
        ulong transactionId,
        InteractionResult result,
        string state,
        string details,
        bool sendToClient = true)
    {
        if (!terminalTransactions.Add(transactionId))
        {
            LogTransaction(
                transactionId,
                clientId,
                "Interaction",
                "DuplicateTerminalSuppressed",
                $"AttemptedResult={result} | AttemptedState={state}");
            return;
        }

        LogTransaction(
            transactionId,
            clientId,
            "Interaction",
            state,
            details);
        if (sendToClient)
            SendResult(clientId, result);
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
            InteractionResult.Synchronizing =>
                "Inventory synchronization is still catching up. Try again.",
            InteractionResult.AttachmentFailed =>
                "The transaction committed, but the returned card could not be synchronized.",
            InteractionResult.ActionChanged =>
                "Your selected inventory item changed; interaction canceled.",
            InteractionResult.Occluded =>
                "Move closer or clear the line of sight to the display case.",
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
        Synchronizing,
        AttachmentFailed,
        ActionChanged,
        Occluded,
        Failed
    }

    private sealed class PendingAttachment
    {
        internal PendingAttachment(
            ulong transactionId,
            ulong clientId,
            int inventorySlot,
            ulong networkObjectId,
            string action)
        {
            TransactionId = transactionId;
            ClientId = clientId;
            InventorySlot = inventorySlot;
            NetworkObjectId = networkObjectId;
            Action = action;
        }

        internal ulong TransactionId { get; }
        internal ulong ClientId { get; }
        internal int InventorySlot { get; }
        internal ulong NetworkObjectId { get; }
        internal string Action { get; }
        internal int Attempts { get; set; }
        internal float ResponseDeadline { get; set; }
    }
}
