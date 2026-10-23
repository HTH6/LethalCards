// Copy this file into the Unity project's Assets/Editor folder.
// It is intentionally excluded from the runtime LethalCards mod assembly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class LethalCardsPrefabColliderFixer
{
    private const string MenuRoot = "Tools/Lethal Cards/";
    private const string CardPrefabFolder = "Assets/LethalCards/Prefabs/Cards";
    private const int ExpectedCardCount = 33;
    private const float ZeroAxisEpsilon = 0.0000001f;
    private const float BoundsTolerance = 0.01f;

    [MenuItem(MenuRoot + "Validate Card Prefab Colliders", false, 200)]
    private static void ValidateCardPrefabColliders()
    {
        List<string> paths = DiscoverCardPrefabs();
        Debug.Log(string.Format(
            "LETHAL CARDS COLLIDER VALIDATION START | Discovered={0} | Expected={1} | Root={2} | QueriesHitTriggers={3}",
            paths.Count,
            ExpectedCardCount,
            CardPrefabFolder,
            Physics.queriesHitTriggers));

        if (!Physics.queriesHitTriggers)
        {
            Debug.LogError(
                "LETHAL CARDS COLLIDER VALIDATION | Project Physics.queriesHitTriggers is false. " +
                "The installed game's scanner uses Unity's global trigger-query policy, so do not run the fixer until this project setting matches the game.");
        }

        if (paths.Count != ExpectedCardCount)
        {
            Debug.LogError(BuildDiscoveryFailure(paths));
            EditorUtility.DisplayDialog(
                "Lethal Cards collider validation",
                string.Format(
                    "Expected {0} physical card prefabs, but discovered {1}. No assets were changed. Check the Console for paths and details.",
                    ExpectedCardCount,
                    paths.Count),
                "OK");
            return;
        }

        int ready = 0;
        int needsChanges = 0;
        int unusual = 0;
        foreach (string path in paths)
        {
            PrefabInspection inspection = InspectPrefab(path);
            LogInspection(inspection);
            if (!inspection.SafeToModify)
                unusual++;
            else if (inspection.NeedsChanges)
                needsChanges++;
            else
                ready++;
        }

        string summary = string.Format(
            "LETHAL CARDS COLLIDER VALIDATION COMPLETE | Total={0} | AlreadyCorrect={1} | NeedsChanges={2} | UnusualOrUnsafe={3} | AssetsChanged=0",
            paths.Count,
            ready,
            needsChanges,
            unusual);
        if (unusual == 0)
            Debug.Log(summary);
        else
            Debug.LogWarning(summary);

        EditorUtility.DisplayDialog(
            "Lethal Cards collider validation",
            string.Format(
                "Inspected {0} card prefabs.\n\nAlready correct: {1}\nNeed expected changes: {2}\nUnusual/unsafe: {3}\n\nNo assets were changed. See the Console for the per-prefab report.",
                paths.Count,
                ready,
                needsChanges,
                unusual),
            "OK");
    }

    [MenuItem(MenuRoot + "Fix Card Prefab Colliders", false, 201)]
    private static void FixCardPrefabColliders()
    {
        List<string> paths = DiscoverCardPrefabs();
        if (!Physics.queriesHitTriggers)
        {
            Debug.LogError(
                "LETHAL CARDS COLLIDER FIX ABORTED | Physics.queriesHitTriggers=False | AssetsChanged=0");
            EditorUtility.DisplayDialog(
                "Lethal Cards collider fix aborted",
                "Physics queries do not currently hit trigger colliders. No assets were changed. Verify the project's Physics settings before converting ScanNode colliders to triggers.",
                "OK");
            return;
        }

        if (paths.Count != ExpectedCardCount)
        {
            Debug.LogError(BuildDiscoveryFailure(paths));
            EditorUtility.DisplayDialog(
                "Lethal Cards collider fix aborted",
                string.Format(
                    "Expected {0} physical card prefabs, but discovered {1}. No assets were changed. Run validation and inspect the Console.",
                    ExpectedCardCount,
                    paths.Count),
                "OK");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Fix Lethal Cards prefab colliders");
        List<string> modified = new List<string>();
        List<string> alreadyCorrect = new List<string>();
        List<string> skipped = new List<string>();

        try
        {
            for (int index = 0; index < paths.Count; index++)
            {
                string path = paths[index];
                EditorUtility.DisplayProgressBar(
                    "Fixing Lethal Cards card colliders",
                    path,
                    (float)index / paths.Count);

                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    PrefabInspection inspection = InspectLoadedPrefab(root, path);
                    if (!inspection.SafeToModify)
                    {
                        skipped.Add(path);
                        LogInspection(inspection);
                        continue;
                    }

                    if (!inspection.NeedsChanges)
                    {
                        alreadyCorrect.Add(path);
                        Debug.Log("LETHAL CARDS COLLIDER FIX | AlreadyCorrect | " + path);
                        continue;
                    }

                    Undo.RegisterFullObjectHierarchyUndo(
                        root,
                        "Fix card prefab colliders: " + root.name);
                    List<string> changes = new List<string>();

                    foreach (MeshCollider collider in inspection.FrontMeshColliders)
                    {
                        changes.Add("Removed CardFront MeshCollider");
                        Undo.DestroyObjectImmediate(collider);
                    }

                    foreach (MeshCollider collider in inspection.BackMeshColliders)
                    {
                        changes.Add("Removed CardBack MeshCollider");
                        Undo.DestroyObjectImmediate(collider);
                    }

                    if (!inspection.ScanNodeCollider.isTrigger)
                    {
                        Undo.RecordObject(
                            inspection.ScanNodeCollider,
                            "Set card ScanNode collider to trigger");
                        inspection.ScanNodeCollider.isTrigger = true;
                        EditorUtility.SetDirty(inspection.ScanNodeCollider);
                        changes.Add("Set ScanNode BoxCollider isTrigger=True");
                    }

                    EditorUtility.SetDirty(root);
                    bool saveSucceeded;
                    PrefabUtility.SaveAsPrefabAsset(root, path, out saveSucceeded);
                    if (!saveSucceeded)
                    {
                        skipped.Add(path + " (save failed)");
                        Debug.LogError("LETHAL CARDS COLLIDER FIX SAVE FAILED | " + path);
                        continue;
                    }

                    PrefabInspection after = InspectLoadedPrefab(root, path);
                    if (!after.SafeToModify || after.FrontMeshColliders.Length != 0 ||
                        after.BackMeshColliders.Length != 0 || !after.ScanNodeCollider.isTrigger)
                    {
                        skipped.Add(path + " (post-save validation failed)");
                        Debug.LogError(
                            "LETHAL CARDS COLLIDER FIX POST-SAVE VALIDATION FAILED | " + path);
                        LogInspection(after);
                        continue;
                    }

                    modified.Add(path);
                    Debug.Log(string.Format(
                        "LETHAL CARDS COLLIDER FIX MODIFIED | Prefab={0} | Changes={1}",
                        path,
                        string.Join(", ", changes.ToArray())));
                }
                catch (Exception exception)
                {
                    skipped.Add(path + " (exception)");
                    Debug.LogError(string.Format(
                        "LETHAL CARDS COLLIDER FIX FAILED | Prefab={0} | {1}",
                        path,
                        exception));
                }
                finally
                {
                    if (root != null)
                        PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            Undo.CollapseUndoOperations(undoGroup);
        }

        string summary = string.Format(
            "LETHAL CARDS COLLIDER FIX COMPLETE | Discovered={0} | Modified={1} | AlreadyCorrect={2} | Skipped={3}",
            paths.Count,
            modified.Count,
            alreadyCorrect.Count,
            skipped.Count);
        if (skipped.Count == 0)
            Debug.Log(summary);
        else
            Debug.LogWarning(summary + " | SkippedPaths=" + string.Join("; ", skipped.ToArray()));

        EditorUtility.DisplayDialog(
            "Lethal Cards collider fix",
            string.Format(
                "Discovered: {0}\nModified: {1}\nAlready correct: {2}\nSkipped: {3}\n\nRun Validate Card Prefab Colliders again and inspect the Console before rebuilding the AssetBundle.",
                paths.Count,
                modified.Count,
                alreadyCorrect.Count,
                skipped.Count),
            "OK");
    }

    private static List<string> DiscoverCardPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { CardPrefabFolder });
        List<string> paths = new List<string>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (path.IndexOf("/Test Prefabs/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("/CardBinder/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null ||
                !root.name.EndsWith("Card", StringComparison.OrdinalIgnoreCase) ||
                !HasComponentNamed(root, "PhysicsProp"))
            {
                continue;
            }

            paths.Add(path);
        }

        paths.Sort(StringComparer.OrdinalIgnoreCase);
        return paths;
    }

    private static PrefabInspection InspectPrefab(string path)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(path);
            return InspectLoadedPrefab(root, path);
        }
        catch (Exception exception)
        {
            PrefabInspection failed = new PrefabInspection(path, "<load failed>");
            failed.Issues.Add("Prefab could not be loaded: " + exception.Message);
            return failed;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static PrefabInspection InspectLoadedPrefab(GameObject root, string path)
    {
        PrefabInspection result = new PrefabInspection(path, root.name);
        result.RootScale = root.transform.localScale;

        if (!HasComponentNamed(root, "PhysicsProp"))
            result.Issues.Add("Root PhysicsProp is missing");
        if (!HasComponentNamed(root, "NetworkObject"))
            result.Issues.Add("Root NetworkObject is missing");
        if (!IsUsableScale(result.RootScale))
            result.Issues.Add("Root local scale is non-finite or has a truly zero axis");

        int missingScripts = 0;
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        result.MissingScriptCount = missingScripts;
        if (missingScripts != 0)
            result.Issues.Add("Prefab contains missing script references: " + missingScripts);

        BoxCollider[] rootBoxes = root.GetComponents<BoxCollider>();
        if (rootBoxes.Length != 1)
        {
            result.Issues.Add("Expected exactly one root BoxCollider; found " + rootBoxes.Length);
        }
        else
        {
            result.RootCollider = rootBoxes[0];
            if (result.RootCollider.isTrigger)
                result.Issues.Add("Root BoxCollider is unexpectedly a trigger");
            if (!result.RootCollider.enabled)
                result.Issues.Add("Root BoxCollider is disabled");
            if (!IsUsableScale(result.RootCollider.size))
                result.Issues.Add("Root BoxCollider has a non-finite or zero size axis");
        }

        Transform[] fronts = FindNamedTransforms(root, "CardFront");
        Transform[] backs = FindNamedTransforms(root, "CardBack");
        Transform[] scanNodes = FindNamedTransforms(root, "ScanNode");
        if (fronts.Length != 1)
            result.Issues.Add("Expected exactly one CardFront transform; found " + fronts.Length);
        if (backs.Length != 1)
            result.Issues.Add("Expected exactly one CardBack transform; found " + backs.Length);
        if (scanNodes.Length != 1)
            result.Issues.Add("Expected exactly one ScanNode transform; found " + scanNodes.Length);

        if (fronts.Length == 1)
        {
            result.FrontMeshColliders = fronts[0].GetComponents<MeshCollider>();
            if (result.FrontMeshColliders.Length > 1)
                result.Issues.Add("CardFront has more than one MeshCollider");
            if (fronts[0].GetComponentsInChildren<Renderer>(true).Length == 0)
                result.Issues.Add("CardFront visual Renderer is missing");
        }

        if (backs.Length == 1)
        {
            result.BackMeshColliders = backs[0].GetComponents<MeshCollider>();
            if (result.BackMeshColliders.Length > 1)
                result.Issues.Add("CardBack has more than one MeshCollider");
            if (backs[0].GetComponentsInChildren<Renderer>(true).Length == 0)
                result.Issues.Add("CardBack visual Renderer is missing");
        }

        if (scanNodes.Length == 1)
        {
            BoxCollider[] scanBoxes = scanNodes[0].GetComponents<BoxCollider>();
            if (scanBoxes.Length != 1)
                result.Issues.Add("Expected exactly one ScanNode BoxCollider; found " + scanBoxes.Length);
            else
                result.ScanNodeCollider = scanBoxes[0];
            if (!HasComponentNamed(scanNodes[0].gameObject, "ScanNodeProperties"))
                result.Issues.Add("ScanNodeProperties is missing");
        }

        MeshCollider[] allMeshColliders = root.GetComponentsInChildren<MeshCollider>(true);
        HashSet<MeshCollider> expectedMeshColliders = new HashSet<MeshCollider>(
            result.FrontMeshColliders.Concat(result.BackMeshColliders));
        MeshCollider[] unexpectedMeshColliders = allMeshColliders
            .Where(collider => !expectedMeshColliders.Contains(collider))
            .ToArray();
        if (unexpectedMeshColliders.Length != 0)
            result.Issues.Add("Unexpected MeshColliders outside CardFront/CardBack: " + unexpectedMeshColliders.Length);

        Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        if (rigidbodies.Length != 0)
            result.Issues.Add("Unexpected Rigidbody components: " + rigidbodies.Length);

        if (result.RootCollider != null && fronts.Length == 1 && backs.Length == 1)
        {
            Bounds visualBounds;
            if (!TryGetVisualBounds(fronts[0], backs[0], out visualBounds))
            {
                result.Issues.Add("Could not calculate CardFront/CardBack renderer bounds");
            }
            else
            {
                result.VisualBounds = visualBounds;
                result.RootColliderCoversVisuals = BoundsContains(
                    result.RootCollider.bounds,
                    visualBounds,
                    BoundsTolerance);
                if (!result.RootColliderCoversVisuals)
                    result.Issues.Add("Root BoxCollider does not cover CardFront/CardBack renderer bounds");
            }
        }

        Component physicsProp = FindComponentNamed(root, "PhysicsProp");
        if (physicsProp != null)
        {
            SerializedObject serialized = new SerializedObject(physicsProp);
            SerializedProperty colliders = serialized.FindProperty("propColliders");
            SerializedProperty originalScale = serialized.FindProperty("originalScale");
            result.SerializedColliderStatus = colliders == null
                ? "propColliders field not found"
                : "propColliders serialized size=" + colliders.arraySize;
            result.SerializedOriginalScaleStatus = originalScale == null
                ? "originalScale field not found"
                : "originalScale=" + originalScale.vector3Value;
        }

        result.SafeToModify = result.Issues.Count == 0 &&
                              result.RootCollider != null &&
                              result.ScanNodeCollider != null;
        result.NeedsChanges = result.SafeToModify &&
                              (result.FrontMeshColliders.Length != 0 ||
                               result.BackMeshColliders.Length != 0 ||
                               !result.ScanNodeCollider.isTrigger);
        return result;
    }

    private static void LogInspection(PrefabInspection inspection)
    {
        string rootCollider = inspection.RootCollider == null
            ? "Missing"
            : string.Format(
                "Present(enabled={0},trigger={1},center={2},size={3})",
                inspection.RootCollider.enabled,
                inspection.RootCollider.isTrigger,
                inspection.RootCollider.center,
                inspection.RootCollider.size);
        string scanCollider = inspection.ScanNodeCollider == null
            ? "Missing"
            : string.Format(
                "Present(enabled={0},trigger={1},center={2},size={3})",
                inspection.ScanNodeCollider.enabled,
                inspection.ScanNodeCollider.isTrigger,
                inspection.ScanNodeCollider.center,
                inspection.ScanNodeCollider.size);
        string expectedChanges = inspection.SafeToModify
            ? BuildExpectedChanges(inspection)
            : "Skipped because the structure is unexpected";
        string report = string.Format(
            "LETHAL CARDS COLLIDER VALIDATION | Prefab={0} | Path={1} | RootScale={2} | RootBox={3} | " +
            "CardFrontMeshColliders={4} | CardBackMeshColliders={5} | ScanNodeBox={6} | " +
            "RootCoversVisuals={7} | MissingScripts={8} | Serialized={9}; {10} | " +
            "SafeToModify={11} | ExpectedChanges={12} | Issues={13}",
            inspection.Name,
            inspection.Path,
            inspection.RootScale,
            rootCollider,
            inspection.FrontMeshColliders.Length,
            inspection.BackMeshColliders.Length,
            scanCollider,
            inspection.RootColliderCoversVisuals,
            inspection.MissingScriptCount,
            inspection.SerializedColliderStatus,
            inspection.SerializedOriginalScaleStatus,
            inspection.SafeToModify,
            expectedChanges,
            inspection.Issues.Count == 0 ? "None" : string.Join("; ", inspection.Issues.ToArray()));

        if (inspection.SafeToModify)
            Debug.Log(report);
        else
            Debug.LogWarning(report);
    }

    private static string BuildExpectedChanges(PrefabInspection inspection)
    {
        List<string> changes = new List<string>();
        if (inspection.FrontMeshColliders.Length != 0)
            changes.Add("remove CardFront MeshCollider");
        if (inspection.BackMeshColliders.Length != 0)
            changes.Add("remove CardBack MeshCollider");
        if (inspection.ScanNodeCollider != null && !inspection.ScanNodeCollider.isTrigger)
            changes.Add("set ScanNode BoxCollider isTrigger=True");
        return changes.Count == 0 ? "None" : string.Join(", ", changes.ToArray());
    }

    private static string BuildDiscoveryFailure(List<string> paths)
    {
        StringBuilder message = new StringBuilder();
        message.AppendFormat(
            "LETHAL CARDS COLLIDER OPERATION ABORTED | Expected={0} | Discovered={1} | Root={2}",
            ExpectedCardCount,
            paths.Count,
            CardPrefabFolder);
        foreach (string path in paths)
            message.Append("\n - ").Append(path);
        return message.ToString();
    }

    private static bool TryGetVisualBounds(
        Transform front,
        Transform back,
        out Bounds bounds)
    {
        Renderer[] renderers = front.GetComponentsInChildren<Renderer>(true)
            .Concat(back.GetComponentsInChildren<Renderer>(true))
            .ToArray();
        if (renderers.Length == 0)
        {
            bounds = new Bounds();
            return false;
        }

        bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return true;
    }

    private static bool BoundsContains(Bounds outer, Bounds inner, float tolerance)
    {
        outer.Expand(tolerance * 2f);
        return outer.Contains(inner.min) && outer.Contains(inner.max);
    }

    private static Transform[] FindNamedTransforms(GameObject root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .Where(transform => transform.name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static bool HasComponentNamed(GameObject gameObject, string typeName)
    {
        return FindComponentNamed(gameObject, typeName) != null;
    }

    private static Component FindComponentNamed(GameObject gameObject, string typeName)
    {
        foreach (Component component in gameObject.GetComponents<Component>())
        {
            if (component != null && component.GetType().Name == typeName)
                return component;
        }

        return null;
    }

    private static bool IsUsableScale(Vector3 value)
    {
        return IsUsableAxis(value.x) &&
               IsUsableAxis(value.y) &&
               IsUsableAxis(value.z);
    }

    private static bool IsUsableAxis(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) &&
               Math.Abs(value) > ZeroAxisEpsilon;
    }

    private sealed class PrefabInspection
    {
        internal PrefabInspection(string path, string name)
        {
            Path = path;
            Name = name;
        }

        internal string Path;
        internal string Name;
        internal Vector3 RootScale;
        internal BoxCollider RootCollider;
        internal BoxCollider ScanNodeCollider;
        internal MeshCollider[] FrontMeshColliders = new MeshCollider[0];
        internal MeshCollider[] BackMeshColliders = new MeshCollider[0];
        internal Bounds VisualBounds;
        internal bool RootColliderCoversVisuals;
        internal int MissingScriptCount;
        internal string SerializedColliderStatus = "Unavailable";
        internal string SerializedOriginalScaleStatus = "Unavailable";
        internal bool SafeToModify;
        internal bool NeedsChanges;
        internal readonly List<string> Issues = new List<string>();
    }
}
