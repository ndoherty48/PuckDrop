using NSubstitute;
using PuckDrop.Application.Models;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using PuckDrop.Domain.Enums;
using Xunit;

namespace PuckDrop.Application.Tests;

public class PollServiceTests
{
    private const string PollId = "poll-1";

    private static GameDayPoll BuildPoll(PollStatus status)
    {
        var poll = new GameDayPoll
        {
            PollId = PollId,
            SeasonId = "2025-26",
            GameDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = "Belfast Giants vs Sheffield Steelers",
            Deadline = DateTime.UtcNow.AddDays(1),
            CreatedBy = "admin-user"
        };

        switch (status)
        {
            case PollStatus.Open: poll.Publish(); break;
            case PollStatus.Closed: poll.Publish(); poll.Close(); break;
            case PollStatus.Scored: poll.Publish(); poll.Close(); poll.MarkScored(DateTime.UtcNow); break;
        }

        return poll;
    }

    /// <param name="questionCount">
    /// How many questions the poll has. PublishPollAsync reads them to reject an empty poll.
    /// </param>
    private static (PollService Service, IPollRepository PollRepository, ISeasonRepository SeasonRepository) CreateService(
        GameDayPoll? poll, int questionCount = 1)
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetByIdAsync(PollId, Arg.Any<CancellationToken>()).Returns(poll);

        var questions = Enumerable.Range(1, questionCount)
            .Select(i => new Question { QuestionId = $"q-{i}", PollId = PollId, Text = $"Question {i}", SortOrder = i })
            .ToList();
        pollRepository.GetWithQuestionsAsync(PollId, Arg.Any<CancellationToken>())
            .Returns(poll is null ? null : new PollWithQuestions(poll, questions, []));

        var seasonRepository = Substitute.For<ISeasonRepository>();
        var seasonService = new SeasonService(seasonRepository);

