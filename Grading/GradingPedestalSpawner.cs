using GameNetcodeStuff;
using LethalCards.Cards;
using UnityEngine;

namespace LethalCards.Grading;

public static class GradingPedestalSpawner
{
    public static readonly Vector3 GradingPedestalPosition =
        new(
            -27.91f,
            -2.63f,
            -22.19f
        );

    public static readonly Vector3 GradedCardPickupPosition =
        new(
            -27.85f,
            -2.63f,
            -14.16f
        );

    private const float ReturnColumnSpacing = 0.55f;
    private const float ReturnRowSpacing = 0.22f;

    private static GameObject? submissionPedestalPrefab;
    private static GameObject? returnPedestalPrefab;
    private static GameObject? pedestalRoot;
    private static GameObject? pickupPedestalRoot;
    private static Transform? returnRestAnchor;
    private static GradingPedestalAnimationController? animationController;

    public static void LoadAssets(AssetBundle bundle)
    {
        submissionPedestalPrefab = LoadPrefab(bundle, "GradingSubmissionPedestal_Animated", false);
        if (submissionPedestalPrefab != null)
        {
            Plugin.Log.LogInfo("ANIMATED GRADING PEDESTAL LOADED");
        }
        else
        {
            Plugin.Log.LogWarning("ANIMATED GRADING PEDESTAL MISSING | Falling back to GradingSubmissionPedestal");
            submissionPedestalPrefab = LoadPrefab(bundle, "GradingSubmissionPedestal");
        }
        returnPedestalPrefab = LoadPrefab(bundle, "GradingReturnPedestal");
    }

    internal static void PlaySubmissionAnimation(string cardId, CardVariant variant) =>
        animationController?.PlaySubmission(cardId, variant);

    internal static void PlayInsufficientCreditsAnimation() =>
        animationController?.PlayInsufficientCredits();

    internal static bool IsReturnPedestalReady =>
        pickupPedestalRoot != null &&
        pickupPedestalRoot.activeInHierarchy &&
        returnRestAnchor != null;

