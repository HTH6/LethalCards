using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using LethalLib.Modules;
using UnityEngine;
using LethalCards.Cards;
using LethalCards.Boosters;
using GameNetcodeStuff;
using Unity.Netcode;
using HarmonyLib;
using System.Reflection;
using LethalCards.Grading;
using LethalCards.Networking;

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

    private ConfigEntry<int> lightBoosterSpawnWeight = null!;
    private ConfigEntry<int> heavyBoosterSpawnWeight = null!;

    // ============================================================

    private void Awake()
    {
        Log = Logger;

        lightBoosterSpawnWeight = Config.Bind(
            "Spawn Weights", "LightBoosterWeight", 12,
            new ConfigDescription(
                "Relative scrap spawn weight for Light Booster packs on all levels. 0 disables natural spawning. Restart the game after changing this setting.",
                new AcceptableValueRange<int>(0, int.MaxValue)));
        heavyBoosterSpawnWeight = Config.Bind(
            "Spawn Weights", "HeavyBoosterWeight", 4,
            new ConfigDescription(
                "Relative scrap spawn weight for Heavy Booster packs on all levels. 0 disables natural spawning. Restart the game after changing this setting.",
                new AcceptableValueRange<int>(0, int.MaxValue)));

        // Unity normally invokes these generated Netcode initializers.
        foreach (System.Type type in Assembly.GetExecutingAssembly().GetTypes())
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (method.IsDefined(typeof(RuntimeInitializeOnLoadMethodAttribute), false))
                method.Invoke(null, null);
        }

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

        foreach (CardDefinition card in CardDatabase.Cards)
        {
            if (card.AssetName != null)
                RegisterCard(bundle, card);
        }

        /* Previous per-card registrations retained for reference; metadata now lives in CardDatabase.V1.cs.
        RegisterCard(
            bundle,
            "HoardingBugCardItem",
            "LC01-001",
            "LC01",
            "Hoarding Bug",
            CardRarity.Common,
            6 // ,
            // 1 // Retained loose-card spawn weight.
        );

        RegisterCard(
            bundle,
            "EyelessDogCardItem",
            "LC01-002",
            "LC01",
            "Eyeless Dog",
            CardRarity.Common,
            7 // ,
            // 1 // Retained loose-card spawn weight.
        );

        RegisterCard(
            bundle,
            "SnareFleaCardItem",
            "LC01-006",
            "LC01",
            "Snare Flea",
            CardRarity.Uncommon,
            10 // ,
            // 1 // Retained loose-card spawn weight.
        );

        RegisterCard(
            bundle,
            "BrackenCardItem",
            "LC01-003",
            "LC01",
            "Bracken",
            CardRarity.Rare,
            32 // ,
            // 1 // Retained loose-card spawn weight.
        );

        RegisterCard(
            bundle,
            "CoilHeadCardItem",
            "LC01-004",
            "LC01",
            "Coil-Head",
            CardRarity.Rare,
            36 // ,
            // 1 // Retained loose-card spawn weight.
        );

        RegisterCard(
            bundle,
            "JesterCardItem",
            "LC01-005",
            "LC01",
            "Jester",
            CardRarity.UltraRare,
            75 // ,
            // 1 // Retained loose-card spawn weight.
        );

        RegisterCard(
            bundle,
            "GhostGirlCardItem",
            "LC01-007",
            "LC01",
            "Ghost Girl",
            CardRarity.SecretRare,
            140 // ,
            // 1 // Retained loose-card spawn weight.
        );

        */
        // ========================================================
        // BOOSTER REGISTRATION
        // ========================================================

        RegisterBooster(
            bundle,
            "LightBoosterPackItem",
            BoosterType.Light,
            // 9999 // Retained test spawn weight.
            lightBoosterSpawnWeight.Value
        );

        RegisterBooster(
            bundle,
            "HeavyBoosterPackItem",
            BoosterType.Heavy,
            // 9999 // Retained test spawn weight.
            heavyBoosterSpawnWeight.Value
        );

        Log.LogInfo(
            // $"Finished registering {CardDatabase.Cards.Count} cards."
            $"CARD REGISTRY | Definitions={CardDatabase.Cards.Count} | " +
            $"Implemented={System.Linq.Enumerable.Count(CardDatabase.GetImplementedCards())}"
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

        if (prefab.GetComponent<GradingReturnData>() == null)
            prefab.AddComponent<GradingReturnData>();
        if (prefab.GetComponent<NetworkItemConsumption>() == null)
            prefab.AddComponent<NetworkItemConsumption>();

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
    private void RegisterCard(AssetBundle bundle, CardDefinition card)
    {
        Item item = bundle.LoadAsset<Item>(card.AssetName!);
        if (item == null || item.spawnPrefab == null ||
            item.spawnPrefab.GetComponent<NetworkObject>() == null ||
            item.spawnPrefab.GetComponent<PhysicsProp>() == null)
        {
            Log.LogError($"CARD DISABLED | Id={card.CardId} | Asset={card.AssetName} | Missing item, prefab, NetworkObject, or PhysicsProp.");
            return;
        }

        PrepareCardPrefab(item.spawnPrefab);
        item.saveItemVariable = true;
        LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(item.spawnPrefab);
        Items.RegisterItem(item); // Cards are pack contents, not natural map scrap.
        card.AttachImplementedItem(item);
        Log.LogInfo($"CARD ENABLED | Id={card.CardId} | SetNumber={card.SetNumber:D3} | Name={card.DisplayName} | Rarity={card.Rarity} | BaseValue={card.BaseScrapValue}");
    }
//     private void RegisterCard(
//         AssetBundle bundle,
//         string assetName,
//         string cardId,
//         string setId,
//         string displayName,
//         CardRarity rarity,
//         int baseScrapValue // ,
//         // int spawnWeight
//         )
//     {
//         Item item =
//             bundle.LoadAsset<Item>(assetName);
//
//         if (item == null)
//         {
//             Log.LogError(
//                 $"Failed to load card item: {assetName}"
//             );
//
//             return;
//         }
//
//         if (item.spawnPrefab == null)
//         {
//             Log.LogError(
//                 $"{assetName} has no spawn prefab assigned."
//             );
//
//             return;
//         }
//
//         Log.LogInfo(
//             $"Loaded card item: {assetName}"
//         );
//
//         Log.LogInfo(
//             $"Prefab: {item.spawnPrefab.name}"
//         );
//
//         PhysicsProp physicsProp =
//             item.spawnPrefab.GetComponent<PhysicsProp>();
//
//         Log.LogInfo(
//             $"PhysicsProp found: " +
//             $"{physicsProp != null}"
//         );
//
//         Log.LogInfo(
//             $"CARD SAVE CONFIG | " +
//             $"Card={displayName} | " +
//             $"SaveItemVariable={item.saveItemVariable}"
//         );
//
//         // Add our networked card metadata component BEFORE
//         // LethalLib registers this prefab with Netcode.
//         PrepareCardPrefab(
//             item.spawnPrefab
//         );
//
//         item.saveItemVariable = true;
//
//         LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(
//             item.spawnPrefab
//         );
//
//         /* Retained code-defined spawn override; level spawn tables are authored in Unity.
//         Items.RegisterScrap(
//             item,
//             spawnWeight,
//             Levels.LevelTypes.All
//         );
//         */
//         Items.RegisterItem(item); // Preserve save/load item registration without map spawns.
//
//         CardDefinition card =
//             new CardDefinition(
//                 cardId,
//                 setId,
//                 displayName,
//                 rarity,
//                 baseScrapValue,
//                 item
//             );
//
//         CardDatabase.Add(card);
//
//         Log.LogInfo(
//             $"Registered card: {displayName} | " +
//             $"Rarity: {rarity} | " +
//             $"Base Value: ${baseScrapValue} | " +
//             $"Natural spawn registration disabled"
//         );
//     }
//
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
        // int spawnWeight
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

        if (item.spawnPrefab.GetComponent<NetworkItemConsumption>() == null)
            item.spawnPrefab.AddComponent<NetworkItemConsumption>();

        Log.LogInfo(
            $"Replaced PhysicsProp with " +
            $"BoosterPackBehaviour for {packType} pack."
        );

        LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(
            item.spawnPrefab
        );

        /* Previous registration retained for reference; boosters now use BepInEx configuration.
        Items.RegisterScrap(
            item,
            spawnWeight,
            Levels.LevelTypes.All
        );
        */
        if (spawnWeight > 0)
            Items.RegisterScrap(item, spawnWeight, Levels.LevelTypes.All);
        else
            Items.RegisterItem(item); // Preserve save/load item registration without map spawns.

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
            $"Configured Spawn Weight: {spawnWeight}"
        );
    }
}
