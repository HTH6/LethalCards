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

        // Slots 1 and 2 are always low-tier.
        pulls.Add(
            CreatePull(
                RollLowTierCard(),
                0
            )
        );

        pulls.Add(
            CreatePull(
                RollLowTierCard(),
                1
            )
        );

        // Slot 3 is the chase slot.
        pulls.Add(
            CreatePull(
                RollLightChaseSlot(),
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
        // 2% God Pack check occurs first.
        if (Random.NextDouble() < 0.02)
        {
            return OpenGodPack();
        }

        List<CardPull> pulls = new();

        // First two remain normal.
        pulls.Add(
            CreatePull(
                RollLowTierCard(),
                0
            )
        );

        pulls.Add(
            CreatePull(
                RollLowTierCard(),
                1
            )
        );

        // Heavy slot 3 is guaranteed Rare+.
        pulls.Add(
            CreatePull(
                RollHeavyHitSlot(),
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
        else if (roll < 0.03)
            variant = CardVariant.AlternateArt;
        else if (roll < 0.10)
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
