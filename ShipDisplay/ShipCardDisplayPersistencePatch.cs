using HarmonyLib;

namespace LethalCards.ShipDisplay;

[HarmonyPatch]
internal static class ShipCardDisplayPersistencePatch
{
    [HarmonyPatch(typeof(StartOfRound), "StartGame"), HarmonyPrefix]
    private static void StartGamePrefix() =>
        ShipCardDisplayController.ResetCrewWipeGuard();

    [HarmonyPatch(typeof(RoundManager), "DespawnPropsAtEndOfRound"), HarmonyPrefix]
    private static void DespawnPropsPrefix()
    {
        if (StartOfRound.Instance?.allPlayersDead == true)
            ShipCardDisplayController.ClearAuthoritativeStorageForCrewWipe();
    }
}
