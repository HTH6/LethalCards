using GameNetcodeStuff;
using HarmonyLib;
using LethalCards.Cards;
using UnityEngine;

namespace LethalCards.Binders;

[HarmonyPatch(typeof(PlayerControllerB))]
internal static class CardBinderInteractionPatch
{
    private static readonly AccessTools.FieldRef<PlayerControllerB, int> InteractableObjectsMask =
        AccessTools.FieldRefAccess<PlayerControllerB, int>("interactableObjectsMask");

    [HarmonyPatch("BeginGrabObject"), HarmonyPrefix]
    private static bool BeginGrabObjectPrefix(PlayerControllerB __instance)
    {
        if (!TryGetTargetBinder(__instance, out CardBinderBehaviour binder) ||
            GetHeldCard(__instance) == null)
            return true;

        binder.RequestStoreHeldCard(__instance);
        return false;
    }

    [HarmonyPatch("SetHoverTipAndCurrentInteractTrigger"), HarmonyPostfix]
    private static void HoverTipPostfix(PlayerControllerB __instance)
    {
        if (GetHeldCard(__instance) == null ||
            !TryGetTargetBinder(__instance, out _))
            return;

        if (__instance.cursorTip != null)
            __instance.cursorTip.text = StartOfRound.Instance != null &&
                StartOfRound.Instance.localPlayerUsingController
                    ? "Store Card : [X]"
                    : "Store Card : [E]";
        if (__instance.cursorIcon != null)
        {
            __instance.cursorIcon.enabled = true;
            __instance.cursorIcon.sprite = __instance.grabItemIcon;
        }
    }

    internal static GrabbableObject? GetHeldCard(PlayerControllerB player)
    {
        if (player == null || !player.isHoldingObject)
            return null;

        GrabbableObject? held = player.currentlyHeldObject;
        if (held == null)
            held = player.currentlyHeldObjectServer;
        if (held == null)
            return null;

        CardInstanceData? data = held.GetComponent<CardInstanceData>();
        return data != null && CardDatabase.GetById(data.CardId) != null
            ? held
            : null;
    }

    private static bool TryGetTargetBinder(
        PlayerControllerB player,
        out CardBinderBehaviour binder)
    {
        binder = null!;
        if (player == null || !player.IsOwner || player.gameplayCamera == null ||
            player.isPlayerDead || player.disableInteract)
            return false;

        Ray ray = new(
            player.gameplayCamera.transform.position,
            player.gameplayCamera.transform.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, player.grabDistance,
                InteractableObjectsMask(player), QueryTriggerInteraction.Collide) ||
            hit.collider == null || !hit.collider.CompareTag("PhysicsProp"))
            return false;

        CardBinderBehaviour? target = hit.collider.GetComponentInParent<CardBinderBehaviour>();
        GrabbableObject? grabbable = target != null ? target.GetComponent<GrabbableObject>() : null;
        if (target == null || !target.IsSpawned || grabbable == null ||
            grabbable.isHeld || grabbable.heldByPlayerOnServer)
            return false;

        if (Physics.Linecast(
                player.gameplayCamera.transform.position,
                hit.collider.transform.position + player.transform.up * 0.16f,
                1 << 30,
                QueryTriggerInteraction.Ignore))
            return false;

        binder = target;
        return true;
    }
}
