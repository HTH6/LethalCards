using GameNetcodeStuff;
using Unity.Collections;
using Unity.Netcode;
using System;
using LethalCards.TerminalCommands;

namespace LethalCards.Grading;

public static class GradingNetworkSync
{
    private const string SubmitMessageName =
        "LethalCards.GradingSubmit";

    private static NetworkManager? currentManager;
    private const string StatusRequestName = "LethalCards.GradingStatusRequest";
    private const string StatusResponseName = "LethalCards.GradingStatusResponse";
    private static ulong nextStatusRequest;
    private static ulong pendingStatusRequest;
    private static Action<string>? pendingStatusCallback;

    internal static void CancelStatusRequest() => pendingStatusCallback = null;

    internal static bool RequestStatus(Action<string> callback)
    {
        Initialize();
        if (currentManager == null || !currentManager.IsConnectedClient || currentManager.IsServer)
            return false;
        pendingStatusRequest = ++nextStatusRequest;
        pendingStatusCallback = callback;
        using FastBufferWriter writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp);
        writer.WriteValueSafe(pendingStatusRequest);
        currentManager.CustomMessagingManager.SendNamedMessage(StatusRequestName,
            NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        Plugin.Log.LogInfo($"GRADING STATUS REQUEST | Requester={currentManager.LocalClientId}");
        return true;
    }

    private static void ReceiveStatusRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || !currentManager.IsServer ||
            !currentManager.ConnectedClients.ContainsKey(senderClientId) || reader.Length != sizeof(ulong))
            return;
        reader.ReadValueSafe(out ulong requestId);
        // Reuse the authoritative host formatter. No job objects or hidden grades are serialized.
        string display = CollectionTerminalPatch.BuildGradingText("grades");
        using FastBufferWriter writer = new FastBufferWriter(
            sizeof(ulong) + FastBufferWriter.GetWriteSize(display), Allocator.Temp);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe(display);
        currentManager.CustomMessagingManager.SendNamedMessage(StatusResponseName, senderClientId,
            writer, NetworkDelivery.ReliableFragmentedSequenced);
        Plugin.Log.LogInfo($"GRADING STATUS RESPONSE | Requester={senderClientId} | Jobs={GradingManager.Jobs.Count}");
    }

    private static void ReceiveStatusResponse(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || currentManager.IsServer || senderClientId != NetworkManager.ServerClientId ||
            pendingStatusCallback == null || reader.Length < sizeof(ulong))
            return;
        reader.ReadValueSafe(out ulong requestId);
        if (requestId != pendingStatusRequest)
            return;
        reader.ReadValueSafe(out string display);
        Action<string> callback = pendingStatusCallback;
        pendingStatusCallback = null;
        callback(display);
    }

    public static void Shutdown()
    {
        if (currentManager != null)
        {
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(SubmitMessageName);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(StatusRequestName);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(StatusResponseName);
        }
        CancelStatusRequest();
        currentManager = null;
    }

    public static void Initialize()
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (manager == null || !manager.IsListening)
            return;

        if (currentManager == manager)
            return;

        Shutdown();

        currentManager =
            manager;

        manager.CustomMessagingManager.RegisterNamedMessageHandler(StatusRequestName, ReceiveStatusRequest);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StatusResponseName, ReceiveStatusResponse);

        manager.CustomMessagingManager
            .RegisterNamedMessageHandler(
                SubmitMessageName,
                ReceiveSubmitRequest
            );

        Plugin.Log.LogInfo(
            $"GRADING NETWORK INITIALIZED | " +
            $"IsServer={manager.IsServer}"
        );
    }

    public static void RequestSubmission()
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (
            manager == null ||
            !manager.IsListening)
        {
            return;
        }

        if (manager.IsServer)
        {
            PlayerControllerB? localPlayer =
                GameNetworkManager.Instance?
                    .localPlayerController;

            if (localPlayer != null)
            {
                GradingPedestalBehaviour
                    .TrySubmitHeldCardServer(
                        localPlayer
                    );
            }

            return;
        }

        using FastBufferWriter writer =
            new FastBufferWriter(
                1,
                Allocator.Temp
            );

        manager.CustomMessagingManager
            .SendNamedMessage(
                SubmitMessageName,
                NetworkManager.ServerClientId,
                writer,
                NetworkDelivery.ReliableSequenced
            );

        Plugin.Log.LogInfo(
            $"GRADING SUBMIT REQUEST SENT | " +
            $"Client={manager.LocalClientId}"
        );
    }

    private static void ReceiveSubmitRequest(
        ulong senderClientId,
        FastBufferReader reader)
    {
        NetworkManager manager =
            NetworkManager.Singleton;

        if (
            manager == null ||
            !manager.IsServer)
        {
            return;
        }

        PlayerControllerB? player =
            FindPlayerByClientId(
                senderClientId
            );

        if (player == null)
        {
            Plugin.Log.LogWarning(
                $"GRADING SUBMIT REQUEST REJECTED | " +
                $"Client={senderClientId} | " +
                $"Reason=Player not found"
            );

            return;
        }

        Plugin.Log.LogInfo(
            $"GRADING SUBMIT REQUEST RECEIVED | " +
            $"Client={senderClientId} | " +
            $"Player={player.playerUsername}"
        );

        GradingPedestalBehaviour
            .TrySubmitHeldCardServer(
                player
            );
    }

    private static PlayerControllerB?
        FindPlayerByClientId(
            ulong clientId)
    {
        if (StartOfRound.Instance == null)
            return null;

        PlayerControllerB[] players =
            StartOfRound.Instance
                .allPlayerScripts;

        if (players == null)
            return null;

        foreach (
            PlayerControllerB player in players)
        {
            if (player == null)
                continue;

            if (
                player.actualClientId ==
                clientId)
            {
                return player;
            }
        }

        return null;
    }
}
