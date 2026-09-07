using System.Collections.Generic;
using Unity.Netcode;

namespace LethalCards.Cards;

public static class CollectionManager
{
    private static readonly HashSet<string> DiscoveredCards = new();

    private static readonly HashSet<string> DiscoveredVariants = new();

    public static void RegisterPull(CardPull pull)
    {
        if (pull.Card == null)
            return;

        string cardId = pull.Card.CardId;

        bool newCard =
            DiscoveredCards.Add(cardId);

        string variantKey =
            $"{cardId}:{pull.Variant}";

        bool newVariant =
            DiscoveredVariants.Add(variantKey);

        if (newCard)
        {
            Plugin.Log.LogInfo(
                $"COLLECTION: NEW CARD | " +
                $"{cardId} | " +
                $"{pull.Card.DisplayName}"
            );
        }

        if (newVariant)
        {
            Plugin.Log.LogInfo(
                $"COLLECTION: NEW VARIANT | " +
                $"{cardId} | " +
                $"{pull.Variant}"
            );
        }

        if (newCard || newVariant)
        {
            // The server owns the authoritative
            // shared collection and save file.
            if (
                NetworkManager.Singleton == null ||
                NetworkManager.Singleton.IsServer)
            {
                CollectionSaveManager.Save();

                CollectionNetworkSync
                    .BroadcastSnapshot();
            }
        }
    }

    public static bool HasCard(string cardId)
    {
        return DiscoveredCards.Contains(cardId);
    }

    public static bool HasVariant(
        string cardId,
        CardVariant variant)
    {
        return DiscoveredVariants.Contains(
            $"{cardId}:{variant}"
        );
    }

    public static int DiscoveredCardCount =>
        DiscoveredCards.Count;

    public static int DiscoveredVariantCount =>
        DiscoveredVariants.Count;

    public static int TotalCardCount =>
        CardDatabase.Cards.Count;

    public static IEnumerable<string> GetDiscoveredCards()
    {
        return DiscoveredCards;
    }

    public static IEnumerable<string> GetDiscoveredVariants()
    {
        return DiscoveredVariants;
    }

    public static void LoadData(
        IEnumerable<string> cards,
        IEnumerable<string> variants)
    {
        DiscoveredCards.Clear();
        DiscoveredVariants.Clear();

        foreach (string card in cards)
        {
            if (!string.IsNullOrWhiteSpace(card))
                DiscoveredCards.Add(card);
        }

        foreach (string variant in variants)
        {
            if (!string.IsNullOrWhiteSpace(variant))
                DiscoveredVariants.Add(variant);
        }

        Plugin.Log.LogInfo(
            $"COLLECTION LOADED | " +
            $"Cards={DiscoveredCardCount} | " +
            $"Variants={DiscoveredVariantCount}"
        );
    }

    public static void Clear()
    {
        DiscoveredCards.Clear();
        DiscoveredVariants.Clear();
    }
}