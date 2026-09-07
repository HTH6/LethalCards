using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using LethalCards.Cards;
using Unity.Netcode;
using LethalCards.Save;

namespace LethalCards.Grading;

public static class GradingSaveManager
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
                "GRADING LOAD | " +
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

        // This is deliberately done BEFORE
        // checking whether the save file exists.
        //
        // A new save with no grading file must
        // still clear the previous save's jobs.
        GradingManager.Clear();
        GradingDayManager.SetDay(0);

        if (!File.Exists(path))
        {
            Plugin.Log.LogInfo(
                $"GRADING LOAD | " +
                $"No save found | " +
                $"Save={saveName} | " +
                $"Identity={saveIdentity} | " +
                $"Path={path}"
            );

            return;
        }

        try
        {
            string[] lines =
                File.ReadAllLines(
                    path
                );

            int loadedJobs =
                0;

            foreach (string line in lines)
            {
                if (
                    string.IsNullOrWhiteSpace(
                        line
                    ))
                {
                    continue;
                }

                string[] parts =
                    line.Split('|');

                if (parts.Length == 0)
                    continue;

                if (
                    parts[0].Equals(
                        "DAY",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        parts.Length >= 2 &&
                        int.TryParse(
                            parts[1],
                            out int savedDay))
                    {
                        GradingDayManager.SetDay(
                            savedDay
                        );
                    }

                    continue;
                }

                if (
                    parts[0].Equals(
                        "JOB",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length < 7)
                    {
                        Plugin.Log.LogWarning(
                            $"GRADING LOAD SKIPPED | " +
                            $"Invalid job line: {line}"
                        );

                        continue;
                    }

                    string jobId =
                        parts[1];

                    string cardId =
                        parts[2];

                    if (
                        !int.TryParse(
                            parts[3],
                            out int variantValue))
                    {
                        continue;
                    }

                    if (
                        !int.TryParse(
                            parts[4],
                            out int grade))
                    {
                        continue;
                    }

                    if (
                        !int.TryParse(
                            parts[5],
                            out int submittedDay))
                    {
                        continue;
                    }

                    if (
                        !int.TryParse(
                            parts[6],
                            out int readyDay))
                    {
                        continue;
                    }

                    GradingJob job =
                        new GradingJob
                        {
                            JobId =
                                jobId,

                            CardId =
                                cardId,

                            Variant =
                                (CardVariant)
                                variantValue,

                            Grade =
                                grade,

                            SubmittedDay =
                                submittedDay,

                            ReadyDay =
                                readyDay
                        };

                    GradingManager.AddLoadedJob(
                        job
                    );

                    loadedJobs++;
                }
            }

            Plugin.Log.LogInfo(
                $"GRADING LOAD | " +
                $"Jobs={loadedJobs} | " +
                $"Day={GradingDayManager.CurrentDay} | " +
                $"Save={saveName} | " +
                $"Identity={saveIdentity} | " +
                $"Path={path}"
            );
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"GRADING LOAD | " +
                $"Failed to load grading data: {ex}"
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

        if (string.IsNullOrWhiteSpace(saveName))
            return;

        string saveIdentity =
            SaveIdentityManager.GetOrCreateIdentity();

        if (string.IsNullOrWhiteSpace(saveIdentity))
        {
            Plugin.Log.LogWarning(
                "GRADING SAVE | " +
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
                new List<string>();

            lines.Add(
                $"DAY|{GradingDayManager.CurrentDay}"
            );

            foreach (
                GradingJob job
                in GradingManager.Jobs)
            {
                lines.Add(
                    $"JOB|" +
                    $"{job.JobId}|" +
                    $"{job.CardId}|" +
                    $"{(int)job.Variant}|" +
                    $"{job.Grade}|" +
                    $"{job.SubmittedDay}|" +
                    $"{job.ReadyDay}"
                );
            }

            File.WriteAllLines(
                path,
                lines
            );

            Plugin.Log.LogInfo(
                $"GRADING SAVE | " +
                $"Jobs={GradingManager.Jobs.Count} | " +
                $"Day={GradingDayManager.CurrentDay} | " +
                $"Save={saveName} | " +
                $"Identity={saveIdentity} | " +
                $"Path={path}"
            );
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"GRADING SAVE | " +
                $"Failed to save grading data: {ex}"
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

        string directory =
            Path.Combine(
                Paths.ConfigPath,
                "LethalCards"
            );

        Directory.CreateDirectory(
            directory
        );

        return Path.Combine(
            directory,
            $"{safeName}_{safeIdentity}_grading.txt"
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