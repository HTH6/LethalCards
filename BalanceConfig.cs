using BepInEx.Configuration;
using LethalCards.Cards;
using LethalCards.Boosters;

namespace LethalCards;

internal static class BalanceConfig
{
    internal const int DefaultGradingCost = 10;
    internal const int DefaultGradingTurnaroundDays = 3;
    internal const int DefaultLightBoosterSpawnWeight = 45;
    internal const int DefaultHeavyBoosterSpawnWeight = 25;
    internal const int DefaultStandardBoosterBoxSpawnWeight = 20;
    internal const int DefaultGoldenBoosterBoxSpawnWeight = 10;
    internal const int DefaultLightBoosterValue = 30;
    internal const int DefaultHeavyBoosterValue = 50;
    internal const int DefaultStandardBoosterBoxValue = 75;
    internal const int DefaultGoldenBoosterBoxValue = 150;

    private static readonly WeightedRarityTable DefaultLightSlot1 = new(72.5f, 20f, 7.5f, 0f, 0f);
    private static readonly WeightedRarityTable DefaultLightSlot2 = new(69f, 20f, 7.5f, 2.5f, 1f);
    private static readonly WeightedRarityTable DefaultLightSlot3 = new(58f, 20f, 15f, 5f, 2f);
    private static readonly WeightedRarityTable DefaultHeavySlot1 = new(30f, 55f, 10f, 5f, 0f);
    private static readonly WeightedRarityTable DefaultHeavySlot2 = new(25f, 50f, 15f, 10f, 0f);
    private static readonly WeightedRarityTable DefaultHeavySlot3 = new(20f, 40f, 20f, 15f, 5f);
    private static readonly WeightedVariantTable DefaultVariants = new(69f, 25f, 5f, 1f);
    private static readonly WeightedGodPackTable DefaultGodPack = new(60f, 40f);
    private static readonly WeightedBoosterBoxTable DefaultStandardBoxPackMix = new(90f, 10f);
    private static readonly WeightedGradeTable DefaultGrades = new(
        95f / 9f, 95f / 9f, 95f / 9f, 95f / 9f, 95f / 9f,
        95f / 9f, 95f / 9f, 95f / 9f, 95f / 9f, 5f);

    internal static WeightedRarityTable LightSlot1 { get; private set; } = DefaultLightSlot1;
    internal static WeightedRarityTable LightSlot2 { get; private set; } = DefaultLightSlot2;
    internal static WeightedRarityTable LightSlot3 { get; private set; } = DefaultLightSlot3;
    internal static WeightedRarityTable HeavySlot1 { get; private set; } = DefaultHeavySlot1;
    internal static WeightedRarityTable HeavySlot2 { get; private set; } = DefaultHeavySlot2;
    internal static WeightedRarityTable HeavySlot3 { get; private set; } = DefaultHeavySlot3;
    internal static WeightedVariantTable Variants { get; private set; } = DefaultVariants;
    internal static WeightedGodPackTable GodPackRarities { get; private set; } = DefaultGodPack;
    internal static WeightedBoosterBoxTable StandardBoxPackMix { get; private set; } = DefaultStandardBoxPackMix;
    internal static WeightedGradeTable GradeChances { get; private set; } = DefaultGrades;

    internal static float HeavyGodPackChancePercent { get; private set; } = 5f;
    internal static float HeavyGodPackChance => HeavyGodPackChancePercent / 100f;
    internal static int GradingCost { get; private set; } = DefaultGradingCost;
    internal static int GradingTurnaroundDays { get; private set; } = DefaultGradingTurnaroundDays;
    internal static int LightBoosterSpawnWeight { get; private set; } = DefaultLightBoosterSpawnWeight;
    internal static int HeavyBoosterSpawnWeight { get; private set; } = DefaultHeavyBoosterSpawnWeight;
    internal static int StandardBoosterBoxSpawnWeight { get; private set; } = DefaultStandardBoosterBoxSpawnWeight;
    internal static int GoldenBoosterBoxSpawnWeight { get; private set; } = DefaultGoldenBoosterBoxSpawnWeight;
    internal static int LightBoosterValue { get; private set; } = DefaultLightBoosterValue;
    internal static int HeavyBoosterValue { get; private set; } = DefaultHeavyBoosterValue;
    internal static int StandardBoosterBoxValue { get; private set; } = DefaultStandardBoosterBoxValue;
    internal static int GoldenBoosterBoxValue { get; private set; } = DefaultGoldenBoosterBoxValue;

