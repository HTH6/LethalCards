using System.Collections.Generic;

namespace LethalCards.Cards;

public static partial class CardDatabase
{
    // CardId is a permanent save key; SetNumber is the printed/display number.
    // Keep the seven existing assets first, in their original registration order:
    // vanilla ship saves also depend on registered item indices.
    // Supplying an asset name opts an entry into asset loading. Only successful
    // registration sets IsImplemented=true. Metadata-only entries need no prefab.
    private static IEnumerable<CardDefinition> CreateV1Roster()
    {
        yield return new CardDefinition("LC01-001", 1, "Hoarding Bug", CardRarity.Common, "HoardingBugCardItem", 6);
        yield return new CardDefinition("LC01-002", 2, "Eyeless Dog", CardRarity.Common, "EyelessDogCardItem", 7);
        yield return new CardDefinition("LC01-006", 11, "Snare Flea", CardRarity.Uncommon, "SnareFleaCardItem", 10);
        yield return new CardDefinition("LC01-003", 22, "Bracken", CardRarity.Rare, "BrackenCardItem", 32);
        yield return new CardDefinition("LC01-004", 21, "Coil-Head", CardRarity.Rare, "CoilHeadCardItem", 36);
        yield return new CardDefinition("LC01-005", 27, "Jester", CardRarity.UltraRare, "JesterCardItem", 75);
        yield return new CardDefinition("LC01-007", 31, "Ghost Girl", CardRarity.SecretRare, "GhostGirlCardItem", 140);

        // New keys use a distinct namespace so reserved numbers cannot collide
        // with historical IDs. Never rename these keys when enabling an entry.
        yield return new CardDefinition("LC01-V1-003", 3, "Bunker Spider", CardRarity.Common, "BunkerSpiderCardItem", 7);
        yield return new CardDefinition("LC01-V1-004", 4, "Hygrodere", CardRarity.Common, "HygrodereCardItem", 6);
        yield return new CardDefinition("LC01-V1-005", 5, "Baboon Hawk", CardRarity.Common, "BaboonHawkCardItem", 8);
        yield return new CardDefinition("LC01-V1-006", 6, "Tulip Snake", CardRarity.Common, "TulipSnakeCardItem", 5);
        yield return new CardDefinition("LC01-V1-007", 7, "Manticoil", CardRarity.Common, "ManticoilCardItem", 5);
        yield return new CardDefinition("LC01-V1-008", 8, "Roaming Locusts", CardRarity.Common, "RoamingLocustCardItem", 5);
        yield return new CardDefinition("LC01-V1-009", 9, "Backwater Gunkfish", CardRarity.Common, "BackwaterGunkfishCardItem", 8);
        yield return new CardDefinition("LC01-V1-010", 10, "Cadaver Growths", CardRarity.Common, "CadaverGrowthCardItem", 9);
        yield return new CardDefinition("LC01-V1-012", 12, "Spore Lizard", CardRarity.Uncommon, "SporeLizardCardItem", 12);
        yield return new CardDefinition("LC01-V1-013", 13, "Butler", CardRarity.Uncommon, "ButlerCardItem", 14);
        yield return new CardDefinition("LC01-V1-014", 14, "Forest Keeper", CardRarity.Uncommon, "ForestKeeperCardItem", 16);
        yield return new CardDefinition("LC01-V1-015", 15, "Franklin", CardRarity.Uncommon, "FranklinCardItem", 13);
        yield return new CardDefinition("LC01-V1-016", 16, "Circuit Bees", CardRarity.Uncommon, "CircuitBeesCardItem", 15);
        yield return new CardDefinition("LC01-V1-017", 17, "Cadaver Bloom", CardRarity.Uncommon, "CadaverBloomCardItem", 14);
        yield return new CardDefinition("LC01-V1-018", 18, "Mask Hornets", CardRarity.Uncommon, "MaskHornetsCardItem", 17);
        yield return new CardDefinition("LC01-V1-019", 19, "Giant Sapsucker", CardRarity.Uncommon, "GiantSapSuckerCardItem", 18);
        yield return new CardDefinition("LC01-V1-020", 20, "Thumper", CardRarity.Rare, "ThumperCardItem", 28);
        yield return new CardDefinition("LC01-V1-023", 23, "Barber", CardRarity.Rare, "BarberCardItem", 30);
        yield return new CardDefinition("LC01-V1-024", 24, "Earth Leviathan", CardRarity.Rare, "EarthLeviathanCardItem", 40);
        yield return new CardDefinition("LC01-V1-025", 25, "Kidnapper Fox", CardRarity.Rare, "KidnapperFoxCardItem", 34);
        yield return new CardDefinition("LC01-V1-026", 26, "Feiopar", CardRarity.Rare, "FeioparCardItem", 38);
        yield return new CardDefinition("LC01-V1-028", 28, "Nutcracker", CardRarity.UltraRare, "NutcrackerCardItem", 70);
        yield return new CardDefinition("LC01-V1-029", 29, "Masked", CardRarity.UltraRare, "MaskedCardItem", 65);
        yield return new CardDefinition("LC01-V1-030", 30, "Maneater", CardRarity.UltraRare, "ManeaterCardItem", 85);
        yield return new CardDefinition("LC01-V1-032", 32, "Jeb", CardRarity.SecretRare, "JEBCardItem", 150);
        yield return new CardDefinition("LC01-V1-033", 33, "Lasso Man", CardRarity.SecretRare, "LassomanCardItem", 160);
    }
}
