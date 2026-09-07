using System.Collections.Generic;

namespace LethalCards.Cards;

public static class CardDatabase
{
    public static readonly List<CardDefinition> Cards = new();

    public static void Add(CardDefinition card)
    {
        Cards.Add(card);
    }

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