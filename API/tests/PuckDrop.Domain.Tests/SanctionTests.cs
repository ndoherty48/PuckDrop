using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Domain.Tests;

/// <summary>
/// Guards on the two admin sanctions. Both reasons are shown publicly on the leaderboard, so a
/// blank one would render as an unexplained penalty next to a player's name.
/// </summary>
public class SanctionTests
{
    private const string SeasonId = "2025-26";

    private static PointAdjustment CreateAdjustment(int points = -5, string reason = "Picked after puck drop") =>
        PointAdjustment.Create(SeasonId, "adj-1", "user-1", "Nathan", points, reason, "admin-user");

    private static PollVoid CreateVoid(string reason = "No-show") =>
        PollVoid.Create(SeasonId, "poll-1", "user-1", "Giants vs Steelers", reason, "admin-user");

    // ─── PointAdjustment ────────────────────────────────────────────────────

    [Fact]
    public void Adjustment_NegativePoints_IsAllowed()
    {
        Assert.Equal(-5, CreateAdjustment(points: -5).Points);
    }

    [Fact]
    public void Adjustment_PositivePoints_IsAllowed()
    {
        Assert.Equal(3, CreateAdjustment(points: 3).Points);
    }

    [Fact]
    public void Adjustment_ZeroPoints_Throws()
    {
        // An adjustment that changes nothing is a mistake, and would show a meaningless "0" penalty.
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateAdjustment(points: 0));
    }

    [Fact]
    public void Adjustment_BeyondTheSanityBound_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateAdjustment(points: PointAdjustment.MaxAbsolutePoints + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateAdjustment(points: -(PointAdjustment.MaxAbsolutePoints + 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Adjustment_BlankReason_Throws(string reason)
    {
        Assert.Throws<ArgumentException>(() => CreateAdjustment(reason: reason));
    }

    [Fact]
    public void Adjustment_OverlongReason_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => CreateAdjustment(reason: new string('x', SanctionReason.MaxLength + 1)));
    }

    [Fact]
    public void Adjustment_Reason_IsTrimmed()
    {
        Assert.Equal("Picked after puck drop", CreateAdjustment(reason: "  Picked after puck drop  ").Reason);
    }

    // ─── PollVoid ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Void_BlankReason_Throws(string reason)
    {
        Assert.Throws<ArgumentException>(() => CreateVoid(reason));
    }

    [Fact]
    public void Void_OverlongReason_Throws()
    {
        Assert.Throws<ArgumentException>(() => CreateVoid(new string('x', SanctionReason.MaxLength + 1)));
    }

    [Fact]
    public void Void_Reason_IsTrimmed()
    {
        Assert.Equal("No-show", CreateVoid("  No-show  ").Reason);
    }

    [Fact]
    public void Void_ReasonAtExactlyTheLimit_IsAllowed()
    {
        var reason = new string('x', SanctionReason.MaxLength);

        Assert.Equal(reason, CreateVoid(reason).Reason);
    }
}
