using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using LethalCards.Cards;
using LethalCards.Save;
using Unity.Netcode;

namespace LethalCards.ShipDisplay;

internal static class ShipCardDisplaySaveManager
{
    internal const int SlotCount = 33;
    private const string FormatHeader = "VERSION|1";

    private static string? loadedSaveKey;
    private static ShipCardDisplayStoredCard?[] currentSlots =
        new ShipCardDisplayStoredCard?[SlotCount];

    internal static ShipCardDisplayStoredCard?[] LoadForCurrentSave()
    {
        if (!IsAuthoritativeProcess())
            return CloneSlots(currentSlots);

        if (!TryGetSaveContext(out string saveName, out string saveIdentity, out string saveKey))
            return new ShipCardDisplayStoredCard?[SlotCount];

        if (loadedSaveKey == saveKey)
            return CloneSlots(currentSlots);

        loadedSaveKey = saveKey;
        currentSlots = new ShipCardDisplayStoredCard?[SlotCount];
        string path = GetSavePath(saveName, saveIdentity);
        if (!File.Exists(path))
            return CloneSlots(currentSlots);

        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (!line.StartsWith("SLOT|", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split('|');
                if (parts.Length != 7 ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slotNumber) ||
                    !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int variantValue) ||
                    !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int grade) ||
                    !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ungradedValue) ||
                    !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int finalValue))
                {
                    Plugin.Log.LogWarning($"SHIP CARD DISPLAY LOAD SKIPPED | InvalidLine={line}");
                    continue;
                }

                int slotIndex = slotNumber - 1;
                CardDefinition? definition = CardDatabase.GetById(parts[2]);
                if (slotIndex < 0 || slotIndex >= SlotCount || definition == null ||
                    definition.SetNumber != slotNumber ||
                    !Enum.IsDefined(typeof(CardVariant), variantValue) ||
                    grade < 0 || grade > 10 || ungradedValue < 0 || finalValue < 0)
                {
                    Plugin.Log.LogWarning(
                        $"SHIP CARD DISPLAY LOAD SKIPPED | Slot={slotNumber} | CardId={parts[2]} | " +
                        "Reason=Metadata did not match the fixed set-number slot");
                    continue;
                }

                currentSlots[slotIndex] = new ShipCardDisplayStoredCard(
                    definition.CardId,
                    (CardVariant)variantValue,
                    grade,
                    ungradedValue,
                    finalValue);
            }

            int occupied = 0;
            foreach (ShipCardDisplayStoredCard? slot in currentSlots)
                if (slot != null)
                    occupied++;
            Plugin.Log.LogInfo(
                $"SHIP CARD DISPLAY LOADED | OccupiedSlots={occupied} | Save={saveName}");
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError($"SHIP CARD DISPLAY LOAD FAILED | Save={saveName} | {exception}");
            currentSlots = new ShipCardDisplayStoredCard?[SlotCount];
        }

        return CloneSlots(currentSlots);
    }

    internal static bool TryCommitSlot(int slotIndex, ShipCardDisplayStoredCard? storedCard)
    {
        if (!IsAuthoritativeProcess() || slotIndex < 0 || slotIndex >= SlotCount)
            return false;

        LoadForCurrentSave();
        ShipCardDisplayStoredCard?[] candidate = CloneSlots(currentSlots);
        candidate[slotIndex] = storedCard?.Clone();
        if (!TrySave(candidate))
            return false;

        currentSlots = candidate;
        return true;
    }

    internal static bool TryClearAll(
        out int occupiedBefore,
        out int occupiedAfter)
    {
        occupiedBefore = 0;
        occupiedAfter = 0;
        if (!IsAuthoritativeProcess())
            return false;

        LoadForCurrentSave();
        occupiedBefore = CountOccupied(currentSlots);
        ShipCardDisplayStoredCard?[] empty = new ShipCardDisplayStoredCard?[SlotCount];
        if (!TrySave(empty))
        {
            occupiedAfter = occupiedBefore;
            return false;
        }

        currentSlots = empty;
        occupiedAfter = CountOccupied(currentSlots);
        return true;
    }

    internal static void ResetLoadedSave()
    {
        loadedSaveKey = null;
        currentSlots = new ShipCardDisplayStoredCard?[SlotCount];
    }

    private static bool TrySave(ShipCardDisplayStoredCard?[] slots)
    {
        if (!TryGetSaveContext(out string saveName, out string saveIdentity, out _))
        {
            Plugin.Log.LogWarning("SHIP CARD DISPLAY SAVE FAILED | No active campaign identity");
            return false;
        }

        try
        {
            string path = GetSavePath(saveName, saveIdentity);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            List<string> lines = new() { FormatHeader };
            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                ShipCardDisplayStoredCard? storedCard = slots[slotIndex];
                if (storedCard == null)
                    continue;

                lines.Add(string.Join("|",
                    "SLOT",
                    (slotIndex + 1).ToString(CultureInfo.InvariantCulture),
                    storedCard.CardId,
                    ((int)storedCard.Variant).ToString(CultureInfo.InvariantCulture),
                    storedCard.Grade.ToString(CultureInfo.InvariantCulture),
                    storedCard.UngradedValue.ToString(CultureInfo.InvariantCulture),
                    storedCard.FinalValue.ToString(CultureInfo.InvariantCulture)));
            }

            string temporaryPath = path + ".tmp";
            File.WriteAllLines(temporaryPath, lines);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError($"SHIP CARD DISPLAY SAVE FAILED | Save={saveName} | {exception}");
            return false;
        }
    }

    private static bool TryGetSaveContext(
        out string saveName,
        out string saveIdentity,
        out string saveKey)
    {
        saveName = GameNetworkManager.Instance?.currentSaveFileName ?? string.Empty;
        saveIdentity = string.Empty;
        saveKey = string.Empty;
        if (string.IsNullOrWhiteSpace(saveName))
            return false;

        saveIdentity = SaveIdentityManager.GetOrCreateIdentity();
        if (string.IsNullOrWhiteSpace(saveIdentity))
            return false;

        saveKey = $"{saveName}|{saveIdentity}";
        return true;
    }

    private static string GetSavePath(string saveName, string saveIdentity) =>
        Path.Combine(
            Paths.ConfigPath,
            "LethalCards",
            $"{MakeSafeFileName(saveName)}_{MakeSafeFileName(saveIdentity)}_ship_display.txt");

    private static string MakeSafeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private static ShipCardDisplayStoredCard?[] CloneSlots(
        ShipCardDisplayStoredCard?[] source)
    {
        ShipCardDisplayStoredCard?[] clone = new ShipCardDisplayStoredCard?[SlotCount];
        for (int index = 0; index < SlotCount && index < source.Length; index++)
            clone[index] = source[index]?.Clone();
        return clone;
    }

    private static int CountOccupied(ShipCardDisplayStoredCard?[] slots)
    {
        int occupied = 0;
        foreach (ShipCardDisplayStoredCard? slot in slots)
            if (slot != null)
                occupied++;
        return occupied;
    }

    private static bool IsAuthoritativeProcess()
    {
        NetworkManager? manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening || manager.IsServer;
    }
}
