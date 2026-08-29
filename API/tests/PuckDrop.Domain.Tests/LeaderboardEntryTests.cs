using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Domain.Tests;

public class LeaderboardEntryTests
{
    private static LeaderboardEntry CreateEntry() => new()
    {
        UserId = "user-1",
        SeasonId = "2025-26",
        DisplayName = "Nathan"
    };

    // ─── Accuracy ───────────────────────────────────────────────────────────

    [Fact]
    public void Accuracy_NoAnswersYet_ReturnsZero_NotDivideByZero()
    {
        var entry = CreateEntry();

        Assert.Equal(0, entry.Accuracy);
    }

    [Fact]
    public void Accuracy_RoundsToOneDecimalPlace()
    {
        var entry = CreateEntry();

        entry.AddPollResults(correctAnswers: 1, totalAnswers: 3);

        Assert.Equal(33.3, entry.Accuracy);
    }

    [Fact]
    public void Accuracy_AllCorrect_ReturnsOneHundred()
    {
        var entry = CreateEntry();

        entry.AddPollResults(correctAnswers: 4, totalAnswers: 4);

        Assert.Equal(100, entry.Accuracy);
    }

    // ─── AddPollResults ─────────────────────────────────────────────────────

    [Fact]
    public void AddPollResults_NegativeCorrectAnswers_ThrowsArgumentOutOfRangeException()
    {
        var entry = CreateEntry();

        Assert.Throws<ArgumentOutOfRangeException>(() => entry.AddPollResults(-1, 3));
    }

    [Fact]
    public void AddPollResults_NegativeTotalAnswers_ThrowsArgumentOutOfRangeException()
    {
        var entry = CreateEntry();

        Assert.Throws<ArgumentOutOfRangeException>(() => entry.AddPollResults(0, -1));
    }

    [Fact]
    public void AddPollResults_CorrectExceedsTotal_ThrowsArgumentException()
    {
        // Distinct from the ArgumentOutOfRangeException cases above: PuckDrop.Api's
        // DomainExceptionFilter maps plain ArgumentException to 400, but
        // ArgumentOutOfRangeException isn't explicitly matched there and propagates unhandled -
        // the exact exception type here isn't an implementation detail, it's API-observable.
        var entry = CreateEntry();

        var ex = Assert.Throws<ArgumentException>(() => entry.AddPollResults(3, 2));
        Assert.IsNotType<ArgumentOutOfRangeException>(ex);
    }

    [Fact]
    public void AddPollResults_AccumulatesAcrossMultiplePolls()
    {
        var entry = CreateEntry();

        entry.AddPollResults(correctAnswers: 2, totalAnswers: 3);
        entry.AddPollResults(correctAnswers: 1, totalAnswers: 2);

        Assert.Equal(3, entry.TotalPoints);
        Assert.Equal(5, entry.TotalAnswered);
    }

    [Fact]
    public void AddPollResults_ZeroZero_IsANoOpAccumulate()
    {
        var entry = CreateEntry();
        entry.AddPollResults(2, 3);

        entry.AddPollResults(0, 0);

        Assert.Equal(2, entry.TotalPoints);
        Assert.Equal(3, entry.TotalAnswered);
    }
}
