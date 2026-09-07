using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;

namespace LethalCards.Cards;

public static class CollectionNetworkSync
{
    private const string MessageName =
        "LethalCards.CollectionSync";

    private static NetworkManager? currentManager;

    public static void Initialize()
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (manager == null)
            return;

        // Already initialized for this NetworkManager.
        if (currentManager == manager)
            return;

        // Clean up callbacks from an older session.
        if (currentManager != null)
        {
            currentManager.OnClientConnectedCallback -=
                OnClientConnected;

            if (currentManager.CustomMessagingManager != null)
            {
                currentManager.CustomMessagingManager
                    .UnregisterNamedMessageHandler(
                        MessageName
                    );
            }
        }

        currentManager =
            manager;

        manager.CustomMessagingManager
            .RegisterNamedMessageHandler(
                MessageName,
                ReceiveCollection
            );

        if (manager.IsServer)
        {
            manager.OnClientConnectedCallback +=
                OnClientConnected;

            Plugin.Log.LogInfo(
                "COLLECTION NETWORK | " +
                "Server synchronization initialized."
            );
        }
        else
        {
            Plugin.Log.LogInfo(
                "COLLECTION NETWORK | " +
                "Client synchronization initialized."
            );
        }
    }

    private static void OnClientConnected(
        ulong clientId)
    {
        if (
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // Host does not need a copy of its own data.
        if (
            clientId ==
            NetworkManager.ServerClientId)
        {
            return;
        }

        Plugin.Log.LogInfo(
            $"COLLECTION NETWORK | " +
            $"Client connected: {clientId}"
        );

        SendSnapshotToClient(
            clientId
        );
    }

    public static void BroadcastSnapshot()
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (
            manager == null ||
            !manager.IsServer ||
            !manager.IsListening)
        {
            return;
        }

        foreach (
            ulong clientId in
            manager.ConnectedClientsIds)
        {
            if (
                clientId ==
                NetworkManager.ServerClientId)
            {
                continue;
            }

            SendSnapshotToClient(
                clientId
            );
        }
    }

    private static void SendSnapshotToClient(
        ulong clientId)
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (
            manager == null ||
            !manager.IsServer)
        {
            return;
        }

        string cardData =
            string.Join(
                "\n",
                CollectionManager
                    .GetDiscoveredCards()
                    .OrderBy(x => x)
            );

        string variantData =
            string.Join(
                "\n",
                CollectionManager
                    .GetDiscoveredVariants()
                    .OrderBy(x => x)
            );

        using FastBufferWriter writer =
            new FastBufferWriter(
                8192,
                Allocator.Temp
            );

        writer.WriteValueSafe(
            cardData
        );

        writer.WriteValueSafe(
            variantData
        );

        manager.CustomMessagingManager
            .SendNamedMessage(
                MessageName,
                clientId,
                writer,
                NetworkDelivery.ReliableSequenced
            );

        Plugin.Log.LogInfo(
            $"COLLECTION NETWORK SEND | " +
            $"Client={clientId} | " +
            $"Cards={CollectionManager.DiscoveredCardCount} | " +
            $"Variants={CollectionManager.DiscoveredVariantCount}"
        );
    }

    private static void ReceiveCollection(
        ulong senderClientId,
        FastBufferReader reader)
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (manager == null)
            return;

        // Server never accepts collection state from clients.
        if (manager.IsServer)
            return;

        reader.ReadValueSafe(
            out string cardData
        );

        reader.ReadValueSafe(
            out string variantData
        );

        List<string> cards =
            SplitLines(
                cardData
            );

        List<string> variants =
            SplitLines(
                variantData
            );

        CollectionManager.LoadData(
            cards,
            variants
        );

        Plugin.Log.LogInfo(
            $"COLLECTION NETWORK RECEIVED | " +
            $"From={senderClientId} | " +
            $"Cards={CollectionManager.DiscoveredCardCount} | " +
            $"Variants={CollectionManager.DiscoveredVariantCount}"
        );
    }

    private static List<string> SplitLines(
        string value)
    {
        if (
            string.IsNullOrWhiteSpace(
                value
            ))
        {
            return new List<string>();
        }

        return value
            .Split(
                new[]
                {
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries
            )
            .ToList();
    }
}