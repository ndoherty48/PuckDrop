using PuckDrop.Api.Mappings;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Standings;
using Xunit;

namespace PuckDrop.Api.Tests;

public class LeaderboardMappingExtensionsTests
{
    private static LeaderboardEntry BuildEntry(TieBreak? tieBreak) => new()
    {
        UserId = "sam",
        SeasonId = "2025-26",
        DisplayName = "Sam",
        EarnedPoints = 2,
        TotalAnswered = 4,
        GameDaysPlayed = 4,
        Rank = 3,
        IsRankShared = true,
        TieBreak = tieBreak
    };

    [Fact]
    public void ToResponse_CarriesRankSharingAndGameDaysPlayed()
    {
        var response = BuildEntry(tieBreak: null).ToResponse();

        Assert.Equal(3, response.Rank);
        Assert.True(response.IsRankShared);
        Assert.Equal(4, response.GameDaysPlayed);
        Assert.Null(response.TieBreak);
    }

    [Fact]
    public void ToResponse_CarriesTheTieBreak()
    {
        var response = BuildEntry(new TieBreak(
            [new TieBreakRival("nick", "Nick", 4)],
            SplitOnGameDaysPlayed: false,
            HeadToHead: new HeadToHeadRecord(Won: 2, Lost: 1, Drawn: 1),
            SplitOnHeadToHead: true)).ToResponse();

        var tieBreak = Assert.IsType<Contracts.LeaderboardTieBreakResponse>(response.TieBreak);
        var rival = Assert.Single(tieBreak.LevelOnPointsWith);
        Assert.Equal(("nick", "Nick", 4), (rival.UserId, rival.DisplayName, rival.GameDaysPlayed));
        Assert.False(tieBreak.SplitOnGameDaysPlayed);
        Assert.True(tieBreak.SplitOnHeadToHead);
        Assert.Equal(new Contracts.LeaderboardHeadToHeadResponse(2, 1, 1), tieBreak.HeadToHead);
    }

    [Fact]
    public void ToResponse_NoHeadToHead_MapsToNull()
    {
        var response = BuildEntry(new TieBreak(
            [new TieBreakRival("nick", "Nick", 6)], true, null, false)).ToResponse();

        Assert.Null(response.TieBreak!.HeadToHead);
    }
}
