using HarmonyLib;

namespace LethalCards.Cards;

public static class CardSaveData
{
    private const int CurrentValueFormatBase = 1000000;
    private const int CurrentValueScale = 1000;

    public static int Encode(
        CardVariant variant,
        int grade,
        int? currentValue = null)
    {
        int variantValue =
            (int)variant;

        int legacyValue =
            1000 +
            (variantValue * 100) +
            grade;

        if (!currentValue.HasValue)
            return legacyValue;

        int maximumCurrentValue =
            (int.MaxValue - CurrentValueFormatBase - 499) / CurrentValueScale;
        int safeCurrentValue = System.Math.Min(
            System.Math.Max(0, currentValue.Value),
            maximumCurrentValue);

        return CurrentValueFormatBase +
            (safeCurrentValue * CurrentValueScale) +
            (variantValue * 100) +
            grade;
    }

    public static bool TryDecode(
        int value,
        out CardVariant variant,
        out int grade,
        out int? currentValue)
    {
        variant = CardVariant.Standard;
        grade = 0;
        currentValue = null;

        if (value < 1000)
            return false;

        int encoded;
        if (value >= CurrentValueFormatBase)
        {
            encoded = value - CurrentValueFormatBase;
            currentValue = encoded / CurrentValueScale;
            encoded %= CurrentValueScale;
        }
        else
        {
            encoded = value - 1000;
        }

        int variantValue =
            encoded / 100;

        grade =
            encoded % 100;

        if (!System.Enum.IsDefined(
            typeof(CardVariant),
            variantValue))
        {
            return false;
        }

        if (grade < 0 || grade > 10)
            return false;

        variant =
            (CardVariant)variantValue;

        return true;
    }
}

[HarmonyPatch(
    typeof(GrabbableObject),
    "GetItemDataToSave")]
public static class CardSavePatch
{
    [HarmonyPostfix]
    private static void Postfix(
        GrabbableObject __instance,
        ref int __result)
    {
        if (__instance == null)
            return;

        CardInstanceData data =
            __instance.GetComponent<CardInstanceData>();

        if (data == null)
            return;

        CardDefinition? card =
            CardDatabase.GetByItem(
                __instance.itemProperties
            );

        if (card == null)
            return;

        __result =
            CardSaveData.Encode(
                data.Variant,
                data.Grade,
                BalanceConfig.LegacyCardValueBehavior ? null : data.FinalValue
            );

        /* Plugin.Log.LogInfo(
            $"CARD SAVE DATA | " +
            $"CardId={data.CardId} | " +
            $"Variant={data.Variant} | " +
            $"Grade={data.Grade} | " +
            $"Encoded={__result}"
        ); */
    }
}

[HarmonyPatch(
    typeof(GrabbableObject),
    "LoadItemSaveData")]
public static class CardLoadPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        GrabbableObject __instance,
        int saveData)
    {
        if (__instance == null)
            return;

        CardDefinition? card =
            CardDatabase.GetByItem(
                __instance.itemProperties
            );

        if (card == null)
            return;

        if (!CardSaveData.TryDecode(
            saveData,
            out CardVariant variant,
            out int grade,
            out int? currentValue))
        {
            return;
        }

        CardInstanceData data =
            __instance.GetComponent<CardInstanceData>();

        if (data == null)
        {
            Plugin.Log.LogError("CARD LOAD FAILED | CardInstanceData missing from registered prefab.");
            return;
        }

        data.InitializeLoaded(
            card,
            variant,
            grade,
            BalanceConfig.LegacyCardValueBehavior ? null : currentValue
        );

        // CardInstanceData applies physical value after pending initialization
        // and whenever replicated metadata arrives on clients.

        /* Plugin.Log.LogInfo(
            $"CARD LOAD DATA | " +
            $"CardId={card.CardId} | " +
            $"Variant={variant} | " +
            $"Grade={grade} | " +
            $"Encoded={saveData}"
        ); */
    }
}
