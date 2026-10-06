using LethalCards.Cards;
using Unity.Netcode;
using UnityEngine;
using GameNetcodeStuff;
using LethalCards.Networking;

namespace LethalCards.Boosters;

public class BoosterPackBehaviour : PhysicsProp
{
    public BoosterType PackType = BoosterType.Light;

    // Server-authoritative state.
    private bool opened;
    private readonly NetworkVariable<bool> networkBoxSpawnedInShip = new(false);
    private readonly NetworkVariable<Vector3> networkBoxShipLocalPosition = new();
    private readonly NetworkVariable<Quaternion> networkBoxShipLocalRotation = new();
    private bool hasPendingBoxShipPlacement;
    private Vector3 pendingBoxShipLocalPosition;
    private Quaternion pendingBoxShipLocalRotation;

    // Server-side diagnostic metadata; natural spawns retain the default false value.
    internal bool SpawnedFromBox { get; set; }

    // Prevent a client from sending many RPC requests
    // while waiting for the server to process the first one.
    private float nextOpenRequestTime;

    internal int DiagnosticActivationCount { get; private set; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkBoxSpawnedInShip.OnValueChanged += OnBoxSpawnedInShipChanged;

        if (IsServer && hasPendingBoxShipPlacement)
            ApplyPendingBoxShipPlacement();

        if (networkBoxSpawnedInShip.Value)
            ApplyBoxShipPlacement();
    }

    public override void OnNetworkDespawn()
    {
        networkBoxSpawnedInShip.OnValueChanged -= OnBoxSpawnedInShipChanged;
        base.OnNetworkDespawn();
    }

    public override void GrabItem()
    {
        if (IsServer && networkBoxSpawnedInShip.Value)
            networkBoxSpawnedInShip.Value = false;
        base.GrabItem();
    }

    internal void InitializeBoxShipPlacement(Vector3 localPosition, Quaternion localRotation)
    {
        pendingBoxShipLocalPosition = localPosition;
        pendingBoxShipLocalRotation = localRotation;
        hasPendingBoxShipPlacement = true;

        if (IsSpawned && IsServer)
            ApplyPendingBoxShipPlacement();
    }

    private void ApplyPendingBoxShipPlacement()
    {
        if (!IsServer)
            return;

        networkBoxShipLocalPosition.Value = pendingBoxShipLocalPosition;
        networkBoxShipLocalRotation.Value = pendingBoxShipLocalRotation;
        networkBoxSpawnedInShip.Value = true;
        hasPendingBoxShipPlacement = false;
        ApplyBoxShipPlacement();
    }

    private void OnBoxSpawnedInShipChanged(bool previous, bool current)
    {
        if (current)
            ApplyBoxShipPlacement();
    }

    private void ApplyBoxShipPlacement()
    {
        Transform? shipParent = StartOfRound.Instance?.elevatorTransform;
        if (shipParent == null)
        {
            Plugin.Log.LogWarning(
                $"BOX PACK SHIP PLACEMENT FAILED | Type={PackType} | Elevator transform unavailable.");
            return;
        }

        transform.SetParent(shipParent, false);
        transform.localPosition = networkBoxShipLocalPosition.Value;
        transform.localRotation = networkBoxShipLocalRotation.Value;
        CustomSpawnPlacement.MarkForFloorTargetCorrection(this);
        ApplyInitialShipRestState(networkBoxShipLocalPosition.Value);
    }

