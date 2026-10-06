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

    // Server-side diagnostic metadata; natural spawns retain the default false value.
    internal bool SpawnedFromBox { get; set; }

    // Prevent a client from sending many RPC requests
    // while waiting for the server to process the first one.
    private float nextOpenRequestTime;

    internal int DiagnosticActivationCount { get; private set; }

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

        GetSpawnFrame(out Vector3 spawnOrigin, out Vector3 spawnRight);

        ulong revealId = CreateRevealId();
        ulong packNetworkObjectId = NetworkObjectId;
        NotifyRevealClientRpc(
            revealId,
            packNetworkObjectId,
            senderClientId,
            (int)PackType,
            holder!.transform.position,
            holder.transform.forward,
            result.IsGodPack,
            result.Cards[0].Card.CardId, (int)result.Cards[0].Variant, (int)result.Cards[0].Card.Rarity,
            result.Cards[1].Card.CardId, (int)result.Cards[1].Variant, (int)result.Cards[1].Card.Rarity,
            result.Cards[2].Card.CardId, (int)result.Cards[2].Variant, (int)result.Cards[2].Card.Rarity
        );

        float revealDuration = BoosterRevealController.CalculateRevealDuration(
            result.Cards[0].Card.Rarity,
            result.Cards[1].Card.Rarity,
            result.Cards[2].Card.Rarity);

        DelayedBoosterCardSpawn.Schedule(result, spawnOrigin, spawnRight, revealDuration);

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
        Vector3 openerPosition, Vector3 openerForward, bool isGodPack,
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
        out Vector3 spawnRight)
    {
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
        Vector3 spawnRight)
    {
        foreach (CardPull pull in result.Cards)
        {
            SpawnCard(
                pull,
                spawnOrigin,
                spawnRight
            );
        }
    }

    private static void SpawnCard(
        CardPull pull,
        Vector3 spawnOrigin,
        Vector3 spawnRight)
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

        GameObject cardObject =
            Instantiate(
                pull.Card.ItemAsset.spawnPrefab,
                spawnPosition,
                Quaternion.identity
            );

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
        float verticalAdjustment =
            CustomSpawnPlacement.PrepareSpawnHeight(
                cardObject,
                spawnPosition,
                CustomSpawnPlacement.CardVerticalClearance,
                out float boundsMinimumY);
        Vector3 correctedPosition = cardObject.transform.position;

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

        // Plugin.Log.LogInfo(
        //     $"SPAWNED SLOT " +
        //     $"{pull.SlotIndex + 1}: " +
        //     $"{pull.Card.DisplayName} | " +
        //     $"{pull.Variant} | " +
        //     $"${pull.UngradedValue}"
        // );
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
    private float delay;

    internal static void Schedule(PackResult result, Vector3 spawnOrigin, Vector3 spawnRight, float delay)
    {
        GameObject host = new("LethalCardsDelayedBoosterCardSpawn");
        DelayedBoosterCardSpawn spawner = host.AddComponent<DelayedBoosterCardSpawn>();
        spawner.result = result;
        spawner.spawnOrigin = spawnOrigin;
        spawner.spawnRight = spawnRight;
        spawner.delay = delay;
        spawner.StartCoroutine(spawner.SpawnAfterReveal());
    }

    private System.Collections.IEnumerator SpawnAfterReveal()
    {
        yield return new WaitForSeconds(delay);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && result != null)
            BoosterPackBehaviour.SpawnPackContents(result, spawnOrigin, spawnRight);

        Destroy(gameObject);
    }
}
