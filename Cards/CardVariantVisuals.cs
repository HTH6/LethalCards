using UnityEngine;

namespace LethalCards.Cards;

/// <summary>Applies authoritative variant metadata to physical card artwork.</summary>
public sealed class CardVariantVisuals : MonoBehaviour
{
    private const string NormalTextureProperty = "_BaseColorMap";
    private const string AlternateTextureProperty = "_BaseMap";
    private static GameObject? foilOverlayPrefab;
    private static Material? alternateArtMaterialTemplate;

    private Transform? cardFront;
    private MeshRenderer? cardFrontRenderer;
    private GameObject? foilOverlay;
    private Quaternion normalFrontRotation;
    private Material? normalFrontMaterial;
    private Material? alternateArtRuntimeMaterial;
    private bool cached;

    internal static void ConfigureAssets(GameObject? overlayPrefab, Material? alternateMaterial)
    {
        foilOverlayPrefab = overlayPrefab;
        alternateArtMaterialTemplate = alternateMaterial;
    }

    private void Awake() => CacheOriginalState();

    public void ApplyVariant(CardVariant variant)
    {
        CacheOriginalState();
        ResetVariantVisuals();
        switch (variant)
        {
            case CardVariant.Foil:
                EnsureFoilOverlay();
                if (foilOverlay != null) foilOverlay.SetActive(true);
                break;
            case CardVariant.AlternateArt:
                ApplyAlternateArt();
                break;
            case CardVariant.Misprint:
                if (cardFront != null)
                    cardFront.localRotation = normalFrontRotation * Quaternion.Euler(0f, 0f, 180f);
                break;
        }
    }

    private void CacheOriginalState()
    {
        if (cached) return;
        foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
            if (child.name.Equals("CardFront", System.StringComparison.OrdinalIgnoreCase))
            {
                cardFront = child;
                break;
            }
        if (cardFront != null)
        {
            normalFrontRotation = cardFront.localRotation;
            cardFrontRenderer = cardFront.GetComponent<MeshRenderer>() ?? cardFront.GetComponentInChildren<MeshRenderer>(true);
        }
        if (cardFrontRenderer != null) normalFrontMaterial = cardFrontRenderer.sharedMaterial;
        cached = true;
        if (cardFront == null || cardFrontRenderer == null)
            Plugin.Log.LogWarning($"CARD VARIANT VISUALS | Card={name} | CardFront or MeshRenderer not found");
    }

    private void ResetVariantVisuals()
    {
        if (foilOverlay != null) foilOverlay.SetActive(false);
        if (cardFront != null) cardFront.localRotation = normalFrontRotation;
        if (cardFrontRenderer != null && normalFrontMaterial != null) cardFrontRenderer.sharedMaterial = normalFrontMaterial;
    }

    private void EnsureFoilOverlay()
    {
        if (foilOverlay != null || cardFront == null || foilOverlayPrefab == null) return;
        foilOverlay = Instantiate(foilOverlayPrefab, cardFront, false);
        foilOverlay.name = "LethalCardsFoilOverlay";
    }

    private void ApplyAlternateArt()
    {
        if (cardFrontRenderer == null || normalFrontMaterial == null || alternateArtMaterialTemplate == null) return;
        if (alternateArtRuntimeMaterial == null)
        {
            alternateArtRuntimeMaterial = new Material(alternateArtMaterialTemplate) { name = $"{alternateArtMaterialTemplate.name} ({name})" };
            if (normalFrontMaterial.HasProperty(NormalTextureProperty) && alternateArtRuntimeMaterial.HasProperty(AlternateTextureProperty))
            {
                alternateArtRuntimeMaterial.SetTexture(AlternateTextureProperty, normalFrontMaterial.GetTexture(NormalTextureProperty));
                alternateArtRuntimeMaterial.SetTextureScale(AlternateTextureProperty, normalFrontMaterial.GetTextureScale(NormalTextureProperty));
                alternateArtRuntimeMaterial.SetTextureOffset(AlternateTextureProperty, normalFrontMaterial.GetTextureOffset(NormalTextureProperty));
            }
            else Plugin.Log.LogWarning($"CARD ALT ART | Card={name} | Required texture properties unavailable");
        }
        cardFrontRenderer.sharedMaterial = alternateArtRuntimeMaterial;
    }

    private void OnDestroy()
    {
        if (alternateArtRuntimeMaterial != null) Destroy(alternateArtRuntimeMaterial);
    }
}
