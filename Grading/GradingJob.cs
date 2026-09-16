namespace LethalCards.Grading;
using LethalCards.Cards;

public class GradingJob
{
    public string JobId { get; set; } = "";

    public string CardId { get; set; } = "";

    public CardVariant Variant { get; set; }

    public int Grade { get; set; }

    public int SubmittedDay { get; set; }

    public int ReadyDay { get; set; }

    // Nullable for legacy saves or definitions temporarily unavailable at load time.
    public int? FinalValue { get; set; }

    public void EnsureFinalValue()
    {
        if (FinalValue.HasValue)
            return;
        CardDefinition? card = CardDatabase.GetById(CardId);
        if (card != null)
            FinalValue = CardGrading.CalculateGradedValue(new CardPull(card, Variant, 0).UngradedValue, Grade);
    }
}
