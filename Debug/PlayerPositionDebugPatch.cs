using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalCards.Debugging;

[HarmonyPatch(typeof(PlayerControllerB), "Update")]
public static class PlayerPositionDebugPatch
{
    private const float DebugRayDistance = 20f;
    private const float VanillaPlacementRayDistance = 4f;

    private static readonly FieldInfo? PlacementMaskField =
        AccessTools.Field(typeof(ShipBuildModeManager), "placementMask");
    private static readonly FieldInfo? PlacementMaskAndBlockersField =
        AccessTools.Field(typeof(ShipBuildModeManager), "placementMaskAndBlockers");
    private static readonly FieldInfo? CanConfirmPositionField =
        AccessTools.Field(typeof(ShipBuildModeManager), "CanConfirmPosition");
    private static readonly FieldInfo? PlacingObjectField =
        AccessTools.Field(typeof(ShipBuildModeManager), "placingObject");
    private static readonly FieldInfo? CurrentColliderField =
        AccessTools.Field(typeof(ShipBuildModeManager), "currentCollider");

    [HarmonyPostfix]
    private static void Postfix(
        PlayerControllerB __instance)
    {
        if (!Plugin.EnableDebugLogging || __instance == null)
            return;

        if (
            GameNetworkManager.Instance == null ||
            __instance !=
                GameNetworkManager.Instance.localPlayerController)
        {
            return;
        }

        Keyboard keyboard =
            Keyboard.current;

        if (keyboard == null)
            return;

        if (keyboard.pKey.wasPressedThisFrame)
        {
            Transform playerTransform = __instance.transform;
            Transform? shipTransform = StartOfRound.Instance?.elevatorTransform;

            if (shipTransform == null)
            {
                Plugin.Log.LogInfo(
                    $"PLAYER POSITION MARKER | " +
                    $"WorldPosition={Format(playerTransform.position)} | " +
                    $"WorldEuler={Format(playerTransform.eulerAngles)} | " +
                    $"WorldForward={Format(playerTransform.forward)} | " +
                    "ShipLocal=Unavailable"
                );
                LogCameraRaycasts(__instance, null);
                return;
            }

            Vector3 localPosition = shipTransform.InverseTransformPoint(playerTransform.position);
            Vector3 localForward = shipTransform.InverseTransformDirection(playerTransform.forward);
            Vector3 localEuler =
                (Quaternion.Inverse(shipTransform.rotation) * playerTransform.rotation).eulerAngles;

            Plugin.Log.LogInfo(
                $"PLAYER POSITION MARKER | " +
                $"WorldPosition={Format(playerTransform.position)} | " +
                $"ShipLocalPosition={Format(localPosition)} | " +
                $"WorldEuler={Format(playerTransform.eulerAngles)} | " +
                $"ShipLocalEuler={Format(localEuler)} | " +
                $"WorldForward={Format(playerTransform.forward)} | " +
                $"ShipLocalForward={Format(localForward)}"
            );

            LogCameraRaycasts(__instance, shipTransform);
            LogFurniturePlacementState(shipTransform);
        }
    }

    private static void LogCameraRaycasts(
        PlayerControllerB player,
        Transform? shipTransform)
    {
        Camera? camera = player.gameplayCamera;
        if (camera == null)
        {
            Plugin.Log.LogInfo("PLAYER CAMERA RAY | Camera=Unavailable");
            return;
        }

        Ray ray = new(camera.transform.position, camera.transform.forward);
        LogRaycast("AllLayers", ray, DebugRayDistance, Physics.DefaultRaycastLayers, shipTransform);

        ShipBuildModeManager? buildManager = ShipBuildModeManager.Instance;
        if (buildManager == null || PlacementMaskField?.GetValue(buildManager) is not int placementMask)
        {
            Plugin.Log.LogInfo("PLAYER CAMERA RAY | VanillaPlacementMask=Unavailable");
            return;
        }

        LogRaycast(
            "VanillaPlacementMask",
            ray,
            VanillaPlacementRayDistance,
            placementMask,
            shipTransform);
    }

