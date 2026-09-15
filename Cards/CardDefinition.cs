using UnityEngine;

namespace LethalCards.Cards;

public class CardDefinition
{
    public string CardId { get; }
    public string SetId { get; }
    public string DisplayName { get; }
    public CardRarity Rarity { get; }
    public int BaseScrapValue { get; }
    public Item? ItemAsset { get; private set; }
    public int SetNumber { get; }
    public string? AssetName { get; }
    public bool IsImplemented { get; private set; }

    public CardDefinition(string cardId, int setNumber, string displayName,
        CardRarity rarity, string? assetName = null, int baseScrapValue = 0,
        string setId = "LC01")
    {
        CardId = cardId;
        SetId = setId;
        SetNumber = setNumber;
        DisplayName = displayName;
        Rarity = rarity;
        AssetName = assetName;
        BaseScrapValue = baseScrapValue;
    }

    internal void AttachImplementedItem(Item item)
    {
        if (item == null || item.spawnPrefab == null)
            throw new System.ArgumentException("Implemented cards require an item and prefab.", nameof(item));
        ItemAsset = item;
        IsImplemented = true;
    }

    /* Previous prefab-required constructor retained for reference.
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
    */
}