    private static readonly float[] GradeMultipliers = { 0f, 0f, 0.2f, 0.35f, 0.5f, 0.65f, 0.8f, 1f, 1.5f, 2.5f, 5f };
    private static readonly float[] VariantValueMultipliers = { 1f, 1f, 1.5f, 2f, 3f };

    internal static void Load(ConfigFile config)
    {
        LightSlot1 = BindRarity(config, "Rarity - Light Slot 1", DefaultLightSlot1);
        LightSlot2 = BindRarity(config, "Rarity - Light Slot 2", DefaultLightSlot2);
        LightSlot3 = BindRarity(config, "Rarity - Light Slot 3", DefaultLightSlot3);
        HeavySlot1 = BindRarity(config, "Rarity - Heavy Slot 1", DefaultHeavySlot1);
        HeavySlot2 = BindRarity(config, "Rarity - Heavy Slot 2", DefaultHeavySlot2);
        HeavySlot3 = BindRarity(config, "Rarity - Heavy Slot 3", DefaultHeavySlot3);

        HeavyGodPackChancePercent = ClampFloat(BindFloat(config, "God Pack", "HeavyGodPackChance", 5f,
            "Direct percent chance for Heavy packs to become God Packs. Clamped to 0-100. Light packs never become God Packs."), 0f, 100f, "God Pack.HeavyGodPackChance");
        GodPackRarities = BindGodPack(config, DefaultGodPack);
        Variants = BindVariants(config, DefaultVariants);
        BindVariantValues(config);
        StandardBoxPackMix = BindBoosterBoxMix(config, DefaultStandardBoxPackMix);

        GradingCost = ClampInt(BindInt(config, "Grading", "GradingCost", DefaultGradingCost,
            "Credits charged by the grading pedestal per submitted card. Clamped to 0 or higher."), 0, int.MaxValue, "Grading.GradingCost");
        GradingTurnaroundDays = ClampInt(BindInt(config, "Grading", "GradingTurnaroundDays", DefaultGradingTurnaroundDays,
            "Number of in-game days before submitted grading jobs become ready. Clamped to 0 or higher."), 0, int.MaxValue, "Grading.GradingTurnaroundDays");
        for (int grade = 1; grade <= 10; grade++)
        {
            GradeMultipliers[grade] = ClampFloat(BindFloat(config, "Grading", $"Grade{grade}ValueMultiplier", GradeMultipliers[grade],
                "Multiplier applied to a card's ungraded value after this grade is rolled. Negative values are clamped to 0."), 0f, float.MaxValue, $"Grading.Grade{grade}ValueMultiplier");
        }
        GradeChances = BindGrades(config, DefaultGrades);

        LightBoosterSpawnWeight = BindNonNegativeInt(config, "Spawning", "LightBoosterSpawnWeight", DefaultLightBoosterSpawnWeight,
            "Relative scrap spawn weight for Light Booster packs on all levels. 0 disables natural spawning while preserving item registration. Restart after changing.");
        HeavyBoosterSpawnWeight = BindNonNegativeInt(config, "Spawning", "HeavyBoosterSpawnWeight", DefaultHeavyBoosterSpawnWeight,
            "Relative scrap spawn weight for Heavy Booster packs on all levels. 0 disables natural spawning while preserving item registration. Restart after changing.");
        StandardBoosterBoxSpawnWeight = BindNonNegativeInt(config, "Spawning", "StandardBoosterBoxSpawnWeight", DefaultStandardBoosterBoxSpawnWeight,
            "Relative scrap spawn weight for Standard Booster Boxes on all levels. 0 disables natural spawning while preserving item registration. Restart after changing.");
        GoldenBoosterBoxSpawnWeight = BindNonNegativeInt(config, "Spawning", "GoldenBoosterBoxSpawnWeight", DefaultGoldenBoosterBoxSpawnWeight,
            "Relative scrap spawn weight for Golden Booster Boxes on all levels. 0 disables natural spawning while preserving item registration. Restart after changing.");

        LightBoosterValue = BindNonNegativeInt(config, "Economy", "LightBoosterScrapValue", DefaultLightBoosterValue,
            "Fixed scrap value for Light Booster packs. Negative values are clamped to 0.");
        HeavyBoosterValue = BindNonNegativeInt(config, "Economy", "HeavyBoosterScrapValue", DefaultHeavyBoosterValue,
            "Fixed scrap value for Heavy Booster packs. Negative values are clamped to 0.");
        StandardBoosterBoxValue = BindNonNegativeInt(config, "Economy", "StandardBoosterBoxScrapValue", DefaultStandardBoosterBoxValue,
            "Fixed scrap value for Standard Booster Boxes. Negative values are clamped to 0.");
        GoldenBoosterBoxValue = BindNonNegativeInt(config, "Economy", "GoldenBoosterBoxScrapValue", DefaultGoldenBoosterBoxValue,
            "Fixed scrap value for Golden Booster Boxes. Negative values are clamped to 0.");

        config.Save();
        LogEffectiveValues();
    }

