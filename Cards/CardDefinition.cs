using UnityEngine;

namespace LethalCards.Cards;

public class CardDefinition
{
    public string CardId { get; }
    public string SetId { get; }
    public string DisplayName { get; }
    public CardRarity Rarity { get; }
    public int BaseScrapValue { get; }
    public Item ItemAsset { get; }

    public CardDefinition(
        string cardId,
        string setId,
        string displayName,
        CardRarity rarity,
        int baseScrapValue,
        Item itemAsset)
    {
        CardId = cardId;
        SetId = setId;
        DisplayName = displayName;
        Rarity = rarity;
        BaseScrapValue = baseScrapValue;
        ItemAsset = itemAsset;
    }
}