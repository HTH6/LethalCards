using GameNetcodeStuff;
using Unity.Collections;
using Unity.Netcode;
using System;
using LethalCards.Cards;
using LethalCards.TerminalCommands;

namespace LethalCards.Grading;

public static class GradingNetworkSync
{
    private const string SubmitMessageName =
        "LethalCards.GradingSubmit";
    private const string PresentationSuccessName = "LethalCards.GradingPresentationSuccess";
    private const string PresentationFailureName = "LethalCards.GradingPresentationFailure";

    private static NetworkManager? currentManager;
    private const string StatusRequestName = "LethalCards.GradingStatusRequest";
    private const string StatusResponseName = "LethalCards.GradingStatusResponse";
    private const int StatusRequestPayloadSize = sizeof(ulong);
    private const int StatusResponseFixedPayloadSize = sizeof(ulong) + sizeof(int);
    private static ulong nextStatusRequest;
    private static ulong pendingStatusRequest;
    private static Action<int, string>? pendingStatusCallback;

    internal static void CancelStatusRequest() => pendingStatusCallback = null;

    internal static bool RequestStatus(Action<int, string> callback)
    {
        Initialize();
        if (currentManager == null)
        {
            Plugin.Log.LogWarning("GRADING REQUEST BLOCKED | Reason=NetworkNotInitialized");
            return false;
        }
        if (!currentManager.IsConnectedClient)
        {
            Plugin.Log.LogWarning("GRADING REQUEST BLOCKED | Reason=ClientNotConnected");
            return false;
        }
        if (currentManager.IsServer)
        {
            Plugin.Log.LogWarning("GRADING REQUEST BLOCKED | Reason=ServerUsesAuthoritativeLocalQueue");
            return false;
        }
        pendingStatusRequest = ++nextStatusRequest;
        pendingStatusCallback = callback;
        using FastBufferWriter writer = new FastBufferWriter(StatusRequestPayloadSize, Allocator.Temp);
        writer.WriteValueSafe(pendingStatusRequest);
        currentManager.CustomMessagingManager.SendNamedMessage(StatusRequestName,
            NetworkManager.ServerClientId, writer, NetworkDelivery.ReliableSequenced);
        Plugin.Log.LogInfo(
            $"GRADING CLIENT REQUEST SENT | LocalClientId={currentManager.LocalClientId} | " +
            $"MessageName={StatusRequestName}");
        return true;
    }

