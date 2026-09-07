using System;

namespace LethalCards.Save;

public static class SaveIdentityManager
{
    private const string IdentityKey =
        "LethalCards_SaveIdentity";

    private static string currentSaveIdentity =
        "";

    public static string CurrentSaveIdentity =>
        currentSaveIdentity;

    public static string GetOrCreateIdentity()
    {
        if (
            GameNetworkManager.Instance == null ||
            string.IsNullOrWhiteSpace(
                GameNetworkManager.Instance
                    .currentSaveFileName))
        {
            Plugin.Log.LogWarning(
                "SAVE IDENTITY | " +
                "No active save file."
            );

            return "";
        }

        string saveName =
            GameNetworkManager.Instance
                .currentSaveFileName;

        try
        {
            if (
                ES3.KeyExists(
                    IdentityKey,
                    saveName))
            {
                currentSaveIdentity =
                    ES3.Load<string>(
                        IdentityKey,
                        saveName
                    );

                Plugin.Log.LogInfo(
                    $"SAVE IDENTITY LOADED | " +
                    $"Save={saveName} | " +
                    $"Id={currentSaveIdentity}"
                );

                return currentSaveIdentity;
            }

            currentSaveIdentity =
                Guid.NewGuid()
                    .ToString("N");

            ES3.Save(
                IdentityKey,
                currentSaveIdentity,
                saveName
            );

            // A brand-new identity means this is a brand-new
            // Lethal Company save. Clear anything that may still
            // be in memory from the previously loaded save.
            Grading.GradingManager.Clear();
            Grading.GradingDayManager.SetDay(0);

            Cards.CollectionManager.Clear();
            Cards.CollectionSaveManager.ResetLoadedSave();

            Plugin.Log.LogInfo(
                $"SAVE IDENTITY RUNTIME RESET | " +
                $"Save={saveName} | " +
                $"Cleared grading jobs, grading day, and collection."
            );

            Plugin.Log.LogInfo(
                $"SAVE IDENTITY CREATED | " +
                $"Save={saveName} | " +
                $"Id={currentSaveIdentity}"
            );

            return currentSaveIdentity;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"SAVE IDENTITY ERROR | " +
                $"Save={saveName} | " +
                $"Exception={ex}"
            );

            return "";
        }
    }

    public static void ResetRuntimeState()
    {
        currentSaveIdentity =
            "";
    }
}