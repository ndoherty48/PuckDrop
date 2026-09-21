using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using Xunit;

namespace PuckDrop.Domain.Tests;

public class GameDayPollTests
{
    private static GameDayPoll CreatePoll(DateTime deadline) => new()
    {
        PollId = "poll-1",
        SeasonId = "2025-26",
        GameDate = DateOnly.FromDateTime(deadline),
        Title = "Belfast Giants vs Sheffield Steelers",
        Deadline = deadline,
        CreatedBy = "admin-user"
    };

    private static readonly DateTime FutureDeadline = DateTime.UtcNow.AddDays(1);
    private static readonly DateTime PastDeadline = DateTime.UtcNow.AddDays(-1);

    // ─── Publish ────────────────────────────────────────────────────────────

    [Fact]
    public void Publish_FromDraft_TransitionsToOpen()
    {
        var poll = CreatePoll(FutureDeadline);

        poll.Publish();

        Assert.Equal(PollStatus.Open, poll.Status);
    }

    [Fact]
    public void Publish_FromOpen_Throws()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();

        Assert.Throws<InvalidOperationException>(poll.Publish);
    }

    [Fact]
    public void Publish_FromClosed_Throws()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();
        poll.Close();

        Assert.Throws<InvalidOperationException>(poll.Publish);
    }

    [Fact]
    public void Publish_FromScored_Throws()
    {
        var poll = CreatePoll(PastDeadline);
        poll.Publish();
        poll.MarkScored(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(poll.Publish);
    }

    // ─── Close ──────────────────────────────────────────────────────────────

    [Fact]
    public void Close_FromOpen_TransitionsToClosed()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();

        poll.Close();

        Assert.Equal(PollStatus.Closed, poll.Status);
    }

    [Fact]
    public void Close_FromDraft_Throws()
    {
        var poll = CreatePoll(FutureDeadline);

        Assert.Throws<InvalidOperationException>(poll.Close);
    }

    [Fact]
    public void Close_FromClosed_Throws()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();
        poll.Close();

        Assert.Throws<InvalidOperationException>(poll.Close);
    }

    [Fact]
    public void Close_FromScored_Throws()
    {
        var poll = CreatePoll(PastDeadline);
        poll.Publish();
        poll.MarkScored(DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(poll.Close);
    }

    // ─── MarkScored ─────────────────────────────────────────────────────────

    [Fact]
    public void MarkScored_FromClosed_TransitionsToScored()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();
        poll.Close();

        poll.MarkScored(DateTime.UtcNow);

        Assert.Equal(PollStatus.Scored, poll.Status);
    }

    [Fact]
    public void MarkScored_FromOpenWithDeadlinePassed_TransitionsToScored()
    {
        var poll = CreatePoll(PastDeadline);
        poll.Publish();

        poll.MarkScored(DateTime.UtcNow);

        Assert.Equal(PollStatus.Scored, poll.Status);
    }

    [Fact]
    public void MarkScored_FromOpenWithDeadlineExactlyNow_TransitionsToScored()
    {
        // MarkScored's guard is `utcNow >= Deadline` (inclusive), unlike IsAcceptingAnswers'
        // strict `<` - exercise the boundary explicitly so a future refactor can't silently
        // flip one without the other.
        var deadline = DateTime.UtcNow;
        var poll = CreatePoll(deadline);
        poll.Publish();

        poll.MarkScored(deadline);

        Assert.Equal(PollStatus.Scored, poll.Status);
    }

    [Fact]
    public void MarkScored_FromOpenWithDeadlineNotPassed_Throws()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();

        Assert.Throws<InvalidOperationException>(() => poll.MarkScored(DateTime.UtcNow));
    }

    [Fact]
    public void MarkScored_FromDraft_Throws()
    {
        var poll = CreatePoll(PastDeadline);

        Assert.Throws<InvalidOperationException>(() => poll.MarkScored(DateTime.UtcNow));
    }

    [Fact]
    public void MarkScored_FromScored_StaysScored_BecauseThatIsARescore()
    {
        // This used to throw, purely to stop the old running-sum leaderboard double-counting.
        // Totals are folded from facts now, so re-scoring overwrites and the guard is gone -
        // which is what lets an admin fix a wrong correct option after the fact.
        var poll = CreatePoll(PastDeadline);
        poll.Publish();
        poll.MarkScored(DateTime.UtcNow);

        poll.MarkScored(DateTime.UtcNow);

        Assert.Equal(PollStatus.Scored, poll.Status);
    }

    [Fact]
    public void EnsureCanBeScored_FromScored_DoesNotThrow()
    {
        var poll = CreatePoll(PastDeadline);
        poll.Publish();
        poll.MarkScored(DateTime.UtcNow);

        poll.EnsureCanBeScored(DateTime.UtcNow);

        Assert.Equal(PollStatus.Scored, poll.Status);
    }

    // ─── EnsureCanBeScored ──────────────────────────────────────────────────

    [Fact]
    public void EnsureCanBeScored_FromClosed_LeavesStatusUnchanged()
    {
        // The whole point of splitting this out of MarkScored: validating must not transition the
        // poll, so scoring can check up front and apply the status only once every write has landed.
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();
        poll.Close();

        poll.EnsureCanBeScored(DateTime.UtcNow);

        Assert.Equal(PollStatus.Closed, poll.Status);
    }

    [Fact]
    public void EnsureCanBeScored_FromOpenWithDeadlinePassed_LeavesStatusUnchanged()
    {
        var poll = CreatePoll(PastDeadline);
        poll.Publish();

        poll.EnsureCanBeScored(DateTime.UtcNow);

        Assert.Equal(PollStatus.Open, poll.Status);
    }

    [Fact]
    public void EnsureCanBeScored_FromDraft_Throws()
    {
        var poll = CreatePoll(PastDeadline);

        Assert.Throws<InvalidOperationException>(() => poll.EnsureCanBeScored(DateTime.UtcNow));
    }

    [Fact]
    public void EnsureCanBeScored_FromOpenWithDeadlineNotPassed_Throws()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();

        Assert.Throws<InvalidOperationException>(() => poll.EnsureCanBeScored(DateTime.UtcNow));
    }

    // ─── IsAcceptingAnswers ─────────────────────────────────────────────────

    [Fact]
    public void IsAcceptingAnswers_OpenBeforeDeadline_ReturnsTrue()
    {
        var poll = CreatePoll(FutureDeadline);
        poll.Publish();

        Assert.True(poll.IsAcceptingAnswers(DateTime.UtcNow));
    }

    [Fact]
    public void IsAcceptingAnswers_OpenExactlyAtDeadline_ReturnsFalse()
    {
        // Strict `<` - the exact-equality boundary is the opposite of MarkScored's `>=`.
        var deadline = DateTime.UtcNow;
        var poll = CreatePoll(deadline);
        poll.Publish();

        Assert.False(poll.IsAcceptingAnswers(deadline));
    }

    [Fact]
    public void IsAcceptingAnswers_OpenAfterDeadline_ReturnsFalse()
    {
        var poll = CreatePoll(PastDeadline);
        poll.Publish();

        Assert.False(poll.IsAcceptingAnswers(DateTime.UtcNow));
    }

    [Fact]
    public void IsAcceptingAnswers_DraftBeforeDeadline_ReturnsFalse()
    {
        var poll = CreatePoll(FutureDeadline);

        Assert.False(poll.IsAcceptingAnswers(DateTime.UtcNow));
    }
}
