using PuckDrop.Web.Display;
using PuckDrop.Web.Services;
using PuckDrop.Web.Tests.TestSupport;
using Xunit;

namespace PuckDrop.Web.Tests;

public class TieBreakTextTests
{
    private static LeaderboardRivalModel Rival(string name, int gameDays) => new(name.ToLowerInvariant(), name, gameDays);

    [Theory]
    [InlineData(3, false, "3")]
    [InlineData(3, true, "=3")]
    public void Rank_PrefixesASharedRankWithEquals(int rank, bool shared, string expected) =>
        Assert.Equal(expected, TieBreakText.Rank(rank, shared));

    [Theory]
    [InlineData(1, "Tied 1st")]
    [InlineData(2, "Tied 2nd")]
    [InlineData(3, "Tied 3rd")]
    [InlineData(4, "Tied 4th")]
    [InlineData(11, "Tied 11th")]
    [InlineData(12, "Tied 12th")]
    [InlineData(13, "Tied 13th")]
    [InlineData(21, "Tied 21st")]
    [InlineData(112, "Tied 112th")]
    public void SharedRankForScreenReader_UsesAnOrdinal(int rank, string expected) =>
        Assert.Equal(expected, TieBreakText.SharedRankForScreenReader(rank));

    [Fact]
    public void Note_NoOneLevelOnPoints_IsNull()
    {
        Assert.Null(TieBreakText.Note(LeaderboardEntries.Entry("u1", "Sam", 5, 5, 1)));
    }

    [Fact]
    public void Note_TieBreakThatSplitNoOne_IsNull()
    {
        // The "=1" already says it; a note would only repeat it.
        var entry = LeaderboardEntries.Entry("u1", "Sam", 5, 5, 1, isRankShared: true,
            tieBreak: new([Rival("Nick", 5)], false, new(1, 1, 3), false));

        Assert.Null(TieBreakText.Note(entry));
    }

    [Fact]
    public void Note_SplitOnGameDaysPlayed_ShowsBothCounts()
    {
        var entry = LeaderboardEntries.Entry("u1", "Sam", 6, 10, 1, gameDaysPlayed: 10,
            tieBreak: new([Rival("Nick", 8)], true, null, false));

        Assert.Equal("Level on points with Nick · split on game days played (10 v 8)", TieBreakText.Note(entry));
    }

    [Fact]
    public void Note_SplitOnGameDaysThenHeadToHead_NamesBothSteps()
    {
        // Level on points with two: one separated by game days, the other only by head-to-head.
        var entry = LeaderboardEntries.Entry("u1", "Sam", 6, 8, 2, gameDaysPlayed: 8,
            tieBreak: new([Rival("Nick", 10), Rival("Dave", 8)], true, new(1, 0, 2), true));

        Assert.Equal(
            "Level on points with Nick and Dave · split on game days played (8 v 10, 8), then head-to-head (1–0)",
            TieBreakText.Note(entry));
    }
}
