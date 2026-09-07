using HarmonyLib;

namespace LethalCards.Grading;

[HarmonyPatch(
    typeof(GrabbableObject),
    "GrabItem"
)]
public static class
    GradingReturnPickupPatch
{
    [HarmonyPostfix]
    private static void Postfix(
        GrabbableObject __instance)
    {
        if (__instance == null)
            return;

        GradingReturnData returnData =
            __instance.GetComponent<
                GradingReturnData>();

        if (returnData == null)
            return;

        returnData.TryClaim();
    }
}