using System;
using GameNetcodeStuff;
using HarmonyLib;
using LethalCards.Cards;
using UnityEngine;

namespace LethalCards.Museum;

[HarmonyPatch(typeof(PlayerControllerB))]
internal static class CompanyMuseumInteractionPatch
{
    [HarmonyPatch("BeginGrabObject"), HarmonyPrefix]
    private static bool BeginGrabObjectPrefix(PlayerControllerB __instance)
    {
        if (!TryGetTarget(__instance, out CompanyMuseumDisplayController controller))
            return true;
        if (controller.IsLocalRequestPending())
            return false;
        CardInstanceData? card = GetSelectedCard(__instance);
        if (card != null && controller.GetPrompt(card) != "Already Registered")
            controller.RequestRegistration();
        return false;
    }

    [HarmonyPatch("SetHoverTipAndCurrentInteractTrigger"), HarmonyPostfix]
    private static void HoverTipPostfix(PlayerControllerB __instance)
    {
        if (!TryGetTarget(__instance, out CompanyMuseumDisplayController controller))
            return;
        CardInstanceData? card = GetSelectedCard(__instance);
        string prompt = controller.IsLocalRequestPending() || card == null
            ? string.Empty
            : controller.GetPrompt(card);
        if (string.IsNullOrEmpty(prompt))
        {
            ClearPrompt(__instance);
            return;
        }
        if (prompt != "Already Registered")
            prompt += StartOfRound.Instance?.localPlayerUsingController == true
                ? " : [X]"
                : " : [E]";
        if (__instance.cursorTip != null)
            __instance.cursorTip.text = prompt;
        if (__instance.cursorIcon != null)
        {
            __instance.cursorIcon.enabled = true;
            __instance.cursorIcon.sprite = __instance.grabItemIcon;
        }
    }

    private static CardInstanceData? GetSelectedCard(PlayerControllerB player)
    {
        if (!IsGameplayContextValid(player) || player.ItemSlots == null ||
            player.currentItemSlot < 0 || player.currentItemSlot >= player.ItemSlots.Length)
            return null;
        GrabbableObject? selected = player.ItemSlots[player.currentItemSlot];
        if (selected == null || !selected.IsSpawned)
            return null;
        CardInstanceData? data = selected.GetComponent<CardInstanceData>();
        return data != null && CardDatabase.GetById(data.CardId) != null ? data : null;
    }

    private static bool TryGetTarget(
        PlayerControllerB player,
        out CompanyMuseumDisplayController controller)
    {
        controller = null!;
        if (!IsGameplayContextValid(player) || player.gameplayCamera == null ||
            player.isPlayerDead || player.disableInteract)
            return false;
        RaycastHit[] hits = Physics.RaycastAll(
            new Ray(player.gameplayCamera.transform.position, player.gameplayCamera.transform.forward),
            player.grabDistance + 1f,
            ~0,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            Collider? collider = hit.collider;
            if (collider == null || collider.transform.IsChildOf(player.transform))
                continue;
            CompanyMuseumDisplayController? candidate =
                collider.GetComponentInParent<CompanyMuseumDisplayController>();
            if (candidate != null)
            {
                if (candidate.OwnsCollider(collider))
                {
                    controller = candidate;
                    return true;
                }

                // The solid museum body can be hit before its overlapping
                // registration trigger. It belongs to the same target and is
                // not an occluder, so continue through this museum's colliders.
                if (collider.transform.IsChildOf(candidate.transform))
                    continue;
            }

            // A nearby vanilla interaction trigger (including a grading
            // pedestal) owns the aim and must not fall through to the museum.
            if (collider.GetComponentInParent<InteractTrigger>() != null ||
                !collider.isTrigger)
                return false;
        }
        return false;
    }

    private static bool IsGameplayContextValid(PlayerControllerB player)
    {
        Unity.Netcode.NetworkManager? manager = Unity.Netcode.NetworkManager.Singleton;
        return player != null && player.gameObject != null &&
               player.gameObject.activeInHierarchy && player.IsSpawned && player.IsOwner &&
               StartOfRound.Instance != null && GameNetworkManager.Instance != null &&
               GameNetworkManager.Instance.localPlayerController == player &&
               manager != null && manager.IsListening && !manager.ShutdownInProgress;
    }

    private static void ClearPrompt(PlayerControllerB player)
    {
        if (player.cursorTip != null)
            player.cursorTip.text = string.Empty;
        if (player.cursorIcon != null)
            player.cursorIcon.enabled = false;
    }
}
