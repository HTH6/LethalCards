using System;

namespace LethalCards.Save;

public static class SaveIdentityManager
{
    private const string IdentityKey =
        "LethalCards_SaveIdentity";

    private static string currentSaveIdentity =
        "";

    private static string currentSaveName =
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

        // Already loaded for this exact save.
        if (
            !string.IsNullOrWhiteSpace(
                currentSaveIdentity) &&
            currentSaveName == saveName)
        {
            return currentSaveIdentity;
        }

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

                currentSaveName =
                    saveName;

                /* Plugin.Log.LogInfo(
                    $"SAVE IDENTITY LOADED | " +
                    $"Save={saveName} | " +
                    $"Id={currentSaveIdentity}"
                ); */

                return currentSaveIdentity;
            }

            currentSaveIdentity =
                Guid.NewGuid()
                    .ToString("N");

            currentSaveName =
                saveName;

            ES3.Save(
                IdentityKey,
                currentSaveIdentity,
                saveName
            );

            Grading.GradingManager.Clear();
            Grading.GradingDayManager.SetDay(0);

            Cards.CollectionManager.Clear();
            Cards.CollectionSaveManager.ResetLoadedSave();

            /* Plugin.Log.LogInfo(
                $"SAVE IDENTITY RUNTIME RESET | " +
                $"Save={saveName} | " +
                $"Cleared grading jobs, grading day, and collection."
            ); */

            /* Plugin.Log.LogInfo(
                $"SAVE IDENTITY CREATED | " +
                $"Save={saveName} | " +
                $"Id={currentSaveIdentity}"
            ); */

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

        currentSaveName =
            "";
    }
}
