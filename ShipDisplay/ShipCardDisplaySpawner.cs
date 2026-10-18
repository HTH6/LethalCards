using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using Unity.Netcode;
using UnityEngine;

namespace LethalCards.ShipDisplay;

internal static class ShipCardDisplaySpawner
{
    private const string VisualPrefabName = "ShipCardDisplayCase";
    private const string NetworkPrefabName = "LethalCardsShipCardDisplayCaseNetworkPrefab";
    private const string UnlockableName = "Ship Card Display Case";
    private const string PlacementPreviewName = "PlacementPreviewMesh";
    private const string PlacementColliderName = "ShipCardDisplayPlacementCollider";
    internal const string InteractionColliderName = "ShipCardDisplayInteractionCollider";
    internal const string AggregateScanNodeName = "ShipCardDisplayAggregateScanNode";

    private static readonly MethodInfo? SpawnUnlockableMethod =
        AccessTools.Method(typeof(StartOfRound), "SpawnUnlockable", new[] { typeof(int), typeof(bool) });

    private static GameObject? networkPrefab;
    private static UnlockableItem? displayCaseUnlockable;
    private static AutoParentToShip? prefabAutoParent;
    private static PlaceableShipObject? prefabPlaceable;
    private static readonly HashSet<ulong> publishedPlacements = new();
    private static readonly HashSet<GameObject> runtimeInstances = new();
    private static bool spawnFailureLogged;

