using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Domain.Tests;

/// <summary>
/// The entry's own computed properties. How the numbers get here is
/// <see cref="SeasonStandingsTests"/>' job - this file only pins the arithmetic on top of them.
/// </summary>
public class LeaderboardEntryTests
{
    private static LeaderboardEntry CreateEntry(int earned = 0, int adjustment = 0, int answered = 0) => new()
    {
        UserId = "user-1",
        SeasonId = "2025-26",
        DisplayName = "Nathan",
        EarnedPoints = earned,
        AdjustmentPoints = adjustment,
        TotalAnswered = answered
    };

    // ─── TotalPoints ────────────────────────────────────────────────────────

    [Fact]
    public void TotalPoints_NoAdjustments_IsJustWhatWasEarned()
    {
        Assert.Equal(7, CreateEntry(earned: 7).TotalPoints);
    }

    [Fact]
    public void TotalPoints_IncludesAdjustments()
    {
        Assert.Equal(5, CreateEntry(earned: 7, adjustment: -2).TotalPoints);
    }

    [Fact]
    public void TotalPoints_DeductionBeyondPointsEarned_GoesNegative()
    {
        // Deliberately not clamped at zero: the leaderboard shows the penalty and the total side by
        // side, so clamping would leave arithmetic on screen that doesn't add up.
        Assert.Equal(-3, CreateEntry(earned: 2, adjustment: -5).TotalPoints);
    }

    // ─── Accuracy ───────────────────────────────────────────────────────────

    [Fact]
    public void Accuracy_NoAnswersYet_ReturnsZero_NotDivideByZero()
    {
        Assert.Equal(0, CreateEntry().Accuracy);
    }

    [Fact]
    public void Accuracy_RoundsToOneDecimalPlace()
    {
        Assert.Equal(33.3, CreateEntry(earned: 1, answered: 3).Accuracy);
    }

    [Fact]
    public void Accuracy_AllCorrect_ReturnsOneHundred()
    {
        Assert.Equal(100, CreateEntry(earned: 4, answered: 4).Accuracy);
    }

    [Fact]
    public void Accuracy_IgnoresAdjustments()
    {
        // A deduction is a sanction, not a wrong answer - it must not rewrite someone's hit rate,
        // and must never be able to produce a negative percentage.
        var entry = CreateEntry(earned: 2, adjustment: -10, answered: 4);

        Assert.Equal(50, entry.Accuracy);
    }

    // ─── Display collections ────────────────────────────────────────────────

    [Fact]
    public void Adjustments_And_Voids_DefaultToEmpty_NotNull()
    {
        var entry = CreateEntry();

        Assert.Empty(entry.Adjustments);
        Assert.Empty(entry.Voids);
    }
}
