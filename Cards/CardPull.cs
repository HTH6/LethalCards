namespace LethalCards.Cards;

public class CardPull
{
    public CardDefinition Card { get; }
    public CardVariant Variant { get; }
    public int SlotIndex { get; }

    public CardPull(
        CardDefinition card,
        CardVariant variant,
        int slotIndex)
    {
        Card = card;
        Variant = variant;
        SlotIndex = slotIndex;
    }

    public float VariantMultiplier =>
        BalanceConfig.GetVariantValueMultiplier(Variant);

    public int UngradedValue
    {
        get
        {
            int calculatedValue =
                (int)System.Math.Round(
                    Card.BaseScrapValue *
                    VariantMultiplier
                );

            Plugin.Log.LogInfo(
                $"CARD VALUE CALC | " +
                $"Card={Card.DisplayName} | " +
                $"Base=${Card.BaseScrapValue} | " +
                $"Variant={Variant} | " +
                $"Multiplier={VariantMultiplier}x | " +
                $"Final=${calculatedValue}"
            );

            return calculatedValue;
        }
    }
}
