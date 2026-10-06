using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Cards;

internal static class GradedCardSlabPresentation
{
    private const string PresentationName = "LethalCardsGradedCardSlab";

    private static GameObject? slabPrefab;
    private static Material? normalLabelMaterial;
    private static Material? grade1LabelMaterial;
    private static Material? grade9LabelMaterial;
    private static Material? grade10LabelMaterial;

    internal static void LoadAssets(AssetBundle bundle)
    {
        string[] assetNames = bundle.GetAllAssetNames();

        slabPrefab = LoadNamed<GameObject>(bundle, assetNames, "GradedCardSlab", false);
        normalLabelMaterial = LoadNamed<Material>(bundle, assetNames, "MAT_GradeLabel_Normal", true);
        grade1LabelMaterial = LoadNamed<Material>(bundle, assetNames, "MAT_GradeLabel_1", true);
        grade9LabelMaterial = LoadNamed<Material>(bundle, assetNames, "MAT_GradeLabel_9", true);
        grade10LabelMaterial = LoadNamed<Material>(bundle, assetNames, "MAT_GradeLabel_10", true);

        if (slabPrefab != null && HasForbiddenGameplayComponents(slabPrefab))
        {
            Plugin.Log.LogError("GRADED SLAB ASSET INVALID | Name=GradedCardSlab | Reason=Prefab contains gameplay, physics, or networking components");
            slabPrefab = null;
        }
    }

    internal static bool Ensure(CardInstanceData cardData)
    {
        if (cardData == null || cardData.Grade <= 0 || slabPrefab == null)
            return false;

        CardDefinition? card = CardDatabase.GetById(cardData.CardId);
        if (card == null)
        {
            Plugin.Log.LogWarning($"GRADED SLAB FAILED | CardId={cardData.CardId} | Reason=Card definition missing");
            return false;
        }

        Transform cardTransform = cardData.transform;
        Transform? slabTransform = FindDirectChild(cardTransform, PresentationName);
        bool created = false;

        if (slabTransform == null)
        {
            GameObject slab = UnityEngine.Object.Instantiate(slabPrefab);
            slab.name = PresentationName;
            slabTransform = slab.transform;
            slabTransform.position = cardTransform.position;
            slabTransform.rotation = cardTransform.rotation;
            Vector3 prefabScale = slabPrefab.transform.localScale;
            Vector3 intendedWorldScale = slabTransform.lossyScale;

            Transform? cardMount = FindChild(slabTransform, "CardMount");
            if (cardMount == null)
            {
                Plugin.Log.LogWarning($"GRADED SLAB FAILED | CardId={cardData.CardId} | Reason=CardMount missing");
                UnityEngine.Object.Destroy(slab);
                return false;
            }

            Vector3 cardPosition = cardTransform.position;
            slabTransform.position += cardPosition - cardMount.position;
            slabTransform.SetParent(cardTransform, true);
            CompensateWorldScale(slabTransform, intendedWorldScale);
            slabTransform.position += cardPosition - cardMount.position;
            created = true;

            /* Plugin.Log.LogInfo(
                $"GRADED SLAB ALIGNMENT | CardId={cardData.CardId} | " +
                $"CardPosition={cardPosition} | MountPosition={cardMount.position} | " +
                $"SlabLocalPosition={slabTransform.localPosition}"); */

            /* Plugin.Log.LogInfo(
                $"GRADED SLAB TRANSFORM | CardId={cardData.CardId} | " +
                $"CardLocalScale={cardTransform.localScale} | CardLossyScale={cardTransform.lossyScale} | " +
                $"SlabLocalScale={slabTransform.localScale} | SlabLossyScale={slabTransform.lossyScale} | " +
                $"PrefabScale={prefabScale}"); */
        }

        PopulateLabel(slabTransform, cardData, card);
        ExpandExistingCollider(cardData, slabTransform);

        if (created)
        {
            /* Plugin.Log.LogInfo(
                $"GRADED SLAB CREATED | CardId={cardData.CardId} | " +
                $"Grade={cardData.Grade} | Variant={cardData.Variant}"); */
        }

        return true;
    }

    private static void PopulateLabel(
        Transform slab,
        CardInstanceData cardData,
        CardDefinition card)
    {
        string variantText = cardData.Variant switch
        {
            CardVariant.Foil => "FOIL",
            CardVariant.AlternateArt => "ALTERNATE ART",
            CardVariant.Misprint => "MISPRINT",
            _ => "STANDARD"
        };
        string setNumber = $"{card.SetId}-{card.SetNumber:D3}";
        string descriptor = GetGradeDescriptor(cardData.Grade);

        SetText(slab, "CardNameText", card.DisplayName.ToUpperInvariant());
        SetText(slab, "SetNumberText", setNumber);
        SetText(slab, "VariantText", variantText);
        SetText(slab, "GradeText", $"GRADE {cardData.Grade}");
        SetText(slab, "GradeDescriptorText", descriptor);

        Transform? labelBackground = FindChild(slab, "LabelBackground");
        MeshRenderer? labelRenderer = labelBackground?.GetComponent<MeshRenderer>();
        Material? labelMaterial = GetGradeLabelMaterial(cardData.Grade);
        if (labelRenderer != null && labelMaterial != null)
            labelRenderer.sharedMaterial = labelMaterial;

        /* Plugin.Log.LogInfo(
            $"GRADED SLAB LABEL | Card={card.DisplayName} | SetNumber={setNumber} | " +
            $"Variant={variantText} | Grade={cardData.Grade} | Descriptor={descriptor}"); */
    }