    private static void LogRaycast(
        string label,
        Ray ray,
        float distance,
        int layerMask,
        Transform? shipTransform)
    {
        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                distance,
                layerMask,
                QueryTriggerInteraction.Ignore))
        {
            Plugin.Log.LogInfo(
                $"PLAYER CAMERA RAY | Mode={label} | Hit=False | " +
                $"Origin={Format(ray.origin)} | Direction={Format(ray.direction)} | " +
                $"Distance={distance:F1} | Mask={layerMask}");
            return;
        }

        Vector3? shipLocalPoint =
            shipTransform == null ? null : shipTransform.InverseTransformPoint(hit.point);
        Vector3? shipLocalNormal =
            shipTransform == null ? null : shipTransform.InverseTransformDirection(hit.normal);
        GameObject hitObject = hit.collider.gameObject;

        Plugin.Log.LogInfo(
            $"PLAYER CAMERA RAY | Mode={label} | Hit=True | " +
            $"Collider={hit.collider.name} | Object={hitObject.name} | " +
            $"Layer={hitObject.layer}:{LayerMask.LayerToName(hitObject.layer)} | Tag={hitObject.tag} | " +
            $"WorldPoint={Format(hit.point)} | " +
            $"ShipLocalPoint={(shipLocalPoint.HasValue ? Format(shipLocalPoint.Value) : "Unavailable")} | " +
            $"WorldNormal={Format(hit.normal)} | " +
            $"ShipLocalNormal={(shipLocalNormal.HasValue ? Format(shipLocalNormal.Value) : "Unavailable")} | " +
            $"UpAngle={Vector3.Angle(hit.normal, Vector3.up):F1} | " +
            $"HitDistance={hit.distance:F2} | Mask={layerMask}");
    }

    private static void LogFurniturePlacementState(Transform shipTransform)
    {
        ShipBuildModeManager? manager = ShipBuildModeManager.Instance;
        if (manager == null || !manager.InBuildMode)
        {
            Plugin.Log.LogInfo("FURNITURE PLACEMENT STATE | InBuildMode=False");
            return;
        }

        bool canConfirm = CanConfirmPositionField?.GetValue(manager) is bool value && value;
        PlaceableShipObject? placingObject =
            PlacingObjectField?.GetValue(manager) as PlaceableShipObject;
        BoxCollider? currentCollider = CurrentColliderField?.GetValue(manager) as BoxCollider;
        Transform? ghost = manager.ghostObject;

        if (placingObject == null || currentCollider == null || ghost == null)
        {
            Plugin.Log.LogInfo(
                $"FURNITURE PLACEMENT STATE | InBuildMode=True | CanConfirm={canConfirm} | " +
                "PlacementData=Incomplete");
            return;
        }

        bool insideShipBounds =
            StartOfRound.Instance?.shipInnerRoomBounds?.bounds.Contains(ghost.position) == true;
        bool blocked = false;
        if (PlacementMaskAndBlockersField?.GetValue(manager) is int blockersMask)
        {
            blocked = Physics.CheckBox(
                ghost.position,
                currentCollider.size * 0.5f * 0.57f,
                Quaternion.Euler(ghost.eulerAngles),
                blockersMask,
                QueryTriggerInteraction.Ignore);
        }

        Plugin.Log.LogInfo(
            $"FURNITURE PLACEMENT STATE | InBuildMode=True | CanConfirm={canConfirm} | " +
            $"UnlockableId={placingObject.unlockableID} | Object={placingObject.name} | " +
            $"GhostWorldPosition={Format(ghost.position)} | " +
            $"GhostShipLocalPosition={Format(shipTransform.InverseTransformPoint(ghost.position))} | " +
            $"GhostWorldEuler={Format(ghost.eulerAngles)} | " +
            $"ColliderSize={Format(currentCollider.size)} | " +
            $"YOffset={placingObject.yOffset:F3} | WallOffset={placingObject.wallOffset:F3} | " +
            $"OverrideWallOffset={placingObject.overrideWallOffset} | " +
            $"InsideShipBounds={insideShipBounds} | Blocked={blocked}");
    }

    private static string Format(Vector3 value) =>
        $"({value.x:F2}, {value.y:F2}, {value.z:F2})";
}
