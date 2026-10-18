using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LethalCards.Grading;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LethalCards.Museum;

internal static class CompanyMuseumDisplaySpawner
{
    private const string PrefabName = "CompanyMuseumDisplayCase";
    private const string InstanceName = "LethalCardsCompanyMuseumDisplayCase";
    private const float FloorProbeHeight = 1.5f;
    private const float FloorProbeDistance = 4f;
    private const float AccessProbeOffset = 0.9f;
    private const float WallProbeForwardOffset = 1f;
    private const float WallProbeDistance = 3f;
    private const float MinimumWallNormalAlignment = 0.65f;

    private static readonly HashSet<string> StructuralGeometryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BackPanel", "FrameTop", "FrameBottom", "FrameLeft", "FrameRight"
    };

    private static readonly Vector3 SubmissionInnerMarker = new(-27.83f, -2.63f, -20.95f);
    private static readonly Vector3 ReturnInnerMarker = new(-27.87f, -2.63f, -15.40f);

    private static GameObject? museumPrefab;
    private static string? museumAssetPath;
    private static GameObject? museumRoot;
    private static CompanyMuseumDisplayController? controller;
    private static bool missingPrefabLogged;

    internal static CompanyMuseumDisplayController? Controller => controller != null ? controller : null;

    internal static void LoadAssets(AssetBundle bundle)
    {
        museumPrefab = LoadPrefab(bundle, PrefabName, out museumAssetPath);
        if (museumPrefab == null)
        {
            Plugin.Log.LogError(
                $"COMPANY MUSEUM ASSET MISSING | Name={PrefabName} | Bundle=lethalcards | " +
                "Assign the prefab to the lethalcards AssetBundle and rebuild it.");
            return;
        }

        ValidatePrefabHierarchy(museumPrefab);
        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM PREFAB LOADED | Name={museumPrefab.name} | " +
            $"AssetPath={museumAssetPath ?? "ResolvedByObjectName"}");
    }

    internal static void TrySpawn()
    {
        if (museumRoot != null || !IsCompanyReady())
            return;
        if (museumPrefab == null)
        {
            if (!missingPrefabLogged)
            {
                missingPrefabLogged = true;
                Plugin.Log.LogError(
                    $"COMPANY MUSEUM SPAWN FAILED | Prefab={PrefabName} was not loaded; " +
                    "no runtime placeholder will be created.");
            }
            return;
        }
        if (!GradingPedestalSpawner.TryGetSpawnedPedestals(
                out GameObject submissionPedestal, out GameObject returnPedestal))
            return;

        Vector3 longAxis = returnPedestal.transform.position - submissionPedestal.transform.position;
        longAxis.y = 0f;
        if (longAxis.sqrMagnitude < 0.01f)
        {
            Plugin.Log.LogError("COMPANY MUSEUM SPAWN FAILED | Grading pedestal separation was invalid");
            return;
        }
        longAxis.Normalize();

        bool submissionBoundsFound = TryGetGeometryBounds(submissionPedestal, out Bounds submissionBounds);
        bool returnBoundsFound = TryGetGeometryBounds(returnPedestal, out Bounds returnBounds);
        float submissionInner;
        float returnInner;
        Vector3 center;
        string footprintSource;
        if (submissionBoundsFound && returnBoundsFound)
        {
            ProjectBounds(submissionBounds, longAxis, out _, out submissionInner);
            ProjectBounds(returnBounds, longAxis, out returnInner, out _);
            float midpoint = (submissionInner + returnInner) * 0.5f;
            center = (submissionPedestal.transform.position + returnPedestal.transform.position) * 0.5f;
            center += longAxis * (midpoint - Vector3.Dot(center, longAxis));
            footprintSource = "RuntimeRendererBounds";
        }
        else
        {
            submissionInner = Vector3.Dot(SubmissionInnerMarker, longAxis);
            returnInner = Vector3.Dot(ReturnInnerMarker, longAxis);
            center = new Vector3(
                CompanyMuseumDisplayConfig.FallbackCenterX,
                CompanyMuseumDisplayConfig.ReferenceFloorY,
                CompanyMuseumDisplayConfig.FallbackCenterZ);
            footprintSource = "ValidatedWorldFallback";
            Plugin.Log.LogWarning(
                $"COMPANY MUSEUM GEOMETRY FALLBACK | SubmissionBounds={submissionBoundsFound} | " +
                $"ReturnBounds={returnBoundsFound}");
        }

        float availableLength = returnInner - submissionInner;
        float bodyLength = CompanyMuseumDisplayConfig.DesiredLength;
        if (availableLength - CompanyMuseumDisplayConfig.PedestalClearance * 2f < bodyLength)
        {
            Plugin.Log.LogWarning(
                $"COMPANY MUSEUM BODY CLEARANCE WARNING | AvailableLength={availableLength:F2} | " +
                $"AuthoredBodyLength={bodyLength:F2} | RequestedClearance={CompanyMuseumDisplayConfig.PedestalClearance:F2} | " +
                "Prefab will not be distorted");
        }

        if (!TryFindFloor(center, out float floorY, out string floorCollider))
        {
            floorY = CompanyMuseumDisplayConfig.ReferenceFloorY;
            floorCollider = "FallbackReferenceY";
        }
        center.y = floorY + CompanyMuseumDisplayConfig.Height * 0.5f;
        Vector3 front = DetermineFrontDirection(center, longAxis, submissionPedestal, out string frontSource);
        Quaternion rotation = Quaternion.LookRotation(front, Vector3.up);

        museumRoot = UnityEngine.Object.Instantiate(museumPrefab, center, rotation);
        museumRoot.name = InstanceName;
        DepositItemsDesk? desk = UnityEngine.Object.FindObjectOfType<DepositItemsDesk>();
        Scene targetScene = desk == null ? default : desk.gameObject.scene;
        if (targetScene.IsValid() && targetScene.isLoaded && museumRoot.scene != targetScene)
            SceneManager.MoveGameObjectToScene(museumRoot, targetScene);

        int environmentCollisionLayer = -1;
        string wallDiagnostic;
        if (TryAdjustToCompanyWall(
                museumRoot,
                front,
                submissionPedestal,
                returnPedestal,
                out environmentCollisionLayer,
                out wallDiagnostic))
        {
            center = museumRoot.transform.position;
            Plugin.Log.LogInfo($"COMPANY MUSEUM WALL PLACEMENT | {wallDiagnostic}");
        }
        else
        {
            Plugin.Log.LogWarning(
                $"COMPANY MUSEUM WALL PLACEMENT FALLBACK | Position={Format(center)} | {wallDiagnostic}");
        }

        controller = museumRoot.AddComponent<CompanyMuseumDisplayController>();
        if (!controller.Initialize(environmentCollisionLayer))
        {
            Plugin.Log.LogError("COMPANY MUSEUM SPAWN FAILED | Authored slot hierarchy validation failed");
            UnityEngine.Object.Destroy(museumRoot);
            museumRoot = null;
            controller = null;
            return;
        }

        TryGetGeometryBounds(museumRoot, out Bounds finalBounds);
        ProjectBounds(finalBounds, longAxis, out float museumMin, out float museumMax);
        float centerProjection = Vector3.Dot(center, longAxis);
        float bodySubmissionClearance = centerProjection - bodyLength * 0.5f - submissionInner;
        float bodyReturnClearance = returnInner - (centerProjection + bodyLength * 0.5f);
        NetworkManager? manager = NetworkManager.Singleton;

        Plugin.Log.LogInfo(
            $"COMPANY MUSEUM SPAWNED | ActiveScene={SceneManager.GetActiveScene().name} | " +
            $"ObjectScene={museumRoot.scene.name} | AssetPath={museumAssetPath ?? "ResolvedByObjectName"} | " +
            $"Host={manager?.IsHost == true} | Client={manager?.IsClient == true} | NetworkedModel=False | " +
            $"Position={Format(center)} | Rotation={Format(rotation.eulerAngles)} | Front={Format(front)} | " +
            $"FrontSource={frontSource} | FloorY={floorY:F2} | FloorCollider={floorCollider} | " +
            $"AuthoredBounds={FormatBounds(finalBounds)} | FootprintSource={footprintSource} | " +
            $"AvailableLength={availableLength:F2} | MainBodyLength={bodyLength:F2} | " +
            $"MainBodySubmissionClearance={bodySubmissionClearance:F2} | " +
            $"MainBodyReturnClearance={bodyReturnClearance:F2} | " +
            $"DecorativeProjectedMin={museumMin:F2} | DecorativeProjectedMax={museumMax:F2} | " +
            $"Discovery={CompanyMuseumManager.DiscoveryCount}/{CompanyMuseumManager.TotalEntries}");
    }

    internal static void Reset()
    {
        if (museumRoot != null)
        {
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM REMOVED | Scene={museumRoot.scene.name} | Position={Format(museumRoot.transform.position)}");
            UnityEngine.Object.Destroy(museumRoot);
        }
        museumRoot = null;
        controller = null;
    }

    private static bool IsCompanyReady()
    {
        NetworkManager? manager = NetworkManager.Singleton;
        StartOfRound? round = StartOfRound.Instance;
        return manager != null && manager.IsListening && !manager.ShutdownInProgress &&
               round != null && !round.shipIsLeaving && round.currentLevel != null &&
               round.currentLevel.PlanetName.Contains("Gordion", StringComparison.OrdinalIgnoreCase) &&
               UnityEngine.Object.FindObjectOfType<DepositItemsDesk>() != null;
    }

    private static Vector3 DetermineFrontDirection(
        Vector3 center, Vector3 longAxis, GameObject submissionPedestal, out string source)
    {
        Vector3 perpendicular = Vector3.Cross(Vector3.up, longAxis).normalized;
        Transform? interactionAnchor = FindDescendant(submissionPedestal.transform, "InteractionAnchor");
        Vector3 authoredDirection = interactionAnchor == null
            ? submissionPedestal.transform.forward
            : interactionAnchor.forward;
        authoredDirection.y = 0f;
        authoredDirection -= longAxis * Vector3.Dot(authoredDirection, longAxis);
        if (authoredDirection.sqrMagnitude < 0.0025f)
        {
            authoredDirection = submissionPedestal.transform.forward;
            authoredDirection.y = 0f;
            authoredDirection -= longAxis * Vector3.Dot(authoredDirection, longAxis);
        }
        if (authoredDirection.sqrMagnitude > 0.0025f &&
            Vector3.Dot(perpendicular, authoredDirection) < 0f)
            perpendicular = -perpendicular;

        bool authoredFloor = TryFindFloor(center + perpendicular * AccessProbeOffset, out _, out _);
        bool oppositeFloor = TryFindFloor(center - perpendicular * AccessProbeOffset, out _, out _);
        if (!authoredFloor && oppositeFloor)
        {
            source = "CompanyGroundProbeOppositeAuthoredFront";
            return -perpendicular;
        }
        source = authoredFloor == oppositeFloor
            ? "SubmissionInteractionOrientation"
            : "CompanyGroundProbeAuthoredFront";
        return perpendicular;
    }

    private static bool TryFindFloor(Vector3 position, out float floorY, out string colliderName)
    {
        floorY = 0f;
        colliderName = string.Empty;
        Vector3 origin = new(position.x,
            CompanyMuseumDisplayConfig.ReferenceFloorY + FloorProbeHeight, position.z);
        RaycastHit[] hits = Physics.RaycastAll(
            origin, Vector3.down, FloorProbeDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bestDifference = float.MaxValue;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.normal.y < 0.5f)
                continue;
            float difference = Mathf.Abs(hit.point.y - CompanyMuseumDisplayConfig.ReferenceFloorY);
            if (difference > 1f || difference >= bestDifference)
                continue;
            bestDifference = difference;
            floorY = hit.point.y;
            colliderName = hit.collider.name;
        }
        return bestDifference < float.MaxValue;
    }

    private static bool TryAdjustToCompanyWall(
        GameObject root,
        Vector3 front,
        GameObject submissionPedestal,
        GameObject returnPedestal,
        out int environmentCollisionLayer,
        out string diagnostic)
    {
        environmentCollisionLayer = -1;
        diagnostic = "Reason=No compatible Company wall hit";
        if (!TryGetStructuralFrontProjection(
                root, front, out float structuralBack, out float structuralFront, out string boundsSource))
        {
            diagnostic = "Reason=Structural back surface could not be measured";
            return false;
        }

        Vector3 origin = root.transform.position + front * WallProbeForwardOffset;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -front,
            WallProbeDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            Collider? collider = hit.collider;
            if (collider == null || collider.transform.IsChildOf(root.transform) ||
                collider.transform.IsChildOf(submissionPedestal.transform) ||
                collider.transform.IsChildOf(returnPedestal.transform))
            {
                continue;
            }

            float normalAlignment = Vector3.Dot(hit.normal, front);
            if (normalAlignment < MinimumWallNormalAlignment || Mathf.Abs(hit.normal.y) > 0.35f)
                continue;

            float wallProjection = Vector3.Dot(hit.point, front);
            float initialClearance = structuralBack - wallProjection;
            if (Mathf.Abs(initialClearance) > 1.5f)
                continue;

            float adjustment = CompanyMuseumDisplayConfig.WallClearance - initialClearance;
            root.transform.position += front * adjustment;
            Physics.SyncTransforms();
            TryGetStructuralFrontProjection(
                root, front, out float finalBack, out float finalFront, out _);
            environmentCollisionLayer = collider.gameObject.layer;
            diagnostic =
                $"Collider={collider.name} | ColliderLayer={LayerMask.LayerToName(environmentCollisionLayer)}({environmentCollisionLayer}) | " +
                $"WallPoint={Format(hit.point)} | WallNormal={Format(hit.normal)} | NormalAlignment={normalAlignment:F3} | " +
                $"StructuralBoundsSource={boundsSource} | InitialBackPlane={structuralBack:F3} | " +
                $"InitialFrontPlane={structuralFront:F3} | Adjustment={adjustment:F3} | " +
                $"FinalBackPlane={finalBack:F3} | FinalFrontPlane={finalFront:F3} | " +
                $"FinalClearance={finalBack - wallProjection:F3} | RequestedClearance={CompanyMuseumDisplayConfig.WallClearance:F3} | " +
                $"AdjustedPosition={Format(root.transform.position)}";
            return true;
        }

        diagnostic =
            $"Reason=No compatible Company wall hit | ProbeOrigin={Format(origin)} | " +
            $"ProbeDirection={Format(-front)} | ProbeDistance={WallProbeDistance:F2} | " +
            $"StructuralBoundsSource={boundsSource} | BackPlane={structuralBack:F3} | Hits={hits.Length}";
        return false;
    }

    private static bool TryGetStructuralFrontProjection(
        GameObject root,
        Vector3 front,
        out float back,
        out float frontSurface,
        out string source)
    {
        back = float.MaxValue;
        frontSurface = float.MinValue;
        int renderers = 0;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !StructuralGeometryNames.Contains(renderer.gameObject.name) ||
                renderer.bounds.size.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            ProjectBounds(renderer.bounds, front, out float minimum, out float maximum);
            back = Mathf.Min(back, minimum);
            frontSurface = Mathf.Max(frontSurface, maximum);
            renderers++;
        }

        if (renderers > 0)
        {
            source = $"StructuralRenderers({renderers})";
            return true;
        }

        Transform? visualRoot = FindDescendant(root.transform, "VisualRoot");
        BoxCollider? bodyCollider = visualRoot?.GetComponent<BoxCollider>();
        if (bodyCollider != null && !bodyCollider.isTrigger)
        {
            ProjectBounds(bodyCollider.bounds, front, out back, out frontSurface);
            source = "AuthoredVisualRootColliderFallback";
            return true;
        }

        source = "Unavailable";
        return false;
    }

    private static GameObject? LoadPrefab(AssetBundle bundle, string expectedName, out string? loadedPath)
    {
        loadedPath = null;
        foreach (string path in bundle.GetAllAssetNames())
        {
            if (!Path.GetFileNameWithoutExtension(path).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                continue;
            GameObject? prefab = bundle.LoadAsset<GameObject>(path);
            if (prefab == null)
                continue;
            loadedPath = path;
            return prefab;
        }
        return bundle.LoadAllAssets<GameObject>().FirstOrDefault(candidate =>
            candidate.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidatePrefabHierarchy(GameObject prefab)
    {
        List<string> missing = new();
        string[] visualNames =
        {
            "VisualRoot", "BackPanel", "FrameTop", "FrameBottom", "FrameLeft", "FrameRight",
            "HeaderPlacard", "HeaderText", "StandardPlacard", "FoilPlacard", "AltArtPlacard",
            "MisprintPlacard", "SectionDividers", "CardGridDividers", "MuseumLights", "DisplaySlots"
        };
        foreach (string name in visualNames)
            if (FindDescendant(prefab.transform, name) == null)
                missing.Add(name);

        int anchors = 0;
        string[] sectionNames = { "Standard", "Foil", "AltArt", "Misprint" };
        Transform? displaySlots = FindDescendant(prefab.transform, "DisplaySlots");
        foreach (string sectionName in sectionNames)
        {
            Transform? section = displaySlots == null ? null : FindDirectChild(displaySlots, sectionName);
            if (section == null)
            {
                missing.Add($"DisplaySlots/{sectionName}");
                continue;
            }
            for (int rowIndex = 0; rowIndex < 3; rowIndex++)
            {
                Transform? row = FindDirectChild(section, $"Row{rowIndex + 1:00}");
                if (row == null)
                {
                    missing.Add($"{sectionName}/Row{rowIndex + 1:00}");
                    continue;
                }
                for (int column = 0; column < 11; column++)
                {
                    int setNumber = rowIndex * 11 + column + 1;
                    if (FindDirectChild(row, $"Slot{setNumber:00}") == null)
                        missing.Add($"{sectionName}/Slot{setNumber:00}");
                    else
                        anchors++;
                }
            }
        }

        int previewChildren = 0;
        if (displaySlots != null)
        {
            foreach (Transform child in displaySlots.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("Slot", StringComparison.OrdinalIgnoreCase))
                    previewChildren += child.childCount;
        }
        if (missing.Count > 0 || anchors != CompanyMuseumManager.TotalEntries)
            Plugin.Log.LogError(
                $"COMPANY MUSEUM PREFAB HIERARCHY INVALID | Anchors={anchors}/{CompanyMuseumManager.TotalEntries} | " +
                $"Missing={string.Join(",", missing)}");
        else
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM PREFAB VALIDATED | Anchors={anchors}/{CompanyMuseumManager.TotalEntries} | " +
                $"Lights={prefab.GetComponentsInChildren<Light>(true).Length} | PreviewChildren={previewChildren}");
        if (previewChildren > 0)
            Plugin.Log.LogError(
                $"COMPANY MUSEUM PREFAB PREVIEW WARNING | SlotChildObjects={previewChildren} | " +
                "Remove all test card/slab children beneath slot anchors and rebuild the AssetBundle.");

        int networkObjects = prefab.GetComponentsInChildren<NetworkObject>(true).Length;
        int grabbables = prefab.GetComponentsInChildren<GrabbableObject>(true).Length;
        int rigidbodies = prefab.GetComponentsInChildren<Rigidbody>(true).Length;
        if (networkObjects > 0 || grabbables > 0 || rigidbodies > 0)
            Plugin.Log.LogError(
                $"COMPANY MUSEUM PREFAB GAMEPLAY COMPONENTS | NetworkObjects={networkObjects} | " +
                $"Grabbables={grabbables} | Rigidbodies={rigidbodies} | " +
                "Remove preview/gameplay components in Unity; runtime sanitization will prevent them from becoming active.");
    }

    private static bool TryGetGeometryBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer is ParticleSystemRenderer ||
                renderer.bounds.size.sqrMagnitude < 0.0001f)
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static void ProjectBounds(Bounds bounds, Vector3 axis, out float minimum, out float maximum)
    {
        float center = Vector3.Dot(bounds.center, axis);
        float radius = Mathf.Abs(axis.x) * bounds.extents.x +
                       Mathf.Abs(axis.y) * bounds.extents.y +
                       Mathf.Abs(axis.z) * bounds.extents.z;
        minimum = center - radius;
        maximum = center + radius;
    }

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

    private static string Format(Vector3 value) =>
        $"({value.x:F2}, {value.y:F2}, {value.z:F2})";

    private static string FormatBounds(Bounds bounds) =>
        $"Center={Format(bounds.center)},Size={Format(bounds.size)}";
}
