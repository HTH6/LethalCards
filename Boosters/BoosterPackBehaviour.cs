using LethalCards.Cards;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Boosters;

public class BoosterPackBehaviour : PhysicsProp
{
    public BoosterType PackType = BoosterType.Light;

    // Server-authoritative state.
    private bool opened;

    // Prevent a client from sending many RPC requests
    // while waiting for the server to process the first one.
    private bool openRequestSent;

    public override void ItemActivate(
        bool used,
        bool buttonDown = true)
    {
        base.ItemActivate(
            used,
            buttonDown
        );

        if (!buttonDown)
            return;

        // Host/server can process the opening immediately.
        if (IsServer)
        {
            TryOpenPackServer();
            return;
        }

        // Clients request that the server open the pack.
        if (openRequestSent)
            return;

        openRequestSent = true;

        Plugin.Log.LogInfo(
            $"BOOSTER OPEN REQUEST | " +
            $"Type={PackType} | " +
            $"Client={NetworkManager.Singleton?.LocalClientId}"
        );

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

        TryOpenPackServer();
    }

    private void TryOpenPackServer()
    {
        if (!IsServer)
            return;

        if (opened)
        {
            Plugin.Log.LogInfo(
                $"BOOSTER OPEN REJECTED | " +
                $"Type={PackType} | " +
                $"Reason=Already opened"
            );

            return;
        }

        opened = true;

        Plugin.Log.LogInfo(
            $"BOOSTER OPEN ACCEPTED | " +
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

        // Only the server spawns physical card objects.
        SpawnPackContents(
            result
        );

        // Only the server removes the physical booster.
        RemoveBoosterPack();
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

    private void SpawnPackContents(
        PackResult result)
    {
        Vector3 spawnOrigin;
        Vector3 spawnRight;

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

        foreach (CardPull pull in result.Cards)
        {
            SpawnCard(
                pull,
                spawnOrigin,
                spawnRight
            );
        }
    }

    private void SpawnCard(
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

        NetworkObject networkObject =
            GetComponent<NetworkObject>();

        if (
            networkObject != null &&
            networkObject.IsSpawned)
        {
            networkObject.Despawn(
                true
            );
        }
        else
        {
            Destroy(
                gameObject
            );
        }
    }
}