    private static void SetText(Transform root, string childName, string value)
    {
        Transform? child = FindChild(root, childName);
        TMP_Text? text = child?.GetComponent<TMP_Text>();
        if (text == null)
        {
            Plugin.Log.LogWarning($"GRADED SLAB LABEL FIELD MISSING | Name={childName}");
            return;
        }

        text.text = value;
    }

    private static Material? GetGradeLabelMaterial(int grade) =>
        grade switch
        {
            1 => grade1LabelMaterial,
            9 => grade9LabelMaterial,
            10 => grade10LabelMaterial,
            _ => normalLabelMaterial
        };

    internal static string GetGradeDescriptor(int grade) =>
        grade switch
        {
            10 => "GEM MINT",
            9 => "MINT",
            8 => "NM-MT",
            7 => "NM",
            6 => "EX-MT",
            5 => "EX",
            4 => "VG-EX",
            3 => "VG",
            2 => "GOOD",
            1 => "POOR",
            _ => string.Empty
        };

    private static void ExpandExistingCollider(CardInstanceData cardData, Transform slab)
    {
        Renderer[] renderers = slab.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Plugin.Log.LogWarning($"GRADED SLAB COLLIDER SKIPPED | CardId={cardData.CardId} | Reason=Slab renderers missing");
            return;
        }

        Bounds worldBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            worldBounds.Encapsulate(renderers[i].bounds);

        /* Plugin.Log.LogInfo(
            $"GRADED SLAB BOUNDS | CardId={cardData.CardId} | " +
            $"Center={worldBounds.center} | Size={worldBounds.size}"); */

        BoxCollider? collider = cardData.GetComponent<BoxCollider>() ??
                                cardData.GetComponentInChildren<BoxCollider>(true);
        if (collider == null)
        {
            Plugin.Log.LogWarning($"GRADED SLAB COLLIDER SKIPPED | CardId={cardData.CardId} | Reason=Existing BoxCollider missing");
            return;
        }

        Vector3 oldCenter = collider.center;
        Vector3 oldSize = collider.size;
        Vector3 oldExtents = oldSize * 0.5f;
        Vector3 minimum = oldCenter - oldExtents;
        Vector3 maximum = oldCenter + oldExtents;
        Vector3 worldMin = worldBounds.min;
        Vector3 worldMax = worldBounds.max;

        for (int x = 0; x < 2; x++)
        for (int y = 0; y < 2; y++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 corner = new(
                x == 0 ? worldMin.x : worldMax.x,
                y == 0 ? worldMin.y : worldMax.y,
                z == 0 ? worldMin.z : worldMax.z);
            Vector3 localCorner = collider.transform.InverseTransformPoint(corner);
            minimum = Vector3.Min(minimum, localCorner);
            maximum = Vector3.Max(maximum, localCorner);
        }

        Vector3 newCenter = (minimum + maximum) * 0.5f;
        Vector3 newSize = maximum - minimum;
        collider.center = newCenter;
        collider.size = newSize;

        /* Plugin.Log.LogInfo(
            $"GRADED SLAB COLLIDER | CardId={cardData.CardId} | " +
            $"OldCenter={oldCenter} | OldSize={oldSize} | " +
            $"NewCenter={newCenter} | NewSize={newSize}"); */
    }

    private static void CompensateWorldScale(Transform transform, Vector3 intendedWorldScale)
    {
        Vector3 actualWorldScale = transform.lossyScale;
        Vector3 localScale = transform.localScale;
        transform.localScale = new Vector3(
            ScaleAxis(localScale.x, intendedWorldScale.x, actualWorldScale.x),
            ScaleAxis(localScale.y, intendedWorldScale.y, actualWorldScale.y),
            ScaleAxis(localScale.z, intendedWorldScale.z, actualWorldScale.z));
    }

    private static float ScaleAxis(float local, float intendedWorld, float actualWorld) =>
        Mathf.Abs(actualWorld) > 0.000001f
            ? local * intendedWorld / actualWorld
            : local;

    private static bool HasForbiddenGameplayComponents(GameObject prefab) =>
        prefab.GetComponentInChildren<NetworkObject>(true) != null ||
        prefab.GetComponentInChildren<NetworkBehaviour>(true) != null ||
        prefab.GetComponentInChildren<GrabbableObject>(true) != null ||
        prefab.GetComponentInChildren<Rigidbody>(true) != null ||
        prefab.GetComponentInChildren<Collider>(true) != null ||
        prefab.GetComponentInChildren<CardInstanceData>(true) != null;

    private static Transform? FindDirectChild(Transform root, string childName)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    private static Transform? FindChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    private static T? LoadNamed<T>(
        AssetBundle bundle,
        string[] paths,
        string expectedName,
        bool optional)
        where T : UnityEngine.Object
    {
        string? path = paths.FirstOrDefault(candidate =>
            Path.GetFileNameWithoutExtension(candidate)
                .Equals(expectedName, StringComparison.OrdinalIgnoreCase));
        T? asset = path == null ? null : bundle.LoadAsset<T>(path);
        if (asset == null)
        {
            asset = bundle.LoadAllAssets<T>().FirstOrDefault(candidate =>
                candidate.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
        }

        if (asset == null)
        {
            string severity = optional ? "OPTIONAL ASSET MISSING" : "ASSET MISSING";
            Plugin.Log.LogWarning($"GRADED SLAB {severity} | Name={expectedName} | Type={typeof(T).Name}");
        }
        else
        {
            string label = typeof(T) == typeof(GameObject) ? "Prefab" : "Asset";
            Plugin.Log.LogInfo($"GRADED SLAB ASSET READY | Name={expectedName} | {label}={asset.name}");
        }

        return asset;
    }
}