    internal static float GetGradeMultiplier(int grade) =>
        grade >= 1 && grade <= 10 ? GradeMultipliers[grade] : 1f;

    internal static float GetVariantValueMultiplier(CardVariant variant) =>
        variant switch
        {
            CardVariant.Foil => VariantValueMultipliers[(int)CardVariant.Foil],
            CardVariant.AlternateArt => VariantValueMultipliers[(int)CardVariant.AlternateArt],
            CardVariant.Misprint => VariantValueMultipliers[(int)CardVariant.Misprint],
            _ => VariantValueMultipliers[(int)CardVariant.Standard]
        };

    internal static WeightedRarityTable GetRarityTable(BoosterType type, int slotIndex) =>
        type == BoosterType.Heavy
            ? slotIndex switch { 0 => HeavySlot1, 1 => HeavySlot2, _ => HeavySlot3 }
            : slotIndex switch { 0 => LightSlot1, 1 => LightSlot2, _ => LightSlot3 };

    internal static int GetBoosterValue(BoosterType type) =>
        type == BoosterType.Heavy ? HeavyBoosterValue : LightBoosterValue;

    internal static int GetBoosterBoxValue(BoosterBoxType type) =>
        type == BoosterBoxType.Golden ? GoldenBoosterBoxValue : StandardBoosterBoxValue;

    private static WeightedRarityTable BindRarity(ConfigFile config, string section, WeightedRarityTable defaults)
    {
        WeightedRarityTable raw = new(
            BindFloat(config, section, "CommonChance", defaults.Common, WeightDescription("Common cards")),
            BindFloat(config, section, "UncommonChance", defaults.Uncommon, WeightDescription("Uncommon cards")),
            BindFloat(config, section, "RareChance", defaults.Rare, WeightDescription("Rare cards")),
            BindFloat(config, section, "UltraRareChance", defaults.UltraRare, WeightDescription("Ultra Rare cards")),
            BindFloat(config, section, "SecretRareChance", defaults.SecretRare, WeightDescription("Secret Rare cards")));
        return NormalizeRarity(section, raw, defaults);
    }

    private static WeightedVariantTable BindVariants(ConfigFile config, WeightedVariantTable defaults)
    {
        WeightedVariantTable raw = new(
            BindFloat(config, "Variants", "StandardChance", defaults.Standard, WeightDescription("Standard variant")),
            BindFloat(config, "Variants", "FoilChance", defaults.Foil, WeightDescription("Foil variant")),
            BindFloat(config, "Variants", "AlternateArtChance", defaults.AlternateArt, WeightDescription("Alternate Art variant")),
            BindFloat(config, "Variants", "MisprintChance", defaults.Misprint, WeightDescription("Misprint variant")));
        return NormalizeVariants(raw, defaults);
    }

    private static WeightedGodPackTable BindGodPack(ConfigFile config, WeightedGodPackTable defaults)
    {
        WeightedGodPackTable raw = new(
            BindFloat(config, "God Pack", "UltraRareChance", defaults.UltraRare, WeightDescription("Ultra Rare God Pack slots")),
            BindFloat(config, "God Pack", "SecretRareChance", defaults.SecretRare, WeightDescription("Secret Rare God Pack slots")));
        return NormalizeGodPack(raw, defaults);
    }

