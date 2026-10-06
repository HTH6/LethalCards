using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalCards.Boosters;

internal static class BoosterDiagnostics
{
    internal static void Log(string stage, BoosterPackBehaviour pack,
        PlayerControllerB? player = null, string detail = "")
    {
        /* Temporary diagnostic body retained for later testing.
        NetworkManager manager = NetworkManager.Singleton;
        player ??= GameNetworkManager.Instance?.localPlayerController;
        Plugin.Log.LogInfo(
            $"BOOSTER TRACE | Stage={stage} | Frame={Time.frameCount} | " +
            $"Object={pack.NetworkObjectId} | Type={pack.PackType} | " +
            $"LocalClient={manager?.LocalClientId} | Server={pack.IsServer} | " +
            $"Spawned={pack.IsSpawned} | Owner={pack.OwnerClientId} | IsOwner={pack.IsOwner} | " +
            $"Player={player?.actualClientId} | PlayerHeldBy={pack.playerHeldBy?.actualClientId} | " +
            $"HeldObjectMatches={(player != null && player.currentlyHeldObjectServer == pack)} | " +
            $"Held={pack.isHeld} | HeldOnServer={pack.heldByPlayerOnServer} | Pocketed={pack.isPocketed} | " +
            $"Controlled={player?.isPlayerControlled} | Dead={player?.isPlayerDead} | {detail}");
        */
    }
}

// [HarmonyPatch(typeof(PlayerControllerB), "ActivateItem_performed")]
internal static class BoosterInputDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerControllerB __instance, InputAction.CallbackContext context,
        float ___timeSinceSwitchingSlots, QuickMenuManager ___quickMenuManager)
    {
        if (!context.performed || __instance != GameNetworkManager.Instance?.localPlayerController)
            return;
        // Also find an equipped pack if currentlyHeldObjectServer is unexpectedly null.
        GrabbableObject equipped = __instance.currentlyHeldObjectServer;
        if (equipped == null && __instance.ItemSlots != null && __instance.currentItemSlot >= 0 &&
            __instance.currentItemSlot < __instance.ItemSlots.Length)
            equipped = __instance.ItemSlots[__instance.currentItemSlot];
        if (equipped is not BoosterPackBehaviour pack)
            return;
        /* BoosterDiagnostics.Log("INPUT", pack, __instance,
            $"PlayerIsOwner={__instance.IsOwner} | HostPlayer={__instance.isHostPlayerObject} | " +
            $"HoldingObject={__instance.isHoldingObject} | Slot={__instance.currentItemSlot} | " +
            $"SinceSlotSwitch={___timeSinceSwitchingSlots:F3} | QuickMenu={___quickMenuManager?.isMenuOpen} | " +
            $"SpecialMenu={__instance.inSpecialMenu} | Terminal={__instance.inTerminalMenu} | " +
            $"Typing={__instance.isTypingChat} | GrabAnimation={__instance.isGrabbingObjectAnimation} | " +
            $"SpecialInteraction={__instance.inSpecialInteractAnimation}"); */
    }
}

// [HarmonyPatch(typeof(GrabbableObject), "UseItemOnClient")]
internal static class BoosterUseDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(GrabbableObject __instance, bool __0, float ___currentUseCooldown, out int __state)
    {
        __state = -1;
        if (__instance is not BoosterPackBehaviour pack)
            return;
        __state = pack.DiagnosticActivationCount;
        /* BoosterDiagnostics.Log("USE_ENTER", pack, detail:
            $"ButtonDown={__0} | UseCooldown={pack.useCooldown} | RemainingCooldown={___currentUseCooldown} | " +
            $"RequiresBattery={pack.itemProperties.requiresBattery} | " +
            $"BatteryPresent={pack.insertedBattery != null} | BatteryEmpty={pack.insertedBattery?.empty} | " +
            $"HoldButtonUse={pack.itemProperties.holdButtonUse} | " +
            $"SyncUse={pack.itemProperties.syncUseFunction} | BeingUsed={pack.isBeingUsed}"); */
    }

    [HarmonyPostfix]
    private static void Postfix(GrabbableObject __instance, int __state)
    {
        if (__instance is BoosterPackBehaviour pack && __state >= 0 &&
            pack.DiagnosticActivationCount == __state)
        {
            /* BoosterDiagnostics.Log("USE_EXIT_WITHOUT_ACTIVATE", pack,
                detail: "Vanilla use returned before ItemActivate; inspect ownership/cooldown/battery state."); */
        }
    }
}
