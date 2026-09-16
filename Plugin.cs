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
    public const string PluginGuid = "Hdaddy.LethalCards";
    public const string PluginName = "Lethal Cards";
    public const string PluginVersion = "0.1.3";

    internal static ManualLogSource Log = null!;

    // ============================================================
    // DEBUG ONLY
    // ============================================================

    internal static Item? LightBoosterItem;
    internal static Item? HeavyBoosterItem;
    internal static Item? BoosterBoxItem;
    internal static Item? GoldenBoosterBoxItem;

    private Harmony? harmony;

    private ConfigEntry<int> lightBoosterSpawnWeight = null!;
    private ConfigEntry<int> heavyBoosterSpawnWeight = null!;
    private ConfigEntry<int> boosterBoxSpawnWeight = null!;
    private ConfigEntry<int> goldenBoosterBoxSpawnWeight = null!;

    // ============================================================

    private void Awake()
    {
        Log = Logger;

        string configPath = Path.Combine(Paths.ConfigPath, "LethalCards.cfg");
        string[] legacyConfigs = Directory.GetFiles(Paths.ConfigPath, "*.LethalCards.cfg");
        // Preserve existing settings on upgrade without deleting the original file.
        // Once the new file exists, it is the sole source of configuration.
        if (!File.Exists(configPath) && legacyConfigs.Length == 1)
        {
            File.Copy(legacyConfigs[0], configPath);
            Log.LogInfo("Copied existing Lethal Cards settings to LethalCards.cfg; the legacy file is no longer used.");
        }
        ConfigFile cardConfig = new ConfigFile(configPath, true);
        // Rewrites legacy header comments without embedding an old plugin identifier.
        cardConfig.Save();

        lightBoosterSpawnWeight = cardConfig.Bind(
            "Spawn Weights", "LightBoosterWeight", 30,
            new ConfigDescription(
                "Relative scrap spawn weight for Light Booster packs on all levels. 0 disables natural spawning. Restart the game after changing this setting.",
                new AcceptableValueRange<int>(0, int.MaxValue)));
        heavyBoosterSpawnWeight = cardConfig.Bind(
            "Spawn Weights", "HeavyBoosterWeight", 15,
            new ConfigDescription(
                "Relative scrap spawn weight for Heavy Booster packs on all levels. 0 disables natural spawning. Restart the game after changing this setting.",
                new AcceptableValueRange<int>(0, int.MaxValue)));

        boosterBoxSpawnWeight = cardConfig.Bind(
            "Spawn Weights", "BoosterBoxWeight", 10,
            new ConfigDescription("Relative scrap spawn weight for Standard Booster Boxes on all levels. 0 disables natural spawning. Restart after changing.",
                new AcceptableValueRange<int>(0, int.MaxValue)));
        goldenBoosterBoxSpawnWeight = cardConfig.Bind(
            "Spawn Weights", "GoldenBoosterBoxWeight", 5,
            new ConfigDescription("Relative scrap spawn weight for Golden Booster Boxes on all levels. 0 disables natural spawning. Restart after changing.",
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

        // The first seven registry entries are the original shipped cards.
        // Keep their item indices, and those of packs/boxes, stable when adding the rest.
        foreach (CardDefinition card in System.Linq.Enumerable.Take(CardDatabase.Cards, 7))
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

        // Append boxes after existing items to preserve their vanilla save indices.
        BoosterBoxItem = RegisterBoosterBox(bundle, "BoosterBoxItem", BoosterBoxType.Standard, boosterBoxSpawnWeight.Value);
        GoldenBoosterBoxItem = RegisterBoosterBox(bundle, "GoldenBoosterBoxItem", BoosterBoxType.Golden, goldenBoosterBoxSpawnWeight.Value);

        foreach (CardDefinition card in System.Linq.Enumerable.Skip(CardDatabase.Cards, 7))
        {
            if (card.AssetName != null)
                RegisterCard(bundle, card);
        }

        Log.LogInfo(
            // $"Finished registering {CardDatabase.Cards.Count} cards."
            $"CARD REGISTRY | Definitions={CardDatabase.Cards.Count} | " +
            $"Implemented={System.Linq.Enumerable.Count(CardDatabase.GetImplementedCards())} | " +
            string.Join(" | ", System.Linq.Enumerable.Select(
                (CardRarity[])System.Enum.GetValues(typeof(CardRarity)), rarity =>
                    $"{rarity}={System.Linq.Enumerable.Count(CardDatabase.GetImplementedCards(), card => card.Rarity == rarity)}"))
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
        item.canBeInspected = true;
        LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(item.spawnPrefab);
        Items.RegisterItem(item); // Cards are pack contents, not natural map scrap.
        card.AttachImplementedItem(item);
        Log.LogInfo($"CARD ENABLED | Id={card.CardId} | SetNumber={card.SetNumber:D3} | Name={card.DisplayName} | Rarity={card.Rarity} | BaseValue={card.BaseScrapValue} | CanBeInspected={item.canBeInspected}");
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

    internal static bool EnsureActionTooltip(Item? item, string action)
    {
        action = BoosterActionTooltips.Format(action);
        if (item == null)
        {
            Log.LogWarning($"ACTION TOOLTIP NOT CONFIGURED | Action=\"{action}\" | Item reference is null.");
            return false;
        }
        if (item.toolTips != null && item.toolTips.Length > 0 && item.toolTips[0] == action)
            return false;

        // The first custom tip is the primary action. Preserve any additional asset-authored tips.
        // Vanilla supplies Drop separately. Format uses the current vanilla use binding when available.
        string[] tips = item.toolTips == null || item.toolTips.Length == 0
            ? new string[1] : (string[])item.toolTips.Clone();
        tips[0] = action;
        item.toolTips = tips;
        return true;
    }

    private Item? RegisterBoosterBox(AssetBundle bundle, string assetName, BoosterBoxType type, int spawnWeight)
    {
        Item item = bundle.LoadAsset<Item>(assetName);
        if (item == null || item.spawnPrefab == null || item.spawnPrefab.GetComponent<NetworkObject>() == null)
        {
            Log.LogError($"BOOSTER BOX REGISTRATION FAILED | Asset={assetName} | Missing Item, spawn prefab, or NetworkObject.");
            return null;
        }
        PhysicsProp original = item.spawnPrefab.GetComponent<PhysicsProp>();
        if (original == null)
        {
            Log.LogError($"BOOSTER BOX REGISTRATION FAILED | Asset={assetName} | Missing PhysicsProp.");
            return null;
        }
        BoosterBoxBehaviour box = item.spawnPrefab.AddComponent<BoosterBoxBehaviour>();
        // Match the working BoosterPackBehaviour replacement: preserve the prefab's Item reference.
        box.itemProperties = original.itemProperties;
        if (box.itemProperties != item)
            Log.LogWarning($"BOOSTER BOX ITEM REFERENCE MISMATCH | Type={type} | Loaded={item.name} | Original={original.itemProperties?.name}");
        box.grabbable = original.grabbable;
        box.isInFactory = original.isInFactory;
        box.mainObjectRenderer = original.mainObjectRenderer;
        box.BoxType = type;
        bool tooltipCorrected = EnsureActionTooltip(item, "Open Box");
        if (box.itemProperties != item)
            tooltipCorrected |= EnsureActionTooltip(box.itemProperties, "Open Box");
        Log.LogInfo($"BOOSTER BOX TOOLTIP | Type={type} | Display=\"{box.itemProperties?.toolTips?[0]}\" | Corrected={tooltipCorrected}");
        item.saveItemVariable = true; // Persist failed-opening lock; never changes Item.weight.
        Object.DestroyImmediate(original);
        Log.LogInfo($"BOOSTER BOX BEHAVIOUR ATTACHED | Type={type} | Prefab={item.spawnPrefab.name} | " +
            $"ComponentType={box.GetType().FullName} | ItemProperties={box.itemProperties?.name} | Grabbable={box.grabbable} | " +
            $"GrabbableComponents={item.spawnPrefab.GetComponents<GrabbableObject>().Length} | " +
            $"ActivationComponent={item.spawnPrefab.GetComponent<GrabbableObject>()?.GetType().FullName} | " +
            $"MainObjectRenderer={box.mainObjectRenderer?.name} | UseCooldown={box.useCooldown}");
        if (item.spawnPrefab.GetComponent<NetworkItemConsumption>() == null)
            item.spawnPrefab.AddComponent<NetworkItemConsumption>();
        LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(item.spawnPrefab);
        if (spawnWeight > 0)
            Items.RegisterScrap(item, spawnWeight, Levels.LevelTypes.All);
        else
            Items.RegisterItem(item);
        Log.LogInfo($"BOOSTER BOX REGISTERED | Type={type} | SpawnWeight={spawnWeight}");
        return item;
    }

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

        bool tooltipCorrected = EnsureActionTooltip(item, "Rip Pack");
        if (boosterBehaviour.itemProperties != item)
            tooltipCorrected |= EnsureActionTooltip(boosterBehaviour.itemProperties, "Rip Pack");
        Log.LogInfo($"BOOSTER TOOLTIP | Type={packType} | Display=\"{boosterBehaviour.itemProperties?.toolTips?[0]}\" | Corrected={tooltipCorrected}");

        Object.DestroyImmediate(
            oldPhysicsProp
        );

        if (item.spawnPrefab.GetComponent<NetworkItemConsumption>() == null)
            item.spawnPrefab.AddComponent<NetworkItemConsumption>();

        Log.LogInfo(
            $"Replaced PhysicsProp with " +
            $"BoosterPackBehaviour for {packType} pack."
        );

        BoosterPrefabDiagnostics.ValidateAndLog(item.spawnPrefab, packType, "BOOSTER PREPARED PREFAB");

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