    internal static void LoadAssets(AssetBundle bundle)
    {
        GameObject? visualPrefab = LoadPrefab(bundle, VisualPrefabName);
        if (visualPrefab == null)
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY ASSET MISSING | Name={VisualPrefabName} | " +
                "The display case will not be registered.");
            return;
        }

        ValidateAnchors(visualPrefab);

        if (visualPrefab.GetComponentInChildren<NetworkObject>(true) != null)
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY REGISTRATION FAILED | Prefab={VisualPrefabName} contains a NetworkObject. " +
                "The visual prefab must remain network-free because the mod creates a registered network root.");
            return;
        }

        if (!TryCreateFurniturePrefab(visualPrefab, out GameObject? createdPrefab))
            return;

        networkPrefab = createdPrefab!;
        displayCaseUnlockable = new UnlockableItem();
        displayCaseUnlockable.unlockableName = UnlockableName;
        displayCaseUnlockable.prefabObject = networkPrefab;
        displayCaseUnlockable.unlockableType = 1;
        displayCaseUnlockable.IsPlaceable = true;
        displayCaseUnlockable.spawnPrefab = true;
        displayCaseUnlockable.alreadyUnlocked = true;
        displayCaseUnlockable.hasBeenUnlockedByPlayer = true;
        displayCaseUnlockable.unlockedInChallengeFile = true;
        displayCaseUnlockable.canBeStored = false;
        displayCaseUnlockable.inStorage = false;
        displayCaseUnlockable.alwaysInStock = false;
        displayCaseUnlockable.maxNumber = 1;
        displayCaseUnlockable.luckValue = 0f;

        // A price of -1 keeps this permanent unlockable out of terminal shop listings.
        Unlockables.RegisterUnlockable(displayCaseUnlockable, -1, StoreType.None);

        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY FURNITURE REGISTERED | Unlockable={UnlockableName} | " +
            $"NetworkPrefab={networkPrefab.name} | CanBeStored={displayCaseUnlockable.canBeStored}");
    }

    internal static void PreparePrefabForUnlockableIndex(int unlockableIndex)
    {
        if (displayCaseUnlockable == null || prefabPlaceable == null || prefabAutoParent == null ||
            StartOfRound.Instance == null || unlockableIndex < 0 ||
            unlockableIndex >= StartOfRound.Instance.unlockablesList.unlockables.Count ||
            !ReferenceEquals(
                StartOfRound.Instance.unlockablesList.unlockables[unlockableIndex],
                displayCaseUnlockable))
        {
            return;
        }

        SetUnlockableId(unlockableIndex);
    }

    internal static void TrySpawn(StartOfRound round)
    {
        if (displayCaseUnlockable == null || networkPrefab == null || round == null ||
            round.unlockablesList == null || round.unlockablesList.unlockables == null)
        {
            return;
        }

        int unlockableId = round.unlockablesList.unlockables.IndexOf(displayCaseUnlockable);
        if (unlockableId < 0)
            return;

        SetUnlockableId(unlockableId);
        displayCaseUnlockable.alreadyUnlocked = true;
        displayCaseUnlockable.hasBeenUnlockedByPlayer = true;
        displayCaseUnlockable.inStorage = false;
        displayCaseUnlockable.canBeStored = false;

        if (TryFindSpawnedInstance(round, unlockableId, out GameObject? existingInstance))
        {
            ConfigureSpawnedInstance(existingInstance!, unlockableId);
            TryPublishAuthoritativePlacement(existingInstance!);
            return;
        }

        NetworkManager? networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening ||
            networkManager.ShutdownInProgress || !networkManager.IsServer ||
            ShipBuildModeManager.Instance == null)
        {
            return;
        }

        if (SpawnUnlockableMethod == null)
        {
            if (!spawnFailureLogged)
            {
                spawnFailureLogged = true;
                Plugin.Log.LogError(
                    "SHIP CARD DISPLAY SPAWN FAILED | StartOfRound.SpawnUnlockable was not found.");
            }
            return;
        }

        try
        {
            SpawnUnlockableMethod.Invoke(round, new object[] { unlockableId, false });

            if (!TryFindSpawnedInstance(round, unlockableId, out GameObject? spawnedInstance))
            {
                if (!spawnFailureLogged)
                {
                    spawnFailureLogged = true;
                    Plugin.Log.LogError(
                        "SHIP CARD DISPLAY SPAWN FAILED | Vanilla SpawnUnlockable returned without a spawned cabinet.");
                }
                return;
            }

            spawnFailureLogged = false;
            ConfigureSpawnedInstance(spawnedInstance!, unlockableId);
            TryPublishAuthoritativePlacement(spawnedInstance!);
            Plugin.Log.LogInfo(
                $"SHIP CARD DISPLAY SPAWNED | UnlockableId={unlockableId} | " +
                "Rendering=Networked vanilla ship furniture");
        }
        catch (TargetInvocationException exception)
        {
            LogSpawnFailure(exception.InnerException ?? exception);
        }
        catch (Exception exception)
        {
            LogSpawnFailure(exception);
        }
    }

    internal static void CleanupForRoundTeardown(StartOfRound round)
    {
        if (displayCaseUnlockable != null && round != null &&
            round.unlockablesList?.unlockables != null)
        {
            int unlockableId = round.unlockablesList.unlockables.IndexOf(displayCaseUnlockable);
            if (unlockableId >= 0 && round.SpawnedShipUnlockables != null &&
                round.SpawnedShipUnlockables.TryGetValue(unlockableId, out GameObject instance) &&
                instance != null)
            {
                runtimeInstances.Add(instance);
            }
        }

        // Catch a cabinet clone that fell out of the vanilla dictionary during
        // network shutdown. The inactive LethalLib prefab template is excluded
        // by FindObjectsOfType and by the clone-name check.
        foreach (AutoParentToShip autoParent in
                 UnityEngine.Object.FindObjectsOfType<AutoParentToShip>())
        {
            if (autoParent != null &&
                autoParent.gameObject.name.StartsWith(
                    NetworkPrefabName,
                    StringComparison.Ordinal))
            {
                runtimeInstances.Add(autoParent.gameObject);
            }
        }

        int cleanedInstances = 0;
        foreach (GameObject instance in new List<GameObject>(runtimeInstances))
        {
            if (instance == null || ReferenceEquals(instance, networkPrefab))
                continue;

            AutoParentToShip? autoParent = instance.GetComponent<AutoParentToShip>();
            if (autoParent != null)
                autoParent.enabled = false;

            NetworkObject? networkObject = instance.GetComponent<NetworkObject>();
            NetworkManager? networkManager = NetworkManager.Singleton;
            bool waitingForServerDespawn = false;
            bool destroyedByNetwork = false;

            try
            {
                if (networkObject != null && networkObject.IsSpawned &&
                    networkManager != null && networkManager.IsListening &&
                    !networkManager.ShutdownInProgress)
                {
                    if (networkManager.IsServer)
                    {
                        networkObject.Despawn(true);
                        destroyedByNetwork = true;
                    }
                    else
                    {
                        // The server owns destruction while the session is still
                        // live. Disabling AutoParent immediately prevents it from
                        // touching a destroyed elevator during that short window.
                        waitingForServerDespawn = true;
                    }
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"SHIP CARD DISPLAY NETWORK CLEANUP FAILED | Falling back to local destroy | " +
                    exception.Message);
            }

            if (!waitingForServerDespawn && !destroyedByNetwork && instance != null)
                UnityEngine.Object.Destroy(instance);

            cleanedInstances++;
        }

        if (cleanedInstances > 0)
        {
            Plugin.Log.LogInfo(
                $"SHIP CARD DISPLAY CLEANUP | RuntimeInstances={cleanedInstances} | " +
                "AutoParent disabled before round teardown");
        }

        runtimeInstances.Clear();
        publishedPlacements.Clear();
        spawnFailureLogged = false;
    }

    private static bool TryCreateFurniturePrefab(
        GameObject visualPrefab,
        out GameObject? createdPrefab)
    {
        createdPrefab = null;

        int placeableLayer = LayerMask.NameToLayer("PlaceableShipObjects");
        if (placeableLayer < 0)
        {
            Plugin.Log.LogError(
                "SHIP CARD DISPLAY REGISTRATION FAILED | Unity layer PlaceableShipObjects was not found.");
            return false;
        }

        createdPrefab = LethalLib.Modules.NetworkPrefabs.CreateNetworkPrefab(NetworkPrefabName);
        createdPrefab.name = NetworkPrefabName;

        prefabAutoParent = createdPrefab.AddComponent<AutoParentToShip>();
        prefabAutoParent.overrideOffset = true;
        prefabAutoParent.positionOffset = ShipCardDisplayConfig.LocalPosition;
        prefabAutoParent.rotationOffset = ShipCardDisplayConfig.LocalEulerAngles;
        prefabAutoParent.startingPosition = ShipCardDisplayConfig.LocalPosition;
        prefabAutoParent.startingRotation = ShipCardDisplayConfig.LocalEulerAngles;

        GameObject visual = UnityEngine.Object.Instantiate(
            visualPrefab,
            createdPrefab.transform,
            false);
        visual.name = VisualPrefabName;
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        if (!TryGetMeshBounds(visual, out Bounds visualBounds, out MeshFilter? mainMesh))
        {
            Plugin.Log.LogError(
                $"SHIP CARD DISPLAY REGISTRATION FAILED | Prefab={VisualPrefabName} has no usable MeshFilter/sharedMesh.");
            return false;
        }

        Transform? explicitPreviewTransform = FindChild(visual.transform, PlacementPreviewName);
        MeshFilter? explicitPreview = explicitPreviewTransform?.GetComponent<MeshFilter>();
        string previewSource;
        if (explicitPreview != null && explicitPreview.sharedMesh != null)
        {
            mainMesh = explicitPreview;
            previewSource = "Authored";
        }
        else
        {
            MeshFilter largestMeshFallback = mainMesh!;
            MeshFilter? generatedPreview = CreateBoundsPreview(visual.transform, visualBounds);
            if (generatedPreview != null)
            {
                mainMesh = generatedPreview;
                previewSource = "RuntimeBoundsProxy";
            }
            else
            {
                mainMesh = largestMeshFallback;
                previewSource = "LargestMeshFallback";
                Plugin.Log.LogWarning(
                    $"SHIP CARD DISPLAY PLACEMENT PREVIEW | Could not create the bounds preview; " +
                    $"using largest mesh {mainMesh!.name}.");
            }
        }

        GameObject placementObject = new(PlacementColliderName);
        placementObject.transform.SetParent(visual.transform, false);
        placementObject.transform.localPosition = visualBounds.center;
        placementObject.transform.localRotation = Quaternion.identity;
        placementObject.layer = placeableLayer;
        placementObject.tag = "PlaceableObject";

        BoxCollider placementCollider = placementObject.AddComponent<BoxCollider>();
        placementCollider.center = Vector3.zero;
        placementCollider.size = visualBounds.size;

        AudioSource audioSource = placementObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;

        prefabPlaceable = placementObject.AddComponent<PlaceableShipObject>();
        prefabPlaceable.parentObject = prefabAutoParent;
        prefabPlaceable.parentObjectSecondary = null;
        prefabPlaceable.mainMesh = mainMesh;
        prefabPlaceable.mainTransform = visual.transform;
        prefabPlaceable.placeObjectCollider = placementCollider;
        prefabPlaceable.yOffset = visualBounds.extents.y;
        prefabPlaceable.AllowPlacementOnWalls = true;
        prefabPlaceable.AllowPlacementOnCounters = false;
        // Vanilla's automatic offset averages half the X and Z dimensions. For
        // this wide, thin cabinet that puts it over half a unit off the wall.
        // Its local Z axis is the authored front/back axis, so depth is the
        // correct wall-normal offset.
        prefabPlaceable.overrideWallOffset = true;
        prefabPlaceable.wallOffset = visualBounds.extents.z + 0.01f;
        prefabPlaceable.doCollisionPointCheck = false;

        CreateInteractionTargets(visual, visualBounds);
        CreateAggregateScanNode(visual, visualBounds);
        createdPrefab.AddComponent<ShipCardDisplayController>();

        LogColliderDiagnostics(visual, placementCollider, placeableLayer);

        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY PLACEMENT READY | BoundsCenter={visualBounds.center} | " +
            $"BoundsSize={visualBounds.size} | YOffset={prefabPlaceable.yOffset:F2} | " +
            $"WallOffset={prefabPlaceable.wallOffset:F3} | Preview={previewSource}:{mainMesh!.name} | " +
            $"PreviewLocalPosition={mainMesh.transform.localPosition} | " +
            $"PreviewLocalEuler={mainMesh.transform.localEulerAngles} | " +
            $"PreviewLocalScale={mainMesh.transform.localScale} | " +
            $"PlacementAxes=WidthX/HeightY/DepthZ | PreviewAlignment=VisualRoot | " +
            $"Walls={prefabPlaceable.AllowPlacementOnWalls}");
        return true;
    }

    private static void CreateInteractionTargets(GameObject visual, Bounds visualBounds)
    {
        GameObject interactionObject = new(InteractionColliderName);
        interactionObject.transform.SetParent(visual.transform, false);
        interactionObject.transform.localPosition = visualBounds.center;
        interactionObject.transform.localRotation = Quaternion.identity;
        BoxCollider interactionCollider = interactionObject.AddComponent<BoxCollider>();
        interactionCollider.center = Vector3.zero;
        interactionCollider.size = new Vector3(
            visualBounds.size.x,
            visualBounds.size.y,
            Mathf.Max(visualBounds.size.z, 0.08f));
        interactionCollider.isTrigger = true;

        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY INTERACTION READY | Selection=LocalSpaceSlotAnchors | " +
            $"BroadTarget={interactionCollider.size}");
    }

    private static void CreateAggregateScanNode(GameObject visual, Bounds visualBounds)
    {
        int scanNodeLayer = LayerMask.NameToLayer("ScanNode");
        if (scanNodeLayer < 0)
        {
            Plugin.Log.LogError(
                "SHIP CARD DISPLAY SCAN DISABLED | Unity layer ScanNode was not found");
            return;
        }

        GameObject scanObject = new(AggregateScanNodeName);
        scanObject.transform.SetParent(visual.transform, false);
        scanObject.transform.localPosition = visualBounds.center;
        scanObject.transform.localRotation = Quaternion.identity;
        scanObject.layer = scanNodeLayer;

        BoxCollider scanCollider = scanObject.AddComponent<BoxCollider>();
        scanCollider.center = Vector3.zero;
        scanCollider.size = new Vector3(
            visualBounds.size.x,
            visualBounds.size.y,
            Mathf.Max(visualBounds.size.z, 0.08f));
        scanCollider.isTrigger = true;

        ScanNodeProperties scanNode = scanObject.AddComponent<ScanNodeProperties>();
        scanNode.headerText = "Card Display Case";
        scanNode.subText = $"0/{ShipCardDisplaySaveManager.SlotCount} cards | Value: $0";
        scanNode.scrapValue = 0;
        scanNode.minRange = 0;
        scanNode.maxRange = 20;
        scanNode.requiresLineOfSight = true;
        scanNode.nodeType = 2;

        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY SCAN READY | NodeType=Scrap | Bounds={scanCollider.size}");
    }

    private static MeshFilter? CreateBoundsPreview(Transform visual, Bounds visualBounds)
    {
        try
        {
            GameObject preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
            preview.name = PlacementPreviewName;
            preview.transform.SetParent(visual, false);
            preview.transform.localPosition = visualBounds.center;
            preview.transform.localRotation = Quaternion.identity;
            preview.transform.localScale = visualBounds.size;

            Collider? primitiveCollider = preview.GetComponent<Collider>();
            if (primitiveCollider != null)
                UnityEngine.Object.DestroyImmediate(primitiveCollider);

            MeshRenderer? renderer = preview.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;

            return preview.GetComponent<MeshFilter>();
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"SHIP CARD DISPLAY PLACEMENT PREVIEW FAILED | {exception.Message}");
            return null;
        }
    }

    private static void LogColliderDiagnostics(
        GameObject visual,
        Collider placementCollider,
        int placeableLayer)
    {
        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        int decorativeCount = 0;
        List<string> suspicious = new();

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider == placementCollider)
                continue;

            decorativeCount++;
            if (collider.gameObject.layer == placeableLayer ||
                collider.gameObject.CompareTag("PlaceableObject"))
            {
                suspicious.Add(
                    $"{GetTransformPath(collider.transform, visual.transform)} " +
                    $"(Layer={LayerMask.LayerToName(collider.gameObject.layer)}, " +
                    $"Tag={collider.gameObject.tag}, Trigger={collider.isTrigger})");
            }
        }

        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY COLLIDERS | Decorative={decorativeCount} | " +
            "Vanilla temporarily disables child colliders while this furniture is being moved");

        if (suspicious.Count > 0)
        {
            Plugin.Log.LogWarning(
                "SHIP CARD DISPLAY COLLIDER CONFIG | Additional colliders can intercept furniture selection: " +
                string.Join("; ", suspicious));
        }
    }

    private static string GetTransformPath(Transform transform, Transform root)
    {
        string path = transform.name;
        while (transform.parent != null && transform.parent != root)
        {
            transform = transform.parent;
            path = $"{transform.name}/{path}";
        }

        return path;
    }

    private static void SetUnlockableId(int unlockableId)
    {
        if (prefabPlaceable != null)
            prefabPlaceable.unlockableID = unlockableId;
        if (prefabAutoParent != null)
            prefabAutoParent.unlockableID = unlockableId;
    }

    private static bool TryFindSpawnedInstance(
        StartOfRound round,
        int unlockableId,
        out GameObject? instance)
    {
        instance = null;

        if (round.SpawnedShipUnlockables != null &&
            round.SpawnedShipUnlockables.TryGetValue(unlockableId, out GameObject spawned))
        {
            NetworkObject? spawnedNetworkObject =
                spawned == null ? null : spawned.GetComponent<NetworkObject>();
            if (spawnedNetworkObject != null && spawnedNetworkObject.IsSpawned)
            {
                instance = spawned;
                runtimeInstances.Add(spawned!);
                return true;
            }

            // SpawnUnlockable adds by key rather than assigning, so discard a
            // stale entry before asking vanilla to recreate the furniture.
            round.SpawnedShipUnlockables.Remove(unlockableId);
        }

        foreach (PlaceableShipObject candidate in
                 UnityEngine.Object.FindObjectsOfType<PlaceableShipObject>())
        {
            if (candidate == null || candidate.unlockableID != unlockableId ||
                candidate.parentObject == null)
            {
                continue;
            }

            GameObject root = candidate.parentObject.gameObject;
            NetworkObject? networkObject = root.GetComponent<NetworkObject>();
            if (networkObject == null || !networkObject.IsSpawned)
                continue;

            instance = root;
            runtimeInstances.Add(root);
            if (round.SpawnedShipUnlockables != null)
                round.SpawnedShipUnlockables[unlockableId] = root;
            return true;
        }

        return false;
    }

    private static void ConfigureSpawnedInstance(GameObject instance, int unlockableId)
    {
        AutoParentToShip? autoParent = instance.GetComponent<AutoParentToShip>();
        PlaceableShipObject? placeable = instance.GetComponentInChildren<PlaceableShipObject>();
        if (autoParent == null || placeable == null)
            return;

        autoParent.unlockableID = unlockableId;
        placeable.unlockableID = unlockableId;

        if (placeable.placeObjectSFX != null)
            return;

        foreach (PlaceableShipObject source in
                 UnityEngine.Object.FindObjectsOfType<PlaceableShipObject>())
        {
            if (source == null || source == placeable || source.placeObjectSFX == null)
                continue;

            placeable.placeObjectSFX = source.placeObjectSFX;
            break;
        }
    }

    private static void TryPublishAuthoritativePlacement(GameObject instance)
    {
        NetworkManager? networkManager = NetworkManager.Singleton;
        if (displayCaseUnlockable == null || networkManager == null || !networkManager.IsServer ||
            ShipBuildModeManager.Instance == null)
        {
            return;
        }

        PlaceableShipObject? placeable = instance.GetComponentInChildren<PlaceableShipObject>();
        NetworkObject? networkObject = instance.GetComponent<NetworkObject>();
        if (placeable == null || placeable.placeObjectCollider == null ||
            placeable.mainMesh == null || networkObject == null || !networkObject.IsSpawned)
        {
            return;
        }

        if (!publishedPlacements.Add(networkObject.NetworkObjectId))
            return;

        bool usingInitialConfig =
            !displayCaseUnlockable.hasBeenMoved && !HasSavedPlacement();
        Vector3 placementPosition;
        Vector3 placementRotation;

        if (usingInitialConfig)
        {
            // AutoParentToShip has already applied the host's configured root offset.
            // Capture its placement proxy and seed vanilla's placement metadata.
            placementPosition = placeable.placeObjectCollider.transform.position;
            placementRotation = placeable.mainMesh.transform.eulerAngles;
            ShipBuildModeManager.Instance.PlaceShipObject(
                placementPosition,
                placementRotation,
                placeable,
                false);
        }
        else
        {
            // SpawnUnlockable has already applied the save's authoritative values on
            // the server. Republish them for clients that were connected during load.
            placementPosition = displayCaseUnlockable.placedPosition;
            placementRotation = displayCaseUnlockable.placedRotation;
        }

        PlayerControllerB? localPlayer = GameNetworkManager.Instance?.localPlayerController;
        int playerWhoMoved = localPlayer == null ? -1 : (int)localPlayer.playerClientId;
        if (networkManager.IsHost && localPlayer != null)
        {
            ShipBuildModeManager.Instance.PlaceShipObjectServerRpc(
                placementPosition,
                placementRotation,
                networkObject,
                playerWhoMoved);
        }
        else
        {
            ShipBuildModeManager.Instance.PlaceShipObjectClientRpc(
                placementPosition,
                placementRotation,
                networkObject,
                playerWhoMoved);
        }

        Plugin.Log.LogInfo(
            $"SHIP CARD DISPLAY PLACEMENT PUBLISHED | " +
            $"Source={(usingInitialConfig ? "HostConfig" : "VanillaSaveOrRuntime")} | " +
            $"WorldPosition={placementPosition} | WorldRotation={placementRotation} | " +
            $"PlacementShipLocal={FormatShipLocalPoint(placementPosition)} | " +
            $"RootShipLocal={FormatShipLocalPoint(instance.transform.position)}");
    }

    private static string FormatShipLocalPoint(Vector3 worldPosition)
    {
        Transform? shipTransform = StartOfRound.Instance?.elevatorTransform;
        return shipTransform == null
            ? "Unavailable"
            : shipTransform.InverseTransformPoint(worldPosition).ToString();
    }

    private static bool HasSavedPlacement()
    {
        GameNetworkManager? gameNetworkManager = GameNetworkManager.Instance;
        if (gameNetworkManager == null || string.IsNullOrWhiteSpace(gameNetworkManager.currentSaveFileName))
            return false;

        try
        {
            return ES3.KeyExists(
                $"ShipUnlockMoved_{UnlockableName}",
                gameNetworkManager.currentSaveFileName);
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                $"SHIP CARD DISPLAY SAVE CHECK FAILED | Using current runtime placement | {exception.Message}");
            return displayCaseUnlockable?.hasBeenMoved == true;
        }
    }

    private static bool TryGetMeshBounds(
        GameObject visual,
        out Bounds bounds,
        out MeshFilter? largestMesh)
    {
        bounds = default;
        largestMesh = null;
        bool foundBounds = false;
        float largestVolume = -1f;

        foreach (MeshFilter meshFilter in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh? mesh = meshFilter.sharedMesh;
            if (mesh == null)
                continue;

            Bounds meshBounds = mesh.bounds;
            Bounds transformedBounds = default;
            bool foundMeshCorner = false;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 meshCorner = meshBounds.center + Vector3.Scale(
                    meshBounds.extents,
                    new Vector3(x, y, z));
                Vector3 visualCorner = visual.transform.InverseTransformPoint(
                    meshFilter.transform.TransformPoint(meshCorner));

                if (!foundMeshCorner)
                {
                    transformedBounds = new Bounds(visualCorner, Vector3.zero);
                    foundMeshCorner = true;
                }
                else
                {
                    transformedBounds.Encapsulate(visualCorner);
                }

                if (!foundBounds)
                {
                    bounds = new Bounds(visualCorner, Vector3.zero);
                    foundBounds = true;
                }
                else
                {
                    bounds.Encapsulate(visualCorner);
                }
            }

            float volume = transformedBounds.size.x * transformedBounds.size.y * transformedBounds.size.z;
            if (volume > largestVolume)
            {
                largestVolume = volume;
                largestMesh = meshFilter;
            }
        }

        return foundBounds && largestMesh != null &&
            bounds.size.x > 0.01f && bounds.size.y > 0.01f && bounds.size.z > 0.01f;
    }

    private static GameObject? LoadPrefab(AssetBundle bundle, string expectedName)
    {
        foreach (string assetName in bundle.GetAllAssetNames())
        {
            string fileName = Path.GetFileNameWithoutExtension(assetName);
            if (!fileName.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                continue;

            GameObject? asset = bundle.LoadAsset<GameObject>(assetName);
            if (asset != null)
                return asset;
        }

        foreach (GameObject candidate in bundle.LoadAllAssets<GameObject>())
        {
            if (candidate.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private static void ValidateAnchors(GameObject prefab)
    {
        List<string> missingAnchors = new();
        string[] requiredParents = { "DisplaySlots", "Row01", "Row02", "Row03", "InteractionRoot" };

        foreach (string requiredParent in requiredParents)
        {
            if (FindChild(prefab.transform, requiredParent) == null)
                missingAnchors.Add(requiredParent);
        }

        for (int slot = 1; slot <= 33; slot++)
        {
            string slotName = $"Slot{slot:00}";
            if (FindChild(prefab.transform, slotName) == null)
                missingAnchors.Add(slotName);
        }

        if (missingAnchors.Count == 0)
        {
            Plugin.Log.LogInfo(
                "SHIP CARD DISPLAY PREFAB VALIDATED | Display rows, InteractionRoot, and 33 slot anchors found");
            return;
        }

        Plugin.Log.LogWarning(
            $"SHIP CARD DISPLAY PREFAB INCOMPLETE | Missing={string.Join(", ", missingAnchors)} | " +
            "Furniture registration will continue, but future display interactions may not work.");
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

    private static void LogSpawnFailure(Exception exception)
    {
        if (spawnFailureLogged)
            return;

        spawnFailureLogged = true;
        Plugin.Log.LogError($"SHIP CARD DISPLAY SPAWN FAILED | {exception}");
    }
}
