using UnityEngine;

namespace LethalCards.Boosters;

internal static class CustomSpawnPlacement
{
    internal const float CardVerticalClearance = 0.10f;
    internal const float BoosterPackVerticalClearance = 0.12f;
    private const float CustomFloorTargetLift = 0.04f;

    internal static float PrepareSpawnHeight(
        GameObject spawnedObject,
        Vector3 originalPosition,
        float clearance,
        out float boundsMinimumY)
    {
        bool hasBounds = false;
        Bounds combinedBounds = default;

        foreach (Collider collider in spawnedObject.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger)
                continue;

            if (!hasBounds)
            {
                combinedBounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(collider.bounds);
            }
        }

        if (!hasBounds)
        {
            foreach (Renderer renderer in spawnedObject.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    combinedBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }
        }

        boundsMinimumY = hasBounds
            ? combinedBounds.min.y
            : spawnedObject.transform.position.y;

        float requiredMinimumY = originalPosition.y + clearance;
        float verticalAdjustment = Mathf.Max(0f, requiredMinimumY - boundsMinimumY);
        spawnedObject.transform.position += Vector3.up * verticalAdjustment;
        return verticalAdjustment;
    }

    internal static void MarkForFloorTargetCorrection(GrabbableObject grabbable)
    {
        if (grabbable.GetComponent<CustomSpawnFloorMarker>() == null)
            grabbable.gameObject.AddComponent<CustomSpawnFloorMarker>();
    }

    internal static void TryApplyFloorTargetCorrection(GrabbableObject grabbable)
    {
        CustomSpawnFloorMarker? marker = grabbable.GetComponent<CustomSpawnFloorMarker>();
        if (marker == null || marker.CorrectionApplied || grabbable.targetFloorPosition == Vector3.zero)
            return;

        Transform? parent = grabbable.transform.parent;
        Vector3 targetBefore = grabbable.targetFloorPosition;
        Vector3 worldTargetBefore = parent != null
            ? parent.TransformPoint(targetBefore)
            : targetBefore;
        Vector3 worldTargetAfter = worldTargetBefore + Vector3.up * CustomFloorTargetLift;
        Vector3 targetAfter = parent != null
            ? parent.InverseTransformPoint(worldTargetAfter)
            : worldTargetAfter;

        grabbable.targetFloorPosition = targetAfter;
        marker.CorrectionApplied = true;

        // Plugin.Log.LogInfo(
        //     $"CUSTOM FLOOR TARGET CORRECTION | InstanceId={grabbable.GetInstanceID()} | " +
        //     $"Object={grabbable.gameObject.name} | WorldPosition={grabbable.transform.position} | " +
        //     $"TargetBefore={targetBefore} | WorldTargetBefore={worldTargetBefore} | " +
        //     $"WorldTargetAfter={worldTargetAfter} | TargetAfter={targetAfter} | " +
        //     $"Lift={CustomFloorTargetLift:F2}");
    }
}

internal sealed class CustomSpawnFloorMarker : MonoBehaviour
{
    internal bool CorrectionApplied { get; set; }
}
