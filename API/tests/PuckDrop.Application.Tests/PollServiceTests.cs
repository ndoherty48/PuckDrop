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

    private static (PollService Service, IPollRepository PollRepository, ISeasonRepository SeasonRepository) CreateService(
        GameDayPoll? poll)
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.GetByIdAsync(PollId, Arg.Any<CancellationToken>()).Returns(poll);

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

        var poll = await service.CreatePollAsync("Title", gameDate, DateTime.UtcNow.AddDays(1), "admin-user");

        Assert.Equal(PollStatus.Draft, poll.Status);
        Assert.Equal(Season.DeriveSeasonId(gameDate), poll.SeasonId);
        Assert.False(string.IsNullOrEmpty(poll.PollId));
        await seasonRepository.Received(1).SaveAsync(Arg.Any<Season>(), Arg.Any<CancellationToken>());
        await pollRepository.Received(1).SavePollAsync(poll, Arg.Any<CancellationToken>());
    }

    // ─── UpdatePollAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePollAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var (service, _, _) = CreateService(poll: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdatePollAsync(PollId, "New Title", null));
    }

    [Fact]
    public async Task UpdatePollAsync_PollClosed_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Closed));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdatePollAsync(PollId, "New Title", null));
    }

    [Fact]
    public async Task UpdatePollAsync_OnlyDeadlineProvided_LeavesTitleUnchanged()
    {
        var poll = BuildPoll(PollStatus.Draft);
        var originalTitle = poll.Title;
        var (service, _, _) = CreateService(poll);
        var newDeadline = DateTime.UtcNow.AddDays(2);

        var result = await service.UpdatePollAsync(PollId, title: null, deadline: newDeadline);

        Assert.Equal(originalTitle, result.Title);
        Assert.Equal(newDeadline.ToUniversalTime(), result.Deadline);
    }

    // ─── PublishPollAsync / ClosePollAsync ──────────────────────────────────

    [Fact]
    public async Task PublishPollAsync_PollNotFound_ThrowsKeyNotFoundException()
    {
        var (service, _, _) = CreateService(poll: null);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.PublishPollAsync(PollId));
    }

    [Fact]
    public async Task PublishPollAsync_AlreadyOpen_PropagatesInvalidOperationExceptionFromDomain()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Open));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishPollAsync(PollId));
    }

    [Fact]
    public async Task ClosePollAsync_Draft_PropagatesInvalidOperationExceptionFromDomain()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Draft));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClosePollAsync(PollId));
    }

    // ─── AddQuestionAsync / DeleteQuestionAsync ─────────────────────────────

    [Fact]
    public async Task AddQuestionAsync_PollScored_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Scored));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddQuestionAsync(PollId, "Who wins?", 0, [new OptionDefinition("Home", 0), new OptionDefinition("Away", 1)]));
    }

    [Fact]
    public async Task AddQuestionAsync_ValidPoll_LinksOptionsToNewQuestionId()
    {
        var (service, pollRepository, _) = CreateService(BuildPoll(PollStatus.Open));

        var question = await service.AddQuestionAsync(
            PollId, "Who wins?", 0, [new OptionDefinition("Home", 0), new OptionDefinition("Away", 1)]);

        await pollRepository.Received(1).SaveQuestionAsync(
            Arg.Is<Question>(q => q.QuestionId == question.QuestionId),
            Arg.Is<IReadOnlyList<Option>>(opts => opts.Count == 2 && opts.All(o => o.QuestionId == question.QuestionId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteQuestionAsync_PollClosed_ThrowsInvalidOperationException()
    {
        var (service, _, _) = CreateService(BuildPoll(PollStatus.Closed));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteQuestionAsync(PollId, "question-1"));
    }

    [Fact]
    public async Task DeleteQuestionAsync_ValidPoll_DelegatesToRepository()
    {
        var (service, pollRepository, _) = CreateService(BuildPoll(PollStatus.Draft));

        await service.DeleteQuestionAsync(PollId, "question-1");

        await pollRepository.Received(1).DeleteQuestionAsync(PollId, "question-1", Arg.Any<CancellationToken>());
    }
}
