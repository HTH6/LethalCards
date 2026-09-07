using HarmonyLib;
using Unity.Netcode;

namespace LethalCards.Grading;

[HarmonyPatch(typeof(TimeOfDay), "OnDayChanged")]
public static class GradingDayPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        if (
            NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        GradingDayManager.AdvanceDay();

        GradingSaveManager.Save();

        Plugin.Log.LogInfo(
            $"GRADING DAY CHANGE DETECTED | " +
            $"CurrentDay={GradingDayManager.CurrentDay}"
        );
    }
}