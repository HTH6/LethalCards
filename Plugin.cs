using System.IO;
using BepInEx;
using BepInEx.Logging;
using LethalLib.Modules;
using UnityEngine;
using LethalCards.Cards;
using LethalCards.Boosters;
using GameNetcodeStuff;
using Unity.Netcode;
using HarmonyLib;
using System.Reflection;

namespace LethalCards;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("evaisa.lethallib")]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "HunterHaaf.LethalCards";
    public const string PluginName = "Lethal Cards";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Log = null!;

    // ============================================================
    // DEBUG ONLY
    // ============================================================

    internal static Item? LightBoosterItem;
    internal static Item? HeavyBoosterItem;

    private Harmony? harmony;

    // ============================================================

    private void Awake()
    {
        Log = Logger;

        Log.LogInfo("=================================");
        Log.LogInfo("LETHAL CARDS LOADED");
        Log.LogInfo($"Version {PluginVersion}");
        Log.LogInfo("=================================");

        string pluginDirectory =
            Path.GetDirectoryName(Info.Location)!;

        string bundlePath =
            Path.Combine(
                pluginDirectory,
                "lethalcards"
            );

        Log.LogInfo(
            $"Loading asset bundle from: {bundlePath}"
        );

        AssetBundle bundle =
            AssetBundle.LoadFromFile(bundlePath);

        if (bundle == null)
        {
            Log.LogError(
                "FAILED TO LOAD lethalcards ASSET BUNDLE"
            );

            return;
        }

        Log.LogInfo(
            "Asset bundle loaded successfully."
        );

        // ========================================================
        // CARD REGISTRATION
        // ========================================================

        RegisterCard(
            bundle,
            "HoardingBugCardItem",
            "LC01-001",
            "LC01",
            "Hoarding Bug",
            CardRarity.Common,
            6,
            1
        );

        RegisterCard(
            bundle,
            "EyelessDogCardItem",
            "LC01-002",
            "LC01",
            "Eyeless Dog",
            CardRarity.Common,
            7,
            1
        );

        RegisterCard(
            bundle,
            "SnareFleaCardItem",
            "LC01-006",
            "LC01",
            "Snare Flea",
            CardRarity.Uncommon,
            10,
            1
        );

        RegisterCard(
            bundle,
            "BrackenCardItem",
            "LC01-003",
            "LC01",
            "Bracken",
            CardRarity.Rare,
            32,
            1
        );

        RegisterCard(
            bundle,
            "CoilHeadCardItem",
            "LC01-004",
            "LC01",
            "Coil-Head",
            CardRarity.Rare,
            36,
            1
        );

        RegisterCard(
            bundle,
            "JesterCardItem",
            "LC01-005",
            "LC01",
            "Jester",
            CardRarity.UltraRare,
            75,
            1
        );

        RegisterCard(
            bundle,
            "GhostGirlCardItem",
            "LC01-007",
            "LC01",
            "Ghost Girl",
            CardRarity.SecretRare,
            140,
            1
        );

        // ========================================================
        // BOOSTER REGISTRATION
        // ========================================================

        RegisterBooster(
            bundle,
            "LightBoosterPackItem",
            BoosterType.Light,
            9999
        );

        RegisterBooster(
            bundle,
            "HeavyBoosterPackItem",
            BoosterType.Heavy,
            9999
        );

        Log.LogInfo(
            $"Finished registering " +
            $"{CardDatabase.Cards.Count} cards."
        );

        //LogItemSaveMethods();
        harmony =
            new Harmony(PluginGuid);

        harmony.PatchAll();

        Log.LogInfo(
            "Harmony debug patches applied."
        );

    }


    // ============================================================
    // CARD REGISTRATION
    // ============================================================

    private void PrepareCardPrefab(
        GameObject prefab)
    {
        if (prefab == null)
        {
            Log.LogError(
                "CARD PREFAB PREP FAILED | Prefab was null."
            );

            return;
        }

        NetworkObject networkObject =
            prefab.GetComponent<NetworkObject>();

        if (networkObject == null)
        {
            Log.LogError(
                $"CARD PREFAB PREP FAILED | " +
                $"Prefab={prefab.name} | " +
                $"Reason=NetworkObject missing"
            );

            return;
        }

        CardInstanceData instanceData =
            prefab.GetComponent<CardInstanceData>();

        if (instanceData == null)
        {
            instanceData =
                prefab.AddComponent<CardInstanceData>();

            Log.LogInfo(
                $"CARD PREFAB PREPARED | " +
                $"Prefab={prefab.name} | " +
                $"Added CardInstanceData=True"
            );
        }
        else
        {
            Log.LogInfo(
                $"CARD PREFAB PREPARED | " +
                $"Prefab={prefab.name} | " +
                $"CardInstanceData already present"
            );
        }
    }
    private void RegisterCard(
        AssetBundle bundle,
        string assetName,
        string cardId,
        string setId,
        string displayName,
        CardRarity rarity,
        int baseScrapValue,
        int spawnWeight)
    {
        Item item =
            bundle.LoadAsset<Item>(assetName);

        if (item == null)
        {
            Log.LogError(
                $"Failed to load card item: {assetName}"
            );

            return;
        }

        if (item.spawnPrefab == null)
        {
            Log.LogError(
                $"{assetName} has no spawn prefab assigned."
            );

            return;
        }

        Log.LogInfo(
            $"Loaded card item: {assetName}"
        );

        Log.LogInfo(
            $"Prefab: {item.spawnPrefab.name}"
        );

        PhysicsProp physicsProp =
            item.spawnPrefab.GetComponent<PhysicsProp>();

        Log.LogInfo(
            $"PhysicsProp found: " +
            $"{physicsProp != null}"
        );

        Log.LogInfo(
            $"CARD SAVE CONFIG | " +
            $"Card={displayName} | " +
            $"SaveItemVariable={item.saveItemVariable}"
        );

        // Add our networked card metadata component BEFORE
        // LethalLib registers this prefab with Netcode.
        PrepareCardPrefab(
            item.spawnPrefab
        );

        LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(
            item.spawnPrefab
        );

        Items.RegisterScrap(
            item,
            spawnWeight,
            Levels.LevelTypes.All
        );

        CardDefinition card =
            new CardDefinition(
                cardId,
                setId,
                displayName,
                rarity,
                baseScrapValue,
                item
            );

        CardDatabase.Add(card);

        Log.LogInfo(
            $"Registered card: {displayName} | " +
            $"Rarity: {rarity} | " +
            $"Base Value: ${baseScrapValue} | " +
            $"Spawn Weight: {spawnWeight}"
        );
    }

    /* private void LogItemSaveMethods()
    {
        Log.LogInfo("===== GRABBABLEOBJECT METHODS =====");

        foreach (
            MethodInfo method in
            typeof(GrabbableObject).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly))
        {
            if (
                method.Name.Contains(
                    "save",
                    System.StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains(
                    "load",
                    System.StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains(
                    "data",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                Log.LogInfo(
                    $"GrabbableObject METHOD: {method}"
                );
            }
        }

        Log.LogInfo("===== PHYSICSPROP METHODS =====");

        foreach (
            MethodInfo method in
            typeof(PhysicsProp).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly))
        {
            if (
                method.Name.Contains(
                    "save",
                    System.StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains(
                    "load",
                    System.StringComparison.OrdinalIgnoreCase) ||
                method.Name.Contains(
                    "data",
                    System.StringComparison.OrdinalIgnoreCase))
            {
                Log.LogInfo(
                    $"PhysicsProp METHOD: {method}"
                );
            }
        }

        Log.LogInfo("===============================");
    } */
    // ============================================================
    // BOOSTER REGISTRATION
    // ============================================================

    private void RegisterBooster(
        AssetBundle bundle,
        string assetName,
        BoosterType packType,
        int spawnWeight)
    {
        Item item =
            bundle.LoadAsset<Item>(assetName);

        if (item == null)
        {
            Log.LogError(
                $"Failed to load booster item: {assetName}"
            );

            return;
        }

        if (item.spawnPrefab == null)
        {
            Log.LogError(
                $"{assetName} has no spawn prefab."
            );

            return;
        }

        Log.LogInfo(
            $"BOOSTER DATA | " +
            $"Asset={assetName} | " +
            $"ItemName={item.itemName} | " +
            $"Prefab={item.spawnPrefab.name} | " +
            $"IsScrap={item.isScrap} | " +
            $"MinValue={item.minValue} | " +
            $"MaxValue={item.maxValue} | " +
            $"Weight={item.weight}"
        );

        PhysicsProp oldPhysicsProp =
            item.spawnPrefab.GetComponent<PhysicsProp>();

        if (oldPhysicsProp == null)
        {
            Log.LogError(
                $"{assetName} has no PhysicsProp."
            );

            return;
        }

        BoosterPackBehaviour boosterBehaviour =
            item.spawnPrefab
                .AddComponent<BoosterPackBehaviour>();

        boosterBehaviour.itemProperties =
            oldPhysicsProp.itemProperties;

        boosterBehaviour.grabbable =
            oldPhysicsProp.grabbable;

        boosterBehaviour.isInFactory =
            oldPhysicsProp.isInFactory;

        boosterBehaviour.mainObjectRenderer =
            oldPhysicsProp.mainObjectRenderer;

        boosterBehaviour.PackType =
            packType;

        Object.DestroyImmediate(
            oldPhysicsProp
        );

        Log.LogInfo(
            $"Replaced PhysicsProp with " +
            $"BoosterPackBehaviour for {packType} pack."
        );

        LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(
            item.spawnPrefab
        );

        Items.RegisterScrap(
            item,
            spawnWeight,
            Levels.LevelTypes.All
        );

        // Save references for our debug spawn keys.
        if (packType == BoosterType.Light)
        {
            LightBoosterItem = item;
        }
        else if (packType == BoosterType.Heavy)
        {
            HeavyBoosterItem = item;
        }

        Log.LogInfo(
            $"Registered {packType} booster | " +
            $"Spawn Weight: {spawnWeight}"
        );
    }
}