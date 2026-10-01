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

    // Prevent a client from sending many RPC requests
    // while waiting for the server to process the first one.
    private float nextOpenRequestTime;

    internal int DiagnosticActivationCount { get; private set; }

    public override void ItemActivate(
        bool used,
        bool buttonDown = true)
    {
        Plugin.Log.LogInfo($"BOOSTER PACK ITEM ACTIVATE | Type={PackType} | Used={used} | ButtonDown={buttonDown} | " +
            $"HeldBy={playerHeldBy?.actualClientId} | Spawned={IsSpawned} | Server={IsServer} | Owner={OwnerClientId} | " +
            $"Component={GetType().Name} | NetworkObjectId={NetworkObjectId} | ItemProperties={itemProperties?.name} | " +
            $"Grabbable={grabbable} | Parent={parentObject?.name} | Renderer={mainObjectRenderer?.name} | UseCooldown={useCooldown} | " +
            $"Consumption={GetComponent<NetworkItemConsumption>() != null}");
        // DiagnosticActivationCount++; // Disabled testing counter.
        BoosterDiagnostics.Log("ACTIVATE", this,
            detail: $"Used={used} | ButtonDown={buttonDown} | Opened={opened}");
        base.ItemActivate(
            used,
            buttonDown
        );

        if (!buttonDown)
        {
            BoosterDiagnostics.Log("ACTIVATE_SKIP", this, detail: "Reason=Button release");
            return;
        }

        // Host/server can process the opening immediately.
        if (IsServer)
        {
            Plugin.Log.LogInfo($"BOOSTER PACK OPEN REQUEST | Type={PackType} | Path=Host");
            BoosterDiagnostics.Log("HOST_DIRECT_OPEN", this);
            TryOpenPackServer(NetworkManager.Singleton.LocalClientId);
            return;
        }

        // Clients request that the server open the pack.
        if (Time.realtimeSinceStartup < nextOpenRequestTime)
        {
            BoosterDiagnostics.Log("ACTIVATE_SKIP", this, detail: "Reason=Request throttle");
            return;
        }

        nextOpenRequestTime = Time.realtimeSinceStartup + 0.5f;

        Plugin.Log.LogInfo(
            $"BOOSTER PACK OPEN REQUEST | " +
            $"Type={PackType} | " +
            $"Client={NetworkManager.Singleton?.LocalClientId}"
        );

        BoosterDiagnostics.Log("REQUEST_SEND", this);
        RequestOpenPackServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOpenPackServerRpc(
        ServerRpcParams rpcParams = default)
    {
        Plugin.Log.LogInfo(
            $"BOOSTER OPEN SERVER RPC | " +
            $"Type={PackType} | " +
            $"SenderClientId=" +
            $"{rpcParams.Receive.SenderClientId}"
        );

        TryOpenPackServer(rpcParams.Receive.SenderClientId);
    }

    private void TryOpenPackServer(ulong senderClientId)
    {
        BoosterDiagnostics.Log("SERVER_OPEN_ENTER", this, detail: $"Sender={senderClientId}");
        if (!IsServer)
        {
            BoosterDiagnostics.Log("OPEN_REJECT", this, detail: "Reason=Not server");
            return;
        }

        // Match the tested box path: unused player slots can also have client ID zero.
        // Authenticate the server's actual holder instead of taking the last matching slot.
        PlayerControllerB? holder = playerHeldBy;
        if (holder == null || holder.actualClientId != senderClientId ||
            !NetworkItemConsumption.IsValidHolder(holder, this) ||
            GetComponent<NetworkItemConsumption>() == null)
        {
            string reason = holder == null ? "Sender player not found" :
                holder.actualClientId != senderClientId ? "Sender is not holder" :
                !holder.isPlayerControlled ? "Player not controlled" :
                holder.isPlayerDead ? "Player dead" :
                !IsSpawned ? "Pack not spawned" :
                !heldByPlayerOnServer ? "Pack not held on server" :
                OwnerClientId != holder.actualClientId ? "Network owner mismatch" :
                holder.currentlyHeldObjectServer != this ? "Current held object mismatch" :
                "Consumption component missing";
            BoosterDiagnostics.Log("OPEN_REJECT", this, holder,
                $"Sender={senderClientId} | Reason={reason}");
            Plugin.Log.LogInfo($"BOOSTER PACK OPEN REJECT | Type={PackType} | Sender={senderClientId} | " +
                $"Holder={holder?.actualClientId} | Controlled={holder?.isPlayerControlled} | Owner={OwnerClientId} | " +
                $"HeldObjectMatches={(holder != null && holder.currentlyHeldObjectServer == this)} | Reason={reason}");
            return;
        }

        CollectionSaveManager.LoadForCurrentSave();

        if (opened)
        {
            BoosterDiagnostics.Log("OPEN_REJECT", this, holder,
                $"Sender={senderClientId} | Reason=Already opened");
            Plugin.Log.LogInfo(
                $"BOOSTER OPEN REJECTED | " +
                $"Type={PackType} | " +
                $"Reason=Already opened"
            );

            return;
        }

        opened = true;

        BoosterDiagnostics.Log("OPEN_ACCEPT", this, holder, $"Sender={senderClientId}");

        Plugin.Log.LogInfo(
            $"BOOSTER PACK OPEN | " +
            $"Type={PackType}"
        );

        // All RNG happens on the server.
        PackResult result =
            BoosterGenerator.OpenPack(
                PackType
            );

        // For now collection registration remains
        // server-side/shared-save behavior.
        RegisterCollection(
            result
        );

        LogPackResult(
            result
        );

        GetSpawnFrame(out Vector3 spawnOrigin, out Vector3 spawnRight);

        NotifyRevealClientRpc(
            result.IsGodPack,
            result.Cards[0].Card.CardId, (int)result.Cards[0].Variant, (int)result.Cards[0].Card.Rarity,
            result.Cards[1].Card.CardId, (int)result.Cards[1].Variant, (int)result.Cards[1].Card.Rarity,
            result.Cards[2].Card.CardId, (int)result.Cards[2].Variant, (int)result.Cards[2].Card.Rarity,
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { senderClientId }
                }
            }
        );

        float revealDuration = BoosterRevealController.CalculateRevealDuration(
            result.Cards[0].Card.Rarity,
            result.Cards[1].Card.Rarity,
            result.Cards[2].Card.Rarity);

        DelayedBoosterCardSpawn.Schedule(result, spawnOrigin, spawnRight, revealDuration);

        // The generated result is now owned by the delayed server spawner, so the
        // real gameplay booster can leave the opener's hand immediately.
        RemoveBoosterPack();
    }

    [ClientRpc]
    private void NotifyRevealClientRpc(bool isGodPack,
        string card1, int variant1, int rarity1,
        string card2, int variant2, int rarity2,
        string card3, int variant3, int rarity3,
        ClientRpcParams clientRpcParams = default)
    {
        BoosterRevealController.Begin(PackType, isGodPack,
            card1, variant1, rarity1,
            card2, variant2, rarity2,
            card3, variant3, rarity3);
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

        cardNetworkObject.Spawn();

        Plugin.Log.LogInfo(
            $"SPAWNED SLOT " +
            $"{pull.SlotIndex + 1}: " +
            $"{pull.Card.DisplayName} | " +
            $"{pull.Variant} | " +
            $"${pull.UngradedValue}"
        );
    }

    private void RegisterCollection(
        PackResult result)
    {
        foreach (CardPull pull in result.Cards)
        {
            CollectionManager.RegisterPull(
                pull
            );
        }

        Plugin.Log.LogInfo(
            $"COLLECTION TOTAL | " +
            $"Cards=" +
            $"{CollectionManager.DiscoveredCardCount} | " +
            $"Variants=" +
            $"{CollectionManager.DiscoveredVariantCount}"
        );
    }

    private void RemoveBoosterPack()
    {
        if (!IsServer)
            return;
        GetComponent<NetworkItemConsumption>().ConsumeServer();
    }
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
