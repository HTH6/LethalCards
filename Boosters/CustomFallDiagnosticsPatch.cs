using GameNetcodeStuff;
using HarmonyLib;
using LethalCards.Cards;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Boosters;

[HarmonyPatch(
    typeof(GrabbableObject),
    nameof(GrabbableObject.FallToGround),
    new[] { typeof(bool), typeof(bool), typeof(Vector3) })]
internal static class FallToGroundDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(GrabbableObject __instance)
    {
        if (!IsLethalCardsItem(__instance))
            return;

        Item? item = __instance.itemProperties;
        CardInstanceData? card = __instance.GetComponent<CardInstanceData>();

        /* Plugin.Log.LogInfo(
            $"FALL TO GROUND PREFIX | InstanceId={__instance.GetInstanceID()} | " +
            $"Object={__instance.gameObject.name} | CardId={card?.CardId ?? "<none>"} | " +
            $"ItemName={item?.itemName ?? "<null>"} | VerticalOffset={(item != null ? item.verticalOffset : 0f)} | " +
            $"Parent={GetParentName(__instance)} | WorldPosition={__instance.transform.position} | " +
            $"LocalPosition={__instance.transform.localPosition} | IsHeld={__instance.isHeld} | " +
            $"IsHeldByEnemy={__instance.isHeldByEnemy} | PlayerHeldBy={GetPlayerName(__instance)} | " +
            $"HasHitGround={__instance.hasHitGround} | ReachedFloorTarget={__instance.reachedFloorTarget} | " +
            $"StartFallingPosition={__instance.startFallingPosition} | TargetFloorPositionBefore={__instance.targetFloorPosition}"); */
    }

    [HarmonyPostfix]
    private static void Postfix(GrabbableObject __instance)
    {
        if (!IsLethalCardsItem(__instance))
            return;

        Item? item = __instance.itemProperties;

        /* Plugin.Log.LogInfo(
            $"FALL TO GROUND POSTFIX | InstanceId={__instance.GetInstanceID()} | " +
            $"Object={__instance.gameObject.name} | ItemName={item?.itemName ?? "<null>"} | " +
            $"VerticalOffset={(item != null ? item.verticalOffset : 0f)} | Parent={GetParentName(__instance)} | " +
            $"WorldPosition={__instance.transform.position} | LocalPosition={__instance.transform.localPosition} | " +
            $"IsHeld={__instance.isHeld} | PlayerHeldBy={GetPlayerName(__instance)} | " +
            $"StartFallingPosition={__instance.startFallingPosition} | " +
            $"TargetFloorPositionAfter={__instance.targetFloorPosition} | HasHitGround={__instance.hasHitGround} | " +
            $"ReachedFloorTarget={__instance.reachedFloorTarget}"); */

        CustomSpawnPlacement.TryApplyFloorTargetCorrection(__instance);
    }

    internal static bool IsLethalCardsItem(GrabbableObject? grabbable) =>
        grabbable != null &&
        (grabbable.GetComponent<CardInstanceData>() != null ||
         grabbable.GetComponent<BoosterPackBehaviour>() != null ||
         grabbable.GetComponent<BoosterBoxBehaviour>() != null);

    internal static string GetParentName(GrabbableObject grabbable) =>
        grabbable.transform.parent != null
            ? grabbable.transform.parent.name
            : "<none>";

    internal static void LogFloorState(string label, GrabbableObject grabbable)
    {
        Item? item = grabbable.itemProperties;
        /* Plugin.Log.LogInfo(
            $"{label} | InstanceId={grabbable.GetInstanceID()} | " +
            $"WorldPosition={grabbable.transform.position} | Parent={GetParentName(grabbable)} | " +
            $"StartFallingPosition={grabbable.startFallingPosition} | " +
            $"TargetFloorPosition={grabbable.targetFloorPosition} | " +
            $"VerticalOffset={(item != null ? item.verticalOffset : 0f)} | " +
            $"HasHitGround={grabbable.hasHitGround} | ReachedFloorTarget={grabbable.reachedFloorTarget}"); */
    }

    private static string GetPlayerName(GrabbableObject grabbable) =>
        grabbable.playerHeldBy != null
            ? grabbable.playerHeldBy.playerUsername
            : "<none>";
}

[HarmonyPatch(
    typeof(PlayerControllerB),
    nameof(PlayerControllerB.DiscardHeldObject),
    new[] { typeof(bool), typeof(NetworkObject), typeof(Vector3), typeof(bool) })]
internal static class PlayerDiscardDiagnosticsPatch
{
    [HarmonyPrefix]
    private static void Prefix(PlayerControllerB __instance, out GrabbableObject? __state)
    {
        GrabbableObject? held = __instance.currentlyHeldObjectServer;
        __state = held;
        if (!FallToGroundDiagnosticsPatch.IsLethalCardsItem(held))
            return;

        Item? item = held!.itemProperties;
        /* Plugin.Log.LogInfo(
            $"PLAYER DROP ENTRY | InstanceId={held.GetInstanceID()} | Object={held.gameObject.name} | " +
            $"ItemName={item?.itemName ?? "<null>"} | WorldPosition={held.transform.position} | " +
            $"ParentBefore={FallToGroundDiagnosticsPatch.GetParentName(held)} | " +
            $"VerticalOffset={(item != null ? item.verticalOffset : 0f)}"); */

        // FallToGroundDiagnosticsPatch.LogFloorState("PLAYER DROP FLOOR STATE BEFORE", held);
    }

    [HarmonyPostfix]
    private static void Postfix(GrabbableObject? __state)
    {
        if (!FallToGroundDiagnosticsPatch.IsLethalCardsItem(__state))
            return;

        // FallToGroundDiagnosticsPatch.LogFloorState("PLAYER DROP FLOOR STATE AFTER", __state!);
    }
}
