using HarmonyLib;

namespace LethalCards.Grading;

[HarmonyPatch(typeof(StartOfRound), "ChangeLevel")]
public static class GradingSceneResetPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        GradingReturnSpawner.Reset();
        GradingPedestalSpawner.Reset();

        Plugin.Log.LogInfo(
            "GRADING SCENE RESET | " +
            "Return and pedestal state cleared."
        );
    }
}