using NSubstitute;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class PollVoidServiceTests
{
    private const string PollId = "poll-1";
    private const string SeasonId = "2025-26";
    private const string UserId = "user-1";
    private const string AdminId = "admin-user";

    private sealed class Fixture
    {
        public required PollVoidService Service { get; init; }
        public required ILeaderboardRepository LeaderboardRepository { get; init; }
    }

    private static GameDayPoll BuildPoll(bool closed = true, bool scored = false)
    {
        var poll = new GameDayPoll
        {
            PollId = PollId,
            SeasonId = SeasonId,
            GameDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Belfast Giants vs Sheffield Steelers",
            Deadline = DateTime.UtcNow.AddDays(-1),
            CreatedBy = AdminId
        };
        poll.Publish();
        if (closed || scored) poll.Close();
        if (scored) poll.MarkScored(DateTime.UtcNow);
        return poll;
    }

    private static Fixture CreateFixture(GameDayPoll? poll, bool userHasAnswers = true)
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetByIdAsync(PollId, Arg.Any<CancellationToken>()).Returns(poll);

        var answerRepository = Substitute.For<IUserAnswerRepository>();
        answerRepository.GetUserAnswersAsync(UserId, PollId, Arg.Any<CancellationToken>()).Returns(
            userHasAnswers
                ? [new UserAnswer
                    {
                        UserId = UserId, DisplayName = "Nathan", PollId = PollId,
                        QuestionId = "q1", SelectedOptionId = "o1"
                    }]
                : []);

        var leaderboardRepository = Substitute.For<ILeaderboardRepository>();

        return new Fixture
        {
            Service = new PollVoidService(pollRepository, answerRepository, leaderboardRepository),
            LeaderboardRepository = leaderboardRepository
        };
    }

    // ─── Voiding ────────────────────────────────────────────────────────────

    [Fact]
    public async Task VoidPicksAsync_ClosedPoll_RecordsTheVoidWithItsReasonAndPollTitle()
    {
        var fixture = CreateFixture(BuildPoll());

        await fixture.Service.VoidPicksAsync(
            PollId, UserId, "Picked after puck drop", AdminId, TestContext.Current.CancellationToken);

        await fixture.LeaderboardRepository.Received(1).SaveVoidAsync(
            Arg.Is<PollVoid>(v =>
                v.UserId == UserId &&
                v.PollId == PollId &&
                v.SeasonId == SeasonId &&
                v.Reason == "Picked after puck drop" &&
                v.PollTitle == "Belfast Giants vs Sheffield Steelers" &&
                v.VoidedBy == AdminId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoidPicksAsync_AlreadyScoredPoll_IsAllowed()
    {
        var fixture = CreateFixture(BuildPoll(scored: true));

        await fixture.Service.VoidPicksAsync(PollId, UserId, "No-show", AdminId, TestContext.Current.CancellationToken);

        await fixture.LeaderboardRepository.Received(1).SaveVoidAsync(Arg.Any<PollVoid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoidPicksAsync_VotingStillOpen_Throws()
    {
        // Nothing is settled while picks can still change, so there is nothing to void yet.
        var open = BuildPoll(closed: false);
        var fixture = CreateFixture(open);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.VoidPicksAsync(PollId, UserId, "No-show", AdminId, TestContext.Current.CancellationToken));

        await fixture.LeaderboardRepository.DidNotReceive().SaveVoidAsync(Arg.Any<PollVoid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoidPicksAsync_PlayerMadeNoPicks_Throws()
    {
        var fixture = CreateFixture(BuildPoll(), userHasAnswers: false);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.VoidPicksAsync(PollId, UserId, "No-show", AdminId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VoidPicksAsync_PollNotFound_Throws()
    {
        var fixture = CreateFixture(poll: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.VoidPicksAsync(PollId, UserId, "No-show", AdminId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VoidPicksAsync_BlankReason_Throws()
    {
        // The reason is shown publicly beside the player's name - a blank one explains nothing.
        var fixture = CreateFixture(BuildPoll());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.VoidPicksAsync(PollId, UserId, "   ", AdminId, TestContext.Current.CancellationToken));

        await fixture.LeaderboardRepository.DidNotReceive().SaveVoidAsync(Arg.Any<PollVoid>(), Arg.Any<CancellationToken>());
    }

    // ─── Restoring ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RestorePicksAsync_RemovesTheVoid()
    {
        // Undo is a single delete: with totals folded from facts there is no delta to reverse, so
        // the player's total returns to exactly what it was.
        var fixture = CreateFixture(BuildPoll(scored: true));

        await fixture.Service.RestorePicksAsync(PollId, UserId, TestContext.Current.CancellationToken);

        await fixture.LeaderboardRepository.Received(1)
            .DeleteVoidAsync(SeasonId, UserId, PollId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestorePicksAsync_NothingWasVoided_PropagatesNotFound()
    {
        // The repository deletes conditionally, so undoing a void that isn't there is a 404 rather
        // than a success that changed nothing.
        var fixture = CreateFixture(BuildPoll(scored: true));
        fixture.LeaderboardRepository
            .DeleteVoidAsync(SeasonId, UserId, PollId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new KeyNotFoundException("No voided picks found.")));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.RestorePicksAsync(PollId, UserId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RestorePicksAsync_PollNotFound_Throws()
    {
        var fixture = CreateFixture(poll: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.RestorePicksAsync(PollId, UserId, TestContext.Current.CancellationToken));
    }
}
