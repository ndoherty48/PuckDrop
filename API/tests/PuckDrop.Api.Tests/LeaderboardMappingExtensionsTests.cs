using PuckDrop.Api.Mappings;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Api.Tests;

public class LeaderboardMappingExtensionsTests
{
    [Fact]
    public void ToResponse_CarriesRankSharingAndGameDaysPlayed()
    {
        var response = new LeaderboardEntry
        {
            UserId = "sam",
            SeasonId = "2025-26",
            DisplayName = "Sam",
            EarnedPoints = 2,
            TotalAnswered = 4,
            GameDaysPlayed = 4,
            Rank = 3,
            IsRankShared = true
        }.ToResponse();

        Assert.Equal(3, response.Rank);
        Assert.True(response.IsRankShared);
        Assert.Equal(4, response.GameDaysPlayed);
    }
}
