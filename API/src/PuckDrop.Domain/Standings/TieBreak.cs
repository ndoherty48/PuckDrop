namespace PuckDrop.Domain.Standings;

/// <summary>
/// How a player level on points with others was separated from them, for display next to their
/// row. Only present when they are level on points with someone.
/// </summary>
/// <remarks>
/// Structured rather than worded, so the UI owns the phrasing. A tie-break that split nobody (all
/// still level, sharing a rank) leaves both flags false.
/// </remarks>
/// <param name="LevelOnPointsWith">Everyone else on the same total, in standings order.</param>
/// <param name="SplitOnGameDaysPlayed">Whether game days played separated this player from any of them.</param>
/// <param name="HeadToHead">
/// This player's combined record in the mini-league against the players also level on game days
/// played. Null when no one else was level that far.
/// </param>
/// <param name="SplitOnHeadToHead">Whether head-to-head separated this player from any of them.</param>
public sealed record TieBreak(
    IReadOnlyList<TieBreakRival> LevelOnPointsWith,
    bool SplitOnGameDaysPlayed,
    HeadToHeadRecord? HeadToHead,
    bool SplitOnHeadToHead);

/// <summary>
/// Another player level on points, with the figure the first tie-break compares.
/// </summary>
public sealed record TieBreakRival(string UserId, string DisplayName, int GameDaysPlayed);

/// <summary>
/// Game days taken, lost and drawn against other players, over only the game days each pair both
/// played.
/// </summary>
public sealed record HeadToHeadRecord(int Won, int Lost, int Drawn)
{
    public static readonly HeadToHeadRecord None = new(0, 0, 0);

    /// <summary>
    /// What the mini-league ranks on. Net rather than raw wins, so a player who simply shared more
    /// game days with the others isn't ahead for that alone.
    /// </summary>
    public int Net => Won - Lost;

    public HeadToHeadRecord Plus(HeadToHeadRecord other) =>
        new(Won + other.Won, Lost + other.Lost, Drawn + other.Drawn);
}
