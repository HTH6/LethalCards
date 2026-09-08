using GameNetcodeStuff;
using Unity.Collections;
using Unity.Netcode;

namespace LethalCards.Grading;

public static class GradingNetworkSync
{
    private const string SubmitMessageName =
        "LethalCards.GradingSubmit";

    private static NetworkManager? currentManager;

    public static void Shutdown()
    {
        if (currentManager != null)
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(SubmitMessageName);
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
