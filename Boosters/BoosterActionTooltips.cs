using HarmonyLib;
using UnityEngine.InputSystem;

namespace LethalCards.Boosters;

// Uses the same action asset as PlayerControllerB.ActivateItem_performed's subscription.
[HarmonyPatch(typeof(GrabbableObject), "SetControlTipsForItem")]
internal static class BoosterActionTooltips
{
    internal static string Format(string actionText)
    {
        InputAction? action = InputSystem.actions?.FindAction("ActivateItem", false);
        bool controller = StartOfRound.Instance != null && StartOfRound.Instance.localPlayerUsingController;
        if (action != null)
        {
            foreach (InputControl control in action.controls)
            {
                bool matchingDevice = controller ? control.device is Gamepad :
                    control.device is Keyboard || control.device is Mouse;
                if (!matchingDevice)
                    continue;
                int bindingIndex = action.GetBindingIndexForControl(control);
                if (bindingIndex < 0)
                    continue;
                string label = action.GetBindingDisplayString(bindingIndex);
                if (string.IsNullOrWhiteSpace(label))
                    continue;
                // Vanilla rewrites the literal RMB token to LMB; preserve a genuine RMB rebind.
                if (label == "RMB")
                    label = "Right Mouse Button";
                return $"{actionText} : [{label}]";
            }
        }
        // Startup may precede the action asset. Vanilla also recognizes this controller token.
        return $"{actionText} : [LMB]";
    }

    [HarmonyPrefix]
    private static void Prefix(GrabbableObject __instance)
    {
        // Refresh on equip/control-tip refresh, after saved rebindings have loaded.
        // Let the original method render Item.toolTips and its normal Drop prompt.
        if (__instance is BoosterPackBehaviour)
            Plugin.EnsureActionTooltip(__instance.itemProperties, "Rip Pack");
        else if (__instance is BoosterBoxBehaviour)
            Plugin.EnsureActionTooltip(__instance.itemProperties, "Open Box");
    }
}
