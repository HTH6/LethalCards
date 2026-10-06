using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Unity.Netcode;
using LethalCards.Save;

namespace LethalCards.Cards;

public static class CollectionSaveManager
{
    private static string? loadedSaveKey;

    public static void LoadForCurrentSave()
    {
        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        string saveName =
            GetCurrentSaveName();

        if (string.IsNullOrWhiteSpace(saveName))
            return;

        string saveIdentity =
            SaveIdentityManager.GetOrCreateIdentity();

        if (string.IsNullOrWhiteSpace(saveIdentity))
        {
            Plugin.Log.LogWarning(
                "COLLECTION LOAD | " +
                "Save identity unavailable."
            );

            return;
        }

        string saveKey =
            $"{saveName}|{saveIdentity}";

        if (loadedSaveKey == saveKey)
            return;

        loadedSaveKey =
            saveKey;

        string path =
            GetSavePath(
                saveName,
                saveIdentity
            );

        if (!File.Exists(path))
        {
            CollectionManager.Clear();

            /* Plugin.Log.LogInfo(
                $"COLLECTION LOAD | " +
                $"No save found | " +
                $"Save={saveName} | " +
                $"Identity={saveIdentity} | " +
                $"Path={path}"
            ); */

            return;
        }

        try
        {
            string[] lines =
                File.ReadAllLines(
                    path
                );

            List<string> cards =
                new();

            List<string> variants =
                new();

            foreach (string line in lines)
            {
                if (
                    line.StartsWith(
                        "CARD|",
                        StringComparison.Ordinal))
                {
                    cards.Add(
                        line.Substring(
                            "CARD|".Length
                        )
                    );
                }
                else if (
                    line.StartsWith(
                        "VARIANT|",
                        StringComparison.Ordinal))
                {
                    variants.Add(
                        line.Substring(
                            "VARIANT|".Length
                        )
                    );
                }
            }

            CollectionManager.LoadData(
                cards,
                variants
            );

            /* Plugin.Log.LogInfo(
                $"COLLECTION LOADED | " +
                $"Cards={cards.Count} | " +
                $"Variants={variants.Count} | " +
                $"Save={saveName} | " +
                $"Identity={saveIdentity} | " +
                $"Path={path}"
            ); */
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"COLLECTION SAVE: " +
                $"Failed to load collection: {ex}"
            );
        }
    }

    public static void Save()
    {
        if (
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening &&
            !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        string saveName =
            GetCurrentSaveName();

        // Don't write collection data before
        // an actual save is active.
        if (string.IsNullOrWhiteSpace(saveName))
            return;

        string saveIdentity =
            SaveIdentityManager.GetOrCreateIdentity();

        if (string.IsNullOrWhiteSpace(saveIdentity))
        {
            Plugin.Log.LogWarning(
                "COLLECTION SAVE | " +
                "Save identity unavailable."
            );

            return;
        }

        string path =
            GetSavePath(
                saveName,
                saveIdentity
            );

        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)!
            );

            List<string> lines =
                new();

            foreach (
                string card in
                CollectionManager
                    .GetDiscoveredCards()
                    .OrderBy(x => x))
            {
                lines.Add(
                    $"CARD|{card}"
                );
            }

            foreach (
                string variant in
                CollectionManager
                    .GetDiscoveredVariants()
                    .OrderBy(x => x))
            {
                lines.Add(
                    $"VARIANT|{variant}"
                );
            }

            File.WriteAllLines(
                path,
                lines
            );

            /* Plugin.Log.LogInfo(
                $"COLLECTION SAVED | " +
                $"Cards={CollectionManager.DiscoveredCardCount} | " +
                $"Variants={CollectionManager.DiscoveredVariantCount} | " +
                $"Save={saveName} | " +
                $"Identity={saveIdentity} | " +
                $"Path={path}"
            ); */
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"COLLECTION SAVE: " +
                $"Failed to save collection: {ex}"
            );
        }
    }

    private static string GetCurrentSaveName()
    {
        if (GameNetworkManager.Instance == null)
            return "";

        return
            GameNetworkManager.Instance
                .currentSaveFileName
            ?? "";
    }

    private static string GetSavePath(
        string saveName,
        string saveIdentity)
    {
        string safeName =
            MakeSafeFileName(
                saveName
            );

        string safeIdentity =
            MakeSafeFileName(
                saveIdentity
            );

        return Path.Combine(
            Paths.ConfigPath,
            "LethalCards",
            $"{safeName}_{safeIdentity}_collection.txt"
        );
    }

    private static string MakeSafeFileName(
        string value)
    {
        foreach (
            char invalid in
            Path.GetInvalidFileNameChars())
        {
            value =
                value.Replace(
                    invalid,
                    '_'
                );
        }

        return value;
    }

    public static void ResetLoadedSave()
    {
        loadedSaveKey =
            null;
    }
}