    internal void ApplyInitialShipRestState(Vector3 localRestPosition)
    {
        parentObject = null;
        isInElevator = true;
        isInShipRoom = true;
        startFallingPosition = localRestPosition;
        targetFloorPosition = localRestPosition;
        fallTime = 1f;
        hasHitGround = true;
        reachedFloorTarget = true;

        Rigidbody? body = propBody != null ? propBody : GetComponent<Rigidbody>();
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    public override void ItemActivate(
        bool used,
        bool buttonDown = true)
    {
        if (buttonDown && BoosterInspectionGuard.IsInspectingThis(playerHeldBy, this))
            return;

        /* Plugin.Log.LogInfo($"BOOSTER PACK ITEM ACTIVATE | Type={PackType} | Used={used} | ButtonDown={buttonDown} | " +
            $"HeldBy={playerHeldBy?.actualClientId} | Spawned={IsSpawned} | Server={IsServer} | Owner={OwnerClientId} | " +
            $"Component={GetType().Name} | NetworkObjectId={NetworkObjectId} | ItemProperties={itemProperties?.name} | " +
            $"Grabbable={grabbable} | Parent={parentObject?.name} | Renderer={mainObjectRenderer?.name} | UseCooldown={useCooldown} | " +
            $"Consumption={GetComponent<NetworkItemConsumption>() != null}"); */
        // DiagnosticActivationCount++; // Disabled testing counter.
        // BoosterDiagnostics.Log("ACTIVATE", this,
        //     detail: $"Used={used} | ButtonDown={buttonDown} | Opened={opened}");
        base.ItemActivate(
            used,
            buttonDown
        );

        if (!buttonDown)
            return;

        // Host/server can process the opening immediately.
        if (IsServer)
        {
            // Plugin.Log.LogInfo($"BOOSTER PACK OPEN REQUEST | Type={PackType} | Path=Host");
            // BoosterDiagnostics.Log("HOST_DIRECT_OPEN", this);
            TryOpenPackServer(NetworkManager.Singleton.LocalClientId);
            return;
        }

        // Clients request that the server open the pack.
        if (Time.realtimeSinceStartup < nextOpenRequestTime)
            return;

        nextOpenRequestTime = Time.realtimeSinceStartup + 0.5f;

        /* Plugin.Log.LogInfo(
            $"BOOSTER PACK OPEN REQUEST | " +
            $"Type={PackType} | " +
            $"Client={NetworkManager.Singleton?.LocalClientId}"
        ); */

        RequestOpenPackServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOpenPackServerRpc(
        ServerRpcParams rpcParams = default)
    {
        /* Plugin.Log.LogInfo(
            $"BOOSTER OPEN SERVER RPC | " +
            $"Type={PackType} | " +
            $"SenderClientId=" +
            $"{rpcParams.Receive.SenderClientId}"
        ); */

        TryOpenPackServer(rpcParams.Receive.SenderClientId);
    }

    private void TryOpenPackServer(ulong senderClientId)
    {
        PlayerControllerB? sender = ResolvePlayer(senderClientId);

        // BoosterDiagnostics.Log("SERVER_OPEN_ENTER", this, detail: $"Sender={senderClientId}");
        if (!IsServer)
        {
            LogOpenRejected("NotServer", senderClientId);
            // BoosterDiagnostics.Log("OPEN_REJECT", this, detail: "Reason=Not server");
            return;
        }

        // Match the tested box path: unused player slots can also have client ID zero.
        // Authenticate the server's actual holder instead of taking the last matching slot.
        PlayerControllerB? holder = playerHeldBy;
        NetworkItemConsumption? consumption = GetComponent<NetworkItemConsumption>();
        string? rejectionReason = holder == null
            ? sender == null ? "SenderNotFound" : "PackHasNoServerHolder"
            : holder.actualClientId != senderClientId
                ? "SenderNotHoldingThisPack"
                : !holder.isPlayerControlled
                    ? "SenderNotFound"
                    : holder.isPlayerDead
                        ? "SenderDead"
                        : !IsSpawned
                            ? "PackNotSpawned"
                            : !heldByPlayerOnServer
                                ? "PackNotHeldOnServer"
                                : OwnerClientId != holder.actualClientId
                                    ? "Ownership"
                                    : holder.currentlyHeldObjectServer != this
                                        ? "SenderNotHoldingThisPack"
                                        : consumption == null
                                            ? "ConsumptionComponentMissing"
                                            : null;
        if (rejectionReason != null)
        {
            LogOpenRejected(rejectionReason, senderClientId);
            // BoosterDiagnostics.Log("OPEN_REJECT", this, holder,
            //     $"Sender={senderClientId} | Reason={rejectionReason}");
            /* Plugin.Log.LogInfo($"BOOSTER PACK OPEN REJECT | Type={PackType} | Sender={senderClientId} | " +
                $"Holder={holder?.actualClientId} | Controlled={holder?.isPlayerControlled} | Owner={OwnerClientId} | " +
                $"HeldObjectMatches={(holder != null && holder.currentlyHeldObjectServer == this)} | Reason={reason}"); */
            return;
        }

        if (BoosterInspectionGuard.IsInspectingThis(holder, this))
        {
            LogOpenRejected("InspectingThisPack", senderClientId);
            return;
        }

        CollectionSaveManager.LoadForCurrentSave();

        if (opened)
        {
            LogOpenRejected("AlreadyOpened", senderClientId);
            // BoosterDiagnostics.Log("OPEN_REJECT", this, holder,
            //     $"Sender={senderClientId} | Reason=Already opened");
            /* Plugin.Log.LogInfo(
                $"BOOSTER OPEN REJECTED | " +
                $"Type={PackType} | " +
                $"Reason=Already opened"
            ); */

            return;
        }

        opened = true;

        // BoosterDiagnostics.Log("OPEN_ACCEPT", this, holder, $"Sender={senderClientId}");

        Plugin.Log.LogInfo(
            $"BOOSTER PACK OPEN | " +
            $"Type={PackType}"
        );

        // All RNG happens on the server.
        PackResult result =
            BoosterGenerator.OpenPack(
                PackType
            );

        // LogPackResult(
        //     result
        // );

        GetSpawnFrame(
            out Vector3 spawnOrigin,
            out Vector3 spawnRight,
            out bool spawnInShip,
            out Vector3 shipLocalSpawnOrigin,
            out Vector3 shipLocalSpawnRight);

        ulong revealId = CreateRevealId();
        ulong packNetworkObjectId = NetworkObjectId;
        Vector3 openerPosition = holder!.transform.position;
        Vector3 openerForward = holder.transform.forward;
        bool revealIsShipAnchored = false;
        Vector3 shipLocalOpeningPosition = Vector3.zero;
        Quaternion shipLocalOpeningRotation = Quaternion.identity;
        Transform? revealShipTransform = StartOfRound.Instance?.elevatorTransform;
        if (revealShipTransform != null &&
            ((isInElevator && isInShipRoom) ||
             (holder.isInElevator && holder.isInHangarShipRoom)))
        {
            Vector3 horizontalOpeningForward = openerForward;
            horizontalOpeningForward.y = 0f;
            if (horizontalOpeningForward.sqrMagnitude <= 0.0001f)
                horizontalOpeningForward = Vector3.forward;
            else
                horizontalOpeningForward.Normalize();

            Quaternion openingRotation = Quaternion.LookRotation(horizontalOpeningForward, Vector3.up);
            revealIsShipAnchored = true;
            shipLocalOpeningPosition = revealShipTransform.InverseTransformPoint(openerPosition);
            shipLocalOpeningRotation = Quaternion.Inverse(revealShipTransform.rotation) * openingRotation;
        }

        NotifyRevealClientRpc(
            revealId,
            packNetworkObjectId,
            senderClientId,
            (int)PackType,
            openerPosition,
            openerForward,
            revealIsShipAnchored,
            shipLocalOpeningPosition,
            shipLocalOpeningRotation,
            result.IsGodPack,
            result.Cards[0].Card.CardId, (int)result.Cards[0].Variant, (int)result.Cards[0].Card.Rarity,
            result.Cards[1].Card.CardId, (int)result.Cards[1].Variant, (int)result.Cards[1].Card.Rarity,
            result.Cards[2].Card.CardId, (int)result.Cards[2].Variant, (int)result.Cards[2].Card.Rarity
        );

        float revealDuration = BoosterRevealController.CalculateRevealDuration(
            result.Cards[0].Card.Rarity,
            result.Cards[1].Card.Rarity,
            result.Cards[2].Card.Rarity);

        DelayedBoosterCardSpawn.Schedule(
            result,
            spawnOrigin,
            spawnRight,
            spawnInShip,
            shipLocalSpawnOrigin,
            shipLocalSpawnRight,
            revealDuration);

        // Collection state remains server-authoritative, but a noncritical
        // synchronization failure must not abort an accepted pack opening.
        try
        {
            _ = RegisterCollection(result);
        }
        catch (System.Exception exception)
        {
            Plugin.Log.LogError(
                $"COLLECTION SYNC FAILED | PackNetworkObjectId={DescribeNetworkId(this)} | " +
                $"SenderClientId={senderClientId} | Exception={exception}");
        }

        // The generated result is now owned by the delayed server spawner, so the
        // real gameplay booster can leave the opener's hand immediately.
        RemoveBoosterPack();
    }

    [ClientRpc]
    private void NotifyRevealClientRpc(
        ulong revealId, ulong packNetworkObjectId, ulong openerClientId, int boosterType,
        Vector3 openerPosition, Vector3 openerForward,
        bool revealIsShipAnchored, Vector3 shipLocalOpeningPosition, Quaternion shipLocalOpeningRotation,
        bool isGodPack,
        string card1, int variant1, int rarity1,
        string card2, int variant2, int rarity2,
        string card3, int variant3, int rarity3,
        ClientRpcParams clientRpcParams = default)
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null || !System.Enum.IsDefined(typeof(BoosterType), boosterType))
            return;

        bool firstPerson = manager.LocalClientId == openerClientId;
        Plugin.Log.LogInfo(
            $"BOOSTER REVEAL RECEIVED | RevealId={revealId} | PackNetworkObjectId={packNetworkObjectId} | " +
            $"OpenerClientId={openerClientId} | LocalClientId={manager.LocalClientId} | " +
            $"Mode={(firstPerson ? "FirstPerson" : "ObserverWorld")} | BoosterType={(BoosterType)boosterType}");

        BoosterRevealController.Begin(
            revealId, packNetworkObjectId, openerClientId, firstPerson,
            firstPerson ? null : ResolvePlayer(openerClientId), openerPosition, openerForward,
            revealIsShipAnchored, shipLocalOpeningPosition, shipLocalOpeningRotation,
            (BoosterType)boosterType, isGodPack,
            card1, variant1, rarity1,
            card2, variant2, rarity2,
            card3, variant3, rarity3);
    }

