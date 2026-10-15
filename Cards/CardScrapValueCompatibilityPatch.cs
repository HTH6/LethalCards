using HarmonyLib;

namespace LethalCards.Cards;

[HarmonyPatch(typeof(GrabbableObject), nameof(GrabbableObject.SetScrapValue))]
internal static class CardScrapValueCompatibilityPatch
{
    [HarmonyPostfix]
    private static void Postfix(GrabbableObject __instance)
    {
        if (__instance == null || CardInstanceData.IsApplyingAuthoritativeCardValue)
            return;

        CardInstanceData? data = __instance.GetComponent<CardInstanceData>();
        if (data == null || string.IsNullOrWhiteSpace(data.CardId))
            return;

        data.HandleExternalScrapValueChange(__instance.scrapValue);
    }
}
