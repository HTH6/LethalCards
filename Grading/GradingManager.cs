using System.Collections.Generic;
using LethalCards.Cards;

namespace LethalCards.Grading;

public static class GradingManager
{
    //public const int GradingCostPerCard = 10;
    public static int GradingTurnaroundDays => BalanceConfig.GradingTurnaroundDays;

    // ============================================================
    // TEMPORARY TEST SETTINGS
    // ============================================================

    // Temporary test override; normal gameplay should set this to false.
    public static readonly bool InstantGradingForTesting = false;

    public static int GradingCostPerCard => BalanceConfig.GradingCost;

    // ============================================================


    private static readonly List<GradingJob> PendingJobs = new();

    public static IReadOnlyList<GradingJob> Jobs =>
        PendingJobs;

    public static GradingJob CreateJob(
        CardInstanceData card,
        int currentDay)
    {
        int grade = CardGrading.RollGrade();

        GradingJob job =
            new GradingJob
            {
                JobId = System.Guid
                    .NewGuid()
                    .ToString("N"),

                CardId = card.CardId,
                Variant = card.Variant,
                Grade = grade,
                SubmittedDay = currentDay,
                ReadyDay = InstantGradingForTesting
                    ? currentDay
                    : currentDay + GradingTurnaroundDays
            };

        job.EnsureFinalValue();
        PendingJobs.Add(job);

        Plugin.Log.LogInfo(
            $"GRADING JOB CREATED | " +
            $"CardId={job.CardId} | " +
            $"Variant={job.Variant} | " +
            $"ResultStored=True | " +
            $"SubmittedDay={job.SubmittedDay} | " +
            $"ReadyDay={job.ReadyDay}"
        );

        GradingSaveManager.Save();

        return job;
    }

    public static GradingJob? GetJobById(
        string jobId)
    {
        if (string.IsNullOrEmpty(jobId))
            return null;

        foreach (GradingJob job in PendingJobs)
        {
            if (job.JobId == jobId)
                return job;
        }

        return null;
    }

    public static bool ClaimJob(
        string jobId)
        {
            GradingJob? job =
                GetJobById(jobId);

            if (job == null)
            {
                Plugin.Log.LogWarning(
                    $"GRADING CLAIM FAILED | " +
                    $"JobId={jobId} | " +
                    $"Reason=Job not found"
                );

                return false;
            }

            PendingJobs.Remove(job);

            Plugin.Log.LogInfo(
                $"GRADING JOB COLLECTED | RemovedFromActiveStatus=True | " +
                $"JobId={job.JobId} | " +
                $"CardId={job.CardId} | " +
                $"Grade={job.Grade}"
            );

            GradingSaveManager.Save();

            return true;
        }

    public static IEnumerable<GradingJob> GetReadyJobs(
        int currentDay)
    {
        foreach (GradingJob job in PendingJobs)
        {
            if (currentDay >= job.ReadyDay)
                yield return job;
        }
    }

    public static void AddLoadedJob(
    GradingJob job)
    {
        if (job == null)
            return;

        PendingJobs.Add(
            job
        );
    }

    public static void RemoveJob(
        GradingJob job)
    {
        if (
            PendingJobs.Remove(
                job
            ))
        {
            GradingSaveManager.Save();
        }
    }

    public static void Clear()
    {
        PendingJobs.Clear();
    }
}