    private static ulong CreateRevealId()
    {
        ulong revealId;
        do
        {
            revealId = System.BitConverter.ToUInt64(System.Guid.NewGuid().ToByteArray(), 0);
        }
        while (revealId == 0);
        return revealId;
    }

    private void LogPackResult(
        PackResult result)
    {
        Plugin.Log.LogInfo(
            "================================"
        );

        Plugin.Log.LogInfo(
            $"OPENING {result.PackType} PACK"
        );

        Plugin.Log.LogInfo(
            $"GOD PACK: {result.IsGodPack}"
        );

        foreach (CardPull pull in result.Cards)
        {
            Plugin.Log.LogInfo(
                $"SLOT {pull.SlotIndex + 1}: " +
                $"{pull.Card.CardId} | " +
                $"{pull.Card.DisplayName} | " +
                $"{pull.Card.Rarity} | " +
                $"{pull.Variant} | " +
                $"VALUE ${pull.UngradedValue}"
            );
        }

        Plugin.Log.LogInfo(
            "================================"
        );
    }

    private void GetSpawnFrame(
        out Vector3 spawnOrigin,
        out Vector3 spawnRight,
        out bool spawnInShip,
        out Vector3 shipLocalSpawnOrigin,
        out Vector3 shipLocalSpawnRight)
    {
        spawnInShip = false;
        shipLocalSpawnOrigin = Vector3.zero;
        shipLocalSpawnRight = Vector3.right;

        if (playerHeldBy != null)
        {
            // Spawn the cards a little in front
            // of the player holding the pack.
            spawnOrigin =
                playerHeldBy.transform.position +
                playerHeldBy.transform.forward *
                1.25f +
                Vector3.up * 0.5f;

            spawnRight =
                playerHeldBy.transform.right;

            StartOfRound? round = StartOfRound.Instance;
            if (playerHeldBy.isInElevator && playerHeldBy.isInHangarShipRoom &&
                round != null && round.elevatorTransform != null)
            {
                spawnInShip = true;
                shipLocalSpawnOrigin = round.elevatorTransform.InverseTransformPoint(spawnOrigin);
                shipLocalSpawnRight = round.elevatorTransform.InverseTransformDirection(spawnRight).normalized;
            }
        }
        else
        {
            // Fallback if the server cannot resolve
            // a player holding the pack.
            spawnOrigin =
                transform.position +
                Vector3.up * 0.4f;

            spawnRight =
                transform.right;

            Plugin.Log.LogWarning(
                $"BOOSTER OPEN | " +
                $"Type={PackType} | " +
                $"playerHeldBy was null; " +
                $"using pack position."
            );
        }
    }

