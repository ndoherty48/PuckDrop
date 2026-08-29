using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Domain.Tests;

public class SeasonTests
{
    [Theory]
    [InlineData(2026, 7, 31, "2025-26")] // last day of the season that started Aug 2025
    [InlineData(2026, 8, 1, "2026-27")] // first day of the next season
    [InlineData(2025, 12, 31, "2025-26")]
    [InlineData(2026, 1, 1, "2025-26")]
    public void DeriveSeasonId_AtSeasonBoundaries_ReturnsExpectedId(int year, int month, int day, string expected)
    {
        var gameDate = new DateOnly(year, month, day);

        Assert.Equal(expected, Season.DeriveSeasonId(gameDate));
    }

    [Fact]
    public void DeriveSeasonId_YearRollover_FormatsEndYearAsTwoDigits()
    {
        // startYear 2099 -> endYear 2100 -> "2100 % 100" == 0, formatted as "00", not "100".
        var gameDate = new DateOnly(2099, 8, 1);

        Assert.Equal("2099-00", Season.DeriveSeasonId(gameDate));
    }

    [Fact]
    public void CreateForDate_BeforeAugust_BuildsPreviousAugustToApril()
    {
        var season = Season.CreateForDate(new DateOnly(2026, 1, 15));

        Assert.Equal("2025-26", season.SeasonId);
        Assert.Equal("2025/26 Season", season.Name);
        Assert.Equal(new DateOnly(2025, 8, 1), season.StartDate);
        Assert.Equal(new DateOnly(2026, 4, 30), season.EndDate);
    }

    [Fact]
    public void CreateForDate_OnAugustFirst_BuildsThatAugustToNextApril()
    {
        var season = Season.CreateForDate(new DateOnly(2026, 8, 1));

        Assert.Equal("2026-27", season.SeasonId);
        Assert.Equal("2026/27 Season", season.Name);
        Assert.Equal(new DateOnly(2026, 8, 1), season.StartDate);
        Assert.Equal(new DateOnly(2027, 4, 30), season.EndDate);
    }
}
