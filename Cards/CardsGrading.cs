using System;

namespace LethalCards.Cards;

public static class CardGrading
{
    private static readonly Random Random = new();

    public static int RollGrade()
    {
        double roll = Random.NextDouble();

        // 5% chance for a perfect 10.
        if (roll < 0.05)
            return 10;

        // Remaining 95% is distributed evenly across grades 1-9.
        return Random.Next(1, 10);
    }

    public static float GetMultiplier(int grade)
    {
        return grade switch
        {
            1 => 0.0f,
            2 => 0.2f,
            3 => 0.35f,
            4 => 0.5f,
            5 => 0.65f,
            6 => 0.8f,
            7 => 1.0f,
            8 => 1.5f,
            9 => 2.5f,
            10 => 5.0f,
            _ => 1.0f
        };
    }

    public static int CalculateGradedValue(
        int ungradedValue,
        int grade)
    {
        if (grade <= 0)
            return ungradedValue;

        return
            (int)Math.Round(
                ungradedValue *
                GetMultiplier(grade)
            );
    }
}