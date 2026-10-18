using HarmonyLib;

namespace LethalCards.Museum;

[HarmonyPatch(typeof(StartOfRound), "ChangeLevel")]
internal static class CompanyMuseumSceneResetPatch
{
    [HarmonyPrefix]
    private static void Prefix() => CompanyMuseumDisplaySpawner.Reset();
}
