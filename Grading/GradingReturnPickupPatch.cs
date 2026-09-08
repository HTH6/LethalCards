using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;

namespace LethalCards.Grading;

[HarmonyPatch(typeof(PlayerControllerB), "GrabObjectServerRpc")]
public static class GradingReturnPickupPatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerControllerB __instance, NetworkObjectReference grabbedObject, out bool __state)
    {
        __state = __instance.IsServer && grabbedObject.TryGet(out NetworkObject obj) &&
            obj.GetComponent<GradingReturnData>() != null &&
            obj.GetComponent<GrabbableObject>() is GrabbableObject item &&
            !item.heldByPlayerOnServer;
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerControllerB __instance, NetworkObjectReference grabbedObject, bool __state)
    {
        // Vanilla marks heldByPlayerOnServer and transfers ownership only on acceptance.
        if (!__state || !__instance.IsServer || !grabbedObject.TryGet(out NetworkObject obj) ||
            obj.OwnerClientId != __instance.actualClientId)
            return;
        obj.GetComponent<GradingReturnData>()?.TryClaim();
    }
}