    private static void BindVariantValues(ConfigFile config)
    {
        VariantValueMultipliers[(int)CardVariant.Standard] = BindNonNegativeFiniteFloat(config, "Variant Values", "StandardValueMultiplier", 1f,
            "Literal value multiplier for Standard cards. This affects monetary value only and does not affect pull odds.");
        VariantValueMultipliers[(int)CardVariant.Foil] = BindNonNegativeFiniteFloat(config, "Variant Values", "FoilValueMultiplier", 1.5f,
            "Literal value multiplier for Foil cards. This affects monetary value only and does not affect pull odds.");
        VariantValueMultipliers[(int)CardVariant.AlternateArt] = BindNonNegativeFiniteFloat(config, "Variant Values", "AlternateArtValueMultiplier", 2f,
            "Literal value multiplier for Alternate Art cards. This affects monetary value only and does not affect pull odds.");
        VariantValueMultipliers[(int)CardVariant.Misprint] = BindNonNegativeFiniteFloat(config, "Variant Values", "MisprintValueMultiplier", 3f,
            "Literal value multiplier for Misprint cards. This affects monetary value only and does not affect pull odds.");
    }

    private static WeightedBoosterBoxTable BindBoosterBoxMix(ConfigFile config, WeightedBoosterBoxTable defaults)
    {
        WeightedBoosterBoxTable raw = new(
            BindFloat(config, "Booster Boxes", "StandardBoxLightPackChance", defaults.Light,
                "Relative weight for Light packs in Standard Booster Boxes. Normalized automatically: 90/10 = 90%/10%, 3/1 = 75%/25%."),
            BindFloat(config, "Booster Boxes", "StandardBoxHeavyPackChance", defaults.Heavy,
                "Relative weight for Heavy packs in Standard Booster Boxes. Normalized automatically: 90/10 = 90%/10%, 3/1 = 75%/25%."));
        return NormalizeBoosterBox(raw, defaults);
    }

    private static WeightedGradeTable BindGrades(ConfigFile config, WeightedGradeTable defaults)
    {
        float[] weights = new float[10];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = BindFloat(config, "Grading", $"Grade{i + 1}Chance", defaults.GetWeight(i + 1),
                "Non-negative grade roll weight. Grade chance weights are normalized automatically and do not need to sum to 100.");
        return NormalizeGrades(new WeightedGradeTable(weights), defaults);
    }

    private static WeightedRarityTable NormalizeRarity(string name, WeightedRarityTable raw, WeightedRarityTable fallback)
    {
        float[] weights = ClampWeights(name, raw.ToArray());
        float sum = Sum(weights);
        if (sum <= 0f)
        {
            Plugin.Log.LogWarning($"BALANCE CONFIG FALLBACK | {name} weights were all zero or invalid; using production defaults.");
            return fallback.Normalized();
        }
        return new WeightedRarityTable(weights).Normalized();
    }

    private static WeightedVariantTable NormalizeVariants(WeightedVariantTable raw, WeightedVariantTable fallback)
    {
        float[] weights = ClampWeights("Variants", raw.ToArray());
        if (Sum(weights) <= 0f)
        {
            Plugin.Log.LogWarning("BALANCE CONFIG FALLBACK | Variant weights were all zero or invalid; using production defaults.");
            return fallback.Normalized();
        }
        return new WeightedVariantTable(weights).Normalized();
    }

    private static WeightedGodPackTable NormalizeGodPack(WeightedGodPackTable raw, WeightedGodPackTable fallback)
    {
        float[] weights = ClampWeights("God Pack", raw.ToArray());
        if (Sum(weights) <= 0f)
        {
            Plugin.Log.LogWarning("BALANCE CONFIG FALLBACK | God Pack Ultra/Secret weights were all zero or invalid; using production defaults.");
            return fallback.Normalized();
        }
        return new WeightedGodPackTable(weights).Normalized();
    }

    private static WeightedGradeTable NormalizeGrades(WeightedGradeTable raw, WeightedGradeTable fallback)
    {
        float[] weights = ClampWeights("Grading grade chances", raw.ToArray());
        if (Sum(weights) <= 0f)
        {
            Plugin.Log.LogWarning("BALANCE CONFIG FALLBACK | Grade chance weights were all zero or invalid; using production defaults.");
            return fallback.Normalized();
        }
        return new WeightedGradeTable(weights).Normalized();
    }

