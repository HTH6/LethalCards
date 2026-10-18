using System;
using LethalCards.Cards;
using Unity.Collections;
using Unity.Netcode;

namespace LethalCards.ShipDisplay;

internal sealed class ShipCardDisplayStoredCard
{
    internal string CardId { get; }
    internal CardVariant Variant { get; }
    internal int Grade { get; }
    internal int UngradedValue { get; }
    internal int FinalValue { get; }

    internal ShipCardDisplayStoredCard(
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

    internal ShipCardDisplayStoredCard Clone() =>
        new(CardId, Variant, Grade, UngradedValue, FinalValue);
}

internal struct ShipCardDisplaySlotState :
    INetworkSerializable,
    IEquatable<ShipCardDisplaySlotState>
{
    internal bool Occupied;
    internal FixedString64Bytes CardId;
    internal int Variant;
    internal int Grade;
    internal int UngradedValue;
    internal int FinalValue;

    internal static ShipCardDisplaySlotState Empty => default;

    internal static ShipCardDisplaySlotState FromStored(
        ShipCardDisplayStoredCard? storedCard)
    {
        if (storedCard == null)
            return Empty;

        return new ShipCardDisplaySlotState
        {
            Occupied = true,
            CardId = storedCard.CardId,
            Variant = (int)storedCard.Variant,
            Grade = storedCard.Grade,
            UngradedValue = storedCard.UngradedValue,
            FinalValue = storedCard.FinalValue
        };
    }

    internal ShipCardDisplayStoredCard? ToStored()
    {
        if (!Occupied)
            return null;

        return new ShipCardDisplayStoredCard(
            CardId.ToString(),
            (CardVariant)Variant,
            Grade,
            UngradedValue,
            FinalValue);
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref Occupied);
        serializer.SerializeValue(ref CardId);
        serializer.SerializeValue(ref Variant);
        serializer.SerializeValue(ref Grade);
        serializer.SerializeValue(ref UngradedValue);
        serializer.SerializeValue(ref FinalValue);
    }

    public bool Equals(ShipCardDisplaySlotState other) =>
        Occupied == other.Occupied &&
        CardId.Equals(other.CardId) &&
        Variant == other.Variant &&
        Grade == other.Grade &&
        UngradedValue == other.UngradedValue &&
        FinalValue == other.FinalValue;
}