    internal static void SpawnPackContents(
        PackResult result,
        Vector3 spawnOrigin,
        Vector3 spawnRight,
        Transform? shipParent = null)
    {
        foreach (CardPull pull in result.Cards)
        {
            SpawnCard(
                pull,
                spawnOrigin,
                spawnRight,
                shipParent
            );
        }
    }

    private static void SpawnCard(
        CardPull pull,
        Vector3 spawnOrigin,
        Vector3 spawnRight,
        Transform? shipParent)
    {
        if (pull.Card == null)
        {
            Plugin.Log.LogError(
                $"Slot {pull.SlotIndex + 1} " +
                $"has no CardDefinition."
            );

            return;
        }

        if (pull.Card.ItemAsset == null)
        {
            Plugin.Log.LogError(
                $"{pull.Card.DisplayName} " +
                $"has no Item asset."
            );

            return;
        }

        if (
            pull.Card.ItemAsset.spawnPrefab ==
            null)
        {
            Plugin.Log.LogError(
                $"{pull.Card.DisplayName} " +
                $"has no spawn prefab."
            );

            return;
        }

        // Slot 1 = left
        // Slot 2 = middle
        // Slot 3 = right
        float horizontalOffset =
            (pull.SlotIndex - 1) *
            0.30f;

        Vector3 spawnPosition =
            spawnOrigin +
            spawnRight *
            horizontalOffset;

        GameObject cardObject = shipParent != null
            ? Instantiate(
                pull.Card.ItemAsset.spawnPrefab,
                spawnPosition,
                Quaternion.identity,
                shipParent)
            : Instantiate(
                pull.Card.ItemAsset.spawnPrefab,
                spawnPosition,
                Quaternion.identity);

        PhysicsProp cardPhysicsProp =
            cardObject.GetComponent<PhysicsProp>();

        if (cardPhysicsProp == null)
        {
            Plugin.Log.LogError(
                $"{pull.Card.DisplayName} " +
                $"spawned without PhysicsProp."
            );

            Destroy(
                cardObject
            );

            return;
        }

        cardPhysicsProp.itemProperties = pull.Card.ItemAsset;
        Vector3? shipLocalRestPosition = null;
        Quaternion shipLocalRestRotation = Quaternion.identity;
        if (shipParent != null)
        {
            Vector3 worldFloorPosition = cardPhysicsProp.GetItemFloorPosition(spawnPosition);
            cardObject.transform.position = worldFloorPosition;
            shipLocalRestPosition = shipParent.InverseTransformPoint(worldFloorPosition);
            shipLocalRestRotation = Quaternion.Euler(
                cardPhysicsProp.itemProperties.restingRotation.x,
                cardPhysicsProp.floorYRot + cardPhysicsProp.itemProperties.restingRotation.y,
                cardPhysicsProp.itemProperties.restingRotation.z);
            cardObject.transform.localRotation = shipLocalRestRotation;
            ApplyShipRestState(cardPhysicsProp, shipLocalRestPosition.Value);
        }
        else
        {
            _ = CustomSpawnPlacement.PrepareSpawnHeight(
                cardObject,
                spawnPosition,
                CustomSpawnPlacement.CardVerticalClearance,
                out _);
        }

        // Plugin.Log.LogInfo(
        //     $"CUSTOM CARD SPAWN PLACEMENT | CardId={pull.Card.CardId} | " +
        //     $"OriginalPosition={spawnPosition} | CorrectedPosition={correctedPosition} | " +
        //     $"VerticalAdjustment={verticalAdjustment} | BoundsMinimumY={boundsMinimumY}");

        CardInstanceData instanceData =
            cardObject.GetComponent<CardInstanceData>();

        if (instanceData == null)
        {
            Plugin.Log.LogError(
                $"{pull.Card.DisplayName} " +
                $"spawned without CardInstanceData."
            );

            Destroy(
                cardObject
            );

            return;
        }

        instanceData.Initialize(
            pull
        );
        if (shipLocalRestPosition.HasValue)
            instanceData.InitializeShipSpawnPlacement(
                shipLocalRestPosition.Value,
                shipLocalRestRotation);

        NetworkObject cardNetworkObject =
            cardObject.GetComponent<NetworkObject>();

        if (cardNetworkObject == null)
        {
            Plugin.Log.LogError(
                $"{pull.Card.DisplayName} " +
                $"spawned without NetworkObject."
            );

            Destroy(
                cardObject
            );

            return;
        }

        // Set this specific pull's value before
        // spawning the NetworkObject.
        cardPhysicsProp.SetScrapValue(
            pull.UngradedValue
        );

        CustomSpawnPlacement.MarkForFloorTargetCorrection(cardPhysicsProp);
        cardNetworkObject.Spawn();

        if (shipLocalRestPosition.HasValue)
        {
            Plugin.Log.LogInfo(
                $"CARD SHIP SPAWN | CardId={pull.Card.CardId} | InShip=True | " +
                $"WorldFloorPosition={cardObject.transform.position} | " +
                $"LocalShipPosition={shipLocalRestPosition.Value} | Parent={shipParent!.name}");
        }

        // Plugin.Log.LogInfo(
        //     $"SPAWNED SLOT " +
        //     $"{pull.SlotIndex + 1}: " +
        //     $"{pull.Card.DisplayName} | " +
        //     $"{pull.Variant} | " +
        //     $"${pull.UngradedValue}"
        // );
    }

