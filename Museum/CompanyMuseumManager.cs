using System;
using System.Collections.Generic;
using LethalCards.Cards;
using Unity.Netcode;

namespace LethalCards.Museum;

internal static class CompanyMuseumManager
{
    internal const int CardSlotsPerVariant = 33;
    internal const int VariantCount = 4;
    internal const int TotalEntries = CardSlotsPerVariant * VariantCount;

    private static readonly Dictionary<string, CompanyMuseumRecord> Records =
        new(StringComparer.OrdinalIgnoreCase);

    internal static event Action<CompanyMuseumRecord?>? Changed;

    internal static int DiscoveryCount => Records.Count;

    internal static bool TryGet(
        string cardId,
        CardVariant variant,
        out CompanyMuseumRecord record)
    {
        if (Records.TryGetValue(
                CompanyMuseumRecord.CreateKey(cardId, variant),
                out CompanyMuseumRecord? existing))
        {
            record = existing;
            return true;
        }

        record = null!;
        return false;
    }

    internal static IReadOnlyCollection<CompanyMuseumRecord> GetRecords() =>
        Records.Values;

    internal static CompanyMuseumRegistrationResult TryRegisterHighest(
        CompanyMuseumRecord incoming,
        out CompanyMuseumRecord? committed)
    {
        committed = null;
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer ||
            !IsValid(incoming))
        {
            return CompanyMuseumRegistrationResult.InvalidHeldItem;
        }

        CompanyMuseumSaveManager.LoadForCurrentSave();
        if (TryGet(incoming.CardId, incoming.Variant, out CompanyMuseumRecord existing) &&
            existing.Grade >= incoming.Grade)
        {
            committed = existing;
            return CompanyMuseumRegistrationResult.AlreadyRegistered;
        }

        Dictionary<string, CompanyMuseumRecord> candidate = CloneRecords();
        candidate[incoming.Key] = incoming.Clone();
        if (!CompanyMuseumSaveManager.TrySave(candidate.Values))
            return CompanyMuseumRegistrationResult.PersistenceFailed;

        bool upgrade = Records.ContainsKey(incoming.Key);
        Records[incoming.Key] = incoming.Clone();
        committed = Records[incoming.Key];
        NotifyChanged(committed);
        Plugin.Log.LogAlways(
            $"COMPANY MUSEUM {(upgrade ? "UPGRADED" : "REGISTERED")} | " +
            $"CardId={incoming.CardId} | Variant={incoming.Variant} | Grade={incoming.Grade} | " +
            $"Discovered={DiscoveryCount}/{TotalEntries}");
        return upgrade
            ? CompanyMuseumRegistrationResult.Upgraded
            : CompanyMuseumRegistrationResult.Registered;
    }

    internal static void ReplaceFromPersistence(
        IEnumerable<CompanyMuseumRecord> records)
    {
        Replace(records);
        NotifyChanged(null);
    }

    internal static void ReplaceFromServerSnapshot(
        IEnumerable<CompanyMuseumRecord> records)
    {
        NetworkManager? manager = NetworkManager.Singleton;
        if (manager != null && manager.IsServer)
            return;
        Replace(records);
        NotifyChanged(null);
        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM SYNC RECEIVED | Discovered={DiscoveryCount}/{TotalEntries}");
    }

    internal static void ClearRuntime()
    {
        Records.Clear();
        NotifyChanged(null);
    }

    internal static bool IsValid(CompanyMuseumRecord record)
    {
        CardDefinition? definition = CardDatabase.GetById(record.CardId);
        return definition != null &&
               definition.SetNumber >= 1 &&
               definition.SetNumber <= CardSlotsPerVariant &&
               Enum.IsDefined(typeof(CardVariant), record.Variant) &&
               record.Grade >= 0 && record.Grade <= 10 &&
               record.UngradedValue >= 0 && record.FinalValue >= 0;
    }

    private static void Replace(IEnumerable<CompanyMuseumRecord> records)
    {
        Records.Clear();
        foreach (CompanyMuseumRecord record in records)
        {
            if (!IsValid(record))
                continue;
            if (!Records.TryGetValue(record.Key, out CompanyMuseumRecord? existing) ||
                record.Grade > existing.Grade)
            {
                Records[record.Key] = record.Clone();
            }
        }
    }

    private static Dictionary<string, CompanyMuseumRecord> CloneRecords()
    {
        Dictionary<string, CompanyMuseumRecord> clone =
            new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, CompanyMuseumRecord value) in Records)
            clone[key] = value.Clone();
        return clone;
    }

    private static void NotifyChanged(CompanyMuseumRecord? changed)
    {
        if (Changed == null)
            return;
        foreach (Delegate callback in Changed.GetInvocationList())
        {
            try
            {
                ((Action<CompanyMuseumRecord?>)callback)(changed);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"COMPANY MUSEUM PRESENTATION UPDATE FAILED | " +
                    $"CardId={changed?.CardId ?? "Snapshot"} | Variant={changed?.Variant.ToString() ?? "All"} | {exception}");
            }
        }
    }
}
