using System;
using GameNetcodeStuff;
using HarmonyLib;
using LethalCards.Cards;
using UnityEngine;

namespace LethalCards.ShipDisplay;

[HarmonyPatch(typeof(PlayerControllerB))]
internal static class ShipCardDisplayInteractionPatch
{
    [HarmonyPatch("BeginGrabObject"), HarmonyPrefix]
    private static bool BeginGrabObjectPrefix(PlayerControllerB __instance)
    {
        if (!TryGetTarget(
                __instance,
                out ShipCardDisplayController controller,
                out int targetSlot))
        {
            return true;
        }

        if (controller.IsLocalInteractionPending())
            return false;

        if (!TryGetSelectedItem(__instance, out GrabbableObject? selectedItem))
            return false;

        GrabbableObject? heldCard = GetCard(selectedItem);
        if (heldCard != null)
        {
            // RequestDeposit performs a second selected-slot lookup immediately
            // before constructing the NetworkObjectReference.
            controller.RequestDeposit(__instance);
            return false;
        }

        // The cabinet owns this hit. Suppress vanilla grab handling even when
        // the player has an unrelated item, or is looking between slot targets.
        if (selectedItem != null || targetSlot < 0)
        {
            return false;
        }

        if (controller.TryGetSlotState(targetSlot, out ShipCardDisplaySlotState state) &&
            state.Occupied && HasSelectedInventoryCapacity(__instance))
        {
            controller.RequestWithdrawal(__instance, targetSlot);
        }

        return false;
    }

    [HarmonyPatch("SetHoverTipAndCurrentInteractTrigger"), HarmonyPostfix]
    private static void HoverTipPostfix(PlayerControllerB __instance)
    {
        if (!TryGetTarget(
                __instance,
                out ShipCardDisplayController controller,
                out int targetSlot))
        {
            return;
        }

        if (controller.IsLocalInteractionPending() ||
            !TryGetSelectedItem(__instance, out GrabbableObject? selectedItem))
        {
            ClearPrompt(__instance);
            return;
        }

        string prompt = string.Empty;
        GrabbableObject? heldCard = GetCard(selectedItem);
        if (heldCard != null)
        {
            CardInstanceData? data = heldCard.GetComponent<CardInstanceData>();
            CardDefinition? definition = data == null
                ? null
                : CardDatabase.GetById(data.CardId);
            if (definition != null)
            {
                int fixedSlot = definition.SetNumber - 1;
                bool occupied =
                    controller.TryGetSlotState(fixedSlot, out ShipCardDisplaySlotState state) &&
                    state.Occupied;
                prompt = occupied ? "Swap Card" : "Display Card";
            }
        }
        else if (selectedItem == null &&
                 targetSlot >= 0 &&
                 controller.TryGetSlotState(targetSlot, out ShipCardDisplaySlotState state))
        {
            if (state.Occupied)
                prompt = HasSelectedInventoryCapacity(__instance)
                    ? "Take Card"
                    : "Inventory Full";
        }

        if (string.IsNullOrEmpty(prompt))
        {
            ClearPrompt(__instance);
            return;
        }

        if (prompt is "Display Card" or "Swap Card" or "Take Card")
        {
            prompt += StartOfRound.Instance != null &&
                      StartOfRound.Instance.localPlayerUsingController
                ? " : [X]"
                : " : [E]";
        }

        if (__instance.cursorTip != null)
            __instance.cursorTip.text = prompt;
        if (__instance.cursorIcon != null)
        {
            __instance.cursorIcon.enabled = true;
            __instance.cursorIcon.sprite = __instance.grabItemIcon;
        }
    }

    internal static GrabbableObject? GetHeldCard(PlayerControllerB player)
    {
        return TryGetSelectedItem(player, out GrabbableObject? selectedItem)
            ? GetCard(selectedItem)
            : null;
    }

    internal static GrabbableObject? GetSelectedCard(PlayerControllerB player) =>
        GetHeldCard(player);

    private static GrabbableObject? GetCard(GrabbableObject? item)
    {
        if (item == null || !item.IsSpawned)
            return null;

        CardInstanceData? data = item.GetComponent<CardInstanceData>();
        return data != null && CardDatabase.GetById(data.CardId) != null
            ? item
            : null;
    }

    private static bool TryGetSelectedItem(
        PlayerControllerB player,
        out GrabbableObject? selectedItem)
    {
        selectedItem = null;
        if (!IsGameplayContextValid(player) ||
            player.currentItemSlot < 0 ||
            player.currentItemSlot >= player.ItemSlots.Length)
        {
            return false;
        }

        GrabbableObject? candidate = player.ItemSlots[player.currentItemSlot];
        // Normalize Unity's destroyed-object pseudo-null so later code never
        // dereferences an inventory entry being torn down.
        selectedItem = candidate == null ? null : candidate;
        return true;
    }

    private static bool IsGameplayContextValid(PlayerControllerB player)
    {
        Unity.Netcode.NetworkManager? manager =
            Unity.Netcode.NetworkManager.Singleton;
        return player != null && player.gameObject != null &&
            player.gameObject.activeInHierarchy && player.IsSpawned && player.IsOwner &&
            player.ItemSlots != null && StartOfRound.Instance != null &&
            GameNetworkManager.Instance != null &&
            GameNetworkManager.Instance.localPlayerController == player &&
            manager != null && manager.IsListening && !manager.ShutdownInProgress;
    }

    private static void ClearPrompt(PlayerControllerB player)
    {
        if (player == null)
            return;
        if (player.cursorTip != null)
            player.cursorTip.text = string.Empty;
        if (player.cursorIcon != null)
            player.cursorIcon.enabled = false;
    }

    private static bool HasSelectedInventoryCapacity(PlayerControllerB player) =>
        player != null && player.ItemSlots != null &&
        player.currentItemSlot >= 0 &&
        player.currentItemSlot < player.ItemSlots.Length &&
        player.ItemSlots[player.currentItemSlot] == null;

    private static bool TryGetTarget(
        PlayerControllerB player,
        out ShipCardDisplayController controller,
        out int targetSlot)
    {
        controller = null!;
        targetSlot = -1;
        if (!IsGameplayContextValid(player) || player.gameplayCamera == null ||
            player.isPlayerDead || player.disableInteract)
        {
            return false;
        }

        Ray ray = new(
            player.gameplayCamera.transform.position,
            player.gameplayCamera.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            player.grabDistance,
            ~0,
            QueryTriggerInteraction.Collide);
        if (hits.Length == 0)
            return false;

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        ShipCardDisplayController? candidate = null;
        foreach (RaycastHit hit in hits)
        {
            Collider? collider = hit.collider;
            if (collider == null ||
                collider.transform.IsChildOf(player.transform))
            {
                continue;
            }

            ShipCardDisplayController? hitController =
                collider.GetComponentInParent<ShipCardDisplayController>();
            if (hitController != null && hitController.IsSpawned)
            {
                candidate = hitController;
                break;
            }

            // Unrelated triggers do not occlude interaction. A solid collider does.
            if (!collider.isTrigger)
                return false;
        }

        if (candidate == null)
            return false;

        // Once the cabinet itself is the unobstructed target, select a slot by
        // projecting its authored anchor positions against the camera ray in
        // cabinet-local space. This is independent of broad furniture/scan
        // colliders and remains correct after the cabinet is moved or rotated.
        candidate.TrySelectSlotFromRay(ray, player.grabDistance, out targetSlot);

        controller = candidate;
        return true;
    }
}
