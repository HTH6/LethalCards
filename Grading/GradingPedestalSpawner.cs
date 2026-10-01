using GameNetcodeStuff;
using UnityEngine;

namespace LethalCards.Grading;

public static class GradingPedestalSpawner
{
    public static readonly Vector3 GradingPedestalPosition =
        new Vector3(
            -27.91f,
            -2.63f,
            -22.19f
        );

    public static readonly Vector3 GradedCardPickupPosition =
        new Vector3(
            -27.85f,
            -2.63f,
            -14.16f
        );

    public static readonly Vector3 GradedCardRestPosition =
        new Vector3(
            -27.85f,
            0.0f,
            -14.16f
        );

    private static GameObject? pedestalRoot;
    private static GameObject? pickupPedestalRoot;

    public static void TrySpawn()
    {
        // If our pedestal still exists, don't create another.
        if (pedestalRoot != null)
            return;

        if (StartOfRound.Instance == null)
            return;

        SelectableLevel level =
            StartOfRound.Instance.currentLevel;

        if (level == null)
            return;

        // Only operate on the Company planet.
        if (
            !level.PlanetName.Contains(
                "Gordion",
                System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Routing to Gordion changes currentLevel before
        // CompanyBuilding is actually loaded.
        //
        // The deposit desk existing tells us the real
        // Company scene has finished loading.
        DepositItemsDesk depositDesk =
            Object.FindObjectOfType<DepositItemsDesk>();

        if (depositDesk == null)
            return;

        CreatePedestal();
        CreatePickupPedestal();
    }

    // ============================================================
    // SUBMISSION PEDESTAL
    // ============================================================

    private static void CreatePedestal()
    {
        pedestalRoot =
            new GameObject(
                "LethalCardsGradingPedestal"
            );

        pedestalRoot.transform.position =
            GradingPedestalPosition;

        pedestalRoot.transform.rotation =
            Quaternion.Euler(
                0f,
                90f,
                0f
            );

        CreatePedestalMesh();
        CreateInteraction();

        Plugin.Log.LogInfo(
            $"GRADING PEDESTAL SPAWNED | " +
            $"Position={pedestalRoot.transform.position} | " +
            $"PickupPosition={GradedCardPickupPosition}"
        );
    }

    private static void CreatePedestalMesh()
    {
        if (pedestalRoot == null)
            return;

        Shader shader =
            Shader.Find(
                "HDRP/Lit"
            );

        if (shader == null)
        {
            Plugin.Log.LogError(
                "GRADING PEDESTAL ERROR | " +
                "HDRP/Lit shader not found."
            );

            return;
        }

        Material pedestalMaterial =
            new Material(shader);

        pedestalMaterial.name =
            "LethalCardsGradingPedestalMaterial";

        pedestalMaterial.color =
            new Color(
                0.15f,
                0.65f,
                1.0f,
                1.0f
            );

        // =========================
        // BASE
        // =========================

        GameObject baseObject =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        baseObject.name =
            "GradingPedestalBase";

        baseObject.transform.SetParent(
            pedestalRoot.transform
        );

        baseObject.transform.localPosition =
            new Vector3(
                0f,
                0.5f,
                0f
            );

        baseObject.transform.localScale =
            new Vector3(
                1.2f,
                1.0f,
                1.2f
            );

        MeshRenderer baseRenderer =
            baseObject.GetComponent<MeshRenderer>();

        if (baseRenderer != null)
        {
            baseRenderer.material =
                pedestalMaterial;

            Plugin.Log.LogInfo(
                $"GRADING BASE RENDERER | " +
                $"Enabled={baseRenderer.enabled} | " +
                $"Material={baseRenderer.material.name}"
            );
        }

        // =========================
        // TOP
        // =========================

        GameObject topObject =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        topObject.name =
            "GradingPedestalTop";

        topObject.transform.SetParent(
            pedestalRoot.transform
        );

        topObject.transform.localPosition =
            new Vector3(
                0f,
                1.1f,
                0f
            );

        topObject.transform.localScale =
            new Vector3(
                1.6f,
                0.2f,
                1.6f
            );

        MeshRenderer topRenderer =
            topObject.GetComponent<MeshRenderer>();

        if (topRenderer != null)
        {
            topRenderer.material =
                pedestalMaterial;
        }

        Plugin.Log.LogInfo(
            $"GRADING PEDESTAL MESH CREATED | " +
            $"BaseActive={baseObject.activeInHierarchy} | " +
            $"BasePosition={baseObject.transform.position} | " +
            $"TopPosition={topObject.transform.position}"
        );
    }

    private static void CreateInteraction()
    {
        if (pedestalRoot == null)
            return;

        GameObject interactionObject =
            new GameObject(
                "GradingPedestalInteract"
            );

        interactionObject.transform.SetParent(
            pedestalRoot.transform
        );

        interactionObject.transform.localPosition =
            new Vector3(
                0f,
                1.3f,
                0f
            );

        interactionObject.layer =
            LayerMask.NameToLayer(
                "InteractableObject"
            );

        interactionObject.tag =
            "InteractTrigger";

        SphereCollider collider =
            interactionObject.AddComponent<
                SphereCollider>();

        collider.radius =
            1.5f;

        collider.isTrigger =
            true;

        InteractTrigger trigger =
            interactionObject.AddComponent<
                InteractTrigger>();

        trigger.interactable =
            true;

        trigger.holdInteraction =
            false;

        trigger.cooldownTime =
            0.5f;

        trigger.hoverTip =
            $"Submit card for grading (${GradingManager.GradingCostPerCard}) : [E]";

        trigger.onInteract =
            new InteractEvent();

        trigger.onInteractEarly =
            new InteractEvent();

        trigger.onCancelAnimation =
            new InteractEvent();

        trigger.onStopInteract =
            new InteractEvent();

        trigger.holdingInteractEvent =
            new InteractEventFloat();

        GradingPedestalBehaviour behaviour =
            pedestalRoot.AddComponent<
                GradingPedestalBehaviour>();

        trigger.onInteract.AddListener(
            (PlayerControllerB player) =>
            {
                behaviour.TrySubmitHeldCard(
                    player
                );
            }
        );
    }

    // ============================================================
    // GRADED CARD PICKUP PEDESTAL
    // ============================================================

    private static void CreatePickupPedestal()
    {
        if (pickupPedestalRoot != null)
            return;

        pickupPedestalRoot =
            new GameObject(
                "LethalCardsGradedPickupPedestal"
            );

        pickupPedestalRoot.transform.position =
            GradedCardPickupPosition;

        pickupPedestalRoot.transform.rotation =
            Quaternion.Euler(
                0f,
                90f,
                0f
            );

        Shader shader =
            Shader.Find(
                "HDRP/Lit"
            );

        if (shader == null)
        {
            Plugin.Log.LogError(
                "GRADED PICKUP PEDESTAL ERROR | " +
                "HDRP/Lit shader not found."
            );

            return;
        }

        Material material =
            new Material(shader);

        material.name =
            "LethalCardsGradedPickupMaterial";

        // Temporary prototype color.
        // Slightly green so it's visually distinct
        // from the blue submission pedestal.
        material.color =
            new Color(
                0.25f,
                0.85f,
                0.35f,
                1f
            );

        // =========================
        // BASE
        // =========================

        GameObject baseObject =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        baseObject.name =
            "GradedPickupPedestalBase";

        baseObject.transform.SetParent(
            pickupPedestalRoot.transform
        );

        baseObject.transform.localPosition =
            new Vector3(
                0f,
                0.5f,
                0f
            );

        baseObject.transform.localScale =
            new Vector3(
                1.6f,
                1.0f,
                1.6f
            );

        MeshRenderer baseRenderer =
            baseObject.GetComponent<MeshRenderer>();

        if (baseRenderer != null)
        {
            baseRenderer.material =
                material;
        }

        // =========================
        // VISIBLE TOP
        // =========================

        GameObject topObject =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        topObject.name =
            "GradedPickupPedestalTop";

        topObject.transform.SetParent(
            pickupPedestalRoot.transform
        );

        topObject.transform.localPosition =
            new Vector3(
                0f,
                1.1f,
                0f
            );

        topObject.transform.localScale =
            new Vector3(
                2.0f,
                0.2f,
                2.0f
            );

        MeshRenderer topRenderer =
            topObject.GetComponent<MeshRenderer>();

        if (topRenderer != null)
        {
            topRenderer.material =
                material;
        }

        // ========================================================
        // INVISIBLE CARD CATCH SURFACE
        //
        // The cards are extremely thin and were falling/clipping
        // into the decorative pedestal top.
        //
        // This creates a much thicker invisible collider slightly
        // above the visible top. The MeshRenderer is disabled, but
        // the BoxCollider created by CreatePrimitive remains active.
        // ========================================================

        GameObject catchSurface =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        catchSurface.name =
            "GradedCardCatchSurface";

        catchSurface.transform.SetParent(
            pickupPedestalRoot.transform
        );

        catchSurface.transform.localPosition =
            new Vector3(
                0f,
                1.25f,
                0f
            );

        catchSurface.transform.localScale =
            new Vector3(
                2.2f,
                0.10f,
                2.2f
            );

        MeshRenderer catchRenderer =
            catchSurface.GetComponent<MeshRenderer>();

        if (catchRenderer != null)
        {
            catchRenderer.enabled =
                false;
        }

        BoxCollider catchCollider =
            catchSurface.GetComponent<BoxCollider>();

        if (catchCollider != null)
        {
            catchCollider.enabled =
                true;

            catchCollider.isTrigger =
                false;
        }

        Plugin.Log.LogInfo(
            $"GRADED PICKUP CATCH SURFACE CREATED | " +
            $"Position={catchSurface.transform.position} | " +
            $"Scale={catchSurface.transform.lossyScale} | " +
            $"ColliderEnabled=" +
            $"{(catchCollider != null && catchCollider.enabled)}"
        );

        Plugin.Log.LogInfo(
            $"GRADED PICKUP PEDESTAL SPAWNED | " +
            $"Position={pickupPedestalRoot.transform.position} | " +
            $"TopPosition={topObject.transform.position} | " +
            $"CatchSurfacePosition={catchSurface.transform.position}"
        );
    }

    // ============================================================
    // RESET
    // ============================================================

    public static void Reset()
    {
        if (pedestalRoot != null)
        {
            Object.Destroy(
                pedestalRoot
            );

            pedestalRoot =
                null;
        }

        if (pickupPedestalRoot != null)
        {
            Object.Destroy(
                pickupPedestalRoot
            );

            pickupPedestalRoot =
                null;
        }
    }
}
