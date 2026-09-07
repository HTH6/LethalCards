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
}