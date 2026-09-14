using PuckDrop.Web.Layout;
using Xunit;

namespace PuckDrop.Web.Tests;

public class NavSectionsTests
{
    [Theory]
    [InlineData("", NavSection.Home)]
    [InlineData("poll/abc", NavSection.Home)]
    [InlineData("leaderboard", NavSection.Leaderboard)]
    [InlineData("Leaderboard/", NavSection.Leaderboard)]
    [InlineData("leaderboard?season=2026-27", NavSection.Leaderboard)]
    [InlineData("history", NavSection.History)]
    [InlineData("results/abc", NavSection.History)]
    [InlineData("admin/polls", NavSection.Admin)]
    [InlineData("admin/polls/abc/score", NavSection.Admin)]
    [InlineData("authentication/logged-out", NavSection.None)]
    [InlineData("pollster", NavSection.None)]
    public void FromRelativePath_MapsChildPagesToTheirSection(string relativePath, NavSection expected)
    {
        Assert.Equal(expected, NavSections.FromRelativePath(relativePath));
    }
}