    private static void ApplyShipRestState(GrabbableObject card, Vector3 localRestPosition)
    {
        card.parentObject = null;
        card.isInElevator = true;
        card.isInShipRoom = true;
        card.startFallingPosition = localRestPosition;
        card.targetFloorPosition = localRestPosition;
        card.fallTime = 1f;
        card.hasHitGround = true;
        card.reachedFloorTarget = true;
    }

    private bool RegisterCollection(
        PackResult result)
    {
        bool synchronizationSucceeded = true;
        foreach (CardPull pull in result.Cards)
        {
            synchronizationSucceeded &= CollectionManager.RegisterPull(pull);
        }

        // Plugin.Log.LogInfo(
        //     $"COLLECTION TOTAL | " +
        //     $"Cards=" +
        //     $"{CollectionManager.DiscoveredCardCount} | " +
        //     $"Variants=" +
        //     $"{CollectionManager.DiscoveredVariantCount}"
        // );
        return synchronizationSucceeded;
    }

    private void RemoveBoosterPack()
    {
        if (!IsServer)
            return;
        GetComponent<NetworkItemConsumption>().ConsumeServer();
    }

    private void LogOpenRejected(string reason, ulong senderClientId)
    {
        Plugin.Log.LogInfo(
            $"PACK OPEN REJECTED | Reason={reason} | NetworkObjectId={DescribeNetworkId(this)} | " +
            $"InstanceId={GetInstanceID()} | BoosterType={PackType} | SenderClientId={senderClientId} | " +
            $"OwnerClientId={OwnerClientId} | SpawnSource={(SpawnedFromBox ? "Box" : "Natural")}");
    }

