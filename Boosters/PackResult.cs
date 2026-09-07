using System.Collections.Generic;
using LethalCards.Cards;

namespace LethalCards.Boosters;

public class PackResult
{
    public BoosterType PackType { get; }
    public bool IsGodPack { get; }
    public List<CardPull> Cards { get; }

    public PackResult(
        BoosterType packType,
        bool isGodPack,
        List<CardPull> cards)
    {
        PackType = packType;
        IsGodPack = isGodPack;
        Cards = cards;
    }
}