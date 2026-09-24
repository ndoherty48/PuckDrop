using PuckDrop.Web.Display;
using Xunit;

namespace PuckDrop.Web.Tests;

public class DisplayTextTests
{
    [Theory]
    [InlineData("Sam Rafferty", "SR")]
    [InlineData("Admin User", "AU")]
    [InlineData("friend", "F")]
    [InlineData("  ciara   de  burca ", "CB")]
    [InlineData("sam.rafferty@example.com", "SR")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initials_UsesFirstAndLastNameParts(string? name, string expected)
    {
        Assert.Equal(expected, DisplayText.Initials(name));
    }

    [Theory]
    [InlineData(-5, "Closed")]
    [InlineData(0, "Closed")]
    [InlineData(1, "In 1 minute")]
    [InlineData(30, "In 30 minutes")]
    [InlineData(60, "In 1 hour")]
    [InlineData(3 * 60 + 10, "In 3 hours")]
    [InlineData(24 * 60, "In 1 day")]
    [InlineData(5 * 24 * 60 + 60, "In 5 days")]
    public void TimeUntilDeadline_RoundsDownToTheLargestWholeUnit(int minutesUntilDeadline, string expected)
    {
        var now = new DateTime(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, DisplayText.TimeUntilDeadline(now.AddMinutes(minutesUntilDeadline), now));
    }

    [Fact]
    public void LocalDeadline_ConvertsFromUtc_ToTheGivenTimeZone_BST()
    {
        // 18:00 UTC on 23 Sep 2026 is 19:00 in Europe/London - British Summer Time (UTC+1).
        var utcDeadline = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        Assert.Equal("Wed 23 Sep, 19:00", DisplayText.LocalDeadline(utcDeadline, london));
    }

    [Fact]
    public void LocalDeadline_ConvertsFromUtc_ToTheGivenTimeZone_GMT()
    {
        // Outside BST, Europe/London is just UTC - no shift.
        var utcDeadline = new DateTime(2026, 1, 15, 19, 0, 0, DateTimeKind.Utc);
        var london = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

        Assert.Equal("Thu 15 Jan, 19:00", DisplayText.LocalDeadline(utcDeadline, london));
    }

    [Fact]
    public void LocalDeadline_ConvertsFromUtc_ToAFixedOffsetZone()
    {
        var utcDeadline = new DateTime(2026, 9, 23, 18, 0, 0, DateTimeKind.Utc);
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // PDT (UTC-7) in September.
        Assert.Equal("Wed 23 Sep, 11:00", DisplayText.LocalDeadline(utcDeadline, pacific));
    }
}