    private static WeightedBoosterBoxTable NormalizeBoosterBox(WeightedBoosterBoxTable raw, WeightedBoosterBoxTable fallback)
    {
        float[] weights = ClampWeights("Booster Boxes", raw.ToArray());
        if (Sum(weights) <= 0f)
        {
            Plugin.Log.LogWarning("BALANCE CONFIG FALLBACK | Standard Booster Box Light/Heavy weights were all zero or invalid; using production defaults.");
            return fallback.Normalized();
        }
        return new WeightedBoosterBoxTable(weights).Normalized();
    }

    private static float[] ClampWeights(string name, float[] weights)
    {
        bool clamped = false;
        for (int i = 0; i < weights.Length; i++)
        {
            if (weights[i] >= 0f)
                continue;
            weights[i] = 0f;
            clamped = true;
        }
        if (clamped)
            Plugin.Log.LogWarning($"BALANCE CONFIG CLAMPED | {name} had negative weights; negatives were treated as 0.");
        return weights;
    }

    private static float BindFloat(ConfigFile config, string section, string key, float defaultValue, string description) =>
        config.Bind(section, key, defaultValue, new ConfigDescription(description)).Value;

    private static int BindInt(ConfigFile config, string section, string key, int defaultValue, string description) =>
        config.Bind(section, key, defaultValue, new ConfigDescription(description)).Value;

    private static int BindNonNegativeInt(ConfigFile config, string section, string key, int defaultValue, string description)
    {
        int value = BindInt(config, section, key, defaultValue, description);
        return ClampInt(value, 0, int.MaxValue, $"{section}.{key}");
    }

