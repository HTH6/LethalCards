using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LethalCards.Boosters;

internal static class BoosterRevealAssets
{
    internal static GameObject? RevealFxPrefab { get; private set; }
    internal static AudioClip? BoosterOpen { get; private set; }
    internal static AudioClip? CommonUncommon { get; private set; }
    internal static AudioClip? Rare { get; private set; }
    internal static AudioClip? Ultra { get; private set; }
    internal static AudioClip? Secret { get; private set; }
    internal static AudioClip? UltraAnticipation { get; private set; }
    internal static AudioClip? SecretAnticipation { get; private set; }
    internal static AudioClip? GodPack { get; private set; }

    internal static void Load(AssetBundle bundle)
    {
        string[] names = bundle.GetAllAssetNames();
        RevealFxPrefab = LoadNamed<GameObject>(bundle, names, "CardRevealFX");
        GameObject? foil = LoadNamed<GameObject>(bundle, names, "FoilOverlay");
        Material? alternate = LoadNamed<Material>(bundle, names, "MAT_AltArtInvert");
        BoosterOpen = LoadNamed<AudioClip>(bundle, names, "SFX_BoosterOpen");
        CommonUncommon = LoadNamed<AudioClip>(bundle, names, "SFX_Card_CommonUncommon");
        Rare = LoadNamed<AudioClip>(bundle, names, "SFX_Card_Rare");
        Ultra = LoadNamed<AudioClip>(bundle, names, "SFX_Card_Ultra");
        Secret = LoadNamed<AudioClip>(bundle, names, "SFX_Card_Secret");
        UltraAnticipation = LoadNamed<AudioClip>(bundle, names, "SFX_Anticipation_Ultra");
        SecretAnticipation = LoadNamed<AudioClip>(bundle, names, "SFX_Anticipation_Secret");
        GodPack = LoadNamed<AudioClip>(bundle, names, "SFX_GodPack");
        Cards.CardVariantVisuals.ConfigureAssets(foil, alternate);
    }

    private static T? LoadNamed<T>(AssetBundle bundle, string[] paths, string expectedName) where T : UnityEngine.Object
    {
        string? path = paths.FirstOrDefault(candidate => Path.GetFileNameWithoutExtension(candidate).Equals(expectedName, StringComparison.OrdinalIgnoreCase));
        T? asset = path == null ? null : bundle.LoadAsset<T>(path);
        if (asset == null)
            asset = bundle.LoadAllAssets<T>().FirstOrDefault(candidate => candidate.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
        if (asset == null) Plugin.Log.LogWarning($"REVEAL ASSET MISSING | Name={expectedName} | Type={typeof(T).Name}");
        else Plugin.Log.LogInfo($"REVEAL ASSET READY | Name={expectedName} | Asset={asset.name}");
        return asset;
    }
}
