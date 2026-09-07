using HarmonyLib;

namespace LethalCards.Cards;

public static class CardSaveData
{
    public static int Encode(
        CardVariant variant,
        int grade)
    {
        int variantValue =
            (int)variant;

        return
            1000 +
            (variantValue * 100) +
            grade;
    }

    public static bool TryDecode(
        int value,
        out CardVariant variant,
        out int grade)
    {
        variant = CardVariant.Standard;
        grade = 0;

        if (value < 1000)
            return false;

        int encoded =
            value - 1000;

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
                data.Grade
            );

        Plugin.Log.LogInfo(
            $"CARD SAVE DATA | " +
            $"CardId={data.CardId} | " +
            $"Variant={data.Variant} | " +
            $"Grade={data.Grade} | " +
            $"Encoded={__result}"
        );
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
            out int grade))
        {
            return;
        }

        CardInstanceData data =
            __instance.GetComponent<CardInstanceData>();

        if (data == null)
        {
            data =
                __instance.gameObject
                    .AddComponent<CardInstanceData>();
        }

        data.InitializeLoaded(
            card,
            variant,
            grade
        );

        __instance.SetScrapValue(
            data.FinalValue
        );

        Plugin.Log.LogInfo(
            $"CARD LOAD DATA | " +
            $"CardId={card.CardId} | " +
            $"Variant={variant} | " +
            $"Grade={grade} | " +
            $"Encoded={saveData}"
        );
    }
}