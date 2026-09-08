using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Networking;

// Registered on card and booster prefabs before LethalLib registers them.
public class NetworkItemConsumption : NetworkBehaviour
{
    private bool consumed;

    public static bool IsValidHolder(PlayerControllerB player, GrabbableObject item)
    {
        return player != null && player.isPlayerControlled && !player.isPlayerDead &&
            item != null && item.IsSpawned && item.heldByPlayerOnServer &&
            item.OwnerClientId == player.actualClientId &&
            player.currentlyHeldObjectServer == item;
    }

    public void ConsumeServer()
    {
        if (!IsServer || !IsSpawned || consumed)
            return;

        consumed = true;
        ClearInventoryClientRpc();
        ClearInventory();
        NetworkObject.Despawn(true);
    }

    [ClientRpc]
    private void ClearInventoryClientRpc()
    {
        ClearInventory();
    }

    private void ClearInventory()
    {
        GrabbableObject item = GetComponent<GrabbableObject>();
        if (item == null || StartOfRound.Instance == null)
            return;

        foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
        {
            if (player == null)
                continue;

            bool held = player.currentlyHeldObjectServer == item;
            bool found = held;
            for (int slot = 0; slot < player.ItemSlots.Length; slot++)
            {
                if (player.ItemSlots[slot] != item)
                    continue;
                found = true;
                player.ItemSlots[slot] = null;
                if (player.IsOwner && HUDManager.Instance != null && slot < HUDManager.Instance.itemSlotIcons.Length)
                    HUDManager.Instance.itemSlotIcons[slot].enabled = false;
            }

            if (player.ItemOnlySlot == item)
            {
                found = true;
                player.ItemOnlySlot = null;
                if (player.IsOwner && HUDManager.Instance != null)
                    HUDManager.Instance.itemOnlySlotIcon.enabled = false;
            }

            if (!found)
                continue;

            player.carryWeight = Mathf.Clamp(player.carryWeight - Mathf.Clamp(item.itemProperties.weight - 1f, 0f, 10f), 1f, 10f);
            if (held)
            {
                player.currentlyHeldObjectServer = null;
                player.isHoldingObject = false;
                player.twoHanded = false;
                player.twoHandedAnimation = false;
                player.activatingItem = false;
                player.playerBodyAnimator.SetBool("cancelHolding", true);
                player.playerBodyAnimator.ResetTrigger("Throw");
                if (player.IsOwner && HUDManager.Instance != null)
                {
                    HUDManager.Instance.holdingTwoHandedItem.enabled = false;
                    HUDManager.Instance.ClearControlTips();
                }
            }

            if (player.IsOwner)
                StartOfRound.Instance.SendChangedWeightEvent();
        }

        item.isHeld = false;
        item.heldByPlayerOnServer = false;
        item.playerHeldBy = null;
        item.parentObject = null;
    }
}
