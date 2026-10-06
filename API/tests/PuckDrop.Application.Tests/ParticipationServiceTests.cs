using NSubstitute;
using PuckDrop.Application.Repositories;
using PuckDrop.Application.Services;
using PuckDrop.Domain.Entities;
using Xunit;

namespace PuckDrop.Application.Tests;

public class ParticipationServiceTests
{
    private const string SeasonId = "2025-26";

    private static GameDayPoll BuildPoll(string pollId, bool published)
    {
        var poll = new GameDayPoll
        {
            PollId = pollId,
            SeasonId = SeasonId,
            GameDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Title = $"Poll {pollId}",
            Deadline = DateTime.UtcNow.AddDays(1),
            CreatedBy = "admin-user"
        };
        if (published)
            poll.Publish();
        return poll;
    }

    private static UserAnswer Answer(string userId, string pollId, string questionId = "q-1") => new()
    {
        UserId = userId,
        DisplayName = userId,
        QuestionId = questionId,
        PollId = pollId,
        SelectedOptionId = "o-1"
    };

    private static (ParticipationService Service, IUserAnswerRepository AnswerRepository) CreateService(
        IReadOnlyList<GameDayPoll> polls, Dictionary<string, IReadOnlyList<UserAnswer>> answersByPoll)
    {
        var pollRepository = Substitute.For<IPollRepository>();
        pollRepository.ListBySeasonAsync(SeasonId, Arg.Any<CancellationToken>()).Returns(polls);

        var answerRepository = Substitute.For<IUserAnswerRepository>();
        foreach (var (pollId, answers) in answersByPoll)
            answerRepository.GetAllAnswersForPollAsync(pollId, Arg.Any<CancellationToken>()).Returns(answers);

        return (new ParticipationService(pollRepository, answerRepository), answerRepository);
    }

    [Fact]
    public async Task GetSeasonParticipationAsync_CountsEachPlayerOncePerPoll_EvenAcrossQuestions()
    {
        var (service, _) = CreateService(
            [BuildPoll("poll-1", published: true)],
            new() { ["poll-1"] = [Answer("alice", "poll-1", "q-1"), Answer("alice", "poll-1", "q-2"), Answer("bob", "poll-1")] });

        var result = await service.GetSeasonParticipationAsync(SeasonId, TestContext.Current.CancellationToken);

        Assert.Equal(2, Assert.Single(result.Polls).PickedCount);
    }

    [Fact]
    public async Task GetSeasonParticipationAsync_PlayerCountIsDistinctPickersAcrossTheSeason()
    {
        var (service, _) = CreateService(
            [BuildPoll("poll-1", published: true), BuildPoll("poll-2", published: true)],
            new()
            {
                ["poll-1"] = [Answer("alice", "poll-1"), Answer("bob", "poll-1")],
                ["poll-2"] = [Answer("bob", "poll-2"), Answer("carol", "poll-2")]
            });

        var result = await service.GetSeasonParticipationAsync(SeasonId, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.PlayerCount);
        Assert.Equal([("poll-1", 2), ("poll-2", 2)], result.Polls.Select(p => (p.PollId, p.PickedCount)));
    }

    [Fact]
    public async Task GetSeasonParticipationAsync_PublishedPollWithNoPicks_ReportsZero()
    {
        var (service, _) = CreateService([BuildPoll("poll-1", published: true)], new() { ["poll-1"] = [] });

        var result = await service.GetSeasonParticipationAsync(SeasonId, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.PlayerCount);
        Assert.Equal(0, Assert.Single(result.Polls).PickedCount);
    }

    [Fact]
    public async Task GetSeasonParticipationAsync_SkipsDrafts_WithoutQueryingTheirAnswers()
    {
        var (service, answerRepository) = CreateService(
            [BuildPoll("draft", published: false), BuildPoll("poll-1", published: true)],
            new() { ["poll-1"] = [Answer("alice", "poll-1")] });

        var result = await service.GetSeasonParticipationAsync(SeasonId, TestContext.Current.CancellationToken);

        Assert.Equal("poll-1", Assert.Single(result.Polls).PollId);
        await answerRepository.DidNotReceive().GetAllAnswersForPollAsync("draft", Arg.Any<CancellationToken>());
    }
}
