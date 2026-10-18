using System;
using System.Collections;
using System.Collections.Generic;
using GameNetcodeStuff;
using LethalCards.Cards;
using LethalCards.ShipDisplay;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.Museum;

internal sealed class CompanyMuseumDisplayController : MonoBehaviour
{
    private const float ServerHeldStateSettleTimeout = 0.6f;
    private const float InteractionDistancePadding = 1f;
    private const float LocalRequestTimeout = 2f;
    private const string InteractionTargetName = "LethalCardsMuseumInteractionTarget";
    private const string ScanTargetName = "LethalCardsMuseumScanNode";
    private const string VisualMountName = "LethalCardsMuseumCardMount";

    private readonly Transform?[] slotAnchors =
        new Transform?[CompanyMuseumManager.TotalEntries];
    private readonly GameObject?[] slotVisuals =
        new GameObject?[CompanyMuseumManager.TotalEntries];
    private readonly Queue<ulong> serverRegistrationQueue = new();
    private readonly HashSet<ulong> queuedClients = new();

    private Collider? interactionCollider;
    private Collider? physicalCollider;
    private ScanNodeProperties? scanNode;
    private bool processingServerQueue;
    private bool localRequestPending;
    private float localRequestDeadline;

    internal bool Initialize(int environmentCollisionLayer)
    {
        SanitizeAuthoredGameplayComponents(environmentCollisionLayer);
        if (!CacheAndValidateAnchors())
            return false;
        RemoveAuthoredSlotPreviewChildren();
        CreateInteractionTarget();
        CreateScanNode();
        CompanyMuseumManager.Changed += OnMuseumChanged;
        RefreshAllVisuals();
        RefreshScanNode();
        return true;
    }

    private void OnDestroy()
    {
        CompanyMuseumManager.Changed -= OnMuseumChanged;
        ClearVisuals();
    }

    internal void RequestRegistration()
    {
        if (localRequestPending && Time.realtimeSinceStartup < localRequestDeadline)
            return;
        localRequestPending = true;
        localRequestDeadline = Time.realtimeSinceStartup + LocalRequestTimeout;
        CompanyMuseumNetworkSync.RequestRegistration();
    }

    internal bool IsLocalRequestPending()
    {
        if (localRequestPending && Time.realtimeSinceStartup >= localRequestDeadline)
            localRequestPending = false;
        return localRequestPending;
    }

    internal void QueueServerRegistration(ulong clientId)
    {
        if (NetworkManager.Singleton?.IsServer != true)
            return;
        if (!queuedClients.Add(clientId))
            return;
        serverRegistrationQueue.Enqueue(clientId);
        if (!processingServerQueue)
            StartCoroutine(ProcessServerQueue());
    }

    private IEnumerator ProcessServerQueue()
    {
        processingServerQueue = true;
        while (serverRegistrationQueue.Count > 0)
        {
            ulong clientId = serverRegistrationQueue.Dequeue();
            float deadline = Time.realtimeSinceStartup + ServerHeldStateSettleTimeout;
            while (!CompanyMuseumNetworkSync.IsAuthoritativeSelectedCardReady(clientId) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            CompanyMuseumNetworkSync.ProcessAuthoritativeRegistration(clientId, this);
            queuedClients.Remove(clientId);
        }
        processingServerQueue = false;
    }

    internal bool ValidateServerInteraction(PlayerControllerB player)
    {
        StartOfRound? round = StartOfRound.Instance;
        if (player == null || !player.isPlayerControlled || player.isPlayerDead ||
            player.gameplayCamera == null || round == null || round.shipIsLeaving ||
            round.currentLevel == null ||
            !round.currentLevel.PlanetName.Contains("Gordion", StringComparison.OrdinalIgnoreCase) ||
            UnityEngine.Object.FindObjectOfType<DepositItemsDesk>() == null)
            return false;

        Vector3 cameraPosition = player.gameplayCamera.transform.position;
        Vector3 target = interactionCollider == null
            ? transform.position
            : interactionCollider.ClosestPoint(cameraPosition);
        Vector3 toTarget = target - cameraPosition;
        float distance = toTarget.magnitude;
        if (distance < 0.01f || distance > player.grabDistance + InteractionDistancePadding ||
            Vector3.Dot(player.gameplayCamera.transform.forward, toTarget / distance) < 0.75f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            cameraPosition,
            toTarget / distance,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(player.transform) ||
                hit.collider.transform.IsChildOf(transform))
                continue;
            return false;
        }
        return true;
    }

