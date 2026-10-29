using PuckDrop.Web.Services;

namespace PuckDrop.Web.Display;

/// <summary>
/// Words for the leaderboard's tie-break: the "=3" rank and the note explaining what separated
/// players level on points.
/// </summary>
public static class TieBreakText
{
    /// <summary>
    /// "3", or "=3" when another player holds the same rank.
    /// </summary>
    public static string Rank(int rank, bool isShared) => isShared ? $"={rank}" : rank.ToString();

    /// <summary>
    /// "Tied 3rd" - what a screen reader says instead of "=3", which it would read as "equals 3".
    /// </summary>
    public static string SharedRankForScreenReader(int rank) => $"Tied {Ordinal(rank)}";

    /// <summary>
    /// "Level on points with Sam · split on game days played (10 v 8)", or
    /// "Level on points with Sam and Dave · split on head-to-head (2–1)". The same wording appears on
    /// every row involved, so the lower-placed player sees why too. Null when there's nothing to
    /// explain: no one else on the same total, or a tie-break that split no one, which the shared
    /// rank already says.
    /// </summary>
    public static string? Note(LeaderboardEntryModel entry)
    {
        if (entry.TieBreak is not { } tieBreak || (!tieBreak.SplitOnGameDaysPlayed && !tieBreak.SplitOnHeadToHead))
            return null;

        var rivals = tieBreak.LevelOnPointsWith;
        var steps = new List<string>();

        if (tieBreak.SplitOnGameDaysPlayed)
            steps.Add($"game days played ({entry.GameDaysPlayed} v {string.Join(", ", rivals.Select(r => r.GameDaysPlayed))})");

        if (tieBreak.SplitOnHeadToHead && tieBreak.HeadToHead is { } record)
            steps.Add($"head-to-head ({record.Won}–{record.Lost})");

        return $"Level on points with {DisplayText.NameList(rivals.Select(r => r.DisplayName).ToList())} · "
            + $"split on {string.Join(", then ", steps)}";
    }

    private static string Ordinal(int number)
    {
        var suffix = (number % 100) is >= 11 and <= 13
            ? "th"
            : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

        return $"{number}{suffix}";
    }
}