    private static void ReceiveStatusRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null)
        {
            Plugin.Log.LogWarning("GRADING REQUEST BLOCKED | Reason=NetworkNotInitialized");
            return;
        }
        if (!currentManager.IsServer)
        {
            Plugin.Log.LogWarning($"GRADING REQUEST BLOCKED | Reason=ReceiverNotServer | SenderClientId={senderClientId}");
            return;
        }
        if (!currentManager.ConnectedClients.ContainsKey(senderClientId))
        {
            Plugin.Log.LogWarning($"GRADING REQUEST BLOCKED | Reason=SenderNotConnected | SenderClientId={senderClientId}");
            return;
        }
        int payloadLength = reader.Length - reader.Position;
        if (payloadLength != StatusRequestPayloadSize)
        {
            Plugin.Log.LogWarning(
                $"GRADING REQUEST BLOCKED | Reason=InvalidPayloadLength | SenderClientId={senderClientId} | " +
                $"ExpectedLength={StatusRequestPayloadSize} | ActualLength={payloadLength} | " +
                $"ReaderLength={reader.Length} | ReaderPosition={reader.Position}");
            return;
        }
        reader.ReadValueSafe(out ulong requestId);
        if (requestId == 0)
        {
            Plugin.Log.LogWarning(
                $"GRADING REQUEST BLOCKED | Reason=InvalidRequestId | SenderClientId={senderClientId} | RequestId={requestId}");
            return;
        }
        int jobCount = GradingManager.Jobs.Count;
        Plugin.Log.LogInfo(
            $"GRADING HOST REQUEST RECEIVED | SenderClientId={senderClientId} | " +
            $"MessageName={StatusRequestName} | RuntimeJobCount={jobCount}");
        // Reuse the authoritative host formatter. No job objects or hidden grades are serialized.
        string display = CollectionTerminalPatch.BuildGradingText("grades");
        using FastBufferWriter writer = new FastBufferWriter(
            StatusResponseFixedPayloadSize + FastBufferWriter.GetWriteSize(display), Allocator.Temp);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe(jobCount);
        writer.WriteValueSafe(display);
        currentManager.CustomMessagingManager.SendNamedMessage(StatusResponseName, senderClientId,
            writer, NetworkDelivery.ReliableFragmentedSequenced);
        Plugin.Log.LogInfo(
            $"GRADING HOST RESPONSE SENT | TargetClientId={senderClientId} | JobCount={jobCount} | " +
            $"MessageName={StatusResponseName}");
    }

    private static void ReceiveStatusResponse(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null)
        {
            Plugin.Log.LogWarning("GRADING RESPONSE BLOCKED | Reason=NetworkNotInitialized");
            return;
        }
        if (currentManager.IsServer)
        {
            Plugin.Log.LogWarning($"GRADING RESPONSE BLOCKED | Reason=ReceiverIsServer | SenderClientId={senderClientId}");
            return;
        }
        if (senderClientId != NetworkManager.ServerClientId)
        {
            Plugin.Log.LogWarning($"GRADING RESPONSE BLOCKED | Reason=SenderIsNotServer | SenderClientId={senderClientId}");
            return;
        }
        if (pendingStatusCallback == null)
        {
            Plugin.Log.LogWarning("GRADING RESPONSE BLOCKED | Reason=NoPendingRequest");
            return;
        }
        int payloadLength = reader.Length - reader.Position;
        if (payloadLength < StatusResponseFixedPayloadSize)
        {
            Plugin.Log.LogWarning(
                $"GRADING RESPONSE BLOCKED | Reason=InvalidPayloadLength | " +
                $"MinimumExpectedLength={StatusResponseFixedPayloadSize} | ActualLength={payloadLength} | " +
                $"ReaderLength={reader.Length} | ReaderPosition={reader.Position}");
            return;
        }
        reader.ReadValueSafe(out ulong requestId);
        if (requestId != pendingStatusRequest)
        {
            Plugin.Log.LogWarning(
                $"GRADING RESPONSE BLOCKED | Reason=RequestIdMismatch | Expected={pendingStatusRequest} | Actual={requestId}");
            return;
        }
        reader.ReadValueSafe(out int jobCount);
        reader.ReadValueSafe(out string display);
        Plugin.Log.LogInfo(
            $"GRADING CLIENT RESPONSE RECEIVED | LocalClientId={currentManager.LocalClientId} | " +
            $"JobCount={jobCount} | MessageName={StatusResponseName}");
        Action<int, string> callback = pendingStatusCallback;
        pendingStatusCallback = null;
        callback(jobCount, display);
    }

    public static void Shutdown()
    {
        if (currentManager != null)
        {
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(SubmitMessageName);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(StatusRequestName);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(StatusResponseName);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(PresentationSuccessName);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(PresentationFailureName);
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
        manager.CustomMessagingManager.RegisterNamedMessageHandler(PresentationSuccessName, ReceivePresentationSuccess);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(PresentationFailureName, ReceivePresentationFailure);

        manager.CustomMessagingManager
            .RegisterNamedMessageHandler(
                SubmitMessageName,
                ReceiveSubmitRequest
            );

        Plugin.Log.LogInfo(
            $"GRADING NETWORK INITIALIZED | " +
            $"IsServer={manager.IsServer} | IsHost={manager.IsHost} | IsClient={manager.IsClient} | " +
            $"LocalClientId={manager.LocalClientId} | StatusHandlersRegistered=True"
        );
    }

    internal static void BroadcastSubmissionPresentation(string cardId, CardVariant variant)
    {
        NetworkManager? manager = currentManager;
        if (manager == null || !manager.IsServer || !manager.IsListening)
            return;

        GradingPedestalSpawner.PlaySubmissionAnimation(cardId, variant);
        foreach (ulong clientId in manager.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId)
                continue;
            using FastBufferWriter writer = new(512, Allocator.Temp);
            writer.WriteValueSafe(cardId);
            writer.WriteValueSafe((int)variant);
            manager.CustomMessagingManager.SendNamedMessage(
                PresentationSuccessName, clientId, writer, NetworkDelivery.ReliableSequenced);
        }
    }

    internal static void SendInsufficientCreditsPresentation(ulong clientId)
    {
        NetworkManager? manager = currentManager;
        if (manager == null || !manager.IsServer || !manager.IsListening)
            return;

        if (clientId == NetworkManager.ServerClientId)
        {
            GradingPedestalSpawner.PlayInsufficientCreditsAnimation();
            return;
        }

        if (!manager.ConnectedClients.ContainsKey(clientId))
            return;
        using FastBufferWriter writer = new(1, Allocator.Temp);
        manager.CustomMessagingManager.SendNamedMessage(
            PresentationFailureName, clientId, writer, NetworkDelivery.ReliableSequenced);
    }

    private static void ReceivePresentationSuccess(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || currentManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
            return;
        reader.ReadValueSafe(out string cardId);
        reader.ReadValueSafe(out int variant);
        if (!System.Enum.IsDefined(typeof(CardVariant), variant))
            return;
        GradingPedestalSpawner.PlaySubmissionAnimation(cardId, (CardVariant)variant);
    }

    private static void ReceivePresentationFailure(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || currentManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
            return;
        GradingPedestalSpawner.PlayInsufficientCreditsAnimation();
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

        // Plugin.Log.LogInfo(
        //     $"GRADING SUBMIT REQUEST SENT | " +
        //     $"Client={manager.LocalClientId}"
        // );
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

        // Plugin.Log.LogInfo(
        //     $"GRADING SUBMIT REQUEST RECEIVED | " +
        //     $"Client={senderClientId} | " +
        //     $"Player={player.playerUsername}"
        // );

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
