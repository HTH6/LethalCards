using UnityEngine;

namespace LethalCards.Grading;

public class GradingReturnData :
    MonoBehaviour
{
    public string JobId = "";

    private bool claimed;

    public void Initialize(
        string jobId)
    {
        JobId = jobId;
        claimed = false;

        Plugin.Log.LogInfo(
            $"GRADING RETURN DATA INITIALIZED | " +
            $"JobId={JobId}"
        );
    }

    public void TryClaim()
    {
        if (claimed)
            return;

        if (string.IsNullOrEmpty(JobId))
            return;

        if (
            Unity.Netcode
                .NetworkManager.Singleton ==
            null ||
            !Unity.Netcode
                .NetworkManager.Singleton
                .IsServer)
        {
            return;
        }

        if (
            GradingManager
                .ClaimJob(JobId))
        {
            claimed = true;

            Plugin.Log.LogInfo(
                $"GRADING RETURN CLAIMED | " +
                $"JobId={JobId}"
            );
        }
    }
}