        return (new PollService(pollRepository, seasonService), pollRepository, seasonRepository);
    }

    // ─── CreatePollAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePollAsync_EnsuresSeasonExistsForGameDate_AndSavesPollInDraft()
    {
        var (service, pollRepository, seasonRepository) = CreateService(poll: null);
        var gameDate = new DateOnly(2026, 1, 15);
        seasonRepository.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Season?)null);

        var poll = await service.CreatePollAsync("Title", gameDate, DateTime.UtcNow.AddDays(1), "admin-user", TestContext.Current.CancellationToken);

        Assert.Equal(PollStatus.Draft, poll.Status);
        Assert.Equal(Season.DeriveSeasonId(gameDate), poll.SeasonId);
        Assert.False(string.IsNullOrEmpty(poll.PollId));
        await seasonRepository.Received(1).SaveAsync(Arg.Any<Season>(), Arg.Any<CancellationToken>());
        await pollRepository.Received(1).SavePollAsync(poll, cancellationToken: Arg.Any<CancellationToken>());
    }

    // ─── UpdatePollAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePollAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var (service, _, _) = CreateService(poll: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdatePollAsync(PollId, "New Title", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdatePollAsync_PollClosed_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Closed));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePollAsync(PollId, "New Title", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdatePollAsync_OnlyDeadlineProvided_LeavesTitleUnchanged()
    {
        var poll = BuildPoll(PollStatus.Draft);
        var originalTitle = poll.Title;
        var (service, _, _) = CreateService(poll);
        var newDeadline = DateTime.UtcNow.AddDays(2);

        var result = await service.UpdatePollAsync(PollId, title: null, deadline: newDeadline, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(originalTitle, result.Title);
        Assert.Equal(newDeadline.ToUniversalTime(), result.Deadline);
    }

    // ─── RescheduleAsync ────────────────────────────────────────────────────

    private static GameDayPoll BuildDraftPollForReschedule(DateOnly gameDate) => new()
    {
        PollId = PollId,
        SeasonId = Season.DeriveSeasonId(gameDate),
        GameDate = gameDate,
        Title = "Belfast Giants vs Sheffield Steelers",
        Deadline = gameDate.ToDateTime(new TimeOnly(19, 0)),
        CreatedBy = "admin-user"
    };

    [Fact]
    public async Task RescheduleAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var (service, _, _) = CreateService(poll: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RescheduleAsync(PollId, new DateOnly(2026, 1, 20), DateTime.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RescheduleAsync_PollNotDraft_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Open));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RescheduleAsync(PollId, new DateOnly(2026, 1, 20), DateTime.UtcNow, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RescheduleAsync_NewDateInADifferentSeason_ThrowsInvalidOperationException_AndDoesNotSave()
    {
        var poll = BuildDraftPollForReschedule(new DateOnly(2026, 1, 15)); // "2025-26" season
        var (service, pollRepository, _) = CreateService(poll);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RescheduleAsync(PollId, new DateOnly(2026, 9, 1), DateTime.UtcNow, TestContext.Current.CancellationToken)); // "2026-27" season

        await pollRepository.DidNotReceive().SavePollAsync(
            Arg.Any<GameDayPoll>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescheduleAsync_SameGameDate_SavesWithNoPreviousGameDate()
    {
        var gameDate = new DateOnly(2026, 1, 15);
        var poll = BuildDraftPollForReschedule(gameDate);
        var (service, pollRepository, _) = CreateService(poll);
        var newDeadline = gameDate.ToDateTime(new TimeOnly(20, 0));

        await service.RescheduleAsync(PollId, gameDate, newDeadline, TestContext.Current.CancellationToken);

        // Same day: no season-collection key change, so no previousGameDate is needed to clean up.
        await pollRepository.Received(1).SavePollAsync(
            Arg.Is<GameDayPoll>(p => p.Deadline == newDeadline.ToUniversalTime()),
            previousGameDate: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RescheduleAsync_DifferentGameDateSameSeason_SavesWithThePreviousGameDate()
    {
        var originalDate = new DateOnly(2026, 1, 15);
        var newDate = new DateOnly(2026, 1, 20); // still "2025-26"
        var poll = BuildDraftPollForReschedule(originalDate);
        var (service, pollRepository, _) = CreateService(poll);

        var result = await service.RescheduleAsync(
            PollId, newDate, newDate.ToDateTime(new TimeOnly(19, 0)), TestContext.Current.CancellationToken);

        Assert.Equal(newDate, result.GameDate);
        // Different day: the stale SEASON# collection item at the old date must be cleaned up, or
        // the poll shows up twice in ListBySeasonAsync - once at each date.
        await pollRepository.Received(1).SavePollAsync(
            result, previousGameDate: originalDate, cancellationToken: Arg.Any<CancellationToken>());
    }

    // ─── PublishPollAsync / ClosePollAsync ──────────────────────────────────

    [Fact]
    public async Task PublishPollAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var (service, _, _) = CreateService(poll: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.PublishPollAsync(PollId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishPollAsync_AlreadyOpen_PropagatesInvalidOperationExceptionFromDomain()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Open));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishPollAsync(PollId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishPollAsync_Draft_OpensItForPicks()
    {
        var (service, pollRepository, _) = CreateService(BuildPoll(PollStatus.Draft));

        var result = await service.PublishPollAsync(PollId, TestContext.Current.CancellationToken);

        Assert.Equal(PollStatus.Open, result.Status);
        await pollRepository.Received(1).SavePollAsync(result, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishPollAsync_WithNoQuestions_ThrowsAndLeavesItInDraft()
    {
        var poll = BuildPoll(PollStatus.Draft);
        var (service, pollRepository, _) = CreateService(poll, questionCount: 0);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishPollAsync(PollId, TestContext.Current.CancellationToken));

        Assert.Equal(PollStatus.Draft, poll.Status);
        await pollRepository.DidNotReceive().SavePollAsync(Arg.Any<GameDayPoll>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClosePollAsync_Draft_PropagatesInvalidOperationExceptionFromDomain()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Draft));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClosePollAsync(PollId, TestContext.Current.CancellationToken));
    }

    // ─── AddQuestionAsync / DeleteQuestionAsync ─────────────────────────────

    [Fact]
    public async Task AddQuestionAsync_PollScored_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Scored));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddQuestionAsync(PollId, "Who wins?", 0, [new OptionDefinition("Home", 0), new OptionDefinition("Away", 1)], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddQuestionAsync_ValidPoll_LinksOptionsToNewQuestionId()
    {
        var (service, pollRepository, _) = CreateService(BuildPoll(PollStatus.Open));

        var question = await service.AddQuestionAsync(
            PollId, "Who wins?", 0, [new OptionDefinition("Home", 0), new OptionDefinition("Away", 1)], TestContext.Current.CancellationToken);

        await pollRepository.Received(1).SaveQuestionAsync(
            Arg.Is<Question>(q => q.QuestionId == question.QuestionId),
            Arg.Is<IReadOnlyList<Option>>(opts => opts.Count == 2 && opts.All(o => o.QuestionId == question.QuestionId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteQuestionAsync_PollClosed_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Closed));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteQuestionAsync(PollId, "question-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteQuestionAsync_ValidPoll_DelegatesToRepository()
    {
        var (service, pollRepository, _) = CreateService(BuildPoll(PollStatus.Draft));

        await service.DeleteQuestionAsync(PollId, "question-1", TestContext.Current.CancellationToken);

        await pollRepository.Received(1).DeleteQuestionAsync(PollId, "question-1", Arg.Any<CancellationToken>());
    }
}
