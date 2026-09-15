using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;

namespace LethalCards.Cards;

public static partial class CardDatabase
{
    public static readonly List<CardDefinition> Cards = new();

    static CardDatabase()
    {
        foreach (CardDefinition card in CreateV1Roster())
            Add(card);
    }

    public static void Add(CardDefinition card)
    {
        if (string.IsNullOrWhiteSpace(card.CardId) || string.IsNullOrWhiteSpace(card.SetId) ||
            card.SetNumber <= 0 || NormalizeName(card.DisplayName).Length == 0 ||
            !Enum.IsDefined(typeof(CardRarity), card.Rarity))
            throw new ArgumentException("Invalid card metadata.", nameof(card));
        if (Cards.Exists(existing =>
            string.Equals(existing.CardId, card.CardId, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(existing.SetId, card.SetId, StringComparison.OrdinalIgnoreCase) &&
             existing.SetNumber == card.SetNumber) ||
            NormalizeName(existing.DisplayName) == NormalizeName(card.DisplayName)))
            throw new ArgumentException($"Duplicate card ID, set number, or normalized name: {card.CardId}");
        Cards.Add(card);
    }

    public static IEnumerable<CardDefinition> GetImplementedCards() =>
        Cards.Where(card => card.IsImplemented && card.ItemAsset != null && card.ItemAsset.spawnPrefab != null);

    public static string NormalizeName(string name)
    {
        StringBuilder result = new();
        foreach (char character in name)
            if (char.IsLetterOrDigit(character))
                result.Append(char.ToUpperInvariant(character));
        return result.ToString();
    }

    public static CardDefinition? GetByName(string name) =>
        Cards.Find(card => NormalizeName(card.DisplayName) == NormalizeName(name));

    public static IEnumerable<CardDefinition> GetByRarity(
        CardRarity rarity)
    {
        foreach (CardDefinition card in Cards)
        {
            if (card.Rarity == rarity)
                yield return card;
        }
    }

    public static CardDefinition? GetByItem(
        Item item)
    {
        if (item == null)
            return null;
        foreach (CardDefinition card in Cards)
        {
            if (card.ItemAsset == item)
                return card;
        }

        return null;
    }

    public static CardDefinition? GetById(
    string cardId)
    {
        foreach (CardDefinition card in Cards)
        {
            if (
                string.Equals(
                    card.CardId,
                    cardId,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return card;
            }
        }

        return null;
    }
}
