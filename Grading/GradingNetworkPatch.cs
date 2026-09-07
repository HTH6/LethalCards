using HarmonyLib;

namespace LethalCards.Grading;

[HarmonyPatch(
    typeof(StartOfRound),
    "Start"
)]
public static class GradingNetworkPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        GradingNetworkSync.Initialize();
    }
}