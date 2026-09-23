using PuckDrop.Application.Services;
using Xunit;

namespace PuckDrop.Application.Tests;

public class FixtureDescriptionParserTests
{
    [Fact]
    public void Parse_RealFeedShape_ExtractsCompetitionHomeAwayAndVenue()
    {
        var result = FixtureDescriptionParser.Parse(
            "CHL: Belfast Giants vs KalPa Kuopio @ The SSE Arena on 28/08/2025 19:00");

        Assert.NotNull(result);
        Assert.Equal("CHL", result.Competition);
        Assert.Equal("Belfast Giants", result.Home);
        Assert.Equal("KalPa Kuopio", result.Away);
        Assert.Equal("The SSE Arena", result.Venue);
    }

    [Fact]
    public void Parse_VenueWithAccentedCharacters_StillMatches()
    {
        var result = FixtureDescriptionParser.Parse(
            "CHL: Lausanne HC vs Belfast Giants @ Vaudoise Aréna on 05/09/2025 17:00");

        Assert.NotNull(result);
        Assert.Equal("Vaudoise Aréna", result.Venue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vs KalPa Kuopio")] // SUMMARY shape, not DESCRIPTION - no competition/venue
    [InlineData("Belfast Giants vs Sheffield Steelers")] // missing the "Competition:" prefix and venue
    public void Parse_UnexpectedShape_ReturnsNull_SoTheCallerFallsBackToSummary(string? description)
    {
        Assert.Null(FixtureDescriptionParser.Parse(description));
    }

    [Fact]
    public void Parse_NullDescription_ReturnsNull()
    {
        Assert.Null(FixtureDescriptionParser.Parse(null));
    }
}
