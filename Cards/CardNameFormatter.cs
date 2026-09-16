namespace LethalCards.Cards;

public static class CardNameFormatter
{
    public static string GetDisplayName(string cardName, CardVariant variant)
    {
        string variantName = variant switch
        {
            CardVariant.Foil => "Foil",
            CardVariant.AlternateArt => "Alternate Art",
            CardVariant.Misprint => "Misprint",
            _ => "Standard"
        };
        return $"{cardName} Card ({variantName})";
    }
}
