using System;

namespace LethalCards.Cards;

public static class CardGrading
{
    private static readonly Random Random = new();

    public static int RollGrade()
    {
        return BalanceConfig.GradeChances.Roll(Random);
    }

    public static float GetMultiplier(int grade)
    {
        return BalanceConfig.GetGradeMultiplier(grade);
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
