using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameNetcodeStuff;
using LethalCards.Cards;
using Unity.Collections;
using Unity.Netcode;

namespace LethalCards.Museum;

internal static class CompanyMuseumNetworkSync
{
    private const string SnapshotMessage = "LethalCards.MuseumSnapshot";
    private const string SnapshotRequestMessage = "LethalCards.MuseumSnapshotRequest";
    private const string RegistrationRequestMessage = "LethalCards.MuseumRegisterRequest";
    private const string RegistrationResultMessage = "LethalCards.MuseumRegisterResult";

    private static NetworkManager? currentManager;
    private static bool receivedSnapshot;
    private static float nextSnapshotRequestTime;

    internal static void Initialize()
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || currentManager == manager)
            return;

        Shutdown();
        currentManager = manager;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            SnapshotMessage, ReceiveSnapshot);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            SnapshotRequestMessage, ReceiveSnapshotRequest);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            RegistrationRequestMessage, ReceiveRegistrationRequest);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(
            RegistrationResultMessage, ReceiveRegistrationResult);
        if (manager.IsServer)
        {
            manager.OnClientConnectedCallback += OnClientConnected;
            CompanyMuseumSaveManager.LoadForCurrentSave();
        }

        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM NETWORK INITIALIZED | Server={manager.IsServer} | " +
            $"Host={manager.IsHost} | Client={manager.IsClient}");
    }

    internal static void EnsureSnapshot()
    {
        NetworkManager? manager = currentManager;
        if (manager == null || manager.IsServer || !manager.IsConnectedClient ||
            receivedSnapshot || UnityEngine.Time.realtimeSinceStartup < nextSnapshotRequestTime)
            return;
        nextSnapshotRequestTime = UnityEngine.Time.realtimeSinceStartup + 2f;
        using FastBufferWriter writer = new(1, Allocator.Temp);
        manager.CustomMessagingManager.SendNamedMessage(
            SnapshotRequestMessage,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    internal static void BroadcastAuthoritativeSnapshot()
    {
        if (currentManager?.IsServer != true)
            return;
        try
        {
            BroadcastSnapshot();
        }
        catch (Exception exception)
        {
            // Persistence has already committed at this point. A connected
            // client can request the same authoritative snapshot again.
            Plugin.Log.LogError($"COMPANY MUSEUM SYNC BROADCAST FAILED | {exception}");
        }
    }

    internal static void RequestRegistration()
    {
        NetworkManager? manager = currentManager;
        if (manager == null || !manager.IsListening)
            return;
        if (manager.IsServer)
        {
            CompanyMuseumDisplaySpawner.Controller?.QueueServerRegistration(
                manager.LocalClientId);
            return;
        }

        using FastBufferWriter writer = new(1, Allocator.Temp);
        manager.CustomMessagingManager.SendNamedMessage(
            RegistrationRequestMessage,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    internal static CompanyMuseumRegistrationResult ProcessAuthoritativeRegistration(
        ulong senderClientId,
        CompanyMuseumDisplayController controller)
    {
        NetworkManager? manager = currentManager;
        if (manager == null || !manager.IsServer ||
            !TryResolvePlayer(senderClientId, out PlayerControllerB player) ||
            !controller.ValidateServerInteraction(player))
        {
            SendResult(senderClientId, CompanyMuseumRegistrationResult.InvalidPlayerOrDistance);
            return CompanyMuseumRegistrationResult.InvalidPlayerOrDistance;
        }

        if (!TryGetAuthoritativeSelectedCard(
                player,
                out CardInstanceData cardData,
                out CardDefinition definition))
        {
            SendResult(senderClientId, CompanyMuseumRegistrationResult.InvalidHeldItem);
            return CompanyMuseumRegistrationResult.InvalidHeldItem;
        }

        CompanyMuseumRecord incoming = new(
            definition.CardId,
            cardData.Variant,
            cardData.Grade,
            Math.Max(0, cardData.UngradedValue),
            Math.Max(0, cardData.FinalValue));
        CompanyMuseumRegistrationResult result =
            CompanyMuseumManager.TryRegisterHighest(incoming, out CompanyMuseumRecord? existing);

        if (result is CompanyMuseumRegistrationResult.Registered or
            CompanyMuseumRegistrationResult.Upgraded)
        {
            BroadcastSnapshot();
        }
        else if (result == CompanyMuseumRegistrationResult.AlreadyRegistered)
        {
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM REGISTRATION REJECTED | Player={player.playerUsername} | " +
                $"CardId={definition.CardId} | Variant={cardData.Variant} | OfferedGrade={cardData.Grade} | " +
                $"ExistingGrade={existing?.Grade ?? -1}");
        }

        SendResult(senderClientId, result);
        return result;
    }

    internal static bool IsAuthoritativeSelectedCardReady(ulong senderClientId)
    {
        return currentManager?.IsServer == true &&
               TryResolvePlayer(senderClientId, out PlayerControllerB player) &&
               TryGetAuthoritativeSelectedCard(player, out _, out _);
    }

    internal static void Shutdown()
    {
        if (currentManager != null)
        {
            currentManager.OnClientConnectedCallback -= OnClientConnected;
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(SnapshotMessage);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(SnapshotRequestMessage);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(RegistrationRequestMessage);
            currentManager.CustomMessagingManager?.UnregisterNamedMessageHandler(RegistrationResultMessage);
        }
        currentManager = null;
        receivedSnapshot = false;
        nextSnapshotRequestTime = 0f;
    }

    private static bool TryGetAuthoritativeSelectedCard(
        PlayerControllerB player,
        out CardInstanceData cardData,
        out CardDefinition definition)
    {
        cardData = null!;
        definition = null!;
        if (player == null || !player.isPlayerControlled || player.isPlayerDead ||
            player.ItemSlots == null || player.currentItemSlot < 0 ||
            player.currentItemSlot >= player.ItemSlots.Length)
            return false;

        // ItemSlots[currentItemSlot] is the selected-slot authority. Vanilla's
        // currentlyHeldObjectServer can remain stale after switching between
        // two occupied slots, so it is intentionally not used as identity proof.
        // Ownership and held state below are all server-observed fields.
        GrabbableObject? selected = player.ItemSlots[player.currentItemSlot];
        if (selected == null || !selected.IsSpawned ||
            !selected.heldByPlayerOnServer || !selected.isHeld ||
            selected.OwnerClientId != player.actualClientId ||
            selected.playerHeldBy != player)
            return false;

        CardInstanceData? data = selected.GetComponent<CardInstanceData>();
        CardDefinition? card = data == null ? null : CardDatabase.GetById(data.CardId);
        if (data == null || card == null || card.SetNumber < 1 ||
            card.SetNumber > CompanyMuseumManager.CardSlotsPerVariant ||
            data.Grade < 0 || data.Grade > 10 ||
            !Enum.IsDefined(typeof(CardVariant), data.Variant))
            return false;

        cardData = data;
        definition = card;
        return true;
    }

    private static void ReceiveRegistrationRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || !currentManager.IsServer ||
            !currentManager.ConnectedClients.ContainsKey(senderClientId))
            return;
        CompanyMuseumDisplayController? controller = CompanyMuseumDisplaySpawner.Controller;
        if (controller == null)
        {
            SendResult(senderClientId, CompanyMuseumRegistrationResult.InvalidPlayerOrDistance);
            return;
        }
        controller.QueueServerRegistration(senderClientId);
    }

    private static void ReceiveRegistrationResult(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || currentManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
            return;
        reader.ReadValueSafe(out int value);
        if (Enum.IsDefined(typeof(CompanyMuseumRegistrationResult), value))
            CompanyMuseumDisplayController.ShowResult((CompanyMuseumRegistrationResult)value);
    }

    private static void SendResult(ulong clientId, CompanyMuseumRegistrationResult result)
    {
        NetworkManager? manager = currentManager;
        if (manager == null || !manager.IsServer)
            return;
        if (clientId == NetworkManager.ServerClientId)
        {
            CompanyMuseumDisplayController.ShowResult(result);
            return;
        }
        if (!manager.ConnectedClients.ContainsKey(clientId))
            return;
        using FastBufferWriter writer = new(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe((int)result);
        manager.CustomMessagingManager.SendNamedMessage(
            RegistrationResultMessage,
            clientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private static void OnClientConnected(ulong clientId)
    {
        if (clientId != NetworkManager.ServerClientId)
            SendSnapshot(clientId);
    }

    private static void ReceiveSnapshotRequest(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager?.IsServer == true &&
            currentManager.ConnectedClients.ContainsKey(senderClientId))
            SendSnapshot(senderClientId);
    }

    private static void BroadcastSnapshot()
    {
        NetworkManager? manager = currentManager;
        if (manager == null || !manager.IsServer)
            return;
        foreach (ulong clientId in manager.ConnectedClientsIds)
            if (clientId != NetworkManager.ServerClientId)
                SendSnapshot(clientId);
    }

    private static void SendSnapshot(ulong clientId)
    {
        NetworkManager? manager = currentManager;
        if (manager == null || !manager.IsServer)
            return;
        CompanyMuseumSaveManager.LoadForCurrentSave();
        string payload = Serialize(CompanyMuseumManager.GetRecords());
        using FastBufferWriter writer = new(
            FastBufferWriter.GetWriteSize(payload), Allocator.Temp);
        writer.WriteValueSafe(payload);
        manager.CustomMessagingManager.SendNamedMessage(
            SnapshotMessage,
            clientId,
            writer,
            NetworkDelivery.ReliableFragmentedSequenced);
        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM SYNC SENT | Client={clientId} | " +
            $"Entries={CompanyMuseumManager.DiscoveryCount}/{CompanyMuseumManager.TotalEntries}");
    }

    private static void ReceiveSnapshot(ulong senderClientId, FastBufferReader reader)
    {
        if (currentManager == null || currentManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
            return;
        reader.ReadValueSafe(out string payload);
        CompanyMuseumManager.ReplaceFromServerSnapshot(Deserialize(payload));
        receivedSnapshot = true;
    }

    private static string Serialize(IEnumerable<CompanyMuseumRecord> records) =>
        string.Join("\n", records
            .OrderBy(record => CardDatabase.GetById(record.CardId)?.SetNumber ?? int.MaxValue)
            .ThenBy(record => (int)record.Variant)
            .Select(record => string.Join("|",
                record.CardId,
                ((int)record.Variant).ToString(CultureInfo.InvariantCulture),
                record.Grade.ToString(CultureInfo.InvariantCulture),
                record.UngradedValue.ToString(CultureInfo.InvariantCulture),
                record.FinalValue.ToString(CultureInfo.InvariantCulture))));

    private static IEnumerable<CompanyMuseumRecord> Deserialize(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            yield break;
        foreach (string line in payload.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('|');
            if (parts.Length != 5 ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int variant) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ungraded) ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int finalValue) ||
                !Enum.IsDefined(typeof(CardVariant), variant))
                continue;
            CompanyMuseumRecord record = new(
                parts[0], (CardVariant)variant, grade, ungraded, finalValue);
            if (CompanyMuseumManager.IsValid(record))
                yield return record;
        }
    }

    private static bool TryResolvePlayer(ulong clientId, out PlayerControllerB player)
    {
        player = null!;
        PlayerControllerB[]? players = StartOfRound.Instance?.allPlayerScripts;
        if (players == null)
            return false;
        foreach (PlayerControllerB candidate in players)
        {
            if (candidate != null && candidate.actualClientId == clientId)
            {
                player = candidate;
                return true;
            }
        }
        return false;
    }
}
