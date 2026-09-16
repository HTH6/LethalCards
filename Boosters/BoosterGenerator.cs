using System;
using System.Collections.Generic;
using System.Linq;
using LethalCards.Cards;

namespace LethalCards.Boosters;

public static class BoosterGenerator
{
    private static readonly Random Random = new();

    public static PackResult OpenPack(
        BoosterType type)
    {
        return type switch
        {
            BoosterType.Heavy =>
                OpenHeavyPack(),

            _ =>
                OpenLightPack()
        };
    }

    private static PackResult OpenLightPack()
    {
        List<CardPull> pulls = new();

        // Each slot rolls directly against its own rarity table.
        pulls.Add(
            CreatePull(
                RollSlotCard(0.725, 0.925, 1.0, 1.0),
                0
            )
        );

        pulls.Add(
            CreatePull(
                RollSlotCard(0.69, 0.89, 0.965, 0.99),
                1
            )
        );

        // Slot 3 has the strongest Light distribution.
        pulls.Add(
            CreatePull(
                RollSlotCard(0.58, 0.78, 0.93, 0.98),
                2
            )
        );

        return new PackResult(
            BoosterType.Light,
            false,
            pulls
        );
    }

    private static PackResult OpenHeavyPack()
    {
        // 5% God Pack check occurs first.
        if (Random.NextDouble() < 0.05)
        {
            return OpenGodPack();
        }

        List<CardPull> pulls = new();

        // Normal Heavy packs use three separate slot distributions.
        pulls.Add(
            CreatePull(
                RollSlotCard(0.30, 0.85, 0.95, 1.0),
                0
            )
        );

        pulls.Add(
            CreatePull(
                RollSlotCard(0.25, 0.75, 0.90, 1.0),
                1
            )
        );

        // Slot 3 can roll any rarity; there is no guaranteed Rare+ hit.
        pulls.Add(
            CreatePull(
                RollSlotCard(0.20, 0.60, 0.80, 0.95),
                2
            )
        );

        return new PackResult(
            BoosterType.Heavy,
            false,
            pulls
        );
    }

    private static PackResult OpenGodPack()
    {
        List<CardPull> pulls = new();

        for (int i = 0; i < 3; i++)
        {
            CardRarity rarity =
                Random.NextDouble() < 0.60
                    ? CardRarity.UltraRare
                    : CardRarity.SecretRare;

            pulls.Add(
                CreatePull(
                    RollCardFromRarity(rarity),
                    i
                )
            );
        }

        return new PackResult(
            BoosterType.Heavy,
            true,
            pulls
        );
    }

    // Cumulative cutoffs in Common, Uncommon, Rare, Ultra Rare order.
    // Secret Rare occupies the remaining interval; 1 excludes higher tiers.
    private static CardDefinition RollSlotCard(
        double common, double uncommon, double rare, double ultraRare)
    {
        double roll = Random.NextDouble();
        CardRarity rarity = roll < common ? CardRarity.Common :
            roll < uncommon ? CardRarity.Uncommon :
            roll < rare ? CardRarity.Rare :
            roll < ultraRare ? CardRarity.UltraRare : CardRarity.SecretRare;
        return RollCardFromRarity(rarity);
    }

    /* Previous rarity logic retained for balance reference; no longer used.
    private static CardDefinition
        RollLowTierCard()
    {
        CardRarity rarity =
            Random.NextDouble() < 0.80
                ? CardRarity.Common
                : CardRarity.Uncommon;

        return RollCardFromRarity(rarity);
    }

    private static CardDefinition
        RollLightChaseSlot()
    {
        double roll =
            Random.NextDouble();

        // 0.5%
        if (roll < 0.005)
        {
            return RollCardFromRarity(
                CardRarity.SecretRare
            );
        }

        // Next 2.5%
        if (roll < 0.030)
        {
            return RollCardFromRarity(
                CardRarity.UltraRare
            );
        }

        // Next 12%
        if (roll < 0.150)
        {
            return RollCardFromRarity(
                CardRarity.Rare
            );
        }

        // Remaining 85%
        return RollLowTierCard();
    }

    private static CardDefinition
        RollHeavyHitSlot()
    {
        double roll =
            Random.NextDouble();

        if (roll < 0.03)
        {
            return RollCardFromRarity(
                CardRarity.SecretRare
            );
        }

        if (roll < 0.20)
        {
            return RollCardFromRarity(
                CardRarity.UltraRare
            );
        }

        return RollCardFromRarity(
            CardRarity.Rare
        );
    }

    */

    private static CardPull CreatePull(
        CardDefinition card,
        int slotIndex)
    {
        return new CardPull(
            card,
            RollVariant(),
            slotIndex
        );
    }

    //Test variant roll here for forced debugging of pulling foil cards. This is a temporary measure for testing purposes.
/*      private static CardVariant RollVariant()
    {
        CardVariant variant = CardVariant.Foil;

        Plugin.Log.LogInfo(
            $"VARIANT ROLL | " +
            $"FORCED TEST | " +
            $"Result={variant}"
        );

        return variant;
    }  */
    
    // REAL VARIENT ROLL HERE
    private static CardVariant RollVariant()
    {
        double roll = Random.NextDouble();

        CardVariant variant;

        if (roll < 0.01)
            variant = CardVariant.Misprint;
        else if (roll < 0.06)
            variant = CardVariant.AlternateArt;
        else if (roll < 0.31)
            variant = CardVariant.Foil;
        else
            variant = CardVariant.Standard;

        Plugin.Log.LogInfo(
            $"VARIANT ROLL | " +
            $"Roll={roll:F4} | " +
            $"Result={variant}"
        );

        return variant;
    }

    private static CardDefinition
        RollCardFromRarity(
            CardRarity rarity)
    {
        List<CardDefinition> candidates =
            // CardDatabase.Cards // Previously every registered definition was playable.
            CardDatabase.GetImplementedCards()
                .Where(
                    card =>
                        card.Rarity == rarity
                )
                .ToList();

        if (candidates.Count == 0)
        {
            Plugin.Log.LogError(
                $"No cards registered for rarity: {rarity}"
            );

            throw new InvalidOperationException(
                $"No cards registered for rarity {rarity}."
            );
        }

        return candidates[
            Random.Next(candidates.Count)
        ];
    }
}