    private static float BindNonNegativeFiniteFloat(ConfigFile config, string section, string key, float defaultValue, string description)
    {
        float value = BindFloat(config, section, key, defaultValue, description);
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            Plugin.Log.LogWarning($"BALANCE CONFIG FALLBACK | {section}.{key}={value} was invalid; using default {defaultValue}.");
            return defaultValue;
        }
        return ClampFloat(value, 0f, float.MaxValue, $"{section}.{key}");
    }

    private static int ClampInt(int value, int min, int max, string name)
    {
        int clamped = System.Math.Max(min, System.Math.Min(max, value));
        if (clamped != value)
            Plugin.Log.LogWarning($"BALANCE CONFIG CLAMPED | {name}={value} was clamped to {clamped}.");
        return clamped;
    }

    private static float ClampFloat(float value, float min, float max, string name)
    {
        float clamped = System.Math.Max(min, System.Math.Min(max, value));
        if (System.Math.Abs(clamped - value) > 0.0001f)
            Plugin.Log.LogWarning($"BALANCE CONFIG CLAMPED | {name}={value} was clamped to {clamped}.");
        return clamped;
    }

    private static string WeightDescription(string label) =>
        $"Non-negative weight/chance for {label}. Tables are normalized automatically and do not need to sum to 100.";

    private static float Sum(float[] weights)
    {
        float sum = 0f;
        foreach (float weight in weights)
            sum += weight;
        return sum;
    }

    private static void LogEffectiveValues()
    {
        LogRarity("Light Slot 1", LightSlot1);
        LogRarity("Light Slot 2", LightSlot2);
        LogRarity("Light Slot 3", LightSlot3);
        LogRarity("Heavy Slot 1", HeavySlot1);
        LogRarity("Heavy Slot 2", HeavySlot2);
        LogRarity("Heavy Slot 3", HeavySlot3);
        Plugin.Log.LogInfo($"VARIANT CONFIG EFFECTIVE | Standard={Variants.Standard:F2}% | Foil={Variants.Foil:F2}% | AlternateArt={Variants.AlternateArt:F2}% | Misprint={Variants.Misprint:F2}%");
        Plugin.Log.LogInfo($"VARIANT VALUE CONFIG EFFECTIVE | Standard={VariantValueMultipliers[(int)CardVariant.Standard]:F2}x | Foil={VariantValueMultipliers[(int)CardVariant.Foil]:F2}x | AlternateArt={VariantValueMultipliers[(int)CardVariant.AlternateArt]:F2}x | Misprint={VariantValueMultipliers[(int)CardVariant.Misprint]:F2}x");
        Plugin.Log.LogInfo($"GOD PACK CONFIG EFFECTIVE | TriggerChance={HeavyGodPackChancePercent:F2}% | UltraRare={GodPackRarities.UltraRare:F2}% | SecretRare={GodPackRarities.SecretRare:F2}%");
        Plugin.Log.LogInfo($"BOOSTER BOX CONFIG EFFECTIVE | StandardBoxLight={StandardBoxPackMix.Light:F2}% | StandardBoxHeavy={StandardBoxPackMix.Heavy:F2}% | GoldenBoxHeavy=100.00%");
        Plugin.Log.LogInfo($"BALANCE CONFIG EFFECTIVE | GradingCost={GradingCost} | GradingTurnaroundDays={GradingTurnaroundDays} | LightSpawnWeight={LightBoosterSpawnWeight} | HeavySpawnWeight={HeavyBoosterSpawnWeight} | StandardBoxSpawnWeight={StandardBoosterBoxSpawnWeight} | GoldenBoxSpawnWeight={GoldenBoosterBoxSpawnWeight}");
        Plugin.Log.LogInfo($"ECONOMY CONFIG EFFECTIVE | LightBoosterValue={LightBoosterValue} | HeavyBoosterValue={HeavyBoosterValue} | StandardBoxValue={StandardBoosterBoxValue} | GoldenBoxValue={GoldenBoosterBoxValue}");
        Plugin.Log.LogInfo($"GRADING CHANCE CONFIG EFFECTIVE | Grade1={GradeChances.GetWeight(1):F2}% | Grade2={GradeChances.GetWeight(2):F2}% | Grade3={GradeChances.GetWeight(3):F2}% | Grade4={GradeChances.GetWeight(4):F2}% | Grade5={GradeChances.GetWeight(5):F2}% | Grade6={GradeChances.GetWeight(6):F2}% | Grade7={GradeChances.GetWeight(7):F2}% | Grade8={GradeChances.GetWeight(8):F2}% | Grade9={GradeChances.GetWeight(9):F2}% | Grade10={GradeChances.GetWeight(10):F2}%");
        Plugin.Log.LogInfo($"GRADING MULTIPLIER CONFIG EFFECTIVE | Grade1={GradeMultipliers[1]:F2} | Grade2={GradeMultipliers[2]:F2} | Grade3={GradeMultipliers[3]:F2} | Grade4={GradeMultipliers[4]:F2} | Grade5={GradeMultipliers[5]:F2} | Grade6={GradeMultipliers[6]:F2} | Grade7={GradeMultipliers[7]:F2} | Grade8={GradeMultipliers[8]:F2} | Grade9={GradeMultipliers[9]:F2} | Grade10={GradeMultipliers[10]:F2}");
    }

    private static void LogRarity(string label, WeightedRarityTable table) =>
        Plugin.Log.LogInfo($"RARITY CONFIG EFFECTIVE | {label} | Common={table.Common:F2}% | Uncommon={table.Uncommon:F2}% | Rare={table.Rare:F2}% | UltraRare={table.UltraRare:F2}% | SecretRare={table.SecretRare:F2}%");
}

internal readonly struct WeightedRarityTable
{
    internal readonly float Common;
    internal readonly float Uncommon;
    internal readonly float Rare;
    internal readonly float UltraRare;
    internal readonly float SecretRare;

    internal WeightedRarityTable(float common, float uncommon, float rare, float ultraRare, float secretRare)
    {
        Common = common;
        Uncommon = uncommon;
        Rare = rare;
        UltraRare = ultraRare;
        SecretRare = secretRare;
    }

    internal WeightedRarityTable(float[] weights) : this(weights[0], weights[1], weights[2], weights[3], weights[4]) { }

    internal CardRarity Roll(System.Random random)
    {
        float roll = (float)(random.NextDouble() * 100.0);
        if (roll < Common) return CardRarity.Common;
        if (roll < Common + Uncommon) return CardRarity.Uncommon;
        if (roll < Common + Uncommon + Rare) return CardRarity.Rare;
        if (roll < Common + Uncommon + Rare + UltraRare) return CardRarity.UltraRare;
        return CardRarity.SecretRare;
    }

    internal float[] ToArray() => new[] { Common, Uncommon, Rare, UltraRare, SecretRare };

    internal WeightedRarityTable Normalized()
    {
        float sum = Common + Uncommon + Rare + UltraRare + SecretRare;
        return new WeightedRarityTable(Common * 100f / sum, Uncommon * 100f / sum, Rare * 100f / sum, UltraRare * 100f / sum, SecretRare * 100f / sum);
    }
}