    private static PlayerControllerB? ResolvePlayer(ulong clientId)
    {
        if (StartOfRound.Instance == null)
            return null;

        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            if (player != null && player.isPlayerControlled && player.actualClientId == clientId)
                return player;

        return null;
    }

    private static string DescribeNetworkId(GrabbableObject? item) =>
        item != null && item.IsSpawned ? item.NetworkObjectId.ToString() : "<unspawned>";
}

internal sealed class DelayedBoosterCardSpawn : MonoBehaviour
{
    private PackResult? result;
    private Vector3 spawnOrigin;
    private Vector3 spawnRight;
    private bool spawnInShip;
    private Vector3 shipLocalSpawnOrigin;
    private Vector3 shipLocalSpawnRight;
    private float delay;

    internal static void Schedule(
        PackResult result,
        Vector3 spawnOrigin,
        Vector3 spawnRight,
        bool spawnInShip,
        Vector3 shipLocalSpawnOrigin,
        Vector3 shipLocalSpawnRight,
        float delay)
    {
        GameObject host = new("LethalCardsDelayedBoosterCardSpawn");
        DelayedBoosterCardSpawn spawner = host.AddComponent<DelayedBoosterCardSpawn>();
        spawner.result = result;
        spawner.spawnOrigin = spawnOrigin;
        spawner.spawnRight = spawnRight;
        spawner.spawnInShip = spawnInShip;
        spawner.shipLocalSpawnOrigin = shipLocalSpawnOrigin;
        spawner.shipLocalSpawnRight = shipLocalSpawnRight;
        spawner.delay = delay;
        spawner.StartCoroutine(spawner.SpawnAfterReveal());
    }

    private System.Collections.IEnumerator SpawnAfterReveal()
    {
        yield return new WaitForSeconds(delay);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && result != null)
        {
            Transform? shipParent = null;
            Vector3 resolvedSpawnOrigin = spawnOrigin;
            Vector3 resolvedSpawnRight = spawnRight;
            if (spawnInShip)
            {
                shipParent = StartOfRound.Instance?.elevatorTransform;
                if (shipParent != null)
                {
                    resolvedSpawnOrigin = shipParent.TransformPoint(shipLocalSpawnOrigin);
                    resolvedSpawnRight = shipParent.TransformDirection(shipLocalSpawnRight).normalized;
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "CARD SHIP SPAWN FALLBACK | Elevator transform was unavailable; using the captured world spawn frame.");
                }
            }

            BoosterPackBehaviour.SpawnPackContents(
                result,
                resolvedSpawnOrigin,
                resolvedSpawnRight,
                shipParent);
        }

        Destroy(gameObject);
    }
}