    internal bool OwnsCollider(Collider collider) =>
        collider != null && collider.transform.IsChildOf(transform) &&
        collider == interactionCollider;

    internal string GetPrompt(CardInstanceData cardData)
    {
        if (cardData == null)
            return string.Empty;
        CardDefinition? definition = CardDatabase.GetById(cardData.CardId);
        if (definition == null)
            return string.Empty;
        if (!CompanyMuseumManager.TryGet(
                definition.CardId, cardData.Variant, out CompanyMuseumRecord existing))
            return "Register Card";
        return cardData.Grade > existing.Grade
            ? "Upgrade Museum Grade"
            : "Already Registered";
    }

    internal static void ShowResult(CompanyMuseumRegistrationResult result)
    {
        CompanyMuseumDisplayController? controller = CompanyMuseumDisplaySpawner.Controller;
        if (controller != null)
            controller.localRequestPending = false;
        if (HUDManager.Instance == null)
            return;
        string message = result switch
        {
            CompanyMuseumRegistrationResult.Registered => "Card registered. The physical card remains in your inventory.",
            CompanyMuseumRegistrationResult.Upgraded => "Museum grade upgraded. The physical card remains in your inventory.",
            CompanyMuseumRegistrationResult.AlreadyRegistered => "This variant is already registered at an equal or higher grade.",
            CompanyMuseumRegistrationResult.InvalidHeldItem => "Hold a valid Lethal Cards card in the selected inventory slot.",
            CompanyMuseumRegistrationResult.InvalidPlayerOrDistance => "Move closer and look directly at the museum.",
            CompanyMuseumRegistrationResult.PersistenceFailed => "Museum registration could not be saved.",
            _ => "The museum is processing another registration."
        };
        HUDManager.Instance.DisplayTip(
            "Lethal Cards Collection", message, false, false, "LC_Tip1");
    }