internal readonly struct WeightedVariantTable
{
    internal readonly float Standard;
    internal readonly float Foil;
    internal readonly float AlternateArt;
    internal readonly float Misprint;

    internal WeightedVariantTable(float standard, float foil, float alternateArt, float misprint)
    {
        Standard = standard;
        Foil = foil;
        AlternateArt = alternateArt;
        Misprint = misprint;
    }

    internal WeightedVariantTable(float[] weights) : this(weights[0], weights[1], weights[2], weights[3]) { }

    internal CardVariant Roll(System.Random random)
    {
        float roll = (float)(random.NextDouble() * 100.0);
        if (roll < Standard) return CardVariant.Standard;
        if (roll < Standard + Foil) return CardVariant.Foil;
        if (roll < Standard + Foil + AlternateArt) return CardVariant.AlternateArt;
        return CardVariant.Misprint;
    }

    internal float[] ToArray() => new[] { Standard, Foil, AlternateArt, Misprint };

    internal WeightedVariantTable Normalized()
    {
        float sum = Standard + Foil + AlternateArt + Misprint;
        return new WeightedVariantTable(Standard * 100f / sum, Foil * 100f / sum, AlternateArt * 100f / sum, Misprint * 100f / sum);
    }
}

internal readonly struct WeightedGodPackTable
{
    internal readonly float UltraRare;
    internal readonly float SecretRare;

    internal WeightedGodPackTable(float ultraRare, float secretRare)
    {
        UltraRare = ultraRare;
        SecretRare = secretRare;
    }

    internal WeightedGodPackTable(float[] weights) : this(weights[0], weights[1]) { }

    internal CardRarity Roll(System.Random random)
    {
        float roll = (float)(random.NextDouble() * 100.0);
        return roll < UltraRare ? CardRarity.UltraRare : CardRarity.SecretRare;
    }

    internal float[] ToArray() => new[] { UltraRare, SecretRare };

    internal WeightedGodPackTable Normalized()
    {
        float sum = UltraRare + SecretRare;
        return new WeightedGodPackTable(UltraRare * 100f / sum, SecretRare * 100f / sum);
    }
}

internal readonly struct WeightedBoosterBoxTable
{
    internal readonly float Light;
    internal readonly float Heavy;

    internal WeightedBoosterBoxTable(float light, float heavy)
    {
        Light = light;
        Heavy = heavy;
    }

    internal WeightedBoosterBoxTable(float[] weights) : this(weights[0], weights[1]) { }

    internal BoosterType Roll(float roll)
    {
        return roll * 100f < Heavy ? BoosterType.Heavy : BoosterType.Light;
    }

    internal float[] ToArray() => new[] { Light, Heavy };

    internal WeightedBoosterBoxTable Normalized()
    {
        float sum = Light + Heavy;
        return new WeightedBoosterBoxTable(Light * 100f / sum, Heavy * 100f / sum);
    }
}

internal readonly struct WeightedGradeTable
{
    private readonly float[] weights;

    internal WeightedGradeTable(params float[] weights)
    {
        this.weights = weights;
    }

    internal int Roll(System.Random random)
    {
        float roll = (float)(random.NextDouble() * 100.0);
        float cumulative = 0f;
        for (int i = 0; i < weights.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return i + 1;
        }
        return 10;
    }

    internal float GetWeight(int grade) => grade >= 1 && grade <= weights.Length ? weights[grade - 1] : 0f;

    internal float[] ToArray() => (float[])weights.Clone();

    internal WeightedGradeTable Normalized()
    {
        float sum = 0f;
        foreach (float weight in weights)
            sum += weight;
        float[] normalized = new float[weights.Length];
        for (int i = 0; i < weights.Length; i++)
            normalized[i] = weights[i] * 100f / sum;
        return new WeightedGradeTable(normalized);
    }
}
