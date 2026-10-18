using System;
using System.Collections.Generic;
using LethalCards.Cards;
using LethalCards.ShipDisplay;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Museum;

/// <summary>
/// Bridges committed server-side ship display transactions into the persistent
/// museum ledger. It deliberately has no dependency on the Company scene model.
/// </summary>
internal static class CompanyMuseumShipDisplayIntegration
{
    private const float PersistenceRetryDelay = 10f;

    private static readonly Dictionary<string, CompanyMuseumRecord> Pending =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool subscribed;
    private static bool reconciledCurrentSession;
    private static float nextRetryTime;

    internal static void Initialize()
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
            return;

        CompanyMuseumSaveManager.LoadForCurrentSave();
        if (!CompanyMuseumSaveManager.HasLoadedCurrentSave)
            return;
        if (!subscribed)
        {
            ShipCardDisplayController.AuthoritativeSlotChanged += OnAuthoritativeSlotChanged;
            subscribed = true;
            Plugin.Log.LogInfo("COMPANY MUSEUM SHIP INTEGRATION INITIALIZED | Server=True");
        }

        if (!reconciledCurrentSession)
            ReconcileCurrentShipStorage();
    }

    internal static void Update()
    {
        if (!subscribed || Pending.Count == 0 ||
            NetworkManager.Singleton?.IsServer != true ||
            Time.realtimeSinceStartup < nextRetryTime)
        {
            return;
        }

        nextRetryTime = Time.realtimeSinceStartup + PersistenceRetryDelay;
        List<CompanyMuseumRecord> retry = new(Pending.Values);
        bool changed = false;
        foreach (CompanyMuseumRecord record in retry)
            changed |= TryCommit(record, "PersistenceRetry", false);
        if (changed)
            CompanyMuseumNetworkSync.BroadcastAuthoritativeSnapshot();
    }

    internal static void Shutdown()
    {
        if (subscribed)
            ShipCardDisplayController.AuthoritativeSlotChanged -= OnAuthoritativeSlotChanged;
        subscribed = false;
        reconciledCurrentSession = false;
        Pending.Clear();
        nextRetryTime = 0f;
    }

    private static void OnAuthoritativeSlotChanged(
        int slotIndex,
        string cardId,
        CardVariant variant,
        int grade,
        int ungradedValue,
        int finalValue,
        bool occupied)
    {
        // Empty payloads are withdrawals and crew-wipe clears. The controller
        // raises occupied payloads only after deposit/swap persistence and any
        // outgoing-card reconstruction have both succeeded.
        if (!occupied || NetworkManager.Singleton?.IsServer != true)
            return;

        CompanyMuseumRecord incoming = new(
            cardId,
            variant,
            grade,
            Math.Max(0, ungradedValue),
            Math.Max(0, finalValue));
        if (!CompanyMuseumManager.IsValid(incoming))
        {
            Plugin.Log.LogError(
                $"COMPANY MUSEUM SHIP REGISTRATION REJECTED | Slot={slotIndex + 1} | " +
                $"CardId={cardId} | Variant={variant} | Grade={grade} | Reason=Invalid authoritative payload");
            return;
        }

        if (TryCommit(incoming, $"ShipSlot{slotIndex + 1}", false))
            CompanyMuseumNetworkSync.BroadcastAuthoritativeSnapshot();
    }

    private static void ReconcileCurrentShipStorage()
    {
        reconciledCurrentSession = true;
        ShipCardDisplayStoredCard?[] storedCards = ShipCardDisplaySaveManager.LoadForCurrentSave();
        int occupied = 0;
        int changed = 0;
        foreach (ShipCardDisplayStoredCard? storedCard in storedCards)
        {
            if (storedCard == null)
                continue;
            occupied++;
            CompanyMuseumRecord record = new(
                storedCard.CardId,
                storedCard.Variant,
                storedCard.Grade,
                Math.Max(0, storedCard.UngradedValue),
                Math.Max(0, storedCard.FinalValue));
            if (TryCommit(record, "ShipSaveReconciliation", false))
                changed++;
        }

        if (changed > 0)
            CompanyMuseumNetworkSync.BroadcastAuthoritativeSnapshot();
        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM SHIP RECONCILIATION | OccupiedShipSlots={occupied} | " +
            $"MuseumChanges={changed} | PendingPersistence={Pending.Count}");
    }

    private static bool TryCommit(
        CompanyMuseumRecord incoming,
        string source,
        bool broadcast)
    {
        try
        {
            CompanyMuseumRegistrationResult result =
                CompanyMuseumManager.TryRegisterHighest(incoming, out CompanyMuseumRecord? committed);
            if (result is CompanyMuseumRegistrationResult.Registered or
                CompanyMuseumRegistrationResult.Upgraded)
            {
                Pending.Remove(incoming.Key);
                if (broadcast)
                    CompanyMuseumNetworkSync.BroadcastAuthoritativeSnapshot();
                Plugin.Log.LogInfo(
                    $"COMPANY MUSEUM SHIP REGISTRATION COMMITTED | Source={source} | " +
                    $"Result={result} | CardId={incoming.CardId} | Variant={incoming.Variant} | " +
                    $"Grade={committed?.Grade ?? incoming.Grade} | PendingPersistence={Pending.Count}");
                return true;
            }

            if (result == CompanyMuseumRegistrationResult.AlreadyRegistered)
            {
                Pending.Remove(incoming.Key);
                return false;
            }

            if (result == CompanyMuseumRegistrationResult.PersistenceFailed)
            {
                QueuePending(incoming);
                Plugin.Log.LogError(
                    $"COMPANY MUSEUM SHIP REGISTRATION PENDING | Source={source} | " +
                    $"CardId={incoming.CardId} | Variant={incoming.Variant} | Grade={incoming.Grade} | " +
                    $"ShipTransactionCommitted=True | MuseumSaved=False | RetrySeconds={PersistenceRetryDelay:F0} | " +
                    $"PendingPersistence={Pending.Count}");
                return false;
            }

            Plugin.Log.LogError(
                $"COMPANY MUSEUM SHIP REGISTRATION REJECTED | Source={source} | " +
                $"CardId={incoming.CardId} | Variant={incoming.Variant} | Grade={incoming.Grade} | Result={result}");
            return false;
        }
        catch (Exception exception)
        {
            QueuePending(incoming);
            Plugin.Log.LogError(
                $"COMPANY MUSEUM SHIP REGISTRATION EXCEPTION | Source={source} | " +
                $"CardId={incoming.CardId} | Variant={incoming.Variant} | Grade={incoming.Grade} | " +
                $"ShipTransactionCommitted=True | MuseumSaved=False | PendingPersistence={Pending.Count} | {exception}");
            return false;
        }
    }

    private static void QueuePending(CompanyMuseumRecord incoming)
    {
        if (!Pending.TryGetValue(incoming.Key, out CompanyMuseumRecord? pending) ||
            incoming.Grade > pending.Grade)
        {
            Pending[incoming.Key] = incoming.Clone();
        }
        nextRetryTime = Time.realtimeSinceStartup + PersistenceRetryDelay;
    }
}
