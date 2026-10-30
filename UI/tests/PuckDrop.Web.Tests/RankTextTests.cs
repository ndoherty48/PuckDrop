using PuckDrop.Web.Display;
using Xunit;

namespace PuckDrop.Web.Tests;

public class RankTextTests
{
    [Theory]
    [InlineData(3, false, "3")]
    [InlineData(3, true, "=3")]
    public void Rank_PrefixesASharedRankWithEquals(int rank, bool shared, string expected) =>
        Assert.Equal(expected, RankText.Rank(rank, shared));

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
        Assert.Equal(expected, RankText.SharedRankForScreenReader(rank));
}