    private bool CacheAndValidateAnchors()
    {
        Transform? displaySlots = FindDirectChild(transform, "DisplaySlots");
        if (displaySlots == null)
        {
            Plugin.Log.LogError("COMPANY MUSEUM PREFAB INVALID | Missing=DisplaySlots");
            return false;
        }

        string[] sectionNames = { "Standard", "Foil", "AltArt", "Misprint" };
        List<string> missing = new();
        int anchorCount = 0;
        for (int variantIndex = 0; variantIndex < sectionNames.Length; variantIndex++)
        {
            Transform? section = FindDirectChild(displaySlots, sectionNames[variantIndex]);
            if (section == null)
            {
                missing.Add($"DisplaySlots/{sectionNames[variantIndex]}");
                continue;
            }

            for (int rowIndex = 0; rowIndex < 3; rowIndex++)
            {
                Transform? row = FindDirectChild(section, $"Row{rowIndex + 1:00}");
                if (row == null)
                {
                    missing.Add($"{sectionNames[variantIndex]}/Row{rowIndex + 1:00}");
                    continue;
                }
                for (int columnIndex = 0; columnIndex < 11; columnIndex++)
                {
                    int setNumber = rowIndex * 11 + columnIndex + 1;
                    Transform? slot = FindDirectChild(row, $"Slot{setNumber:00}");
                    if (slot == null)
                    {
                        missing.Add($"{sectionNames[variantIndex]}/Row{rowIndex + 1:00}/Slot{setNumber:00}");
                        continue;
                    }
                    slotAnchors[variantIndex * CompanyMuseumManager.CardSlotsPerVariant + setNumber - 1] = slot;
                    anchorCount++;
                }
            }

            Transform? first = slotAnchors[variantIndex * CompanyMuseumManager.CardSlotsPerVariant];
            Transform? eleventh = slotAnchors[variantIndex * CompanyMuseumManager.CardSlotsPerVariant + 10];
            if (first != null && eleventh != null &&
                (first.localPosition.x <= 0f || eleventh.localPosition.x >= 0f))
            {
                missing.Add($"{sectionNames[variantIndex]} slot X ordering is not right-to-left");
            }
        }

        if (missing.Count != 0 || anchorCount != CompanyMuseumManager.TotalEntries)
        {
            Plugin.Log.LogError(
                $"COMPANY MUSEUM PREFAB INVALID | Anchors={anchorCount}/{CompanyMuseumManager.TotalEntries} | " +
                $"Problems={string.Join(", ", missing)}");
            return false;
        }

        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM SLOTS VALIDATED | Anchors={anchorCount}/{CompanyMuseumManager.TotalEntries} | " +
            "Mapping=VariantSection+CardDefinition.SetNumber | SlotOrder=RightToLeft");
        return true;
    }

    private void SanitizeAuthoredGameplayComponents(int environmentCollisionLayer)
    {
        Transform? visualRoot = FindDescendant(transform, "VisualRoot");
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        BoxCollider? authoredBody = null;
        if (visualRoot != null)
        {
            foreach (BoxCollider candidate in visualRoot.GetComponents<BoxCollider>())
            {
                if (candidate.isTrigger)
                    continue;
                if (authoredBody == null || ColliderVolume(candidate) > ColliderVolume(authoredBody))
                    authoredBody = candidate;
            }
        }

        foreach (Collider collider in colliders)
        {
            if (collider == authoredBody)
                continue;
            collider.enabled = false;
            Destroy(collider);
        }

        bool authoredColliderPreserved = authoredBody != null;
        if (authoredBody == null)
        {
            Transform fallbackParent = visualRoot ?? transform;
            authoredBody = fallbackParent.gameObject.AddComponent<BoxCollider>();
            authoredBody.center = Vector3.zero;
            authoredBody.size = new Vector3(
                CompanyMuseumDisplayConfig.DesiredLength,
                CompanyMuseumDisplayConfig.Height,
                CompanyMuseumDisplayConfig.Depth);
            Plugin.Log.LogError(
                "COMPANY MUSEUM PHYSICAL COLLIDER MISSING | Expected a non-trigger BoxCollider directly on VisualRoot | " +
                "RuntimeFallback=True | Rebuild the lethalcards AssetBundle with the authored VisualRoot collider.");
        }

        authoredBody.enabled = true;
        authoredBody.isTrigger = false;
        int playerLayer = LayerMask.NameToLayer("Player");
        int colliderLayer = authoredBody.gameObject.layer;
        bool currentLayerIgnored = playerLayer >= 0 &&
                                   Physics.GetIgnoreLayerCollision(playerLayer, colliderLayer);
        bool environmentLayerUsable = playerLayer >= 0 && environmentCollisionLayer >= 0 &&
                                      !Physics.GetIgnoreLayerCollision(playerLayer, environmentCollisionLayer);
        if (currentLayerIgnored && environmentLayerUsable)
        {
            authoredBody.gameObject.layer = environmentCollisionLayer;
            colliderLayer = environmentCollisionLayer;
        }
        physicalCollider = authoredBody;

        Rigidbody[] rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        foreach (Rigidbody rigidbody in rigidbodies)
            Destroy(rigidbody);
        GrabbableObject[] grabbables = GetComponentsInChildren<GrabbableObject>(true);
        foreach (GrabbableObject grabbable in grabbables)
            Destroy(grabbable);
        NetworkBehaviour[] networkBehaviours = GetComponentsInChildren<NetworkBehaviour>(true);
        foreach (NetworkBehaviour networkBehaviour in networkBehaviours)
            Destroy(networkBehaviour);
        NetworkObject[] networkObjects = GetComponentsInChildren<NetworkObject>(true);
        foreach (NetworkObject networkObject in networkObjects)
            Destroy(networkObject);
        Physics.SyncTransforms();
        bool playerCollisionEnabled = playerLayer < 0 ||
                                      !Physics.GetIgnoreLayerCollision(playerLayer, colliderLayer);
        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM PHYSICAL COLLIDER | Source={(authoredColliderPreserved ? "AuthoredVisualRoot" : "RuntimeFallback")} | " +
            $"Center={Format(authoredBody.center)} | Size={Format(authoredBody.size)} | " +
            $"WorldBoundsCenter={Format(authoredBody.bounds.center)} | WorldBoundsSize={Format(authoredBody.bounds.size)} | " +
            $"Layer={LayerMask.LayerToName(colliderLayer)}({colliderLayer}) | " +
            $"PlayerLayer={playerLayer} | PlayerCollision={playerCollisionEnabled} | Trigger={authoredBody.isTrigger}");
        if (!playerCollisionEnabled)
            Plugin.Log.LogError(
                "COMPANY MUSEUM PHYSICAL COLLISION DISABLED | The active physics collision matrix ignores the museum collider layer for players.");

        if (colliders.Length > (authoredColliderPreserved ? 1 : 0) || rigidbodies.Length > 0 ||
            grabbables.Length > 0 || networkBehaviours.Length > 0 || networkObjects.Length > 0)
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM PREFAB SANITIZED | RemovedColliders={colliders.Length - (authoredColliderPreserved ? 1 : 0)} | " +
                $"Rigidbodies={rigidbodies.Length} | Grabbables={grabbables.Length} | " +
                $"NetworkBehaviours={networkBehaviours.Length} | NetworkObjects={networkObjects.Length} | " +
                "PreservedPhysicalColliders=1 | RuntimeInteractionTargets=2");
    }

    private void RemoveAuthoredSlotPreviewChildren()
    {
        List<string> previews = new();
        foreach (Transform? anchor in slotAnchors)
        {
            if (anchor == null)
                continue;
            for (int index = anchor.childCount - 1; index >= 0; index--)
            {
                Transform child = anchor.GetChild(index);
                previews.Add(GetPath(child));
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
        if (previews.Count > 0)
        {
            Plugin.Log.LogError(
                $"COMPANY MUSEUM PREVIEW OBJECTS REMOVED | Count={previews.Count} | " +
                $"Objects={string.Join(",", previews)} | Remove these children from the Unity prefab before rebuilding the bundle.");
        }
    }

    private void CreateInteractionTarget()
    {
        GameObject target = new(InteractionTargetName);
        target.transform.SetParent(transform, false);
        target.transform.localPosition = Vector3.zero;
        int layer = LayerMask.NameToLayer("InteractableObject");
        if (layer >= 0)
            target.layer = layer;
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.size = new Vector3(
            CompanyMuseumDisplayConfig.DesiredLength,
            CompanyMuseumDisplayConfig.Height,
            Mathf.Max(CompanyMuseumDisplayConfig.Depth, 0.12f));
        collider.isTrigger = true;
        interactionCollider = collider;
    }

    private void CreateScanNode()
    {
        int scanLayer = LayerMask.NameToLayer("ScanNode");
        if (scanLayer < 0)
        {
            Plugin.Log.LogError("COMPANY MUSEUM SCAN DISABLED | Unity layer ScanNode was not found");
            return;
        }
        GameObject target = new(ScanTargetName);
        target.transform.SetParent(transform, false);
        target.transform.localPosition = Vector3.zero;
        target.layer = scanLayer;
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.size = new Vector3(
            CompanyMuseumDisplayConfig.DesiredLength,
            CompanyMuseumDisplayConfig.Height,
            Mathf.Max(CompanyMuseumDisplayConfig.Depth, 0.12f));
        collider.isTrigger = true;
        scanNode = target.AddComponent<ScanNodeProperties>();
        scanNode.headerText = "Lethal Cards Collection Museum";
        scanNode.scrapValue = 0;
        scanNode.minRange = 0;
        scanNode.maxRange = 20;
        scanNode.requiresLineOfSight = true;
        scanNode.nodeType = 1;
        RefreshScanNode();
        Plugin.Log.LogInfo("COMPANY MUSEUM SCAN READY | NodeType=Information | ScrapValue=0");
    }

    private void OnMuseumChanged(CompanyMuseumRecord? changed)
    {
        if (changed == null)
            RefreshAllVisuals();
        else
        {
            CardDefinition? definition = CardDatabase.GetById(changed.CardId);
            if (definition != null)
                RefreshSlotVisual(GetVisualIndex(definition.SetNumber, changed.Variant));
        }
        RefreshScanNode();
    }

    private void RefreshScanNode()
    {
        if (scanNode != null)
            scanNode.subText =
                $"{CompanyMuseumManager.DiscoveryCount} / {CompanyMuseumManager.TotalEntries} Card Variants Found";
    }

    private void RefreshAllVisuals()
    {
        for (int index = 0; index < slotVisuals.Length; index++)
            RefreshSlotVisual(index);
    }

    private void RefreshSlotVisual(int visualIndex)
    {
        if (visualIndex < 0 || visualIndex >= slotVisuals.Length)
            return;
        if (slotVisuals[visualIndex] != null)
        {
            slotVisuals[visualIndex]!.SetActive(false);
            Destroy(slotVisuals[visualIndex]);
            slotVisuals[visualIndex] = null;
        }

        Transform? anchor = slotAnchors[visualIndex];
        int variantIndex = visualIndex / CompanyMuseumManager.CardSlotsPerVariant;
        int setNumber = visualIndex % CompanyMuseumManager.CardSlotsPerVariant + 1;
        CardVariant variant = (CardVariant)variantIndex;
        CardDefinition? definition = null;
        foreach (CardDefinition candidate in CardDatabase.Cards)
        {
            if (candidate.SetNumber == setNumber)
            {
                definition = candidate;
                break;
            }
        }
        if (anchor == null || definition == null ||
            !CompanyMuseumManager.TryGet(definition.CardId, variant, out CompanyMuseumRecord record))
            return;

        GameObject mount = new(VisualMountName);
        mount.transform.SetParent(anchor, false);
        bool graded = record.Grade > 0;
        mount.transform.localPosition = graded
            ? new Vector3(0f, 0f, -0.0369f)
            : Vector3.zero;
        // Both source visuals face away from this authored slot plane at identity.
        // Rotate only the museum mount so held, binder, grading, and ship visuals
        // retain their existing prefab orientation.
        mount.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        float scale = graded
            ? CompanyMuseumDisplayConfig.GradedVisualScale
            : CompanyMuseumDisplayConfig.RawVisualScale;
        mount.transform.localScale = Vector3.one * scale;

        ShipCardDisplayStoredCard visualRecord = new(
            record.CardId,
            record.Variant,
            record.Grade,
            record.UngradedValue,
            record.FinalValue);
        GameObject? visual = ShipCardDisplayVisualFactory.Create(mount.transform, visualRecord);
        if (visual == null)
        {
            Destroy(mount);
            Plugin.Log.LogWarning(
                $"COMPANY MUSEUM VISUAL FAILED | CardId={record.CardId} | " +
                $"Variant={record.Variant} | Grade={record.Grade}");
            return;
        }

        StripForbiddenVisualComponents(mount);
        slotVisuals[visualIndex] = mount;
    }

    private static void StripForbiddenVisualComponents(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
            Destroy(collider);
        }
        foreach (Rigidbody rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
            Destroy(rigidbody);
        foreach (GrabbableObject grabbable in root.GetComponentsInChildren<GrabbableObject>(true))
            Destroy(grabbable);
        foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
            Destroy(networkObject);
    }

    private void ClearVisuals()
    {
        for (int index = 0; index < slotVisuals.Length; index++)
        {
            if (slotVisuals[index] != null)
                Destroy(slotVisuals[index]);
            slotVisuals[index] = null;
        }
    }

    private static int GetVisualIndex(int setNumber, CardVariant variant) =>
        (int)variant * CompanyMuseumManager.CardSlotsPerVariant + setNumber - 1;

    private static Transform? FindDirectChild(Transform root, string name)
    {
        for (int index = 0; index < root.childCount; index++)
        {
            Transform child = root.GetChild(index);
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private static Transform? FindDescendant(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return child;
        return null;
    }

    private static float ColliderVolume(BoxCollider collider)
    {
        Vector3 size = collider.size;
        return Mathf.Abs(size.x * size.y * size.z);
    }

    private static string Format(Vector3 value) =>
        $"({value.x:F2}, {value.y:F2}, {value.z:F2})";

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        Transform? parent = transform.parent;
        while (parent != null)
        {
            path = $"{parent.name}/{path}";
            parent = parent.parent;
        }
        return path;
    }
}
