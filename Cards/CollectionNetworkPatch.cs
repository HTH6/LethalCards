using HarmonyLib;

namespace LethalCards.Cards;

[HarmonyPatch(
    typeof(StartOfRound),
    "Start"
)]
public static class CollectionNetworkPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        CollectionNetworkSync.Initialize();
    }
}