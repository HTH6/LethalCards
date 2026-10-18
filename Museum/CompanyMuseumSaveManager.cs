using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using LethalCards.Cards;
using LethalCards.Save;
using Unity.Netcode;

namespace LethalCards.Museum;

internal static class CompanyMuseumSaveManager
{
    private const string FormatHeader = "VERSION|1";
    private static string? loadedSaveKey;
    private static bool saveBlockedByCorruptInput;
    private static string? loadedPath;

    internal static bool HasLoadedCurrentSave => loadedSaveKey != null;

    internal static void LoadForCurrentSave()
    {
        if (!IsAuthoritativeProcess() ||
            !TryGetSaveContext(out string saveName, out string identity, out string saveKey))
            return;
        if (loadedSaveKey == saveKey)
            return;

        loadedSaveKey = saveKey;
        saveBlockedByCorruptInput = false;
        loadedPath = GetSavePath(saveName, identity);
        List<CompanyMuseumRecord> records = new();
        if (!File.Exists(loadedPath))
        {
            CompanyMuseumManager.ReplaceFromPersistence(records);
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM LOADED | Save={saveName} | Entries=0/{CompanyMuseumManager.TotalEntries} | NewCampaignData=True");
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(loadedPath);
            if (lines.Length == 0 || !lines[0].Equals(FormatHeader, StringComparison.Ordinal))
            {
                saveBlockedByCorruptInput = true;
                Plugin.Log.LogError(
                    $"COMPANY MUSEUM LOAD BLOCKED | Path={loadedPath} | Reason=Missing or unsupported version header | " +
                    "The existing file will not be overwritten.");
            }

            for (int index = 1; index < lines.Length; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (!TryParseRecord(line, out CompanyMuseumRecord? record))
                {
                    saveBlockedByCorruptInput = true;
                    Plugin.Log.LogError(
                        $"COMPANY MUSEUM LOAD INVALID | Path={loadedPath} | Line={index + 1} | " +
                        "The existing file will not be overwritten.");
                    continue;
                }
                records.Add(record);
            }

            CompanyMuseumManager.ReplaceFromPersistence(records);
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM LOADED | Save={saveName} | " +
                $"Entries={CompanyMuseumManager.DiscoveryCount}/{CompanyMuseumManager.TotalEntries} | " +
                $"WriteBlocked={saveBlockedByCorruptInput} | Path={loadedPath}");
        }
        catch (Exception exception)
        {
            saveBlockedByCorruptInput = true;
            CompanyMuseumManager.ReplaceFromPersistence(records);
            Plugin.Log.LogError(
                $"COMPANY MUSEUM LOAD FAILED | Path={loadedPath} | Existing file will not be overwritten | {exception}");
        }
    }

    internal static bool TrySave(IEnumerable<CompanyMuseumRecord> records)
    {
        if (!IsAuthoritativeProcess() ||
            !TryGetSaveContext(out string saveName, out string identity, out string saveKey))
            return false;
        if (loadedSaveKey != saveKey)
            LoadForCurrentSave();
        if (saveBlockedByCorruptInput)
        {
            Plugin.Log.LogError(
                $"COMPANY MUSEUM SAVE REFUSED | Path={loadedPath ?? "Unknown"} | " +
                "The loaded museum file contained invalid data; preserve or repair it before registering more cards.");
            return false;
        }

        string path = GetSavePath(saveName, identity);
        string temporaryPath = path + ".tmp";
        string backupPath = path + ".bak";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            List<string> lines = new() { FormatHeader };
            foreach (CompanyMuseumRecord record in records
                         .Where(CompanyMuseumManager.IsValid)
                         .OrderBy(record => CardDatabase.GetById(record.CardId)!.SetNumber)
                         .ThenBy(record => (int)record.Variant))
            {
                lines.Add(string.Join("|",
                    "ENTRY",
                    record.CardId,
                    ((int)record.Variant).ToString(CultureInfo.InvariantCulture),
                    record.Grade.ToString(CultureInfo.InvariantCulture),
                    record.UngradedValue.ToString(CultureInfo.InvariantCulture),
                    record.FinalValue.ToString(CultureInfo.InvariantCulture)));
            }

            File.WriteAllLines(temporaryPath, lines);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, backupPath);
            else
                File.Move(temporaryPath, path);
            Plugin.Log.LogInfo(
                $"COMPANY MUSEUM SAVED | Save={saveName} | Entries={lines.Count - 1}/{CompanyMuseumManager.TotalEntries}");
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError($"COMPANY MUSEUM SAVE FAILED | Path={path} | {exception}");
            return false;
        }
    }

    internal static void ResetLoadedSave()
    {
        loadedSaveKey = null;
        loadedPath = null;
        saveBlockedByCorruptInput = false;
    }

    private static bool TryParseRecord(string line, out CompanyMuseumRecord record)
    {
        record = null!;
        string[] parts = line.Split('|');
        if (parts.Length != 6 || !parts[0].Equals("ENTRY", StringComparison.Ordinal) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int variant) ||
            !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade) ||
            !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ungradedValue) ||
            !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int finalValue) ||
            !Enum.IsDefined(typeof(CardVariant), variant))
            return false;

        record = new CompanyMuseumRecord(
            parts[1], (CardVariant)variant, grade, ungradedValue, finalValue);
        return CompanyMuseumManager.IsValid(record);
    }

    private static bool TryGetSaveContext(
        out string saveName,
        out string identity,
        out string saveKey)
    {
        saveName = GameNetworkManager.Instance?.currentSaveFileName ?? string.Empty;
        identity = string.Empty;
        saveKey = string.Empty;
        if (string.IsNullOrWhiteSpace(saveName))
            return false;
        identity = SaveIdentityManager.GetOrCreateIdentity();
        if (string.IsNullOrWhiteSpace(identity))
            return false;
        saveKey = $"{saveName}|{identity}";
        return true;
    }

    private static string GetSavePath(string saveName, string identity) =>
        Path.Combine(
            Paths.ConfigPath,
            "LethalCards",
            $"{MakeSafeFileName(saveName)}_{MakeSafeFileName(identity)}_museum_v1.txt");

    private static string MakeSafeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private static bool IsAuthoritativeProcess()
    {
        NetworkManager? manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || manager.IsServer;
    }
}
