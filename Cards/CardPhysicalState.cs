using System;
using System.Collections.Generic;
using System.Text;
using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Cards;

/// <summary>
/// Establishes the authored card scale and collider set before vanilla item
/// lifecycle code can use them. This is deliberately limited to physical cards.
/// </summary>
internal static class CardPhysicalState
{
    // The authored card Z scale is approximately 0.004, so this threshold only
    // treats an effectively zero axis as invalid.
    private const float ZeroAxisEpsilon = 0.0000001f;
    private static readonly Dictionary<Item, Vector3> ExpectedScales = new();

    internal static bool PreparePrefab(Item item, GameObject prefab, string cardId)
    {
        if (item == null || prefab == null)
            return false;

        GrabbableObject? grabbable = prefab.GetComponent<GrabbableObject>();
        Vector3 expectedScale = prefab.transform.localScale;
        if (grabbable == null || !IsUsableScale(expectedScale))
        {
            Plugin.Log.LogError(
                $"CARD PHYSICS PREFAB INVALID | CardId={cardId} | " +
                $"Prefab={prefab.name} | Scale={Format(expectedScale)} | " +
                $"GrabbableObjectPresent={grabbable != null}");
            return false;
        }

        ExpectedScales[item] = expectedScale;
        grabbable.originalScale = expectedScale;
        RefreshColliderReferences(grabbable, true);

        Plugin.Log.LogInfo(
            $"CARD PHYSICS PREFAB READY | CardId={cardId} | Prefab={prefab.name} | " +
            $"ExpectedScale={Format(expectedScale)} | Colliders={grabbable.propColliders.Length}");
        return true;
    }

    internal static bool EnsureInstance(
        GrabbableObject? grabbable,
        string context,
        bool logState = false)
    {
        if (grabbable == null || grabbable.GetComponent<CardInstanceData>() == null)
            return false;

        if (!TryGetExpectedScale(grabbable, out Vector3 expectedScale))
        {
            Plugin.Log.LogError(
                $"CARD PHYSICS STATE INVALID | Context={context} | " +
                $"Object={grabbable.name} | Reason=NoValidExpectedScale | " +
                $"LocalScale={Format(grabbable.transform.localScale)} | " +
                $"OriginalScale={Format(grabbable.originalScale)}");
            return false;
        }

        bool repairedLocalScale = false;
        bool repairedOriginalScale = false;
        if (!IsUsableScale(grabbable.transform.localScale))
        {
            grabbable.transform.localScale = expectedScale;
            repairedLocalScale = true;
        }

        if (!IsUsableScale(grabbable.originalScale))
        {
            grabbable.originalScale = expectedScale;
            repairedOriginalScale = true;
        }

        bool refreshedColliders = RefreshColliderReferences(grabbable, false);
        bool validLocalScale = IsUsableScale(grabbable.transform.localScale);
        bool validLossyScale = IsUsableScale(grabbable.transform.lossyScale);
        bool validOriginalScale = IsUsableScale(grabbable.originalScale);

        if (repairedLocalScale || repairedOriginalScale)
        {
            Plugin.Log.LogWarning(
                $"CARD PHYSICS STATE REPAIRED | Context={context} | {Describe(grabbable)} | " +
                $"ExpectedScale={Format(expectedScale)} | " +
                $"RepairedLocalScale={repairedLocalScale} | " +
                $"RepairedOriginalScale={repairedOriginalScale}");
        }

        if (!validLocalScale || !validLossyScale || !validOriginalScale)
        {
            Plugin.Log.LogError(
                $"CARD PHYSICS STATE INVALID | Context={context} | {Describe(grabbable)} | " +
                $"ExpectedScale={Format(expectedScale)} | " +
                $"ParentHierarchy={DescribeHierarchy(grabbable.transform)}");
            return false;
        }

        if (logState || refreshedColliders)
        {
            Plugin.Log.LogInfo(
                $"CARD PHYSICS STATE | Context={context} | {Describe(grabbable)} | " +
                $"ExpectedScale={Format(expectedScale)} | Colliders={grabbable.propColliders.Length} | " +
                $"ColliderRefsRefreshed={refreshedColliders} | " +
                $"ParentHierarchy={DescribeHierarchy(grabbable.transform)}");
        }

        return true;
    }

    internal static bool IsUsableScale(Vector3 scale) =>
        IsUsableAxis(scale.x) &&
        IsUsableAxis(scale.y) &&
        IsUsableAxis(scale.z);

    private static bool TryGetExpectedScale(
        GrabbableObject grabbable,
        out Vector3 expectedScale)
    {
        Item? item = grabbable.itemProperties;
        if (item != null && ExpectedScales.TryGetValue(item, out expectedScale) &&
            IsUsableScale(expectedScale))
        {
            return true;
        }

        if (item?.spawnPrefab != null)
        {
            expectedScale = item.spawnPrefab.transform.localScale;
            if (IsUsableScale(expectedScale))
            {
                ExpectedScales[item] = expectedScale;
                return true;
            }
        }

        if (IsUsableScale(grabbable.originalScale))
        {
            expectedScale = grabbable.originalScale;
            return true;
        }

        expectedScale = grabbable.transform.localScale;
        return IsUsableScale(expectedScale);
    }

