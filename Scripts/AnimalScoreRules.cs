// Per-round animal score is derived from the stable placed count, not from
// animation callbacks. Reloading a save cannot award the same animal twice.
public static class AnimalScoreRules
{
    public const int FirstAnimal = 446;
    public const int Increment = 98;

    public static int RewardFor(int ordinal) =>
        ordinal >= 1 && ordinal <= 10 ? FirstAnimal + (ordinal - 1) * Increment : 0;

    public static int TotalFor(int placedCount)
    {
        if (placedCount < 0 || placedCount > 10) return 0;
        return placedCount * FirstAnimal + Increment * placedCount * (placedCount - 1) / 2;
    }
}
