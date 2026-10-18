using LethalCards.Cards;
using UnityEngine;

namespace LethalCards.ShipDisplay;

internal static class ShipCardDisplayVisualFactory
{
    private const string DisplayVisualName = "LethalCardsStoredCardVisual";

    internal static GameObject? Create(
        Transform slotAnchor,
        ShipCardDisplayStoredCard storedCard)
    {
        CardDefinition? definition = CardDatabase.GetById(storedCard.CardId);
        GameObject? sourcePrefab = definition?.ItemAsset?.spawnPrefab;
        if (definition == null || sourcePrefab == null)
        {
            Plugin.Log.LogWarning(
                $"SHIP CARD DISPLAY VISUAL SKIPPED | CardId={storedCard.CardId} | " +
                "Reason=Implemented card prefab unavailable");
            return null;
        }

        GameObject displayRoot = new(DisplayVisualName);
        displayRoot.transform.SetParent(slotAnchor, false);
        displayRoot.transform.localPosition = Vector3.zero;
        displayRoot.transform.localRotation = Quaternion.identity;
        displayRoot.transform.localScale = Vector3.one;

        Transform cardParent = displayRoot.transform;
        if (storedCard.Grade > 0 &&
            GradedCardSlabPresentation.TryCreateDisplaySlab(
                displayRoot.transform,
                definition,
                storedCard.Variant,
                storedCard.Grade,
                out Transform slabCardMount))
        {
            cardParent = slabCardMount;
            // The slab has its own authored scale. The card copied into CardMount
            // uses the card prefab's transform scale.
        }

        GameObject cardVisual = new("CardVisual");
        cardVisual.transform.SetParent(cardParent, false);
        cardVisual.transform.localPosition = Vector3.zero;
        cardVisual.transform.localRotation = Quaternion.identity;
        cardVisual.transform.localScale = sourcePrefab.transform.localScale;
        CopyRenderableHierarchy(sourcePrefab.transform, cardVisual.transform, true);

        CardVariantVisuals.ApplyToDisplay(cardVisual, storedCard.Variant);
        return displayRoot;
    }

    private static void CopyRenderableHierarchy(
        Transform source,
        Transform destination,
        bool copySourceComponents)
    {
        if (copySourceComponents)
            CopyRenderComponents(source.gameObject, destination.gameObject);

        for (int childIndex = 0; childIndex < source.childCount; childIndex++)
        {
            Transform sourceChild = source.GetChild(childIndex);
            GameObject destinationChild = new(sourceChild.name);
            destinationChild.SetActive(sourceChild.gameObject.activeSelf);
            destinationChild.transform.SetParent(destination, false);
            destinationChild.transform.localPosition = sourceChild.localPosition;
            destinationChild.transform.localRotation = sourceChild.localRotation;
            destinationChild.transform.localScale = sourceChild.localScale;
            CopyRenderComponents(sourceChild.gameObject, destinationChild);
            CopyRenderableHierarchy(sourceChild, destinationChild.transform, false);
        }
    }

    private static void CopyRenderComponents(GameObject source, GameObject destination)
    {
        MeshFilter? sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter != null && sourceFilter.sharedMesh != null)
            destination.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;

        MeshRenderer? sourceRenderer = source.GetComponent<MeshRenderer>();
        if (sourceRenderer != null)
        {
            MeshRenderer renderer = destination.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.enabled = sourceRenderer.enabled;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            renderer.receiveShadows = sourceRenderer.receiveShadows;
            renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        }

        SkinnedMeshRenderer? sourceSkinned = source.GetComponent<SkinnedMeshRenderer>();
        if (sourceSkinned != null && sourceSkinned.sharedMesh != null)
        {
            SkinnedMeshRenderer renderer = destination.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = sourceSkinned.sharedMesh;
            renderer.sharedMaterials = sourceSkinned.sharedMaterials;
            renderer.enabled = sourceSkinned.enabled;
            renderer.shadowCastingMode = sourceSkinned.shadowCastingMode;
            renderer.receiveShadows = sourceSkinned.receiveShadows;
            renderer.updateWhenOffscreen = sourceSkinned.updateWhenOffscreen;
            renderer.localBounds = sourceSkinned.localBounds;
        }
    }
}
