namespace LethalCards.Grading;

public static class GradingDayManager
{
    private static int currentDay = 0;

    public static int CurrentDay =>
        currentDay;

    public static void AdvanceDay()
    {
        currentDay++;

        Plugin.Log.LogInfo(
            $"GRADING DAY ADVANCED | " +
            $"CurrentDay={currentDay}"
        );
    }

    public static void SetDay(
        int day)
    {
        currentDay =
            System.Math.Max(
                0,
                day
            );

        Plugin.Log.LogInfo(
            $"GRADING DAY SET | " +
            $"CurrentDay={currentDay}"
        );
    }
}