    private static bool RefreshColliderReferences(
        GrabbableObject grabbable,
        bool force)
    {
        Collider[] discovered =
            grabbable.gameObject.GetComponentsInChildren<Collider>(true);
        Collider[]? existing = grabbable.propColliders;
        bool needsRefresh = force || existing == null ||
                            existing.Length != discovered.Length;
        if (!needsRefresh)
        {
            HashSet<Collider> intended = new(discovered);
            foreach (Collider? collider in existing!)
            {
                if (collider == null || !intended.Remove(collider))
                {
                    needsRefresh = true;
                    break;
                }
            }

            needsRefresh |= intended.Count != 0;
        }

        if (needsRefresh)
            grabbable.propColliders = discovered;
        return needsRefresh;
    }

    private static bool IsUsableAxis(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) &&
        Math.Abs(value) > ZeroAxisEpsilon;

    private static string Describe(GrabbableObject grabbable)
    {
        CardInstanceData? data = grabbable.GetComponent<CardInstanceData>();
        NetworkObject? networkObject = grabbable.GetComponent<NetworkObject>();
        string networkId = networkObject != null && networkObject.IsSpawned
            ? networkObject.NetworkObjectId.ToString()
            : "Unspawned";
        return
            $"CardId={(string.IsNullOrEmpty(data?.CardId) ? "<pending>" : data.CardId)} | " +
            $"InstanceId={grabbable.GetInstanceID()} | NetworkObjectId={networkId} | " +
            $"LocalScale={Format(grabbable.transform.localScale)} | " +
            $"LossyScale={Format(grabbable.transform.lossyScale)} | " +
            $"OriginalScale={Format(grabbable.originalScale)} | " +
            $"Held={grabbable.isHeld} | Pocketed={grabbable.isPocketed}";
    }

    internal static string DescribeHierarchy(Transform? transform)
    {
        StringBuilder result = new();
        Transform? current = transform;
        int depth = 0;
        while (current != null && depth < 12)
        {
            if (result.Length > 0)
                result.Append(" <- ");
            result.Append(current.name)
                .Append("[local=").Append(Format(current.localScale))
                .Append(",lossy=").Append(Format(current.lossyScale)).Append(']');
            current = current.parent;
            depth++;
        }

        return result.ToString();
    }

    private static string Format(Vector3 value) =>
        $"({value.x:F6},{value.y:F6},{value.z:F6})";
}

[HarmonyPatch(typeof(GrabbableObject), "Start")]
internal static class CardPhysicalStateStartPatch
{
    [HarmonyPrefix]
    private static void Prefix(GrabbableObject __instance) =>
        CardPhysicalState.EnsureInstance(__instance, "GrabbableObject.Start.Before");

    [HarmonyPostfix]
    private static void Postfix(GrabbableObject __instance) =>
        // Vanilla discovers only active child colliders. Rebuild with inactive
        // children included so a carried ScanNode remains known after Start.
        CardPhysicalState.EnsureInstance(__instance, "GrabbableObject.Start.After");
}

[HarmonyPatch(
    typeof(PlayerControllerB),
    "PlaceGrabbableObject",
    new[] { typeof(Transform), typeof(Vector3), typeof(bool), typeof(GrabbableObject) })]
internal static class CardPhysicalStatePlacePatch
{
    [HarmonyPrefix]
    private static void Prefix(Transform parentObject, GrabbableObject placeObject) =>
        CardPhysicalState.EnsureInstance(
            placeObject,
            $"PlaceGrabbableObject.Before:Target={CardPhysicalState.DescribeHierarchy(parentObject)}",
            Plugin.EnableDebugLogging);

    [HarmonyPostfix]
    private static void Postfix(Transform parentObject, GrabbableObject placeObject) =>
        CardPhysicalState.EnsureInstance(
            placeObject,
            $"PlaceGrabbableObject.After:Target={CardPhysicalState.DescribeHierarchy(parentObject)}",
            Plugin.EnableDebugLogging);
}

[HarmonyPatch(
    typeof(PlayerControllerB),
    "SetObjectAsNoLongerHeld",
    new[] { typeof(bool), typeof(bool), typeof(Vector3), typeof(GrabbableObject), typeof(int) })]
internal static class CardPhysicalStateDropPatch
{
    [HarmonyPrefix]
    private static void Prefix(GrabbableObject dropObject) =>
        CardPhysicalState.EnsureInstance(
            dropObject,
            "SetObjectAsNoLongerHeld.Before",
            Plugin.EnableDebugLogging);

    [HarmonyPostfix]
    private static void Postfix(GrabbableObject dropObject) =>
        CardPhysicalState.EnsureInstance(
            dropObject,
            "SetObjectAsNoLongerHeld.After",
            Plugin.EnableDebugLogging);
}