    public static void TrySpawn()
    {
        if (pedestalRoot != null)
            return;

        if (StartOfRound.Instance == null)
            return;

        SelectableLevel level =
            StartOfRound.Instance.currentLevel;

        if (level == null)
            return;

        if (
            !level.PlanetName.Contains(
                "Gordion",
                System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DepositItemsDesk depositDesk =
            Object.FindObjectOfType<DepositItemsDesk>();

        if (depositDesk == null)
            return;

        CreatePedestal();
        CreatePickupPedestal();
    }

    public static bool TryGetReturnPlacement(
        int spawnIndex,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;

        if (returnRestAnchor == null)
        {
            Plugin.Log.LogError("GRADING RETURN PLACEMENT FAILED | GradedCardRestAnchor is missing; refusing to spawn returned card at fallback position.");
            return false;
        }

        int column = spawnIndex % 3;
        int row = spawnIndex / 3;
        Vector3 localOffset = new(
            column * ReturnColumnSpacing,
            row * -ReturnRowSpacing,
            0f
        );

        position = returnRestAnchor.TransformPoint(localOffset);
        rotation = returnRestAnchor.rotation;

        /* Plugin.Log.LogInfo(
            $"GRADING RETURN PLACEMENT | " +
            $"SpawnIndex={spawnIndex} | " +
            $"Column={column} | " +
            $"Row={row} | " +
            $"LocalOffset={localOffset} | " +
            $"WorldPosition={position} | " +
            $"WorldRotation={rotation.eulerAngles} | " +
            $"AnchorRight={returnRestAnchor.right} | " +
            $"AnchorUp={returnRestAnchor.up} | " +
            $"AnchorForward={returnRestAnchor.forward}"
        ); */

        return true;
    }

    private static void CreatePedestal()
    {
        if (submissionPedestalPrefab == null)
        {
            Plugin.Log.LogError("GRADING PEDESTAL SPAWN FAILED | No submission pedestal prefab was loaded.");
            return;
        }

        pedestalRoot =
            Object.Instantiate(
                submissionPedestalPrefab,
                GradingPedestalPosition,
                Quaternion.Euler(0f, 90f, 0f)
            );

        pedestalRoot.name =
            "LethalCardsGradingPedestal";

        animationController = pedestalRoot.AddComponent<GradingPedestalAnimationController>();
        animationController.Initialize();

        Transform? interactionAnchor =
            FindChild(pedestalRoot.transform, "InteractionAnchor");

        if (interactionAnchor == null)
        {
            Plugin.Log.LogError("GRADING PEDESTAL ERROR | GradingSubmissionPedestal is missing required child InteractionAnchor.");
            return;
        }

        CreateInteraction(interactionAnchor);

        /* Plugin.Log.LogInfo(
            $"GRADING PEDESTAL PREFAB SPAWNED | " +
            $"Position={pedestalRoot.transform.position} | " +
            $"Rotation={pedestalRoot.transform.rotation.eulerAngles} | " +
            $"InteractionAnchor={interactionAnchor.position}"
        ); */
    }

    private static void CreateInteraction(
        Transform interactionAnchor)
    {
        if (pedestalRoot == null)
            return;

        GameObject interactionObject =
            new(
                "GradingPedestalInteract"
            );

        interactionObject.transform.SetParent(
            interactionAnchor,
            false
        );

        interactionObject.transform.localPosition =
            Vector3.zero;

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

        ConfigureInteractionIcons(trigger);

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

    private static void ConfigureInteractionIcons(InteractTrigger trigger)
    {
        InteractTrigger? iconSource = Object.FindObjectOfType<DepositItemsDesk>()?.triggerScript;
        if (iconSource == null || iconSource.hoverIcon == null)
        {
            foreach (InteractTrigger candidate in Object.FindObjectsOfType<InteractTrigger>())
            {
                if (candidate == null || candidate == trigger || candidate.hoverIcon == null ||
                    !candidate.interactable || candidate.holdInteraction)
                    continue;

                iconSource = candidate;
                break;
            }
        }

        if (iconSource != null && iconSource.hoverIcon != null)
        {
            trigger.hoverIcon = iconSource.hoverIcon;
            trigger.disabledHoverIcon = iconSource.disabledHoverIcon;
            return;
        }

        Sprite? vanillaGrabIcon = GameNetworkManager.Instance?.localPlayerController?.grabItemIcon;
        if (vanillaGrabIcon != null)
        {
            trigger.hoverIcon = vanillaGrabIcon;
            trigger.disabledHoverIcon = vanillaGrabIcon;
            return;
        }

        trigger.hoverIcon = null;
        trigger.disabledHoverIcon = null;
        Plugin.Log.LogWarning("GRADING PEDESTAL INTERACTION ICON UNAVAILABLE | No vanilla interaction sprite was available; icon omitted.");
    }

    private static void CreatePickupPedestal()
    {
        if (pickupPedestalRoot != null)
            return;

        if (returnPedestalPrefab == null)
        {
            Plugin.Log.LogError("GRADED PICKUP PEDESTAL SPAWN FAILED | GradingReturnPedestal prefab was not loaded.");
            return;
        }

        pickupPedestalRoot =
            Object.Instantiate(
                returnPedestalPrefab,
                GradedCardPickupPosition,
                Quaternion.Euler(0f, 90f, 0f)
            );

        pickupPedestalRoot.name =
            "LethalCardsGradedPickupPedestal";

        Transform? catchSurface =
            FindChild(pickupPedestalRoot.transform, "CardCatchSurface");

        returnRestAnchor =
            FindChild(pickupPedestalRoot.transform, "GradedCardRestAnchor");

        if (catchSurface == null)
            Plugin.Log.LogError("GRADED PICKUP PEDESTAL ERROR | GradingReturnPedestal is missing required child CardCatchSurface.");

        if (returnRestAnchor == null)
        {
            Plugin.Log.LogError("GRADED PICKUP PEDESTAL ERROR | GradingReturnPedestal is missing required child GradedCardRestAnchor.");
        }
        else
        {
            /* Plugin.Log.LogInfo(
                $"GRADED PICKUP REST ANCHOR | " +
                $"WorldPosition={returnRestAnchor.position} | " +
                $"WorldRotation={returnRestAnchor.rotation.eulerAngles}"
            ); */
        }

        /* Plugin.Log.LogInfo(
            $"GRADED PICKUP PEDESTAL PREFAB SPAWNED | " +
            $"Position={pickupPedestalRoot.transform.position} | " +
            $"Rotation={pickupPedestalRoot.transform.rotation.eulerAngles} | " +
            $"CatchSurfaceFound={catchSurface != null} | " +
            $"RestAnchorFound={returnRestAnchor != null}"
        ); */
    }

    private static GameObject? LoadPrefab(
        AssetBundle bundle,
        string expectedName,
        bool logMissing = true)
    {
        string[] assetNames =
            bundle.GetAllAssetNames();

        string? path = null;

        foreach (string assetName in assetNames)
        {
            string fileName =
                System.IO.Path.GetFileNameWithoutExtension(assetName);

            if (fileName.Equals(expectedName, System.StringComparison.OrdinalIgnoreCase))
            {
                path = assetName;
                break;
            }
        }

        GameObject? prefab =
            path == null ? null : bundle.LoadAsset<GameObject>(path);

        if (prefab == null)
        {
            foreach (GameObject candidate in bundle.LoadAllAssets<GameObject>())
            {
                if (!candidate.name.Equals(expectedName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                prefab = candidate;
                break;
            }
        }

        if (prefab == null)
        {
            if (logMissing)
                Plugin.Log.LogError($"GRADING PEDESTAL ASSET MISSING | Name={expectedName}");
        }
        else
        {
            Plugin.Log.LogInfo($"GRADING PEDESTAL ASSET READY | Name={expectedName} | Prefab={prefab.name}");
        }

        return prefab;
    }

    private static Transform? FindChild(
        Transform root,
        string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    public static void Reset()
    {
        if (pedestalRoot != null)
        {
            Object.Destroy(
                pedestalRoot
            );

            pedestalRoot =
                null;
            animationController = null;
        }

        if (pickupPedestalRoot != null)
        {
            Object.Destroy(
                pickupPedestalRoot
            );

            pickupPedestalRoot =
                null;
        }

        returnRestAnchor =
            null;
    }
}
