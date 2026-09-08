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
    private const string RequestMessageName = "LethalCards.CollectionRequest";
    private static bool receivedSnapshot;
    private static float nextRequestTime;

    public static void Shutdown()
    {
        if (currentManager != null)
        {
            currentManager.OnClientConnectedCallback -= OnClientConnected;
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(MessageName);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(RequestMessageName);
        }
        currentManager = null;
        receivedSnapshot = false;
        nextRequestTime = 0f;
    }

    public static void EnsureSnapshot()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsConnectedClient || manager.IsServer || receivedSnapshot ||
            UnityEngine.Time.realtimeSinceStartup < nextRequestTime)
            return;
        nextRequestTime = UnityEngine.Time.realtimeSinceStartup + 2f;
        using FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
        manager.CustomMessagingManager.SendNamedMessage(RequestMessageName,
            NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private static void ReceiveSnapshotRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || !currentManager.IsServer ||
            !currentManager.ConnectedClients.ContainsKey(senderClientId))
            return;
        SendSnapshotToClient(senderClientId);
    }

    public static void Initialize()
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (manager == null || !manager.IsListening)
            return;

        // Already initialized for this NetworkManager.
        if (currentManager == manager)
            return;

        Shutdown();

        currentManager =
            manager;

        manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessageName, ReceiveSnapshotRequest);

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

        // A join can precede the first round update/load.
        CollectionSaveManager.LoadForCurrentSave();

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
        if (manager.IsServer || senderClientId != NetworkManager.ServerClientId)
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
        receivedSnapshot = true;

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
