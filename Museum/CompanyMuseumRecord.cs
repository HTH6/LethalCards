using LethalCards.Cards;

namespace LethalCards.Museum;

internal sealed class CompanyMuseumRecord
{
    internal string CardId { get; }
    internal CardVariant Variant { get; }
    internal int Grade { get; }
    internal int UngradedValue { get; }
    internal int FinalValue { get; }

    internal CompanyMuseumRecord(
        string cardId,
        CardVariant variant,
        int grade,
        int ungradedValue,
        int finalValue)
    {
        CardId = cardId;
        Variant = variant;
        Grade = grade;
        UngradedValue = ungradedValue;
        FinalValue = finalValue;
    }

    internal CompanyMuseumRecord Clone() =>
        new(CardId, Variant, Grade, UngradedValue, FinalValue);

    internal string Key => CreateKey(CardId, Variant);

    internal static string CreateKey(string cardId, CardVariant variant) =>
        $"{cardId}:{(int)variant}";
}

internal enum CompanyMuseumRegistrationResult
{
    Registered,
    Upgraded,
    AlreadyRegistered,
    InvalidHeldItem,
    InvalidPlayerOrDistance,
    PersistenceFailed,
    Busy
}
