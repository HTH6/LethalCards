using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace LethalCards.Boosters;

// Temporary diagnostics only: observe vanilla routing without bypassing its checks.
[HarmonyPatch(typeof(PlayerControllerB), "ActivateItem_performed")]
internal static class BoosterBoxInputDiagnostics
{
    [HarmonyPrefix]
    private static void Prefix(PlayerControllerB __instance, InputAction.CallbackContext context)
    {
        if (!context.performed || __instance != GameNetworkManager.Instance?.localPlayerController)
            return;
        GrabbableObject held = __instance.currentlyHeldObjectServer;
        if (held == null)
            return;
        BoosterBoxBehaviour box = held.GetComponent<BoosterBoxBehaviour>();
        if (box == null)
            return;
        /* Plugin.Log.LogInfo($"BOOSTER BOX INPUT | Type={box.BoxType} | HeldComponent={held.GetType().FullName} | " +
            $"ActivationComponent={held.GetComponent<GrabbableObject>()?.GetType().FullName} | " +
            $"PlayerOwner={__instance.IsOwner} | Holding={__instance.isHoldingObject} | " +
            $"Dead={__instance.isPlayerDead} | SpecialMenu={__instance.inSpecialMenu} | " +
            $"Terminal={__instance.inTerminalMenu} | Typing={__instance.isTypingChat} | " +
            $"GrabAnimation={__instance.isGrabbingObjectAnimation} | SpecialInteraction={__instance.inSpecialInteractAnimation}"); */
    }
}

[HarmonyPatch(typeof(GrabbableObject), "UseItemOnClient")]
internal static class BoosterBoxUseDiagnostics
{
    [HarmonyPrefix]
    private static void Prefix(GrabbableObject __instance, bool __0, float ___currentUseCooldown, out int __state)
    {
        __state = -1;
        BoosterBoxBehaviour box = __instance.GetComponent<BoosterBoxBehaviour>();
        if (box == null)
            return;
        __state = box.DiagnosticActivationCount;
        /* Plugin.Log.LogInfo($"BOOSTER BOX USE ENTER | ComponentType={__instance.GetType().FullName} | " +
            $"ButtonDown={__0} | IsOwner={__instance.IsOwner} | Owner={__instance.OwnerClientId} | " +
            $"HeldBy={__instance.playerHeldBy?.actualClientId} | Parent={__instance.parentObject?.name} | " +
            $"RemainingCooldown={___currentUseCooldown} | UseCooldown={__instance.useCooldown} | " +
            $"ItemProperties={__instance.itemProperties?.name} | RequiresBattery={__instance.itemProperties?.requiresBattery} | " +
            $"BatteryPresent={__instance.insertedBattery != null} | BatteryEmpty={__instance.insertedBattery?.empty}"); */
    }

    [HarmonyPostfix]
    private static void Postfix(GrabbableObject __instance, int __state)
    {
        BoosterBoxBehaviour box = __instance.GetComponent<BoosterBoxBehaviour>();
        if (box != null && __state >= 0 && box.DiagnosticActivationCount == __state)
        {
            // Plugin.Log.LogInfo("BOOSTER BOX USE EXIT WITHOUT ACTIVATE | Check component type, ownership, cooldown, and battery state.");
        }
    }
}
