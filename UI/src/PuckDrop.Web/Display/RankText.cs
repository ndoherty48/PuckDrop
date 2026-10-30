namespace PuckDrop.Web.Display;

/// <summary>
/// Words for a standings position, where a rank the tie-break couldn't split is shown as "=3".
/// </summary>
public static class RankText
{
    /// <summary>
    /// "3", or "=3" when another player holds the same rank.
    /// </summary>
    public static string Rank(int rank, bool isShared) => isShared ? $"={rank}" : rank.ToString();

    /// <summary>
    /// "Tied 3rd" - what a screen reader says instead of "=3", which it would read as "equals 3".
    /// </summary>
    public static string SharedRankForScreenReader(int rank) => $"Tied {Ordinal(rank)}";

    private static string Ordinal(int number)
    {
        var suffix = (number % 100) is >= 11 and <= 13
            ? "th"
            : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        return $"{number}{suffix}";
    }
}
